using HardwareVision.Models;

namespace HardwareVision.Services;

public interface INavigationTransitionService
{
    event EventHandler<NavigationTransitionChangedEventArgs>? TransitionChanged;

    NavigationTransitionSnapshot CurrentSnapshot { get; }

    bool IsTransitioning { get; }

    Task NavigateAsync(
        NavigationTransitionIntent intent,
        Func<CancellationToken, Task> commitAsync,
        CancellationToken cancellationToken = default);

    void Cancel();
}
