using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IThemeTransitionService
{
    event EventHandler<ThemeTransitionChangedEventArgs>? TransitionChanged;

    ThemeTransitionSnapshot Current { get; }

    bool IsTransitioning { get; }

    Task<ThemeTransitionResult> ApplyThemeAsync(AppTheme targetTheme, CancellationToken cancellationToken = default);

    void Cancel();
}
