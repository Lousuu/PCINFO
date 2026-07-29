namespace HardwareVision.Services;

internal static class StartupPollingCoordinator
{
    internal static async Task<FirstPollingCycleOutcome> WaitForFirstCycleThenRefreshAsync(
        PollingService pollingService,
        Func<FirstPollingCycleOutcome, Task> startRefreshAsync,
        CancellationToken cancellationToken = default)
    {
        FirstPollingCycleOutcome outcome = await pollingService
            .WaitForFirstCycleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (outcome != FirstPollingCycleOutcome.Cancelled)
        {
            await startRefreshAsync(outcome).ConfigureAwait(false);
        }

        return outcome;
    }
}
