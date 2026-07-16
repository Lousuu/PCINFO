namespace HardwareVision.Models;

public enum GameTimelineDeviceType
{
    Cpu,
    Gpu,
    Memory
}

public sealed class GameHardwareTimelineSample
{
    public Guid CaptureSessionId { get; init; }

    public int CaptureGeneration { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public double ElapsedSeconds { get; init; }

    public GameTimelineDeviceType DeviceType { get; init; }

    public string DeviceId { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public double? CpuAverageCoreClockMHz { get; init; }

    public double? CpuEffectiveClockMHz { get; init; }

    public double? CpuMaximumCoreClockMHz { get; init; }

    public double? CpuLoadPercent { get; init; }

    public double? CpuTemperatureCelsius { get; init; }

    public double? CpuPackagePowerWatts { get; init; }

    public bool? CpuLimitActive { get; init; }

    public int? CpuLimitReasonCount { get; init; }

    public IReadOnlyList<string> CpuLimitReasons { get; init; } = [];

    public PerformanceLimitSupportStatus? CpuLimitSupportStatus { get; init; }

    public double? GpuCoreClockMHz { get; init; }

    public double? GpuMemoryClockMHz { get; init; }

    public double? GpuLoadPercent { get; init; }

    public double? GpuTemperatureCelsius { get; init; }

    public double? GpuHotSpotTemperatureCelsius { get; init; }

    public double? GpuBoardPowerWatts { get; init; }

    public bool? GpuLimitActive { get; init; }

    public int? GpuLimitReasonCount { get; init; }

    public IReadOnlyList<string> GpuLimitReasons { get; init; } = [];

    public PerformanceLimitSupportStatus? GpuLimitSupportStatus { get; init; }

    public double? MemoryUsedBytes { get; init; }

    public double? MemoryLoadPercent { get; init; }
}

public sealed class GameHardwareTimelineResult
{
    public Guid CaptureSessionId { get; init; }

    public int CaptureGeneration { get; init; }

    public string? FilePath { get; init; }

    public long WrittenSampleCount { get; init; }

    public long DroppedSampleCount { get; init; }

    public bool CompletedSuccessfully { get; init; }
}
