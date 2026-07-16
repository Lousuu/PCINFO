namespace HardwareVision.Models;

public enum GameFrameSampleQuality
{
    Accepted,
    WarmupDiscarded,
    FirstSwapChainFrame,
    InvalidFrameTime,
    InvalidTimestamp,
    NonMonotonicTimestamp,
    ImplausibleStartupOutlier,
    NonPrimarySwapChain,
    SanitizedMetricField,
    SchemaSuspect
}

public enum GameCaptureWarmupState
{
    WaitingForHeader,
    WaitingForStableSwapChain,
    WarmingUp,
    Stable,
    Resetting,
    Completed
}

public sealed class GameFrameQualityDiagnostics
{
    public long WarmupCandidateSampleCount { get; init; }

    public long WarmupDiscardedSampleCount { get; init; }

    public long NonPrimarySwapChainSampleCount { get; init; }

    public long InvalidFrameTimeSampleCount { get; init; }

    public long InvalidTimestampSampleCount { get; init; }

    public long SanitizedMetricFieldCount { get; init; }

    public string? PrimarySwapChainAddress { get; init; }

    public int SwapChainSwitchCount { get; init; }

    public double CaptureWarmupDurationSeconds { get; init; }

    public bool UsedCompatibilityFallback { get; init; }
}
