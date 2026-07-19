namespace HardwareVision.Services;

public sealed class SystemThemeTransitionClock : IThemeTransitionClock
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
    }
}
