using HardwareVision.Behaviors;

namespace HardwareVision.Tests;

internal static class NestedScrollingTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Nested scroll 01 inner can scroll down", InnerCanScrollDown),
        ("Nested scroll 02 inner bottom forwards down", InnerBottomForwardsDown),
        ("Nested scroll 03 inner can scroll up", InnerCanScrollUp),
        ("Nested scroll 04 inner top forwards up", InnerTopForwardsUp),
        ("Nested scroll 05 no scroll range forwards", NoScrollRangeForwards),
        ("Nested scroll 06 ComboBox dropdown suppresses forwarding", ComboBoxDropDownSuppressesForwarding),
        ("Nested scroll 07 behavior attached to both report themes", BehaviorAttachedToBothReportThemes)
    ];

    private static void InnerCanScrollDown()
    {
        TestSupport.False(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 12d, scrollableHeight: 40d, delta: -120, isComboBoxDropDownOpen: false),
            "inner handles wheel down before bottom");
    }

    private static void InnerBottomForwardsDown()
    {
        TestSupport.True(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 40d, scrollableHeight: 40d, delta: -120, isComboBoxDropDownOpen: false),
            "bottom forwards wheel down");
    }

    private static void InnerCanScrollUp()
    {
        TestSupport.False(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 12d, scrollableHeight: 40d, delta: 120, isComboBoxDropDownOpen: false),
            "inner handles wheel up before top");
    }

    private static void InnerTopForwardsUp()
    {
        TestSupport.True(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 0d, scrollableHeight: 40d, delta: 120, isComboBoxDropDownOpen: false),
            "top forwards wheel up");
    }

    private static void NoScrollRangeForwards()
    {
        TestSupport.True(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 0d, scrollableHeight: 0d, delta: -120, isComboBoxDropDownOpen: false),
            "no range forwards wheel down");
        TestSupport.True(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 0d, scrollableHeight: 0d, delta: 120, isComboBoxDropDownOpen: false),
            "no range forwards wheel up");
    }

    private static void ComboBoxDropDownSuppressesForwarding()
    {
        TestSupport.False(
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(verticalOffset: 40d, scrollableHeight: 40d, delta: -120, isComboBoxDropDownOpen: true),
            "open ComboBox dropdown keeps wheel inside popup");
    }

    private static void BehaviorAttachedToBothReportThemes()
    {
        string root = FindRepositoryRoot();
        string classic = File.ReadAllText(Path.Combine(root, "HardwareVision", "Views", "GameSessionReport", "ClassicGameSessionReportLayout.xaml"));
        string tracework = File.ReadAllText(Path.Combine(root, "HardwareVision", "Views", "GameSessionReport", "TraceworkGameSessionReportLayout.xaml"));

        TestSupport.True(
            classic.Contains("NestedScrollViewerBehavior.BubbleMouseWheelAtBoundary=\"True\"", StringComparison.Ordinal),
            "Classic report limit events attach nested scroll behavior");
        TestSupport.True(
            tracework.Contains("NestedScrollViewerBehavior.BubbleMouseWheelAtBoundary=\"True\"", StringComparison.Ordinal),
            "Tracework report limit events attach nested scroll behavior");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? candidate = new(Directory.GetCurrentDirectory());
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
