using HardwareVision.Utilities;

namespace HardwareVision.Services;

[Flags]
public enum DashboardRefreshKind
{
    None = 0,
    Sensors = 1,
    Disk = 2,
    Network = 4,
    Hardware = 8,
    All = Sensors | Disk | Network | Hardware
}

public sealed class DashboardRefreshCoordinator : IDisposable
{
    private readonly TimeSpan coalesceDelay;
    private readonly Action<DashboardRefreshKind> applyRefresh;
    private readonly CancellationTokenSource cancellation = new();
    private int pendingKinds;
    private int runnerActive;
    private bool isDisposed;

    public DashboardRefreshCoordinator(
        Action<DashboardRefreshKind> applyRefresh,
        TimeSpan? coalesceDelay = null)
    {
        this.applyRefresh = applyRefresh;
        this.coalesceDelay = coalesceDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public void Request(DashboardRefreshKind kinds)
    {
        if (isDisposed || kinds == DashboardRefreshKind.None)
        {
            return;
        }

        Interlocked.Or(ref pendingKinds, (int)kinds);
        if (Interlocked.CompareExchange(ref runnerActive, 1, 0) == 0)
        {
            _ = RunAsync();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(coalesceDelay, cancellation.Token).ConfigureAwait(false);
                DashboardRefreshKind kinds = (DashboardRefreshKind)Interlocked.Exchange(ref pendingKinds, 0);
                if (kinds != DashboardRefreshKind.None)
                {
                    applyRefresh(kinds);
                }

                Interlocked.Exchange(ref runnerActive, 0);
                if (Volatile.Read(ref pendingKinds) == 0
                    || Interlocked.CompareExchange(ref runnerActive, 1, 0) != 0)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref runnerActive, 0);
            AppLogger.LogError(
                "Dashboard refresh coordinator failed.",
                exception,
                $"dashboard-refresh-coordinator:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }
}
