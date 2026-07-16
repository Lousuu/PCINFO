namespace HardwareVision.Models;

public enum GameFrameSampleQuality
{
    Accepted,
    WarmupDiscarded,
    FirstSwapChainFrame,
    InvalidFrameTime,
    FrameTimeOutlier,
    DuplicateCaptureElapsed,
    RegressedCaptureElapsed,
    DuplicateExplicitTimestamp,
    RegressedExplicitTimestamp,
    MissingTimestampFallback,
    StableLevelTransitionCandidate,
    StableLevelTransitionConfirmed,
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

    public long FrameTimeOutlierSampleCount { get; init; }

    public long DuplicateCaptureElapsedSampleCount { get; init; }

    public long RegressedCaptureElapsedSampleCount { get; init; }

    public long DuplicateExplicitTimestampSampleCount { get; init; }

    public long RegressedExplicitTimestampSampleCount { get; init; }

    public long InvalidTimestampSampleCount => DuplicateCaptureElapsedSampleCount
        + RegressedCaptureElapsedSampleCount
        + DuplicateExplicitTimestampSampleCount
        + RegressedExplicitTimestampSampleCount;

    public long MissingTimestampSampleCount { get; init; }

    public long CompatibilityFallbackSampleCount { get; init; }

    public long StableLevelTransitionCandidateSampleCount { get; init; }

    public long StableLevelTransitionConfirmedCount { get; init; }

    public long SanitizedMetricFieldCount { get; init; }

    public long InvalidAuxiliaryMetricFieldCount { get; init; }

    public long AuxiliaryMetricOutlierFieldCount { get; init; }

    public long CpuBusySanitizedCount { get; init; }

    public long CpuWaitSanitizedCount { get; init; }

    public long GpuLatencySanitizedCount { get; init; }

    public long GpuTimeSanitizedCount { get; init; }

    public long GpuBusySanitizedCount { get; init; }

    public long GpuWaitSanitizedCount { get; init; }

    public long RenderLatencySanitizedCount { get; init; }

    public long DisplayLatencySanitizedCount { get; init; }

    public long DisplayedTimeSanitizedCount { get; init; }

    public long ClickToPhotonLatencySanitizedCount { get; init; }

    public string? PrimarySwapChainAddress { get; init; }

    public int SwapChainSwitchCount { get; init; }

    public double CaptureWarmupDurationSeconds { get; init; }

    public bool UsedCompatibilityFallback { get; init; }

    public bool PrimarySwapChainSelectionUncertain { get; init; }

    public double? RawMaximumFps { get; init; }

    public double? SustainedMaximumFps { get; init; }
}
