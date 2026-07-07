using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class PollingService : IDisposable, IAsyncDisposable
{
	private readonly ISensorService sensorService;

	private readonly object intervalLock = new object();

	private readonly SemaphoreSlim lifecycleLock = new SemaphoreSlim(1, 1);

	private TimeSpan foregroundInterval;

	private TimeSpan backgroundInterval;

	private CancellationTokenSource? pollingCancellation;

	private Task? pollingTask;

	private volatile bool isBackgroundMode;

	private int disposeStarted;

	private bool isDisposed;

	public IReadOnlyList<SensorReading> LatestReadings { get; private set; } = Array.Empty<SensorReading>();


	public event EventHandler<SensorReadingsUpdatedEventArgs>? ReadingsUpdated;

	public event EventHandler<Exception>? PollingFailed;

	public PollingService(ISensorService sensorService, AppSettings settings)
	{
		this.sensorService = sensorService;
		foregroundInterval = CreateForegroundInterval(settings.RefreshIntervalSeconds);
		backgroundInterval = CreateBackgroundInterval(settings.BackgroundRefreshIntervalSeconds);
	}

	public async Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ThrowIfDisposed();
		await lifecycleLock.WaitAsync(cancellationToken);
		try
		{
			Task task = pollingTask;
			if (task == null || task.IsCompleted)
			{
				pollingCancellation?.Dispose();
				pollingCancellation = new CancellationTokenSource();
				pollingTask = Task.Run(() => RunPollingAsync(pollingCancellation.Token), CancellationToken.None);
			}
		}
		finally
		{
			lifecycleLock.Release();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await lifecycleLock.WaitAsync(cancellationToken);
		CancellationTokenSource cancellationToStop;
		Task taskToStop;
		try
		{
			cancellationToStop = pollingCancellation;
			taskToStop = pollingTask;
			pollingCancellation = null;
			pollingTask = null;
			cancellationToStop?.Cancel();
		}
		finally
		{
			lifecycleLock.Release();
		}
		if (taskToStop != null)
		{
			try
			{
				await taskToStop.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToStop?.IsCancellationRequested ?? false)
			{
			}
		}
		cancellationToStop?.Dispose();
	}

	public async Task RestartAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await StopAsync(cancellationToken);
		await StartAsync(cancellationToken);
	}

	public void SetBackgroundMode(bool enabled)
	{
		isBackgroundMode = enabled;
	}

	public void UpdateIntervals(double foregroundSeconds, int backgroundSeconds)
	{
		lock (intervalLock)
		{
			foregroundInterval = CreateForegroundInterval(foregroundSeconds);
			backgroundInterval = CreateBackgroundInterval(backgroundSeconds);
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
		{
			isDisposed = true;
			CancellationTokenSource cancellationTokenSource = pollingCancellation;
			Task taskToObserve = pollingTask;
			pollingCancellation = null;
			pollingTask = null;
			cancellationTokenSource?.Cancel();
			_ = DisposeAfterPollingStopsAsync(taskToObserve, cancellationTokenSource);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
		{
			isDisposed = true;
			try
			{
				await StopAsync();
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				AppLogger.LogError(
					"Polling service async dispose failed.",
					ex,
					$"polling-dispose:{ex.GetType().FullName}",
					TimeSpan.FromMinutes(5));
			}
			lifecycleLock.Dispose();
		}
	}

	private async Task RunPollingAsync(CancellationToken cancellationToken)
	{
		try
		{
			await InitializeSensorServiceAsync(cancellationToken);
			await PollOnceAsync(cancellationToken);
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(GetCurrentInterval(), cancellationToken);
				await PollOnceAsync(cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
	}

	private async Task InitializeSensorServiceAsync(CancellationToken cancellationToken)
	{
		try
		{
			await sensorService.InitializeAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
	}

	private async Task PollOnceAsync(CancellationToken cancellationToken)
	{
		try
		{
			IReadOnlyList<SensorReading> readings = (LatestReadings = await sensorService.GetCurrentReadingsAsync(cancellationToken));
			this.ReadingsUpdated?.Invoke(this, new SensorReadingsUpdatedEventArgs(readings, DateTimeOffset.Now, isBackgroundMode));
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
	}

	private TimeSpan GetCurrentInterval()
	{
		lock (intervalLock)
		{
			return isBackgroundMode ? backgroundInterval : foregroundInterval;
		}
	}

	private void OnPollingFailed(Exception exception)
	{
		AppLogger.LogError("Sensor polling failed.", exception, "polling:" + exception.GetType().FullName);
		try
		{
			this.PollingFailed?.Invoke(this, exception);
		}
		catch
		{
		}
	}

	private static TimeSpan CreateForegroundInterval(double seconds)
	{
		return CreateInterval(seconds, 0.5d, 0.5d, 30d);
	}

	private static TimeSpan CreateBackgroundInterval(int seconds)
	{
		return CreateInterval(seconds, 10, 5, 120);
	}

	private static TimeSpan CreateInterval(double seconds, double fallbackSeconds, double minimumSeconds, double maximumSeconds)
	{
		double value = ((seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds)) ? seconds : fallbackSeconds);
		value = Math.Clamp(value, minimumSeconds, maximumSeconds);
		return TimeSpan.FromSeconds(value);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}

	private async Task DisposeAfterPollingStopsAsync(Task? taskToObserve, CancellationTokenSource? cancellationToDispose)
	{
		try
		{
			if (taskToObserve != null)
			{
				await taskToObserve.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
		finally
		{
			cancellationToDispose?.Dispose();
			lifecycleLock.Dispose();
		}
	}
}
