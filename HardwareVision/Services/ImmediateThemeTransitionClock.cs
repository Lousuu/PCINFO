namespace HardwareVision.Services;

public sealed class ImmediateThemeTransitionClock : IThemeTransitionClock
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
}
