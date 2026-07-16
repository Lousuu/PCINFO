using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public enum HardwareRefreshReason
{
    Startup,
    ManualSettings,
    Tray,
    DeviceArrival,
    DeviceRemoval,
    DeviceNodesChanged,
    Diagnostic
}

public enum HardwareRefreshState
{
    Idle,
    Scanning,
    Completed,
    PartiallyFailed,
    Failed
}

public sealed class HardwareRefreshResult
{
    public HardwareRefreshReason Reason { get; init; }
    public HardwareRefreshState State { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public HardwareSnapshot? Snapshot { get; init; }
    public IReadOnlyList<string> FailedProviders { get; init; } = [];
    public string? ErrorMessage { get; init; }
    public bool Succeeded => State is HardwareRefreshState.Completed or HardwareRefreshState.PartiallyFailed;
}

public sealed class HardwareRefreshStatusChangedEventArgs : EventArgs
{
    public required HardwareRefreshState State { get; init; }
    public required HardwareRefreshReason Reason { get; init; }
    public HardwareRefreshResult? Result { get; init; }
}

public interface IHardwareRefreshService
{
    event EventHandler<HardwareRefreshStatusChangedEventArgs>? StatusChanged;
    event EventHandler<HardwareSnapshot>? SnapshotRefreshed;
    bool IsRefreshing { get; }
    HardwareRefreshResult? LastResult { get; }
    Task<HardwareRefreshResult> RefreshAsync(
        HardwareRefreshReason reason,
        CancellationToken cancellationToken = default);
}

public sealed class HardwareRefreshService : IHardwareRefreshService
{
    private readonly object syncRoot = new();
    private readonly IHardwareInfoService hardwareInfoService;
    private readonly SensorAggregatorService sensorAggregator;
    private readonly PollingService pollingService;
    private Task<HardwareRefreshResult>? activeRefresh;
    private bool pendingAutomaticRefresh;
    private HardwareRefreshReason pendingReason;

    public HardwareRefreshService(
        IHardwareInfoService hardwareInfoService,
        SensorAggregatorService sensorAggregator,
        PollingService pollingService)
    {
        this.hardwareInfoService = hardwareInfoService;
        this.sensorAggregator = sensorAggregator;
        this.pollingService = pollingService;
    }

    public event EventHandler<HardwareRefreshStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<HardwareSnapshot>? SnapshotRefreshed;

    public bool IsRefreshing
    {
        get { lock (syncRoot) return activeRefresh is { IsCompleted: false }; }
    }

    public HardwareRefreshResult? LastResult { get; private set; }

    public Task<HardwareRefreshResult> RefreshAsync(
        HardwareRefreshReason reason,
        CancellationToken cancellationToken = default)
    {
        Task<HardwareRefreshResult> task;
        lock (syncRoot)
        {
            if (activeRefresh is { IsCompleted: false })
            {
                RuntimePerformanceDiagnostics.RecordHardwareRefreshRequest(coalesced: true);
                if (IsAutomatic(reason))
                {
                    pendingAutomaticRefresh = true;
                    pendingReason = reason;
                }
                task = activeRefresh;
            }
            else
            {
                RuntimePerformanceDiagnostics.RecordHardwareRefreshRequest(coalesced: false);
                activeRefresh = task = RefreshLoopAsync(reason);
            }
        }

        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private async Task<HardwareRefreshResult> RefreshLoopAsync(HardwareRefreshReason reason)
    {
        HardwareRefreshResult result = await RefreshOnceAsync(reason).ConfigureAwait(false);
        bool runFollowUp;
        HardwareRefreshReason followUpReason;
        lock (syncRoot)
        {
            runFollowUp = pendingAutomaticRefresh;
            followUpReason = pendingReason;
            pendingAutomaticRefresh = false;
        }

        if (runFollowUp)
        {
            result = await RefreshOnceAsync(followUpReason).ConfigureAwait(false);
        }

        lock (syncRoot)
        {
            // A refresh triggered while the single allowed follow-up was running is
            // intentionally absorbed; a later independent notification starts a new cycle.
            pendingAutomaticRefresh = false;
            LastResult = result;
            activeRefresh = null;
        }
        return result;
    }

    private async Task<HardwareRefreshResult> RefreshOnceAsync(HardwareRefreshReason reason)
    {
        DateTimeOffset startedAt = DateTimeOffset.Now;
        RaiseStatus(HardwareRefreshState.Scanning, reason, null);
        List<string> failedProviders = [];
        HardwareSnapshot? snapshot = null;
        string? error = null;
        try
        {
            hardwareInfoService.InvalidateCaches();
            SensorProviderRefreshResult providerResult = await sensorAggregator
                .RefreshDevicesAsync(CancellationToken.None)
                .ConfigureAwait(false);
            failedProviders.AddRange(providerResult.FailedProviders);
            snapshot = await hardwareInfoService.GetHardwareSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await pollingService.PollNowAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedProviders.Add("Immediate polling");
                AppLogger.LogError("Immediate poll after hardware refresh failed.", exception,
                    $"hardware-refresh-poll:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            error = exception.Message;
            AppLogger.LogError("Hardware refresh failed; the previous snapshot remains active.", exception,
                $"hardware-refresh:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
        }

        HardwareRefreshState state = snapshot is null
            ? HardwareRefreshState.Failed
            : failedProviders.Count == 0
                ? HardwareRefreshState.Completed
                : HardwareRefreshState.PartiallyFailed;
        HardwareRefreshResult result = new()
        {
            Reason = reason,
            State = state,
            CompletedAt = DateTimeOffset.Now,
            Duration = DateTimeOffset.Now - startedAt,
            Snapshot = snapshot,
            FailedProviders = failedProviders,
            ErrorMessage = error
        };
        if (snapshot is not null)
        {
            InvokeSnapshotRefreshed(snapshot);
        }
        LastResult = result;
        RuntimePerformanceDiagnostics.RecordHardwareRefresh(result.Duration, result.Succeeded, failedProviders.Count);
        RaiseStatus(state, reason, result);
        return result;
    }

    private void RaiseStatus(HardwareRefreshState state, HardwareRefreshReason reason, HardwareRefreshResult? result)
    {
        HardwareRefreshStatusChangedEventArgs args = new()
        {
            State = state,
            Reason = reason,
            Result = result
        };
        EventHandler<HardwareRefreshStatusChangedEventArgs>? handlers = StatusChanged;
        if (handlers is null) return;
        foreach (EventHandler<HardwareRefreshStatusChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); }
            catch (Exception exception)
            {
                AppLogger.LogError("Hardware refresh status subscriber failed.", exception,
                    $"hardware-refresh-status-subscriber:{handler.Method.Name}", TimeSpan.FromMinutes(5));
            }
        }
    }

    private void InvokeSnapshotRefreshed(HardwareSnapshot snapshot)
    {
        EventHandler<HardwareSnapshot>? handlers = SnapshotRefreshed;
        if (handlers is null) return;
        foreach (EventHandler<HardwareSnapshot> handler in handlers.GetInvocationList())
        {
            try { handler(this, snapshot); }
            catch (Exception exception)
            {
                AppLogger.LogError("Hardware snapshot subscriber failed.", exception,
                    $"hardware-refresh-snapshot-subscriber:{handler.Method.Name}", TimeSpan.FromMinutes(5));
            }
        }
    }

    private static bool IsAutomatic(HardwareRefreshReason reason) => reason is
        HardwareRefreshReason.DeviceArrival or
        HardwareRefreshReason.DeviceRemoval or
        HardwareRefreshReason.DeviceNodesChanged;
}

public sealed class HardwareChangeMonitor : IDisposable
{
    public const int WmDeviceChange = 0x0219;
    public const int DbtDeviceArrival = 0x8000;
    public const int DbtDeviceRemoveComplete = 0x8004;
    public const int DbtDeviceNodesChanged = 0x0007;
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(5);

