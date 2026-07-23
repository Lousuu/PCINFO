using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class StartupReleaseVisualGateTests
{
    private static readonly (double Width, double Height)[] ResponsiveSizes =
    [
        (920d, 620d),
        (1107d, 685d),
        (1120d, 720d),
        (1600d, 900d)
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"Native first-frame gate {iteration:00}/20", VerifyNativeFirstFrameGate));
            tests.Add(($"First-frame fail-open {iteration:00}/20", VerifyFirstFrameFailOpen));
            tests.Add(($"Commit monotonic authorization {iteration:00}/20", VerifyCommitMonotonicAuthorization));
            tests.Add(($"Commit visual latch {iteration:00}/20", VerifyCommitVisualLatch));
            tests.Add(($"Commit exit no-relight {iteration:00}/20", VerifyCommitExitNoRelight));
            tests.Add(($"Reveal atomic bottom rail {iteration:00}/20", VerifyRevealAtomicBottomRail));
            tests.Add(($"Reveal/Shell overlap timing {iteration:00}/20", VerifyRevealShellOverlapTiming));
            tests.Add(($"Reveal late-snapshot irreversibility {iteration:00}/20", VerifyRevealLateSnapshotIrreversibility));
            tests.Add(($"Stable Index Clip {iteration:00}/20", VerifyStableIndexClip));
            tests.Add(($"Startup clock cleanup {iteration:00}/20", VerifyStartupClockCleanup));
        }
        return tests;
    }

    private static void VerifyNativeFirstFrameGate()
    {
        EnsureApplication();
        Color expected = Color.FromRgb(0x0B, 0x0E, 0x11);
        foreach ((double width, double height) in ResponsiveSizes)
        {
            Window window = new()
            {
                Width = width,
                Height = height,
                Background = new SolidColorBrush(expected),
                Opacity = 0d,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            try
            {
                nint nativeHandle = new WindowInteropHelper(window).EnsureHandle();
                HwndSource source = TestSupport.NotNull(
                    HwndSource.FromHwnd(nativeHandle),
                    "native source");
                source.CompositionTarget.BackgroundColor = expected;
                TestSupport.Equal(expected, source.CompositionTarget.BackgroundColor, "native background");
                SolidColorBrush brush = TestSupport.NotNull(
                    window.Background as SolidColorBrush,
                    "window background");
                TestSupport.Equal(expected, brush.Color, "WPF background");
                TestSupport.Equal(0d, window.Opacity, "gate starts hidden");
            }
            finally
            {
                window.Close();
            }
        }

        string code = Read("HardwareVision", "MainWindow.xaml.cs");
        int shell = code.IndexOf("MainShell.PrepareFirstFrame();", StringComparison.Ordinal);
        int handle = code.IndexOf("EnsureHandle();", StringComparison.Ordinal);
        TestSupport.True(shell >= 0 && handle > shell, "shell prepares before HWND");
        TestSupport.True(code.Contains("CompositionTarget.BackgroundColor = FirstFrameColor", StringComparison.Ordinal), "native color commit");
        TestSupport.True(code.Contains("DwmUseImmersiveDarkMode = 20", StringComparison.Ordinal), "DWM attribute 20");
        TestSupport.True(code.Contains("DwmUseImmersiveDarkModeLegacy = 19", StringComparison.Ordinal), "DWM fallback 19");
    }

    private static void VerifyFirstFrameFailOpen()
    {
        int state = 0;
        TestSupport.True(MainWindow.TryArmFirstFrameGate(ref state), "first arm");
        TestSupport.False(MainWindow.TryArmFirstFrameGate(ref state), "duplicate arm rejected");
        TestSupport.True(MainWindow.TryReleaseFirstFrameGateState(ref state), "first release");
        TestSupport.False(MainWindow.TryReleaseFirstFrameGateState(ref state), "duplicate release rejected");
        TestSupport.Equal(TimeSpan.FromMilliseconds(500), MainWindow.FirstFrameFailOpenTimeout, "bounded timeout");

        string code = Read("HardwareVision", "MainWindow.xaml.cs");
        TestSupport.True(code.Contains("Task.Delay(FirstFrameFailOpenDelay)", StringComparison.Ordinal), "one-shot delay");
        TestSupport.True(code.Contains("generation == Volatile.Read(ref firstFrameGateGeneration)", StringComparison.Ordinal), "generation guard");
        TestSupport.False(code.Contains("DispatcherTimer", StringComparison.Ordinal), "no dispatcher timer");
        TestSupport.False(code.Contains("CompositionTarget.Rendering", StringComparison.Ordinal), "no rendering subscription");
        int trayStart = code.IndexOf("public void ShowFromTray()", StringComparison.Ordinal);
        int trayEnd = code.IndexOf("public void PrepareFirstFrame()", StringComparison.Ordinal);
        string tray = code[trayStart..trayEnd];
        TestSupport.False(tray.Contains("PrepareFirstFrame", StringComparison.Ordinal), "tray restore does not re-arm");
    }

    private static void VerifyCommitMonotonicAuthorization()
    {
        using StartupSequenceService service = ReadyService();
        MoveToPhase(service, StartupSequencePhase.Bind);
        TestSupport.True(service.CurrentSnapshot.CanCommit, "Bind readiness visible");

        service.ReportInitialProjection(PendingProjection(2));
        TestSupport.False(service.CurrentSnapshot.CanCommit, "Bind does not latch transient readiness");
        service.ReportInitialProjection(ResolvedProjection(2));
        service.ReportPostDataLayout(2);
        TestSupport.True(service.CurrentSnapshot.CanCommit, "Bind readiness restored");

        MoveToPhase(service, StartupSequencePhase.Lock);
        service.ReportInitialProjection(PendingProjection(3));
        TestSupport.True(service.CurrentSnapshot.CanCommit, "Lock authorization remains monotonic");
        TestSupport.Equal(3L, service.CurrentSnapshot.InitialProjection.PollingVersion, "new projection retained");
    }

    private static void VerifyCommitVisualLatch() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            FrameworkElement group = Element<FrameworkElement>(overlay, "CommitGroup");
            FrameworkElement commitLock = Element<FrameworkElement>(overlay, "CommitLock");
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: true);
            TestSupport.Equal(Visibility.Visible, group.Visibility, "COMMIT established");
            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: false);
            TestSupport.Equal(Visibility.Visible, group.Visibility, "later snapshot cannot collapse group");
            TestSupport.Equal(0.70d, (double)group.GetAnimationBaseValue(UIElement.OpacityProperty), "group stable base");
            TestSupport.Equal(0.70d, (double)commitLock.GetAnimationBaseValue(UIElement.OpacityProperty), "lock stable base");
        });

    private static void VerifyCommitExitNoRelight() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            FrameworkElement group = Element<FrameworkElement>(overlay, "CommitGroup");
            FrameworkElement commitLock = Element<FrameworkElement>(overlay, "CommitLock");
            FrameworkElement text = Element<FrameworkElement>(overlay, "CommitText");
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: true);
            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Reveal, MotionLevel.Full, canCommit: true);
            PumpUntil(() => overlay.Visibility == Visibility.Collapsed, TimeSpan.FromMilliseconds(500));
            TestSupport.Equal(Visibility.Collapsed, group.Visibility, "group remains collapsed");
            TestSupport.Equal(0d, group.Opacity, "group final opacity");
            TestSupport.Equal(0d, commitLock.Opacity, "lock prepared opacity");
            TestSupport.Equal(1d, text.Opacity, "text prepared base");
            TestSupport.False(group.HasAnimatedProperties, "group exit clock cleared");
            TestSupport.False(commitLock.HasAnimatedProperties, "lock exit clock cleared");
            TestSupport.False(text.HasAnimatedProperties, "text exit clock cleared");
        });

    private static void VerifyRevealAtomicBottomRail() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Reveal, MotionLevel.Full);
            TextBlock currentText = Element<TextBlock>(overlay, "BottomCurrentPhaseText");
            TextBlock currentCode = Element<TextBlock>(overlay, "BottomCurrentPhaseCode");
            TextBlock previousText = Element<TextBlock>(overlay, "BottomPreviousPhaseText");
            TextBlock previousCode = Element<TextBlock>(overlay, "BottomPreviousPhaseCode");
            TestSupport.True(overlay.IsRevealVisualStateEntered, "irreversible state entered immediately");
            TestSupport.True(currentText.Text.StartsWith("05 / 05", StringComparison.Ordinal), "Reveal step committed");
            TestSupport.Equal("REVEAL", currentCode.Text, "Reveal code committed");
            TestSupport.Equal(1d, currentText.Opacity, "Reveal text visible");
            TestSupport.Equal(1d, currentCode.Opacity, "Reveal code visible");
            TestSupport.Equal(string.Empty, previousText.Text, "previous text cleared");
            TestSupport.Equal(string.Empty, previousCode.Text, "previous code cleared");
            TestSupport.Equal(TimeSpan.FromMilliseconds(100), TraceworkStartupSequenceOverlay.ResolveRevealHoldDuration(MotionLevel.Full), "Full hold");
            TestSupport.Equal(TimeSpan.FromMilliseconds(80), TraceworkStartupSequenceOverlay.ResolveRevealHoldDuration(MotionLevel.Standard), "Standard hold");
            TestSupport.Equal(TimeSpan.FromMilliseconds(40), TraceworkStartupSequenceOverlay.ResolveRevealHoldDuration(MotionLevel.Reduced), "Reduced hold");
        });

    private static void VerifyRevealShellOverlapTiming()
    {
        (TimeSpan Delay, TimeSpan Duration)[] full =
        [
            (TimeSpan.Zero, TimeSpan.FromMilliseconds(90)),
            (TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(90)),
            (TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(120)),
            (TimeSpan.FromMilliseconds(70), TimeSpan.FromMilliseconds(90))
        ];
        (TimeSpan Delay, TimeSpan Duration)[] standard =
        [
            (TimeSpan.Zero, TimeSpan.FromMilliseconds(70)),
            (TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(70)),
            (TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(100)),
            (TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(70))
        ];
        for (int index = 0; index < 4; index++)
        {
            TestSupport.Equal(full[index], StartupShellRevealCoordinator.ResolveTraceworkTiming(MotionLevel.Full, index), $"Full target {index}");
            TestSupport.Equal(standard[index], StartupShellRevealCoordinator.ResolveTraceworkTiming(MotionLevel.Standard, index), $"Standard target {index}");
        }
        TestSupport.Equal(TimeSpan.FromMilliseconds(160), full.Max(item => item.Delay + item.Duration), "Full latest completion");
        TestSupport.Equal(TimeSpan.FromMilliseconds(130), standard.Max(item => item.Delay + item.Duration), "Standard latest completion");

        EnsureApplication();
        Border[] targets = [new(), new(), new(), new()];
        Grid grid = new();
        foreach (Border target in targets)
        {
            grid.Children.Add(target);
        }
        Window host = CreateHost(grid, 1120d, 720d);
        try
        {
            StartupShellRevealCoordinator coordinator = new(targets);
            coordinator.Apply(Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full));
            coordinator.Apply(Snapshot(2, StartupSequencePhase.Reveal, MotionLevel.Full));
            TestSupport.True(targets.All(target => target.HasAnimatedProperties), "Shell starts on Reveal snapshot");
            PumpUntil(
                () => targets.All(target => Math.Abs(target.Opacity - 1d) < 0.001d),
                TimeSpan.FromMilliseconds(250));
            TestSupport.True(targets.All(target => Math.Abs(target.Opacity - 1d) < 0.001d), "Shell stable by 160 ms");
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static void VerifyRevealLateSnapshotIrreversibility() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Reveal, MotionLevel.Full);
            FrameworkElement background = Element<FrameworkElement>(overlay, "StartupBackgroundLayer");
            TextBlock code = Element<TextBlock>(overlay, "BottomCurrentPhaseCode");
            overlay.Snapshot = Snapshot(3, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: true);
            TestSupport.True(overlay.IsRevealVisualStateEntered, "Reveal boundary preserved");
            TestSupport.Equal("REVEAL", code.Text, "late snapshot cannot replace rail");
            PumpUntil(() => overlay.Visibility == Visibility.Collapsed, TimeSpan.FromMilliseconds(350));
            TestSupport.Equal(0d, background.Opacity, "late snapshot cannot restore background");
            TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "overlay remains exited");
        });

    private static void VerifyStableIndexClip()
    {
        foreach ((double width, double height) in ResponsiveSizes)
        {
            WithOverlay(width, height, overlay =>
            {
                overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full);
                TextBlock text = Element<TextBlock>(overlay, "SystemIndexText");
                FrameworkElement host = Element<FrameworkElement>(overlay, "SystemIndexClipHost");
                PumpUntil(
                    () => host.Clip is RectangleGeometry,
                    TimeSpan.FromMilliseconds(1000));
                RectangleGeometry clip = TestSupport.NotNull(host.Clip as RectangleGeometry, "stable index clip");
                PumpUntil(
                    () => !clip.HasAnimatedProperties,
                    TimeSpan.FromMilliseconds(1000));
                TestSupport.True(text.IsMeasureValid && text.IsArrangeValid, "text layout valid");
                TestSupport.True(Math.Abs(text.ActualWidth - text.DesiredSize.Width) <= 1d, "stable text width");
                TestSupport.Nearly(text.ActualWidth, clip.Rect.Width, "clip final width", 0.01d);
                TestSupport.False(clip.HasAnimatedProperties, "clip clock cleared");
                TestSupport.False(overlay.IsIndexRevealRetryScheduled, "no retry remains");
            });
        }

        TraceworkStartupSequenceOverlay unarranged = new();
        unarranged.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full);
        TestSupport.True(unarranged.IsIndexRevealRetryScheduled, "invalid layout queues one retry");
        Pump(TimeSpan.FromMilliseconds(20));
        TestSupport.False(unarranged.IsIndexRevealRetryScheduled, "single retry completes fail-open");
        TestSupport.True(Element<FrameworkElement>(unarranged, "SystemIndexClipHost").Clip is null, "invalid layout restores final state");
    }

    private static void VerifyStartupClockCleanup() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full);
            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: true);
            overlay.Snapshot = Snapshot(3, StartupSequencePhase.Reveal, MotionLevel.Full, canCommit: true);
            PumpUntil(() => overlay.Visibility == Visibility.Collapsed, TimeSpan.FromMilliseconds(500));
            foreach (string name in new[]
                     {
                         "StartupBackgroundLayer",
                         "StartupContentLayer",
                         "StartupBottomRailLayer",
                         "CommitGroup",
                         "CommitLock",
                         "CommitText",
                         "RevealPresentationHold"
                     })
            {
                TestSupport.False(Element<FrameworkElement>(overlay, name).HasAnimatedProperties, $"{name} clocks");
            }
            TestSupport.True(Element<FrameworkElement>(overlay, "StartupContentLayer").Clip is null, "content clip cleared");
            TestSupport.True(Element<FrameworkElement>(overlay, "CommitCenterClipHost").Clip is null, "commit clip cleared");
            TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "overlay collapsed");
            TestSupport.False(overlay.IsHitTestVisible, "overlay interaction disabled");
        });

    private static StartupSequenceService ReadyService()
    {
        StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Full);
        service.ReportMilestone(StartupMilestoneId.ThemeResources, StartupMilestoneState.Ready);
        service.ReportMilestone(StartupMilestoneId.ServiceGraph, StartupMilestoneState.Ready);
        service.ReportMilestone(StartupMilestoneId.PageRouter, StartupMilestoneState.Ready);
        service.ReportMilestone(StartupMilestoneId.HistoryBuffer, StartupMilestoneState.Ready);
        service.ReportMilestone(StartupMilestoneId.SensorBus, StartupMilestoneState.Ready);
        service.ReportSurfaceReady(1120d, 720d, "test surface");
        service.ReportInitialProjection(ResolvedProjection(1));
        service.ReportPostDataLayout(1);
        return service;
    }

    private static void MoveToPhase(StartupSequenceService service, StartupSequencePhase phase)
    {
        MethodInfo method = TestSupport.NotNull(
            typeof(StartupSequenceService).GetMethod(
                "CreateSnapshotLocked",
                BindingFlags.Instance | BindingFlags.NonPublic),
            "snapshot method");
        _ = method.Invoke(service, [phase, true, false, string.Empty, null]);
    }

    private static StartupInitialProjectionSnapshot ResolvedProjection(long version) => new(
        version,
        StartupInitialProjectionSnapshot.Pending.Slots
            .Select(slot => slot with { State = StartupProjectionState.Value, Detail = "value" })
            .ToArray(),
        DispatcherApplied: true,
        PostDataLayoutObserved: false);

    private static StartupInitialProjectionSnapshot PendingProjection(long version) => new(
        version,
        StartupInitialProjectionSnapshot.Pending.Slots,
        DispatcherApplied: false,
        PostDataLayoutObserved: false);

    private static StartupSequenceSnapshot Snapshot(
        long version,
        StartupSequencePhase phase,
        MotionLevel level,
        bool canCommit = false) =>
        StartupSequenceSnapshot.Dormant(AppTheme.Tracework, level) with
        {
            Version = version,
            Phase = phase,
            IsActive = true,
            HasCompleted = false,
            ShellReady = true,
            VisualReady = true,
            CanCommit = canCommit,
            InitialProjection = ResolvedProjection(version) with { PostDataLayoutObserved = true },
            Milestones = Enum.GetValues<StartupMilestoneId>()
                .Select(id => new StartupMilestoneSnapshot(
                    id,
                    StartupMilestoneSnapshot.GetName(id),
                    StartupMilestoneState.Ready,
                    StartupMilestoneSnapshot.GetStatusText(StartupMilestoneState.Ready),
                    "ready"))
                .ToArray()
        };

    private static void WithOverlay(
        double width,
        double height,
        Action<TraceworkStartupSequenceOverlay> assertion)
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new();
        Window host = CreateHost(overlay, width, height);
        try
        {
            assertion(overlay);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static Window CreateHost(object content, double width, double height)
    {
        Window host = new()
        {
            Content = content,
            Width = width,
            Height = height,
            Left = -32000,
            Top = -32000,
            Opacity = 0d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        host.Show();
        host.UpdateLayout();
        return host;
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

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(5));
        }
        TestSupport.True(condition(), "condition reached before timeout");
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

    private static T Element<T>(FrameworkElement owner, string name) where T : class =>
        TestSupport.NotNull(owner.FindName(name) as T, name);

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
}
