using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.Sensors;

public sealed class WindowsCpuPerformanceLimitSensorProvider : ISensorProvider, IGameSessionSensorProvider, IDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromSeconds(5);
    private readonly TimeProvider timeProvider;
    private readonly object syncRoot = new();
    private PerformanceCounter? flagsCounter;
    private PerformanceCounter? percentLimitCounter;
    private IReadOnlyList<SensorReading> cachedReadings = [];
    private DateTimeOffset cachedAt = DateTimeOffset.MinValue;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private bool hasSuccessfulRead;
    private volatile bool isSessionActive;
    private bool isDisposed;

    public WindowsCpuPerformanceLimitSensorProvider()
        : this(TimeProvider.System)
    {
    }

    internal WindowsCpuPerformanceLimitSensorProvider(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public string Name => "Windows CPU Performance Counters";

    public bool IsAvailable { get; private set; } = true;

    public int Priority => 30;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!isSessionActive)
        {
            return Task.FromResult<IReadOnlyList<SensorReading>>([]);
        }

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return GetReadingsLocked(cancellationToken);
        }
    }

    void IGameSessionSensorProvider.SetSessionActive(bool active)
    {
        lock (syncRoot)
        {
            if (isSessionActive == active)
            {
                return;
            }

            isSessionActive = active;
            cachedReadings = [];
            cachedAt = DateTimeOffset.MinValue;
            retryAfter = DateTimeOffset.MinValue;
        }
    }

    private Task<IReadOnlyList<SensorReading>> GetReadingsLocked(CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (now - cachedAt < CacheDuration)
        {
            return Task.FromResult(cachedReadings);
        }

        if (now < retryAfter)
        {
            return Task.FromResult(cachedReadings);
        }

        try
        {
            IReadOnlyList<SensorReading> readings = ReadPerformanceLimitFlags(cancellationToken);
            cachedReadings = readings;
            cachedAt = now;
            retryAfter = DateTimeOffset.MinValue;
            IsAvailable = true;
            hasSuccessfulRead = true;
            return Task.FromResult(cachedReadings);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            cachedReadings = [CreateStatusReading(
                isAvailable: false,
                hasSuccessfulRead ? SensorAvailability.Error : SensorAvailability.NotReported,
                now,
                exception.Message)];
            cachedAt = now;
            retryAfter = now + FailureRetryDelay;
            IsAvailable = false;
            AppLogger.LogError(
                "Windows CPU performance limit counters could not be read.",
                exception,
                $"windows-cpu-performance-limit:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            return Task.FromResult<IReadOnlyList<SensorReading>>([]);
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            flagsCounter?.Dispose();
            percentLimitCounter?.Dispose();
            flagsCounter = null;
            percentLimitCounter = null;
        }
    }

    internal static IReadOnlyList<SensorReading> CreateReadings(
        uint flags,
        uint? percentPerformanceLimit,
        DateTimeOffset timestamp)
    {
        if (flags == 0)
        {
            return [];
        }

        List<SensorReading> readings = [];
        uint remaining = flags;
        for (int bitIndex = 0; bitIndex < 32; bitIndex++)
        {
            uint bit = 1u << bitIndex;
            if ((remaining & bit) == 0)
            {
                continue;
            }

            string percentage = percentPerformanceLimit.HasValue
                ? $"; Windows reported {percentPerformanceLimit.Value}% performance limit"
                : string.Empty;
            readings.Add(new SensorReading
            {
                DeviceName = "Windows CPU",
                SensorName = $"CPU Performance Limit Flag 0x{bit:X8}",
                Category = SensorCategory.Cpu,
                Type = SensorType.State,
                Value = 1d,
                Unit = "state",
                Status = HardwareStatus.Warning,
                Timestamp = timestamp,
                IsAvailable = true,
                Source = "Windows Performance Counters",
                Availability = SensorAvailability.Available,
                RawIdentifier = $"Processor Information.PerformanceLimitFlags/0x{bit:X8}",
                LastUpdated = timestamp,
                ErrorMessage = $"Windows Performance Limit Flags=0x{flags:X8}{percentage}"
            });
            remaining &= ~bit;
            if (remaining == 0)
            {
                break;
            }
        }

        return readings;
    }

    private IReadOnlyList<SensorReading> ReadPerformanceLimitFlags(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        flagsCounter ??= new PerformanceCounter(
            "Processor Information",
            "Performance Limit Flags",
            "_Total",
            readOnly: true);
        percentLimitCounter ??= new PerformanceCounter(
            "Processor Information",
            "% Performance Limit",
            "_Total",
            readOnly: true);
        DateTimeOffset now = timeProvider.GetUtcNow();
        uint flags = unchecked((uint)flagsCounter.RawValue);
        long percentRaw = percentLimitCounter.RawValue;
        uint? percentage = percentRaw is >= 0 and <= uint.MaxValue ? (uint)percentRaw : null;
        IReadOnlyList<SensorReading> readings = CreateReadings(flags, percentage, now);
        return readings.Count == 0
            ? [CreateStatusReading(true, SensorAvailability.Available, now, null)]
            : readings;
    }

    private static SensorReading CreateStatusReading(
        bool isAvailable,
        SensorAvailability availability,
        DateTimeOffset timestamp,
        string? error)
    {
        return new SensorReading
        {
            DeviceName = "Windows CPU",
            SensorName = "CPU Performance Limit Status",
            Category = SensorCategory.Cpu,
            Type = SensorType.State,
            Value = isAvailable ? 0d : null,
            Unit = "state",
            Status = isAvailable ? HardwareStatus.Normal : HardwareStatus.Unknown,
            Timestamp = timestamp,
            IsAvailable = isAvailable,
            Source = "Windows Performance Counters",
            Availability = availability,
            RawIdentifier = "/performance-limit-status/cpu/windows",
            LastUpdated = timestamp,
            ErrorMessage = error
        };
    }
}
