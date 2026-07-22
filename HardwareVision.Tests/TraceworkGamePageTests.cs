using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Themes;
using HardwareVision.ViewModels;
using HardwareVision.Views;
using HardwareVision.Views.GamePerformance;
using HardwareVision.Views.GameSessionReport;

namespace HardwareVision.Tests;

internal static class TraceworkGamePageTests
{
    private static readonly Size LayoutSize = new(1280d, 900d);
    private static readonly Size MinimumLayoutSize = new(920d, 620d);

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("XAML 62 Classic Game Performance layout instantiates", ClassicGamePerformanceLayoutInstantiates),
        ("XAML 63 Tracework Game Performance layout instantiates", TraceworkGamePerformanceLayoutInstantiates),
        ("XAML 64 Tracework Game Performance lays out at 920x620", TraceworkGamePerformanceMinimumLayout),
        ("XAML 65 game capture controls instantiate", GameCaptureControlsInstantiate),
        ("XAML 66 live metrics and charts instantiate", LiveMetricsAndChartsInstantiate),
        ("XAML 67 performance limit events instantiate", PerformanceLimitEventsInstantiate),
        ("XAML 68 session archive and paging instantiate", SessionArchiveAndPagingInstantiate),
        ("XAML 69 Game Performance theme switch preserves state", GamePerformanceSwitchPreservesState),
        ("XAML 70 Classic Game Session Report layout instantiates", ClassicGameSessionReportLayoutInstantiates),
        ("XAML 71 Tracework Game Session Report layout instantiates", TraceworkGameSessionReportLayoutInstantiates),
        ("XAML 72 Tracework Game Session Report lays out at 920x620", TraceworkGameSessionReportMinimumLayout),
        ("XAML 73 session report chart and selector instantiate", SessionReportChartAndSelectorInstantiate),
        ("XAML 74 report limit events and warnings instantiate", ReportLimitEventsAndWarningsInstantiate),
        ("XAML 75 report theme switch preserves loaded state", ReportSwitchPreservesLoadedState),
        ("XAML 76 open report remains open during bidirectional switch", OpenReportRemainsOpenDuringSwitch),
        ("Game pages 01 layouts are presentation-only controls", LayoutsArePresentationOnlyControls),
        ("Game pages 02 architecture and resources are isolated", ArchitectureAndResourcesAreIsolated),
        ("Game pages 03 pure theme switching has zero side effects", PureThemeSwitchHasZeroSideEffects),
        ("Game pages 04 capturing state survives theme switching", CapturingStateSurvivesThemeSwitching),
        ("Game pages 05 loaded report does not reload or close", LoadedReportDoesNotReloadOrClose)
    ];

    private static void ClassicGamePerformanceLayoutInstantiates() => WithPerformanceFixture(fixture =>
        AssertTemplate(fixture.View, AppTheme.Classic, typeof(ClassicGamePerformanceLayout), typeof(TraceworkGamePerformanceLayout)));

    private static void TraceworkGamePerformanceLayoutInstantiates() => WithPerformanceFixture(fixture =>
        AssertTemplate(fixture.View, AppTheme.Tracework, typeof(TraceworkGamePerformanceLayout), typeof(ClassicGamePerformanceLayout)));

    private static void TraceworkGamePerformanceMinimumLayout() => WithPerformanceFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, MinimumLayoutSize, _ =>
        {
            TraceworkGamePerformanceLayout layout = FindSingle<TraceworkGamePerformanceLayout>(fixture.View, "Tracework Game Performance layout");
            ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkGamePerformanceScrollViewer");
            TestSupport.Equal(0d, scroll.ScrollableWidth, "Game Performance horizontal overflow");
            TestSupport.True(layout.ActualWidth > 0d && layout.ActualHeight > 0d, "Game Performance minimum layout size");
        });
    });

    private static void GameCaptureControlsInstantiate() => WithPerformanceFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            TestSupport.NotNull(FindVisualDescendants<TextBox>(fixture.View).SingleOrDefault(textBox => textBox.Name == "TraceworkProcessSearchBox"), "process search");
            ComboBox selector = FindVisualDescendants<ComboBox>(fixture.View).First(combo => ReferenceEquals(combo.ItemsSource, fixture.ViewModel.ProcessOptions));
            TestSupport.True(ReferenceEquals(fixture.ViewModel.SelectedProcess, selector.SelectedItem), "process selection");
            string[] controls = FindVisualDescendants<Button>(fixture.View).Select(button => button.Content?.ToString() ?? string.Empty).ToArray();
            foreach (string label in new[] { "刷新", "识别游戏", "开始", "停止" }) TestSupport.True(controls.Contains(label), $"capture button {label}");
        });
    });

    private static void LiveMetricsAndChartsInstantiate() => WithPerformanceFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            TestSupport.True(FindVisualDescendants<ItemsControl>(fixture.View).Any(items => ReferenceEquals(items.ItemsSource, fixture.ViewModel.Metrics)), "live metric ItemsControl");
            TestSupport.Equal(5, FindVisualDescendants<RealtimeLineChart>(fixture.View).Count(), "live chart count");
            TestSupport.Equal(5, fixture.ViewModel.Charts.Count, "ViewModel chart count");
        });
    });

    private static void PerformanceLimitEventsInstantiate() => WithPerformanceFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            ItemsControl events = FindVisualDescendants<ItemsControl>(fixture.View).First(items => ReferenceEquals(items.ItemsSource, fixture.ViewModel.PerformanceLimitEvents));
            TestSupport.Equal(1, events.Items.Count, "performance-limit event count");
            TestSupport.True(FindVisualDescendants<TextBlock>(events).Any(text => Equals(text.Text, "CPU")), "performance-limit processor");
        });
    });

    private static void SessionArchiveAndPagingInstantiate() => WithPerformanceFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            ListBox archive = FindVisualDescendants<ListBox>(fixture.View).Single(list => ReferenceEquals(list.ItemsSource, fixture.ViewModel.RecentRecords));
            TestSupport.Equal(1, archive.Items.Count, "session archive record count");
            TestSupport.True(VirtualizingPanel.GetIsVirtualizing(archive), "session archive virtualization");
            TestSupport.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(archive), "session archive recycling");
            TestSupport.True(FindVisualDescendants<Button>(fixture.View).Any(button => Equals(button.Content, "显示更多")), "load-more button");
        });
    });

    private static void GamePerformanceSwitchPreservesState() => WithPerformanceFixture(fixture =>
    {
        PerformanceState state = PerformanceState.Capture(fixture.ViewModel);
        int settingsWrites = fixture.SettingsService.TotalWrites;
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchTheme(fixture.View, host, AppTheme.Tracework);
            SwitchTheme(fixture.View, host, AppTheme.Classic);
            state.AssertUnchanged(fixture.ViewModel);
            TestSupport.Equal(settingsWrites, fixture.SettingsService.TotalWrites, "settings writes during Game Performance switch");
        });
    });

    private static void ClassicGameSessionReportLayoutInstantiates() => WithReportFixture(fixture =>
        AssertTemplate(fixture.View, AppTheme.Classic, typeof(ClassicGameSessionReportLayout), typeof(TraceworkGameSessionReportLayout)));

    private static void TraceworkGameSessionReportLayoutInstantiates() => WithReportFixture(fixture =>
        AssertTemplate(fixture.View, AppTheme.Tracework, typeof(TraceworkGameSessionReportLayout), typeof(ClassicGameSessionReportLayout)));

    private static void TraceworkGameSessionReportMinimumLayout() => WithReportFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, MinimumLayoutSize, _ =>
        {
            TraceworkGameSessionReportLayout layout = FindSingle<TraceworkGameSessionReportLayout>(fixture.View, "Tracework report layout");
            ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).Single(item => item.Name == "TraceworkGameSessionReportScrollViewer");
            TestSupport.Equal(0d, scroll.ScrollableWidth, "Session Report horizontal overflow");
            TestSupport.True(layout.ActualWidth > 0d && layout.ActualHeight > 0d, "Session Report minimum layout size");
        });
    });

    private static void SessionReportChartAndSelectorInstantiate() => WithReportFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            ComboBox selector = FindVisualDescendants<ComboBox>(fixture.View).Single(combo => ReferenceEquals(combo.ItemsSource, fixture.ViewModel.Charts));
            TestSupport.True(ReferenceEquals(fixture.ViewModel.SelectedChart, selector.SelectedItem), "report SelectedChart");
            SessionTelemetryChart chart = FindSingle<SessionTelemetryChart>(fixture.View, "SessionTelemetryChart");
            TestSupport.True(ReferenceEquals(fixture.ViewModel.SelectedChart, chart.Model), "report chart model");
        });
    });

    private static void ReportLimitEventsAndWarningsInstantiate() => WithReportFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            ListBox events = FindVisualDescendants<ListBox>(fixture.View).Single(list => ReferenceEquals(list.ItemsSource, fixture.ViewModel.PerformanceLimitEvents));
            TestSupport.Equal(1, events.Items.Count, "report limit events");
            ItemsControl warnings = FindVisualDescendants<ItemsControl>(fixture.View).Single(items => ReferenceEquals(items.ItemsSource, fixture.ViewModel.Warnings));
            TestSupport.Equal(1, warnings.Items.Count, "report warnings");
        });
    });

    private static void ReportSwitchPreservesLoadedState() => WithReportFixture(fixture =>
    {
        ReportState state = ReportState.Capture(fixture.ViewModel);
        int loadCalls = fixture.ReportService.LoadCount;
        int iconCalls = fixture.IconService.LoadCount;
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchTheme(fixture.View, host, AppTheme.Tracework);
            SwitchTheme(fixture.View, host, AppTheme.Classic);
            state.AssertUnchanged(fixture.ViewModel);
            TestSupport.Equal(loadCalls, fixture.ReportService.LoadCount, "report load calls during theme switch");
            TestSupport.Equal(iconCalls, fixture.IconService.LoadCount, "icon load calls during theme switch");
            TestSupport.Equal(0, fixture.CloseCount, "report close calls during theme switch");
        });
    });

    private static void OpenReportRemainsOpenDuringSwitch() => WithPerformanceFixture(performance => WithReportFixture(report =>
    {
        SetPrivate(performance.ViewModel, "sessionReport", report.ViewModel);
        performance.ViewModel.SuspendRealtimeUiForSessionReport();
        GameSessionReportViewModel sessionReport = report.ViewModel;
        ThemeContext.SetCurrentTheme(performance.View, AppTheme.Classic);
        WithHostedView(performance.View, MinimumLayoutSize, host =>
        {
            TestSupport.Equal(0, CountLayout(performance.View, typeof(ClassicGamePerformanceLayout)), "capture layout hidden while report is open");
            TestSupport.Equal(1, FindVisualDescendants<GameSessionReportView>(performance.View).Count(), "report view before switch");
            SwitchTheme(performance.View, host, AppTheme.Tracework);
            SwitchTheme(performance.View, host, AppTheme.Classic);
            TestSupport.True(ReferenceEquals(sessionReport, performance.ViewModel.SessionReport), "open SessionReport reference");
            TestSupport.True(performance.ViewModel.HasSessionReport, "HasSessionReport remains true");
            TestSupport.False(performance.ViewModel.IsUiRefreshTimerEnabled, "realtime UI remains suspended");
            TestSupport.Equal(1, FindVisualDescendants<GameSessionReportView>(performance.View).Count(), "single report view after switch");
            TestSupport.Equal(1, report.ReportService.LoadCount, "report remains singly loaded");
        });
        SetPrivate(performance.ViewModel, "sessionReport", null);
    }));

    private static void LayoutsArePresentationOnlyControls()
    {
        Type[] layouts = [typeof(ClassicGamePerformanceLayout), typeof(TraceworkGamePerformanceLayout), typeof(ClassicGameSessionReportLayout), typeof(TraceworkGameSessionReportLayout)];
        foreach (Type type in layouts)
        {
            ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            TestSupport.Equal(1, constructors.Length, $"{type.Name} constructor count");
            TestSupport.Equal(0, constructors[0].GetParameters().Length, $"{type.Name} constructor parameters");
            TestSupport.Equal(0, type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Count(field => field.FieldType.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                    || field.FieldType.Namespace == typeof(IGamePerformanceService).Namespace), $"{type.Name} business fields");
        }
        foreach (Type type in new[] { typeof(GamePerformanceViewModel), typeof(GameSessionReportViewModel) })
            TestSupport.False(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Any(parameter => typeof(IThemeService).IsAssignableFrom(parameter.ParameterType)), $"{type.Name} ThemeService dependency");
    }

    private static void ArchitectureAndResourcesAreIsolated()
    {
        string root = FindRepositoryRoot();
        string application = Path.Combine(root, "HardwareVision");
        string performanceRoot = File.ReadAllText(Path.Combine(application, "Views", "GamePerformanceView.xaml"));
        string reportRoot = File.ReadAllText(Path.Combine(application, "Views", "GameSessionReportView.xaml"));
        TestSupport.Equal(1, Regex.Matches(performanceRoot, "x:Name=\"CaptureWorkspaceHost\"").Count, "single themed capture host");
        TestSupport.Equal(1, Regex.Matches(reportRoot, "<ContentControl(?=\\s|>)").Count, "single report theme host");
        TestSupport.True(performanceRoot.Contains("SessionReportHost", StringComparison.Ordinal), "SessionReport host");
        TestSupport.True(performanceRoot.Contains("ThemeContext.CurrentTheme", StringComparison.Ordinal), "performance inherited ThemeContext");
        TestSupport.True(reportRoot.Contains("ThemeContext.CurrentTheme", StringComparison.Ordinal), "report inherited ThemeContext");

        string layoutSources = string.Join(Environment.NewLine,
            new[] { "GamePerformance", "GameSessionReport" }.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(application, "Views", folder), "*.*")).Select(File.ReadAllText));
        string[] forbiddenBehavior = ["new GamePerformanceViewModel", "new GameSessionReportViewModel", "IGamePerformanceService", "IGameSessionRecorder", "IGameSessionReportService", "IGameIconService", ".Execute(", "LoadAsync(", "SetActive(", "RefreshProcesses(", "StartCapture(", "StopCapture(", "ExportPlainCsvAsync(", "Process.Start("];
        TestSupport.False(forbiddenBehavior.Any(layoutSources.Contains), "game layouts contain business behavior");
        string[] forbiddenVisual = ["Storyboard", "DoubleAnimation", "ColorAnimation", "ThicknessAnimation", "ObjectAnimationUsingKeyFrames", "CompositionTarget.Rendering", "DispatcherTimer", "PixelShader", "BlurEffect", "Source=\"http://", "Source=\"https://", "pack://siteoforigin"];
        TestSupport.False(forbiddenVisual.Any(layoutSources.Contains), "game layouts contain forbidden visual material");
        TestSupport.False(new Regex("(?<![A-Z])[A-Z]:[\\\\/]").IsMatch(layoutSources), "game layouts contain absolute paths");

        string resources = File.ReadAllText(Path.Combine(application, "Themes", "Tracework", "GamePages.xaml"));
        TestSupport.False(new Regex("<Style(?=\\s|>)(?![^>]*\\bx:Key\\s*=)[^>]*>", RegexOptions.Singleline).IsMatch(resources), "GamePages implicit Style");
        TestSupport.False(resources.Contains("BasedOn=\"{DynamicResource", StringComparison.Ordinal), "GamePages DynamicResource BasedOn");
        string allXaml = string.Join(Environment.NewLine, Directory.EnumerateFiles(application, "*.xaml", SearchOption.AllDirectories).Select(File.ReadAllText));
        TestSupport.Equal(1, Regex.Matches(allXaml, "Binding\\s+CurrentPage\\b").Count, "single CurrentPage binding");
    }

    private static void PureThemeSwitchHasZeroSideEffects() => WithPerformanceFixture(fixture =>
    {
        SideEffectCounts before = fixture.Counts();
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchTheme(fixture.View, host, AppTheme.Tracework);
            SwitchTheme(fixture.View, host, AppTheme.Classic);
            TestSupport.Equal(before, fixture.Counts(), "Game Performance side-effect counts");
        });
    });

    private static void CapturingStateSurvivesThemeSwitching() => WithPerformanceFixture(fixture =>
    {
        SetPrivate(fixture.ViewModel, "captureState", GameCaptureState.Capturing);
        SetPrivate(fixture.ViewModel, "isCapturing", true);
        SetPrivate(fixture.ViewModel, "isActive", true);
        fixture.ViewModel.ResumeRealtimeUiAfterSessionReport();
        object selected = TestSupport.NotNull(fixture.ViewModel.SelectedProcess, "selected process");
        object remembered = TestSupport.NotNull(ReadPrivate<object?>(fixture.ViewModel, "rememberedSelection"), "remembered process");
        object charts = fixture.ViewModel.Charts;
        object values = fixture.ViewModel.Charts[0].Values;
        int stopCalls = fixture.GameService.StopCount;
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchTheme(fixture.View, host, AppTheme.Tracework);
            SwitchTheme(fixture.View, host, AppTheme.Classic);
            TestSupport.Equal(GameCaptureState.Capturing, fixture.ViewModel.CaptureState, "capturing state");
            TestSupport.True(fixture.ViewModel.IsCapturing, "IsCapturing");
            TestSupport.False(fixture.ViewModel.CanSelectProcess, "capture selection lock");
            TestSupport.True(fixture.ViewModel.IsUiRefreshTimerEnabled, "UI timer remains enabled");
            TestSupport.True(ReferenceEquals(selected, fixture.ViewModel.SelectedProcess), "captured process reference");
            TestSupport.True(ReferenceEquals(remembered, ReadPrivate<object?>(fixture.ViewModel, "rememberedSelection")), "remembered selection reference");
            TestSupport.True(ReferenceEquals(charts, fixture.ViewModel.Charts), "capturing Charts reference");
            TestSupport.True(ReferenceEquals(values, fixture.ViewModel.Charts[0].Values), "capturing chart Values reference");
            TestSupport.Equal(stopCalls, fixture.GameService.StopCount, "no capture stop call");
        });
    });

    private static void LoadedReportDoesNotReloadOrClose() => WithReportFixture(fixture =>
    {
        ReportState state = ReportState.Capture(fixture.ViewModel);
        SideEffectCounts before = new(0, 0, 0, 0, 0, 0, 0, 0, 0, fixture.ReportService.LoadCount, fixture.IconService.LoadCount, fixture.CloseCount);
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchTheme(fixture.View, host, AppTheme.Tracework);
            SwitchTheme(fixture.View, host, AppTheme.Classic);
            state.AssertUnchanged(fixture.ViewModel);
            SideEffectCounts after = new(0, 0, 0, 0, 0, 0, 0, 0, 0, fixture.ReportService.LoadCount, fixture.IconService.LoadCount, fixture.CloseCount);
            TestSupport.Equal(before, after, "loaded report side-effect counts");
        });
    });

    private static void WithPerformanceFixture(Action<PerformanceFixture> test)
    {
        PerformanceFixture fixture = new();
        try { test(fixture); } finally { fixture.Dispose(); }
    }

    private static void WithReportFixture(Action<ReportFixture> test)
    {
        ReportFixture fixture = new();
        try { test(fixture); } finally { fixture.Dispose(); }
    }

    private static void AssertTemplate(UserControl view, AppTheme theme, Type expected, Type excluded)
    {
        ThemeContext.SetCurrentTheme(view, theme);
        WithHostedView(view, LayoutSize, _ =>
        {
            FrameworkElement layout = TestSupport.NotNull(FindVisualDescendants<FrameworkElement>(view).SingleOrDefault(element => element.GetType() == expected), expected.Name);
            TestSupport.Equal(0, CountLayout(view, excluded), $"excluded {excluded.Name}");
            TestSupport.True(ReferenceEquals(view.DataContext, layout.DataContext), $"{expected.Name} DataContext");
        });
    }

    private static void SwitchTheme(UserControl view, Window host, AppTheme theme)
    {
        ThemeContext.SetCurrentTheme(view, theme);
        host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.ApplyTemplate();
        view.ApplyTemplate();
        host.Measure(MinimumLayoutSize);
        host.Arrange(new Rect(new Point(), MinimumLayoutSize));
        host.UpdateLayout();
        view.UpdateLayout();
    }

    private static void WithHostedView(UserControl view, Size size, Action<Window> test)
    {
        Application application = GetApplication();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Window host = new() { Content = view, Width = size.Width, Height = size.Height, Left = -32000d, Top = -32000d, Opacity = 0d, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            host.Show(); host.ApplyTemplate(); view.ApplyTemplate(); host.Measure(size); host.Arrange(new Rect(new Point(), size)); host.UpdateLayout(); view.UpdateLayout(); test(host);
        }
        finally { host.Content = null; host.Close(); }
    }

    private static Application GetApplication()
    {
        if (Application.Current is not null) return Application.Current;
        HardwareVision.App app = new(); app.InitializeComponent(); return app;
    }

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

    private static T FindSingle<T>(DependencyObject root, string label) where T : DependencyObject => TestSupport.NotNull(FindVisualDescendants<T>(root).SingleOrDefault(), label);
    private static int CountLayout(DependencyObject root, Type type) => FindVisualDescendants<FrameworkElement>(root).Count(element => element.GetType() == type);
    private static T ReadPrivate<T>(object instance, string name) => (T)TestSupport.NotNull(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic), name).GetValue(instance)!;
    private static void SetPrivate(object instance, string name, object? value) => TestSupport.NotNull(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic), name).SetValue(instance, value);

    private static string FindRepositoryRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
            for (DirectoryInfo? candidate = new(origin); candidate is not null; candidate = candidate.Parent)
                if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml"))) return candidate.FullName;
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class PerformanceFixture : IDisposable
    {
        public PerformanceFixture()
        {
            Settings = new AppSettings { RecordGameSessions = true };
            GameService = new CountingGamePerformanceService();
            ForegroundTracker = new CountingForegroundTracker();
            Recorder = new CountingRecorder();
            SettingsService = new CountingSettingsService(Settings);
            EnergyTracker = new CountingEnergyTracker();
            LimitTracker = new CountingLimitTracker();
            ReportService = new CountingReportService(CreateReport(CreateRecord()));
            ViewModel = new GamePerformanceViewModel(GameService, Dispatcher.CurrentDispatcher, ForegroundTracker, Recorder, Settings, SettingsService, EnergyTracker, LimitTracker, ReportService);
            GameProcessInfo process = new() { ProcessId = 42, ProcessName = "synthetic-game", DisplayName = "Synthetic Game", WindowTitle = "Synthetic Window", IsLikelyGame = true };
            ViewModel.ProcessOptions.Add(process);
            ReadPrivate<List<GameProcessInfo>>(ViewModel, "allProcessOptions").Add(process);
            ViewModel.SelectedProcess = process;
            SetPrivate(ViewModel, "processSearchText", "synthetic");
            ViewModel.Metrics.Add(new DetailMetricViewModel("FPS", "60") { IsVisible = true });
            ViewModel.Charts[0].Append(60d);
            ViewModel.ApplyPerformanceLimitSnapshot(new GamePerformanceLimitSnapshot { IsTracking = true, CpuSupportStatus = PerformanceLimitSupportStatus.ActiveLimit, Events = [CreateLimitEvent()] });
            ViewModel.ApplySessionRecordPageForDiagnostics(new GameSessionRecordPage { Records = [CreateRecord()], TotalCount = 20, HasMore = true, SnapshotToken = "snapshot-1" }, replace: true);
            SetPrivate(ViewModel, "refreshGeneration", 7);
            SetPrivate(ViewModel, "sessionHistoryGeneration", 9);
            View = new GamePerformanceView { DataContext = ViewModel };
        }

        public AppSettings Settings { get; }
        public CountingGamePerformanceService GameService { get; }
        public CountingForegroundTracker ForegroundTracker { get; }
        public CountingRecorder Recorder { get; }
        public CountingSettingsService SettingsService { get; }
        public CountingEnergyTracker EnergyTracker { get; }
        public CountingLimitTracker LimitTracker { get; }
        public CountingReportService ReportService { get; }
        public GamePerformanceViewModel ViewModel { get; }
        public GamePerformanceView View { get; }

        public SideEffectCounts Counts() => new(GameService.CandidateCount, ForegroundTracker.CallCount, GameService.StartCount, GameService.StopCount, GameService.ExportCount, Recorder.OperationCount, SettingsService.TotalWrites, EnergyTracker.OperationCount, LimitTracker.OperationCount, ReportService.LoadCount, 0, 0);
        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class ReportFixture : IDisposable
    {
        public ReportFixture()
        {
            Record = CreateRecord();
            ReportService = new CountingReportService(CreateReport(Record));
            IconService = new CountingIconService();
            ViewModel = new GameSessionReportViewModel(Record, ReportService, () => CloseCount++, IconService);
            ViewModel.LoadAsync().GetAwaiter().GetResult();
            View = new GameSessionReportView { DataContext = ViewModel };
        }
        public GameSessionRecordInfo Record { get; }
        public CountingReportService ReportService { get; }
        public CountingIconService IconService { get; }
        public int CloseCount { get; private set; }
        public GameSessionReportViewModel ViewModel { get; }
        public GameSessionReportView View { get; }
        public void Dispose() => ViewModel.Dispose();
    }

    private sealed record PerformanceState(object ViewModel, object Processes, object Process, object Remembered, string Search, object AllProcesses, object Metrics, object Metric, object Charts, object Chart, object Values, int Window, object Events, object Event, object Records, object Record, int Total, bool More, string? Token, string Status, GameCaptureState CaptureState, bool AutoRecord, object? RefreshCancellation, int RefreshGeneration, object? HistoryCancellation, int HistoryGeneration, object? SessionReport)
    {
        public static PerformanceState Capture(GamePerformanceViewModel vm) => new(vm, vm.ProcessOptions, vm.ProcessOptions[0], ReadPrivate<object>(vm, "rememberedSelection"), vm.ProcessSearchText, ReadPrivate<object>(vm, "allProcessOptions"), vm.Metrics, vm.Metrics[0], vm.Charts, vm.Charts[0], vm.Charts[0].Values, vm.SelectedChartWindowSeconds, vm.PerformanceLimitEvents, vm.PerformanceLimitEvents[0], vm.RecentRecords, vm.RecentRecords[0], vm.TotalSessionRecordCount, vm.HasMoreSessionRecords, ReadPrivate<string?>(vm, "sessionHistorySnapshotToken"), vm.StatusText, vm.CaptureState, vm.AutoRecordGameSessions, ReadPrivate<CancellationTokenSource?>(vm, "refreshCancellation"), ReadPrivate<int>(vm, "refreshGeneration"), ReadPrivate<CancellationTokenSource?>(vm, "sessionHistoryCancellation"), ReadPrivate<int>(vm, "sessionHistoryGeneration"), vm.SessionReport);
        public void AssertUnchanged(GamePerformanceViewModel vm)
        {
            TestSupport.True(ReferenceEquals(ViewModel, vm), "GamePerformanceViewModel reference"); TestSupport.True(ReferenceEquals(Processes, vm.ProcessOptions), "ProcessOptions reference"); TestSupport.True(ReferenceEquals(Process, vm.ProcessOptions[0]), "process item reference"); TestSupport.True(ReferenceEquals(Process, vm.SelectedProcess), "SelectedProcess reference"); TestSupport.True(ReferenceEquals(Remembered, ReadPrivate<object>(vm, "rememberedSelection")), "remembered selection"); TestSupport.Equal(Search, vm.ProcessSearchText, "process search"); TestSupport.True(ReferenceEquals(AllProcesses, ReadPrivate<object>(vm, "allProcessOptions")), "all process options"); TestSupport.True(ReferenceEquals(Metrics, vm.Metrics), "Metrics reference"); TestSupport.True(ReferenceEquals(Metric, vm.Metrics[0]), "metric item"); TestSupport.True(ReferenceEquals(Charts, vm.Charts), "Charts reference"); TestSupport.True(ReferenceEquals(Chart, vm.Charts[0]), "chart reference"); TestSupport.True(ReferenceEquals(Values, vm.Charts[0].Values), "chart Values reference"); TestSupport.Equal(Window, vm.SelectedChartWindowSeconds, "chart window"); TestSupport.True(ReferenceEquals(Events, vm.PerformanceLimitEvents), "limit events reference"); TestSupport.True(ReferenceEquals(Event, vm.PerformanceLimitEvents[0]), "limit event item"); TestSupport.True(ReferenceEquals(Records, vm.RecentRecords), "RecentRecords reference"); TestSupport.True(ReferenceEquals(Record, vm.RecentRecords[0]), "record item"); TestSupport.Equal(Total, vm.TotalSessionRecordCount, "total record count"); TestSupport.Equal(More, vm.HasMoreSessionRecords, "has-more state"); TestSupport.Equal(Token, ReadPrivate<string?>(vm, "sessionHistorySnapshotToken"), "history token"); TestSupport.Equal(Status, vm.StatusText, "status text"); TestSupport.Equal(CaptureState, vm.CaptureState, "capture state"); TestSupport.Equal(AutoRecord, vm.AutoRecordGameSessions, "auto record"); TestSupport.True(ReferenceEquals(RefreshCancellation, ReadPrivate<CancellationTokenSource?>(vm, "refreshCancellation")), "refresh cancellation"); TestSupport.Equal(RefreshGeneration, ReadPrivate<int>(vm, "refreshGeneration"), "refresh generation"); TestSupport.True(ReferenceEquals(HistoryCancellation, ReadPrivate<CancellationTokenSource?>(vm, "sessionHistoryCancellation")), "history cancellation"); TestSupport.Equal(HistoryGeneration, ReadPrivate<int>(vm, "sessionHistoryGeneration"), "history generation"); TestSupport.True(ReferenceEquals(SessionReport, vm.SessionReport), "SessionReport reference");
        }
    }

    private sealed record ReportState(object ViewModel, object? Report, object? SelectedChart, object? Icon, object KeyMetrics, object KeyMetric, object HardwareMetrics, object HardwareMetric, object ThrottleMetrics, object ThrottleMetric, object Charts, object Events, object Event, object Warnings, object Warning, bool Loading, string Status, object? Cancellation)
    {
        public static ReportState Capture(GameSessionReportViewModel vm) => new(vm, vm.Report, vm.SelectedChart, vm.GameIcon, vm.KeyMetrics, vm.KeyMetrics[0], vm.HardwareMetrics, vm.HardwareMetrics[0], vm.ThrottleMetrics, vm.ThrottleMetrics[0], vm.Charts, vm.PerformanceLimitEvents, vm.PerformanceLimitEvents[0], vm.Warnings, vm.Warnings[0], vm.IsLoading, vm.StatusText, ReadPrivate<CancellationTokenSource?>(vm, "loadCancellation"));
        public void AssertUnchanged(GameSessionReportViewModel vm)
        {
            TestSupport.True(ReferenceEquals(ViewModel, vm), "GameSessionReportViewModel reference"); TestSupport.True(ReferenceEquals(Report, vm.Report), "Report reference"); TestSupport.True(ReferenceEquals(SelectedChart, vm.SelectedChart), "SelectedChart reference"); TestSupport.True(ReferenceEquals(Icon, vm.GameIcon), "GameIcon reference"); TestSupport.True(ReferenceEquals(KeyMetrics, vm.KeyMetrics) && ReferenceEquals(KeyMetric, vm.KeyMetrics[0]), "KeyMetrics references"); TestSupport.True(ReferenceEquals(HardwareMetrics, vm.HardwareMetrics) && ReferenceEquals(HardwareMetric, vm.HardwareMetrics[0]), "HardwareMetrics references"); TestSupport.True(ReferenceEquals(ThrottleMetrics, vm.ThrottleMetrics) && ReferenceEquals(ThrottleMetric, vm.ThrottleMetrics[0]), "ThrottleMetrics references"); TestSupport.True(ReferenceEquals(Charts, vm.Charts), "report Charts reference"); TestSupport.True(ReferenceEquals(Events, vm.PerformanceLimitEvents) && ReferenceEquals(Event, vm.PerformanceLimitEvents[0]), "report limit event references"); TestSupport.True(ReferenceEquals(Warnings, vm.Warnings) && ReferenceEquals(Warning, vm.Warnings[0]), "Warnings references"); TestSupport.Equal(Loading, vm.IsLoading, "report loading state"); TestSupport.Equal(Status, vm.StatusText, "report status"); TestSupport.True(ReferenceEquals(Cancellation, ReadPrivate<CancellationTokenSource?>(vm, "loadCancellation")), "report load cancellation");
        }
    }

    private sealed record SideEffectCounts(int ProcessRefresh, int ForegroundLookup, int CaptureStart, int CaptureStop, int Export, int Recorder, int Settings, int EnergyTracker, int PerformanceLimitTracker, int ReportLoad, int IconLoad, int ReportClose);

    private sealed class CountingGamePerformanceService : IGamePerformanceService
    {
        public event EventHandler<GameFrameSample>? FrameReceived { add { } remove { } } public event EventHandler<string>? StatusChanged { add { } remove { } } public event EventHandler<GameCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public bool IsCaptureAvailable => true; public string StatusText => "Ready"; public GameCaptureState CaptureState => GameCaptureState.Idle; public string? CaptureToolPath => null; public IReadOnlyList<GameFrameSample> RecentSamples => [];
        public int CandidateCount { get; private set; } public int StartCount { get; private set; } public int StopCount { get; private set; } public int ExportCount { get; private set; }
        public GamePerformanceSnapshot GetSnapshot(TimeSpan window) => new();
        public Task<IReadOnlyList<GameProcessInfo>> GetCandidateProcessesAsync(CancellationToken cancellationToken = default) { CandidateCount++; return Task.FromResult<IReadOnlyList<GameProcessInfo>>([]); }
        public Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default) { StartCount++; return Task.CompletedTask; }
        public Task StopCaptureAsync(CancellationToken cancellationToken = default) { StopCount++; return Task.CompletedTask; }
        public Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default) { ExportCount++; return Task.FromResult<string?>(null); }
        public Task<string?> ExportWindowCsvAsync(string directory, TimeSpan window, string? processName = null, CancellationToken cancellationToken = default) { ExportCount++; return Task.FromResult<string?>(null); }
        public Task<string?> ExportCacheCsvAsync(string directory, string? processName = null, CancellationToken cancellationToken = default) { ExportCount++; return Task.FromResult<string?>(null); }
        public void Dispose() { }
    }

    private sealed class CountingForegroundTracker : IForegroundProcessTracker { public int CallCount { get; private set; } public ForegroundProcessSnapshot? GetSnapshot() { CallCount++; return null; } }
    private sealed class CountingSettingsService(AppSettings settings) : ISettingsService { public int SaveCount { get; private set; } public int UpdateCount { get; private set; } public int TotalWrites => SaveCount + UpdateCount; public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings); public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) { SaveCount++; return Task.CompletedTask; } public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings); public Task<AppSettings> UpdateAsync(Action<AppSettings> action, CancellationToken cancellationToken = default) { action(settings); UpdateCount++; return Task.FromResult(settings); } }
    private sealed class CountingEnergyTracker : IGameEnergyTracker { public event EventHandler<GameEnergySnapshot>? SnapshotChanged { add { } remove { } } public int OperationCount { get; private set; } public GameEnergySnapshot CurrentSnapshot => GameEnergySnapshot.Empty; public void StartSession(GameSessionStartInfo info) => OperationCount++; public GameEnergySnapshot? CompleteSession(Guid id, int generation) { OperationCount++; return null; } public void Dispose() { } }
    private sealed class CountingLimitTracker : IGamePerformanceLimitTracker { public event EventHandler<GamePerformanceLimitSnapshot>? SnapshotChanged { add { } remove { } } public int OperationCount { get; private set; } public GamePerformanceLimitSnapshot CurrentSnapshot => GamePerformanceLimitSnapshot.Empty; public void StartSession(GameSessionStartInfo info) => OperationCount++; public GamePerformanceLimitSnapshot? CompleteSession(Guid id, int generation) { OperationCount++; return null; } public void Dispose() { } }
    private sealed class CountingReportService(GameSessionReport report) : IGameSessionReportService { public int LoadCount { get; private set; } public Task<GameSessionReport> LoadAsync(GameSessionRecordInfo record, CancellationToken cancellationToken = default) { LoadCount++; return Task.FromResult(report); } }
    private sealed class CountingIconService : IGameIconService { private readonly ImageSource icon = new DrawingImage(new DrawingGroup()); public int LoadCount { get; private set; } public Task<ImageSource?> LoadAsync(string? path, CancellationToken cancellationToken = default) { LoadCount++; return Task.FromResult<ImageSource?>(icon); } }

    private sealed class CountingRecorder : IGameSessionRecorder
    {
        public event EventHandler<GameSessionRecorderStateChangedEventArgs>? StateChanged { add { } remove { } } public string RootDirectory => Path.GetTempPath(); public bool IsRecording => false; public string RecordingStatusText => "Idle"; public string? CurrentFilePath => null; public long DroppedSampleCount => 0; public int OperationCount { get; private set; }
        public Task RecoverIncompleteSessionsAsync(CancellationToken cancellationToken = default) { OperationCount++; return Task.CompletedTask; } public Task StartAsync(GameSessionStartInfo info, CancellationToken cancellationToken = default) { OperationCount++; return Task.CompletedTask; } public bool TryRecord(GameFrameSample sample, Guid id, int generation) { OperationCount++; return false; } public Task<GameSessionRecordInfo?> CompleteAsync(GameSessionEndReason reason, bool normal, CancellationToken cancellationToken = default) { OperationCount++; return Task.FromResult<GameSessionRecordInfo?>(null); } public Task<IReadOnlyList<GameSessionRecordInfo>> GetRecentRecordsAsync(int maximumCount = 10, CancellationToken cancellationToken = default) { OperationCount++; return Task.FromResult<IReadOnlyList<GameSessionRecordInfo>>([]); } public Task<long> GetDirectorySizeAsync(CancellationToken cancellationToken = default) { OperationCount++; return Task.FromResult(0L); } public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static GameSessionRecordInfo CreateRecord() => new() { GameName = "Synthetic Game", StartedAt = new DateTimeOffset(2026, 7, 19, 10, 0, 0, TimeSpan.Zero), Duration = TimeSpan.FromMinutes(12), IsComplete = true, CsvPath = Path.Combine(Path.GetTempPath(), "synthetic.csv.gz"), EndReason = GameSessionEndReason.UserStopped, EstimatedEnergyWh = 12.5, CpuPerformanceLimitEventCount = 1, CpuPerformanceLimitSupportStatus = PerformanceLimitSupportStatus.ActiveLimit, GpuPerformanceLimitSupportStatus = PerformanceLimitSupportStatus.SupportedNormal };
    private static GamePerformanceLimitEvent CreateLimitEvent() => new() { EventId = 1, ProcessorType = PerformanceLimitProcessorType.Cpu, StartedAt = new DateTimeOffset(2026, 7, 19, 10, 1, 0, TimeSpan.Zero), Duration = TimeSpan.FromSeconds(3), Reasons = ["Thermal"], RawReasonNames = ["PROCHOT"], TriggerCount = 2, WasMerged = true };
    private static GameSessionReport CreateReport(GameSessionRecordInfo record)
    {
        SessionChartModel chart = new() { Key = "fps", Title = "FPS", Subtitle = "Synthetic chart", SampleNotice = "2 samples", DurationSeconds = 12, Series = [new SessionChartSeries { Name = "FPS", Unit = "FPS", Points = [new SessionChartPoint(0, 60), new SessionChartPoint(1, 61)] }] };
        return new GameSessionReport { Record = record, Charts = [chart], PerformanceLimitEvents = [CreateLimitEvent()], PerformanceLimitFileStatus = SessionAuxiliaryFileStatus.Recorded, HardwareTimelineFileStatus = SessionAuxiliaryFileStatus.Recorded, AverageFps = 60, MaximumFps = 61, Warnings = ["Synthetic integrity warning"] };
    }
}
