using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class SensorAggregatorService : ISensorService, IDisposable, IAsyncDisposable
{
	private readonly IReadOnlyList<ISensorProvider> providers;

	private readonly SemaphoreSlim providerLock = new SemaphoreSlim(1, 1);

	private bool isInitialized;

	private bool isDisposed;

	public SensorAggregatorService(IEnumerable<ISensorProvider> providers)
	{
		this.providers = providers.OrderByDescending((ISensorProvider provider) => provider.Priority).ToArray();
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ThrowIfDisposed();
		await providerLock.WaitAsync(cancellationToken);
		try
		{
			if (isInitialized)
			{
				return;
			}
			foreach (ISensorProvider provider in providers)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					await provider.InitializeAsync(cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					AppLogger.LogError("Sensor provider initialization failed: " + provider.Name + ".", ex, "sensor-provider-init:" + provider.Name + ":" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
				}
			}
			isInitialized = true;
		}
		finally
		{
			providerLock.Release();
		}
	}

	public async Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ThrowIfDisposed();
		await InitializeAsync(cancellationToken);
		await providerLock.WaitAsync(cancellationToken);
		try
		{
			List<(ISensorProvider Provider, SensorReading Reading)> collected = new List<(ISensorProvider, SensorReading)>();
			foreach (ISensorProvider provider in providers)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					collected.AddRange((await provider.GetReadingsAsync(cancellationToken)).Select((SensorReading reading) => (provider: provider, reading: reading)));
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					AppLogger.LogError("Sensor provider read failed: " + provider.Name + ".", ex, "sensor-provider-read:" + provider.Name + ":" + ex.GetType().FullName);
				}
			}
			return MergeReadings(collected);
		}
		finally
		{
			providerLock.Release();
		}
	}

	public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return GetCurrentReadingsAsync(cancellationToken);
	}

	public void Dispose()
	{
		if (isDisposed)
		{
			return;
		}
		isDisposed = true;
		foreach (ISensorProvider provider in providers)
		{
			if (provider is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}
		providerLock.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		if (isDisposed)
		{
			return;
		}
		isDisposed = true;
		foreach (ISensorProvider provider in providers)
		{
			if (provider is IAsyncDisposable asyncDisposable)
			{
				await asyncDisposable.DisposeAsync();
			}
			else if (provider is IDisposable disposable)
			{
				disposable.Dispose();
			}
		}
		providerLock.Dispose();
	}

	private static IReadOnlyList<SensorReading> MergeReadings(IEnumerable<(ISensorProvider Provider, SensorReading Reading)> collected)
	{
		List<(ISensorProvider, SensorReading)> list = (from item in collected
			orderby item.Provider.Priority descending, item.Reading.Category
			select item).ThenBy<(ISensorProvider, SensorReading), string>(((ISensorProvider Provider, SensorReading Reading) item) => item.Reading.DeviceName, StringComparer.OrdinalIgnoreCase).ThenBy<(ISensorProvider, SensorReading), SensorType>(((ISensorProvider Provider, SensorReading Reading) item) => item.Reading.Type).ThenBy<(ISensorProvider, SensorReading), string>(((ISensorProvider Provider, SensorReading Reading) item) => item.Reading.SensorName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		bool flag = list.Any<(ISensorProvider, SensorReading)>(((ISensorProvider Provider, SensorReading Reading) item) => item.Provider.Priority > 20 && item.Reading.Category == SensorCategory.Cpu && item.Reading.Type == SensorType.Clock && item.Reading.IsAvailable);
		Dictionary<string, SensorReading> dictionary = new Dictionary<string, SensorReading>(StringComparer.OrdinalIgnoreCase);
		foreach (var (sensorProvider, sensorReading) in list)
		{
			if (!(sensorProvider.Name == "WMI" && sensorReading.Category == SensorCategory.Cpu && sensorReading.Type == SensorType.Clock && flag))
			{
				string key = CreateKey(sensorReading);
				if (!dictionary.TryGetValue(key, out var value))
				{
					dictionary[key] = sensorReading;
				}
				else if (!value.IsAvailable && sensorReading.IsAvailable)
				{
					dictionary[key] = sensorReading;
				}
			}
		}
		return dictionary.Values.ToArray();
	}

	private static string CreateKey(SensorReading reading)
	{
		return string.Join("|", reading.Category, reading.DeviceName.Trim(), reading.Type, reading.SensorName.Trim());
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}
}
