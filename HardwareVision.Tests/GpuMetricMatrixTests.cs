namespace HardwareVision.Tests;

internal static class GpuMetricMatrixTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("GPU matrix 01 primary column spans 8", () => Contains("x:Name=\"GpuPrimaryColumn\"", "WideColumnSpan=\"8\"")),
        ("GPU matrix 02 secondary column spans 4", () => Contains("x:Name=\"GpuSecondaryColumn\"", "WideColumnSpan=\"4\"")),
        ("GPU matrix 03 columns are independent stacks", IndependentStacks),
        ("GPU matrix 04 selector does not share Hero row", SelectorSeparated),
        ("GPU matrix 05 identity does not share chart row", IdentitySeparated),
        ("GPU matrix 06 uses AdaptiveUniformGrid", () => Contains("AdaptiveUniformGrid", "MaximumColumns=\"2\"")),
        ("GPU matrix 07 old secondary UniformGrid removed", OldGridRemoved),
        ("GPU matrix 08 label column is Auto", () => Contains("SharedSizeGroup=\"GpuMetricLabel\"", "Width=\"Auto\"")),
        ("GPU matrix 09 compact order is explicit", CompactOrder),
        ("GPU matrix 10 original selector bindings remain", () => Contains("GpuDevices", "SelectedGpu, Mode=TwoWay")),
        ("GPU matrix 11 charts and projections remain", () => Contains("ItemsSource=\"{Binding Charts}\"", "InfoProjection.VisibleMetrics", "MetricProjection.SecondaryMetrics")),
        ("GPU matrix 12 no new sampling ViewModel", NoSampling)
    ];

    private static string Source => TraceworkPilotSource.Read("HardwareVision", "Views", "Gpu", "TraceworkGpuLayout.xaml");
    private static void Contains(params string[] values) { foreach (string value in values) TestSupport.True(Source.Contains(value, StringComparison.Ordinal), value); }
    private static void IndependentStacks() { Contains("<StackPanel x:Name=\"GpuPrimaryColumn\"", "<StackPanel x:Name=\"GpuSecondaryColumn\""); TestSupport.Equal(1, TraceworkPilotSource.Count(Source, "NavigationMotion.Role=\"Primary\""), "primary role"); TestSupport.Equal(1, TraceworkPilotSource.Count(Source, "NavigationMotion.Role=\"Secondary\""), "secondary role"); }
    private static void SelectorSeparated() { int secondary = Source.IndexOf("GpuSecondaryColumn", StringComparison.Ordinal); int primary = Source.IndexOf("GpuPrimaryColumn", StringComparison.Ordinal); int selector = Source.IndexOf("GpuDeviceSelector", StringComparison.Ordinal); TestSupport.True(selector > secondary && selector < primary, "selector in secondary stack"); }
    private static void IdentitySeparated() => Contains("GpuWideDeviceIdentity", "GpuPrimaryChartField");
    private static void OldGridRemoved() { int start = Source.IndexOf("MetricProjection.SecondaryMetrics", StringComparison.Ordinal); int end = Source.IndexOf("</ItemsControl>", start, StringComparison.Ordinal); TestSupport.False(Source[start..end].Contains("<UniformGrid Columns=\"2\"", StringComparison.Ordinal), "old grid"); }
    private static void CompactOrder() { Contains("CompactRow=\"2\"", "CompactRow=\"3\"", "x:Name=\"GpuCompactDeviceIdentity\"", "CompactRow=\"4\"", "CompactRow=\"5\""); }
    private static void NoSampling() { TestSupport.False(Source.Contains("new GpuViewModel", StringComparison.Ordinal), "new VM"); TestSupport.False(Source.Contains("Polling", StringComparison.Ordinal), "polling"); }
}
