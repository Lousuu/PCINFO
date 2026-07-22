namespace HardwareVision.Services;

public interface IStartupSequenceClock
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
