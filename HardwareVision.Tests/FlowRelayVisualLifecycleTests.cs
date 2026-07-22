namespace HardwareVision.Tests;

internal static class FlowRelayVisualLifecycleTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow visual lifecycle 01 shared snapshot rail", () => Has(RailCode, "NavigationTransition")),
        ("Flow visual lifecycle 02 shared snapshot telemetry", () => Has(SpineXaml, "Snapshot")),
        ("Flow visual lifecycle 03 shared snapshot band", () => Has(HostXaml, "NavigationTransition")),
        ("Flow visual lifecycle 04 unload cancellation", UnloadCancellation),
        ("Flow visual lifecycle 05 theme cancellation", ThemeCancellation),
        ("Flow visual lifecycle 06 shell cancellation fanout", CancellationFanout),
        ("Flow visual lifecycle 07 stale rail callback guarded", StaleRailGuarded),
        ("Flow visual lifecycle 08 telemetry versions", () => Has(TelemetryCode, "renderedVersion")),
        ("Flow visual lifecycle 09 band versions", () => Has(BandCode, "activeVersion")),
        ("Flow visual lifecycle 10 commit version locks", CommitVersions),
        ("Flow visual lifecycle 11 no timers", NoTimers),
        ("Flow visual lifecycle 12 no layout-property animation", NoLayoutPropertyAnimation),
        ("Flow visual lifecycle 13 no focus capture", NoFocusCapture),
        ("Flow visual lifecycle 14 no hardware dependency", NoHardwareDependency),
        ("Flow visual lifecycle 15 cleanup entry points", CleanupEntryPoints),
        ("Flow visual lifecycle 16 state machine untouched by visuals", StateMachineBoundary)
    ];

    private static string SignalXaml => Read("Views", "Shell", "TraceworkSignalRail.xaml");
    private static string SpineXaml => Read("Views", "Shell", "TraceworkTelemetrySpine.xaml");
    private static string HostXaml => Read("Views", "Shell", "MainShellHost.xaml");
    private static string ShellCode => Read("Views", "Shell", "MainShellHost.xaml.cs");
    private static string ChromeCode => Read("Views", "Shell", "TraceworkShellChrome.xaml.cs");
    private static string RailCode => Read("Views", "Shell", "TraceworkSignalRail.xaml.cs");
    private static string TelemetryCode => FlowRelayVisualSource.Read("HardwareVision", "Controls", "TelemetryTitleTransitionHost.cs");
    private static string BandCode => FlowRelayVisualSource.Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
    private static string MotionCode => FlowRelayVisualSource.Read("HardwareVision", "Controls", "MotionTransitionHost.cs");
    private static string Read(params string[] parts) => FlowRelayVisualSource.Read(["HardwareVision", .. parts]);
    private static void Has(string source, string value) => TestSupport.True(source.Contains(value, StringComparison.Ordinal), value);

    private static void UnloadCancellation() { Has(ShellCode, "OnUnloaded"); Has(ShellCode, "CancelFlowRelayVisuals"); }
    private static void ThemeCancellation() { Has(ShellCode, "nameof(MainViewModel.ThemeTransition)"); Has(ShellCode, "CancelFlowRelayVisuals"); }

    private static void CancellationFanout()
    {
        Has(ChromeCode, "SignalRail.CancelTransition()");
        Has(ChromeCode, "TelemetrySpine.CancelTransition()");
        Has(ShellCode, "RelayBandOverlay.CancelTransition()");
        Has(ShellCode, "PageHost.RestoreFinalState()");
    }

    private static void StaleRailGuarded() { Has(RailCode, "snapshot.Version != activeVersion"); Has(RailCode, "!snapshot.IsActive"); }
    private static void CommitVersions() { Has(TelemetryCode, "renderedCommitted"); Has(BandCode, "committedVersion"); }

    private static void NoTimers()
    {
        foreach (string source in new[] { RailCode, TelemetryCode, BandCode, MotionCode })
            TestSupport.False(source.Contains("DispatcherTimer", StringComparison.Ordinal), "visual timer");
    }

    private static void NoLayoutPropertyAnimation()
    {
        foreach (string source in new[] { RailCode, TelemetryCode, BandCode, MotionCode })
            foreach (string forbidden in new[] { "WidthProperty", "HeightProperty", "MarginProperty", "Canvas.LeftProperty", "Canvas.TopProperty" })
                TestSupport.False(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    private static void NoFocusCapture()
    {
        Has(SignalXaml, "Focusable=\"False\"");
        Has(SignalXaml, "IsHitTestVisible=\"False\"");
    }

    private static void NoHardwareDependency()
    {
        foreach (string source in new[] { RailCode, TelemetryCode, BandCode, MotionCode })
        {
            TestSupport.False(source.Contains("LibreHardware", StringComparison.Ordinal), "hardware library");
            TestSupport.False(source.Contains("HardwareMonitor", StringComparison.Ordinal), "hardware monitor");
        }
    }

    private static void CleanupEntryPoints()
    {
        Has(RailCode, "RestoreRouteVisuals");
        Has(TelemetryCode, "RestoreFinalState");
        Has(BandCode, "RestoreFinalState");
        Has(MotionCode, "RestoreFinalState");
    }

    private static void StateMachineBoundary()
    {
        string service = FlowRelayVisualSource.Read("HardwareVision", "Services", "NavigationTransitionService.cs");
        Has(service, "NavigationTransitionPhase.Route");
        Has(service, "NavigationTransitionPhase.Shift");
        Has(service, "NavigationTransitionPhase.Relay");
        Has(service, "NavigationTransitionPhase.Settle");
    }
}
