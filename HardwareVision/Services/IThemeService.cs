using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }

    IReadOnlyList<ThemeDescriptor> AvailableThemes { get; }

    bool ApplyTheme(AppTheme theme);
}
