namespace HardwareVision.Tests;

internal static class TraceworkCpuTelemetryLayoutTests
{
    private static string Layout => TraceworkPilotSource.Read("HardwareVision", "Views", "Cpu", "TraceworkCpuLayout.xaml");

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("CPU telemetry 01 subject is PACKAGE TELEMETRY", () => Contains("Text=\"PACKAGE TELEMETRY\"")),
        ("CPU telemetry 02 responsive grid exists", () => Contains("x:Name=\"CpuTelemetryGrid\"")),
        ("CPU telemetry 03 wide identity is four columns", () => RegionHas("CpuPrimaryRegion", "WideColumnSpan=\"4\"")),
        ("CPU telemetry 04 wide chart is eight columns", () => RegionHas("CpuPrimaryChartField", "WideColumnSpan=\"8\"")),
        ("CPU telemetry 05 device identity remains", () => Contains("Text=\"{Binding CpuName}\"")),
        ("CPU telemetry 06 topology remains", () => Contains("Text=\"{Binding CoreThreadSummary}\"")),
        ("CPU telemetry 07 window remains", () => Contains("SelectedChartWindowSeconds, Mode=TwoWay")),
        ("CPU telemetry 08 primary metric remains", () => Contains("MetricProjection.PrimaryMetric.Value")),
        ("CPU telemetry 09 secondary metrics remain", () => Contains("ItemsSource=\"{Binding MetricProjection.SecondaryMetrics}\"")),
        ("CPU telemetry 10 primary chart uses Charts zero", () => Contains("DataContext=\"{Binding Charts[0]}\"")),
        ("CPU telemetry 11 three auxiliary charts remain", ThreeAuxiliaryChartsRemain),
        ("CPU telemetry 12 values and ranges remain", ChartBindingsRemain),
        ("CPU telemetry 13 CoreRows matrix remains", () => Contains("ItemsSource=\"{Binding CoreRows}\"")),
        ("CPU telemetry 14 page has no raw hex colors", () => TestSupport.False(System.Text.RegularExpressions.Regex.IsMatch(Layout, "#[0-9A-Fa-f]{6,8}"), "raw CPU color")),
        ("CPU telemetry 15 only matrix is a full panel", () => TestSupport.Equal(1, TraceworkPilotSource.Count(Layout, "<controls:TraceworkPanel"), "CPU full panel count"))
    ];

    private static bool Contains(string value)
    {
        TestSupport.True(Layout.Contains(value, StringComparison.Ordinal), value);
        return true;
    }

    private static void RegionHas(string name, string placement)
    {
        int start = Layout.IndexOf($"x:Name=\"{name}\"", StringComparison.Ordinal);
        string region = Layout[start..Math.Min(Layout.Length, start + 1800)];
        TestSupport.True(region.Contains(placement, StringComparison.Ordinal), $"{name} {placement}");
    }

    private static void ThreeAuxiliaryChartsRemain()
    {
        foreach (int index in new[] { 1, 2, 3 }) Contains($"DataContext=\"{{Binding Charts[{index}]}}\"");
    }

    private static void ChartBindingsRemain()
    {
        foreach (string binding in new[] { "Values", "HasData", "MinimumValue", "MaximumValue", "AverageText", "MinimumText", "MaximumText" }) Contains($"{{Binding {binding}");
    }
}
