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
