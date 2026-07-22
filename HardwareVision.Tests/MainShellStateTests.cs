using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class MainShellStateTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Shell state 01 MainViewModel initializes from theme service", MainViewModelInitializesFromThemeService),
        ("Shell state 02 theme change preserves page and navigation", ThemeChangePreservesPageAndNavigation),
        ("Shell state 03 disposed MainViewModel ignores theme changes", DisposedMainViewModelIgnoresThemeChanges),
        ("Navigation 01 Dashboard is selected at startup", DashboardIsSelectedAtStartup),
        ("Navigation 02 navigating to CPU selects only CPU", NavigatingToCpuSelectsOnlyCpu),
        ("Navigation 03 navigating to GPU selects only GPU", NavigatingToGpuSelectsOnlyGpu),
        ("Navigation 04 repeated navigation keeps one selection", RepeatedNavigationKeepsOneSelection),
        ("Navigation 05 theme switch preserves selection", ThemeSwitchPreservesSelection),
        ("Navigation 06 display codes follow shell order", DisplayCodesFollowShellOrder),
        ("Navigation 07 disabled item cannot become current", DisabledItemCannotBecomeCurrent),
        ("Navigation 08 FLOW RELAY defers business commit", FlowRelayDefersBusinessCommit)
    ];

    private static void MainViewModelInitializesFromThemeService() =>
        WithEnvironment(environment =>
        {
            TestSupport.Equal(AppTheme.Tracework, environment.ViewModel.CurrentTheme, "initial MainViewModel theme");
            TestSupport.False(environment.ViewModel.IsClassicTheme, "initial Classic flag");
            TestSupport.True(environment.ViewModel.IsTraceworkTheme, "initial Tracework flag");
        }, AppTheme.Tracework);

    private static void ThemeChangePreservesPageAndNavigation() =>
        WithEnvironment(environment =>
        {
            Navigate(environment.ViewModel, "Gpu");
            object page = TestSupport.NotNull(environment.ViewModel.CurrentPage, "GPU page before theme switch");
            NavigationItemViewModel selected = SelectedItem(environment.ViewModel);

            TestSupport.True(environment.ThemeService.ApplyTheme(AppTheme.Tracework), "apply Tracework in MainViewModel test");

            TestSupport.Equal(AppTheme.Tracework, environment.ViewModel.CurrentTheme, "theme after notification");
            TestSupport.False(environment.ViewModel.IsClassicTheme, "Classic flag after notification");
            TestSupport.True(environment.ViewModel.IsTraceworkTheme, "Tracework flag after notification");
            TestSupport.True(ReferenceEquals(page, environment.ViewModel.CurrentPage), "CurrentPage reference after theme switch");
            TestSupport.True(ReferenceEquals(selected, SelectedItem(environment.ViewModel)), "navigation item after theme switch");
            TestSupport.Equal(1, environment.ThemeService.ApplyCount, "theme apply count after MainViewModel notification");
        });

    private static void DisposedMainViewModelIgnoresThemeChanges() =>
        WithEnvironment(environment =>
        {
            environment.ViewModel.Dispose();
            TestSupport.True(environment.ThemeService.ApplyTheme(AppTheme.Tracework), "apply theme after MainViewModel dispose");

            TestSupport.Equal(AppTheme.Classic, environment.ViewModel.CurrentTheme, "disposed MainViewModel theme");
        });

    private static void DashboardIsSelectedAtStartup() =>
        WithEnvironment(environment => AssertOnlySelected(environment.ViewModel, "Dashboard"));

    private static void NavigatingToCpuSelectsOnlyCpu() =>
        WithEnvironment(environment =>
        {
            Navigate(environment.ViewModel, "Cpu");
            AssertOnlySelected(environment.ViewModel, "Cpu");
        });

    private static void NavigatingToGpuSelectsOnlyGpu() =>
        WithEnvironment(environment =>
        {
            Navigate(environment.ViewModel, "Gpu");
            AssertOnlySelected(environment.ViewModel, "Gpu");
        });

    private static void RepeatedNavigationKeepsOneSelection() =>
        WithEnvironment(environment =>
        {
            NavigationItemViewModel gpu = Find(environment.ViewModel, "Gpu");
            environment.ViewModel.NavigateCommand.Execute(gpu);
            object page = TestSupport.NotNull(environment.ViewModel.CurrentPage, "GPU page before repeated navigation");
            environment.ViewModel.NavigateCommand.Execute(gpu);

            AssertOnlySelected(environment.ViewModel, "Gpu");
            TestSupport.True(ReferenceEquals(page, environment.ViewModel.CurrentPage), "page after repeated navigation");
        });

    private static void ThemeSwitchPreservesSelection() =>
        WithEnvironment(environment =>
        {
            Navigate(environment.ViewModel, "Cpu");
            environment.ThemeService.ApplyTheme(AppTheme.Tracework);
            AssertOnlySelected(environment.ViewModel, "Cpu");
        });

    private static void DisplayCodesFollowShellOrder() =>
        WithEnvironment(environment =>
        {
            string[] expected = ["01", "02", "03", "04", "05", "06", "07", "08", "09", "10"];
            TestSupport.True(
                expected.SequenceEqual(environment.ViewModel.NavigationItems.Select(item => item.DisplayCode)),
                "navigation display-code order");
        });

    private static void DisabledItemCannotBecomeCurrent() =>
        WithEnvironment(environment =>
        {
            object page = TestSupport.NotNull(environment.ViewModel.CurrentPage, "startup page");
            NavigationItemViewModel cpu = Find(environment.ViewModel, "Cpu");
            cpu.IsEnabled = false;

            environment.ViewModel.NavigateCommand.Execute(cpu);

            AssertOnlySelected(environment.ViewModel, "Dashboard");
            TestSupport.True(ReferenceEquals(page, environment.ViewModel.CurrentPage), "page after disabled navigation");
        });

    private static void FlowRelayDefersBusinessCommit() => RunOnDispatcher(async dispatcher =>
    {
        _ = dispatcher;
        await TestSupport.InTemporaryDirectory(async directory =>
        {
            GateNavigationClock clock = new();
            using MainViewModelTestEnvironment environment = new(directory, AppTheme.Tracework, clock);
            MainViewModel viewModel = environment.ViewModel;
            object dashboard = TestSupport.NotNull(viewModel.CurrentPage, "Dashboard before FLOW RELAY");
            NavigationItemViewModel gpu = Find(viewModel, "Gpu");

            viewModel.NavigateCommand.Execute(gpu);
            await clock.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            TestSupport.True(ReferenceEquals(dashboard, viewModel.CurrentPage), "page remains before Relay commit");
            AssertOnlySelected(viewModel, "Dashboard");
            TestSupport.Equal("Dashboard", environment.Settings.LastSelectedPage, "settings remain before Relay commit");

            Task transition = TestSupport.NotNull(environment.NavigationService.ActiveTask, "active FLOW RELAY task");
            clock.Release();
            await transition;

            TestSupport.True(ReferenceEquals(gpu.CreatedPage, viewModel.CurrentPage), "target page committed at Relay");
            AssertOnlySelected(viewModel, "Gpu");
            TestSupport.Equal("Gpu", environment.Settings.LastSelectedPage, "settings committed with target page");
        });
    });

    private static void Navigate(MainViewModel viewModel, string key) =>
        viewModel.NavigateCommand.Execute(Find(viewModel, key));

    private static NavigationItemViewModel Find(MainViewModel viewModel, string key) =>
        viewModel.NavigationItems.Single(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static NavigationItemViewModel SelectedItem(MainViewModel viewModel) =>
        viewModel.NavigationItems.Single(item => item.IsSelected);

    private static void AssertOnlySelected(MainViewModel viewModel, string expectedKey)
    {
        NavigationItemViewModel[] selected = viewModel.NavigationItems.Where(item => item.IsSelected).ToArray();
        TestSupport.Equal(1, selected.Length, $"selected navigation count for {expectedKey}");
        TestSupport.Equal(expectedKey, selected[0].Key, "selected navigation key");
    }

    private static void WithEnvironment(Action<MainViewModelTestEnvironment> test, AppTheme theme = AppTheme.Classic) =>
        TestSupport.InTemporaryDirectory(directory =>
        {
            using MainViewModelTestEnvironment environment = new(directory, theme);
            test(environment);
        });

    private sealed class MainViewModelTestEnvironment : IDisposable
    {
        private readonly PollingService pollingService;
        private readonly SensorHistoryService sensorHistoryService;
        private readonly CsvGameSessionRecorder recorder;

        public MainViewModelTestEnvironment(
            string directory,
            AppTheme theme,
            INavigationTransitionClock? navigationClock = null)
        {
            Settings = new AppSettings { Theme = AppThemeParser.ToStorageValue(theme) };
            CountingSettingsService settingsService = new(Settings);
            ThemeService = new TestThemeService(theme);
            MotionEnvironment = new FakeMotionEnvironment();
            MotionService = new MotionService(MotionEnvironment, MotionLevel.Standard, Dispatcher.CurrentDispatcher);
            ThemeTransitionService = new ThemeTransitionService(ThemeService, MotionService, Dispatcher.CurrentDispatcher);
            NavigationService = new NavigationTransitionService(navigationClock);
            pollingService = new PollingService(new CountingSensorService(), Settings);
            sensorHistoryService = new SensorHistoryService(pollingService);
            recorder = new CsvGameSessionRecorder(Path.Combine(directory, "sessions"), 8);
            ViewModel = new MainViewModel(
                Settings,
                new EmptyHardwareInfoService(),
                pollingService,
                settingsService,
                ThemeService,
                MotionService,
                ThemeTransitionService,
                NavigationService,
                new NoopStartupService(),
                Dispatcher.CurrentDispatcher,
                new SensorDiagnosticService(),
                EmptyForegroundProcessTracker.Instance,
                sensorHistoryService,
                recorder);
        }

        public TestThemeService ThemeService { get; }

        public FakeMotionEnvironment MotionEnvironment { get; }

        public MotionService MotionService { get; }

        public ThemeTransitionService ThemeTransitionService { get; }

        public NavigationTransitionService NavigationService { get; }

        public AppSettings Settings { get; }

        public MainViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            sensorHistoryService.Dispose();
            pollingService.Dispose();
            recorder.Dispose();
            NavigationService.Dispose();
            ThemeTransitionService.Dispose();
            MotionService.Dispose();
        }
    }

    private static void RunOnDispatcher(Func<Dispatcher, Task> test)
    {
        Exception? failure = null;
        using ManualResetEventSlim completed = new();
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await test(dispatcher);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    completed.Set();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "MainShellStateTests.Dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!completed.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Main shell Dispatcher test timed out.");
        thread.Join(TimeSpan.FromSeconds(3));
        if (failure is not null)
            throw new InvalidOperationException("Main shell Dispatcher test failed.", failure);
    }

    private sealed class GateNavigationClock : INavigationTransitionClock
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            _ = duration;
            Entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }

        public void Release() => released.TrySetResult();
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
