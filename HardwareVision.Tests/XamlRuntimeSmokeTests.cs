using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Themes;
using HardwareVision.Views;
using HardwareVision.Views.Cpu;
using HardwareVision.Views.Dashboard;
using HardwareVision.Views.Gpu;
using HardwareVision.Views.Disk;
using HardwareVision.Views.Memory;
using HardwareVision.Views.Motherboard;
using HardwareVision.Views.Network;
using HardwareVision.Views.Shell;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class XamlRuntimeSmokeTests
{
    private static readonly Size LayoutSize = new(1280d, 900d);
    private static readonly Size MinimumLayoutSize = new(920d, 620d);
    private static ThemeService? sharedThemeService;

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
        ("XAML 12 Classic Dashboard template instantiates", ClassicDashboardTemplateInstantiates),
        ("XAML 13 Tracework Dashboard template instantiates", TraceworkDashboardTemplateInstantiates),
        ("XAML 14 Tracework Dashboard lays out at 920x620", TraceworkDashboardLaysOutAtMinimumSize),
        ("XAML 15 Dashboard theme switch preserves DataContext", DashboardThemeSwitchPreservesDataContext),
        ("XAML 16 Tracework dashboard device selectors instantiate", TraceworkDashboardDeviceSelectorsInstantiate),
        ("XAML 17 Dashboard architecture static checks", DashboardArchitectureStaticChecks),
        ("XAML 18 shell architecture static checks", ShellArchitectureStaticChecks),
        ("XAML 19 Classic CPU template instantiates", ClassicCpuTemplateInstantiates),
        ("XAML 20 Tracework CPU template instantiates", TraceworkCpuTemplateInstantiates),
        ("XAML 21 Tracework CPU lays out at 920x620", TraceworkCpuLaysOutAtMinimumSize),
        ("XAML 22 CPU theme switch preserves chart state and DataContext", CpuThemeSwitchPreservesChartStateAndDataContext),
        ("XAML 23 Classic GPU template instantiates", ClassicGpuTemplateInstantiates),
        ("XAML 24 Tracework GPU template instantiates", TraceworkGpuTemplateInstantiates),
        ("XAML 25 Tracework GPU lays out at 920x620", TraceworkGpuLaysOutAtMinimumSize),
        ("XAML 26 Tracework GPU selector and sensor matrix instantiate", TraceworkGpuSelectorAndSensorMatrixInstantiate),
        ("XAML 27 GPU theme switch preserves selection and DataContext", GpuThemeSwitchPreservesSelectionAndDataContext),
        ("XAML 28 Classic Memory template instantiates", ClassicMemoryTemplateInstantiates),
        ("XAML 29 Tracework Memory template instantiates", TraceworkMemoryTemplateInstantiates),
        ("XAML 30 Tracework Memory lays out at 920x620", TraceworkMemoryLaysOutAtMinimumSize),
        ("XAML 31 Memory module topology instantiates", MemoryModuleTopologyInstantiates),
        ("XAML 32 Classic Disk template instantiates", ClassicDiskTemplateInstantiates),
        ("XAML 33 Tracework Disk template instantiates", TraceworkDiskTemplateInstantiates),
        ("XAML 34 Tracework Disk lays out at 920x620", TraceworkDiskLaysOutAtMinimumSize),
        ("XAML 35 Disk device nodes instantiate", DiskDeviceNodesInstantiate),
        ("XAML 36 Classic Network template instantiates", ClassicNetworkTemplateInstantiates),
        ("XAML 37 Tracework Network template instantiates", TraceworkNetworkTemplateInstantiates),
        ("XAML 38 Tracework Network lays out at 920x620", TraceworkNetworkLaysOutAtMinimumSize),
        ("XAML 39 Network selector and toggle instantiate", NetworkSelectorAndToggleInstantiate),
        ("XAML 40 Network adapter nodes instantiate", NetworkAdapterNodesInstantiate),
        ("XAML 41 Classic Motherboard template instantiates", ClassicMotherboardTemplateInstantiates),
        ("XAML 42 Tracework Motherboard template instantiates", TraceworkMotherboardTemplateInstantiates),
        ("XAML 43 Tracework Motherboard lays out at 920x620", TraceworkMotherboardLaysOutAtMinimumSize),
        ("XAML 44 Motherboard sensor matrix instantiates", MotherboardSensorMatrixInstantiates),
        ("XAML 45 four-page bidirectional theme switch preserves DataContext", FourPageThemeSwitchPreservesDataContext)
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

    private static void ClassicDashboardTemplateInstantiates()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic for Dashboard template smoke");
        DashboardView dashboard = CreateDashboardView(out DashboardSmokeData data);
        ThemeContext.SetCurrentTheme(dashboard, AppTheme.Classic);

        WithHostedView(dashboard, MinimumLayoutSize, _ =>
        {
            ClassicDashboardLayout classic = TestSupport.NotNull(
                FindVisualDescendants<ClassicDashboardLayout>(dashboard).SingleOrDefault(),
                "Classic Dashboard layout");
            TestSupport.Equal(0, FindVisualDescendants<TraceworkDashboardLayout>(dashboard).Count(),
                "Tracework layout count in Classic Dashboard");
            TestSupport.True(ReferenceEquals(data, classic.DataContext), "Classic Dashboard DataContext");
        });
    }

    private static void TraceworkDashboardTemplateInstantiates()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for Dashboard template smoke");
        try
        {
            DashboardView dashboard = CreateDashboardView(out DashboardSmokeData data);
            ThemeContext.SetCurrentTheme(dashboard, AppTheme.Tracework);
            WithHostedView(dashboard, MinimumLayoutSize, _ =>
            {
                TraceworkDashboardLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkDashboardLayout>(dashboard).SingleOrDefault(),
                    "Tracework Dashboard layout");
                TestSupport.Equal(0, FindVisualDescendants<ClassicDashboardLayout>(dashboard).Count(),
                    "Classic layout count in Tracework Dashboard");
                TestSupport.True(ReferenceEquals(data, tracework.DataContext), "Tracework Dashboard DataContext");
                TestSupport.Equal(6, FindVisualDescendants<TraceworkPanel>(tracework).Count(),
                    "Tracework Dashboard panel count");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void TraceworkDashboardLaysOutAtMinimumSize()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for minimum Dashboard smoke");
        try
        {
            DashboardView dashboard = CreateDashboardView(out _);
            ShellSmokeData shellData = new(AppTheme.Tracework, dashboard);
            MainShellHost shell = new() { DataContext = shellData };

            WithHostedView(shell, MinimumLayoutSize, _ =>
            {
                TraceworkDashboardLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkDashboardLayout>(dashboard).SingleOrDefault(),
                    "Tracework Dashboard at minimum size");
                ScrollViewer scrollViewer = TestSupport.NotNull(
                    FindVisualDescendant<ScrollViewer>(tracework),
                    "Tracework Dashboard ScrollViewer");
                TraceworkPanel cpu = FindPanel(tracework, "CPU.01");
                TraceworkPanel gpu = FindPanel(tracework, "GPU.02");

                TestSupport.Equal(AppTheme.Tracework, ThemeContext.GetCurrentTheme(dashboard),
                    "Dashboard inherited theme at minimum size");
                TestSupport.True(tracework.ActualWidth > 0d && tracework.ActualHeight > 0d,
                    "Tracework Dashboard minimum layout size");
                TestSupport.True(cpu.ActualWidth > 0d && gpu.ActualWidth > 0d,
                    "Tracework primary panel widths at minimum size");
                TestSupport.Equal(0d, scrollViewer.ScrollableWidth, "Tracework Dashboard horizontal overflow");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void DashboardThemeSwitchPreservesDataContext()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic before Dashboard switch smoke");
        DashboardView dashboard = CreateDashboardView(out DashboardSmokeData data);
        object dataContext = dashboard.DataContext;
        HardwareOverviewCardViewModel[] cards = data.OverviewCards.ToArray();
        ShellSmokeData shellData = new(AppTheme.Classic, dashboard);
        MainShellHost shell = new() { DataContext = shellData };

        try
        {
            WithHostedView(shell, MinimumLayoutSize, host =>
            {
                TestSupport.Equal(1, FindVisualDescendants<ClassicDashboardLayout>(dashboard).Count(),
                    "Classic layout before Dashboard switch");

                TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework during Dashboard switch");
                shellData.SetTheme(AppTheme.Tracework);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                host.UpdateLayout();

                TestSupport.Equal(0, FindVisualDescendants<ClassicDashboardLayout>(dashboard).Count(),
                    "Classic layout after forward Dashboard switch");
                TestSupport.Equal(1, FindVisualDescendants<TraceworkDashboardLayout>(dashboard).Count(),
                    "Tracework layout after forward Dashboard switch");
                TestSupport.True(ReferenceEquals(dataContext, dashboard.DataContext),
                    "Dashboard DataContext after forward switch");
                AssertCardReferences(data, cards, "forward switch");

                TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic during reverse Dashboard switch");
                shellData.SetTheme(AppTheme.Classic);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                host.UpdateLayout();

                TestSupport.Equal(1, FindVisualDescendants<ClassicDashboardLayout>(dashboard).Count(),
                    "Classic layout after reverse Dashboard switch");
                TestSupport.Equal(0, FindVisualDescendants<TraceworkDashboardLayout>(dashboard).Count(),
                    "Tracework layout after reverse Dashboard switch");
                TestSupport.True(ReferenceEquals(dataContext, dashboard.DataContext),
                    "Dashboard DataContext after reverse switch");
                AssertCardReferences(data, cards, "reverse switch");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void TraceworkDashboardDeviceSelectorsInstantiate()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for Dashboard selector smoke");
        try
        {
            DashboardView dashboard = CreateDashboardView(out _);
            ThemeContext.SetCurrentTheme(dashboard, AppTheme.Tracework);
            WithHostedView(dashboard, MinimumLayoutSize, _ =>
            {
                ComboBox gpuSelector = FindSelector(dashboard, HardwareOverviewKind.Gpu);
                ComboBox diskSelector = FindSelector(dashboard, HardwareOverviewKind.Disk);

                TestSupport.Equal(Visibility.Visible, gpuSelector.Visibility, "GPU selector visibility");
                TestSupport.Equal(Visibility.Visible, diskSelector.Visibility, "disk selector visibility");
                TestSupport.True(gpuSelector.IsHitTestVisible && diskSelector.IsHitTestVisible,
                    "Tracework selector hit testing");
                TestSupport.True(gpuSelector.ActualWidth > 0d && diskSelector.ActualWidth > 0d,
                    "Tracework selector layout widths");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void ClassicCpuTemplateInstantiates()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic for CPU template smoke");
        CpuView cpu = CreateCpuView(out CpuViewModel data);
        ThemeContext.SetCurrentTheme(cpu, AppTheme.Classic);

        WithHostedView(cpu, MinimumLayoutSize, _ =>
        {
            ClassicCpuLayout classic = TestSupport.NotNull(
                FindVisualDescendants<ClassicCpuLayout>(cpu).SingleOrDefault(),
                "Classic CPU layout");
            TestSupport.Equal(0, FindVisualDescendants<TraceworkCpuLayout>(cpu).Count(),
                "Tracework layout count in Classic CPU");
            TestSupport.True(ReferenceEquals(data, classic.DataContext), "Classic CPU DataContext");
            TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(classic).Count(),
                "Classic CPU realtime chart count");
            TestSupport.Equal(1, FindVisualDescendants<DataGrid>(classic).Count(),
                "Classic CPU DataGrid count");
        });
    }

    private static void TraceworkCpuTemplateInstantiates()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for CPU template smoke");
        try
        {
            CpuView cpu = CreateCpuView(out CpuViewModel data);
            ThemeContext.SetCurrentTheme(cpu, AppTheme.Tracework);
            WithHostedView(cpu, LayoutSize, _ =>
            {
                TraceworkCpuLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkCpuLayout>(cpu).SingleOrDefault(),
                    "Tracework CPU layout");
                TestSupport.Equal(0, FindVisualDescendants<ClassicCpuLayout>(cpu).Count(),
                    "Classic layout count in Tracework CPU");
                TestSupport.True(ReferenceEquals(data, tracework.DataContext), "Tracework CPU DataContext");
                TestSupport.Equal(3, FindVisualDescendants<TraceworkPanel>(tracework).Count(),
                    "Tracework CPU panel count");
                TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(tracework).Count(),
                    "Tracework CPU realtime chart count");
                TestSupport.Equal(1, FindVisualDescendants<DataGrid>(tracework).Count(),
                    "Tracework CPU DataGrid count");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void TraceworkCpuLaysOutAtMinimumSize()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for minimum CPU smoke");
        try
        {
            CpuView cpu = CreateCpuView(out _);
            ThemeContext.SetCurrentTheme(cpu, AppTheme.Tracework);
            WithHostedView(cpu, MinimumLayoutSize, _ =>
            {
                TraceworkCpuLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkCpuLayout>(cpu).SingleOrDefault(),
                    "Tracework CPU at minimum size");
                ScrollViewer scrollViewer = TestSupport.NotNull(
                    FindVisualDescendant<ScrollViewer>(tracework),
                    "Tracework CPU ScrollViewer");
                TestSupport.True(tracework.ActualWidth > 0d && tracework.ActualHeight > 0d,
                    "Tracework CPU minimum layout size");
                TestSupport.Equal(0d, scrollViewer.ScrollableWidth, "Tracework CPU horizontal overflow");
                TestSupport.True(FindPanel(tracework, "CPU.10").ActualWidth > 0d,
                    "Tracework CPU primary panel width");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void CpuThemeSwitchPreservesChartStateAndDataContext()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic before CPU switch smoke");
        CpuView cpu = CreateCpuView(out CpuViewModel data);
        object dataContext = cpu.DataContext;
        object metrics = data.Metrics;
        object rows = data.CoreRows;
        object charts = data.Charts;
        RealtimeMetricChartViewModel[] chartReferences = data.Charts.ToArray();
        IReadOnlyList<double>[] valueReferences = data.Charts.Select(chart => chart.Values).ToArray();
        int window = data.SelectedChartWindowSeconds;

        try
        {
            WithHostedView(cpu, MinimumLayoutSize, host =>
            {
                TestSupport.Equal(1, FindVisualDescendants<ClassicCpuLayout>(cpu).Count(),
                    "Classic layout before CPU switch");
                TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(cpu).Count(),
                    "CPU charts before switch");

                TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework during CPU switch");
                ThemeContext.SetCurrentTheme(cpu, AppTheme.Tracework);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                host.UpdateLayout();

                TestSupport.Equal(0, FindVisualDescendants<ClassicCpuLayout>(cpu).Count(),
                    "Classic layout after CPU switch");
                TestSupport.Equal(1, FindVisualDescendants<TraceworkCpuLayout>(cpu).Count(),
                    "Tracework layout after CPU switch");
                TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(cpu).Count(),
                    "CPU charts after switch");
                TestSupport.True(ReferenceEquals(dataContext, cpu.DataContext), "CPU DataContext after switch");
                TestSupport.True(ReferenceEquals(metrics, data.Metrics), "CPU Metrics after switch");
                TestSupport.True(ReferenceEquals(rows, data.CoreRows), "CPU CoreRows after switch");
                TestSupport.True(ReferenceEquals(charts, data.Charts), "CPU Charts after switch");
                TestSupport.True(chartReferences.SequenceEqual(data.Charts), "CPU chart references after switch");
                for (int index = 0; index < valueReferences.Length; index++)
                {
                    TestSupport.True(ReferenceEquals(valueReferences[index], data.Charts[index].Values),
                        $"CPU chart Values reference after switch {index}");
                }
                TestSupport.Equal(window, data.SelectedChartWindowSeconds, "CPU window after switch");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void ClassicGpuTemplateInstantiates()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic for GPU template smoke");
        GpuView gpu = CreateGpuView(out GpuViewModel data, out _);
        ThemeContext.SetCurrentTheme(gpu, AppTheme.Classic);

        WithHostedView(gpu, MinimumLayoutSize, _ =>
        {
            ClassicGpuLayout classic = TestSupport.NotNull(
                FindVisualDescendants<ClassicGpuLayout>(gpu).SingleOrDefault(),
                "Classic GPU layout");
            TestSupport.Equal(0, FindVisualDescendants<TraceworkGpuLayout>(gpu).Count(),
                "Tracework layout count in Classic GPU");
            TestSupport.True(ReferenceEquals(data, classic.DataContext), "Classic GPU DataContext");
            TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(classic).Count(),
                "Classic GPU realtime chart count");
            TestSupport.Equal(1, FindVisualDescendants<DataGrid>(classic).Count(),
                "Classic GPU DataGrid count");
        });
    }

    private static void TraceworkGpuTemplateInstantiates()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for GPU template smoke");
        try
        {
            GpuView gpu = CreateGpuView(out GpuViewModel data, out _);
            ThemeContext.SetCurrentTheme(gpu, AppTheme.Tracework);
            WithHostedView(gpu, LayoutSize, _ =>
            {
                TraceworkGpuLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkGpuLayout>(gpu).SingleOrDefault(),
                    "Tracework GPU layout");
                TestSupport.Equal(0, FindVisualDescendants<ClassicGpuLayout>(gpu).Count(),
                    "Classic layout count in Tracework GPU");
                TestSupport.True(ReferenceEquals(data, tracework.DataContext), "Tracework GPU DataContext");
                TestSupport.Equal(4, FindVisualDescendants<TraceworkPanel>(tracework).Count(),
                    "Tracework GPU panel count");
                TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(tracework).Count(),
                    "Tracework GPU realtime chart count");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void TraceworkGpuLaysOutAtMinimumSize()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for minimum GPU smoke");
        try
        {
            GpuView gpu = CreateGpuView(out _, out _);
            ThemeContext.SetCurrentTheme(gpu, AppTheme.Tracework);
            WithHostedView(gpu, MinimumLayoutSize, _ =>
            {
                TraceworkGpuLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkGpuLayout>(gpu).SingleOrDefault(),
                    "Tracework GPU at minimum size");
                ScrollViewer scrollViewer = TestSupport.NotNull(
                    FindVisualDescendant<ScrollViewer>(tracework),
                    "Tracework GPU ScrollViewer");
                TestSupport.True(tracework.ActualWidth > 0d && tracework.ActualHeight > 0d,
                    "Tracework GPU minimum layout size");
                TestSupport.Equal(0d, scrollViewer.ScrollableWidth, "Tracework GPU horizontal overflow");
                TestSupport.True(FindPanel(tracework, "GPU.10").ActualWidth > 0d,
                    "Tracework GPU primary panel width");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void TraceworkGpuSelectorAndSensorMatrixInstantiate()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework for GPU selector smoke");
        try
        {
            GpuView gpu = CreateGpuView(out GpuViewModel data, out GpuDevice selectedGpu);
            ThemeContext.SetCurrentTheme(gpu, AppTheme.Tracework);
            WithHostedView(gpu, LayoutSize, _ =>
            {
                TraceworkGpuLayout tracework = TestSupport.NotNull(
                    FindVisualDescendants<TraceworkGpuLayout>(gpu).SingleOrDefault(),
                    "Tracework GPU selector layout");
                ComboBox selector = TestSupport.NotNull(
                    FindVisualDescendants<ComboBox>(tracework)
                        .SingleOrDefault(comboBox => string.Equals(comboBox.Name, "GpuDeviceSelector", StringComparison.Ordinal)),
                    "Tracework GPU device selector");
                DataGrid sensorGrid = TestSupport.NotNull(
                    FindVisualDescendants<DataGrid>(tracework)
                        .SingleOrDefault(dataGrid => string.Equals(dataGrid.Name, "GpuSensorDataGrid", StringComparison.Ordinal)),
                    "Tracework GPU sensor DataGrid");

                TestSupport.True(selector.IsKeyboardFocusWithin || selector.Focusable,
                    "Tracework GPU selector keyboard usability");
                TestSupport.True(ReferenceEquals(selectedGpu, selector.SelectedItem),
                    "Tracework GPU selector selected item");
                TestSupport.Equal(data.GpuDevices.Count, selector.Items.Count, "Tracework GPU selector item count");
                TestSupport.Equal(data.SensorRows.Count, sensorGrid.Items.Count, "Tracework GPU sensor row count");
                sensorGrid.ScrollIntoView(sensorGrid.Items[0]);
                sensorGrid.UpdateLayout();
                TestSupport.NotNull(FindVisualDescendant<DataGridCell>(sensorGrid),
                    "Tracework GPU sensor DataGrid cells");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void GpuThemeSwitchPreservesSelectionAndDataContext()
    {
        ThemeService themeService = GetThemeService();
        TestSupport.True(themeService.ApplyTheme(AppTheme.Classic), "apply Classic before GPU switch smoke");
        GpuView gpu = CreateGpuView(out GpuViewModel data, out GpuDevice selectedGpu);
        object dataContext = gpu.DataContext;
        object devices = data.GpuDevices;
        object metrics = data.Metrics;
        object infoItems = data.InfoItems;
        object rows = data.SensorRows;
        object charts = data.Charts;
        RealtimeMetricChartViewModel[] chartReferences = data.Charts.ToArray();
        IReadOnlyList<double>[] valueReferences = data.Charts.Select(chart => chart.Values).ToArray();
        int window = data.SelectedChartWindowSeconds;
        string? chartGpuKey = typeof(GpuViewModel)
            .GetField("chartGpuKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .GetValue(data) as string;

        try
        {
            WithHostedView(gpu, MinimumLayoutSize, host =>
            {
                TestSupport.Equal(1, FindVisualDescendants<ClassicGpuLayout>(gpu).Count(),
                    "Classic layout before GPU switch");

                TestSupport.True(themeService.ApplyTheme(AppTheme.Tracework), "apply Tracework during GPU switch");
                ThemeContext.SetCurrentTheme(gpu, AppTheme.Tracework);
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                host.UpdateLayout();

                TestSupport.Equal(0, FindVisualDescendants<ClassicGpuLayout>(gpu).Count(),
                    "Classic layout after GPU switch");
                TestSupport.Equal(1, FindVisualDescendants<TraceworkGpuLayout>(gpu).Count(),
                    "Tracework layout after GPU switch");
                TestSupport.Equal(4, FindVisualDescendants<RealtimeLineChart>(gpu).Count(),
                    "GPU charts after switch");
                TestSupport.True(ReferenceEquals(dataContext, gpu.DataContext), "GPU DataContext after switch");
                TestSupport.True(ReferenceEquals(devices, data.GpuDevices), "GPU devices after switch");
                TestSupport.True(ReferenceEquals(metrics, data.Metrics), "GPU Metrics after switch");
                TestSupport.True(ReferenceEquals(infoItems, data.InfoItems), "GPU InfoItems after switch");
                TestSupport.True(ReferenceEquals(rows, data.SensorRows), "GPU SensorRows after switch");
                TestSupport.True(ReferenceEquals(charts, data.Charts), "GPU Charts after switch");
                TestSupport.True(ReferenceEquals(selectedGpu, data.SelectedGpu), "GPU selection after switch");
                TestSupport.True(chartReferences.SequenceEqual(data.Charts), "GPU chart references after switch");
                for (int index = 0; index < valueReferences.Length; index++)
                {
                    TestSupport.True(ReferenceEquals(valueReferences[index], data.Charts[index].Values),
                        $"GPU chart Values reference after switch {index}");
                }
                TestSupport.Equal(window, data.SelectedChartWindowSeconds, "GPU window after switch");
                TestSupport.Equal(chartGpuKey, typeof(GpuViewModel)
                    .GetField("chartGpuKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
                    .GetValue(data) as string, "GPU chart identity after switch");
            });
        }
        finally
        {
            themeService.ApplyTheme(AppTheme.Classic);
        }
    }

    private static void ClassicMemoryTemplateInstantiates() => AssertHardwareTemplate(
        CreateMemoryView(out _), AppTheme.Classic, typeof(ClassicMemoryLayout), typeof(TraceworkMemoryLayout), "Memory", 0);

    private static void TraceworkMemoryTemplateInstantiates() => AssertHardwareTemplate(
        CreateMemoryView(out _), AppTheme.Tracework, typeof(TraceworkMemoryLayout), typeof(ClassicMemoryLayout), "Memory", 3);

    private static void TraceworkMemoryLaysOutAtMinimumSize() => AssertHardwareMinimumLayout(
        CreateMemoryView(out _), typeof(TraceworkMemoryLayout), "MEM.10", "Memory");

    private static void MemoryModuleTopologyInstantiates()
    {
        MemoryView view = CreateMemoryView(out MemoryViewModel data);
        AssertNamedItemsControl(view, "MemoryModuleTopology", data.MemoryModules.Count, "Memory module topology");
    }

    private static void ClassicDiskTemplateInstantiates() => AssertHardwareTemplate(
        CreateDiskView(out _), AppTheme.Classic, typeof(ClassicDiskLayout), typeof(TraceworkDiskLayout), "Disk", 0);

    private static void TraceworkDiskTemplateInstantiates() => AssertHardwareTemplate(
        CreateDiskView(out _), AppTheme.Tracework, typeof(TraceworkDiskLayout), typeof(ClassicDiskLayout), "Disk", 3);

    private static void TraceworkDiskLaysOutAtMinimumSize() => AssertHardwareMinimumLayout(
        CreateDiskView(out _), typeof(TraceworkDiskLayout), "DSK.10", "Disk");

    private static void DiskDeviceNodesInstantiate()
    {
        DiskView view = CreateDiskView(out DiskViewModel data);
        AssertNamedItemsControl(view, "DiskDeviceNodes", data.DiskDevices.Count, "Disk device nodes");
    }

    private static void ClassicNetworkTemplateInstantiates() => AssertHardwareTemplate(
        CreateNetworkView(out _), AppTheme.Classic, typeof(ClassicNetworkLayout), typeof(TraceworkNetworkLayout), "Network", 0);

    private static void TraceworkNetworkTemplateInstantiates() => AssertHardwareTemplate(
        CreateNetworkView(out _), AppTheme.Tracework, typeof(TraceworkNetworkLayout), typeof(ClassicNetworkLayout), "Network", 3);

    private static void TraceworkNetworkLaysOutAtMinimumSize() => AssertHardwareMinimumLayout(
        CreateNetworkView(out _), typeof(TraceworkNetworkLayout), "NET.10", "Network");

    private static void NetworkSelectorAndToggleInstantiate()
    {
        NetworkView view = CreateNetworkView(out NetworkViewModel data);
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, LayoutSize, _ =>
        {
            ComboBox selector = TestSupport.NotNull(
                FindVisualDescendants<ComboBox>(view).SingleOrDefault(control => control.Name == "NetworkAdapterSelector"),
                "Tracework Network selector");
            CheckBox toggle = TestSupport.NotNull(
                FindVisualDescendants<CheckBox>(view).SingleOrDefault(control => control.Name == "NetworkVirtualAdapterToggle"),
                "Tracework Network virtual-adapter toggle");
            TestSupport.True(selector.Focusable, "Tracework Network selector focusable");
            TestSupport.True(toggle.Focusable, "Tracework Network toggle focusable");
            TestSupport.True(ReferenceEquals(data.SelectedAdapter, selector.SelectedItem), "Tracework Network selected adapter");
            TestSupport.Equal(data.ShowVirtualAdapters, toggle.IsChecked == true, "Tracework Network toggle state");
        });
    }

    private static void NetworkAdapterNodesInstantiate()
    {
        NetworkView view = CreateNetworkView(out NetworkViewModel data);
        AssertNamedItemsControl(view, "NetworkAdapterNodes", data.NetworkAdapters.Count, "Network adapter nodes");
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, LayoutSize, _ =>
        {
            TestSupport.True(FindVisualDescendants<TextBlock>(view)
                .Any(text => string.Equals(text.Text, "已连接", StringComparison.Ordinal)),
                "Network adapter status text");
        });
    }

    private static void ClassicMotherboardTemplateInstantiates() => AssertHardwareTemplate(
        CreateMotherboardView(out _), AppTheme.Classic, typeof(ClassicMotherboardLayout), typeof(TraceworkMotherboardLayout), "Motherboard", 0);

    private static void TraceworkMotherboardTemplateInstantiates() => AssertHardwareTemplate(
        CreateMotherboardView(out _), AppTheme.Tracework, typeof(TraceworkMotherboardLayout), typeof(ClassicMotherboardLayout), "Motherboard", 4);

    private static void TraceworkMotherboardLaysOutAtMinimumSize() => AssertHardwareMinimumLayout(
        CreateMotherboardView(out _), typeof(TraceworkMotherboardLayout), "BRD.10", "Motherboard");

    private static void MotherboardSensorMatrixInstantiates()
    {
        MotherboardView view = CreateMotherboardView(out MotherboardViewModel data);
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, LayoutSize, _ =>
        {
            DataGrid matrix = TestSupport.NotNull(
                FindVisualDescendants<DataGrid>(view).SingleOrDefault(control => control.Name == "MotherboardSensorGrid"),
                "Motherboard sensor matrix");
            TestSupport.Equal(data.SensorRows.Count, matrix.Items.Count, "Motherboard sensor matrix row count");
            matrix.ScrollIntoView(matrix.Items[0]);
            matrix.UpdateLayout();
            TestSupport.NotNull(FindVisualDescendant<DataGridCell>(matrix), "Motherboard sensor matrix cells");
        });
    }

    private static void FourPageThemeSwitchPreservesDataContext()
    {
        MemoryView memory = CreateMemoryView(out MemoryViewModel memoryData);
        object memoryOverview = memoryData.OverviewMetrics;
        object memoryProfessional = memoryData.ProfessionalMetrics;
        object memoryModules = memoryData.MemoryModules;
        MemoryModuleViewModel memoryModule = memoryData.MemoryModules[0];
        object memoryModuleMetrics = memoryModule.Metrics;
        AssertBidirectionalHardwareSwitch(memory, typeof(ClassicMemoryLayout), typeof(TraceworkMemoryLayout), "Memory", () =>
        {
            TestSupport.True(ReferenceEquals(memoryOverview, memoryData.OverviewMetrics), "Memory overview after switch");
            TestSupport.True(ReferenceEquals(memoryProfessional, memoryData.ProfessionalMetrics), "Memory professional after switch");
            TestSupport.True(ReferenceEquals(memoryModules, memoryData.MemoryModules), "Memory modules after switch");
            TestSupport.True(ReferenceEquals(memoryModule, memoryData.MemoryModules[0]), "Memory module after switch");
            TestSupport.True(ReferenceEquals(memoryModuleMetrics, memoryData.MemoryModules[0].Metrics), "Memory module metrics after switch");
        });

        DiskView disk = CreateDiskView(out DiskViewModel diskData);
        object diskOverview = diskData.OverviewMetrics;
        object diskProfessional = diskData.ProfessionalMetrics;
        object diskDevices = diskData.DiskDevices;
        DiskDeviceViewModel diskDevice = diskData.DiskDevices[0];
        string diskStatus = diskData.StatusText;
        AssertBidirectionalHardwareSwitch(disk, typeof(ClassicDiskLayout), typeof(TraceworkDiskLayout), "Disk", () =>
        {
            TestSupport.True(ReferenceEquals(diskOverview, diskData.OverviewMetrics), "Disk overview after switch");
            TestSupport.True(ReferenceEquals(diskProfessional, diskData.ProfessionalMetrics), "Disk professional after switch");
            TestSupport.True(ReferenceEquals(diskDevices, diskData.DiskDevices), "Disk devices after switch");
            TestSupport.True(ReferenceEquals(diskDevice, diskData.DiskDevices[0]), "Disk device after switch");
            TestSupport.Equal(diskStatus, diskData.StatusText, "Disk status after switch");
        });

        NetworkView network = CreateNetworkView(out NetworkViewModel networkData);
        object adapters = networkData.NetworkAdapters;
        NetworkAdapterItemViewModel selectedAdapter = TestSupport.NotNull(networkData.SelectedAdapter, "selected Network adapter");
        bool showVirtualAdapters = networkData.ShowVirtualAdapters;
        AssertBidirectionalHardwareSwitch(network, typeof(ClassicNetworkLayout), typeof(TraceworkNetworkLayout), "Network", () =>
        {
            TestSupport.True(ReferenceEquals(adapters, networkData.NetworkAdapters), "Network adapters after switch");
            TestSupport.True(ReferenceEquals(selectedAdapter, networkData.SelectedAdapter), "Network selection after switch");
            TestSupport.Equal(showVirtualAdapters, networkData.ShowVirtualAdapters, "Network virtual setting after switch");
        });

        MotherboardView board = CreateMotherboardView(out MotherboardViewModel boardData);
        object boardMetrics = boardData.BoardMetrics;
        object biosMetrics = boardData.BiosMetrics;
        object deviceMetrics = boardData.DeviceMetrics;
        object sensorMetrics = boardData.SensorMetrics;
        object sensorRows = boardData.SensorRows;
        string boardName = boardData.MotherboardName;
        string deviceSummary = boardData.DeviceSummary;
        AssertBidirectionalHardwareSwitch(board, typeof(ClassicMotherboardLayout), typeof(TraceworkMotherboardLayout), "Motherboard", () =>
        {
            TestSupport.True(ReferenceEquals(boardMetrics, boardData.BoardMetrics), "Motherboard board metrics after switch");
            TestSupport.True(ReferenceEquals(biosMetrics, boardData.BiosMetrics), "Motherboard BIOS metrics after switch");
            TestSupport.True(ReferenceEquals(deviceMetrics, boardData.DeviceMetrics), "Motherboard device metrics after switch");
            TestSupport.True(ReferenceEquals(sensorMetrics, boardData.SensorMetrics), "Motherboard sensor metrics after switch");
            TestSupport.True(ReferenceEquals(sensorRows, boardData.SensorRows), "Motherboard sensor rows after switch");
            TestSupport.Equal(boardName, boardData.MotherboardName, "Motherboard name after switch");
            TestSupport.Equal(deviceSummary, boardData.DeviceSummary, "Motherboard device summary after switch");
        });
    }

    private static void AssertHardwareTemplate(
        UserControl view,
        AppTheme theme,
        Type expectedLayout,
        Type excludedLayout,
        string pageName,
        int expectedPanelCount)
    {
        ThemeContext.SetCurrentTheme(view, theme);
        WithHostedView(view, LayoutSize, _ =>
        {
            FrameworkElement layout = TestSupport.NotNull(
                FindVisualDescendants<FrameworkElement>(view).SingleOrDefault(element => element.GetType() == expectedLayout),
                $"{theme} {pageName} layout");
            TestSupport.Equal(0, FindVisualDescendants<FrameworkElement>(view).Count(element => element.GetType() == excludedLayout),
                $"excluded {pageName} layout count");
            TestSupport.True(ReferenceEquals(view.DataContext, layout.DataContext), $"{pageName} layout DataContext");
            if (expectedPanelCount > 0)
            {
                TestSupport.Equal(expectedPanelCount, FindVisualDescendants<TraceworkPanel>(layout).Count(),
                    $"Tracework {pageName} panel count");
            }
        });
    }

    private static void AssertHardwareMinimumLayout(UserControl view, Type layoutType, string panelCode, string pageName)
    {
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, MinimumLayoutSize, _ =>
        {
            FrameworkElement layout = TestSupport.NotNull(
                FindVisualDescendants<FrameworkElement>(view).SingleOrDefault(element => element.GetType() == layoutType),
                $"Tracework {pageName} minimum layout");
            ScrollViewer scrollViewer = TestSupport.NotNull(FindVisualDescendant<ScrollViewer>(layout),
                $"Tracework {pageName} ScrollViewer");
            TestSupport.True(layout.ActualWidth > 0d && layout.ActualHeight > 0d, $"Tracework {pageName} minimum size");
            TestSupport.Equal(0d, scrollViewer.ScrollableWidth, $"Tracework {pageName} horizontal overflow");
            TestSupport.True(FindPanel(layout, panelCode).ActualWidth > 0d, $"Tracework {pageName} panel width");
        });
    }

    private static void AssertNamedItemsControl(UserControl view, string controlName, int expectedCount, string label)
    {
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, LayoutSize, _ =>
        {
            ItemsControl items = TestSupport.NotNull(
                FindVisualDescendants<ItemsControl>(view).SingleOrDefault(control => control.Name == controlName), label);
            TestSupport.Equal(expectedCount, items.Items.Count, $"{label} item count");
            TestSupport.True(FindVisualDescendants<ContentPresenter>(items).Any(), $"{label} content presenter");
        });
    }

    private static void AssertBidirectionalHardwareSwitch(
        UserControl view,
        Type classicLayout,
        Type traceworkLayout,
        string pageName,
        Action assertState)
    {
        ThemeContext.SetCurrentTheme(view, AppTheme.Classic);
        object dataContext = TestSupport.NotNull(view.DataContext, $"{pageName} DataContext");
        WithHostedView(view, MinimumLayoutSize, host =>
        {
            TestSupport.Equal(1, FindVisualDescendants<FrameworkElement>(view).Count(element => element.GetType() == classicLayout),
                $"Classic {pageName} before switch");

            ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            TestSupport.Equal(0, FindVisualDescendants<FrameworkElement>(view).Count(element => element.GetType() == classicLayout),
                $"Classic {pageName} after forward switch");
            TestSupport.Equal(1, FindVisualDescendants<FrameworkElement>(view).Count(element => element.GetType() == traceworkLayout),
                $"Tracework {pageName} after forward switch");
            TestSupport.True(ReferenceEquals(dataContext, view.DataContext), $"{pageName} DataContext after forward switch");
            assertState();

            ThemeContext.SetCurrentTheme(view, AppTheme.Classic);
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            host.UpdateLayout();
            TestSupport.Equal(1, FindVisualDescendants<FrameworkElement>(view).Count(element => element.GetType() == classicLayout),
                $"Classic {pageName} after reverse switch");
            TestSupport.Equal(0, FindVisualDescendants<FrameworkElement>(view).Count(element => element.GetType() == traceworkLayout),
                $"Tracework {pageName} after reverse switch");
            TestSupport.True(ReferenceEquals(dataContext, view.DataContext), $"{pageName} DataContext after reverse switch");
            assertState();
        });
    }

    private static void DashboardArchitectureStaticChecks()
    {
        string repositoryRoot = FindRepositoryRoot();
        string applicationRoot = Path.Combine(repositoryRoot, "HardwareVision");
        string pagesPath = Path.Combine(applicationRoot, "Themes", "Tracework", "Pages.xaml");
        string pages = File.ReadAllText(pagesPath);
        Regex unkeyedStyle = new(
            """<Style(?=\s|>)(?![^>]*\bx:Key\s*=)[^>]*>""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        TestSupport.False(unkeyedStyle.IsMatch(pages), "Tracework Pages contains an unkeyed Style");

        string dashboardDirectory = Path.Combine(applicationRoot, "Views", "Dashboard");
        string[] dashboardXamlFiles =
        [
            Path.Combine(applicationRoot, "Views", "DashboardView.xaml"),
            .. Directory.EnumerateFiles(dashboardDirectory, "*.xaml", SearchOption.TopDirectoryOnly),
            pagesPath
        ];
        string dashboardXaml = string.Join(Environment.NewLine, dashboardXamlFiles.Select(File.ReadAllText));
        Regex forbiddenExternalMaterial = new(
            """((?<![A-Z])[A-Z]:[\\/]|file:|pack://siteoforigin|<\s*(Image|ImageBrush|BitmapImage)\b|\b(Source|UriSource)\s*=)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        TestSupport.False(forbiddenExternalMaterial.IsMatch(dashboardXaml),
            "Dashboard XAML contains a local path or external/image material reference");

        string[] forbiddenMotion =
        [
            "Storyboard", "DoubleAnimation", "ThicknessAnimation", "ColorAnimation",
            "CompositionTarget.Rendering", "PixelShader", "BlurEffect", "DispatcherTimer"
        ];
        TestSupport.False(forbiddenMotion.Any(dashboardXaml.Contains),
            "Dashboard XAML contains forbidden motion or shader code");
        TestSupport.False(dashboardXaml.Contains("Binding CurrentPage", StringComparison.Ordinal),
            "Dashboard XAML contains a shell PageHost binding");
        TestSupport.False(dashboardXaml.Contains("new DashboardViewModel", StringComparison.Ordinal),
            "Dashboard layout creates a DashboardViewModel");
        TestSupport.False(dashboardXaml.Contains("ApplyTheme", StringComparison.Ordinal),
            "Dashboard layout applies a theme");
        TestSupport.False(dashboardXaml.Contains("Navigate", StringComparison.Ordinal),
            "Dashboard layout performs navigation");
        TestSupport.False(dashboardXaml.Contains("RefreshHardwareInfo", StringComparison.Ordinal),
            "Dashboard layout triggers hardware refresh");
        TestSupport.False(dashboardXaml.Contains("PollingService", StringComparison.Ordinal),
            "Dashboard layout subscribes to polling");

        string dashboardViewModel = File.ReadAllText(
            Path.Combine(applicationRoot, "ViewModels", "DashboardViewModel.cs"));
        TestSupport.False(dashboardViewModel.Contains("OverviewCards[", StringComparison.Ordinal),
            "DashboardViewModel still updates cards by collection index");
        TestSupport.False(dashboardViewModel.Contains("IThemeService", StringComparison.Ordinal),
            "DashboardViewModel depends on IThemeService");

        string appXaml = File.ReadAllText(Path.Combine(applicationRoot, "App.xaml"));
        TestSupport.True(appXaml.Contains("Themes/Tracework/Pages.xaml", StringComparison.Ordinal),
            "Tracework Pages resource merge");
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

    private static DashboardView CreateDashboardView(out DashboardSmokeData data)
    {
        data = new DashboardSmokeData();
        return new DashboardView { DataContext = data };
    }

    private static HardwareOverviewCardViewModel CreateSmokeCard(
        HardwareOverviewKind kind,
        string title,
        string hardwareName,
        bool hasSelector = false)
    {
        HardwareOverviewCardViewModel card = new(kind)
        {
            Title = title,
            HardwareName = hardwareName,
            HeaderNote = "Synthetic source"
        };
        card.Metrics.Add(new DetailMetricViewModel("Primary metric", "50%"));
        card.Metrics.Add(new DetailMetricViewModel("Secondary metric", "42"));
        card.Metrics.Add(new DetailMetricViewModel("Tertiary metric", "18 W"));
        if (hasSelector)
        {
            card.UpdateHardwareOptions(
                [("device-0", $"{hardwareName} 0"), ("device-1", $"{hardwareName} 1")],
                "device-0",
                _ => { });
        }

        return card;
    }

    private static TraceworkPanel FindPanel(DependencyObject root, string code) =>
        TestSupport.NotNull(
            FindVisualDescendants<TraceworkPanel>(root)
                .SingleOrDefault(panel => string.Equals(panel.Code, code, StringComparison.Ordinal)),
            $"Tracework panel {code}");

    private static ComboBox FindSelector(DependencyObject root, HardwareOverviewKind kind)
    {
        string panelCode = kind switch
        {
            HardwareOverviewKind.Gpu => "GPU.02",
            HardwareOverviewKind.Disk => "DSK.04",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No selector fixture for this card kind.")
        };
        TraceworkPanel panel = FindPanel(root, panelCode);
        return TestSupport.NotNull(
            FindVisualDescendants<ComboBox>(panel)
                .FirstOrDefault(selector =>
                    selector.Visibility == Visibility.Visible
                    && selector.ActualWidth > 0d
                    && selector.DataContext is HardwareOverviewCardViewModel card
                    && card.Kind == kind),
            $"visible Tracework selector {kind}");
    }

    private static void AssertCardReferences(
        DashboardSmokeData data,
        IReadOnlyList<HardwareOverviewCardViewModel> expected,
        string phase)
    {
        TestSupport.Equal(expected.Count, data.OverviewCards.Count, $"card count after {phase}");
        for (int index = 0; index < expected.Count; index++)
        {
            TestSupport.True(
                ReferenceEquals(expected[index], data.OverviewCards[index]),
                $"card reference {index} after {phase}");
        }
    }

    private static MemoryView CreateMemoryView(out MemoryViewModel data)
    {
        data = new MemoryViewModel();
        data.OverviewMetrics.Add(new DetailMetricViewModel("总物理内存", "32.0 GB"));
        data.OverviewMetrics.Add(new DetailMetricViewModel("已用物理内存", "12.0 GB"));
        data.OverviewMetrics.Add(new DetailMetricViewModel("隐藏提交容量", "48.0 GB") { IsVisible = false });
        data.ProfessionalMetrics.Add(new DetailMetricViewModel("已安装内存条数量", "2"));
        data.ProfessionalMetrics.Add(new DetailMetricViewModel("ConfiguredClockSpeed", "5600 MHz"));
        MemoryModuleViewModel module = new()
        {
            SlotName = "DIMM_A1",
            ModuleName = "Synthetic Memory Module With A Deliberately Long Part Number"
        };
        module.Metrics.Add(new DetailMetricViewModel("容量", "16.0 GB"));
        module.Metrics.Add(new DetailMetricViewModel("ConfiguredClockSpeed", "5600 MHz"));
        module.Metrics.Add(new DetailMetricViewModel("SerialNumber", "hidden") { IsVisible = false });
        data.MemoryModules.Add(module);
        SetPrivateProperty(data, nameof(MemoryViewModel.HasMemoryModules), true);
        return new MemoryView { DataContext = data };
    }

    private static DiskView CreateDiskView(out DiskViewModel data)
    {
        data = new DiskViewModel();
        data.OverviewMetrics.Add(new DetailMetricViewModel("硬盘数量", "2"));
        data.OverviewMetrics.Add(new DetailMetricViewModel("总容量", "3.0 TB"));
        data.ProfessionalMetrics.Add(new DetailMetricViewModel("健康状态", "Healthy"));
        data.ProfessionalMetrics.Add(new DetailMetricViewModel("最低剩余寿命", "97 %"));
        DiskDeviceViewModel device = new()
        {
            Name = "Synthetic NVMe Storage Device With A Deliberately Long Model Name",
            Subtitle = "NVMe / PCIe / Synthetic Bridge Controller"
        };
        device.Metrics.Add(new DetailMetricViewModel("型号", "Synthetic Model 123456789"));
        device.Metrics.Add(new DetailMetricViewModel("固件", "FW-1.2.3-SYNTHETIC"));
        device.Metrics.Add(new DetailMetricViewModel("温度", "42 °C"));
        device.Metrics.Add(new DetailMetricViewModel("SerialNumber", "hidden") { IsVisible = false });
        data.DiskDevices.Add(device);
        return new DiskView { DataContext = data };
    }

    private static NetworkView CreateNetworkView(out NetworkViewModel data)
    {
        data = new NetworkViewModel();
        NetworkAdapterItemViewModel physical = new()
        {
            Device = new NetworkAdapterDevice
            {
                Id = "network-primary",
                Name = "Synthetic Ethernet Adapter With A Deliberately Long Interface Name",
                Description = "Synthetic PCIe 2.5 Gigabit Ethernet Controller",
                IsUp = true,
                LinkSpeed = 2_500_000_000UL,
                DownloadSpeed = 12_000_000d,
                UploadSpeed = 2_000_000d,
                IPv4Addresses = ["192.0.2.42"],
                IPv6Addresses = ["2001:db8:0000:0000:0000:0000:0000:0042"],
                DnsServers = ["2001:4860:4860::8888"],
                Gateway = "192.0.2.1",
                Source = "Synthetic"
            }
        };
        physical.Metrics.Add(new DetailMetricViewModel("状态", "已连接"));
        physical.Metrics.Add(new DetailMetricViewModel("IPv4", "192.0.2.42"));
        data.NetworkAdapters.Add(physical);
        data.SelectedAdapter = physical;
        data.ShowVirtualAdapters = true;
        return new NetworkView { DataContext = data };
    }

    private static MotherboardView CreateMotherboardView(out MotherboardViewModel data)
    {
        data = new MotherboardViewModel();
        data.BoardMetrics.Add(new DetailMetricViewModel("主板厂商", "Synthetic Vendor"));
        data.BoardMetrics.Add(new DetailMetricViewModel("主板型号", "Synthetic Board Model With A Long Identifier"));
        data.BiosMetrics.Add(new DetailMetricViewModel("BIOS 版本", "TEST-UEFI-1.2.3"));
        data.BiosMetrics.Add(new DetailMetricViewModel("Secure Boot", "已启用"));
        data.DeviceMetrics.Add(new DetailMetricViewModel("设备类型", "工作站"));
        data.SensorMetrics.Add(new DetailMetricViewModel("主板温度", "41 °C"));
        IReadOnlyList<DetailSensorRowViewModel> rows =
        [
            DetailSensorRowViewModel.FromReading(CreateReading(
                "Synthetic Motherboard", "VRM Temperature", SensorCategory.Motherboard, SensorType.Temperature, 48d, "°C"))
        ];
        SetPrivateProperty(data, nameof(MotherboardViewModel.SensorRows), rows);
        SetPrivateProperty(data, nameof(MotherboardViewModel.MotherboardName), "Synthetic Board With A Deliberately Long Product Name");
        SetPrivateProperty(data, nameof(MotherboardViewModel.DeviceSummary), "Synthetic Workstation Chassis");
        SetPrivateProperty(data, nameof(MotherboardViewModel.HasMotherboardSensors), true);
        SetPrivateProperty(data, nameof(MotherboardViewModel.NoSensorData), false);
        return new MotherboardView { DataContext = data };
    }

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        System.Reflection.PropertyInfo property = TestSupport.NotNull(
            target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public),
            $"{target.GetType().Name}.{propertyName}");
        property.SetValue(target, value);
    }

    private static CpuView CreateCpuView(out CpuViewModel data)
    {
        data = new CpuViewModel();
        data.Metrics.Add(new DetailMetricViewModel("Package temperature", "58.0 °C"));
        data.Metrics.Add(new DetailMetricViewModel("Total load", "42.0 %"));
        data.Metrics.Add(new DetailMetricViewModel("Hidden channel", "--") { IsVisible = false });
        data.Metrics.Add(new DetailMetricViewModel("Package power", "76.0 W"));
        data.CoreRows.Add(DetailSensorRowViewModel.FromReading(CreateReading(
            "Synthetic CPU",
            "Core 0 Load",
            SensorCategory.Cpu,
            SensorType.Load,
            42d,
            "%")));
        for (int index = 0; index < data.Charts.Count; index++)
        {
            data.Charts[index].Append(20d + index * 5d);
            data.Charts[index].Append(24d + index * 5d);
        }
        data.SelectedChartWindowSeconds = 120;
        return new CpuView { DataContext = data };
    }

    private static GpuView CreateGpuView(out GpuViewModel data, out GpuDevice selectedGpu)
    {
        data = new GpuViewModel();
        selectedGpu = CreateGpuDevice(
            "gpu-primary",
            "Synthetic Graphics Adapter With A Deliberately Long Display Name",
            62d);
        GpuDevice secondaryGpu = CreateGpuDevice("gpu-secondary", "Synthetic Integrated Adapter", 18d);
        data.GpuDevices.Add(selectedGpu);
        data.GpuDevices.Add(secondaryGpu);
        data.SelectedGpu = selectedGpu;
        data.SelectedChartWindowSeconds = 120;
        return new GpuView { DataContext = data };
    }

    private static GpuDevice CreateGpuDevice(string id, string name, double loadValue)
    {
        SensorReading load = CreateReading(name, "GPU Core Load", SensorCategory.Gpu, SensorType.Load, loadValue, "%");
        SensorReading temperature = CreateReading(name, "GPU Core Temperature", SensorCategory.Gpu, SensorType.Temperature, 58d, "°C");
        SensorReading power = CreateReading(name, "GPU Package Power", SensorCategory.Gpu, SensorType.Power, 132d, "W");
        SensorReading clock = CreateReading(name, "GPU Core Clock", SensorCategory.Gpu, SensorType.Clock, 1980d, "MHz");
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

    private static SensorReading CreateReading(
        string deviceName,
        string sensorName,
        SensorCategory category,
        SensorType type,
        double value,
        string unit) => new()
    {
        DeviceName = deviceName,
        SensorName = sensorName,
        Category = category,
        Type = type,
        Value = value,
        Unit = unit,
        IsAvailable = true,
        Availability = SensorAvailability.Available,
        Source = "Synthetic"
    };

    private static void LayoutDashboardView()
    {
        DashboardView view = CreateDashboardView(out _);
        ThemeContext.SetCurrentTheme(view, AppTheme.Classic);
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

    private static ThemeService GetThemeService() =>
        sharedThemeService ??= new ThemeService(GetApplication());

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

    private sealed class DashboardSmokeData
    {
        public DashboardSmokeData()
        {
            CpuOverviewCard = CreateSmokeCard(HardwareOverviewKind.Cpu, "CPU", "Synthetic CPU");
            GpuOverviewCard = CreateSmokeCard(HardwareOverviewKind.Gpu, "GPU", "Synthetic GPU", hasSelector: true);
            MemoryOverviewCard = CreateSmokeCard(HardwareOverviewKind.Memory, "内存", "Synthetic Memory");
            DiskOverviewCard = CreateSmokeCard(HardwareOverviewKind.Disk, "硬盘", "Synthetic Disk", hasSelector: true);
            NetworkOverviewCard = CreateSmokeCard(HardwareOverviewKind.Network, "网络", "Synthetic Network");
            SystemOverviewCard = CreateSmokeCard(HardwareOverviewKind.System, "主板 / 系统", "Synthetic System");
            OverviewCards =
            [
                CpuOverviewCard,
                GpuOverviewCard,
                MemoryOverviewCard,
                DiskOverviewCard,
                NetworkOverviewCard,
                SystemOverviewCard
            ];
        }

        public string ApplicationName => "HardwareVision.Tests";
        public string DeviceName => "Synthetic Device With A Deliberately Long Name";
        public string? DeviceNameToolTip => DeviceName;
        public string OperatingSystem => "Windows Test Host With A Deliberately Long Platform Name";
        public string? OperatingSystemToolTip => OperatingSystem;
        public string LastRefreshTime => "Now";
        public string LoadMessage => "Synthetic hardware data is ready";
        public IReadOnlyList<HardwareOverviewCardViewModel> OverviewCards { get; }
        public HardwareOverviewCardViewModel CpuOverviewCard { get; }
        public HardwareOverviewCardViewModel GpuOverviewCard { get; }
        public HardwareOverviewCardViewModel MemoryOverviewCard { get; }
        public HardwareOverviewCardViewModel DiskOverviewCard { get; }
        public HardwareOverviewCardViewModel NetworkOverviewCard { get; }
        public HardwareOverviewCardViewModel SystemOverviewCard { get; }
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
