using System.Windows;
using System.Windows.Controls;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionTelemetryChartTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Telemetry chart 01 same model reuses geometry", SameModelReusesGeometry),
        ("Telemetry chart 02 model change invalidates geometry", ModelChangeInvalidatesGeometry),
        ("Telemetry chart 03 size change invalidates geometry", SizeChangeInvalidatesGeometry),
        ("Telemetry chart 04 1000 redraw diagnostics allocate minimally", ThousandRedrawDiagnostics),
        ("Telemetry chart 05 binary interval hit finds event", BinaryIntervalHitFindsEvent),
        ("Telemetry chart 06 1500 points and 200 intervals stay bounded", PointsAndIntervalsStayBounded),
        ("Telemetry chart 07 event list XAML enables recycling", EventListXamlEnablesRecycling),
        ("Telemetry chart 08 2000-event source stays virtualized", TwoThousandEventSourceStaysVirtualized)
    ];

    private static void SameModelReusesGeometry()
    {
        SessionTelemetryChart chart = new();
        SessionChartModel model = Model();
        chart.BuildGeometryForDiagnostics(model, new Size(800, 300));
        int first = chart.GeometryBuildCount;
        chart.BuildGeometryForDiagnostics(model, new Size(800, 300));
        TestSupport.Equal(first, chart.GeometryBuildCount, "geometry was rebuilt for identical model and size");
    }

    private static void ModelChangeInvalidatesGeometry()
    {
        SessionTelemetryChart chart = new();
        chart.BuildGeometryForDiagnostics(Model(1d), new Size(800, 300));
        int first = chart.GeometryBuildCount;
        chart.BuildGeometryForDiagnostics(Model(2d), new Size(800, 300));
        TestSupport.True(chart.GeometryBuildCount > first, "new model did not rebuild geometry");
    }

    private static void SizeChangeInvalidatesGeometry()
    {
        SessionTelemetryChart chart = new();
        SessionChartModel model = Model();
        chart.BuildGeometryForDiagnostics(model, new Size(800, 300));
        int first = chart.GeometryBuildCount;
        chart.BuildGeometryForDiagnostics(model, new Size(900, 300));
        TestSupport.True(chart.GeometryBuildCount > first, "size change did not rebuild geometry");
    }

    private static void ThousandRedrawDiagnostics()
    {
        SessionTelemetryChart chart = new();
        SessionChartModel model = Model();
        Measurement measurement = TestSupport.Measure("chart-1000-cached-geometry-requests", () =>
        {
            for (int index = 0; index < 1000; index++)
                chart.BuildGeometryForDiagnostics(model, new Size(800, 300));
        });
        TestSupport.Equal(1, chart.GeometryBuildCount, "cached geometry build count");
        TestSupport.True(measurement.AllocatedBytes < 2_000_000, "cached geometry requests allocated unexpectedly heavily");
    }

    private static void BinaryIntervalHitFindsEvent()
    {
        List<SessionLimitInterval> intervals = Enumerable.Range(0, 200)
            .Select(index => new SessionLimitInterval
            {
                EventId = index + 1,
                StartSeconds = index * 2d,
                EndSeconds = index * 2d + 1d
            })
            .ToList();
        int hit = SessionTelemetryChart.FindIntervalForDiagnostics(intervals, 200.5d);
        TestSupport.Equal(100, hit, "binary interval hit index");
        TestSupport.Equal(-1, SessionTelemetryChart.FindIntervalForDiagnostics(intervals, 202d + 1.5d), "interval miss");
    }

    private static void PointsAndIntervalsStayBounded()
    {
        List<SessionChartPoint> source = Enumerable.Range(0, 20_000)
            .Select(index => new SessionChartPoint(index * 0.1d, 1000d + Math.Sin(index) * 200d))
            .ToList();
        List<SessionLimitInterval> intervals = Enumerable.Range(0, 200)
            .Select(index => new SessionLimitInterval
            {
                EventId = index + 1,
                StartSeconds = index * 5d,
                EndSeconds = index * 5d + 0.5d
            })
            .ToList();
        IReadOnlyList<SessionChartPoint> displayed = SessionChartDownsampler.Downsample(source, intervals, 1500);
        SessionTelemetryChart chart = new();
        SessionChartModel model = new()
        {
            Key = "stress",
            Title = "stress",
            DurationSeconds = 2000d,
            LimitIntervals = intervals,
            Series = [new SessionChartSeries { Name = "clock", Unit = "MHz", Points = displayed }]
        };
        chart.BuildGeometryForDiagnostics(model, new Size(1200, 400));
        Console.WriteLine($"MEASURE chart-model: sourcePoints={source.Count}; retainedPoints={displayed.Count}; intervals={intervals.Count}; geometryBuilds={chart.GeometryBuildCount}");
        TestSupport.True(displayed.Count <= 1500, "chart point cap exceeded");
        TestSupport.Equal(1, chart.GeometryBuildCount, "stress model geometry count");
    }

    private static void EventListXamlEnablesRecycling()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "HardwareVision", "Views", "GameSessionReportView.xaml");
        string xaml = File.ReadAllText(path);
        TestSupport.True(xaml.Contains("<ListBox", StringComparison.Ordinal), "event list is not a ListBox");
        TestSupport.True(xaml.Contains("VirtualizingStackPanel.IsVirtualizing=\"True\"", StringComparison.Ordinal), "virtualization missing");
        TestSupport.True(xaml.Contains("VirtualizingStackPanel.VirtualizationMode=\"Recycling\"", StringComparison.Ordinal), "recycling missing");
        TestSupport.True(xaml.Contains("ScrollViewer.CanContentScroll=\"True\"", StringComparison.Ordinal), "content scrolling missing");
        TestSupport.True(xaml.Contains("MaxHeight=\"420\"", StringComparison.Ordinal), "bounded event-list height missing");
    }

    private static void TwoThousandEventSourceStaysVirtualized()
    {
        List<GamePerformanceLimitEvent> events = Enumerable.Range(0, 2000)
            .Select(index => new GamePerformanceLimitEvent { EventId = index + 1 })
            .ToList();
        ListBox list = new()
        {
            ItemsSource = events,
            MaxHeight = 420d
        };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(list, true);
        list.Measure(new Size(1000d, 420d));
        list.Arrange(new Rect(0d, 0d, 1000d, 420d));
        list.UpdateLayout();
        int generated = 0;
        for (int index = 0; index < events.Count; index++)
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not null) generated++;
        Console.WriteLine($"MEASURE virtualized-events: sourceItems={events.Count}; generatedContainers={generated}");
        TestSupport.True(generated < events.Count, "all 2000 event containers were generated");
        list.ItemsSource = null;
        list.ToolTip = null;
    }

    private static SessionChartModel Model(double offset = 0d) => new()
    {
        Key = "model-" + offset,
        Title = "model",
        DurationSeconds = 10d,
        Series =
        [
            new SessionChartSeries
            {
                Name = "frequency",
                Unit = "MHz",
                Points =
                [
                    new SessionChartPoint(0d, 1000d + offset),
                    new SessionChartPoint(5d, 1500d + offset),
                    new SessionChartPoint(10d, 1200d + offset)
                ]
            }
        ]
    };
}
