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
                TimeSpan.FromMilliseconds(70),
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(190),
                TimeSpan.FromMilliseconds(220),
                TimeSpan.FromMilliseconds(420),
                TimeSpan.FromMilliseconds(120), 0.32d, 6d,
                TimeSpan.FromMilliseconds(24), TimeSpan.FromMilliseconds(96), 0.42d, 5d,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(88), 0.24d, 8d,
                TimeSpan.FromMilliseconds(220), 0.32d, 8d,
                TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(180), 0.42d, 6d,
                TimeSpan.FromMilliseconds(66), TimeSpan.FromMilliseconds(190), 0.24d, 10d,
                true, true, true, true, true, true, true,
                MotionLevel.Full),
            MotionLevel.Standard => new(
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(90),
                TimeSpan.FromMilliseconds(140),
                TimeSpan.FromMilliseconds(160),
                TimeSpan.FromMilliseconds(320),
                TimeSpan.FromMilliseconds(90), 0.38d, 4d,
                TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(74), 0.46d, 4d,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(66), 0.30d, 6d,
                TimeSpan.FromMilliseconds(160), 0.38d, 6d,
                TimeSpan.FromMilliseconds(14), TimeSpan.FromMilliseconds(138), 0.46d, 4d,
                TimeSpan.FromMilliseconds(44), TimeSpan.FromMilliseconds(146), 0.30d, 7d,
                true, true, true, true, true, true, true,
                MotionLevel.Standard),
            MotionLevel.Reduced => new(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(50),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(150),
                TimeSpan.FromMilliseconds(50), 0.58d, 0d,
                TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
                TimeSpan.Zero, TimeSpan.Zero, 1d, 0d,
                TimeSpan.FromMilliseconds(100), 0.58d, 0d,
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
