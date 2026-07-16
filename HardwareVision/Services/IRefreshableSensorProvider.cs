namespace HardwareVision.Services;

public interface IRefreshableSensorProvider
{
    Task RefreshDevicesAsync(CancellationToken cancellationToken = default);
}

public sealed class SensorProviderRefreshResult
{
    public IReadOnlyList<string> FailedProviders { get; init; } = [];

    public bool CompletedSuccessfully => FailedProviders.Count == 0;
}
