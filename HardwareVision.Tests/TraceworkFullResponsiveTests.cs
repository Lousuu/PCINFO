using System.Windows;
using HardwareVision.Controls;
using HardwareVision.Views.AdvancedSensors;
using HardwareVision.Views.Disk;
using HardwareVision.Views.GamePerformance;
using HardwareVision.Views.GameSessionReport;
using HardwareVision.Views.Gpu;
using HardwareVision.Views.Memory;
using HardwareVision.Views.MetricVisibility;
using HardwareVision.Views.Motherboard;
using HardwareVision.Views.Network;
using HardwareVision.Views.Settings;

namespace HardwareVision.Tests;

internal static class TraceworkFullResponsiveTests
{
    private sealed record PageFactory(string Name, string Grid, Func<FrameworkElement> Create);

    private static readonly PageFactory[] Pages =
    [
        new("GPU", "GpuRenderPipelineGrid", () => new TraceworkGpuLayout()),
        new("Memory", "MemoryTopologyGrid", () => new TraceworkMemoryLayout()),
        new("Disk", "StorageHealthGrid", () => new TraceworkDiskLayout()),
        new("Network", "NetworkLinkTelemetryGrid", () => new TraceworkNetworkLayout()),
        new("Motherboard", "PlatformIdentityGrid", () => new TraceworkMotherboardLayout()),
        new("Advanced Sensors", "AdvancedSensorMatrixGrid", () => new TraceworkAdvancedSensorsLayout()),
        new("Game Performance", "GameCaptureControlGrid", () => new TraceworkGamePerformanceLayout()),
        new("Session Report", "SessionDiagnosisGrid", () => new TraceworkGameSessionReportLayout()),
        new("Settings", "SettingsWorkspaceGrid", () => new TraceworkSettingsLayout()),
        new("Metric Visibility", "MetricRoutingGrid", () => new TraceworkMetricVisibilityLayout())
    ];

    private static readonly double[] Widths = [1600d, 1366d, 1100d, 920d, 679d];

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        foreach (PageFactory factory in Pages)
        foreach (double width in Widths)
        {
            PageFactory page = factory;
            double contentWidth = width;
            tests.Add(($"Full responsive {page.Name} {contentWidth:0}", () => AssertPage(page, contentWidth)));
        }
        return tests;
    }

    private static void AssertPage(PageFactory factory, double width)
    {
        EnsureApplication();
        FrameworkElement page = factory.Create();
        TraceworkResponsiveGrid grid = TestSupport.NotNull(page.FindName(factory.Grid) as TraceworkResponsiveGrid, factory.Grid);
        grid.Measure(new Size(width, double.PositiveInfinity));
        grid.Arrange(new Rect(0d, 0d, width, Math.Max(1d, grid.DesiredSize.Height)));
        grid.UpdateLayout();
        TestSupport.Equal(TraceworkResponsiveGrid.ResolveMode(width), grid.CurrentMode, $"{factory.Name} mode");
        TestSupport.True(grid.RenderSize.Width > 0d && grid.RenderSize.Height > 0d, $"{factory.Name} positive size");
        foreach (UIElement child in grid.Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            Point origin = child.TranslatePoint(new Point(), grid);
            TestSupport.True(origin.X >= -0.001d, $"{factory.Name} left bound");
            TestSupport.True(origin.X + child.RenderSize.Width <= width + 0.001d, $"{factory.Name} horizontal overflow");
            TestSupport.True(child.RenderSize.Width > 0d, $"{factory.Name} child width");
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
