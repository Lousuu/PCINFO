using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Views;
using HardwareVision.Views.Shell;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class XamlRuntimeSmokeTests
{
    private static readonly Size LayoutSize = new(1280d, 900d);
    private static readonly Size MinimumLayoutSize = new(920d, 620d);

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("XAML 01 Style BasedOn never uses DynamicResource", StyleBasedOnNeverUsesDynamicResource),
        ("XAML 02 Dashboard view instantiates and lays out", DashboardViewInstantiatesAndLaysOut),
        ("XAML 03 GPU view instantiates and lays out", GpuViewInstantiatesAndLaysOut),
        ("XAML 04 all safe application views instantiate", AllSafeApplicationViewsInstantiate),
        ("XAML 05 key views instantiate after Classic to Tracework switch", KeyViewsInstantiateAfterClassicToTraceworkSwitch),
        ("XAML 06 key views instantiate after Tracework to Classic switch", KeyViewsInstantiateAfterTraceworkToClassicSwitch),
        ("XAML 07 Classic shell instantiates and lays out", ClassicShellInstantiatesAndLaysOut),
        ("XAML 08 Tracework shell instantiates and lays out", TraceworkShellInstantiatesAndLaysOut),
        ("XAML 09 shell switch preserves the single page host", ShellSwitchPreservesSinglePageHost),
        ("XAML 10 shell switch preserves current page content", ShellSwitchPreservesCurrentPageContent),
        ("XAML 11 Tracework navigation selection is visible", TraceworkNavigationSelectionIsVisible),
        ("XAML 12 shell architecture static checks", ShellArchitectureStaticChecks)
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

    private static void ClassicShellInstantiatesAndLaysOut()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic for shell smoke");
        ShellSmokeData data = new(AppTheme.Classic);
        MainShellHost shell = new() { DataContext = data };

        WithHostedView(shell, MinimumLayoutSize, _ =>
        {
            ClassicShellChrome classic = TestSupport.NotNull(
                shell.FindName("ClassicChrome") as ClassicShellChrome,
                "Classic shell chrome");
            TraceworkShellChrome tracework = TestSupport.NotNull(
                shell.FindName("TraceworkChrome") as TraceworkShellChrome,
                "Tracework shell chrome in Classic host");
            TestSupport.Equal(Visibility.Visible, classic.Visibility, "Classic chrome visibility");
            TestSupport.Equal(Visibility.Collapsed, tracework.Visibility, "Tracework chrome visibility in Classic");
            TestSupport.True(classic.ActualWidth > 0d && classic.ActualHeight > 0d, "Classic chrome minimum layout");
        });
    }

    private static void TraceworkShellInstantiatesAndLaysOut()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for shell smoke");
        try
        {
            ShellSmokeData data = new(AppTheme.Tracework);
            MainShellHost shell = new() { DataContext = data };
            WithHostedView(shell, MinimumLayoutSize, _ =>
            {
                TraceworkShellChrome tracework = TestSupport.NotNull(
                    shell.FindName("TraceworkChrome") as TraceworkShellChrome,
                    "Tracework shell chrome");
                TestSupport.Equal(Visibility.Visible, tracework.Visibility, "Tracework chrome visibility");
                TestSupport.NotNull(FindVisualDescendant<TraceworkSignalRail>(tracework), "Tracework SignalRail");
                TestSupport.NotNull(FindVisualDescendant<TraceworkTelemetrySpine>(tracework), "Tracework TelemetrySpine");
                TestSupport.NotNull(FindVisualDescendant<TraceworkTimeRibbon>(tracework), "Tracework TimeRibbon");
                TestSupport.True(tracework.ActualWidth > 0d && tracework.ActualHeight > 0d, "Tracework chrome minimum layout");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void ShellSwitchPreservesSinglePageHost()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic before shell switch");
        ShellSmokeData data = new(AppTheme.Classic);
        MainShellHost shell = new() { DataContext = data };

        try
        {
            WithHostedView(shell, MinimumLayoutSize, host =>
            {
                ContentControl pageHost = GetPageHost(shell);
                TestSupport.Equal(1, CountPageHosts(shell), "PageHost count before theme switch");

                TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework during shell switch");
                data.SetTheme(AppTheme.Tracework);
                host.UpdateLayout();

                TestSupport.True(ReferenceEquals(pageHost, GetPageHost(shell)), "PageHost instance after theme switch");
                TestSupport.Equal(1, CountPageHosts(shell), "PageHost count after theme switch");
                TestSupport.Equal(
                    Visibility.Collapsed,
                    TestSupport.NotNull(shell.FindName("ClassicChrome") as ClassicShellChrome, "Classic chrome after switch").Visibility,
                    "Classic visibility after switch");
                TestSupport.Equal(
                    Visibility.Visible,
                    TestSupport.NotNull(shell.FindName("TraceworkChrome") as TraceworkShellChrome, "Tracework chrome after switch").Visibility,
                    "Tracework visibility after switch");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void ShellSwitchPreservesCurrentPageContent()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic before content preservation smoke");
        Border page = new() { Width = 120d, Height = 80d };
        ShellSmokeData data = new(AppTheme.Classic, page);
        MainShellHost shell = new() { DataContext = data };

        try
        {
            WithHostedView(shell, MinimumLayoutSize, host =>
            {
                ContentControl pageHost = GetPageHost(shell);
                TestSupport.True(ReferenceEquals(page, pageHost.Content), "page content before theme switch");

                TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for content preservation smoke");
                data.SetTheme(AppTheme.Tracework);
                host.UpdateLayout();

                TestSupport.True(ReferenceEquals(pageHost, GetPageHost(shell)), "PageHost during content preservation smoke");
                TestSupport.True(ReferenceEquals(page, pageHost.Content), "page content after theme switch");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void TraceworkNavigationSelectionIsVisible()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for navigation smoke");
        try
        {
            ShellSmokeData data = new(AppTheme.Tracework, selectedKey: "Gpu");
            MainShellHost shell = new() { DataContext = data };
            WithHostedView(shell, MinimumLayoutSize, _ =>
            {
                Button selectedButton = TestSupport.NotNull(
                    FindVisualDescendants<Button>(shell)
                        .FirstOrDefault(button => button.DataContext is NavigationItemViewModel { IsSelected: true }),
                    "selected Tracework navigation button");
                Border indicator = TestSupport.NotNull(
                    selectedButton.Template.FindName("SignalIndicator", selectedButton) as Border,
                    "selected navigation signal indicator");
                TestSupport.Equal(1d, indicator.Opacity, "selected navigation signal opacity");
                TestSupport.True(
                    FindVisualDescendants<TextBlock>(selectedButton)
                        .Any(text => string.Equals(text.Text, "GPU", StringComparison.Ordinal) && text.ActualWidth > 0d),
                    "selected navigation Chinese title visibility");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void ShellArchitectureStaticChecks()
    {
        string repositoryRoot = FindRepositoryRoot();
        string applicationRoot = Path.Combine(repositoryRoot, "HardwareVision");
        Regex currentPageBinding = new(
            """\bContent\s*=\s*(["'])\s*\{\s*Binding\s+CurrentPage\b[^}]*\}\s*\1""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        List<string> currentPageBindings = [];
        foreach (string xamlPath in Directory.EnumerateFiles(applicationRoot, "*.xaml", SearchOption.AllDirectories))
        {
            string contents = File.ReadAllText(xamlPath);
            foreach (Match match in currentPageBinding.Matches(contents))
            {
                int line = 1 + contents.AsSpan(0, match.Index).Count('\n');
                currentPageBindings.Add($"{Path.GetRelativePath(repositoryRoot, xamlPath)}:{line}");
            }
        }

        TestSupport.Equal(1, currentPageBindings.Count,
            "CurrentPage Content binding count; found " + string.Join(", ", currentPageBindings));
        string expectedBindingOwner = Path.Combine(
            "HardwareVision", "Views", "Shell", "MainShellHost.xaml") + ":";
        TestSupport.True(
            currentPageBindings[0].StartsWith(expectedBindingOwner, StringComparison.OrdinalIgnoreCase),
            "CurrentPage binding owner");

        string shellResourcesPath = Path.Combine(applicationRoot, "Themes", "Tracework", "Shell.xaml");
        string shellResources = File.ReadAllText(shellResourcesPath);
        Regex unkeyedStyle = new(
            """<Style(?=\s|>)(?![^>]*\bx:Key\s*=)[^>]*>""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        TestSupport.False(unkeyedStyle.IsMatch(shellResources), "Tracework shell contains an unkeyed Style");

        string[] shellXamlFiles =
        [
            .. Directory.EnumerateFiles(Path.Combine(applicationRoot, "Views", "Shell"), "*.xaml", SearchOption.TopDirectoryOnly),
            shellResourcesPath
        ];
        string shellXaml = string.Join(Environment.NewLine, shellXamlFiles.Select(File.ReadAllText));
        Regex forbiddenExternalMaterial = new(
            """((?<![A-Z])[A-Z]:[\\/]|file:|pack://siteoforigin|<\s*(Image|ImageBrush|BitmapImage)\b|\b(Source|UriSource)\s*=)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        TestSupport.False(forbiddenExternalMaterial.IsMatch(shellXaml),
            "Shell XAML contains a local path or external/image material reference");
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

    private static void Layout(UserControl view) => WithHostedView(view, LayoutSize, _ => { });

    private static void WithHostedView(UserControl view, Size size, Action<Window> test)
    {
        System.Windows.Application application = GetApplication();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Window host = new()
        {
            Content = view,
            Width = size.Width,
            Height = size.Height,
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
            host.Measure(size);
            host.Arrange(new Rect(new Point(), size));
            host.UpdateLayout();
            view.UpdateLayout();
            test(host);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static ContentControl GetPageHost(MainShellHost shell) => TestSupport.NotNull(
        shell.FindName("PageHost") as ContentControl,
        "MainShellHost PageHost");

    private static int CountPageHosts(DependencyObject root) =>
        FindVisualDescendants<FrameworkElement>(root).Count(element => element.Name == "PageHost");

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

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
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
        string[] searchOrigins =
        [
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        ];

        foreach (string searchOrigin in searchOrigins.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DirectoryInfo? candidate = new(searchOrigin);
            while (candidate is not null)
            {
                if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml"))
                    && File.Exists(Path.Combine(
                        candidate.FullName,
                        "HardwareVision.Tests",
                        "XamlRuntimeSmokeTests.cs")))
                {
                    return candidate.FullName;
                }

                candidate = candidate.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above {Directory.GetCurrentDirectory()} "
            + $"or {AppContext.BaseDirectory}.");
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

    private sealed class ShellSmokeData : INotifyPropertyChanged
    {
        private AppTheme theme;

        public ShellSmokeData(AppTheme theme, object? currentPage = null, string selectedKey = "Dashboard")
        {
            this.theme = theme;
            CurrentPage = currentPage ?? new Border();
            NavigationItems = CreateNavigationItems(selectedKey);
            NavigateCommand = NoopCommand.Instance;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ApplicationName => "HardwareVision";
        public AppTheme CurrentTheme => theme;
        public bool IsClassicTheme => theme == AppTheme.Classic;
        public bool IsTraceworkTheme => theme == AppTheme.Tracework;
        public object CurrentPage { get; }
        public string CurrentPageCode => "03";
        public string CurrentPageTitle => "GPU";
        public string CurrentPageSubtitle => "显卡指标";
        public string StatusText => "正在读取硬件信息";
        public string FooterText => "最后刷新：测试宿主";
        public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }
        public ICommand NavigateCommand { get; }

        public void SetTheme(AppTheme value)
        {
            if (theme == value)
            {
                return;
            }

            theme = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTheme)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsClassicTheme)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTraceworkTheme)));
        }

        private static IReadOnlyList<NavigationItemViewModel> CreateNavigationItems(string selectedKey)
        {
            (string Key, string Code, string Title)[] definitions =
            [
                ("Dashboard", "01", "首页"),
                ("Cpu", "02", "CPU"),
                ("Gpu", "03", "GPU"),
                ("Memory", "04", "内存"),
                ("Disk", "05", "硬盘"),
                ("Network", "06", "网络"),
                ("Motherboard", "07", "主板"),
                ("GamePerformance", "08", "游戏"),
                ("AdvancedSensors", "09", "高级传感器"),
                ("Settings", "10", "设置")
            ];
            return definitions.Select(definition => new NavigationItemViewModel(
                    definition.Key,
                    definition.Code,
                    definition.Title,
                    definition.Title + " smoke",
                    new object())
                {
                    IsSelected = string.Equals(definition.Key, selectedKey, StringComparison.Ordinal)
                })
                .ToArray();
        }
    }

    private sealed class NoopCommand : ICommand
    {
        public static NoopCommand Instance { get; } = new();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
