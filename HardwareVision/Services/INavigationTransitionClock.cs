namespace HardwareVision.Services;

public interface INavigationTransitionClock
{
    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}
