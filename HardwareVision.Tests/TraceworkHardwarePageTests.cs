using System.Reflection;
using System.Text.RegularExpressions;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views.Disk;
using HardwareVision.Views.Memory;
using HardwareVision.Views.Motherboard;
using HardwareVision.Views.Network;

namespace HardwareVision.Tests;

internal static class TraceworkHardwarePageTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Hardware pages 01 projection disposal releases subscriptions", ProjectionDisposalReleasesSubscriptions),
        ("Hardware pages 02 Memory state and projections remain stable", MemoryStateAndProjectionsRemainStable),
        ("Hardware pages 03 Disk state and projections remain stable", DiskStateAndProjectionsRemainStable),
        ("Hardware pages 04 Network selection and projections remain stable", NetworkSelectionAndProjectionsRemainStable),
        ("Hardware pages 05 Motherboard state and projections remain stable", MotherboardStateAndProjectionsRemainStable),
        ("Hardware pages 06 view models have no theme service dependency", ViewModelsHaveNoThemeServiceDependency),
        ("Hardware pages 07 layouts are presentation-only controls", LayoutsArePresentationOnlyControls),
        ("Hardware pages 08 dual-template and behavior architecture checks", ArchitectureSourceChecks),
        ("Hardware pages 09 Tracework resources remain explicit and unique", TraceworkResourcesRemainExplicitAndUnique)
    ];

    private static void ProjectionDisposalReleasesSubscriptions()
    {
        System.Collections.ObjectModel.ObservableCollection<DetailMetricViewModel> metrics = [Metric("First", true)];
        VisibleMetricProjection projection = new(metrics);
        projection.Dispose();
        projection.Dispose();
        metrics.Add(Metric("Second", true));
        metrics[0].IsVisible = false;

        TestSupport.Equal(1, projection.VisibleMetricCount, "disposed projection remains detached");
        TestSupport.True(ReferenceEquals(metrics[0], projection.PrimaryMetric), "disposed projection retains last snapshot reference");
    }

    private static void MemoryStateAndProjectionsRemainStable()
    {
        using MemoryViewModel viewModel = new();
        object overview = viewModel.OverviewMetrics;
        object professional = viewModel.ProfessionalMetrics;
        object modules = viewModel.MemoryModules;
        MemoryModuleViewModel module = new() { SlotName = "DIMM_A1", ModuleName = "Synthetic DIMM" };
        DetailMetricViewModel moduleMetric = Metric("Capacity", true);
        module.Metrics.Add(moduleMetric);
        viewModel.MemoryModules.Add(module);
        DetailMetricViewModel hidden = Metric("Hidden", false);
        DetailMetricViewModel visible = Metric("Visible", true);
        viewModel.OverviewMetrics.Add(hidden);
        viewModel.OverviewMetrics.Add(visible);

        TestSupport.True(ReferenceEquals(overview, viewModel.OverviewMetrics), "Memory overview collection reference");
        TestSupport.True(ReferenceEquals(professional, viewModel.ProfessionalMetrics), "Memory professional collection reference");
        TestSupport.True(ReferenceEquals(modules, viewModel.MemoryModules), "Memory module collection reference");
        TestSupport.True(ReferenceEquals(module, viewModel.MemoryModules[0]), "Memory module item reference");
        TestSupport.True(ReferenceEquals(moduleMetric, viewModel.MemoryModules[0].Metrics[0]), "Memory module metric reference");
        TestSupport.True(ReferenceEquals(visible, viewModel.OverviewProjection.PrimaryMetric), "Memory projection order and reference");
    }

    private static void DiskStateAndProjectionsRemainStable()
    {
        using DiskViewModel viewModel = new();
        object overview = viewModel.OverviewMetrics;
        object professional = viewModel.ProfessionalMetrics;
        object devices = viewModel.DiskDevices;
        string status = viewModel.StatusText;
        DiskDeviceViewModel device = new() { Name = "Synthetic Disk", Subtitle = "NVMe" };
        DetailMetricViewModel deviceMetric = Metric("Firmware", true);
        device.Metrics.Add(deviceMetric);
        viewModel.DiskDevices.Add(device);
        viewModel.ProfessionalMetrics.Add(Metric("Health", true));

        TestSupport.True(ReferenceEquals(overview, viewModel.OverviewMetrics), "Disk overview collection reference");
        TestSupport.True(ReferenceEquals(professional, viewModel.ProfessionalMetrics), "Disk professional collection reference");
        TestSupport.True(ReferenceEquals(devices, viewModel.DiskDevices), "Disk device collection reference");
        TestSupport.True(ReferenceEquals(device, viewModel.DiskDevices[0]), "Disk device item reference");
        TestSupport.True(ReferenceEquals(deviceMetric, viewModel.DiskDevices[0].Metrics[0]), "Disk device metric reference");
        TestSupport.Equal(status, viewModel.StatusText, "Disk status text");
        TestSupport.Equal(1, viewModel.ProfessionalProjection.VisibleMetricCount, "Disk professional projection");
    }

    private static void NetworkSelectionAndProjectionsRemainStable()
    {
        AppSettings settings = new() { PreferredNetworkAdapterId = "previous-adapter" };
        CountingSettingsService settingsService = new(settings);
        using NetworkViewModel viewModel = new(null!, settings, settingsService);
        NetworkAdapterItemViewModel adapter = new()
        {
            Device = new NetworkAdapterDevice { Id = "adapter-1", Name = "Synthetic Ethernet", IsUp = true }
        };
        adapter.Metrics.Add(Metric("Status", true));
        viewModel.NetworkAdapters.Add(adapter);
        viewModel.SelectedAdapter = adapter;
        viewModel.ShowVirtualAdapters = true;
        int updatesAfterStateChange = settingsService.UpdateCount;
        viewModel.SelectedAdapter = adapter;
        viewModel.ShowVirtualAdapters = true;
        object adapters = viewModel.NetworkAdapters;
        object overview = viewModel.OverviewMetrics;
        object professional = viewModel.ProfessionalMetrics;

        TestSupport.True(ReferenceEquals(adapters, viewModel.NetworkAdapters), "Network adapter collection reference");
        TestSupport.True(ReferenceEquals(adapter, viewModel.NetworkAdapters[0]), "Network adapter item reference");
        TestSupport.True(ReferenceEquals(adapter, viewModel.SelectedAdapter), "Network selected adapter reference");
        TestSupport.True(ReferenceEquals(overview, viewModel.OverviewMetrics), "Network overview collection reference");
        TestSupport.True(ReferenceEquals(professional, viewModel.ProfessionalMetrics), "Network professional collection reference");
        TestSupport.True(viewModel.ShowVirtualAdapters, "Network virtual adapter setting");
        TestSupport.Equal("adapter-1", settings.PreferredNetworkAdapterId, "Network preferred adapter id");
        TestSupport.Equal(updatesAfterStateChange, settingsService.UpdateCount,
            "Network same-value template binding does not rewrite settings");
        TestSupport.Equal("已连接", adapter.StatusText, "Network status text");
    }

    private static void MotherboardStateAndProjectionsRemainStable()
    {
        using MotherboardViewModel viewModel = new();
        object board = viewModel.BoardMetrics;
        object bios = viewModel.BiosMetrics;
        object device = viewModel.DeviceMetrics;
        object sensor = viewModel.SensorMetrics;
        object rows = viewModel.SensorRows;
        string motherboardName = viewModel.MotherboardName;
        string deviceSummary = viewModel.DeviceSummary;
        viewModel.BoardMetrics.Add(Metric("Vendor", true));
        viewModel.BiosMetrics.Add(Metric("BIOS", true));
        viewModel.DeviceMetrics.Add(Metric("Chassis", true));
        viewModel.SensorMetrics.Add(Metric("Temperature", true));

        TestSupport.True(ReferenceEquals(board, viewModel.BoardMetrics), "Motherboard board collection reference");
        TestSupport.True(ReferenceEquals(bios, viewModel.BiosMetrics), "Motherboard BIOS collection reference");
        TestSupport.True(ReferenceEquals(device, viewModel.DeviceMetrics), "Motherboard device collection reference");
        TestSupport.True(ReferenceEquals(sensor, viewModel.SensorMetrics), "Motherboard sensor collection reference");
        TestSupport.True(ReferenceEquals(rows, viewModel.SensorRows), "Motherboard SensorRows reference");
        TestSupport.Equal(motherboardName, viewModel.MotherboardName, "Motherboard name");
        TestSupport.Equal(deviceSummary, viewModel.DeviceSummary, "Motherboard device summary");
        TestSupport.Equal(1, viewModel.BoardProjection.VisibleMetricCount, "Motherboard board projection");
        TestSupport.Equal(1, viewModel.SensorProjection.VisibleMetricCount, "Motherboard sensor projection");
    }

    private static void ViewModelsHaveNoThemeServiceDependency()
    {
        Type[] viewModelTypes = [typeof(MemoryViewModel), typeof(DiskViewModel), typeof(NetworkViewModel), typeof(MotherboardViewModel)];
        foreach (Type type in viewModelTypes)
        {
            bool dependsOnThemeService = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => typeof(IThemeService).IsAssignableFrom(parameter.ParameterType));
            TestSupport.False(dependsOnThemeService, $"{type.Name} theme-service constructor dependency");
        }
    }

    private static void LayoutsArePresentationOnlyControls()
    {
        Type[] layoutTypes =
        [
            typeof(ClassicMemoryLayout), typeof(TraceworkMemoryLayout),
            typeof(ClassicDiskLayout), typeof(TraceworkDiskLayout),
            typeof(ClassicNetworkLayout), typeof(TraceworkNetworkLayout),
            typeof(ClassicMotherboardLayout), typeof(TraceworkMotherboardLayout)
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
        string[] pageNames = ["Memory", "Disk", "Network", "Motherboard"];
        Regex contentControl = new("<ContentControl(?=\\s|>)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (string pageName in pageNames)
        {
            string root = File.ReadAllText(Path.Combine(applicationRoot, "Views", $"{pageName}View.xaml"));
            TestSupport.Equal(1, contentControl.Matches(root).Count, $"{pageName} root ContentControl count");
            TestSupport.True(root.Contains($"Classic{pageName}Template", StringComparison.Ordinal)
                && root.Contains($"Tracework{pageName}Template", StringComparison.Ordinal), $"{pageName} dual templates");
            TestSupport.True(root.Contains("ThemeContext.CurrentTheme", StringComparison.Ordinal), $"{pageName} inherited theme binding");
        }

        string pageSources = string.Join(Environment.NewLine, pageNames.SelectMany(pageName =>
                Directory.EnumerateFiles(Path.Combine(applicationRoot, "Views", pageName), "*.*"))
            .Select(File.ReadAllText));
        string[] forbiddenBehavior =
        [
            "new MemoryViewModel", "new DiskViewModel", "new NetworkViewModel", "new MotherboardViewModel",
            "IThemeService", "ThemeService", "PollingService", "DashboardViewModel", "SetActive", "ApplyTheme",
            "RefreshHardwareInfo", "Refresh()", "Navigate", "DispatcherTimer", "CompositionTarget.Rendering"
        ];
        TestSupport.False(forbiddenBehavior.Any(pageSources.Contains),
            "hardware layouts contain ViewModel, service, refresh, navigation, or timer behavior");

        string[] forbiddenVisualMaterial =
        [
            "Storyboard", "DoubleAnimation", "ThicknessAnimation", "ColorAnimation", "ObjectAnimationUsingKeyFrames",
            "PixelShader", "BlurEffect", "<Image", "<ImageBrush", "<BitmapImage", "file:", "pack://siteoforigin",
            "Binding CurrentPage"
        ];
        TestSupport.False(forbiddenVisualMaterial.Any(pageSources.Contains),
            "hardware layouts contain forbidden motion, shader, image, path, or PageHost material");

        Regex absolutePath = new("(?<![A-Z])[A-Z]:[\\\\/]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        TestSupport.False(absolutePath.IsMatch(pageSources), "hardware layouts contain an absolute path");
    }

    private static void TraceworkResourcesRemainExplicitAndUnique()
    {
        string repositoryRoot = FindRepositoryRoot();
        string applicationRoot = Path.Combine(repositoryRoot, "HardwareVision");
        string pages = File.ReadAllText(Path.Combine(applicationRoot, "Themes", "Tracework", "Pages.xaml"));
        Regex unkeyedStyle = new("<Style(?=\\s|>)(?![^>]*\\bx:Key\\s*=)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        TestSupport.False(unkeyedStyle.IsMatch(pages), "Tracework Pages contains an unkeyed Style");
        TestSupport.False(pages.Contains("BasedOn=\"{DynamicResource", StringComparison.Ordinal),
            "Tracework Pages DynamicResource BasedOn");

        string[] sourceFiles = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories).ToArray();
        int projectionDeclarations = sourceFiles.Sum(path => Regex.Matches(
            File.ReadAllText(path), "class\\s+VisibleMetricProjection\\b", RegexOptions.CultureInvariant).Count);
        TestSupport.Equal(1, projectionDeclarations, "VisibleMetricProjection declaration count");

        string allXaml = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(applicationRoot, "*.xaml", SearchOption.AllDirectories).Select(File.ReadAllText));
        TestSupport.Equal(1, Regex.Matches(allXaml, "Binding\\s+CurrentPage\\b", RegexOptions.CultureInvariant).Count,
            "CurrentPage PageHost binding count");
    }

    private static DetailMetricViewModel Metric(string label, bool isVisible) => new(label, "42")
    {
        IsVisible = isVisible
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
                    && File.Exists(Path.Combine(candidate.FullName, "HardwareVision.Tests", "TraceworkHardwarePageTests.cs")))
                {
                    return candidate.FullName;
                }

                candidate = candidate.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the HardwareVision repository root.");
    }

    private sealed class CountingSettingsService(AppSettings settings) : ISettingsService
    {
        public int UpdateCount { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            updateAction(settings);
            UpdateCount++;
            return Task.FromResult(settings);
        }
    }
}
