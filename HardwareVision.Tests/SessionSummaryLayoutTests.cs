namespace HardwareVision.Tests;

internal static class SessionSummaryLayoutTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session layout 01 independent left column", () => Contains("x:Name=\"SessionLeftColumn\"", "WideColumnSpan=\"4\"")),
        ("Session layout 02 independent right column", () => Contains("x:Name=\"SessionRightColumn\"", "WideColumnSpan=\"8\"")),
        ("Session layout 03 summary has no large minimum", NoLargeSummary),
        ("Session layout 04 summary content aligns top", () => Contains("VerticalContentAlignment=\"Top\"", "<Grid VerticalAlignment=\"Top\"")),
        ("Session layout 05 summary uses compact padding", () => Contains("Padding=\"16\"")),
        ("Session layout 06 compact order is fixed", CompactOrder),
        ("Session layout 07 nested scrolling preserved", () => Contains("NestedScrollViewerBehavior.BubbleMouseWheelAtBoundary=\"True\"")),
        ("Session layout 08 report commands preserved", () => Contains("BackCommand", "OpenDirectoryCommand", "ExportPlainCsvCommand")),
        ("Session layout 09 report data bindings preserved", () => Contains("KeyMetrics", "Charts", "SelectedChart, Mode=TwoWay", "PerformanceLimitEvents", "HardwareMetrics")),
        ("Session layout 10 no report business source changed", BusinessSourceUntouched)
    ];

    private static string Source => TraceworkPilotSource.Read("HardwareVision", "Views", "GameSessionReport", "TraceworkGameSessionReportLayout.xaml");
    private static void Contains(params string[] values) { foreach (string value in values) TestSupport.True(Source.Contains(value, StringComparison.Ordinal), value); }
    private static string SummaryTemplate() { int start = Source.IndexOf("SessionDiagnosisSummaryTemplate", StringComparison.Ordinal); int end = Source.IndexOf("SessionTimelineTemplate", start, StringComparison.Ordinal); return Source[start..end]; }
    private static void NoLargeSummary() { string summary = SummaryTemplate(); TestSupport.False(summary.Contains("MinHeight", StringComparison.Ordinal), "summary min height"); TestSupport.False(summary.Contains("<controls:TraceworkPanel Height=", StringComparison.Ordinal), "panel fixed height"); }
    private static void CompactOrder()
    {
        int start = Source.IndexOf("x:Name=\"SessionCompactColumn\"", StringComparison.Ordinal);
        int end = Source.IndexOf("Code=\"RPT.60\"", start, StringComparison.Ordinal);
        string compact = Source[start..end];
        string[] templates = ["SessionDiagnosisSummaryTemplate", "SessionTimelineTemplate", "SessionPerformanceSummaryTemplate", "SessionTimelineFieldTemplate", "SessionLimitEventsTemplate", "SessionHardwareSnapshotTemplate"];
        int previous = -1;
        foreach (string template in templates) { int current = compact.IndexOf(template, previous + 1, StringComparison.Ordinal); TestSupport.True(current > previous, template); previous = current; }
    }
    private static void BusinessSourceUntouched()
    {
        string[] changedBusinessTokens = ["GameSessionReportService", "Report accuracy", "PresentMon"];
        foreach (string token in changedBusinessTokens) TestSupport.False(Source.Contains($"new {token}", StringComparison.Ordinal), token);
    }
}
