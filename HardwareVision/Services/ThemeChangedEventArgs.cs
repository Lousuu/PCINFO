using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class ThemeChangedEventArgs : EventArgs
{
    public ThemeChangedEventArgs(AppTheme previousTheme, AppTheme currentTheme)
    {
        PreviousTheme = previousTheme;
        CurrentTheme = currentTheme;
    }

    public AppTheme PreviousTheme { get; }

    public AppTheme CurrentTheme { get; }
}
