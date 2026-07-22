using System.Text.RegularExpressions;

namespace HardwareVision.Tests;

internal static class TraceworkVisualPrimitiveTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Visual primitive 01 InstrumentField exists", () => HasStyle("TraceworkInstrumentFieldStyle")),
        ("Visual primitive 02 DataRail exists", () => HasStyle("TraceworkDataRailStyle")),
        ("Visual primitive 03 AnnotationRail exists", () => HasStyle("TraceworkAnnotationRailStyle")),
        ("Visual primitive 04 ChartField exists", () => HasStyle("TraceworkChartFieldStyle")),
        ("Visual primitive 05 SignalMatrix exists", () => HasStyle("TraceworkSignalMatrixStyle")),
        ("Visual primitive 06 TechnicalPanel exists", () => HasStyle("TraceworkTechnicalPanelStyle")),
        ("Visual primitive 07 primary chart exists", () => HasStyle("TraceworkPrimaryChartStyle")),
        ("Visual primitive 08 micro chart exists", () => HasStyle("TraceworkMicroChartStyle")),
        ("Visual primitive 09 chart statistics rail exists", () => HasStyle("TraceworkChartStatisticRailStyle")),
        ("Visual primitive 10 micro channel exists", () => HasStyle("TraceworkMicroChannelStyle")),
        ("Visual primitive 11 Display typography is 48", () => HasStyleValue("TraceworkDisplayValueTextStyle", "FontSize", "48")),
        ("Visual primitive 12 PageIndex typography is 36", () => HasStyleValue("TraceworkPageIndexTextStyle", "FontSize", "36")),
        ("Visual primitive 13 Section typography is 16", () => HasStyleValue("TraceworkSectionTitleTextStyle", "FontSize", "16")),
        ("Visual primitive 14 Body typography is 12", () => HasStyleValue("TraceworkBodyValueTextStyle", "FontSize", "12")),
        ("Visual primitive 15 Annotation typography is 9", () => HasStyleValue("TraceworkAnnotationTextStyle", "FontSize", "9")),
        ("Visual primitive 16 old panel style remains", () => HasStyle("TraceworkPanelStyle")),
        ("Visual primitive 17 old hero style remains", () => HasStyle("TraceworkHeroPanelStyle")),
        ("Visual primitive 18 old data table style remains", () => HasStyle("TraceworkDataTablePanelStyle")),
        ("Visual primitive 19 Pages has no unkeyed style", NoUnkeyedStyle),
        ("Visual primitive 20 primitives contain no motion clocks", NoMotionClocks)
    ];

    private static string Pages => TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Pages.xaml");

    private static void HasStyle(string key) => TestSupport.True(Pages.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal), key);

    private static void HasStyleValue(string key, string property, string value)
    {
        int start = Pages.IndexOf($"x:Key=\"{key}\"", StringComparison.Ordinal);
        string style = Pages[start..Math.Min(Pages.Length, start + 900)];
        TestSupport.True(style.Contains($"Property=\"{property}\" Value=\"{value}\"", StringComparison.Ordinal), $"{key} {property}");
    }

    private static void NoUnkeyedStyle() => TestSupport.False(
        Regex.IsMatch(Pages, "<Style(?=\\s|>)(?![^>]*\\bx:Key\\s*=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline),
        "unkeyed Tracework style");

    private static void NoMotionClocks()
    {
        string pilot = Pages + TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Colors.xaml");
        TestSupport.False(new[] { "Storyboard", "DoubleAnimation", "DispatcherTimer", "CompositionTarget.Rendering" }.Any(pilot.Contains), "visual primitive motion clock");
    }
}
