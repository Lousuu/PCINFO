using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class FinalVisualRuntimeTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Final visual runtime 01 full production startup pulse survives Bind to Lock", StartupPulseSurvivesBindToLock),
        ("Final visual runtime 02 startup reveal completes on a rendered Dashboard frame", StartupRevealCompletesOnRenderedFrame),
        ("Final visual runtime 03 theme gate covers pages cycles and resize", ThemeGateCoversPagesCyclesAndResize),
        ("Final visual runtime 04 Classic gutters and Tracework strip contain target surface pixels", ThemeSurfacePixels)
    ];

    private static void StartupPulseSurvivesBindToLock() =>
        TestSupport.InTemporaryDirectory(directory =>
        {
            EnsureApplication();
            ThemeService theme = CreateThemeService(AppTheme.Tracework);
            StepClock sequenceClock = new();
            using StartupSequenceService startup = new(
                AppTheme.Tracework,
                MotionLevel.Full,
                sequenceClock,
                new SystemStartupSequenceClock(),
                new SystemStartupSequenceClock());
            MarkCoreMilestonesReady(startup);
            using RuntimeScope scope = new(directory, theme, MotionLevel.Full, startup);
            try
            {
                scope.Show(1120d, 720d);
                startup.ReportFirstFrameGateReleased("RuntimeTestCompositorReady");
                Task sequence = startup.StartAsync();

                PumpUntil(() => startup.CurrentSnapshot.Phase == StartupSequencePhase.Index,
                    TimeSpan.FromSeconds(2), "Index phase");
                sequenceClock.ReleaseNext();
                PumpUntil(() => startup.CurrentSnapshot.Phase == StartupSequencePhase.Route,
                    TimeSpan.FromSeconds(2), "Route phase");
                sequenceClock.ReleaseNext();
                PumpUntil(() => startup.CurrentSnapshot.Phase == StartupSequencePhase.Bind,
                    TimeSpan.FromSeconds(2), "Bind phase");

                TestSupport.True(
                    scope.ViewModel.ReportStartupInitialProjection(ResolvedProjection(41)),
                    "MainViewModel forwards real InitialProjection");
                PumpUntil(
                    () => startup.CurrentSnapshot.InitialProjection.PostDataLayoutObserved,
                    TimeSpan.FromSeconds(2),
                    "post-data layout observed");

                TraceworkStartupSequenceOverlay overlay = scope.Overlay;
                PumpUntil(
                    () => overlay.IsProjectionPulseActive
                        && overlay.LastProjectionRoute is not null,
                    TimeSpan.FromSeconds(2),
                    "projection pulse starts from production path");

                FrameworkElement source = Element<FrameworkElement>(
                    overlay, "ProjectionSourceHorizontalSegment");
                FrameworkElement vertical = Element<FrameworkElement>(
                    overlay, "ProjectionVerticalBridgeSegment");
                FrameworkElement target = Element<FrameworkElement>(
                    overlay, "ProjectionTargetHorizontalSegment");
                FrameworkElement head = Element<FrameworkElement>(
                    overlay, "ProjectionPulseHead");
                FrameworkElement canvas = Element<FrameworkElement>(
                    overlay, "ProjectionPulseCanvas");
                List<double> clipWidths = [];
                List<double> segmentOpacities = [];
                List<double> headOpacities = [];
                SampleProjectionFrames(
                    source, vertical, target, head,
                    TimeSpan.FromMilliseconds(60),
                    clipWidths, segmentOpacities, headOpacities);
                sequenceClock.ReleaseNext();
                PumpUntil(
                    () => startup.CurrentSnapshot.Phase == StartupSequencePhase.Lock
                        && scope.ViewModel.StartupSequence.Phase
                            == StartupSequencePhase.Lock
                        && overlay.Snapshot?.Phase == StartupSequencePhase.Lock,
                    TimeSpan.FromSeconds(2), "Lock follows late Bind request");
                FrameworkElement commit = Element<FrameworkElement>(
                    overlay, "CommitGroup");
                if (!overlay.ProjectionPulseCompletedAt.HasValue)
                {
                    TestSupport.True(
                        overlay.IsProjectionPulseActive || overlay.IsProjectionPulsePending,
                        "latched projection remains owned in Lock");
                    TestSupport.True(overlay.IsCommitPendingForProjection,
                        "COMMIT waits for projection visual");
                    TestSupport.Equal(Visibility.Collapsed, commit.Visibility,
                        "COMMIT hidden before pulse completion");
                }
                SampleProjectionFrames(
                    source, vertical, target, head,
                    TimeSpan.FromMilliseconds(180),
                    clipWidths, segmentOpacities, headOpacities);

                PumpUntil(
                    () => overlay.ProjectionPulseCompletedAt.HasValue
                        && commit.Visibility == Visibility.Visible,
                    TimeSpan.FromSeconds(2),
                    "pulse completes before COMMIT");
                TestSupport.True(canvas.IsLoaded && canvas.Opacity > 0d,
                    "pulse canvas was a loaded visible surface");
                TestSupport.True(
                    overlay.LastProjectionRoute?.TotalRouteLength > 24d,
                    "projection geometry exceeds minimum route length");
                TestSupport.True(clipWidths.Distinct().Count() >= 2,
                    "projection owns at least two distinct clip widths");
                TestSupport.True(segmentOpacities.Any(value => value > 0d),
                    "at least one projection segment becomes visible");
                TestSupport.True(headOpacities.Any(value => value > 0d),
                    "Full pulse head becomes visible");
                TestSupport.True(
                    overlay.CommitVisualStartedAt > overlay.ProjectionPulseCompletedAt,
                    "COMMIT starts after ProjectionPulseCompleted");

                sequenceClock.ReleaseNext();
                PumpUntil(() => startup.CurrentSnapshot.Phase == StartupSequencePhase.Reveal,
                    TimeSpan.FromSeconds(2), "Reveal phase");
                PumpUntil(() => sequence.IsCompleted, TimeSpan.FromSeconds(3),
                    "startup sequence completes through visual gate");
                sequence.GetAwaiter().GetResult();
            }
            finally
            {
                scope.DisposeWindow();
                theme.ApplyTheme(AppTheme.Classic);
            }
        });

    private static void StartupRevealCompletesOnRenderedFrame() =>
        TestSupport.InTemporaryDirectory(directory =>
        {
            EnsureApplication();
            ThemeService theme = CreateThemeService(AppTheme.Tracework);
            using StartupSequenceService startup = new(
                AppTheme.Tracework,
                MotionLevel.Full,
                new ImmediateStartupClock(),
                new SystemStartupSequenceClock(),
                new SystemStartupSequenceClock());
            MarkCoreMilestonesReady(startup);
            startup.ReportInitialProjection(ResolvedProjection(52));
            startup.ReportPostDataLayout(52);
            using RuntimeScope scope = new(directory, theme, MotionLevel.Full, startup);
            try
            {
                scope.Show(1120d, 720d);
                startup.ReportFirstFrameGateReleased("RuntimeTestCompositorReady");
                Task sequence = startup.StartAsync();
                PumpUntil(() => startup.CurrentSnapshot.Phase == StartupSequencePhase.Reveal,
                    TimeSpan.FromSeconds(2), "Reveal phase starts");

                List<double> overlayValues = [];
                List<double> rootValues = [];
                List<double> primaryValues = [];
                List<double> secondaryValues = [];
                DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                while (!sequence.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Pump(TimeSpan.FromMilliseconds(16));
                    overlayValues.Add(scope.Overlay.Opacity);
                    if (scope.PageHost.ActiveRoot is FrameworkElement root)
                        rootValues.Add(root.Opacity);
                    if (scope.PageHost.ActivePrimary is FrameworkElement primary)
                        primaryValues.Add(primary.Opacity);
                    if (scope.PageHost.ActiveSecondary is FrameworkElement secondary)
                        secondaryValues.Add(secondary.Opacity);
                }

                TestSupport.True(sequence.IsCompleted,
                    "visual completion unblocks logical completion");
                sequence.GetAwaiter().GetResult();
                TestSupport.True(startup.WasRevealVisualCompletionReported,
                    "MainShellHost reports RevealVisualCompleted");
                TestSupport.True(
                    startup.LogicalSequenceCompletedAt >= startup.RevealVisualCompletedAt,
                    "logical Complete is not earlier than visual Complete");
                TestSupport.True(DistinctIntermediate(overlayValues, 0d, 1d) >= 4,
                    "Overlay has at least four intermediate opacity samples");
                TestSupport.True(DistinctIntermediate(rootValues, 0.32d, 1d) >= 4,
                    "Dashboard Root has at least four intermediate samples");
                int primaryFirst = FirstAbove(primaryValues, 0.43d);
                int secondaryFirst = FirstAbove(secondaryValues, 0.25d);
                TestSupport.True(
                    primaryFirst >= 0 && secondaryFirst >= 0
                        && primaryFirst < secondaryFirst,
                    $"Primary begins before Secondary (primary={primaryFirst}, secondary={secondaryFirst})");
                TestSupport.True(
                    scope.Overlay.Visibility == Visibility.Collapsed
                        && scope.PageHost.ActiveRoot?.Opacity == 1d,
                    "completed visible frame has collapsed overlay and final Dashboard");
            }
            finally
            {
                scope.DisposeWindow();
                theme.ApplyTheme(AppTheme.Classic);
            }
        });

    private static void ThemeGateCoversPagesCyclesAndResize() =>
        TestSupport.InTemporaryDirectory(directory =>
        {
            EnsureApplication();
            ThemeService theme = CreateThemeService(AppTheme.Tracework);
            using RuntimeScope scope = new(
                directory, theme, MotionLevel.Standard, startup: null,
                themeClock: new RuntimeThemeClock());
            try
            {
                scope.Show(1600d, 900d);
                string[] pageKeys =
                    ["Dashboard", "Cpu", "AdvancedSensors", "Settings"];
                int transitions = 0;
                foreach (string pageKey in pageKeys)
                {
                    scope.Navigate(pageKey);
                    ApplyThemeAndPump(scope, AppTheme.Classic);
                    transitions++;
                    ApplyThemeAndPump(scope, AppTheme.Tracework);
                    transitions++;
                }

                Task<ThemeTransitionResult> resizeTransition =
                    scope.ThemeTransitions.ApplyThemeAsync(AppTheme.Classic);
                scope.Window.Width = 1366d;
                scope.Window.Height = 768d;
                PumpUntil(() => resizeTransition.IsCompleted,
                    TimeSpan.FromSeconds(2), "resize transition completes");
                TestSupport.Equal(
                    ThemeTransitionStatus.Applied,
                    resizeTransition.GetAwaiter().GetResult().Status,
                    "resize transition status");
                AssertThemeFinal(scope, AppTheme.Classic);
                transitions++;
                ApplyThemeAndPump(scope, AppTheme.Tracework);
                transitions++;
                TestSupport.Equal(10, transitions, "ten real theme transitions");
            }
            finally
            {
                scope.DisposeWindow();
                theme.ApplyTheme(AppTheme.Classic);
            }
        });

    private static void ThemeSurfacePixels() =>
        TestSupport.InTemporaryDirectory(directory =>
        {
            EnsureApplication();
            ThemeService theme = CreateThemeService(AppTheme.Tracework);
            using RuntimeScope scope = new(
                directory, theme, MotionLevel.Standard, startup: null,
                themeClock: new RuntimeThemeClock());
            try
            {
                scope.Show(1600d, 900d);
                ApplyThemeAndPump(scope, AppTheme.Classic);
                TestSupport.Equal(
                    Visibility.Collapsed,
                    scope.Overlay.Visibility,
                    "startup overlay is not covering Classic surface");
                Color classic = ((SolidColorBrush)scope.Shell.FindResource(
                    "AppBackgroundBrush")).Color;
                RenderTargetBitmap classicBitmap = Render(scope.Shell);
                int right = Math.Max(0, classicBitmap.PixelWidth - 21);
                int bottomGap = Math.Max(0, classicBitmap.PixelHeight - 35);
                foreach ((int x, int y) in new[]
                         {
                             (20, 300), (right, 300), (800, 120), (800, bottomGap)
                         })
                {
                    Color sample = Pixel(classicBitmap, x, y);
                    TestSupport.True(ColorDistance(sample, classic) <= 8,
                        $"Classic surface pixel {x},{y} matches AppBackgroundBrush "
                        + $"(sample=#{sample.R:X2}{sample.G:X2}{sample.B:X2}, "
                        + $"expected=#{classic.R:X2}{classic.G:X2}{classic.B:X2})");
                    TestSupport.True(
                        ColorDistance(sample, Color.FromRgb(0x0B, 0x0E, 0x11)) > 20,
                        $"Classic surface pixel {x},{y} is not Safety Background");
                }

                ApplyThemeAndPump(scope, AppTheme.Tracework);
                RenderTargetBitmap traceworkBitmap = Render(scope.Shell);
                int broadLightRun = 0;
                int longestBroadLightRun = 0;
                for (int y = 58; y < 72; y++)
                {
                    int lightRun = 0;
                    int longestLightRun = 0;
                    int lightSamples = 0;
                    int samples = 0;
                    for (int x = 124; x < 1580; x += 4)
                    {
                        Color sample = Pixel(traceworkBitmap, x, y);
                        bool light =
                            sample.R > 180 && sample.G > 180 && sample.B > 180;
                        samples++;
                        lightSamples += light ? 1 : 0;
                        lightRun = light ? lightRun + 1 : 0;
                        longestLightRun = Math.Max(longestLightRun, lightRun);
                    }
                    bool broadLightRow = lightSamples >= samples * 9 / 10;
                    broadLightRun = broadLightRow ? broadLightRun + 1 : 0;
                    longestBroadLightRun =
                        Math.Max(longestBroadLightRun, broadLightRun);
                    if (!broadLightRow)
                    {
                        TestSupport.True(
                            longestLightRun <= 3
                                && lightSamples <= samples / 20,
                            $"Tracework strip row remains dark at y={y} "
                            + $"(longest={longestLightRun * 4} DIP; "
                            + $"light={lightSamples}/{samples})");
                    }
                }
                TestSupport.True(
                    longestBroadLightRun <= 1,
                    "Tracework strip contains no multi-row light band "
                    + $"(longest={longestBroadLightRun} DIP)");
            }
            finally
            {
                scope.DisposeWindow();
                theme.ApplyTheme(AppTheme.Classic);
            }
        });

    private static void SampleProjectionFrames(
        FrameworkElement source,
        FrameworkElement vertical,
        FrameworkElement target,
        FrameworkElement head,
        TimeSpan duration,
        List<double> clipWidths,
        List<double> segmentOpacities,
        List<double> headOpacities)
    {
        DateTime deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(12));
            if (source.Clip is RectangleGeometry clip
                && clip.Rect.Width > 0d
                && clip.Rect.Width < source.Width)
            {
                clipWidths.Add(clip.Rect.Width);
            }
            segmentOpacities.Add(Math.Max(
                source.Opacity,
                Math.Max(vertical.Opacity, target.Opacity)));
            headOpacities.Add(head.Opacity);
        }
    }

    private static void ApplyThemeAndPump(RuntimeScope scope, AppTheme target)
    {
        Task<ThemeTransitionResult> transition =
            scope.ThemeTransitions.ApplyThemeAsync(target);
        PumpUntil(() => transition.IsCompleted, TimeSpan.FromSeconds(2),
            $"theme {target} completes");
        ThemeTransitionResult result = transition.GetAwaiter().GetResult();
        Pump(TimeSpan.FromMilliseconds(20));
        TestSupport.True(
            result.Status is ThemeTransitionStatus.Applied
                or ThemeTransitionStatus.AlreadyCurrent,
            $"theme {target} status");
        TestSupport.True(
            scope.ThemeTransitions.Current.Phase == ThemeTransitionPhase.Idle,
            $"theme {target} service reaches Idle before returning "
            + $"(result={result.Status}; committed={result.WasThemeCommitted}; "
            + $"current={scope.ThemeTransitions.Current.Phase}/v{scope.ThemeTransitions.Current.Version})");
        AssertThemeFinal(scope, target);
    }

    private static void AssertThemeFinal(RuntimeScope scope, AppTheme target)
    {
        bool tracework = target == AppTheme.Tracework;
        FrameworkElement classicChrome = Element<FrameworkElement>(
            scope.Shell, "ClassicChrome");
        FrameworkElement traceworkChrome = Element<FrameworkElement>(
            scope.Shell, "TraceworkChrome");
        FrameworkElement rewire = Element<FrameworkElement>(
            scope.Shell, "SystemRewireOverlay");
        FrameworkElement relay = Element<FrameworkElement>(
            scope.Shell, "RelayBandOverlay");
        Border surface = Element<Border>(scope.Shell, "ThemeSurface");
        TestSupport.Equal(
            tracework ? Visibility.Collapsed : Visibility.Visible,
            classicChrome.Visibility, "Classic Chrome final visibility");
        TestSupport.Equal(
            tracework ? Visibility.Visible : Visibility.Collapsed,
            traceworkChrome.Visibility, "Tracework Chrome final visibility");
        TestSupport.Equal(
            tracework
                ? new Thickness(120d, 72d, 16d, 46d)
                : new Thickness(18d, 128d, 18d, 42d),
            scope.PageHost.Margin, "PageHost final margin");
        TestSupport.Equal(1d, scope.PageHost.Opacity, "PageHost final opacity");
        TestSupport.True(scope.PageHost.Clip is null, "PageHost final clip");
        SystemRewireOverlay rewireOverlay =
            TestSupport.NotNull(rewire as SystemRewireOverlay,
                "SYSTEM REWIRE control");
        TestSupport.True(
            rewire.Visibility == Visibility.Collapsed,
            "SYSTEM REWIRE hidden"
            + $" (visibility={rewire.Visibility};"
            + $" vm={scope.ViewModel.ThemeTransition.Phase}/v{scope.ViewModel.ThemeTransition.Version};"
            + $" service={scope.ThemeTransitions.Current.Phase}/v{scope.ThemeTransitions.Current.Version};"
            + $" overlay={rewireOverlay.Snapshot?.Phase}/v{rewireOverlay.Snapshot?.Version};"
            + $" active={rewireOverlay.Snapshot?.IsActive}; suppressed={rewireOverlay.IsSuppressed})");
        TestSupport.Equal(Visibility.Collapsed, relay.Visibility,
            "RelayBand hidden");
        TestSupport.True(surface.Background is SolidColorBrush,
            "ThemeSurface realized brush");
        ThemeVisualReadinessResult? readiness =
            scope.ThemeTransitions.LastVisualReadinessResult;
        TestSupport.True(
            readiness?.IsReady == true,
            $"visual readiness result is ready ({readiness?.FailureReason ?? "not reported"})");
    }

    private static void MarkCoreMilestonesReady(StartupSequenceService service)
    {
        foreach (StartupMilestoneId id in Enum.GetValues<StartupMilestoneId>()
                     .Where(id => id != StartupMilestoneId.ShellSurface))
        {
            service.ReportMilestone(id, StartupMilestoneState.Ready, "runtime ready");
        }
    }

    private static StartupInitialProjectionSnapshot ResolvedProjection(long version) =>
        new(
            version,
            Enum.GetValues<HardwareOverviewKind>()
                .Select(kind => new StartupProjectionSlotSnapshot(
                    kind, StartupProjectionState.Value, "runtime projection"))
                .ToArray(),
            DispatcherApplied: true,
            PostDataLayoutObserved: false);

    private static int DistinctIntermediate(
        IEnumerable<double> values, double lower, double upper) =>
        values
            .Where(value => value > lower + 0.005d && value < upper - 0.005d)
            .Select(value => Math.Round(value, 2))
            .Distinct()
            .Count();

    private static int FirstAbove(IReadOnlyList<double> values, double threshold)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] > threshold)
                return index;
        }
        return -1;
    }

    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        int width = Math.Max(1, (int)Math.Round(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Round(element.ActualHeight));
        RenderTargetBitmap bitmap = new(
            width, height, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static Color Pixel(RenderTargetBitmap bitmap, int x, int y)
    {
        byte[] bytes = new byte[4];
        bitmap.CopyPixels(
            new Int32Rect(
                Math.Clamp(x, 0, bitmap.PixelWidth - 1),
                Math.Clamp(y, 0, bitmap.PixelHeight - 1),
                1,
                1),
            bytes,
            4,
            0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static int ColorDistance(Color left, Color right) =>
        Math.Abs(left.R - right.R)
        + Math.Abs(left.G - right.G)
        + Math.Abs(left.B - right.B);

    private static T Element<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        TestSupport.NotNull(root.FindName(name) as T, name);

    private static void PumpUntil(
        Func<bool> condition, TimeSpan timeout, string label)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            Pump(TimeSpan.FromMilliseconds(10));
        TestSupport.True(condition(), label);
    }

    private static void Pump(TimeSpan duration)
    {
        DateTime deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(
                () => { },
                DispatcherPriority.ApplicationIdle);
            Thread.Sleep(1);
        }
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
        }
        Application.Current!.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private static ThemeService CreateThemeService(AppTheme initialTheme)
    {
        ThemeService theme = new(Application.Current!);
        AppTheme opposite = initialTheme == AppTheme.Tracework
            ? AppTheme.Classic
            : AppTheme.Tracework;
        TestSupport.True(theme.ApplyTheme(opposite),
            $"prepare clean {opposite} resources");
        TestSupport.True(theme.ApplyTheme(initialTheme),
            $"prepare clean {initialTheme} resources");
        return theme;
    }

    private sealed class RuntimeScope : IDisposable
    {
        private readonly MotionService motion;
        private readonly NavigationTransitionService navigation;
        private readonly PollingService polling;
        private readonly SensorHistoryService history;
        private readonly CsvGameSessionRecorder recorder;
        private readonly MainViewModel viewModel;
        private readonly StartupSequenceService? ownedStartup;
        private bool windowDisposed;

        public RuntimeScope(
            string directory,
            IThemeService theme,
            MotionLevel motionLevel,
            IStartupSequenceService? startup,
            IThemeTransitionClock? themeClock = null)
        {
            AppSettings settings = new()
            {
                Theme = AppThemeParser.ToStorageValue(theme.CurrentTheme),
                Motion = motionLevel.ToString()
            };
            motion = new MotionService(
                new FakeMotionEnvironment(), motionLevel,
                Dispatcher.CurrentDispatcher);
            ThemeTransitions = new ThemeTransitionService(
                theme, motion, Dispatcher.CurrentDispatcher,
                themeClock ?? new RuntimeThemeClock());
            navigation = new NavigationTransitionService(
                new ImmediateNavigationClock());
            polling = new PollingService(new CountingSensorService(), settings);
            history = new SensorHistoryService(polling);
            recorder = new CsvGameSessionRecorder(
                Path.Combine(directory, "sessions"), 8);
            IStartupSequenceService effectiveStartup = startup
                ?? (ownedStartup = new StartupSequenceService(
                    theme.CurrentTheme,
                    motionLevel,
                    new ImmediateStartupClock(),
                    new ImmediateStartupClock(),
                    new ImmediateStartupClock()));
            Window = new HardwareVision.MainWindow(
                settings,
                new EmptyHardwareInfoService(),
                polling,
                new CountingSettingsService(settings),
                theme,
                motion,
                ThemeTransitions,
                navigation,
                effectiveStartup,
                new NoopStartupService(),
                new SensorDiagnosticService(),
                EmptyForegroundProcessTracker.Instance,
                history,
                recorder)
            {
                Left = -32000d,
                Top = -32000d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            viewModel = TestSupport.NotNull(
                Window.DataContext as MainViewModel,
                "MainWindow MainViewModel");
            Shell = TestSupport.NotNull(
                Window.FindName("MainShell") as MainShellHost,
                "MainWindow MainShellHost");
            ownedStartup?.CompleteForHiddenWindow();
        }

        public ThemeTransitionService ThemeTransitions { get; }
        public MainViewModel ViewModel => viewModel;
        public MainShellHost Shell { get; }
        public HardwareVision.MainWindow Window { get; }
        public MotionTransitionHost PageHost =>
            Element<MotionTransitionHost>(Shell, "PageHost");
        public TraceworkStartupSequenceOverlay Overlay =>
            Element<TraceworkStartupSequenceOverlay>(
                Shell, "StartupSequenceOverlay");

        public void Show(double width, double height)
        {
            Window.Width = width;
            Window.Height = height;
            Window.Show();
            Window.ApplyTemplate();
            Shell.ApplyTemplate();
            Pump(TimeSpan.FromMilliseconds(30));
        }

        public void Navigate(string pageKey)
        {
            NavigationItemViewModel item = viewModel.NavigationItems.Single(
                candidate => candidate.Key == pageKey);
            viewModel.NavigateCommand.Execute(item);
            Pump(TimeSpan.FromMilliseconds(30));
        }

        public void DisposeWindow()
        {
            if (windowDisposed)
                return;
            windowDisposed = true;
            Window.Close();
        }

        public void Dispose()
        {
            DisposeWindow();
            viewModel.Dispose();
            ThemeTransitions.Dispose();
            navigation.Dispose();
            history.Dispose();
            polling.Dispose();
            recorder.Dispose();
            motion.Dispose();
            ownedStartup?.Dispose();
        }
    }

    private sealed class StepClock : IStartupSequenceClock
    {
        private readonly ConcurrentQueue<TaskCompletionSource> pending = new();

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            TaskCompletionSource completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(completion);
            cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }

        public void ReleaseNext()
        {
            TestSupport.True(
                pending.TryDequeue(out TaskCompletionSource? completion),
                "startup clock has a pending phase delay");
            completion!.TrySetResult();
        }
    }

    private sealed class ImmediateStartupClock : IStartupSequenceClock
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class RuntimeThemeClock : IThemeTransitionClock
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            delay == TimeSpan.FromMilliseconds(900)
                ? Task.Delay(delay, cancellationToken)
                : Task.CompletedTask;
    }

    private sealed class ImmediateNavigationClock : INavigationTransitionClock
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyHardwareInfoService : IHardwareInfoService
    {
        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot
            {
                Timestamp = DateTimeOffset.Now
            });

        public Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>([]);

        public Task<HardwareSummary> GetHardwareSummaryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new HardwareSummary(
                    "runtime", "runtime", null, null, null, null));
    }
}
