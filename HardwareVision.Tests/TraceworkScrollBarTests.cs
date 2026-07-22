namespace HardwareVision.Tests;

internal static class TraceworkScrollBarTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Scrollbar 01 Tracework corner is rectangular", () => TraceContains("<CornerRadius x:Key=\"ScrollThumbCornerRadius\">1</CornerRadius>")),
        ("Scrollbar 02 Thumb minimum is 32", () => ControlsContains("MinHeight=\"32\"")),
        ("Scrollbar 03 page visibility stays Auto", PageVisibility),
        ("Scrollbar 04 Thumb is visible without hover", DefaultThumb),
        ("Scrollbar 05 track line exists", () => ControlsContains("x:Name=\"TrackLine\"")),
        ("Scrollbar 06 no fade storyboard", NoFade),
        ("Scrollbar 07 no timer", NoTimer),
        ("Scrollbar 08 page horizontal scroll disabled", HorizontalDisabled),
        ("Scrollbar 09 collection controls retain scrolling", InternalScrolling),
        ("Scrollbar 10 Classic keeps rounded resource", ClassicCorner)
    ];

    private static string Controls => TraceworkPilotSource.Read("HardwareVision", "Themes", "Controls.xaml");
    private static string TraceColors => TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Colors.xaml");
    private static string ClassicColors => TraceworkPilotSource.Read("HardwareVision", "Themes", "Colors.xaml");
    private static string Pages => TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Pages.xaml");
    private static void ControlsContains(string value) => TestSupport.True(Controls.Contains(value, StringComparison.Ordinal), value);
    private static void TraceContains(string value) => TestSupport.True(TraceColors.Contains(value, StringComparison.Ordinal), value);
    private static void PageVisibility() => TestSupport.True(Pages.Contains("VerticalScrollBarVisibility\" Value=\"Auto", StringComparison.Ordinal), "Auto scrollbar");
    private static void DefaultThumb() { ControlsContains("Background=\"{DynamicResource ScrollThumbBrush}\""); TestSupport.False(Controls.Contains("Visibility=\"Hidden\"", StringComparison.Ordinal), "thumb hidden"); }
    private static void NoFade() { TestSupport.False(Controls.Contains("Storyboard", StringComparison.Ordinal), "storyboard"); TestSupport.False(Controls.Contains("DoubleAnimation", StringComparison.Ordinal), "fade"); }
    private static void NoTimer() => TestSupport.False(Controls.Contains("Timer", StringComparison.Ordinal), "timer");
    private static void HorizontalDisabled() => TestSupport.True(Pages.Contains("HorizontalScrollBarVisibility\" Value=\"Disabled", StringComparison.Ordinal), "horizontal disabled");
    private static void InternalScrolling() { TestSupport.True(Pages.Contains("ScrollViewer.CanContentScroll", StringComparison.Ordinal), "content scroll"); TestSupport.True(Pages.Contains("VirtualizationMode", StringComparison.Ordinal), "virtualized list"); }
    private static void ClassicCorner() => TestSupport.True(ClassicColors.Contains("<CornerRadius x:Key=\"ScrollThumbCornerRadius\">4</CornerRadius>", StringComparison.Ordinal), "Classic corner");
}
