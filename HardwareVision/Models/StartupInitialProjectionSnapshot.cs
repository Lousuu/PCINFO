namespace HardwareVision.Models;

public enum StartupProjectionState
{
    Pending,
    Value,
    Unavailable,
    Unsupported,
    Failed,
    TimedOut
}

public sealed record StartupProjectionSlotSnapshot(
    HardwareOverviewKind Region,
    StartupProjectionState State,
    string Detail)
{
    public bool IsResolved => State != StartupProjectionState.Pending;
}

public sealed record StartupInitialProjectionSnapshot(
    long PollingVersion,
    IReadOnlyList<StartupProjectionSlotSnapshot> Slots,
    bool DispatcherApplied,
    bool PostDataLayoutObserved)
{
    public int ResolvedVisibleSlotCount => Slots.Count(slot => slot.IsResolved);

    public int TotalVisibleSlotCount => Slots.Count;

    public bool IsReady => DispatcherApplied
        && PostDataLayoutObserved
        && Slots.Count == 6
        && Slots.All(slot => slot.IsResolved);

    public static StartupInitialProjectionSnapshot Pending { get; } = new(
        0,
        Enum.GetValues<HardwareOverviewKind>()
            .Where(kind => kind is HardwareOverviewKind.Cpu
                or HardwareOverviewKind.Gpu
                or HardwareOverviewKind.Memory
                or HardwareOverviewKind.Disk
                or HardwareOverviewKind.Network
                or HardwareOverviewKind.System)
            .Select(kind => new StartupProjectionSlotSnapshot(kind, StartupProjectionState.Pending, "Awaiting initial projection"))
            .ToArray(),
        DispatcherApplied: false,
        PostDataLayoutObserved: false);
}

internal sealed class StartupDashboardReadinessTracker
{
    private readonly Dictionary<HardwareOverviewKind, StartupProjectionSlotSnapshot> slots =
        StartupInitialProjectionSnapshot.Pending.Slots.ToDictionary(slot => slot.Region);
    private bool changed;

    public IReadOnlyList<StartupProjectionSlotSnapshot> Slots => OrderedSlots();

    public bool IsReady => slots.Values.All(slot => slot.IsResolved);

    public bool TryComplete(
        HardwareOverviewKind region,
        StartupProjectionState state,
        string detail)
    {
        if (state == StartupProjectionState.Pending || state == StartupProjectionState.TimedOut)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Dashboard source completion must be terminal and cannot manufacture timeout.");
        }

        StartupProjectionSlotSnapshot current = slots[region];
        if (current.IsResolved)
        {
            return false;
        }

        slots[region] = new StartupProjectionSlotSnapshot(region, state, detail);
        changed = true;
        return true;
    }

    public bool TryCreateSnapshot(
        long pollingVersion,
        out StartupInitialProjectionSnapshot snapshot)
    {
        if (!changed)
        {
            snapshot = StartupInitialProjectionSnapshot.Pending;
            return false;
        }

        changed = false;
        snapshot = new StartupInitialProjectionSnapshot(
            Math.Max(1, pollingVersion),
            OrderedSlots(),
            DispatcherApplied: true,
            PostDataLayoutObserved: false);
        return true;
    }

    public static StartupProjectionState ResolveCompletedSourceState(
        IEnumerable<MetricAvailability> availabilities)
    {
        MetricAvailability[] values = availabilities.ToArray();
        return values.Any(value => value == MetricAvailability.Available)
            ? StartupProjectionState.Value
            : values.Any(value => value == MetricAvailability.Error)
                ? StartupProjectionState.Failed
                : values.Any(value => value == MetricAvailability.Unsupported)
                    ? StartupProjectionState.Unsupported
                    : StartupProjectionState.Unavailable;
    }

    private StartupProjectionSlotSnapshot[] OrderedSlots() =>
        StartupInitialProjectionSnapshot.Pending.Slots
            .Select(slot => slots[slot.Region])
            .ToArray();
}
