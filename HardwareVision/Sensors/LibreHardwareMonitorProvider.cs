using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;
using LibreHardwareMonitor.Hardware;

namespace HardwareVision.Sensors;

public sealed class LibreHardwareMonitorProvider : ISensorProvider, IDisposable, IAsyncDisposable
{
	private const string LibreHardwareMonitorSource = "LibreHardwareMonitor";

	private readonly SemaphoreSlim sensorLock = new SemaphoreSlim(1, 1);

	private Computer? computer;

	private bool isInitialized;

	private bool isDisposed;

	private int disposeStarted;

	public string Name => "LibreHardwareMonitor";

	public bool IsAvailable { get; private set; }

	public int Priority => 100;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		await sensorLock.WaitAsync(cancellationToken);
		try
		{
			await Task.Run(delegate
			{
				InitializeCore(cancellationToken);
			}, cancellationToken);
		}
		finally
		{
			sensorLock.Release();
		}
	}

	public async Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		await sensorLock.WaitAsync(cancellationToken);
		try
		{
			return await Task.Run(() => GetCurrentReadingsCore(cancellationToken), cancellationToken);
		}
		finally
		{
			sensorLock.Release();
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
		{
			isDisposed = true;
			DisposeCore();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
		{
			isDisposed = true;
			await DisposeCoreAsync();
		}
	}

	private void InitializeCore(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (isInitialized && this.computer != null)
		{
			return;
		}
		CloseComputer();
		Computer computer = new Computer
		{
			IsCpuEnabled = true,
			IsGpuEnabled = true,
			IsMemoryEnabled = true,
			IsMotherboardEnabled = true,
			IsControllerEnabled = true,
			IsStorageEnabled = true,
			IsNetworkEnabled = true,
			IsBatteryEnabled = true,
			IsPowerMonitorEnabled = true
		};
		try
		{
			computer.Open();
			UpdateVisitor.WarmUp(computer, cancellationToken);
			this.computer = computer;
			isInitialized = true;
			IsAvailable = true;
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError("LibreHardwareMonitor initialization failed. Static hardware information can still be displayed.", ex, "lhm-init:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			TryClose(computer);
			this.computer = null;
			isInitialized = false;
			IsAvailable = false;
		}
	}

	private IReadOnlyList<SensorReading> GetCurrentReadingsCore(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!isInitialized || computer == null)
		{
			InitializeCore(cancellationToken);
		}
		if (computer == null)
		{
			AppLogger.LogError("LibreHardwareMonitor is unavailable; sensor readings are empty.", null, "lhm-readings-unavailable", TimeSpan.FromMinutes(10.0));
			IsAvailable = false;
			return Array.Empty<SensorReading>();
		}
		List<SensorReading> list = new List<SensorReading>();
		DateTimeOffset now = DateTimeOffset.Now;
		try
		{
			UpdateVisitor.Update(computer, cancellationToken);
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError("LibreHardwareMonitor visitor update failed before reading sensors.", ex, "lhm-update-visitor-root:" + ex.GetType().FullName);
		}
		foreach (IHardware item in computer.Hardware)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CollectReadings(item, list, now, cancellationToken);
		}
		return list;
	}

	private static void CollectReadings(IHardware hardware, ICollection<SensorReading> readings, DateTimeOffset timestamp, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ISensor[] sensors = hardware.Sensors;
		foreach (ISensor sensor in sensors)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				SensorReading sensorReading = CreateReading(sensor, hardware, timestamp);
				if (sensorReading != null)
				{
					readings.Add(sensorReading);
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				AppLogger.LogError($"Sensor reading failed for {hardware.Name} / {sensor.Name}.", ex, $"lhm-sensor:{hardware.HardwareType}:{hardware.Name}:{sensor.Name}:{ex.GetType().FullName}");
			}
		}
		IHardware[] subHardware = hardware.SubHardware;
		foreach (IHardware hardware2 in subHardware)
		{
			CollectReadings(hardware2, readings, timestamp, cancellationToken);
		}
	}

	private static SensorReading? CreateReading(ISensor sensor, IHardware hardware, DateTimeOffset timestamp)
	{
		HardwareVision.Models.SensorType sensorType = MapSensorType(sensor.SensorType);
		if (sensorType == HardwareVision.Models.SensorType.Unknown)
		{
			return null;
		}
		double? value = ToNullableDouble(sensor.Value);
		bool hasValue = value.HasValue;
		SensorAvailability availability = ((!hasValue) ? SensorAvailability.NotReported : SensorAvailability.Available);
		SensorCategory sensorCategory = MapHardwareCategory(hardware.HardwareType);
		return new SensorReading
		{
			DeviceName = hardware.Name,
			SensorName = sensor.Name,
			Category = sensorCategory,
			Type = sensorType,
			Value = value,
			Unit = GetUnit(sensorType, hardware),
			Min = ToNullableDouble(sensor.Min),
			Max = ToNullableDouble(sensor.Max),
			Status = ((!hasValue) ? HardwareStatus.NotReported : HardwareStatus.Normal),
			Timestamp = timestamp,
			IsAvailable = hasValue,
			Source = "LibreHardwareMonitor",
			Availability = availability,
			RawIdentifier = sensor.Identifier.ToString(),
			LastUpdated = timestamp,
			ErrorMessage = ((!hasValue && sensorCategory == SensorCategory.Cpu && sensorType == HardwareVision.Models.SensorType.Temperature) ? "LibreHardwareMonitor 官方程序可读取，但当前集成库未返回该值。请检查库版本、驱动文件、权限和运行架构。" : null)
		};
	}

	private static HardwareVision.Models.SensorType MapSensorType(LibreHardwareMonitor.Hardware.SensorType sensorType)
	{
		string text = sensorType.ToString();
		if (1 == 0)
		{
		}
		HardwareVision.Models.SensorType result = text switch
		{
			"Temperature" => HardwareVision.Models.SensorType.Temperature, 
			"Load" => HardwareVision.Models.SensorType.Load, 
			"Clock" => HardwareVision.Models.SensorType.Clock, 
			"Power" => HardwareVision.Models.SensorType.Power, 
			"Fan" => HardwareVision.Models.SensorType.Fan, 
			"Voltage" => HardwareVision.Models.SensorType.Voltage, 
			"Data" => HardwareVision.Models.SensorType.Data, 
			"SmallData" => HardwareVision.Models.SensorType.Data,
			"Throughput" => HardwareVision.Models.SensorType.Throughput, 
			_ => HardwareVision.Models.SensorType.Unknown, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static SensorCategory MapHardwareCategory(HardwareType hardwareType)
	{
		string text = hardwareType.ToString();
		if (1 == 0)
		{
		}
		SensorCategory result;
		switch (text)
		{
		case "Cpu":
			result = SensorCategory.Cpu;
			break;
		case "GpuAmd":
		case "GpuIntel":
		case "GpuNvidia":
			result = SensorCategory.Gpu;
			break;
		case "Memory":
			result = SensorCategory.Memory;
			break;
		case "Storage":
			result = SensorCategory.Disk;
			break;
		case "Motherboard":
		case "Controller":
		case "SuperIO":
		case "EmbeddedController":
			result = SensorCategory.Motherboard;
			break;
		case "Network":
			result = SensorCategory.Network;
			break;
		case "Battery":
			result = SensorCategory.Battery;
			break;
		default:
			result = SensorCategory.Unknown;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static string GetUnit(HardwareVision.Models.SensorType sensorType, IHardware hardware)
	{
		if (1 == 0)
		{
		}
		string result = sensorType switch
		{
			HardwareVision.Models.SensorType.Temperature => "C", 
			HardwareVision.Models.SensorType.Load => "%", 
			HardwareVision.Models.SensorType.Clock => "MHz", 
			HardwareVision.Models.SensorType.Power => "W", 
			HardwareVision.Models.SensorType.Fan => "RPM", 
			HardwareVision.Models.SensorType.Voltage => "V", 
			HardwareVision.Models.SensorType.Data => GetDataUnit(hardware),
			HardwareVision.Models.SensorType.Throughput => "KB/s", 
			_ => string.Empty, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string GetDataUnit(IHardware hardware)
	{
		string hardwareType = hardware.HardwareType.ToString();
		return hardwareType.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) ? "MB" : "GB";
	}

	private static double? ToNullableDouble(float? value)
	{
		return value.HasValue && !float.IsNaN(value.Value) && !float.IsInfinity(value.Value)
			? value.Value
			: null;
	}

	private void DisposeCore()
	{
		try
		{
			sensorLock.Wait();
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		try
		{
			CloseComputer();
		}
		finally
		{
			sensorLock.Release();
			sensorLock.Dispose();
		}
	}

	private async Task DisposeCoreAsync()
	{
		try
		{
			await sensorLock.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		try
		{
			CloseComputer();
		}
		finally
		{
			sensorLock.Release();
			sensorLock.Dispose();
		}
	}

	private void CloseComputer()
	{
		if (computer != null)
		{
			TryClose(computer);
			computer = null;
		}
		isInitialized = false;
		IsAvailable = false;
	}

	private static void TryClose(Computer computer)
	{
		try
		{
			computer.Close();
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError("LibreHardwareMonitor computer close failed.", ex, "lhm-close:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}
}
