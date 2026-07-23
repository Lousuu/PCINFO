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
        ("Nested scroll 07 behavior attached to both report themes", BehaviorAttachedToBothReportThemes),
        ("Nested scroll 08 top threshold includes half DIP", () => Forward(0.5d, 40d, 120, true)),
        ("Nested scroll 09 top threshold excludes 0.51 DIP", () => Forward(0.51d, 40d, 120, false)),
        ("Nested scroll 10 bottom threshold includes half DIP", () => Forward(39.5d, 40d, -120, true)),
        ("Nested scroll 11 bottom threshold excludes 0.51 DIP", () => Forward(39.49d, 40d, -120, false)),
        ("Nested scroll 12 legacy attached property remains", () => BehaviorContains("BubbleMouseWheelAtBoundaryProperty")),
        ("Nested scroll 13 ForwardAtBoundary attached property exists", () => BehaviorContains("ForwardAtBoundaryProperty")),
        ("Nested scroll 14 one wheel notch is one step", () => TestSupport.Equal(1, NestedScrollViewerBehavior.WheelStepCount(120), "steps")),
        ("Nested scroll 15 two wheel notches are two steps", () => TestSupport.Equal(2, NestedScrollViewerBehavior.WheelStepCount(-240), "steps")),
        ("Nested scroll 16 Shift wheel stays horizontal", () => BehaviorContains("ModifierKeys.Shift")),
        ("Nested scroll 17 pointer drag does not forward", () => BehaviorContains("Mouse.LeftButton == MouseButtonState.Pressed")),
        ("Nested scroll 18 forwarding calls outer line methods", () => BehaviorContains("outer.LineUp()", "outer.LineDown()")),
        ("Nested scroll 19 forwarding raises no recursive wheel", () => TestSupport.False(ReadBehavior().Contains("outer.RaiseEvent", StringComparison.Ordinal), "recursive RaiseEvent")),
        ("Nested scroll 20 Advanced Sensors opts in", AdvancedSensorsOptsIn)
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

    private static void Forward(double offset, double height, int delta, bool expected) =>
        TestSupport.Equal(
            expected,
            NestedScrollViewerBehavior.ShouldForwardAtBoundary(offset, height, delta, false),
            "boundary result");

    private static void BehaviorContains(params string[] values)
    {
        string source = ReadBehavior();
        foreach (string value in values)
        {
            TestSupport.True(source.Contains(value, StringComparison.Ordinal), value);
        }
    }

    private static void AdvancedSensorsOptsIn()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "HardwareVision", "Views", "AdvancedSensors", "TraceworkAdvancedSensorsLayout.xaml"));
        TestSupport.True(
            xaml.Contains("NestedScrollViewerBehavior.ForwardAtBoundary=\"True\"", StringComparison.Ordinal),
            "Advanced Sensors boundary forwarding");
    }

    private static string ReadBehavior() =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "HardwareVision", "Behaviors", "NestedScrollViewerBehavior.cs"));

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
