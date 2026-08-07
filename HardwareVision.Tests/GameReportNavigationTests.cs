using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class GameReportNavigationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Game report navigation 01 open selected recent session", OpenSelectedRecentSession),
        ("Game report navigation 02 close returns cached game page", CloseReturnsCachedGamePage),
        ("Game report navigation 03 repeated reports use latest record", RepeatedReportsUseLatestRecord),
        ("Game report navigation 04 rapid open and navigation", RapidOpenAndNavigation),
        ("Game report navigation 05 close during transition", CloseDuringTransition),
        ("Game report navigation 06 missing record file", MissingRecordFile),
        ("Game report navigation 07 corrupt or unauthorized record", CorruptOrUnauthorizedRecord),
        ("Game report navigation 08 motion levels", MotionLevels),
        ("Game report navigation 09 first and cached entry", FirstAndCachedEntry),
        ("Game report navigation 10 disposal", Disposal)
    ];

    private static void OpenSelectedRecentSession() => TestSupport.InTemporaryDirectory(directory =>
    {
        EnsureApplication();
        using DispatcherContextScope dispatcherContext = new();
        using TestEnvironment environment = new(directory, MotionLevel.Full);
        MainViewModel shellViewModel = environment.ViewModel;
        MainShellHost shell = CreateShell(shellViewModel);
        using WindowScope scope = new(shell);

        NavigationItemViewModel gameNavigation = shellViewModel.NavigationItems.Single(
            item => string.Equals(item.Key, "GamePerformance", StringComparison.Ordinal));
        shellViewModel.NavigateCommand.Execute(gameNavigation);
        PumpUntil(
            () => shellViewModel.CurrentPage is GamePerformanceViewModel
                && !shellViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(5),
            "game page navigation settled");

        GamePerformanceViewModel gamePage = (GamePerformanceViewModel)shellViewModel.CurrentPage!;
        PumpUntil(
            () => !gamePage.IsLoadingSessionRecords,
            TimeSpan.FromSeconds(3),
            "initial session history load settled");

        GameSessionRecordInfo first = Record(directory, "First game", "first.csv");
        GameSessionRecordInfo selected = Record(directory, "Selected game", "selected.csv");
        gamePage.ApplySessionRecordPageForDiagnostics(
            new GameSessionRecordPage
            {
                Records = [first, selected],
                PageSize = 10,
                TotalCount = 2,
                HasMore = false,
                SnapshotToken = "game-report-navigation"
            },
            replace: true);

        Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(selected);
        PumpUntil(
            () => openTask.IsCompleted && !shellViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(8),
            "selected report navigation settled");
        openTask.GetAwaiter().GetResult();
        scope.Window.UpdateLayout();
        Pump(TimeSpan.FromMilliseconds(50));

        GameSessionReportViewModel report = TestSupport.NotNull(
            gamePage.SessionReport,
            "selected session report");
        TestSupport.Equal("Selected game", report.GameName, "selected report identity");
        TestSupport.True(
            ReferenceEquals(report, shellViewModel.CurrentPage),
            "outer CurrentPage is the selected report content");
        TestSupport.Equal("GameSessionReport", shellViewModel.CurrentNavigationPageKey, "report route");
        TestSupport.Equal("08R", shellViewModel.CurrentPageCode, "report page code");
        TestSupport.Equal("GamePerformance", SelectedNavigation(shellViewModel).Key, "selected sidebar item");

        GameSessionReportView[] visibleReports = Descendants<GameSessionReportView>(shell)
            .Where(view => view.IsVisible)
            .ToArray();
        TestSupport.Equal(1, visibleReports.Length, "visible report view count");
        TestSupport.True(
            ReferenceEquals(report, visibleReports[0].DataContext),
            "visible report DataContext");
        TestSupport.Equal(
            0,
            Descendants<GamePerformanceView>(shell).Count(view => view.IsVisible),
            "visible game page count while report is active");

        MotionTransitionHost pageHost = TestSupport.NotNull(
            shell.FindName("PageHost") as MotionTransitionHost,
            "shell page host");
        CachedPagePresenter presenter = TestSupport.NotNull(
            pageHost.Template.FindName("PagePresenter", pageHost) as CachedPagePresenter,
            "shell cached page presenter");
        TestSupport.True(
            ReferenceEquals(report, presenter.PresentedContent),
            "presented content is the selected report");
    });

    private static void CloseReturnsCachedGamePage() => TestSupport.InTemporaryDirectory(directory =>
    {
        using Scenario scenario = new(directory, MotionLevel.Full, AppTheme.Tracework);
        GamePerformanceViewModel gamePage = scenario.NavigateToGame();
        GameSessionRecordInfo[] records = Enumerable.Range(0, 20)
            .Select(index => Record(directory, $"Game {index:D2}", $"game-{index:D2}.csv"))
            .ToArray();
        gamePage.ApplySessionRecordPageForDiagnostics(
            new GameSessionRecordPage
            {
                Records = records,
                PageSize = 20,
                TotalCount = 25,
                HasMore = true,
                SnapshotToken = "cached-return"
            },
            replace: true);
        GameSessionRecordInfo[] originalRecords = gamePage.RecentRecords.ToArray();

        ScrollViewer scroll = Descendants<ScrollViewer>(scenario.Shell)
            .Single(view => string.Equals(
                view.Name,
                "TraceworkGamePerformanceScrollViewer",
                StringComparison.Ordinal));
        scroll.ScrollToVerticalOffset(180d);
        Pump(TimeSpan.FromMilliseconds(30));
        double originalOffset = scroll.VerticalOffset;

        Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(records[11]);
        scenario.WaitForReport(openTask);
        TestSupport.False(gamePage.IsUiRefreshTimerEnabled, "timer paused while report is active");
        scenario.CloseReport();

        TestSupport.True(ReferenceEquals(gamePage, scenario.ViewModel.CurrentPage), "cached game page instance");
        TestSupport.Equal("GamePerformance", scenario.ViewModel.CurrentNavigationPageKey, "restored game route");
        TestSupport.Equal("08", scenario.ViewModel.CurrentPageCode, "restored game page code");
        TestSupport.Equal(25, gamePage.TotalSessionRecordCount, "restored total record count");
        TestSupport.True(gamePage.HasMoreSessionRecords, "restored pagination state");
        TestSupport.True(originalRecords.SequenceEqual(gamePage.RecentRecords), "restored record instances and order");
        TestSupport.True(gamePage.IsUiRefreshTimerEnabled, "timer resumed after report close");
        ScrollViewer returnedScroll = Descendants<ScrollViewer>(scenario.Shell)
            .Single(view => string.Equals(
                view.Name,
                "TraceworkGamePerformanceScrollViewer",
                StringComparison.Ordinal));
        TestSupport.True(
            Math.Abs(originalOffset - returnedScroll.VerticalOffset) < 0.5d,
            $"restored game scroll offset (original={originalOffset:0.###}, stored={gamePage.PageScrollOffset:0.###}, returned={returnedScroll.VerticalOffset:0.###})");
    });

    private static void RepeatedReportsUseLatestRecord() => TestSupport.InTemporaryDirectory(directory =>
    {
        ControlledReportService reportService = new("Game A");
        using Scenario scenario = new(directory, MotionLevel.Full, AppTheme.Tracework, reportService);
        GamePerformanceViewModel gamePage = scenario.NavigateToGame();
        GameSessionRecordInfo recordA = Record(directory, "Game A", "a.csv");
        GameSessionRecordInfo recordB = Record(directory, "Game B", "b.csv");
        scenario.SetRecords(gamePage, recordA, recordB);

        Task firstOpen = gamePage.OpenSessionReportCommand.ExecuteAsync(recordA);
        scenario.WaitForReportRoute();
        PumpUntil(() => reportService.BlockedLoadStarted, TimeSpan.FromSeconds(3), "A load started");
        scenario.CloseReport();
        PumpUntil(() => firstOpen.IsCompleted, TimeSpan.FromSeconds(3), "A open task completed after close");
        firstOpen.GetAwaiter().GetResult();
        TestSupport.True(reportService.CancellationObserved, "A load cancellation observed");

        Task secondOpen = gamePage.OpenSessionReportCommand.ExecuteAsync(recordB);
        scenario.WaitForReport(secondOpen);
        GameSessionReportViewModel reportB = TestSupport.NotNull(gamePage.SessionReport, "B report");
        TestSupport.Equal("Game B", reportB.GameName, "latest report identity");
        TestSupport.True(ReferenceEquals(reportB, scenario.ViewModel.CurrentPage), "latest outer report content");
        TestSupport.Equal(
            1,
            Descendants<GameSessionReportView>(scenario.Shell).Count(view => view.IsVisible),
            "single visible latest report");
    });

    private static void RapidOpenAndNavigation() => TestSupport.InTemporaryDirectory(directory =>
    {
        using Scenario scenario = new(directory, MotionLevel.Full, AppTheme.Tracework);
        GamePerformanceViewModel gamePage = scenario.NavigateToGame();
        GameSessionRecordInfo record = Record(directory, "Rapid game", "rapid.csv");
        scenario.SetRecords(gamePage, record);

        Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(record);
        NavigationItemViewModel cpuNavigation = scenario.ViewModel.NavigationItems.Single(
            item => string.Equals(item.Key, "Cpu", StringComparison.Ordinal));
        scenario.ViewModel.NavigateCommand.Execute(cpuNavigation);
        PumpUntil(
            () => scenario.ViewModel.CurrentPage is CpuViewModel
                && !scenario.ViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(8),
            "rapid CPU navigation settled");
        PumpUntil(() => openTask.IsCompleted, TimeSpan.FromSeconds(3), "superseded report task completed");
        openTask.GetAwaiter().GetResult();

        TestSupport.True(scenario.ViewModel.CurrentPage is CpuViewModel, "last requested CPU page");
        TestSupport.Equal("Cpu", scenario.ViewModel.CurrentNavigationPageKey, "last requested CPU route");
        TestSupport.Equal("Cpu", SelectedNavigation(scenario.ViewModel).Key, "CPU sidebar selection");
        TestSupport.True(gamePage.SessionReport is null, "no residual session report");
        TestSupport.Equal(
            0,
            Descendants<GameSessionReportView>(scenario.Shell).Count(view => view.IsVisible),
            "no visible residual report");
        TestSupport.Equal(
            1,
            Descendants<CpuView>(scenario.Shell).Count(view => view.IsVisible),
            "one visible CPU page");
    });

    private static void CloseDuringTransition() => TestSupport.InTemporaryDirectory(directory =>
    {
        ControlledReportService reportService = new("Transition game");
        using Scenario scenario = new(directory, MotionLevel.Full, AppTheme.Tracework, reportService);
        GamePerformanceViewModel gamePage = scenario.NavigateToGame();
        GameSessionRecordInfo record = Record(directory, "Transition game", "transition.csv");
        scenario.SetRecords(gamePage, record);

        Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(record);
        PumpUntil(
            () => scenario.ViewModel.CurrentPage is GameSessionReportViewModel
                && scenario.ViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(3),
            "report committed during active transition");
        GameSessionReportViewModel report = (GameSessionReportViewModel)scenario.ViewModel.CurrentPage!;
        report.BackCommand.Execute(null);
        PumpUntil(
            () => ReferenceEquals(gamePage, scenario.ViewModel.CurrentPage)
                && !scenario.ViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(8),
            "close during transition settled");
        PumpUntil(() => openTask.IsCompleted, TimeSpan.FromSeconds(3), "opening task settled after close");
        openTask.GetAwaiter().GetResult();

        TestSupport.Equal("GamePerformance", scenario.ViewModel.CurrentNavigationPageKey, "route after transition close");
        TestSupport.Equal("GamePerformance", SelectedNavigation(scenario.ViewModel).Key, "sidebar after transition close");
        TestSupport.True(gamePage.SessionReport is null, "report cleared after transition close");
        TestSupport.Equal(
            1,
            Descendants<GamePerformanceView>(scenario.Shell).Count(view => view.IsVisible),
            "one visible cached game page after transition close");
    });

    private static void MissingRecordFile() => TestSupport.InTemporaryDirectory(directory =>
    {
        using Scenario scenario = new(
            directory,
            MotionLevel.Full,
            AppTheme.Tracework,
            new ThrowingReportService(new FileNotFoundException("Missing session record.")));
        GamePerformanceViewModel gamePage = scenario.NavigateToGame();
        GameSessionRecordInfo missing = Record(directory, "Missing game", "missing.csv");
        scenario.SetRecords(gamePage, missing);

        Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(missing);
        scenario.WaitForReport(openTask);
        GameSessionReportViewModel report = TestSupport.NotNull(gamePage.SessionReport, "missing-file report");
        TestSupport.Equal("无法加载会话数据", report.StatusText, "missing-file bounded error");
        TestSupport.Equal("GameSessionReport", scenario.ViewModel.CurrentNavigationPageKey, "missing-file route");
        TestSupport.True(ReferenceEquals(report, scenario.ViewModel.CurrentPage), "missing-file content remains report");
        scenario.CloseReport();
        TestSupport.True(ReferenceEquals(gamePage, scenario.ViewModel.CurrentPage), "missing-file return target");
    });

    private static void CorruptOrUnauthorizedRecord() => TestSupport.InTemporaryDirectory(directory =>
    {
        Exception[] failures =
        [
            new InvalidDataException("Corrupt session record."),
            new UnauthorizedAccessException("Session record access denied.")
        ];
        for (int index = 0; index < failures.Length; index++)
        {
            string caseDirectory = Path.Combine(directory, $"fault-{index}");
            Directory.CreateDirectory(caseDirectory);
            using Scenario scenario = new(
                caseDirectory,
                MotionLevel.Standard,
                AppTheme.Tracework,
                new ThrowingReportService(failures[index]));
            GamePerformanceViewModel gamePage = scenario.NavigateToGame();
            GameSessionRecordInfo record = Record(caseDirectory, $"Fault {index}", $"fault-{index}.csv");
            scenario.SetRecords(gamePage, record);

            Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(record);
            scenario.WaitForReport(openTask);
            GameSessionReportViewModel report = TestSupport.NotNull(gamePage.SessionReport, $"fault {index} report");
            TestSupport.Equal("无法加载会话数据", report.StatusText, $"fault {index} bounded error");
            TestSupport.Equal("GameSessionReport", scenario.ViewModel.CurrentNavigationPageKey, $"fault {index} route");
            scenario.CloseReport();
            TestSupport.True(gamePage.SessionReport is null, $"fault {index} report cleared");
        }
    });

    private static void MotionLevels() => TestSupport.InTemporaryDirectory(directory =>
    {
        foreach (AppTheme theme in new[] { AppTheme.Classic, AppTheme.Tracework })
        {
            foreach (MotionLevel level in new[] { MotionLevel.Full, MotionLevel.Standard, MotionLevel.Reduced, MotionLevel.Off })
            {
                string caseDirectory = Path.Combine(directory, $"{theme}-{level}");
                Directory.CreateDirectory(caseDirectory);
                using Scenario scenario = new(caseDirectory, level, theme);
                GamePerformanceViewModel gamePage = scenario.NavigateToGame();
                GameSessionRecordInfo record = Record(caseDirectory, $"{theme} {level}", "motion.csv");
                scenario.SetRecords(gamePage, record);
                Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(record);
                scenario.WaitForReport(openTask);
                TestSupport.Equal("GameSessionReport", scenario.ViewModel.CurrentNavigationPageKey, $"{theme}/{level} report route");
                TestSupport.Equal("GamePerformance", SelectedNavigation(scenario.ViewModel).Key, $"{theme}/{level} report selection");
                TestSupport.Equal(1, Descendants<GameSessionReportView>(scenario.Shell).Count(view => view.IsVisible), $"{theme}/{level} visible report");
                scenario.CloseReport();
                TestSupport.True(ReferenceEquals(gamePage, scenario.ViewModel.CurrentPage), $"{theme}/{level} cached return");
            }
        }
    });

    private static void FirstAndCachedEntry() => TestSupport.InTemporaryDirectory(directory =>
    {
        using Scenario scenario = new(directory, MotionLevel.Full, AppTheme.Tracework);
        GamePerformanceViewModel firstGamePage = scenario.NavigateToGame();
        GameSessionRecordInfo first = Record(directory, "First entry", "first-entry.csv");
        GameSessionRecordInfo cached = Record(directory, "Cached entry", "cached-entry.csv");
        scenario.SetRecords(firstGamePage, first, cached);
        scenario.WaitForReport(firstGamePage.OpenSessionReportCommand.ExecuteAsync(first));
        scenario.CloseReport();

        NavigationItemViewModel cpu = scenario.ViewModel.NavigationItems.Single(item => item.Key == "Cpu");
        scenario.ViewModel.NavigateCommand.Execute(cpu);
        PumpUntil(
            () => scenario.ViewModel.CurrentPage is CpuViewModel && !scenario.ViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(8),
            "CPU between first and cached game entry");
        NavigationItemViewModel game = scenario.ViewModel.NavigationItems.Single(item => item.Key == "GamePerformance");
        scenario.ViewModel.NavigateCommand.Execute(game);
        PumpUntil(
            () => ReferenceEquals(firstGamePage, scenario.ViewModel.CurrentPage)
                && !scenario.ViewModel.IsNavigationTransitionActive,
            TimeSpan.FromSeconds(8),
            "cached game entry settled");

        scenario.WaitForReport(firstGamePage.OpenSessionReportCommand.ExecuteAsync(cached));
        GameSessionReportViewModel report = TestSupport.NotNull(firstGamePage.SessionReport, "cached-entry report");
        TestSupport.Equal("Cached entry", report.GameName, "cached-entry report identity");
        TestSupport.Equal("GameSessionReport", scenario.ViewModel.CurrentNavigationPageKey, "cached-entry route");
        scenario.CloseReport();
        TestSupport.True(ReferenceEquals(firstGamePage, scenario.ViewModel.CurrentPage), "same game instance after cached report");
    });

    private static void Disposal() => TestSupport.InTemporaryDirectory(directory =>
    {
        ControlledReportService reportService = new("Disposal game");
        using Scenario scenario = new(directory, MotionLevel.Full, AppTheme.Tracework, reportService);
        GamePerformanceViewModel gamePage = scenario.NavigateToGame();
        GameSessionRecordInfo record = Record(directory, "Disposal game", "disposal.csv");
        scenario.SetRecords(gamePage, record);
        Task openTask = gamePage.OpenSessionReportCommand.ExecuteAsync(record);
        scenario.WaitForReportRoute();
        PumpUntil(() => reportService.BlockedLoadStarted, TimeSpan.FromSeconds(3), "disposal load started");

        scenario.DisposeEnvironment();
        PumpUntil(() => reportService.CancellationObserved, TimeSpan.FromSeconds(3), "disposal cancellation observed");
        PumpUntil(() => openTask.IsCompleted, TimeSpan.FromSeconds(3), "disposed open task completed");
        openTask.GetAwaiter().GetResult();
        TestSupport.True(gamePage.SessionReport is null, "disposed report cleared");
        scenario.CloseWindow();
    });

    private static MainShellHost CreateShell(MainViewModel viewModel)
    {
        MainShellHost shell = new() { DataContext = viewModel };
        shell.Resources.Add(
            new DataTemplateKey(typeof(GamePerformanceViewModel)),
            Template<GamePerformanceView>());
        shell.Resources.Add(
            new DataTemplateKey(typeof(GameSessionReportViewModel)),
            Template<GameSessionReportView>());
        shell.Resources.Add(
            new DataTemplateKey(typeof(DashboardViewModel)),
            Template<DashboardView>());
        shell.Resources.Add(
            new DataTemplateKey(typeof(CpuViewModel)),
            Template<CpuView>());
        return shell;
    }

    private static DataTemplate Template<TView>() where TView : FrameworkElement, new() =>
        new() { VisualTree = new FrameworkElementFactory(typeof(TView)) };

    private static GameSessionRecordInfo Record(string directory, string gameName, string fileName) =>
        new()
        {
            GameName = gameName,
            StartedAt = DateTimeOffset.Now,
            Duration = TimeSpan.FromMinutes(5),
            IsComplete = true,
            CsvPath = Path.Combine(directory, fileName),
            EndReason = GameSessionEndReason.TargetProcessExited
        };

    private static NavigationItemViewModel SelectedNavigation(MainViewModel viewModel) =>
        viewModel.NavigationItems.Single(item => item.IsSelected);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App application = new();
            application.InitializeComponent();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string label)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(5));
        }

        TestSupport.True(condition(), label);
    }

    private static void Pump(TimeSpan duration)
    {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly PollingService pollingService;
        private readonly SensorHistoryService sensorHistoryService;
        private readonly CsvGameSessionRecorder recorder;
        private bool isDisposed;

        public TestEnvironment(
            string directory,
            MotionLevel motionLevel,
            AppTheme theme = AppTheme.Tracework,
            IGameSessionReportService? reportService = null)
        {
            AppSettings settings = new()
            {
                Theme = AppThemeParser.ToStorageValue(theme),
                Motion = MotionLevelParser.ToStorageValue(motionLevel)
            };
            CountingSettingsService settingsService = new(settings);
            TestThemeService themeService = new(theme);
            MotionService = new MotionService(
                new FakeMotionEnvironment(),
                motionLevel,
                Dispatcher.CurrentDispatcher);
            ThemeTransitionService = new ThemeTransitionService(
                themeService,
                MotionService,
                Dispatcher.CurrentDispatcher);
            NavigationService = new NavigationTransitionService();
            pollingService = new PollingService(new CountingSensorService(), settings);
            sensorHistoryService = new SensorHistoryService(pollingService);
            recorder = new CsvGameSessionRecorder(Path.Combine(directory, "sessions"), 8);
            ViewModel = new MainViewModel(
                settings,
                new EmptyHardwareInfoService(),
                pollingService,
                settingsService,
                themeService,
                MotionService,
                ThemeTransitionService,
                NavigationService,
                new NoopStartupService(),
                Dispatcher.CurrentDispatcher,
                new SensorDiagnosticService(),
                EmptyForegroundProcessTracker.Instance,
                sensorHistoryService,
                recorder,
                gameSessionReportService: reportService);
        }

        public MainViewModel ViewModel { get; }

        public MotionService MotionService { get; }

        public ThemeTransitionService ThemeTransitionService { get; }

        public NavigationTransitionService NavigationService { get; }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            ViewModel.Dispose();
            sensorHistoryService.Dispose();
            pollingService.Dispose();
            recorder.Dispose();
            NavigationService.Dispose();
            ThemeTransitionService.Dispose();
            MotionService.Dispose();
        }
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

    private sealed class WindowScope : IDisposable
    {
        private bool isDisposed;

        public WindowScope(FrameworkElement content)
        {
            Window = new Window
            {
                Content = content,
                Width = 1120d,
                Height = 720d,
                Left = -32000d,
                Top = -32000d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            Window.Show();
            Window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(30));
        }

        public Window Window { get; }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            Window.Content = null;
            Window.Close();
            Pump(TimeSpan.FromMilliseconds(5));
        }
    }

    private sealed class Scenario : IDisposable
    {
        private readonly DispatcherContextScope dispatcherContext;
        private readonly TestEnvironment environment;
        private readonly WindowScope windowScope;

        public Scenario(
            string directory,
            MotionLevel motionLevel,
            AppTheme theme,
            IGameSessionReportService? reportService = null)
        {
            EnsureApplication();
            dispatcherContext = new DispatcherContextScope();
            environment = new TestEnvironment(
                directory,
                motionLevel,
                theme,
                reportService ?? new FixedReportService());
            Shell = CreateShell(environment.ViewModel);
            windowScope = new WindowScope(Shell);
        }

        public MainShellHost Shell { get; }

        public MainViewModel ViewModel => environment.ViewModel;

        public GamePerformanceViewModel NavigateToGame()
        {
            NavigationItemViewModel gameNavigation = ViewModel.NavigationItems.Single(
                item => string.Equals(item.Key, "GamePerformance", StringComparison.Ordinal));
            ViewModel.NavigateCommand.Execute(gameNavigation);
            PumpUntil(
                () => ViewModel.CurrentPage is GamePerformanceViewModel
                    && !ViewModel.IsNavigationTransitionActive,
                TimeSpan.FromSeconds(8),
                "scenario game navigation settled");
            GamePerformanceViewModel gamePage = (GamePerformanceViewModel)ViewModel.CurrentPage!;
            PumpUntil(
                () => !gamePage.IsLoadingSessionRecords,
                TimeSpan.FromSeconds(3),
                "scenario session history load settled");
            return gamePage;
        }

        public void SetRecords(GamePerformanceViewModel gamePage, params GameSessionRecordInfo[] records) =>
            gamePage.ApplySessionRecordPageForDiagnostics(
                new GameSessionRecordPage
                {
                    Records = records,
                    PageSize = 10,
                    TotalCount = records.Length,
                    HasMore = false,
                    SnapshotToken = "scenario-records"
                },
                replace: true);

        public void WaitForReport(Task openTask)
        {
            PumpUntil(
                () => openTask.IsCompleted
                    && ViewModel.CurrentPage is GameSessionReportViewModel
                    && !ViewModel.IsNavigationTransitionActive,
                TimeSpan.FromSeconds(8),
                "scenario report navigation settled");
            openTask.GetAwaiter().GetResult();
            windowScope.Window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(30));
        }

        public void WaitForReportRoute()
        {
            PumpUntil(
                () => ViewModel.CurrentPage is GameSessionReportViewModel,
                TimeSpan.FromSeconds(3),
                "scenario report route committed");
            windowScope.Window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(10));
        }

        public void CloseReport()
        {
            GameSessionReportViewModel report = TestSupport.NotNull(
                ViewModel.CurrentPage as GameSessionReportViewModel,
                "scenario active report");
            GamePerformanceViewModel gamePage = ViewModel.GamePerformance;
            report.BackCommand.Execute(null);
            PumpUntil(
                () => ReferenceEquals(gamePage, ViewModel.CurrentPage)
                    && !ViewModel.IsNavigationTransitionActive,
                TimeSpan.FromSeconds(8),
                "scenario report close settled");
            windowScope.Window.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(30));
        }

        public void DisposeEnvironment() => environment.Dispose();

        public void CloseWindow() => windowScope.Dispose();

        public void Dispose()
        {
            windowScope.Dispose();
            environment.Dispose();
            dispatcherContext.Dispose();
        }
    }

    private sealed class FixedReportService : IGameSessionReportService
    {
        public Task<GameSessionReport> LoadAsync(
            GameSessionRecordInfo record,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GameSessionReport { Record = record });
        }
    }

    private sealed class ThrowingReportService(Exception failure) : IGameSessionReportService
    {
        public Task<GameSessionReport> LoadAsync(
            GameSessionRecordInfo record,
            CancellationToken cancellationToken = default)
        {
            _ = record;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<GameSessionReport>(failure);
        }
    }

    private sealed class ControlledReportService(string blockedGameName) : IGameSessionReportService
    {
        private readonly TaskCompletionSource<GameSessionReport> blocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockedLoadStarted { get; private set; }

        public bool CancellationObserved { get; private set; }

        public async Task<GameSessionReport> LoadAsync(
            GameSessionRecordInfo record,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(record.GameName, blockedGameName, StringComparison.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new GameSessionReport { Record = record };
            }

            BlockedLoadStarted = true;
            try
            {
                return await blocked.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class DispatcherContextScope : IDisposable
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;

        public DispatcherContextScope() =>
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        public void Dispose() => SynchronizationContext.SetSynchronizationContext(previous);
    }
}
