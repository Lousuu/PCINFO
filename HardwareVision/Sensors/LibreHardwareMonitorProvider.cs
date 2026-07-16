using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;
using LibreHardwareMonitor.Hardware;
using ModelSensorType = HardwareVision.Models.SensorType;

namespace HardwareVision.Sensors;

public sealed class LibreHardwareMonitorProvider : ISensorProvider, IRefreshableSensorProvider, IDisposable, IAsyncDisposable
{
    private const string LibreHardwareMonitorSource = "LibreHardwareMonitor";

    private sealed class CachedSensorMetadata
    {
        public required ISensor Sensor { get; init; }
        public required string DeviceName { get; init; }
        public required string SensorName { get; init; }
        public required SensorCategory Category { get; init; }
        public required ModelSensorType Type { get; init; }
        public required string Unit { get; init; }
        public required string RawIdentifier { get; init; }
    }

    private readonly SemaphoreSlim sensorLock = new(1, 1);
    private readonly List<CachedSensorMetadata> sensorMetadata = new();
    private Computer? computer;
    private bool isInitialized;
    private bool isDisposed;
    private int disposeStarted;

    public string Name => LibreHardwareMonitorSource;

    public bool IsAvailable { get; private set; }

    public int Priority => 100;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await sensorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() => InitializeCore(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sensorLock.Release();
        }
    }

    public async Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await sensorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => GetCurrentReadingsCore(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sensorLock.Release();
        }
    }

    public async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await sensorLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                CloseComputer();
                InitializeCore(cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
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
            await DisposeCoreAsync().ConfigureAwait(false);
        }
    }

    private void InitializeCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (isInitialized && computer is not null)
        {
            return;
        }

        CloseComputer();
        Computer nextComputer = new()
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
            nextComputer.Open();
            UpdateVisitor.WarmUp(nextComputer, cancellationToken);
            computer = nextComputer;
            RebuildSensorMetadata(nextComputer, cancellationToken);
            isInitialized = true;
            IsAvailable = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.LogError(
                "LibreHardwareMonitor initialization failed. Static hardware information can still be displayed.",
                exception,
                $"lhm-init:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            TryClose(nextComputer);
            computer = null;
            sensorMetadata.Clear();
            isInitialized = false;
            IsAvailable = false;
        }
    }

    private IReadOnlyList<SensorReading> GetCurrentReadingsCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isInitialized || computer is null)
        {
            InitializeCore(cancellationToken);
        }

        if (computer is null)
        {
            AppLogger.LogError(
                "LibreHardwareMonitor is unavailable; sensor readings are empty.",
                null,
                "lhm-readings-unavailable",
                TimeSpan.FromMinutes(10));
            IsAvailable = false;
            return Array.Empty<SensorReading>();
        }

        Stopwatch updateClock = Stopwatch.StartNew();
        try
        {
            UpdateVisitor.Update(computer, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.LogError(
                "LibreHardwareMonitor visitor update failed before reading sensors.",
                exception,
                $"lhm-update-visitor-root:{exception.GetType().FullName}");
        }
        finally
        {
            updateClock.Stop();
            RuntimePerformanceDiagnostics.RecordLibreHardwareMonitorUpdate(updateClock.Elapsed);
        }

        if (sensorMetadata.Count == 0)
        {
            RebuildSensorMetadata(computer, cancellationToken);
        }

        DateTimeOffset timestamp = DateTimeOffset.Now;
        List<SensorReading> readings = new(sensorMetadata.Count);
        for (int index = 0; index < sensorMetadata.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CachedSensorMetadata metadata = sensorMetadata[index];
            try
            {
                readings.Add(CreateReading(metadata, timestamp));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppLogger.LogError(
                    $"Sensor reading failed for {metadata.DeviceName} / {metadata.SensorName}.",
                    exception,
                    $"lhm-sensor:{metadata.Category}:{metadata.DeviceName}:{metadata.SensorName}:{exception.GetType().FullName}");
            }
        }

        return readings;
    }

    private void RebuildSensorMetadata(Computer source, CancellationToken cancellationToken)
    {
        sensorMetadata.Clear();
        foreach (IHardware hardware in source.Hardware)
        {
            CollectSensorMetadata(hardware, cancellationToken);
        }
    }

    private void CollectSensorMetadata(IHardware hardware, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SensorCategory category = MapHardwareCategory(hardware.HardwareType);
        foreach (ISensor sensor in hardware.Sensors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ModelSensorType type = MapSensorType(sensor.SensorType);
            if (type == ModelSensorType.Unknown)
            {
                continue;
            }

            sensorMetadata.Add(new CachedSensorMetadata
            {
                Sensor = sensor,
                DeviceName = hardware.Name,
                SensorName = sensor.Name,
                Category = category,
                Type = type,
                Unit = GetUnit(type, hardware.HardwareType),
                RawIdentifier = sensor.Identifier.ToString()
            });
        }

        foreach (IHardware child in hardware.SubHardware)
        {
            CollectSensorMetadata(child, cancellationToken);
        }
    }

    private static SensorReading CreateReading(CachedSensorMetadata metadata, DateTimeOffset timestamp)
    {
        double? value = ToNullableDouble(metadata.Sensor.Value);
        bool hasValue = value.HasValue;
        return new SensorReading
        {
            DeviceName = metadata.DeviceName,
            SensorName = metadata.SensorName,
            Category = metadata.Category,
            Type = metadata.Type,
            Value = value,
            Unit = metadata.Unit,
            Min = ToNullableDouble(metadata.Sensor.Min),
            Max = ToNullableDouble(metadata.Sensor.Max),
            Status = hasValue ? HardwareStatus.Normal : HardwareStatus.NotReported,
            Timestamp = timestamp,
            IsAvailable = hasValue,
            Source = LibreHardwareMonitorSource,
            Availability = hasValue ? SensorAvailability.Available : SensorAvailability.NotReported,
            RawIdentifier = metadata.RawIdentifier,
            LastUpdated = timestamp,
            ErrorMessage = !hasValue
                && metadata.Category == SensorCategory.Cpu
                && metadata.Type == ModelSensorType.Temperature
                    ? SensorRuntimeDiagnostics.OfficialReadableButIntegratedValueMissingMessage
                    : null
        };
    }

    private static ModelSensorType MapSensorType(LibreHardwareMonitor.Hardware.SensorType sensorType)
    {
        return sensorType switch
        {
            LibreHardwareMonitor.Hardware.SensorType.Temperature => ModelSensorType.Temperature,
            LibreHardwareMonitor.Hardware.SensorType.Load => ModelSensorType.Load,
            LibreHardwareMonitor.Hardware.SensorType.Clock => ModelSensorType.Clock,
            LibreHardwareMonitor.Hardware.SensorType.Power => ModelSensorType.Power,
            LibreHardwareMonitor.Hardware.SensorType.Fan => ModelSensorType.Fan,
            LibreHardwareMonitor.Hardware.SensorType.Voltage => ModelSensorType.Voltage,
            LibreHardwareMonitor.Hardware.SensorType.Data => ModelSensorType.Data,
            LibreHardwareMonitor.Hardware.SensorType.SmallData => ModelSensorType.Data,
            LibreHardwareMonitor.Hardware.SensorType.Throughput => ModelSensorType.Throughput,
            _ => ModelSensorType.Unknown
        };
    }

    private static SensorCategory MapHardwareCategory(HardwareType hardwareType)
    {
        return hardwareType switch
        {
            HardwareType.Cpu => SensorCategory.Cpu,
            HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => SensorCategory.Gpu,
            HardwareType.Memory => SensorCategory.Memory,
            HardwareType.Storage => SensorCategory.Disk,
            HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => SensorCategory.Motherboard,
            HardwareType.Network => SensorCategory.Network,
            HardwareType.Battery => SensorCategory.Battery,
            _ => SensorCategory.Unknown
        };
    }

    private static string GetUnit(ModelSensorType sensorType, HardwareType hardwareType)
    {
        return sensorType switch
        {
            ModelSensorType.Temperature => "C",
            ModelSensorType.Load => "%",
            ModelSensorType.Clock => "MHz",
            ModelSensorType.Power => "W",
            ModelSensorType.Fan => "RPM",
            ModelSensorType.Voltage => "V",
            ModelSensorType.Data => hardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia ? "MB" : "GB",
            ModelSensorType.Throughput => "KB/s",
            _ => string.Empty
        };
    }

    private static double? ToNullableDouble(float? value)
    {
        return value.HasValue && float.IsFinite(value.Value) ? value.Value : null;
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
            await sensorLock.WaitAsync().ConfigureAwait(false);
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
        if (computer is not null)
        {
            TryClose(computer);
            computer = null;
        }

        sensorMetadata.Clear();
        isInitialized = false;
        IsAvailable = false;
    }

    private static void TryClose(Computer source)
    {
        try
        {
            source.Close();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.LogError(
                "LibreHardwareMonitor computer close failed.",
                exception,
                $"lhm-close:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
