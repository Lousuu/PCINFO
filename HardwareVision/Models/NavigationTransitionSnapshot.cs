namespace HardwareVision.Models;

public sealed record NavigationTransitionSnapshot(
    long Version,
    bool IsActive,
    NavigationTransitionPhase Phase,
    NavigationTransitionDirection Direction,
    string OriginPage,
    string TargetPage,
    string OriginCode,
    string TargetCode,
    string OriginTitle,
    string TargetTitle,
    string OriginSubtitle,
    string TargetSubtitle,
    NavigationTransitionPlan Plan,
    bool HasCommitted)
{
    public static NavigationTransitionSnapshot Idle(
        NavigationRouteDescriptor? current = null,
        long version = 0L) => new(
            version,
            false,
            NavigationTransitionPhase.Idle,
            NavigationTransitionDirection.None,
            current?.PageKey ?? string.Empty,
            current?.PageKey ?? string.Empty,
            current?.Code ?? string.Empty,
            current?.Code ?? string.Empty,
            current?.Title ?? string.Empty,
            current?.Title ?? string.Empty,
            current?.Subtitle ?? string.Empty,
            current?.Subtitle ?? string.Empty,
            NavigationTransitionPlan.Immediate(MotionLevel.Off),
            true);
}
