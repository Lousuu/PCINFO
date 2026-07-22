namespace HardwareVision.Services;

public sealed class SystemNavigationTransitionClock : INavigationTransitionClock
{
    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        duration <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(duration, cancellationToken);
}
