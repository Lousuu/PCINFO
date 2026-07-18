using System.Windows;
using HardwareVision.Models;

namespace HardwareVision.Themes;

public static class ThemeContext
{
    public static readonly DependencyProperty CurrentThemeProperty =
        DependencyProperty.RegisterAttached(
            "CurrentTheme",
            typeof(AppTheme),
            typeof(ThemeContext),
            new FrameworkPropertyMetadata(
                AppTheme.Classic,
                FrameworkPropertyMetadataOptions.Inherits));

    public static AppTheme GetCurrentTheme(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (AppTheme)element.GetValue(CurrentThemeProperty);
    }

    public static void SetCurrentTheme(DependencyObject element, AppTheme value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CurrentThemeProperty, value);
    }
}
