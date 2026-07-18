using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Views;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class XamlRuntimeSmokeTests
{
    private static readonly Size LayoutSize = new(1280d, 900d);

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("XAML 01 Style BasedOn never uses DynamicResource", StyleBasedOnNeverUsesDynamicResource),
        ("XAML 02 Dashboard view instantiates and lays out", DashboardViewInstantiatesAndLaysOut),
        ("XAML 03 GPU view instantiates and lays out", GpuViewInstantiatesAndLaysOut),
        ("XAML 04 all safe application views instantiate", AllSafeApplicationViewsInstantiate),
        ("XAML 05 key views instantiate after Classic to Tracework switch", KeyViewsInstantiateAfterClassicToTraceworkSwitch),
        ("XAML 06 key views instantiate after Tracework to Classic switch", KeyViewsInstantiateAfterTraceworkToClassicSwitch)
    ];

    private static void StyleBasedOnNeverUsesDynamicResource()
    {
        string repositoryRoot = FindRepositoryRoot();
        string applicationRoot = Path.Combine(repositoryRoot, "HardwareVision");
        Regex invalidBasedOn = new(
            """\bBasedOn\s*=\s*(["'])\s*\{\s*DynamicResource\b[^}]*\}\s*\1""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        List<string> failures = [];

        foreach (string xamlPath in Directory.EnumerateFiles(applicationRoot, "*.xaml", SearchOption.AllDirectories))
        {
            string contents = File.ReadAllText(xamlPath);
            foreach (Match match in invalidBasedOn.Matches(contents))
            {
                int line = 1 + contents.AsSpan(0, match.Index).Count('\n');
                string relativePath = Path.GetRelativePath(repositoryRoot, xamlPath);
                string attribute = Regex.Replace(match.Value, "\\s+", " ").Trim();
                failures.Add($"{relativePath}:{line}: {attribute}");
            }
        }

        TestSupport.True(
            failures.Count == 0,
            "Style.BasedOn cannot use DynamicResource:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static void DashboardViewInstantiatesAndLaysOut() => LayoutDashboardView();

    private static void GpuViewInstantiatesAndLaysOut() => LayoutGpuView();

    private static void AllSafeApplicationViewsInstantiate()
    {
        (string Name, Func<UserControl> Create)[] views =
        [
            (nameof(AdvancedSensorsView), () => new AdvancedSensorsView()),
            (nameof(CpuView), () => new CpuView()),
            (nameof(DashboardView), () => new DashboardView()),
            (nameof(DiskView), () => new DiskView()),
            (nameof(GamePerformanceView), () => new GamePerformanceView()),
            (nameof(GameSessionReportView), () => new GameSessionReportView()),
            (nameof(GpuView), () => new GpuView()),
            (nameof(MemoryView), () => new MemoryView()),
            (nameof(MetricVisibilityView), () => new MetricVisibilityView()),
            (nameof(MotherboardView), () => new MotherboardView()),
            (nameof(NetworkView), () => new NetworkView()),
            (nameof(SettingsView), () => new SettingsView())
        ];

        foreach ((string name, Func<UserControl> create) in views)
        {
            UserControl view = create();
            Layout(view);
            TestSupport.True(view.ActualWidth > 0d && view.ActualHeight > 0d, $"{name} layout size");
        }
    }

    private static void KeyViewsInstantiateAfterClassicToTraceworkSwitch()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic before forward switch");
        LayoutDashboardView();
        LayoutGpuView();

        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework after Classic");
        LayoutDashboardView();
        LayoutGpuView();
    }

    private static void KeyViewsInstantiateAfterTraceworkToClassicSwitch()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework before reverse switch");
        LayoutDashboardView();
        LayoutGpuView();

        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic after Tracework");
        LayoutDashboardView();
        LayoutGpuView();
    }

    private static void LayoutDashboardView()
    {
        HardwareOverviewCardViewModel card = new()
        {
            Title = "GPU",
            HardwareName = "Synthetic GPU"
        };
        card.UpdateHardwareOptions(
            [("gpu-0", "Synthetic GPU 0"), ("gpu-1", "Synthetic GPU 1")],
            "gpu-0",
            _ => { });
        card.Metrics.Add(new DetailMetricViewModel("Utilization", "50%"));

        DashboardView view = new()
        {
            DataContext = new DashboardSmokeData([card])
        };
        Layout(view);

        ComboBox? selector = FindVisualDescendant<ComboBox>(view);
        TestSupport.NotNull(selector, "Dashboard overview-card DataTemplate was not instantiated");
    }

    private static void LayoutGpuView()
    {
        GpuView view = new()
        {
            DataContext = new GpuSmokeData([new DetailSensorRowViewModel()])
        };
        Layout(view);

        DataGrid grid = TestSupport.NotNull(
            FindVisualDescendant<DataGrid>(view),
            "GPU sensor DataGrid was not instantiated");
        TestSupport.Equal(1, grid.Items.Count, "GPU smoke sensor row count");
        grid.ScrollIntoView(grid.Items[0]);
        grid.UpdateLayout();
        TestSupport.NotNull(
            FindVisualDescendant<DataGridCell>(grid),
            "GPU sensor DataGrid cells were not instantiated");
    }

    private static void Layout(UserControl view)
    {
        System.Windows.Application application = GetApplication();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Window host = new()
        {
            Content = view,
            Width = LayoutSize.Width,
            Height = LayoutSize.Height,
            Left = -32000d,
            Top = -32000d,
            Opacity = 0d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };

        try
        {
            host.Show();
            host.ApplyTemplate();
            view.ApplyTemplate();
            host.Measure(LayoutSize);
            host.Arrange(new Rect(new Point(), LayoutSize));
            host.UpdateLayout();
            view.UpdateLayout();
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            T? descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static ThemeService GetThemeService() => new(GetApplication());

    private static System.Windows.Application GetApplication()
    {
        if (System.Windows.Application.Current is not null)
        {
            return System.Windows.Application.Current;
        }

        HardwareVision.App application = new();
        application.InitializeComponent();
        return application;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null)
        {
            if (Directory.Exists(Path.Combine(candidate.FullName, "HardwareVision"))
                && Directory.Exists(Path.Combine(candidate.FullName, "HardwareVision.Tests")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above {AppContext.BaseDirectory}.");
    }

    private sealed record DashboardSmokeData(IReadOnlyList<HardwareOverviewCardViewModel> OverviewCards)
    {
        public string ApplicationName => "HardwareVision.Tests";
        public string DeviceName => "Synthetic Device";
        public string OperatingSystem => "Windows Test Host";
        public string LastRefreshTime => "Now";
    }

    private sealed record GpuSmokeData(IReadOnlyList<DetailSensorRowViewModel> SensorRows)
    {
        public string GpuName => "Synthetic GPU";
        public IReadOnlyList<GpuDevice> GpuDevices => [];
        public GpuDevice? SelectedGpu { get; set; }
        public string GpuSelectionHint => "Synthetic GPU fixture";
        public bool HasGpuMetrics => false;
        public IReadOnlyList<DetailMetricViewModel> Metrics => [];
        public bool HasGpuDetails => false;
        public IReadOnlyList<DetailMetricViewModel> InfoItems => [];
        public IReadOnlyList<RealtimeMetricChartViewModel> Charts => [];
        public IReadOnlyList<int> ChartWindowOptions => [30, 60, 120];
        public int SelectedChartWindowSeconds { get; set; } = 60;
        public bool HasGpuSensors => true;
    }
}
