namespace HardwareVision.Services;

public interface IThemeTransitionClock
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
