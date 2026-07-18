using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IThemeService
{
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    AppTheme CurrentTheme { get; }

    IReadOnlyList<ThemeDescriptor> AvailableThemes { get; }

    bool ApplyTheme(AppTheme theme);
}
