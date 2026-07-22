namespace HardwareVision.Models;

public sealed record ThemeTransitionResult(
    ThemeTransitionStatus Status,
    AppTheme SourceTheme,
    AppTheme TargetTheme,
    bool WasThemeCommitted,
    string? FailureMessage = null)
{
    public bool ShouldPersist => Status == ThemeTransitionStatus.Applied && WasThemeCommitted;

    public static ThemeTransitionResult Applied(AppTheme sourceTheme, AppTheme targetTheme) =>
        new(ThemeTransitionStatus.Applied, sourceTheme, targetTheme, WasThemeCommitted: true);

    public static ThemeTransitionResult AlreadyCurrent(AppTheme theme) =>
        new(ThemeTransitionStatus.AlreadyCurrent, theme, theme, WasThemeCommitted: false);

    public static ThemeTransitionResult Failed(AppTheme sourceTheme, AppTheme targetTheme, string? message = null) =>
        new(ThemeTransitionStatus.Failed, sourceTheme, targetTheme, WasThemeCommitted: false, message);

    public static ThemeTransitionResult Superseded(AppTheme sourceTheme, AppTheme targetTheme, bool wasThemeCommitted) =>
        new(ThemeTransitionStatus.Superseded, sourceTheme, targetTheme, wasThemeCommitted);

    public static ThemeTransitionResult Cancelled(AppTheme sourceTheme, AppTheme targetTheme, bool wasThemeCommitted) =>
        new(ThemeTransitionStatus.Cancelled, sourceTheme, targetTheme, wasThemeCommitted);
}
