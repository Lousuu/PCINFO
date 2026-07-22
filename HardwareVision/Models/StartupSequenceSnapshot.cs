namespace HardwareVision.Models;

public sealed record StartupSequenceSnapshot(
    long Version,
    StartupSequencePhase Phase,
    bool IsActive,
    bool HasCompleted,
    DateTimeOffset? StartedAt,
    AppTheme CurrentTheme,
    MotionLevel MotionLevel,
    string LaunchKind,
    IReadOnlyList<StartupMilestoneSnapshot> Milestones,
    bool ShellReady,
    bool VisualReady,
    StartupInitialProjectionSnapshot InitialProjection,
    bool CanCommit,
    string? FailureMessage,
    string Announcement)
{
    public static StartupSequenceSnapshot Dormant(AppTheme theme, MotionLevel motionLevel) => new(
        0,
        StartupSequencePhase.Dormant,
        IsActive: false,
        HasCompleted: false,
        StartedAt: null,
        theme,
        motionLevel,
        "COLD START / LOCAL",
        Enum.GetValues<StartupMilestoneId>()
            .Select(StartupMilestoneSnapshot.Waiting)
            .ToArray(),
        ShellReady: false,
        VisualReady: false,
        StartupInitialProjectionSnapshot.Pending,
        CanCommit: false,
        FailureMessage: null,
        Announcement: string.Empty);
}

public sealed class StartupSequenceChangedEventArgs(
    StartupSequenceSnapshot previousSnapshot,
    StartupSequenceSnapshot currentSnapshot) : EventArgs
{
    public StartupSequenceSnapshot PreviousSnapshot { get; } = previousSnapshot;

    public StartupSequenceSnapshot CurrentSnapshot { get; } = currentSnapshot;
}
