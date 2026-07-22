namespace HardwareVision.Tests;

internal static class TraceworkDashboardEditorialLayoutTests
{
    private static string Layout => TraceworkPilotSource.Read("HardwareVision", "Views", "Dashboard", "TraceworkDashboardLayout.xaml");

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Dashboard editorial 01 subject is SYSTEM STATE", () => Contains("Text=\"SYSTEM STATE\"")),
        ("Dashboard editorial 02 responsive grid exists", () => Contains("x:Name=\"DashboardEditorialGrid\"")),
        ("Dashboard editorial 03 wide primary is seven columns", () => RegionHas("DashboardPrimaryRegion", "WideColumnSpan=\"7\"")),
        ("Dashboard editorial 04 wide secondary is five columns", () => RegionHas("DashboardSecondaryRegion", "WideColumnSpan=\"5\"")),
        ("Dashboard editorial 05 CPU is primary instrument", () => Contains("x:Name=\"CpuPrimaryInstrument\"")),
        ("Dashboard editorial 06 GPU is telemetry field", () => Contains("x:Name=\"GpuTelemetryField\"")),
        ("Dashboard editorial 07 one shared data rail", () => TestSupport.Equal(1, TraceworkPilotSource.Count(Layout, "x:Name=\"DashboardDataRail\""), "DashboardDataRail count")),
        ("Dashboard editorial 08 all six semantic cards remain", SixSemanticCardsRemain),
        ("Dashboard editorial 09 selector options remain", () => Contains("ItemsSource=\"{Binding HardwareOptions}\"")),
        ("Dashboard editorial 10 selector selection remains two-way", () => Contains("SelectedItem=\"{Binding SelectedHardwareOption, Mode=TwoWay")),
        ("Dashboard editorial 11 metric visibility remains", () => Contains("Value=\"{Binding IsVisible, Converter={StaticResource BoolToVisibilityConverter}}\"")),
        ("Dashboard editorial 12 tooltips remain", () => TestSupport.True(TraceworkPilotSource.Count(Layout, "ToolTip=\"{Binding") >= 12, "Dashboard tooltip coverage")),
        ("Dashboard editorial 13 page has no raw hex colors", () => TestSupport.False(System.Text.RegularExpressions.Regex.IsMatch(Layout, "#[0-9A-Fa-f]{6,8}"), "raw Dashboard color")),
        ("Dashboard editorial 14 full panel count is bounded", () => TestSupport.True(TraceworkPilotSource.Count(Layout, "<controls:TraceworkPanel") <= 2, "bounded full panels"))
    ];

    private static bool Contains(string value)
    {
        TestSupport.True(Layout.Contains(value, StringComparison.Ordinal), value);
        return true;
    }

    private static void RegionHas(string name, string placement)
    {
        int start = Layout.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        TestSupport.True(start >= 0, name);
        string region = Layout[start..Math.Min(Layout.Length, start + 1800)];
        TestSupport.True(region.Contains(placement, StringComparison.Ordinal), $"{name} {placement}");
    }

    private static void SixSemanticCardsRemain()
    {
        foreach (string binding in new[] { "CpuOverviewCard", "GpuOverviewCard", "MemoryOverviewCard", "DiskOverviewCard", "NetworkOverviewCard", "SystemOverviewCard" })
            Contains($"DataContext=\"{{Binding {binding}}}\"");
    }
}
