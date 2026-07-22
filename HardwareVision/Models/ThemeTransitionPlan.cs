namespace HardwareVision.Models;

public sealed record ThemeTransitionPlan(
    bool IsOverlayEnabled,
    bool UsesClock,
    bool BlocksInteraction,
    bool AllowsTraceTranslation,
    bool AllowsSegmentMotion,
    bool ShowsSystemRewireLabel,
    TimeSpan TraceDuration,
    TimeSpan LatchDuration,
    TimeSpan SpliceDuration,
    double BackdropTargetOpacity,
    double TraceOffset,
    MotionLevel EffectiveLevel)
{
    public TimeSpan TotalDuration => TraceDuration + LatchDuration + SpliceDuration;

    public static ThemeTransitionPlan Create(MotionProfile profile) => profile.EffectiveLevel switch
    {
        MotionLevel.Full => new(
            IsOverlayEnabled: true,
            UsesClock: true,
            BlocksInteraction: true,
            AllowsTraceTranslation: true,
            AllowsSegmentMotion: true,
            ShowsSystemRewireLabel: true,
            TraceDuration: TimeSpan.FromMilliseconds(260),
            LatchDuration: TimeSpan.FromMilliseconds(120),
            SpliceDuration: TimeSpan.FromMilliseconds(360),
            BackdropTargetOpacity: 0.86d,
            TraceOffset: 18d,
            EffectiveLevel: MotionLevel.Full),
        MotionLevel.Reduced => new(
            IsOverlayEnabled: true,
            UsesClock: true,
            BlocksInteraction: true,
            AllowsTraceTranslation: false,
            AllowsSegmentMotion: false,
            ShowsSystemRewireLabel: false,
            TraceDuration: TimeSpan.FromMilliseconds(60),
            LatchDuration: TimeSpan.FromMilliseconds(30),
            SpliceDuration: TimeSpan.FromMilliseconds(60),
            BackdropTargetOpacity: 0.52d,
            TraceOffset: 0d,
            EffectiveLevel: MotionLevel.Reduced),
        MotionLevel.Off => Immediate(MotionLevel.Off),
        _ => new(
            IsOverlayEnabled: true,
            UsesClock: true,
            BlocksInteraction: true,
            AllowsTraceTranslation: true,
            AllowsSegmentMotion: true,
            ShowsSystemRewireLabel: true,
            TraceDuration: TimeSpan.FromMilliseconds(190),
            LatchDuration: TimeSpan.FromMilliseconds(90),
            SpliceDuration: TimeSpan.FromMilliseconds(280),
            BackdropTargetOpacity: 0.78d,
            TraceOffset: 12d,
            EffectiveLevel: MotionLevel.Standard)
    };

    public static ThemeTransitionPlan Immediate(MotionLevel effectiveLevel) => new(
        IsOverlayEnabled: false,
        UsesClock: false,
        BlocksInteraction: false,
        AllowsTraceTranslation: false,
        AllowsSegmentMotion: false,
        ShowsSystemRewireLabel: false,
        TraceDuration: TimeSpan.Zero,
        LatchDuration: TimeSpan.Zero,
        SpliceDuration: TimeSpan.Zero,
        BackdropTargetOpacity: 0d,
        TraceOffset: 0d,
        EffectiveLevel: effectiveLevel);
}
