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
        ("Advanced scroll 01 outer owns vertical page scroll", () => SourceContains("VerticalScrollBarVisibility=\"Auto\"")),
        ("Advanced scroll 02 outer disables horizontal scroll", () => SourceContains("HorizontalScrollBarVisibility=\"Disabled\"")),
        ("Advanced scroll 03 outer uses physical scrolling", () => SourceContains("CanContentScroll=\"False\"")),
        ("Advanced scroll 04 DataGrid forwards only at boundary", () => SourceContains("NestedScrollViewerBehavior.ForwardAtBoundary=\"True\"")),
        ("Advanced scroll 05 Wide height contract", () => AssertHeight(1600, 520, 640, 760)),
        ("Advanced scroll 06 Standard height contract", () => AssertHeight(1120, 460, 560, 680)),
        ("Advanced scroll 07 Compact height contract", () => AssertHeight(800, 420, 500, 600)),
        ("Advanced scroll 08 Narrow height contract", () => AssertHeight(600, 380, 460, 540)),
        ("Advanced scroll 09 top half-DIP forwards up", () => TestSupport.True(NestedScrollViewerBehavior.ShouldForwardAtBoundary(0.5, 40, 120, false), "top threshold")),
        ("Advanced scroll 10 beyond top threshold stays inner", () => TestSupport.False(NestedScrollViewerBehavior.ShouldForwardAtBoundary(0.51, 40, 120, false), "top interior")),
        ("Advanced scroll 11 bottom half-DIP forwards down", () => TestSupport.True(NestedScrollViewerBehavior.ShouldForwardAtBoundary(39.5, 40, -120, false), "bottom threshold")),
        ("Advanced scroll 12 before bottom threshold stays inner", () => TestSupport.False(NestedScrollViewerBehavior.ShouldForwardAtBoundary(39.49, 40, -120, false), "bottom interior")),
        ("Advanced scroll 13 runtime 1600x900 owns both ranges", () => AssertRuntimeLayout(1600, 900, 520)),
        ("Advanced scroll 14 runtime 1120x720 owns both ranges", () => AssertRuntimeLayout(1120, 720, 420)),
        ("Advanced scroll 15 runtime boundary moves nearest outer", BoundaryMovesNearestOuter)
    ];

    private static void AssertHeight(double width, double minimum, double height, double maximum)
    {
        (double actualMinimum, double actualHeight, double actualMaximum) =
            TraceworkAdvancedSensorsLayout.ResolveDataGridHeight(width);
        TestSupport.Equal(minimum, actualMinimum, "minimum");
        TestSupport.Equal(height, actualHeight, "height");
        TestSupport.Equal(maximum, actualMaximum, "maximum");
    }

    private static void AssertRuntimeLayout(double width, double height, double minimumGridHeight) =>
        WithLayout(width, height, (layout, outer, grid, inner) =>
        {
            TestSupport.True(grid.ActualHeight >= minimumGridHeight, "DataGrid actual height");
            TestSupport.True(outer.ScrollableHeight > 0d, "outer scroll range");
            TestSupport.True(inner.ScrollableHeight > 0d, "inner scroll range");
            TestSupport.False(ReferenceEquals(outer, inner), "separate scroll owners");
        });

    private static void BoundaryMovesNearestOuter() =>
        WithLayout(1120, 720, (layout, outer, grid, inner) =>
        {
            inner.ScrollToEnd();
            outer.ScrollToTop();
            layout.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            double before = outer.VerticalOffset;
            MouseWheelEventArgs wheel = new(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
                Source = grid
            };
            grid.RaiseEvent(wheel);
            layout.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            TestSupport.True(wheel.Handled, "boundary event handled once");
            TestSupport.True(outer.VerticalOffset > before, "nearest outer moved down");
            TestSupport.True(inner.VerticalOffset >= inner.ScrollableHeight - 0.5d, "inner remains at bottom");
        });

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

    private static void SourceContains(string value) =>
        TestSupport.True(
            TraceworkPilotSource.Read("HardwareVision", "Views", "AdvancedSensors", "TraceworkAdvancedSensorsLayout.xaml")
                .Contains(value, StringComparison.Ordinal),
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
