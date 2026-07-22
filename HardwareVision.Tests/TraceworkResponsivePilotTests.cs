using System.Windows;
using System.Windows.Controls;
using HardwareVision.Controls;
using HardwareVision.Views.Cpu;
using HardwareVision.Views.Dashboard;

namespace HardwareVision.Tests;

internal static class TraceworkResponsivePilotTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Responsive pilot 01 1600 is Wide 12-column", () => AssertMode(1600d, TraceworkResponsiveMode.Wide, 12)),
        ("Responsive pilot 02 1366 is Wide 12-column", () => AssertMode(1366d, TraceworkResponsiveMode.Wide, 12)),
        ("Responsive pilot 03 1100 is Standard 8-column", () => AssertMode(1100d, TraceworkResponsiveMode.Standard, 8)),
        ("Responsive pilot 04 920 is Compact 4-column", () => AssertMode(920d, TraceworkResponsiveMode.Compact, 4)),
        ("Responsive pilot 05 679 is Narrow 1-column", () => AssertMode(679d, TraceworkResponsiveMode.Narrow, 1)),
        ("Responsive pilot 06 exact 1360 enters Wide", () => TestSupport.Equal(TraceworkResponsiveMode.Wide, TraceworkResponsiveGrid.ResolveMode(1360d), "1360 mode")),
        ("Responsive pilot 07 exact 960 enters Standard", () => TestSupport.Equal(TraceworkResponsiveMode.Standard, TraceworkResponsiveGrid.ResolveMode(960d), "960 mode")),
        ("Responsive pilot 08 exact 680 enters Compact", () => TestSupport.Equal(TraceworkResponsiveMode.Compact, TraceworkResponsiveGrid.ResolveMode(680d), "680 mode")),
        ("Responsive pilot 09 wide 7/5 geometry has no overflow", () => AssertSplit(1600d, 7, 5, 7)),
        ("Responsive pilot 10 standard 5/3 geometry has no overflow", () => AssertSplit(1100d, 5, 3, 5)),
        ("Responsive pilot 11 compact regions stack", () => AssertStacked(920d, TraceworkResponsiveMode.Compact)),
        ("Responsive pilot 12 narrow regions stack", () => AssertStacked(679d, TraceworkResponsiveMode.Narrow)),
        ("Responsive pilot 13 Dashboard 1600 internal width", () => AssertPageGrid<TraceworkDashboardLayout>("DashboardEditorialGrid", 1600d)),
        ("Responsive pilot 14 Dashboard 1366 internal width", () => AssertPageGrid<TraceworkDashboardLayout>("DashboardEditorialGrid", 1366d)),
        ("Responsive pilot 15 Dashboard 1100 internal width", () => AssertPageGrid<TraceworkDashboardLayout>("DashboardEditorialGrid", 1100d)),
        ("Responsive pilot 16 Dashboard 920 internal width", () => AssertPageGrid<TraceworkDashboardLayout>("DashboardEditorialGrid", 920d)),
        ("Responsive pilot 17 Dashboard 679 internal width", () => AssertPageGrid<TraceworkDashboardLayout>("DashboardEditorialGrid", 679d)),
        ("Responsive pilot 18 CPU 1600 internal width", () => AssertPageGrid<TraceworkCpuLayout>("CpuTelemetryGrid", 1600d)),
        ("Responsive pilot 19 CPU 1366 internal width", () => AssertPageGrid<TraceworkCpuLayout>("CpuTelemetryGrid", 1366d)),
        ("Responsive pilot 20 CPU 1100 internal width", () => AssertPageGrid<TraceworkCpuLayout>("CpuTelemetryGrid", 1100d)),
        ("Responsive pilot 21 CPU 920 internal width", () => AssertPageGrid<TraceworkCpuLayout>("CpuTelemetryGrid", 920d)),
        ("Responsive pilot 22 CPU 679 internal width", () => AssertPageGrid<TraceworkCpuLayout>("CpuTelemetryGrid", 679d))
    ];

    private static void AssertMode(double width, TraceworkResponsiveMode expectedMode, int expectedColumns)
    {
        TraceworkResponsiveGrid grid = NewGrid(out _, out _);
        Layout(grid, width);
        TestSupport.Equal(expectedMode, grid.CurrentMode, $"mode at {width}");
        TestSupport.Equal(expectedColumns, grid.ColumnCount, $"columns at {width}");
        foreach (UIElement child in grid.Children)
            TestSupport.True(child.RenderSize.Width <= width + 0.001d, $"child width at {width}");
    }

    private static void AssertSplit(double width, int firstSpan, int secondSpan, int secondColumn)
    {
        TraceworkResponsiveGrid grid = NewGrid(out Border first, out Border second);
        TraceworkResponsiveGrid.SetWideColumnSpan(first, firstSpan);
        TraceworkResponsiveGrid.SetWideColumn(second, secondColumn);
        TraceworkResponsiveGrid.SetWideColumnSpan(second, secondSpan);
        TraceworkResponsiveGrid.SetStandardColumnSpan(first, firstSpan);
        TraceworkResponsiveGrid.SetStandardColumn(second, secondColumn);
        TraceworkResponsiveGrid.SetStandardColumnSpan(second, secondSpan);
        Layout(grid, width);
        Point secondOrigin = second.TranslatePoint(new Point(), grid);
        TestSupport.True(secondOrigin.X >= first.RenderSize.Width, "second region begins after first");
        TestSupport.True(secondOrigin.X + second.RenderSize.Width <= width + 0.001d, "split does not overflow");
    }

    private static void AssertStacked(double width, TraceworkResponsiveMode mode)
    {
        TraceworkResponsiveGrid grid = NewGrid(out Border first, out Border second);
        Layout(grid, width);
        TestSupport.Equal(mode, grid.CurrentMode, "stack mode");
        Point secondOrigin = second.TranslatePoint(new Point(), grid);
        TestSupport.True(secondOrigin.Y >= first.RenderSize.Height, "second region stacks below first");
        TestSupport.True(first.RenderSize.Width <= width + 0.001d && second.RenderSize.Width <= width + 0.001d, "stack widths do not overflow");
    }

    private static TraceworkResponsiveGrid NewGrid(out Border first, out Border second)
    {
        TraceworkResponsiveGrid grid = new() { ColumnGap = 18d, RowGap = 24d };
        first = new Border { Height = 100d };
        second = new Border { Height = 80d };
        TraceworkResponsiveGrid.SetWideColumnSpan(first, 7);
        TraceworkResponsiveGrid.SetWideColumn(second, 7);
        TraceworkResponsiveGrid.SetWideColumnSpan(second, 5);
        TraceworkResponsiveGrid.SetStandardColumnSpan(first, 5);
        TraceworkResponsiveGrid.SetStandardColumn(second, 5);
        TraceworkResponsiveGrid.SetStandardColumnSpan(second, 3);
        TraceworkResponsiveGrid.SetCompactColumnSpan(first, 4);
        TraceworkResponsiveGrid.SetCompactColumnSpan(second, 4);
        TraceworkResponsiveGrid.SetCompactRow(second, 1);
        TraceworkResponsiveGrid.SetNarrowRow(second, 1);
        grid.Children.Add(first);
        grid.Children.Add(second);
        return grid;
    }

    private static void Layout(TraceworkResponsiveGrid grid, double width)
    {
        grid.Measure(new Size(width, double.PositiveInfinity));
        grid.Arrange(new Rect(0d, 0d, width, grid.DesiredSize.Height));
        grid.UpdateLayout();
    }

    private static void AssertPageGrid<TPage>(string gridName, double internalWidth) where TPage : FrameworkElement, new()
    {
        EnsureApplication();
        TPage page = new();
        TraceworkResponsiveGrid grid = TestSupport.NotNull(page.FindName(gridName) as TraceworkResponsiveGrid, gridName);
        Layout(grid, internalWidth);
        TestSupport.Equal(TraceworkResponsiveGrid.ResolveMode(internalWidth), grid.CurrentMode, $"{gridName} mode at {internalWidth}");
        foreach (UIElement child in grid.Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            Point origin = child.TranslatePoint(new Point(), grid);
            TestSupport.True(origin.X >= -0.001d, $"{gridName} left bound at {internalWidth}");
            TestSupport.True(origin.X + child.RenderSize.Width <= internalWidth + 0.001d, $"{gridName} right bound at {internalWidth}");
            TestSupport.True(child.RenderSize.Width > 0d, $"{gridName} positive region width at {internalWidth}");
        }
    }

    private static void EnsureApplication()
    {
        if (Application.Current is not null) return;
        HardwareVision.App app = new();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }
}
