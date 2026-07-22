namespace HardwareVision.Tests;

internal static class TraceworkBindingPreservationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Pilot preservation 01 Classic Dashboard content hash", () => AssertHash("340CE96DE59909F4FD19CB50A4E06F6B2453C47A37AFBD7C45A1EA8A32B6C4E4", "HardwareVision", "Views", "Dashboard", "ClassicDashboardLayout.xaml")),
        ("Pilot preservation 02 Classic CPU content hash", () => AssertHash("3E2A60B0B04EE6563C88E08BC7860514CF58A14026C55ADD4DFA5BDED914BCF8", "HardwareVision", "Views", "Cpu", "ClassicCpuLayout.xaml")),
        ("Pilot preservation 03 Dashboard dual templates remain", DashboardDualTemplatesRemain),
        ("Pilot preservation 04 CPU dual templates remain", CpuDualTemplatesRemain),
        ("Pilot preservation 05 one PageHost remains", OnePageHostRemains),
        ("Pilot preservation 06 Dashboard selectors retain accessible names", DashboardSelectorsAccessible),
        ("Pilot preservation 07 CPU selector retains accessible name", CpuSelectorAccessible),
        ("Pilot preservation 08 Tracework layouts have no timers", () => ForbiddenAbsent("DispatcherTimer")),
        ("Pilot preservation 09 Tracework layouts have no render loop", () => ForbiddenAbsent("CompositionTarget.Rendering")),
        ("Pilot preservation 10 Tracework layouts have no storyboard", () => ForbiddenAbsent("Storyboard")),
        ("Pilot preservation 11 Tracework layouts have no image assets", () => ForbiddenAbsent("<Image")),
        ("Pilot preservation 12 Tracework layouts have no local file paths", () => ForbiddenAbsent("file:")),
        ("Pilot preservation 13 Tracework layouts have no navigation calls", () => ForbiddenAbsent("Navigate")),
        ("Pilot preservation 14 Tracework layouts have no polling calls", () => ForbiddenAbsent("PollingService")),
        ("Pilot preservation 15 shared responsive control has no timer", SharedGridHasNoTimer)
    ];

    private static string PilotLayouts =>
        TraceworkPilotSource.Read("HardwareVision", "Views", "Dashboard", "TraceworkDashboardLayout.xaml") +
        TraceworkPilotSource.Read("HardwareVision", "Views", "Cpu", "TraceworkCpuLayout.xaml");

    private static void AssertHash(string expected, params string[] parts)
    {
        string source = File.ReadAllText(Path.Combine([TraceworkPilotSource.Root(), .. parts]));
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(normalized);
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        TestSupport.Equal(expected, actual, string.Join('/', parts));
    }

    private static void DashboardDualTemplatesRemain()
    {
        string view = TraceworkPilotSource.Read("HardwareVision", "Views", "DashboardView.xaml");
        TestSupport.True(view.Contains("ClassicDashboardTemplate", StringComparison.Ordinal) && view.Contains("TraceworkDashboardTemplate", StringComparison.Ordinal), "Dashboard dual templates");
    }

    private static void CpuDualTemplatesRemain()
    {
        string view = TraceworkPilotSource.Read("HardwareVision", "Views", "CpuView.xaml");
        TestSupport.True(view.Contains("ClassicCpuTemplate", StringComparison.Ordinal) && view.Contains("TraceworkCpuTemplate", StringComparison.Ordinal), "CPU dual templates");
    }

    private static void OnePageHostRemains()
    {
        string shell = TraceworkPilotSource.Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        TestSupport.Equal(1, TraceworkPilotSource.Count(shell, "x:Name=\"PageHost\""), "PageHost count");
        TestSupport.Equal(1, TraceworkPilotSource.Count(shell, "Content=\"{Binding CurrentPage}\""), "CurrentPage binding count");
    }

    private static void DashboardSelectorsAccessible()
    {
        string dashboard = TraceworkPilotSource.Read("HardwareVision", "Views", "Dashboard", "TraceworkDashboardLayout.xaml");
        TestSupport.True(dashboard.Contains("AutomationProperties.Name=\"{Binding HardwareSelectorAutomationName}\"", StringComparison.Ordinal), "Dashboard selector automation");
        TestSupport.True(dashboard.Contains("ToolTip=\"{Binding HardwareSelectorToolTip}\"", StringComparison.Ordinal), "Dashboard selector tooltip");
    }

    private static void CpuSelectorAccessible()
    {
        string cpu = TraceworkPilotSource.Read("HardwareVision", "Views", "Cpu", "TraceworkCpuLayout.xaml");
        TestSupport.True(cpu.Contains("AutomationProperties.Name=\"CPU chart time window\"", StringComparison.Ordinal), "CPU selector automation");
    }

    private static void ForbiddenAbsent(string value) => TestSupport.False(PilotLayouts.Contains(value, StringComparison.OrdinalIgnoreCase), value);

    private static void SharedGridHasNoTimer()
    {
        string grid = TraceworkPilotSource.Read("HardwareVision", "Controls", "TraceworkResponsiveGrid.cs");
        foreach (string value in new[] { "Timer", "CompositionTarget", "Storyboard", "Thread.Sleep" })
            TestSupport.False(grid.Contains(value, StringComparison.Ordinal), $"responsive grid {value}");
    }
}
