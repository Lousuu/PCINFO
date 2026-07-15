using System.Text.Json.Serialization;

namespace HardwareVision.Models;

public enum PerformanceLimitProcessorType
{
    Cpu,
    Gpu
}

public enum PerformanceLimitSupportStatus
{
    NotStarted,
    SupportedNormal,
    ActiveLimit,
    Unsupported,
    TemporarilyUnavailable
}

public sealed class GamePerformanceLimitEvent
{
    private int triggerCount;
    private bool wasMerged;

    public long EventId { get; init; }

    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public PerformanceLimitProcessorType ProcessorType { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public bool IsActive { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<string> RawReasonNames { get; init; } = [];

    public IReadOnlyList<string> Scopes { get; init; } = [];

    public IReadOnlyList<string> RawIdentifiers { get; init; } = [];

    public int TriggerCount
    {
        get => triggerCount;
        init
        {
            triggerCount = value;
            HasTriggerCount = true;
        }
    }

    public bool WasMerged
    {
        get => wasMerged;
        init
        {
            wasMerged = value;
            HasWasMerged = true;
        }
    }

    [JsonIgnore]
    public bool HasTriggerCount { get; private set; }

    [JsonIgnore]
    public bool HasWasMerged { get; private set; }

    [JsonIgnore]
    public DateTimeOffset EndedAt => StartedAt + Duration;

    [JsonIgnore]
    public string TimeText => StartedAt.ToLocalTime().ToString("HH:mm:ss");

    [JsonIgnore]
    public string ProcessorText => ProcessorType == PerformanceLimitProcessorType.Cpu ? "CPU" : "GPU";

    [JsonIgnore]
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

    [JsonIgnore]
    public string ReasonCountText => $"{Reasons.Count} 个原因";

    [JsonIgnore]
    public string ReasonText => Reasons.Count == 0 ? "--" : string.Join(" / ", Reasons);

    [JsonIgnore]
    public string RawReasonText => RawReasonNames.Count == 0 ? "--" : string.Join(" / ", RawReasonNames);

    [JsonIgnore]
    public string TriggerCountText => HasTriggerCount ? $"确认 {TriggerCount} 次" : "确认次数未记录";

    [JsonIgnore]
    public string MergeText => !HasWasMerged
        ? "合并状态未记录"
        : WasMerged
            ? "发生过合并"
            : "未合并";
}

public sealed class GamePerformanceLimitSnapshot
{
    public static GamePerformanceLimitSnapshot Empty { get; } = new();

    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public bool IsTracking { get; init; }

    public PerformanceLimitSupportStatus CpuSupportStatus { get; init; } = PerformanceLimitSupportStatus.NotStarted;

    public PerformanceLimitSupportStatus GpuSupportStatus { get; init; } = PerformanceLimitSupportStatus.NotStarted;

    public bool EventsTruncated { get; init; }

    public IReadOnlyList<GamePerformanceLimitEvent> Events { get; init; } = [];

    public int ActiveEventCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < Events.Count; index++)
            {
                if (Events[index].IsActive) count++;
            }

            return count;
        }
    }
}
