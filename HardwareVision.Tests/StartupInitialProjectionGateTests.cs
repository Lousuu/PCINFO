using HardwareVision.Models;

namespace HardwareVision.Tests;

internal static class StartupInitialProjectionGateTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup projection gate has exactly six visible regions", SixRegions),
        ("Startup projection gate rejects pending slot", PendingIsNotReady),
        ("Startup projection gate accepts explicit terminal states", TerminalStatesAreReady)
    ];

    private static void SixRegions() =>
        TestSupport.Equal(6, StartupInitialProjectionSnapshot.Pending.Slots.Count, "region count");

    private static void PendingIsNotReady() =>
        TestSupport.False(StartupInitialProjectionSnapshot.Pending.IsReady, "pending projection");

    private static void TerminalStatesAreReady()
    {
        StartupProjectionState[] states =
        [StartupProjectionState.Value, StartupProjectionState.Unavailable, StartupProjectionState.Unsupported,
         StartupProjectionState.Failed, StartupProjectionState.TimedOut, StartupProjectionState.Value];
        StartupInitialProjectionSnapshot snapshot = new(1,
            Enum.GetValues<HardwareOverviewKind>().Select((kind, index) =>
                new StartupProjectionSlotSnapshot(kind, states[index], states[index].ToString())).ToArray(), true, true);
        TestSupport.True(snapshot.IsReady, "terminal projection");
    }
}
