namespace HardwareVision.Models;

public sealed record NavigationTransitionPlan(
    TimeSpan RouteDuration,
    TimeSpan ExitDuration,
    TimeSpan CommitTime,
    TimeSpan EnterDuration,
    TimeSpan TotalDuration,
    TimeSpan PageExitDuration,
    double PageExitOpacity,
    double PageExitOffset,
    TimeSpan PrimaryExitDelay,
    TimeSpan PrimaryExitDuration,
    double PrimaryCommitOpacity,
    double PrimaryExitOffset,
    TimeSpan SecondaryExitDelay,
    TimeSpan SecondaryExitDuration,
    double SecondaryCommitOpacity,
    double SecondaryExitOffset,
    TimeSpan PageEnterDuration,
    double PageStartOpacity,
    double PageSettleOffset,
    TimeSpan PrimaryEnterDelay,
    TimeSpan PrimaryEnterDuration,
    double PrimaryModuleStartOpacity,
    double PrimaryModuleOffset,
    TimeSpan SecondaryEnterDelay,
    TimeSpan SecondaryEnterDuration,
    double SecondaryModuleStartOpacity,
    double SecondaryModuleOffset,
    bool UsesClock,
    bool ShowsRelayBand,
    bool AllowsRelayTranslation,
    bool ShowsSignalRailCursor,
    bool AllowsTelemetryTranslation,
    bool AllowsPageTranslation,
    bool AllowsRoleStagger,
    MotionLevel EffectiveLevel)
{
    public TimeSpan ShiftDuration => ExitDuration;
    public TimeSpan SettleDuration => EnterDuration;
    public TimeSpan FinalizeDuration =>
        TotalDuration - CommitTime - EnterDuration > TimeSpan.Zero
            ? TotalDuration - CommitTime - EnterDuration
            : TimeSpan.Zero;
    public TimeSpan PageRevealDuration => TimeSpan.Zero;
    public TimeSpan PrimaryModuleDelay => PrimaryEnterDelay;
    public TimeSpan SecondaryModuleDelay => SecondaryEnterDelay;
    public bool AllowsModuleStagger => AllowsRoleStagger;

    public static NavigationTransitionPlan Create(MotionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return profile.EffectiveLevel switch
        {
            MotionLevel.Full => new(
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(70),
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(100), 0.74d, 4d,
                TimeSpan.FromMilliseconds(12), TimeSpan.FromMilliseconds(78), 0.80d, 3d,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(70), 0.68d, 5d,
                TimeSpan.FromMilliseconds(120), 0.78d, 5d,
                TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100), 0.84d, 4d,
                TimeSpan.FromMilliseconds(34), TimeSpan.FromMilliseconds(106), 0.72d, 6d,
                true, true, true, true, true, true, true,
                MotionLevel.Full),
            MotionLevel.Standard => new(
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(35),
                TimeSpan.FromMilliseconds(45),
                TimeSpan.FromMilliseconds(95),
                TimeSpan.FromMilliseconds(150),
                TimeSpan.FromMilliseconds(70), 0.80d, 3d,
                TimeSpan.FromMilliseconds(8), TimeSpan.FromMilliseconds(54), 0.84d, 2d,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(48), 0.76d, 4d,
                TimeSpan.FromMilliseconds(95), 0.84d, 4d,
                TimeSpan.FromMilliseconds(8), TimeSpan.FromMilliseconds(78), 0.88d, 3d,
                TimeSpan.FromMilliseconds(24), TimeSpan.FromMilliseconds(82), 0.80d, 4d,
                true, true, true, true, true, true, true,
                MotionLevel.Standard),
            MotionLevel.Reduced => new(
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(90),
                TimeSpan.FromMilliseconds(90),
                TimeSpan.FromMilliseconds(45), 0.90d, 0d,
                TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
                TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
                TimeSpan.FromMilliseconds(90), 0.90d, 0d,
                TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
                TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
                true, true, false, false, false, false, false,
                MotionLevel.Reduced),
            _ => Immediate(MotionLevel.Off)
        };
    }

    public static NavigationTransitionPlan Immediate(MotionLevel effectiveLevel) => new(
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
        TimeSpan.Zero, 1d, 0d,
        TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
        TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
        TimeSpan.Zero, 1d, 0d,
        TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
        TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
        false, false, false, false, false, false, false,
        effectiveLevel);
}
