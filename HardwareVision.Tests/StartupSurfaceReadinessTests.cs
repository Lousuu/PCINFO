using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class StartupSurfaceReadinessTests
{
    private static readonly Size[] LayoutSizes = [new(1120, 720), new(1600, 900)];

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string, Action)> tests =
        [
            ("Startup surface atomic shell and visual readiness", AtomicSurfaceReadiness),
            ("Startup surface zero size waits for a valid entry", ZeroSizeThenValidSurface),
            ("Startup lifecycle waits before visual surface and starts once", TestSupport.Run(WaitsForSurfaceAndStartsOnceAsync)),
            ("Startup real WPF Show Loaded surface completes lifecycle", RealWpfLifecycle),
            ("INITIAL TRACE runtime layout 1120x720", () => VerifyLayout(LayoutSizes[0])),
            ("INITIAL TRACE runtime layout 1600x900", () => VerifyLayout(LayoutSizes[1]))
        ];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"Startup visual readiness fail-open {iteration:00}/20", VisualReadinessTimeoutFailsOpen));
        }
        return tests;
    }

    private static void AtomicSurfaceReadiness()
    {
        using StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Standard);
        long before = service.CurrentSnapshot.Version;
        int changes = 0;
        StartupSequenceSnapshot? published = null;
        service.SnapshotChanged += (_, args) =>
        {
            changes++;
            published = args.CurrentSnapshot;
        };

        TestSupport.True(service.ReportSurfaceReady(1120, 720, "Loaded"), "first surface report");
        TestSupport.Equal(1, changes, "one atomic publish");
        TestSupport.Equal(before + 1, service.CurrentSnapshot.Version, "one version increment");
        TestSupport.True(TestSupport.NotNull(published, "published snapshot").ShellReady, "shell ready atomically");
        TestSupport.True(published!.VisualReady, "visual ready atomically");
        TestSupport.False(service.ReportSurfaceReady(1600, 900, "duplicate"), "duplicate ignored");
        TestSupport.Equal(1, changes, "duplicate publishes nothing");
    }

    private static void ZeroSizeThenValidSurface()
    {
        using StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Standard);
        int changes = 0;
        service.SnapshotChanged += (_, _) => changes++;
        TestSupport.False(service.ReportSurfaceReady(0, 720, "Loaded"), "zero width");
        TestSupport.False(service.ReportSurfaceReady(1120, 0, "LayoutUpdated"), "zero height");
        TestSupport.False(service.CurrentSnapshot.VisualReady, "zero size does not commit");
        TestSupport.Equal(0, changes, "zero size publishes nothing");
        TestSupport.True(service.ReportSurfaceReady(1120, 720, "SizeChanged"), "later valid size");
        TestSupport.True(service.CurrentSnapshot.ShellReady && service.CurrentSnapshot.VisualReady, "valid surface succeeds");
    }

    private static async Task WaitsForSurfaceAndStartsOnceAsync()
    {
        ManualClock readiness = new();
        using StartupSequenceService service = ReadyExceptSurface(new ImmediateClock(), readiness);
        ConcurrentQueue<StartupSequencePhase> phases = new();
        service.SnapshotChanged += (_, args) => phases.Enqueue(args.CurrentSnapshot.Phase);
        Task first = service.StartAsync();
        Task second = service.StartAsync();
        TestSupport.True(ReferenceEquals(first, second), "StartAsync returns the one active task");
        await Task.Yield();
        TestSupport.Equal(StartupSequencePhase.Dormant, service.CurrentSnapshot.Phase, "waits before surface");
        TestSupport.True(service.ReportSurfaceReady(1120, 720, "ContentRendered"), "surface report");
        await first;
        StartupSequencePhase[] expected =
        [StartupSequencePhase.Index, StartupSequencePhase.Route, StartupSequencePhase.Bind,
         StartupSequencePhase.Lock, StartupSequencePhase.Reveal, StartupSequencePhase.Complete];
        TestSupport.True(expected.SequenceEqual(phases.ToArray().Where(phase => phase != StartupSequencePhase.Dormant)), "phase order after surface");
    }

    private static void VisualReadinessTimeoutFailsOpen()
    {
        using StartupSequenceService service = ReadyExceptSurface(new ImmediateClock(), new ImmediateClock());
        service.StartAsync().GetAwaiter().GetResult();
        StartupSequenceSnapshot snapshot = service.CurrentSnapshot;
        TestSupport.Equal(StartupSequencePhase.Complete, snapshot.Phase, "timeout phase");
        TestSupport.True(snapshot.HasCompleted, "timeout completed");
        TestSupport.False(snapshot.VisualReady, "timeout does not fake visual readiness");
        TestSupport.False(snapshot.ShellReady, "timeout does not fake shell readiness");
        TestSupport.True(snapshot.FailureMessage?.Contains("visual surface readiness timeout", StringComparison.OrdinalIgnoreCase) == true,
            "timeout failure detail");

        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new() { Snapshot = snapshot };
        TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "timeout overlay collapsed");
        TestSupport.False(overlay.IsHitTestVisible, "timeout overlay releases interaction");
    }

    private static void RealWpfLifecycle() => TestSupport.InTemporaryDirectory(directory =>
    {
        EnsureApplication();
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        using StartupSequenceService service = ReadyExceptSurface(new ImmediateClock(), new ManualClock());
        AppSettings settings = new() { Theme = AppThemeParser.ToStorageValue(AppTheme.Tracework) };
        CountingSettingsService settingsService = new(settings);
        TestThemeService themeService = new(AppTheme.Tracework);
        FakeMotionEnvironment motionEnvironment = new();
        using MotionService motionService = new(motionEnvironment, MotionLevel.Standard, Dispatcher.CurrentDispatcher);
        using ThemeTransitionService themeTransitions = new(themeService, motionService, Dispatcher.CurrentDispatcher);
        using NavigationTransitionService navigationTransitions = new(new ImmediateNavigationClock());
        using PollingService polling = new(new CountingSensorService(), settings);
        using SensorHistoryService history = new(polling);
        using CsvGameSessionRecorder recorder = new(Path.Combine(directory, "sessions"), 8);
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
            recorder,
            startupSequenceService: service);
        MainShellHost shell = new() { DataContext = viewModel };
        Window host = new()
        {
            Content = shell,
            Width = 1120,
            Height = 720,
            Left = -32000,
            Top = -32000,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        List<StartupSequencePhase> phases = [];
        service.SnapshotChanged += (_, args) => phases.Add(args.CurrentSnapshot.Phase);
        try
        {
            host.Show();
            Task sequence = service.StartAsync();
            for (int pass = 0; pass < 8 && !sequence.IsCompleted; pass++)
            {
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            sequence.GetAwaiter().GetResult();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            host.UpdateLayout();

            TestSupport.True(service.CurrentSnapshot.ShellReady && service.CurrentSnapshot.VisualReady, "Loaded surface readiness");
            TestSupport.True(service.CurrentSnapshot.HasCompleted, "real WPF lifecycle complete");
            foreach (StartupSequencePhase phase in Enum.GetValues<StartupSequencePhase>().Where(phase => phase != StartupSequencePhase.Dormant))
            {
                TestSupport.True(phases.Contains(phase), $"real WPF phase {phase}");
            }
            TraceworkStartupSequenceOverlay overlay = TestSupport.NotNull(
                shell.FindName("StartupSequenceOverlay") as TraceworkStartupSequenceOverlay,
                "startup overlay");
            TestSupport.Equal(StartupSequencePhase.Complete, viewModel.StartupSequence.Phase, "view model completion");
            TestSupport.Equal(StartupSequencePhase.Complete, TestSupport.NotNull(overlay.Snapshot, "overlay snapshot").Phase, "overlay completion");
            TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "completed overlay collapsed");
            TestSupport.False(overlay.IsHitTestVisible, "completed overlay releases shell");
            FrameworkElement pageHost = TestSupport.NotNull(shell.FindName("PageHost") as FrameworkElement, "page host");
            TestSupport.True(pageHost.IsHitTestVisible, "shell remains operational");
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    });

    private static void VerifyLayout(Size size)
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new()
        {
            Snapshot = StartupSequenceSnapshot.Dormant(AppTheme.Tracework, MotionLevel.Standard) with
            {
                Version = 1,
                Phase = StartupSequencePhase.Index,
                IsActive = true,
                VisualReady = true
            }
        };
        WithHostedOverlay(overlay, size, () =>
        {
            ItemsControl matrix = TestSupport.NotNull(overlay.FindName("RouteMatrixItems") as ItemsControl, "route matrix");
            TestSupport.Equal(6, matrix.Items.Count, "six milestone rows");
            TestSupport.True(overlay.DesiredSize.Width <= size.Width + 0.5, "no horizontal overflow");
            foreach (object item in matrix.Items)
            {
                FrameworkElement row = TestSupport.NotNull(
                    matrix.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement,
                    "milestone row");
                Border node = TestSupport.NotNull(
                    Descendants<Border>(row).FirstOrDefault(border => Math.Abs(border.Width - 4d) < 0.1 && Math.Abs(border.Height - 4d) < 0.1),
                    "rectangular milestone node");
                TextBlock name = TestSupport.NotNull(
                    Descendants<TextBlock>(row).FirstOrDefault(text => text.Text == ((StartupMilestoneSnapshot)item).Name),
                    "milestone name");
                double nodeCenter = node.TransformToAncestor(overlay).Transform(new Point(0, node.ActualHeight / 2d)).Y;
                double textCenter = name.TransformToAncestor(overlay).Transform(new Point(0, name.ActualHeight / 2d)).Y;
                TestSupport.True(Math.Abs(nodeCenter - textCenter) <= 1d, "node and text row centers align");
            }

            FrameworkElement rail = TestSupport.NotNull(overlay.FindName("StartupBottomRailLayer") as FrameworkElement, "bottom rail");
            double railBottom = rail.TransformToAncestor(overlay).Transform(new Point(0, rail.ActualHeight)).Y;
            TestSupport.True(Math.Abs(railBottom - (overlay.ActualHeight - 24d)) <= 1d, "rail remains at bottom");

            FrameworkElement commit = TestSupport.NotNull(overlay.FindName("CommitGroup") as FrameworkElement, "commit group");
            overlay.Snapshot = overlay.Snapshot! with { Version = 2, Phase = StartupSequencePhase.Dormant, IsActive = false, CanCommit = false };
            TestSupport.Equal(Visibility.Collapsed, commit.Visibility, "Dormant hides COMMIT");
            overlay.Snapshot = overlay.Snapshot! with { Version = 3, Phase = StartupSequencePhase.Lock, IsActive = true, CanCommit = true };
            TestSupport.Equal(Visibility.Visible, commit.Visibility, "Lock and CanCommit show COMMIT");
        });
    }

    private static StartupSequenceService ReadyExceptSurface(IStartupSequenceClock clock, IStartupSequenceClock readinessClock)
    {
        StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Standard, clock, readinessClock);
        foreach (StartupMilestoneId id in Enum.GetValues<StartupMilestoneId>().Where(id => id != StartupMilestoneId.ShellSurface))
        {
            service.ReportMilestone(id, StartupMilestoneState.Ready, "ready");
        }
        service.ReportInitialProjection(new StartupInitialProjectionSnapshot(
            1,
            Enum.GetValues<HardwareOverviewKind>()
                .Select(kind => new StartupProjectionSlotSnapshot(kind, StartupProjectionState.Value, "value"))
                .ToArray(),
            DispatcherApplied: true,
            PostDataLayoutObserved: false));
        service.ReportPostDataLayout(1);
        return service;
    }

    private static void WithHostedOverlay(TraceworkStartupSequenceOverlay overlay, Size size, Action assertion)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Window host = new()
        {
            Content = overlay,
            Width = size.Width,
            Height = size.Height,
            Left = -32000,
            Top = -32000,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            host.ApplyTemplate();
            overlay.ApplyTemplate();
            host.Measure(size);
            host.Arrange(new Rect(new Point(), size));
            host.UpdateLayout();
            assertion();
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

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
            HardwareVision.App app = new();
            app.InitializeComponent();
        }
    }

    private sealed class ImmediateClock : IStartupSequenceClock
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class ManualClock : IStartupSequenceClock
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => completion.Task.WaitAsync(cancellationToken);
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
        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot { Timestamp = DateTimeOffset.Now });
        public Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>([]);
        public Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("test", "test", null, null, null, null));
    }
}
