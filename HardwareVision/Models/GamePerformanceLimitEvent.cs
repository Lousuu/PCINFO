namespace HardwareVision.Models;

public enum PerformanceLimitProcessorType
{
    Cpu,
    Gpu
}

public sealed class GamePerformanceLimitEvent
{
    public long EventId { get; init; }

    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public PerformanceLimitProcessorType ProcessorType { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public string TimeText => StartedAt.ToLocalTime().ToString("HH:mm:ss");

    public string ProcessorText => ProcessorType == PerformanceLimitProcessorType.Cpu ? "CPU" : "GPU";

    public string DurationText
    {
        get
        {
            string value = Duration.TotalMinutes >= 1d
                ? $"{(int)Duration.TotalMinutes:00}:{Duration.Seconds:00}"
                : Duration.TotalSeconds < 1d
                    ? "<1 秒"
                    : $"{Duration.TotalSeconds:0.0} 秒";
            return IsActive ? $"进行中 · {value}" : value;
        }
    }

    public string ReasonCountText => $"{Reasons.Count} 个原因";
}

public sealed class GamePerformanceLimitSnapshot
{
    public static GamePerformanceLimitSnapshot Empty { get; } = new();

    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public bool IsTracking { get; init; }

    public IReadOnlyList<GamePerformanceLimitEvent> Events { get; init; } = [];

    public int ActiveEventCount => Events.Count(item => item.IsActive);
}
