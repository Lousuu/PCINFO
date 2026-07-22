namespace HardwareVision.Tests;

internal static class NetworkMatrixCompactionTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Network matrix 01 container owns visibility", ContainerVisibility),
        ("Network matrix 02 cell template has no layout visibility", TemplateVisibilityRemoved),
        ("Network matrix 03 six metrics use three columns", () => Contains("<UniformGrid Columns=\"3\"")),
        ("Network matrix 04 hidden item collapses container", () => Contains("TargetType=\"ContentPresenter\"", "IsVisible, Converter={StaticResource BoolToVisibilityConverter}")),
        ("Network matrix 05 badges share base style", BadgeBase),
        ("Network matrix 06 badge corners match", () => StyleContains("CornerRadius\" Value=\"1\"")),
        ("Network matrix 07 badge heights match", () => StyleContains("Height\" Value=\"28\"")),
        ("Network matrix 08 status binds Device IsUp", () => StyleContains("Binding=\"{Binding Device.IsUp}\"")),
        ("Network matrix 09 status does not infer text", NoTextInference),
        ("Network matrix 10 original bindings remain", () => Contains("ItemsSource=\"{Binding NetworkAdapters}\"", "ItemsSource=\"{Binding Metrics}\"", "DisplayType", "StatusText"))
    ];

    private static string Source => TraceworkPilotSource.Read("HardwareVision", "Views", "Network", "TraceworkNetworkLayout.xaml");
    private static string Styles => TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Pages.xaml");
    private static void Contains(params string[] values) { foreach (string value in values) TestSupport.True(Source.Contains(value, StringComparison.Ordinal), value); }
    private static void StyleContains(string value) => TestSupport.True(Styles.Contains(value, StringComparison.Ordinal), value);
    private static string CellTemplate() { int start = Source.IndexOf("x:Key=\"NetworkMetricCellTemplate\"", StringComparison.Ordinal); int end = Source.IndexOf("</DataTemplate>", start, StringComparison.Ordinal); return Source[start..end]; }
    private static void ContainerVisibility() => Contains("<ItemsControl.ItemContainerStyle>", "<Setter Property=\"Visibility\" Value=\"{Binding IsVisible");
    private static void TemplateVisibilityRemoved() => TestSupport.False(CellTemplate().Contains("Visibility=", StringComparison.Ordinal), "template visibility");
    private static void BadgeBase() { Contains("TraceworkNetworkBadgeStyle", "TraceworkNetworkStatusBadgeStyle"); StyleContains("BasedOn=\"{StaticResource TraceworkNetworkBadgeStyle}\""); }
    private static void NoTextInference() { int start = Styles.IndexOf("TraceworkNetworkStatusBadgeStyle", StringComparison.Ordinal); int end = Styles.IndexOf("TraceworkNetworkBadgeTextStyle", start, StringComparison.Ordinal); TestSupport.False(Styles[start..end].Contains("StatusText", StringComparison.Ordinal), "status text inference"); }
}
