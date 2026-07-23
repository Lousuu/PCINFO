using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Behaviors;
using HardwareVision.Views.AdvancedSensors;

namespace HardwareVision.Tests;

internal static class AdvancedSensorsScrollTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Advanced scroll 01 runtime outer page ScrollViewer exists", OuterPageScrollViewerExists),
        ("Advanced scroll 02 runtime 1600x900 grid is at least 520", () => AssertGridHeight(1600, 900, 520)),
        ("Advanced scroll 03 runtime 1120x720 grid is at least 420", () => AssertGridHeight(1120, 720, 420)),
        ("Advanced scroll 04 runtime outer range is positive", OuterRangeIsPositive),
        ("Advanced scroll 05 runtime inner range is positive", InnerRangeIsPositive),
        ("Advanced scroll 06 runtime middle wheel moves only DataGrid", MiddleWheelMovesOnlyInner),
        ("Advanced scroll 07 runtime top-up wheel moves outer", TopUpMovesOuter),
        ("Advanced scroll 08 runtime bottom-down wheel moves outer", BottomDownMovesOuter),
        ("Advanced scroll 09 runtime one wheel never moves both layers", OneWheelDoesNotMoveBoth),
        ("Advanced scroll 10 open ComboBox suppresses forwarding", OpenComboBoxSuppressesForwarding),
        ("Advanced scroll 11 virtualization attributes are preserved", VirtualizationAttributesPreserved),
        ("Advanced scroll 12 runtime 500 rows stay virtualized", FiveHundredRowsStayVirtualized),
        ("Advanced scroll 13 runtime rail is content-height", RailIsContentHeight),
        ("Advanced scroll 14 runtime page scrolls to panel bottom", PageScrollsToBottom),
        ("Advanced scroll 15 report boundary behavior is preserved", ReportBoundaryBehaviorPreserved)
    ];

    private static void OuterPageScrollViewerExists() =>
        WithLayout(1120, 720, (_, outer, _, _) =>
        {
            TestSupport.Equal(ScrollBarVisibility.Auto, outer.VerticalScrollBarVisibility, "vertical owner");
            TestSupport.Equal(ScrollBarVisibility.Disabled, outer.HorizontalScrollBarVisibility, "horizontal owner");
            TestSupport.False(outer.CanContentScroll, "physical page scrolling");
        });

    private static void AssertGridHeight(double width, double height, double minimumGridHeight) =>
        WithLayout(width, height, (layout, outer, grid, inner) =>
        {
            TestSupport.True(grid.ActualHeight >= minimumGridHeight, "DataGrid actual height");
        });

    private static void OuterRangeIsPositive() =>
        WithLayout(1120, 720, (_, outer, _, _) =>
            TestSupport.True(outer.ScrollableHeight > 0d, "outer scroll range"));

    private static void InnerRangeIsPositive() =>
        WithLayout(1120, 720, (_, _, _, inner) =>
            TestSupport.True(inner.ScrollableHeight > 0d, "inner scroll range"));

    private static void MiddleWheelMovesOnlyInner() =>
        WithLayout(1120, 720, (layout, outer, grid, inner) =>
        {
            outer.ScrollToVerticalOffset(Math.Min(20d, outer.ScrollableHeight));
            inner.ScrollToVerticalOffset(Math.Min(100d, inner.ScrollableHeight / 2d));
            Drain(layout);
            double outerBefore = outer.VerticalOffset;
            double innerBefore = inner.VerticalOffset;
            MouseWheelEventArgs preview = RaiseWheel(layout, grid, inner, -120);
            TestSupport.False(preview.Handled, "interior preview not forwarded");
            TestSupport.True(inner.VerticalOffset > innerBefore, "DataGrid moves");
            TestSupport.True(Math.Abs(outer.VerticalOffset - outerBefore) <= 0.5d, "outer stays fixed");
        });

    private static void TopUpMovesOuter() =>
        WithLayout(1120, 720, (layout, outer, grid, inner) =>
        {
            outer.ScrollToVerticalOffset(Math.Min(100d, outer.ScrollableHeight));
            inner.ScrollToTop();
            Drain(layout);
            double outerBefore = outer.VerticalOffset;
            MouseWheelEventArgs preview = RaiseWheel(layout, grid, inner, 120);
            TestSupport.True(preview.Handled, "top preview forwarded");
            TestSupport.True(outer.VerticalOffset < outerBefore, "outer moves up");
            TestSupport.True(inner.VerticalOffset <= 0.5d, "inner stays at top");
        });

    private static void BottomDownMovesOuter() =>
        WithLayout(1120, 720, (layout, outer, grid, inner) =>
        {
            inner.ScrollToEnd();
            outer.ScrollToTop();
            Drain(layout);
            double before = outer.VerticalOffset;
            MouseWheelEventArgs wheel = RaiseWheel(layout, grid, inner, -120);
            TestSupport.True(wheel.Handled, "bottom preview forwarded");
            TestSupport.True(outer.VerticalOffset > before, "nearest outer moved down");
            TestSupport.True(inner.VerticalOffset >= inner.ScrollableHeight - 0.5d, "inner remains at bottom");
        });

    private static void OneWheelDoesNotMoveBoth() =>
        WithLayout(1120, 720, (layout, outer, grid, inner) =>
        {
            inner.ScrollToEnd();
            outer.ScrollToTop();
            Drain(layout);
            double innerBefore = inner.VerticalOffset;
            RaiseWheel(layout, grid, inner, -120);
            TestSupport.True(outer.VerticalOffset > 0d, "outer moved");
            TestSupport.True(Math.Abs(inner.VerticalOffset - innerBefore) <= 0.5d, "inner did not also move");
        });

    private static void OpenComboBoxSuppressesForwarding()
    {
        EnsureApplication();
        ComboBox comboBox = new() { ItemsSource = new[] { "one", "two" } };
        Window host = new()
        {
            Content = comboBox,
            Width = 200,
            Height = 100,
            Left = -32000,
            Top = -32000,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            comboBox.IsDropDownOpen = true;
            Drain(comboBox);
            TestSupport.True(NestedScrollViewerBehavior.IsOpenComboBoxDropDown(comboBox), "runtime open ComboBox");
            TestSupport.False(
                NestedScrollViewerBehavior.ShouldForwardAtBoundary(40d, 40d, -120, isComboBoxDropDownOpen: true),
                "open ComboBox suppresses boundary forwarding");
        }
        finally
        {
            comboBox.IsDropDownOpen = false;
            host.Content = null;
            host.Close();
        }
    }

    private static void VirtualizationAttributesPreserved()
    {
        foreach (string value in new[]
                 {
                     "EnableRowVirtualization=\"True\"",
                     "EnableColumnVirtualization=\"True\"",
                     "VirtualizingPanel.IsVirtualizing=\"True\"",
                     "VirtualizingPanel.VirtualizationMode=\"Recycling\"",
                     "ScrollViewer.CanContentScroll=\"True\"",
                     "VirtualizingPanel.ScrollUnit=\"Item\"",
                     "RowHeight=\"34\""
                 })
        {
            SourceContains(value);
        }
    }

    private static void FiveHundredRowsStayVirtualized() =>
        WithLayout(1120, 720, (_, _, grid, _) =>
        {
            int realized = Enumerable.Range(0, grid.Items.Count)
                .Count(index => grid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow);
            TestSupport.True(realized > 0, "some visible rows realized");
            TestSupport.True(realized < 500, "not all 500 rows realized");
        });

    private static void RailIsContentHeight() =>
        WithLayout(1120, 720, (layout, _, _, _) =>
        {
            FrameworkElement rail = TestSupport.NotNull(
                layout.FindName("AdvancedSensorFilterRail") as FrameworkElement,
                "rail");
            TestSupport.True(rail.ActualHeight > 0d && rail.ActualHeight < layout.ActualHeight, "bounded rail height");
            TestSupport.Equal(VerticalAlignment.Top, rail.VerticalAlignment, "rail aligned to content top");
            TestSupport.False(Source().Contains("<RowDefinition Height=\"*\"", StringComparison.Ordinal), "no star-owned page row");
        });

    private static void PageScrollsToBottom() =>
        WithLayout(1120, 720, (layout, outer, _, _) =>
        {
            outer.ScrollToEnd();
            Drain(layout);
            TestSupport.True(
                outer.VerticalOffset >= outer.ScrollableHeight - 0.5d,
                "outer reaches panel bottom");
        });

    private static void ReportBoundaryBehaviorPreserved()
    {
        string classic = TraceworkPilotSource.Read(
            "HardwareVision", "Views", "GameSessionReport", "ClassicGameSessionReportLayout.xaml");
        string tracework = TraceworkPilotSource.Read(
            "HardwareVision", "Views", "GameSessionReport", "TraceworkGameSessionReportLayout.xaml");
        TestSupport.True(classic.Contains("BubbleMouseWheelAtBoundary=\"True\"", StringComparison.Ordinal), "Classic report");
        TestSupport.True(tracework.Contains("BubbleMouseWheelAtBoundary=\"True\"", StringComparison.Ordinal), "Tracework report");
    }

    private static MouseWheelEventArgs RaiseWheel(
        TraceworkAdvancedSensorsLayout layout,
        DataGrid grid,
        ScrollViewer inner,
        int delta)
    {
        MouseWheelEventArgs preview = new(Mouse.PrimaryDevice, Environment.TickCount, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
            Source = inner
        };
        inner.RaiseEvent(preview);
        if (!preview.Handled)
        {
            MouseWheelEventArgs bubble = new(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = inner
            };
            inner.RaiseEvent(bubble);
        }

        Drain(layout);
        return preview;
    }

    private static void Drain(DispatcherObject owner) =>
        owner.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void WithLayout(
        double width,
        double height,
        Action<TraceworkAdvancedSensorsLayout, ScrollViewer, DataGrid, ScrollViewer> assertion)
    {
        EnsureApplication();
        TraceworkAdvancedSensorsLayout layout = new()
        {
            DataContext = new SensorRowsFixture()
        };
        Window host = new()
        {
            Content = layout,
            Width = width,
            Height = height,
            Left = -32000,
            Top = -32000,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            host.UpdateLayout();
            ScrollViewer outer = TestSupport.NotNull(
                layout.FindName("AdvancedSensorsPageScrollViewer") as ScrollViewer,
                "outer ScrollViewer");
            DataGrid grid = TestSupport.NotNull(
                layout.FindName("AdvancedSensorDataGrid") as DataGrid,
                "DataGrid");
            ScrollViewer inner = TestSupport.NotNull(
                Descendants<ScrollViewer>(grid).FirstOrDefault(),
                "DataGrid ScrollViewer");
            assertion(layout, outer, grid, inner);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static string Source() =>
        TraceworkPilotSource.Read("HardwareVision", "Views", "AdvancedSensors", "TraceworkAdvancedSensorsLayout.xaml");

    private static void SourceContains(string value) =>
        TestSupport.True(
            Source().Contains(value, StringComparison.Ordinal),
            value);

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private sealed class SensorRowsFixture
    {
        public string StatusText => "ACTIVE";
        public IReadOnlyList<SensorRowFixture> SensorRows { get; } =
            Enumerable.Range(0, 500).Select(index => new SensorRowFixture(index)).ToArray();
    }

    private sealed class SensorRowFixture(int index)
    {
        public string DisplayName => $"Sensor {index}";
        public string DisplayType => "Temperature";
        public string Readout => $"{index} °C";
        public string Availability => "Available";
    }
}
