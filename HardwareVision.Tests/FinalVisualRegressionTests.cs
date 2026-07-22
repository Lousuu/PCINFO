using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.ViewModels;
using HardwareVision.Views.Dashboard;
using HardwareVision.Views.GameSessionReport;
using HardwareVision.Views.Gpu;

namespace HardwareVision.Tests;

internal static class FinalVisualRegressionTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Final regression 01 periodic sensors keep stable status", SensorPeriodicStatusIsStable),
        ("Final regression 02 sensor throttle is unchanged", () => AdvancedContains("TimeSpan.FromSeconds(3)")),
        ("Final regression 03 sensor cap is unchanged", () => AdvancedContains("MaxVisibleRows = 500")),
        ("Final regression 04 sensor reconciliation remains O(n)", () => AdvancedContains("existingById", "AdvancedSensorRowReconciler.Apply")),
        ("Final regression 05 diagnosis uses compact style", () => ReportContains("TraceworkDiagnosisSummaryPanelStyle")),
        ("Final regression 06 diagnosis style clears minimum", () => GameStylesContain("x:Key=\"TraceworkDiagnosisSummaryPanelStyle\"", "Property=\"MinHeight\" Value=\"0\"")),
        ("Final regression 07 diagnosis style is Auto", () => GameStylesContain("Property=\"Height\" Value=\"Auto\"")),
        ("Final regression 08 diagnosis content is top aligned", () => ReportContains("VerticalContentAlignment=\"Top\"", "HorizontalContentAlignment=\"Stretch\"")),
        ("Final regression 09 diagnosis runtime height is compact", DiagnosisRuntimeHeightIsCompact),
        ("Final regression 10 GPU selector has no inner columns", GpuSelectorHasNoColumns),
        ("Final regression 11 GPU selector uses full-width style", () => GpuContains("TraceworkGpuSelectorFullWidthStyle")),
        ("Final regression 12 GPU selector style has no visual max", GpuStyleHasNoMaxWidth),
        ("Final regression 13 GPU selector stack aligns at runtime", GpuSelectorAlignsAtRuntime),
        ("Final regression 14 GPU selector stretches at runtime", GpuSelectorStretchesAtRuntime),
        ("Final regression 15 Dashboard final row is Auto", DashboardFinalRowIsAuto),
        ("Final regression 16 Dashboard primary uses compact style", () => DashboardContains("TraceworkDashboardPrimaryInstrumentStyle")),
        ("Final regression 17 Dashboard primary is top aligned", () => DashboardContains("x:Name=\"DashboardPrimaryRegion\"", "VerticalAlignment=\"Top\"", "Height=\"Auto\"")),
        ("Final regression 18 Dashboard items are top aligned", () => DashboardContains("VerticalAlignment=\"Top\"", "ItemsSource=\"{Binding SecondaryMetrics}\"")),
        ("Final regression 19 Dashboard primary is content sized at runtime", DashboardPrimaryIsContentSized),
        ("Final regression 20 Classic Dashboard remains untouched", ClassicDashboardUntouched),
        ("Final regression 21 report commands remain", () => ReportContains("BackCommand", "OpenDirectoryCommand", "ExportPlainCsvCommand")),
        ("Final regression 22 report nested scrolling remains", () => ReportContains("NestedScrollViewerBehavior.BubbleMouseWheelAtBoundary=\"True\"")),
        ("Final regression 23 GPU binding remains two-way", () => GpuContains("SelectedGpu, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged")),
        ("Final regression 24 GPU TextSearch remains", () => GpuContains("TextSearch.TextPath=\"Name\"")),
        ("Final regression 25 native report fixture uses fixed time", ReportFixtureUsesFixedTime),
        ("Final regression 26 native report fixture has monotonic timestamps", () => AccuracyContains("started.AddSeconds(5)", "started.AddSeconds(10)")),
        ("Final regression 27 report tolerance is not widened", () => AccuracyContains("last native elapsed time")),
        ("Final regression 28 report test is not skipped", () => AccuracyContains("Report accuracy 01 native elapsed timestamp aligns frames")),
        ("Final regression 29 Compact GPU order remains", CompactGpuOrderRemains),
        ("Final regression 30 report columns remain independent", () => ReportContains("x:Name=\"SessionLeftColumn\"", "x:Name=\"SessionRightColumn\""))
    ];

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
    private static string Advanced => Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs");
    private static string Report => Read("HardwareVision", "Views", "GameSessionReport", "TraceworkGameSessionReportLayout.xaml");
    private static string GameStyles => Read("HardwareVision", "Themes", "Tracework", "GamePages.xaml");
    private static string Gpu => Read("HardwareVision", "Views", "Gpu", "TraceworkGpuLayout.xaml");
    private static string Pages => Read("HardwareVision", "Themes", "Tracework", "Pages.xaml");
    private static string Dashboard => Read("HardwareVision", "Views", "Dashboard", "TraceworkDashboardLayout.xaml");
    private static string Accuracy => Read("HardwareVision.Tests", "SessionReportAccuracyTests.cs");
    private static void AdvancedContains(params string[] values) => Contains(Advanced, values);
    private static void ReportContains(params string[] values) => Contains(Report, values);
    private static void GameStylesContain(params string[] values) => Contains(GameStyles, values);
    private static void GpuContains(params string[] values) => Contains(Gpu, values);
    private static void DashboardContains(params string[] values) => Contains(Dashboard, values);
    private static void AccuracyContains(params string[] values) => Contains(Accuracy, values);

    private static void Contains(string source, params string[] values)
    {
        foreach (string value in values) TestSupport.True(source.Contains(value, StringComparison.Ordinal), value);
    }

    private static void SensorPeriodicStatusIsStable()
    {
        int start = Advanced.IndexOf("private void QueueApplyReadings", StringComparison.Ordinal);
        int end = Advanced.IndexOf("private async Task ApplyReadingsAsync", start, StringComparison.Ordinal);
        string method = Advanced[start..end];
        TestSupport.True(method.Contains("if (SensorRows.Count == 0)", StringComparison.Ordinal), "initial-only organizing status");
        TestSupport.Equal(1, TraceworkPilotSource.Count(method, "正在整理"), "organizing status writes");
    }

    private static void GpuSelectorHasNoColumns()
    {
        string selector = Slice(Gpu, "<Border Style=\"{DynamicResource TraceworkIdentityPlateStyle}\">", "<controls:TraceworkPanel x:Name=\"GpuWideDeviceIdentity\"");
        TestSupport.False(selector.Contains("ColumnDefinition", StringComparison.Ordinal), "selector columns");
        TestSupport.True(selector.Contains("<StackPanel>", StringComparison.Ordinal), "selector stack");
    }

    private static void GpuStyleHasNoMaxWidth()
    {
        string style = Slice(Pages, "x:Key=\"TraceworkGpuSelectorFullWidthStyle\"", "x:Key=\"TraceworkDashboardPrimaryInstrumentStyle\"");
        TestSupport.False(style.Contains("MaxWidth", StringComparison.Ordinal), "selector MaxWidth");
        TestSupport.False(style.Contains("TraceworkGpuSelectorStyle", StringComparison.Ordinal), "selector limited base");
    }

    private static void DashboardFinalRowIsAuto()
    {
        string template = Slice(Dashboard, "x:Key=\"DashboardInstrumentTemplate\"", "x:Key=\"DashboardTelemetryModuleTemplate\"");
        TestSupport.False(template.Contains("RowDefinition Height=\"*\"", StringComparison.Ordinal), "star row");
        TestSupport.True(TraceworkPilotSource.Count(template, "RowDefinition Height=\"Auto\"") >= 4, "Auto rows");
    }

    private static void ReportFixtureUsesFixedTime()
    {
        string method = Slice(Accuracy, "private static Task NativeElapsedTimestampAlignsAsync", "private static Task WallClockTimestampAlignsAsync");
        TestSupport.True(method.Contains("new(2026, 1, 1", StringComparison.Ordinal), "fixed timestamp");
        TestSupport.False(method.Contains("UtcNow", StringComparison.Ordinal), "live timestamp");
    }

    private static void CompactGpuOrderRemains()
    {
        int selector = Gpu.IndexOf("x:Name=\"GpuSecondaryColumn\"", StringComparison.Ordinal);
        int telemetry = Gpu.IndexOf("x:Name=\"GpuPrimaryColumn\"", StringComparison.Ordinal);
        int compactIdentity = Gpu.IndexOf("x:Name=\"GpuCompactDeviceIdentity\"", StringComparison.Ordinal);
        int sensors = Gpu.IndexOf("x:Name=\"GpuSensorDataGrid\"", StringComparison.Ordinal);
        TestSupport.True(selector >= 0 && telemetry > selector && compactIdentity > telemetry && sensors > compactIdentity, "GPU compact source order");
    }

    private static void DiagnosisRuntimeHeightIsCompact()
    {
        TraceworkGameSessionReportLayout layout = new() { DataContext = new object() };
        WithHosted(layout, new Size(1600, 900), () =>
        {
            TraceworkPanel summary = FindVisualDescendants<TraceworkPanel>(layout)
                .First(panel => panel.Code == "RPT.10");
            TestSupport.True(summary.ActualHeight is >= 80d and <= 200d, $"summary height {summary.ActualHeight}");
            TestSupport.Equal(0d, summary.MinHeight, "summary MinHeight");
            TestSupport.Equal(VerticalAlignment.Top, summary.VerticalAlignment, "summary alignment");
        });
    }

    private static void GpuSelectorAlignsAtRuntime()
    {
        TraceworkGpuLayout layout = new() { DataContext = new GpuViewModel() };
        WithHosted(layout, new Size(1366, 900), () =>
        {
            FrameworkElement container = Named<FrameworkElement>(layout, "GpuSecondaryColumn");
            FrameworkElement[] elements =
            [
                Named<FrameworkElement>(layout, "GpuSelectorTitle"),
                Named<FrameworkElement>(layout, "GpuSelectorDescription"),
                Named<FrameworkElement>(layout, "GpuDeviceSelector"),
                Named<FrameworkElement>(layout, "GpuSelectorHint")
            ];
            double[] lefts = elements.Select(element => element.TranslatePoint(new Point(), container).X).ToArray();
            TestSupport.True(lefts.Max() - lefts.Min() <= 1d, $"selector lefts {string.Join(",", lefts)}");
        });
    }

    private static void GpuSelectorStretchesAtRuntime()
    {
        TraceworkGpuLayout layout = new() { DataContext = new GpuViewModel() };
        WithHosted(layout, new Size(920, 760), () =>
        {
            ComboBox selector = Named<ComboBox>(layout, "GpuDeviceSelector");
            FrameworkElement title = Named<FrameworkElement>(layout, "GpuSelectorTitle");
            FrameworkElement plate = (FrameworkElement)VisualTreeHelper.GetParent(title);
            TestSupport.True(selector.ActualWidth >= plate.ActualWidth - 1d, $"selector width {selector.ActualWidth}/{plate.ActualWidth}");
            TestSupport.True(selector.ActualWidth <= 920d, "narrow overflow");
        });
    }

    private static void DashboardPrimaryIsContentSized()
    {
        DashboardFixture fixture = new();
        TraceworkDashboardLayout layout = new() { DataContext = fixture };
        WithHosted(layout, new Size(1600, 900), () =>
        {
            Border primary = Named<Border>(layout, "DashboardPrimaryRegion");
            StackPanel secondary = Named<StackPanel>(layout, "DashboardSecondaryRegion");
            TestSupport.True(primary.ActualHeight < secondary.ActualHeight, $"primary/secondary {primary.ActualHeight}/{secondary.ActualHeight}");
            TestSupport.Equal(VerticalAlignment.Top, primary.VerticalAlignment, "primary top");
        });
    }

    private static void ClassicDashboardUntouched()
    {
        string classic = Read("HardwareVision", "Views", "Dashboard", "ClassicDashboardLayout.xaml");
        TestSupport.False(classic.Contains("TraceworkDashboardPrimaryInstrumentStyle", StringComparison.Ordinal), "Classic Tracework style");
    }

    private static string Slice(string source, string startToken, string endToken)
    {
        int start = source.IndexOf(startToken, StringComparison.Ordinal);
        int end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        TestSupport.True(start >= 0 && end > start, $"slice {startToken}");
        return source[start..end];
    }

    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        TestSupport.NotNull(root.FindName(name) as T, name);

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void WithHosted(UserControl view, Size size, Action assertion)
    {
        EnsureApplication();
        Window host = new()
        {
            Content = view,
            Width = size.Width,
            Height = size.Height,
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
            host.Measure(size);
            host.Arrange(new Rect(new Point(), size));
            host.UpdateLayout();
            view.UpdateLayout();
            assertion();
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static void EnsureApplication()
    {
        if (System.Windows.Application.Current is not null) return;
        HardwareVision.App app = new();
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private sealed class DashboardFixture
    {
        public DashboardFixture()
        {
            CpuOverviewCard = Card(HardwareOverviewKind.Cpu, 4);
            GpuOverviewCard = Card(HardwareOverviewKind.Gpu, 10);
            MemoryOverviewCard = Card(HardwareOverviewKind.Memory, 8);
            DiskOverviewCard = Card(HardwareOverviewKind.Disk, 8);
            NetworkOverviewCard = Card(HardwareOverviewKind.Network, 6);
            SystemOverviewCard = Card(HardwareOverviewKind.System, 6);
        }

        public HardwareOverviewCardViewModel CpuOverviewCard { get; }
        public HardwareOverviewCardViewModel GpuOverviewCard { get; }
        public HardwareOverviewCardViewModel MemoryOverviewCard { get; }
        public HardwareOverviewCardViewModel DiskOverviewCard { get; }
        public HardwareOverviewCardViewModel NetworkOverviewCard { get; }
        public HardwareOverviewCardViewModel SystemOverviewCard { get; }
        public string ApplicationName => "HardwareVision.Tests";
        public string LastRefreshTime => "fixed";
        public string LoadMessage => "ready";
        public string DeviceName => "Synthetic node";
        public string OperatingSystem => "Windows";

        private static HardwareOverviewCardViewModel Card(HardwareOverviewKind kind, int count)
        {
            HardwareOverviewCardViewModel card = new(kind)
            {
                Title = kind.ToString(),
                HardwareName = $"Synthetic {kind}",
                IsVisible = true
            };
            for (int index = 0; index < count; index++)
            {
                card.Metrics.Add(new DetailMetricViewModel($"Metric {index}", $"{index + 1}"));
            }
            return card;
        }
    }
}
