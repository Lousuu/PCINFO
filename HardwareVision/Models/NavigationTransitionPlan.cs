namespace HardwareVision.Models;

public sealed record NavigationTransitionPlan(
    TimeSpan RouteDuration,
    TimeSpan ShiftDuration,
    TimeSpan SettleDuration,
    TimeSpan TotalDuration,
    TimeSpan CommitTime,
    double PageStartOpacity,
    double PageSettleOffset,
    TimeSpan PrimaryModuleDelay,
    TimeSpan SecondaryModuleDelay,
    bool UsesClock,
    bool ShowsRelayBand,
    bool AllowsRelayTranslation,
    bool ShowsSignalRailCursor,
    bool AllowsTelemetryTranslation,
    bool AllowsPageTranslation,
    bool AllowsModuleStagger,
    MotionLevel EffectiveLevel)
{
    public static NavigationTransitionPlan Create(MotionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.EffectiveLevel switch
        {
            MotionLevel.Full => new(
                TimeSpan.FromMilliseconds(70),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(210),
                TimeSpan.FromMilliseconds(330),
                TimeSpan.FromMilliseconds(120),
                0.64d,
                8d,
                TimeSpan.FromMilliseconds(24),
                TimeSpan.FromMilliseconds(42),
                true, true, true, true, true, true, true,
                MotionLevel.Full),
            MotionLevel.Reduced => new(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(80),
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(40),
                0.84d,
                0d,
                TimeSpan.Zero,
                TimeSpan.Zero,
                true, true, false, false, false, false, false,
                MotionLevel.Reduced),
            MotionLevel.Off => Immediate(MotionLevel.Off),
            _ => new(
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(170),
                TimeSpan.FromMilliseconds(260),
                TimeSpan.FromMilliseconds(90),
                0.72d,
                5d,
                TimeSpan.FromMilliseconds(16),
                TimeSpan.FromMilliseconds(28),
                true, true, true, true, true, true, true,
                MotionLevel.Standard)
        };
    }

    public static NavigationTransitionPlan Immediate(MotionLevel effectiveLevel) => new(
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.Zero,
        1d,
        0d,
        TimeSpan.Zero,
        TimeSpan.Zero,
        false, false, false, false, false, false, false,
        effectiveLevel);
}
