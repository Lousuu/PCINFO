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

    public bool CompletedNormally { get; init; }

    public GameSessionEndReason EndReason { get; init; }

    public string CsvFileName { get; init; } = string.Empty;

    public long CsvFileSize { get; init; }
}
