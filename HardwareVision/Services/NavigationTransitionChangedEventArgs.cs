using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class NavigationTransitionChangedEventArgs(
    NavigationTransitionSnapshot previousSnapshot,
    NavigationTransitionSnapshot currentSnapshot) : EventArgs
{
    public NavigationTransitionSnapshot PreviousSnapshot { get; } = previousSnapshot;

    public NavigationTransitionSnapshot CurrentSnapshot { get; } = currentSnapshot;
}