    private readonly object syncRoot = new();
    private readonly IHardwareRefreshService refreshService;
    private readonly Func<bool> isEnabled;
    private readonly TimeSpan debounce;
    private readonly TimeSpan cooldown;
    private CancellationTokenSource? pending;
    private DateTimeOffset lastRefreshAt;
    private bool isDisposed;

    public HardwareChangeMonitor(
        IHardwareRefreshService refreshService,
        Func<bool> isEnabled,
        TimeSpan? debounce = null,
        TimeSpan? cooldown = null)
    {
        this.refreshService = refreshService;
        this.isEnabled = isEnabled;
        this.debounce = debounce ?? DefaultDebounce;
        this.cooldown = cooldown ?? DefaultCooldown;
    }

    public DateTimeOffset? LastDeviceChangeAt { get; private set; }

    public bool NotifyDeviceChange(int eventCode)
    {
        HardwareRefreshReason? reason = eventCode switch
        {
            DbtDeviceArrival => HardwareRefreshReason.DeviceArrival,
            DbtDeviceRemoveComplete => HardwareRefreshReason.DeviceRemoval,
            DbtDeviceNodesChanged => HardwareRefreshReason.DeviceNodesChanged,
            _ => null
        };
        if (!reason.HasValue || isDisposed || !isEnabled())
        {
            return false;
        }

        LastDeviceChangeAt = DateTimeOffset.Now;
        RuntimePerformanceDiagnostics.RecordDeviceChangeNotification();
        CancellationTokenSource next = new();
        CancellationTokenSource? previous;
        lock (syncRoot)
        {
            if (isDisposed)
            {
                next.Dispose();
                return false;
            }
            previous = pending;
            pending = next;
        }
        previous?.Cancel();
        _ = DebounceAsync(reason.Value, next);
        return true;
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (syncRoot)
        {
            if (isDisposed) return;
            isDisposed = true;
            cancellation = pending;
            pending = null;
        }
        cancellation?.Cancel();
    }

    private async Task DebounceAsync(HardwareRefreshReason reason, CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(debounce, owner.Token).ConfigureAwait(false);
            TimeSpan remaining = cooldown - (DateTimeOffset.Now - lastRefreshAt);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, owner.Token).ConfigureAwait(false);
            }
            await refreshService.RefreshAsync(reason, owner.Token).ConfigureAwait(false);
            owner.Token.ThrowIfCancellationRequested();
            lastRefreshAt = DateTimeOffset.Now;
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
        }
        finally
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(pending, owner)) pending = null;
            }
            owner.Dispose();
        }
    }
}
