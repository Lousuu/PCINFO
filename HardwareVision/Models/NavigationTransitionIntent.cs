namespace HardwareVision.Models;

public sealed record NavigationTransitionIntent(
    NavigationRouteDescriptor Origin,
    NavigationRouteDescriptor Target,
    MotionProfile MotionProfile)
{
    public NavigationTransitionDirection Direction =>
        NavigationRouteDescriptor.ResolveDirection(Origin, Target);
}
