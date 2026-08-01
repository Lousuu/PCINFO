using HardwareVision.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
        ("Nested scroll 08 top tolerance includes one tenth DIP", () => Forward(0.1d, 40d, 120, true)),
        ("Nested scroll 09 top tolerance excludes 0.11 DIP", () => Forward(0.11d, 40d, 120, false)),
        ("Nested scroll 10 bottom tolerance includes one tenth DIP", () => Forward(39.9d, 40d, -120, true)),
        ("Nested scroll 11 bottom tolerance excludes 0.11 DIP", () => Forward(39.89d, 40d, -120, false)),
        ("Nested scroll 12 legacy attached property remains", () => BehaviorContains("BubbleMouseWheelAtBoundaryProperty")),
        ("Nested scroll 13 ForwardAtBoundary attached property exists", () => BehaviorContains("ForwardAtBoundaryProperty")),
        ("Nested scroll 14 one wheel notch is one step", () => TestSupport.Equal(1, NestedScrollViewerBehavior.WheelStepCount(120), "steps")),
        ("Nested scroll 15 two wheel notches are two steps", () => TestSupport.Equal(2, NestedScrollViewerBehavior.WheelStepCount(-240), "steps")),
        ("Nested scroll 16 Shift wheel stays horizontal", () => BehaviorContains("ModifierKeys.Shift")),
        ("Nested scroll 17 pointer drag does not forward", () => BehaviorContains("Mouse.LeftButton == MouseButtonState.Pressed")),
        ("Nested scroll 18 forwarding walks the full viewer chain", () => BehaviorContains("BuildScrollChain", "candidate.LineUp()", "candidate.LineDown()")),
        ("Nested scroll 19 forwarding raises no recursive wheel", () => TestSupport.False(ReadBehavior().Contains("outer.RaiseEvent", StringComparison.Ordinal), "recursive RaiseEvent")),
        ("Nested scroll 20 Advanced Sensors opts in", AdvancedSensorsOptsIn),
        ("Nested scroll 21 high resolution deltas accumulate", HighResolutionDeltasAccumulate),
        ("Nested scroll 22 static forwarding guard is absent", () =>
            TestSupport.False(ReadBehavior().Contains("static bool isForwarding", StringComparison.Ordinal), "global forwarding guard")),
        ("Nested scroll 23 shown triple viewer forwards same gesture to outer",
            ShownTripleViewerForwardsToOuter),
        ("Nested scroll 24 shell attaches application-wide forwarding core",
            ShellAttachesApplicationWideCore)
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

    private static void HighResolutionDeltasAccumulate()
    {
        int notches = NestedScrollViewerBehavior.AccumulateWheelDeltaForDiagnostics(
            0,
            40,
            out int remainder);
        TestSupport.Equal(0, notches, "first fractional notch");
        notches = NestedScrollViewerBehavior.AccumulateWheelDeltaForDiagnostics(
            remainder,
            40,
            out remainder);
        TestSupport.Equal(0, notches, "second fractional notch");
        notches = NestedScrollViewerBehavior.AccumulateWheelDeltaForDiagnostics(
            remainder,
            40,
            out remainder);
        TestSupport.Equal(1, notches, "third fractional notch");
        TestSupport.Equal(0, remainder, "fractional remainder");
    }

    private static void ShownTripleViewerForwardsToOuter()
    {
        EnsureApplication();
        Border leaf = new()
        {
            Height = 360d,
            Background = Brushes.Transparent
        };
        ScrollViewer inner = new()
        {
            Height = 100d,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = leaf
        };
        StackPanel middleContent = new();
        middleContent.Children.Add(inner);
        middleContent.Children.Add(new Border { Height = 360d });
        ScrollViewer middle = new()
        {
            Height = 160d,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = middleContent
        };
        StackPanel outerContent = new();
        outerContent.Children.Add(middle);
        outerContent.Children.Add(new Border { Height = 480d });
        ScrollViewer outer = new()
        {
            Height = 220d,
            Width = 420d,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = outerContent
        };
        Window window = new()
        {
            Width = 460d,
            Height = 280d,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Content = outer
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            inner.ScrollToEnd();
            middle.ScrollToEnd();
            outer.ScrollToTop();
            window.UpdateLayout();
            bool handled = NestedScrollViewerBehavior.TryForwardWheel(
                leaf,
                outer,
                -120);
            Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                DispatcherPriority.ApplicationIdle);
            TestSupport.True(handled, "wheel handled by continuous core");
            TestSupport.True(
                outer.VerticalOffset > 0.1d,
                $"outer offset advances from same gesture ({outer.VerticalOffset:0.###})");
        }
        finally
        {
            window.Close();
            window.Content = null;
        }
    }

    private static void ShellAttachesApplicationWideCore()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(
            Path.Combine(root, "HardwareVision", "Views", "Shell", "MainShellHost.xaml"));
        TestSupport.True(
            xaml.Contains(
                "NestedScrollViewerBehavior.ForwardAtBoundary=\"True\"",
                StringComparison.Ordinal),
            "PageHost application-wide nested scroll attachment");
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App application = new();
            application.InitializeComponent();
        }

        Application.Current!.ShutdownMode = ShutdownMode.OnExplicitShutdown;
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
