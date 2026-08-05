using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class PageTransitionSmoothnessTests
{
    private const int SwitchCount = 60;

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Page transition production clock commits before decorative shift", ProductionClockCommitsBeforeDecorativeShift)
    ];

    private static void ProductionClockCommitsBeforeDecorativeShift() =>
        TestSupport.InTemporaryDirectory(directory =>
        {
            EnsureApplication();
            AppSettings settings = new()
            {
                Theme = AppThemeParser.ToStorageValue(AppTheme.Tracework)
            };
            CountingSettingsService settingsService = new(settings);
            TestThemeService themeService = new(AppTheme.Tracework);
            FakeMotionEnvironment motionEnvironment = new();
            using MotionService motionService = new(
                motionEnvironment,
                MotionLevel.Full,
                Dispatcher.CurrentDispatcher);
            using ThemeTransitionService themeTransitions = new(
                themeService,
                motionService,
                Dispatcher.CurrentDispatcher);
            using NavigationTransitionService navigationTransitions = new(
                new SystemNavigationTransitionClock());
            using PollingService polling = new(new CountingSensorService(), settings);
            using SensorHistoryService history = new(polling);
            using CsvGameSessionRecorder recorder = new(
                Path.Combine(directory, "sessions"),
                8);
            using MainViewModel viewModel = new(
                settings,
                new EmptyHardwareInfoService(),
                polling,
                settingsService,
                themeService,
                motionService,
                themeTransitions,
                navigationTransitions,
                new NoopStartupService(),
                Dispatcher.CurrentDispatcher,
                new SensorDiagnosticService(),
                EmptyForegroundProcessTracker.Instance,
                history,
                recorder);
            MainShellHost shell = new() { DataContext = viewModel };
            using WindowScope scope = new(shell);
            MotionTransitionHost pageHost = TestSupport.NotNull(
                shell.FindName("PageHost") as MotionTransitionHost,
                "MainShellHost PageHost");
            CachedPagePresenter presenter = TestSupport.NotNull(
                pageHost.Template.FindName("PagePresenter", pageHost) as CachedPagePresenter,
                "PageHost cached presenter");
            PageTransitionProbe probe = new(viewModel, navigationTransitions, presenter);
            using (probe)
            {
                string[] route =
                ["Cpu", "Gpu", "AdvancedSensors", "GamePerformance", "Dashboard"];
                for (int index = 0; index < SwitchCount; index++)
                {
                    string key = route[index % route.Length];
                    NavigationItemViewModel item = viewModel.NavigationItems.Single(
                        candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
                    bool cacheHit = item.IsPageCreated;
                    PageTransitionObservation observation = probe.Begin(key, cacheHit);
                    viewModel.NavigateCommand.Execute(item);
                    probe.HandlerReturned(observation);
                    PumpUntil(
                        () => observation.FirstVisibleRenderTimestamp > 0
                            && !navigationTransitions.CurrentSnapshot.IsActive,
                        TimeSpan.FromSeconds(3),
                        $"production-clock transition {index + 1} to {key}");
                    probe.Complete(observation);
                }
            }

            IReadOnlyList<PageTransitionObservation> samples = probe.Samples;
            TestSupport.Equal(SwitchCount, samples.Count, "real-time switch sample count");
            TestSupport.True(
                samples.All(sample => sample.HandlerReturnTimestamp > 0),
                "every request records synchronous handler return");
            TestSupport.True(
                samples.All(sample => sample.CurrentPageCommitTimestamp > 0),
                "every request records CurrentPage commit");
            TestSupport.True(
                samples.All(sample => sample.PresenterAttachTimestamp > 0),
                "every request records presenter incoming attach");
            TestSupport.True(
                samples.All(sample => sample.FirstVisibleRenderTimestamp > 0),
                "every request records first visible render");
            TestSupport.True(
                samples.All(sample => sample.Phases.ContainsKey(NavigationTransitionPhase.Route)
                    && sample.Phases.ContainsKey(NavigationTransitionPhase.Shift)
                    && sample.Phases.ContainsKey(NavigationTransitionPhase.Relay)
                    && sample.Phases.ContainsKey(NavigationTransitionPhase.Settle)),
                "every request records production transition phases");
            TestSupport.True(
                samples.All(sample => sample.CurrentPageCommitTimestamp
                    < sample.Phases[NavigationTransitionPhase.Shift]),
                "every CurrentPage commit precedes the decorative Shift phase");
            TestSupport.True(
                samples.All(sample => sample.PresenterAttachTimestamp
                    < sample.Phases[NavigationTransitionPhase.Relay]),
                "every incoming presenter attaches before the decorative Relay phase");
            TestSupport.True(
                samples.All(sample => sample.FirstVisibleRenderTimestamp
                    < sample.Phases[NavigationTransitionPhase.Relay]),
                "every incoming page renders visibly before the decorative Relay phase");

            double[] requestToCommit = samples
                .Select(sample => sample.ElapsedMilliseconds(sample.CurrentPageCommitTimestamp))
                .Order()
                .ToArray();
            WriteStatistics("request-to-current-page-commit", requestToCommit, samples);
            WriteStatistics(
                "request-handler-return",
                samples.Select(sample => sample.ElapsedMilliseconds(sample.HandlerReturnTimestamp)).Order().ToArray(),
                samples);
            WriteStatistics(
                "commit-to-first-visible-render",
                samples.Select(sample => sample.BetweenMilliseconds(
                    sample.CurrentPageCommitTimestamp,
                    sample.FirstVisibleRenderTimestamp)).Order().ToArray(),
                samples);

            double p50Commit = Percentile(requestToCommit, 0.50d);
            TestSupport.True(
                p50Commit < 50d,
                $"Full content commit is independent of the 70ms decorative timeline (P50={p50Commit:0.###}ms)");
        });

    private static void WriteStatistics(
        string stage,
        double[] values,
        IReadOnlyList<PageTransitionObservation> samples)
    {
        Console.WriteLine(
            "PAGE_TRANSITION_STATS "
            + $"utc={DateTimeOffset.UtcNow:O}; motion=Full; stage={stage}; "
            + $"count={values.Length}; p50={Percentile(values, 0.50d):0.###}ms; "
            + $"p90={Percentile(values, 0.90d):0.###}ms; "
            + $"p95={Percentile(values, 0.95d):0.###}ms; "
            + $"p99={Percentile(values, 0.99d):0.###}ms; max={values[^1]:0.###}ms");
        PageTransitionObservation maximum = samples
            .MaxBy(sample => stage switch
            {
                "request-handler-return" => sample.ElapsedMilliseconds(sample.HandlerReturnTimestamp),
                "commit-to-first-visible-render" => sample.BetweenMilliseconds(
                    sample.CurrentPageCommitTimestamp,
                    sample.FirstVisibleRenderTimestamp),
                _ => sample.ElapsedMilliseconds(sample.CurrentPageCommitTimestamp)
            })!;
        Console.WriteLine(
            "PAGE_TRANSITION_MAX "
            + $"utc={maximum.Utc:O}; page={maximum.PageKey}; cacheHit={maximum.CacheHit}; "
            + $"motion=Full; stage={stage}");
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
        return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string label)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            DispatcherFrame frame = new();
            DispatcherTimer timer = new(
                TimeSpan.FromMilliseconds(5),
                DispatcherPriority.Background,
                (_, _) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
        TestSupport.True(condition(), label);
    }

    private sealed class PageTransitionProbe : IDisposable
    {
        private readonly MainViewModel viewModel;
        private readonly NavigationTransitionService transitions;
        private readonly CachedPagePresenter presenter;
        private PageTransitionObservation? active;
        private bool disposed;

        public PageTransitionProbe(
            MainViewModel viewModel,
            NavigationTransitionService transitions,
            CachedPagePresenter presenter)
        {
            this.viewModel = viewModel;
            this.transitions = transitions;
            this.presenter = presenter;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            transitions.TransitionChanged += OnTransitionChanged;
            presenter.ContentPresented += OnContentPresented;
            CompositionTarget.Rendering += OnRendering;
        }

        public List<PageTransitionObservation> Samples { get; } = [];

        public PageTransitionObservation Begin(string pageKey, bool cacheHit)
        {
            PageTransitionObservation observation = new(
                pageKey,
                cacheHit,
                DateTimeOffset.UtcNow,
                Stopwatch.GetTimestamp());
            active = observation;
            return observation;
        }

        public void HandlerReturned(PageTransitionObservation observation) =>
            observation.HandlerReturnTimestamp = Stopwatch.GetTimestamp();

        public void Complete(PageTransitionObservation observation)
        {
            TestSupport.True(ReferenceEquals(active, observation), "completed probe is active");
            Samples.Add(observation);
            active = null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            CompositionTarget.Rendering -= OnRendering;
            presenter.ContentPresented -= OnContentPresented;
            transitions.TransitionChanged -= OnTransitionChanged;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (active is null || args.PropertyName != nameof(MainViewModel.CurrentPage))
            {
                return;
            }
            active.ExpectedPage = viewModel.CurrentPage;
            active.CurrentPageCommitTimestamp = Stopwatch.GetTimestamp();
        }

        private void OnTransitionChanged(object? sender, NavigationTransitionChangedEventArgs args)
        {
            PageTransitionObservation? observation = active;
            NavigationTransitionSnapshot snapshot = args.CurrentSnapshot;
            if (observation is null
                || !string.Equals(snapshot.TargetPage, observation.PageKey, StringComparison.Ordinal))
            {
                return;
            }
            observation.Phases.TryAdd(snapshot.Phase, Stopwatch.GetTimestamp());
        }

        private void OnContentPresented(object? sender, EventArgs args)
        {
            if (active?.ExpectedPage is object expected
                && ReferenceEquals(presenter.PresentedContent, expected))
            {
                active.PresenterAttachTimestamp = Stopwatch.GetTimestamp();
            }
        }

        private void OnRendering(object? sender, EventArgs args)
        {
            if (active is not { FirstVisibleRenderTimestamp: 0 } observation
                || observation.ExpectedPage is null
                || !ReferenceEquals(presenter.PresentedContent, observation.ExpectedPage)
                || presenter.PresentedRoot is not { ActualWidth: > 0d, ActualHeight: > 0d, IsVisible: true })
            {
                return;
            }
            observation.FirstVisibleRenderTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private sealed class PageTransitionObservation(
        string pageKey,
        bool cacheHit,
        DateTimeOffset utc,
        long requestTimestamp)
    {
        public string PageKey { get; } = pageKey;
        public bool CacheHit { get; } = cacheHit;
        public DateTimeOffset Utc { get; } = utc;
        public long RequestTimestamp { get; } = requestTimestamp;
        public long HandlerReturnTimestamp { get; set; }
        public long CurrentPageCommitTimestamp { get; set; }
        public long PresenterAttachTimestamp { get; set; }
        public long FirstVisibleRenderTimestamp { get; set; }
        public object? ExpectedPage { get; set; }
        public Dictionary<NavigationTransitionPhase, long> Phases { get; } = [];

        public double ElapsedMilliseconds(long timestamp) =>
            Stopwatch.GetElapsedTime(RequestTimestamp, timestamp).TotalMilliseconds;

        public double BetweenMilliseconds(long start, long end) =>
            Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
    }

    private sealed class WindowScope : IDisposable
    {
        public WindowScope(FrameworkElement content)
        {
            Window = new Window
            {
                Content = content,
                Width = 1120d,
                Height = 720d,
                Left = -32000d,
                Top = -32000d,
                Opacity = 1d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            Window.Show();
            Window.UpdateLayout();
            PumpUntil(
                () => PresentationSource.FromVisual(Window) is not null,
                TimeSpan.FromSeconds(1),
                "shown WPF window has a presentation source");
        }

        public Window Window { get; }

        public void Dispose()
        {
            Window.Content = null;
            Window.Close();
        }
    }

    private sealed class EmptyHardwareInfoService : IHardwareInfoService
    {
        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot { Timestamp = DateTimeOffset.Now });

        public Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>([]);

        public Task<HardwareSummary> GetHardwareSummaryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("test", "test", null, null, null, null));
    }
}
