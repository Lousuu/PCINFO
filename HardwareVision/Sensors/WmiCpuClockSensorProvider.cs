using System.Diagnostics;
using System.Globalization;
using System.Management;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.Sensors;

public sealed class WmiCpuClockSensorProvider : IConditionalSensorProvider, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim queryLock = new(1, 1);
    private readonly TimeProvider timeProvider;
    private readonly Func<CancellationToken, IReadOnlyList<SensorReading>>? queryOverride;
    private ManagementObjectSearcher? searcher;
    private IReadOnlyList<SensorReading> cachedReadings = Array.Empty<SensorReading>();
    private DateTimeOffset cachedAt = DateTimeOffset.MinValue;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private int consecutiveFailures;
    private bool isDisposed;

    public WmiCpuClockSensorProvider()
        : this(TimeProvider.System, queryOverride: null)
    {
    }

    internal WmiCpuClockSensorProvider(
        TimeProvider timeProvider,
        Func<CancellationToken, IReadOnlyList<SensorReading>>? queryOverride)
    {
        this.timeProvider = timeProvider;
        this.queryOverride = queryOverride;
    }

    public string Name => "WMI";

    public bool IsAvailable { get; private set; } = true;

    public int Priority => 20;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(isDisposed, this);
        IsAvailable = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
    {
        return GetReadingsAsync(Array.Empty<SensorReading>(), cancellationToken);
    }

    public async Task<IReadOnlyList<SensorReading>> GetReadingsAsync(
        IReadOnlyList<SensorReading> higherPriorityReadings,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (HasValidPrimaryCpuClock(higherPriorityReadings))
        {
            RuntimePerformanceDiagnostics.RecordWmiCpuClockCacheHit();
            return Array.Empty<SensorReading>();
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now - cachedAt < CacheDuration || now < retryAfter)
        {
            RuntimePerformanceDiagnostics.RecordWmiCpuClockCacheHit();
            return cachedReadings;
        }

        await queryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = timeProvider.GetUtcNow();
            if (now - cachedAt < CacheDuration || now < retryAfter)
            {
                RuntimePerformanceDiagnostics.RecordWmiCpuClockCacheHit();
                return cachedReadings;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                IReadOnlyList<SensorReading> readings = await Task.Run(
                    () => queryOverride?.Invoke(cancellationToken) ?? ReadCpuClockSpeed(cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                RuntimePerformanceDiagnostics.RecordWmiCpuClockQuery(stopwatch.Elapsed);
                cachedReadings = readings;
                cachedAt = now;
                retryAfter = DateTimeOffset.MinValue;
                consecutiveFailures = 0;
                IsAvailable = readings.Any(static reading => reading.IsAvailable);
                return cachedReadings;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                stopwatch.Stop();
                RuntimePerformanceDiagnostics.RecordWmiCpuClockQuery(stopwatch.Elapsed);
                RuntimePerformanceDiagnostics.RecordWmiCpuClockFailure();
                consecutiveFailures = Math.Min(consecutiveFailures + 1, 5);
                double delaySeconds = Math.Min(
                    CacheDuration.TotalSeconds * Math.Pow(2d, consecutiveFailures - 1),
                    MaximumRetryDelay.TotalSeconds);
                retryAfter = now + TimeSpan.FromSeconds(delaySeconds);
                IsAvailable = cachedReadings.Any(static reading => reading.IsAvailable);
                AppLogger.LogError(
                    "WMI CPU clock speed fallback failed.",
                    exception,
                    $"wmi-cpu-clock:{exception.GetType().FullName}",
                    TimeSpan.FromMinutes(10));
                return cachedReadings;
            }
        }
        finally
        {
            queryLock.Release();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        searcher?.Dispose();
        searcher = null;
        queryLock.Dispose();
    }

    private IReadOnlyList<SensorReading> ReadCpuClockSpeed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<SensorReading> readings = new();
        DateTimeOffset now = timeProvider.GetUtcNow();
        searcher ??= new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, CurrentClockSpeed FROM Win32_Processor");
        using ManagementObjectCollection objects = searcher.Get();
        int index = 0;
        foreach (ManagementBaseObject item in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double? value = GetWmiDouble(item, "CurrentClockSpeed");
            bool valid = value.HasValue && value.Value > 0d;
            readings.Add(new SensorReading
            {
                DeviceName = GetWmiString(item, "Name") ?? $"CPU {index + 1}",
                SensorName = "CurrentClockSpeed",
                Category = SensorCategory.Cpu,
                Type = SensorType.Clock,
                Value = valid ? value : null,
                Unit = "MHz",
                Status = valid ? HardwareStatus.Normal : HardwareStatus.NotReported,
                Timestamp = now,
                IsAvailable = valid,
                Source = Name,
                Availability = valid ? SensorAvailability.Available : SensorAvailability.NotReported,
                RawIdentifier = "Win32_Processor.CurrentClockSpeed",
                LastUpdated = now,
                ErrorMessage = valid ? null : "WMI CurrentClockSpeed 未返回有效值"
            });
            index++;
        }

        return readings;
    }

    private static bool HasValidPrimaryCpuClock(IReadOnlyList<SensorReading> readings)
    {
        for (int index = 0; index < readings.Count; index++)
        {
            SensorReading reading = readings[index];
            if (reading.Category == SensorCategory.Cpu
                && reading.Type == SensorType.Clock
                && reading.IsAvailable
                && reading.Value is > 0d
                && !IsCpuBusClockReading(reading))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCpuBusClockReading(SensorReading reading)
    {
        return ContainsBusClockTerm(reading.SensorName)
            || ContainsBusClockTerm(reading.DeviceName)
            || ContainsBusClockTerm(reading.RawIdentifier);
    }

    private static bool ContainsBusClockTerm(string? value)
    {
        return value?.Contains("Bus", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("BCLK", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("Base Clock", StringComparison.OrdinalIgnoreCase) == true
            || value?.Contains("Reference Clock", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static double? GetWmiDouble(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            object? value = obj.Properties[propertyName]?.Value;
            return value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or ManagementException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static string? GetWmiString(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            string? value = Convert.ToString(obj.Properties[propertyName]?.Value, CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or ManagementException or ArgumentException)
        {
            return null;
        }
    }
}
