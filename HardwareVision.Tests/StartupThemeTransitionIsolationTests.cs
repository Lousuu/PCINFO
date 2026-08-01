namespace HardwareVision.Tests;

internal static class StartupThemeTransitionIsolationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup isolation binds SystemRewire suppression", () => Contains("IsSuppressed=\"{Binding IsStartupSequenceActive}\"")),
        ("Startup isolation cancels page motion once while active", () => ContainsCode("if (!startupSequenceOwnsMotion)", "PageHost.CancelNavigationTransitionForStartup()")),
        ("Startup isolation prewarms rewire at Loaded priority", () => ContainsCode("DispatcherPriority.Loaded", "EnsureTemplateReady"))
    ];

    private static void Contains(string value) => TestSupport.True(
        TraceworkPilotSource.Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml").Contains(value, StringComparison.Ordinal), value);
    private static void ContainsCode(params string[] values)
    {
        string source = TraceworkPilotSource.Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml.cs");
        foreach (string value in values) TestSupport.True(source.Contains(value, StringComparison.Ordinal), value);
    }
}
