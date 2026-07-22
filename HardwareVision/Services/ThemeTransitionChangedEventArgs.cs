using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class ThemeTransitionChangedEventArgs : EventArgs
{
    public ThemeTransitionChangedEventArgs(
        ThemeTransitionSnapshot previousSnapshot,
        ThemeTransitionSnapshot currentSnapshot)
    {
        PreviousSnapshot = previousSnapshot;
        CurrentSnapshot = currentSnapshot;
    }

    public ThemeTransitionSnapshot PreviousSnapshot { get; }

    public ThemeTransitionSnapshot CurrentSnapshot { get; }
}
