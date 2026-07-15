namespace HardwareVision.Models;

public sealed class GameSessionSummary
{
    public string HardwareVisionVersion { get; init; } = string.Empty;

    public string PresentMonVersion { get; init; } = string.Empty;

    public Guid CaptureSessionId { get; init; }

    public int CaptureGeneration { get; init; }

    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string? WindowTitle { get; init; }

    public string? ExecutablePath { get; init; }

    public DateTimeOffset CaptureStartedAt { get; init; }

    public DateTimeOffset CaptureEndedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public long ReceivedSampleCount { get; init; }

    public long WrittenSampleCount { get; init; }

    public long DroppedRecordSampleCount { get; init; }

    public double? AverageFps { get; init; }

    public double? OnePercentLowFps { get; init; }

    public double? ZeroPointOnePercentLowFps { get; init; }

    public double? AverageFrameTimeMs { get; init; }

    public double? AverageCpuBusyMs { get; init; }

    public double? AverageGpuTimeMs { get; init; }

    public double? AverageDisplayLatencyMs { get; init; }

    public double? EstimatedEnergyWh { get; init; }

    public double? CpuEstimatedEnergyWh { get; init; }

    public double? GpuEstimatedEnergyWh { get; init; }

    public double? AverageEstimatedPowerWatts { get; init; }

    public double? CpuAverageEstimatedPowerWatts { get; init; }

    public double? GpuAverageEstimatedPowerWatts { get; init; }

    public double? EnergyCoveragePercent { get; init; }

    public TimeSpan? EnergyValidIntegrationDuration { get; init; }

    public string? EnergyIncludedComponents { get; init; }

    public double? AverageCpuLoadPercent { get; init; }

    public double? AverageCpuTemperatureCelsius { get; init; }

    public double? AverageGpuLoadPercent { get; init; }

    public double? AverageGpuTemperatureCelsius { get; init; }

    public double? AverageMemoryLoadPercent { get; init; }

    public GameSessionHardwareMetadata? HardwareMetadata { get; init; }

    public int? CpuPerformanceLimitEventCount { get; init; }

    public int? GpuPerformanceLimitEventCount { get; init; }

    public double? CpuPerformanceLimitDurationSeconds { get; init; }

    public double? GpuPerformanceLimitDurationSeconds { get; init; }

    public IReadOnlyList<GamePerformanceLimitEvent>? PerformanceLimitEvents { get; init; }

    public bool? PerformanceLimitEventsTruncated { get; init; }

    public PerformanceLimitSupportStatus? CpuPerformanceLimitSupportStatus { get; init; }

    public PerformanceLimitSupportStatus? GpuPerformanceLimitSupportStatus { get; init; }

    public bool CompletedNormally { get; init; }

    public GameSessionEndReason EndReason { get; init; }

    public string CsvFileName { get; init; } = string.Empty;

    public string? PerformanceLimitCsvFileName { get; init; }

    public string? HardwareTimelineCsvFileName { get; init; }

    public long? TimelineWrittenSampleCount { get; init; }

    public long? TimelineDroppedSampleCount { get; init; }

    public long CsvFileSize { get; init; }
}
