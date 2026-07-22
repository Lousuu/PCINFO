namespace HardwareVision.Models;

public sealed record NavigationTransitionPlan(
    TimeSpan RouteDuration,
    TimeSpan ShiftDuration,
    TimeSpan SettleDuration,
    TimeSpan TotalDuration,
    TimeSpan CommitTime,
    TimeSpan PageRevealDuration,
    double PageStartOpacity,
    double PageSettleOffset,
    TimeSpan PrimaryModuleDelay,
    TimeSpan SecondaryModuleDelay,
    double PrimaryModuleStartOpacity,
    double SecondaryModuleStartOpacity,
    double PrimaryModuleOffset,
    double SecondaryModuleOffset,
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
                TimeSpan.FromMilliseconds(150),
                0.74d,
                10d,
                TimeSpan.FromMilliseconds(28),
                TimeSpan.FromMilliseconds(72),
                0.68d,
                0.58d,
                8d,
                12d,
                true, true, true, true, true, true, true,
                MotionLevel.Full),
            MotionLevel.Reduced => new(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(80),
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.Zero,
                0.86d,
                0d,
                TimeSpan.Zero,
                TimeSpan.Zero,
                1d,
                1d,
                0d,
                0d,
                true, true, false, false, false, false, false,
                MotionLevel.Reduced),
            MotionLevel.Off => Immediate(MotionLevel.Off),
            _ => new(
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromMilliseconds(170),
                TimeSpan.FromMilliseconds(260),
                TimeSpan.FromMilliseconds(90),
                TimeSpan.FromMilliseconds(118),
                0.80d,
                7d,
                TimeSpan.FromMilliseconds(18),
                TimeSpan.FromMilliseconds(48),
                0.78d,
                0.70d,
                6d,
                8d,
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
        TimeSpan.Zero,
        1d,
        0d,
        TimeSpan.Zero,
        TimeSpan.Zero,
        1d,
        1d,
        0d,
        0d,
        false, false, false, false, false, false, false,
        effectiveLevel);
}
