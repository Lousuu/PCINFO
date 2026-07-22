using System.Reflection;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views.Cpu;
using HardwareVision.Views.Gpu;

namespace HardwareVision.Tests;

internal static class TraceworkProcessorPageTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Processor pages 01 visible projection preserves order and references", VisibleProjectionPreservesOrderAndReferences),
        ("Processor pages 02 visible projection responds to visibility changes", VisibleProjectionRespondsToVisibilityChanges),
        ("Processor pages 03 CPU state objects remain stable", CpuStateObjectsRemainStable),
        ("Processor pages 04 GPU state objects and selection remain stable", GpuStateObjectsAndSelectionRemainStable),
        ("Processor pages 05 GPU selection keeps existing preferred setting path", GpuSelectionKeepsExistingPreferredSettingPath),
        ("Processor pages 06 view models have no theme service dependency", ViewModelsHaveNoThemeServiceDependency),
        ("Processor pages 07 layouts are presentation-only controls", LayoutsArePresentationOnlyControls),
        ("Processor pages 08 architecture source checks", ArchitectureSourceChecks)
    ];

    private static void VisibleProjectionPreservesOrderAndReferences()
    {
        System.Collections.ObjectModel.ObservableCollection<DetailMetricViewModel> metrics =
        [
            Metric("Primary", true),
            Metric("Hidden", false),
            Metric("Secondary", true),
            Metric("Tertiary", true)
        ];
        VisibleMetricProjection projection = new(metrics);

        TestSupport.Equal(4, metrics.Count, "original metric count");
        TestSupport.Equal(3, projection.VisibleMetricCount, "visible metric count");
        TestSupport.True(ReferenceEquals(metrics[0], projection.PrimaryMetric), "primary metric reference");
        TestSupport.Equal(2, projection.SecondaryMetrics.Count, "secondary metric count");
        TestSupport.True(ReferenceEquals(metrics[2], projection.SecondaryMetrics[0]), "first secondary reference");
        TestSupport.True(ReferenceEquals(metrics[3], projection.SecondaryMetrics[1]), "second secondary reference");
        TestSupport.True(projection.VisibleMetrics.SequenceEqual([metrics[0], metrics[2], metrics[3]]),
            "visible projection order");
    }

    private static void VisibleProjectionRespondsToVisibilityChanges()
    {
        DetailMetricViewModel first = Metric("First", true);
        DetailMetricViewModel second = Metric("Second", false);
        System.Collections.ObjectModel.ObservableCollection<DetailMetricViewModel> metrics = [first, second];
        VisibleMetricProjection projection = new(metrics);
        List<string> notifications = [];
        projection.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                notifications.Add(args.PropertyName!);
            }
        };

        first.IsVisible = false;
        second.IsVisible = true;

        TestSupport.True(ReferenceEquals(second, projection.PrimaryMetric), "updated primary reference");
        TestSupport.Equal(0, projection.SecondaryMetrics.Count, "updated secondary count");
        TestSupport.Equal(1, projection.VisibleMetricCount, "updated visible count");
        TestSupport.True(notifications.Contains(nameof(VisibleMetricProjection.PrimaryMetric)),
            "primary projection notification");
        TestSupport.True(notifications.Contains(nameof(VisibleMetricProjection.SecondaryMetrics)),
            "secondary projection notification");
        TestSupport.True(notifications.Contains(nameof(VisibleMetricProjection.VisibleMetricCount)),
            "count projection notification");

        second.IsVisible = false;
        TestSupport.True(projection.PrimaryMetric is null, "empty projection primary");
        TestSupport.Equal(0, projection.VisibleMetricCount, "empty projection count");
    }

    private static void CpuStateObjectsRemainStable()
    {
        using CpuViewModel viewModel = new();
        object metrics = viewModel.Metrics;
        object rows = viewModel.CoreRows;
        object charts = viewModel.Charts;
        RealtimeMetricChartViewModel[] chartReferences = viewModel.Charts.ToArray();
        viewModel.Metrics.Add(Metric("Package", true));
        viewModel.Metrics.Add(Metric("Power", true));
        viewModel.CoreRows.Add(new DetailSensorRowViewModel());
        viewModel.Charts[0].Append(41d);
        viewModel.SelectedChartWindowSeconds = 120;
        IReadOnlyList<double> values = viewModel.Charts[0].Values;

        _ = viewModel.MetricProjection.PrimaryMetric;
        _ = viewModel.MetricProjection.SecondaryMetrics;

        TestSupport.True(ReferenceEquals(metrics, viewModel.Metrics), "CPU Metrics collection reference");
        TestSupport.True(ReferenceEquals(rows, viewModel.CoreRows), "CPU CoreRows collection reference");
        TestSupport.True(ReferenceEquals(charts, viewModel.Charts), "CPU Charts collection reference");
        TestSupport.True(chartReferences.SequenceEqual(viewModel.Charts), "CPU chart references");
        TestSupport.True(ReferenceEquals(values, viewModel.Charts[0].Values), "CPU chart Values reference");
        TestSupport.Equal(120, viewModel.SelectedChartWindowSeconds, "CPU selected chart window");
        TestSupport.True(viewModel.Charts.All(chart => chart.WindowSeconds == 120), "CPU chart windows");
    }

    private static void GpuStateObjectsAndSelectionRemainStable()
    {
        using GpuViewModel viewModel = new();
        GpuDevice gpu = CreateGpu("gpu-state", "Synthetic GPU State");
        viewModel.GpuDevices.Add(gpu);
        viewModel.SelectedGpu = gpu;
        viewModel.SelectedChartWindowSeconds = 30;

        object devices = viewModel.GpuDevices;
        object metrics = viewModel.Metrics;
        object infoItems = viewModel.InfoItems;
        object sensorRows = viewModel.SensorRows;
        object charts = viewModel.Charts;
        RealtimeMetricChartViewModel[] chartReferences = viewModel.Charts.ToArray();
        IReadOnlyList<double>[] valueReferences = viewModel.Charts.Select(chart => chart.Values).ToArray();
        string? chartGpuKey = typeof(GpuViewModel)
            .GetField("chartGpuKey", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(viewModel) as string;

        _ = viewModel.MetricProjection.VisibleMetrics;
        _ = viewModel.InfoProjection.VisibleMetrics;

        TestSupport.True(ReferenceEquals(devices, viewModel.GpuDevices), "GPU devices collection reference");
        TestSupport.True(ReferenceEquals(metrics, viewModel.Metrics), "GPU Metrics collection reference");
        TestSupport.True(ReferenceEquals(infoItems, viewModel.InfoItems), "GPU InfoItems collection reference");
        TestSupport.True(ReferenceEquals(sensorRows, viewModel.SensorRows), "GPU SensorRows collection reference");
        TestSupport.True(ReferenceEquals(charts, viewModel.Charts), "GPU Charts collection reference");
        TestSupport.True(ReferenceEquals(gpu, viewModel.SelectedGpu), "GPU selection reference");
        TestSupport.True(chartReferences.SequenceEqual(viewModel.Charts), "GPU chart references");
        for (int index = 0; index < valueReferences.Length; index++)
        {
            TestSupport.True(ReferenceEquals(valueReferences[index], viewModel.Charts[index].Values),
                $"GPU chart Values reference {index}");
        }
        TestSupport.Equal(30, viewModel.SelectedChartWindowSeconds, "GPU selected chart window");
        TestSupport.True(viewModel.Charts.All(chart => chart.WindowSeconds == 30), "GPU chart windows");
        TestSupport.Equal(chartGpuKey, typeof(GpuViewModel)
            .GetField("chartGpuKey", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(viewModel) as string, "GPU chart identity key");
    }

    private static void GpuSelectionKeepsExistingPreferredSettingPath()
    {
        AppSettings settings = new();
        CountingSettingsService settingsService = new(settings);
        using PollingService pollingService = new(new CountingSensorService(), settings);
        using SensorHistoryService historyService = new(pollingService);
        using DashboardViewModel dashboard = new(
            settings,
            new EmptyHardwareInfoService(),
            pollingService,
            settingsService,
            System.Windows.Threading.Dispatcher.CurrentDispatcher,
            historyService);
        using GpuViewModel viewModel = new(dashboard, settings, settingsService, historyService);
        GpuDevice gpu = CreateGpu("gpu-preferred", "Synthetic Preferred GPU");
        viewModel.GpuDevices.Add(gpu);
        viewModel.SelectedGpu = gpu;

        TestSupport.Equal(gpu.Id, settings.PreferredGpuId, "persisted preferred GPU id");
        TestSupport.Equal(gpu.Id, dashboard.PreferredGpuId, "Dashboard preferred GPU id");
        TestSupport.True(ReferenceEquals(gpu, viewModel.SelectedGpu), "preferred GPU selection reference");
    }

    private static void ViewModelsHaveNoThemeServiceDependency()
    {
        Type[] viewModelTypes = [typeof(CpuViewModel), typeof(GpuViewModel)];
        foreach (Type type in viewModelTypes)
        {
            bool dependsOnThemeService = type
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => typeof(IThemeService).IsAssignableFrom(parameter.ParameterType));
            TestSupport.False(dependsOnThemeService, $"{type.Name} theme-service constructor dependency");
        }
    }

    private static void LayoutsArePresentationOnlyControls()
    {
        Type[] layoutTypes =
        [
            typeof(ClassicCpuLayout),
            typeof(TraceworkCpuLayout),
            typeof(ClassicGpuLayout),
            typeof(TraceworkGpuLayout)
        ];

        foreach (Type type in layoutTypes)
        {
            ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            TestSupport.Equal(1, constructors.Length, $"{type.Name} constructor count");
            TestSupport.Equal(0, constructors[0].GetParameters().Length, $"{type.Name} constructor parameters");
            TestSupport.Equal(0, type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Count(field => typeof(IThemeService).IsAssignableFrom(field.FieldType)
                    || field.FieldType.Name.EndsWith("ViewModel", StringComparison.Ordinal)),
                $"{type.Name} service or ViewModel fields");
        }
    }

    private static void ArchitectureSourceChecks()
    {
        string repositoryRoot = FindRepositoryRoot();
        string applicationRoot = Path.Combine(repositoryRoot, "HardwareVision");
        string cpuView = File.ReadAllText(Path.Combine(applicationRoot, "Views", "CpuView.xaml"));
        string gpuView = File.ReadAllText(Path.Combine(applicationRoot, "Views", "GpuView.xaml"));
        string pages = File.ReadAllText(Path.Combine(applicationRoot, "Themes", "Tracework", "Pages.xaml"));
        string processorSources = string.Join(
            Environment.NewLine,
            new[]
            {
                Path.Combine(applicationRoot, "Views", "CpuView.xaml"),
                Path.Combine(applicationRoot, "Views", "GpuView.xaml")
            }
            .Concat(Directory.EnumerateFiles(Path.Combine(applicationRoot, "Views", "Cpu"), "*.*"))
            .Concat(Directory.EnumerateFiles(Path.Combine(applicationRoot, "Views", "Gpu"), "*.*"))
            .Select(File.ReadAllText));

        System.Text.RegularExpressions.Regex contentControl = new(
            "<ContentControl(?=\\s|>)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        TestSupport.Equal(1, contentControl.Matches(cpuView).Count, "CPU root ContentControl count");
        TestSupport.Equal(1, contentControl.Matches(gpuView).Count, "GPU root ContentControl count");
        TestSupport.True(cpuView.Contains("ClassicCpuTemplate", StringComparison.Ordinal)
            && cpuView.Contains("TraceworkCpuTemplate", StringComparison.Ordinal), "CPU dual templates");
        TestSupport.True(gpuView.Contains("ClassicGpuTemplate", StringComparison.Ordinal)
            && gpuView.Contains("TraceworkGpuTemplate", StringComparison.Ordinal), "GPU dual templates");

        System.Text.RegularExpressions.Regex unkeyedStyle = new(
            "<Style(?=\\s|>)(?![^>]*\\bx:Key\\s*=)[^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant
                | System.Text.RegularExpressions.RegexOptions.Singleline);
        TestSupport.False(unkeyedStyle.IsMatch(pages), "Tracework Pages contains an unkeyed Style");

        string[] forbiddenBehavior =
        [
            "new CpuViewModel", "new GpuViewModel", "IThemeService", "ThemeService",
            "PollingService", "DashboardViewModel", "SetActive", "ApplyTheme",
            "RefreshHardwareInfo", "Navigate", "DispatcherTimer", "CompositionTarget.Rendering"
        ];
        TestSupport.False(forbiddenBehavior.Any(processorSources.Contains),
            "processor layouts contain ViewModel, service, refresh, navigation, or timer behavior");

        string[] forbiddenVisualMaterial =
        [
            "Storyboard", "DoubleAnimation", "ThicknessAnimation", "ColorAnimation",
            "PixelShader", "BlurEffect", "<Image", "<ImageBrush", "<BitmapImage",
            "file:", "pack://siteoforigin", "Binding CurrentPage"
        ];
        TestSupport.False(forbiddenVisualMaterial.Any(processorSources.Contains),
            "processor layouts contain forbidden motion, shader, image, path, or PageHost material");

        System.Text.RegularExpressions.Regex absolutePath = new(
            "(?<![A-Z])[A-Z]:[\\\\/]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        TestSupport.False(absolutePath.IsMatch(processorSources), "processor layouts contain an absolute path");

        string appXaml = File.ReadAllText(Path.Combine(applicationRoot, "App.xaml"));
        TestSupport.True(appXaml.Contains("Themes/Tracework/Pages.xaml", StringComparison.Ordinal),
            "Tracework Pages resource merge for processor layouts");
    }

    private static DetailMetricViewModel Metric(string label, bool isVisible) => new(label, "42")
    {
        IsVisible = isVisible
    };

    private static GpuDevice CreateGpu(string id, string name)
    {
        SensorReading load = Reading(name, "GPU Core", SensorType.Load, 64d, "%");
        SensorReading temperature = Reading(name, "GPU Core", SensorType.Temperature, 58d, "°C");
        SensorReading power = Reading(name, "GPU Package", SensorType.Power, 132d, "W");
        SensorReading clock = Reading(name, "GPU Core", SensorType.Clock, 1980d, "MHz");
        return new GpuDevice
        {
            Id = id,
            Name = name,
            Vendor = "Synthetic Vendor",
            HardwareType = "Synthetic Adapter",
            DriverVersion = "1.2.3-test",
            AdapterRam = 8UL * 1024UL * 1024UL * 1024UL,
            IsDiscrete = true,
            Availability = SensorAvailability.Available,
            CoreLoad = load,
            TemperatureCore = temperature,
            PowerPackage = power,
            CoreClock = clock,
            Sensors = [load, temperature, power, clock]
        };
    }

    private static SensorReading Reading(
        string deviceName,
        string sensorName,
        SensorType type,
        double value,
        string unit) => new()
    {
        DeviceName = deviceName,
        SensorName = sensorName,
        Category = SensorCategory.Gpu,
        Type = type,
        Value = value,
        Unit = unit,
        IsAvailable = true,
        Availability = SensorAvailability.Available,
        Source = "Synthetic"
    };

    private static string FindRepositoryRoot()
    {
        foreach (string searchOrigin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DirectoryInfo? candidate = new(searchOrigin);
            while (candidate is not null)
            {
                if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml"))
                    && File.Exists(Path.Combine(
                        candidate.FullName,
                        "HardwareVision.Tests",
                        "TraceworkProcessorPageTests.cs")))
                {
                    return candidate.FullName;
                }

                candidate = candidate.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the HardwareVision repository root.");
    }

    private sealed class EmptyHardwareInfoService : IHardwareInfoService
    {
        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot { Timestamp = DateTimeOffset.Now });

        public Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>([]);

        public Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("test", "test", null, null, null, null));
    }
}
