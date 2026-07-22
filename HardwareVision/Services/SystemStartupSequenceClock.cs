namespace HardwareVision.Services;

public sealed class SystemStartupSequenceClock : IStartupSequenceClock
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
}
