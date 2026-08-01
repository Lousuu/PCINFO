using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class MotionSpecAcceptanceTests
{
    private static readonly string WindowSource =
        FlowRelayVisualSource.Read("HardwareVision", "MainWindow.xaml.cs");
    private static readonly string WindowXaml =
        FlowRelayVisualSource.Read("HardwareVision", "MainWindow.xaml");
    private static readonly string HostSource =
        FlowRelayVisualSource.Read("HardwareVision", "Controls", "MotionTransitionHost.cs");
    private static readonly string OverlaySource =
        FlowRelayVisualSource.Read(
            "HardwareVision",
            "Views",
            "Shell",
            "TraceworkStartupSequenceOverlay.xaml.cs");
    private static readonly string ServiceSource =
        FlowRelayVisualSource.Read(
            "HardwareVision",
            "Services",
            "StartupSequenceService.cs");

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        (string Name, Action Test)[] groups =
        [
            ("three-render first-frame gate", ThreeRenderGate),
            ("first visible dark invariant", FirstVisibleDarkInvariant),
            ("measured and gate separation", MeasuredGateSeparation),
            ("pending Index replay", PendingIndexReplay),
            ("visible SYSBOOT intermediate clip", VisibleSysbootIntermediate),
            ("gate before Index order", GateBeforeIndexOrder),
            ("Full Dashboard handoff", FullDashboardHandoff),
            ("startup Standard Reduced Off", StartupMotionLevels),
            ("deferred cleanup", DeferredCleanup),
            ("Full old-page exit", FullOldPageExit),
            ("Relay continuity no flash", RelayContinuity),
            ("Full new-page enter", FullNewPageEnter),
            ("Standard exit and enter", StandardExitEnter),
            ("Reduced and Off navigation", ReducedOffNavigation),
            ("direction and opposite exit", DirectionAndOppositeExit),
            ("twelve-page role map and cache", TwelvePageRoleMap),
            ("cancellation latest lifecycle", CancellationLatestLifecycle),
            ("no full-page clip and performance guards", NoFullPageClipAndPerformance)
        ];

        return groups
            .Select((group, index) =>
                ($"Motion Static contract guard {index + 1:00} {group.Name}", group.Test))
            .ToArray();
    }

    private static void ThreeRenderGate()
    {
        int render1 = WindowSource.IndexOf(
            "CommitFirstOffscreenRenderedFrame(generation)",
            StringComparison.Ordinal);
        int render2 = WindowSource.IndexOf(
            "ApplyFinalFirstFramePlacement(generation)",
            StringComparison.Ordinal);
        int render3 = WindowSource.IndexOf(
            "CommitFinalPositionRenderedFrame(generation)",
            StringComparison.Ordinal);
        int release = WindowSource.IndexOf(
            "\"CompositorReady\"",
            render3,
            StringComparison.Ordinal);
        TestSupport.True(
            render1 >= 0 && render2 > render1 && render3 > render2 && release > render3,
            "three ordered Render callbacks before release");
        TestSupport.False(
            WindowSource.Contains("Dispatcher.Invoke(() => { }, DispatcherPriority.Render)", StringComparison.Ordinal),
            "no synchronous Render barrier");
    }

    private static void FirstVisibleDarkInvariant()
    {
        Contains(WindowXaml, "Background=\"#0B0E11\"", "x:Name=\"MainWindowRoot\"");
        Contains(WindowSource, "FirstFrameColor = Color.FromRgb(0x0B, 0x0E, 0x11)", "Opacity = 0d");
        int finalFlush = WindowSource.IndexOf("FinalPositionDwmFlushResult", StringComparison.Ordinal);
        int visible = WindowSource.IndexOf(
            "Opacity = 1d;",
            WindowSource.IndexOf("private void CompleteFirstFrameRelease", StringComparison.Ordinal),
            StringComparison.Ordinal);
        TestSupport.True(finalFlush >= 0 && visible > finalFlush, "visibility follows final-position flush");
    }

    private static void MeasuredGateSeparation()
    {
        using StartupSequenceService service =
            new(AppTheme.Tracework, MotionLevel.Standard);
        TestSupport.True(service.ReportSurfaceReady(1120d, 720d, "measured"), "surface");
        TestSupport.True(service.CurrentSnapshot.SurfaceMeasured, "measured flag");
        TestSupport.False(service.CurrentSnapshot.FirstFrameGateReleased, "gate still closed");
        TestSupport.False(service.CurrentSnapshot.VisualReady, "not visually ready");
        TestSupport.True(service.ReportFirstFrameGateReleased("CompositorReady"), "release");
        TestSupport.True(service.CurrentSnapshot.VisualReady, "both conditions ready");
    }

    private static void PendingIndexReplay()
    {
        Contains(
            OverlaySource,
            "pendingIndexSnapshot = snapshot",
            "SchedulePendingIndexReplay()",
            "DispatcherPriority.Render",
            "indexPlayed = true");
        int deferred = OverlaySource.IndexOf("pendingIndexSnapshot = snapshot", StringComparison.Ordinal);
        int played = OverlaySource.IndexOf("indexPlayed = true", deferred, StringComparison.Ordinal);
        TestSupport.True(played > deferred, "pending state precedes played state");
    }

    private static void VisibleSysbootIntermediate()
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new();
        Window host = CreateHost(overlay);
        try
        {
            host.Show();
            host.UpdateLayout();
            FrameworkElement clipHost =
                (FrameworkElement)overlay.FindName("SystemIndexClipHost");
            FrameworkElement indexText =
                (FrameworkElement)overlay.FindName("SystemIndexText");
            overlay.Snapshot = ReadyIndexSnapshot(1, MotionLevel.Full) with
            {
                FirstFrameGateReleased = false,
                FirstFrameGateReleaseReason = string.Empty,
                VisualReady = false
            };
            PumpUntil(
                () => host.IsLoaded
                    && indexText.IsMeasureValid
                    && indexText.IsArrangeValid
                    && indexText.ActualWidth > 1d,
                TimeSpan.FromSeconds(1),
                () => $"pre-index layout; loaded={host.IsLoaded}; width={indexText.ActualWidth:0.##}");
            bool preIndexRenderCommitted = false;
            _ = host.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() => preIndexRenderCommitted = true));
            PumpUntil(
                () => preIndexRenderCommitted,
                TimeSpan.FromSeconds(1),
                () => "pre-index Render boundary");
            overlay.Snapshot = ReadyIndexSnapshot(2, MotionLevel.Full);
            double finalWidth = 0d;
            bool observedIntermediate = false;
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
            while (!observedIntermediate && DateTime.UtcNow < deadline)
            {
                Pump(TimeSpan.FromMilliseconds(5));
                finalWidth = indexText.ActualWidth;
                if (clipHost.Clip is RectangleGeometry clip
                    && finalWidth > 1d
                    && clip.Rect.Width > 0d
                    && clip.Rect.Width < finalWidth)
                {
                    observedIntermediate = true;
                }
            }
            TestSupport.True(observedIntermediate,
                $"real WPF intermediate; pending={overlay.IsIndexPendingForFirstFrameGate}; "
                + $"played={overlay.IsIndexPlayed}; final={finalWidth:0.##}");
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static void GateBeforeIndexOrder()
    {
        int wait = ServiceSource.IndexOf("WaitForVisualReadyAsync", StringComparison.Ordinal);
        int publish = ServiceSource.IndexOf(
            "PublishPhase(StartupSequencePhase.Index",
            wait,
            StringComparison.Ordinal);
        TestSupport.True(wait >= 0 && publish > wait, "Index follows readiness wait");
        Contains(ServiceSource, "surfaceMeasured && firstFrameGateReleased");
    }

    private static void FullDashboardHandoff()
    {
        TestSupport.Equal(
            (TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(220)),
            StartupShellRevealCoordinator.ResolveTraceworkTiming(MotionLevel.Full, 0),
            "SignalRail");
        TestSupport.Equal(
            (TimeSpan.FromMilliseconds(140), TimeSpan.FromMilliseconds(190)),
            StartupShellRevealCoordinator.ResolveTraceworkTiming(MotionLevel.Full, 1),
            "Telemetry");
        TestSupport.Equal(
            (TimeSpan.FromMilliseconds(190), TimeSpan.FromMilliseconds(185)),
            StartupShellRevealCoordinator.ResolveTraceworkTiming(MotionLevel.Full, 3),
            "Time ribbon");
        TestSupport.Equal(
            TimeSpan.FromMilliseconds(180),
            TraceworkStartupSequenceOverlay.ResolveRevealExitDuration(MotionLevel.Full),
            "overlay fade");
    }

    private static void StartupMotionLevels()
    {
        TestSupport.Equal(TimeSpan.FromMilliseconds(150),
            TraceworkStartupSequenceOverlay.ResolveRevealExitDuration(MotionLevel.Standard),
            "Standard fade");
        TestSupport.Equal(TimeSpan.FromMilliseconds(100),
            TraceworkStartupSequenceOverlay.ResolveRevealExitDuration(MotionLevel.Reduced),
            "Reduced fade");
        TestSupport.Equal(TimeSpan.Zero,
            TraceworkStartupSequenceOverlay.ResolveRevealExitDuration(MotionLevel.Off),
            "Off immediate");
        TestSupport.False(
            FlowRelayVisualSource.Read(
                "HardwareVision",
                "Controls",
                "StartupShellRevealCoordinator.cs")
                .Contains("AnimateClip", StringComparison.Ordinal),
            "no startup shell spatial clip");
    }

    private static void DeferredCleanup()
    {
        int collapse = OverlaySource.IndexOf("Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        int idle = OverlaySource.IndexOf("DispatcherPriority.ContextIdle", collapse, StringComparison.Ordinal);
        int cleanup = OverlaySource.IndexOf("ClearChoreographyClocks();", idle, StringComparison.Ordinal);
        TestSupport.True(collapse >= 0 && idle > collapse && cleanup > idle,
            "collapse precedes deferred cleanup");
    }

    private static void FullOldPageExit()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(TimeSpan.FromMilliseconds(100), plan.PageExitDuration, "root exit");
        TestSupport.Equal(TimeSpan.FromMilliseconds(70), plan.SecondaryExitDuration, "secondary exits first");
        TestSupport.Equal(TimeSpan.FromMilliseconds(12), plan.PrimaryExitDelay, "primary delay");
        TestSupport.Equal(0.74d, plan.PageExitOpacity, "root overlap opacity");
    }

    private static void RelayContinuity()
    {
        Contains(
            HostSource,
            "ExplicitRelayCommit",
            "ApplyCommittedBaseState()",
            "PrepareCommittedContent(");
        int explicitPath = HostSource.IndexOf("if (explicitNavigationVersion >= 0)", StringComparison.Ordinal);
        int cancel = HostSource.IndexOf("CancelTransition();", explicitPath, StringComparison.Ordinal);
        int returnIndex = HostSource.IndexOf("return;", explicitPath, StringComparison.Ordinal);
        TestSupport.True(returnIndex >= 0 && (cancel < 0 || returnIndex < cancel),
            "explicit Content change bypasses legacy cancellation");
        int resolve = HostSource.IndexOf("ResolveRoleCache();", explicitPath, StringComparison.Ordinal);
        int committedBase = HostSource.IndexOf("ApplyCommittedBaseState();", explicitPath, StringComparison.Ordinal);
        TestSupport.True(resolve > explicitPath && committedBase > resolve,
            "new-page roles resolve before commit opacity is applied");
    }

    private static void FullNewPageEnter()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(TimeSpan.FromMilliseconds(120), plan.PageEnterDuration, "root");
        TestSupport.Equal(TimeSpan.FromMilliseconds(10), plan.PrimaryEnterDelay, "primary delay");
        TestSupport.Equal(TimeSpan.FromMilliseconds(100), plan.PrimaryEnterDuration, "primary duration");
        TestSupport.Equal(TimeSpan.FromMilliseconds(34), plan.SecondaryEnterDelay, "secondary delay");
        TestSupport.Equal(TimeSpan.FromMilliseconds(106), plan.SecondaryEnterDuration, "secondary duration");
    }

    private static void StandardExitEnter()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Standard);
        TestSupport.Equal(TimeSpan.FromMilliseconds(35), plan.ExitDuration, "exit");
        TestSupport.Equal(TimeSpan.FromMilliseconds(45), plan.CommitTime, "commit");
        TestSupport.Equal(TimeSpan.FromMilliseconds(95), plan.EnterDuration, "enter");
        TestSupport.Equal(TimeSpan.FromMilliseconds(150), plan.TotalDuration, "total");
    }

    private static void ReducedOffNavigation()
    {
        NavigationTransitionPlan reduced = Plan(MotionLevel.Reduced);
        TestSupport.True(reduced.TotalDuration <= TimeSpan.FromMilliseconds(160), "Reduced total");
        TestSupport.False(reduced.AllowsPageTranslation, "Reduced translation");
        TestSupport.False(reduced.AllowsRoleStagger, "Reduced stagger");
        NavigationTransitionPlan off = Plan(MotionLevel.Off);
        TestSupport.False(off.UsesClock, "Off clockless");
        TestSupport.Equal(TimeSpan.Zero, off.TotalDuration, "Off immediate");
    }

    private static void DirectionAndOppositeExit()
    {
        TestSupport.Equal(
            NavigationTransitionDirection.FromBottom,
            NavigationRouteDescriptor.ResolveDirection(Route("Cpu"), Route("Gpu")),
            "same group forward");
        TestSupport.Equal(
            NavigationTransitionDirection.FromLeft,
            NavigationRouteDescriptor.ResolveDirection(Route("GameSessionReport"), Route("GamePerformance")),
            "report reverse");
        Contains(HostSource, "double exitOffset = -enterOffset");
    }

    private static void TwelvePageRoleMap()
    {
        string[] files =
        [
            "Dashboard/TraceworkDashboardLayout.xaml",
            "Cpu/TraceworkCpuLayout.xaml",
            "Gpu/TraceworkGpuLayout.xaml",
            "Memory/TraceworkMemoryLayout.xaml",
            "Disk/TraceworkDiskLayout.xaml",
            "Network/TraceworkNetworkLayout.xaml",
            "Motherboard/TraceworkMotherboardLayout.xaml",
            "AdvancedSensors/TraceworkAdvancedSensorsLayout.xaml",
            "GamePerformance/TraceworkGamePerformanceLayout.xaml",
            "GameSessionReport/TraceworkGameSessionReportLayout.xaml",
            "Settings/TraceworkSettingsLayout.xaml",
            "MetricVisibility/TraceworkMetricVisibilityLayout.xaml"
        ];
        foreach (string file in files)
        {
            string xaml = FlowRelayVisualSource.Read(
                "HardwareVision",
                "Views",
                file.Replace('/', Path.DirectorySeparatorChar));
            TestSupport.Equal(1, Count(xaml, "NavigationMotion.Role=\"Primary\""), $"{file} Primary");
            TestSupport.True(Count(xaml, "NavigationMotion.Role=\"Secondary\"") <= 1, $"{file} Secondary");
        }
        Contains(HostSource, "cachedRoleContent", "cachedPrimary", "cachedSecondary");
    }

    private static void CancellationLatestLifecycle()
    {
        Contains(
            HostSource,
            "explicitContentCommitted",
            "CompleteNavigation(long version)",
            "version != explicitNavigationVersion",
            "DispatcherPriority.ContextIdle");
        string service = FlowRelayVisualSource.Read(
            "HardwareVision",
            "Services",
            "NavigationTransitionService.cs");
        Contains(service, "snapshot.Version < current.Version", "activeCancellation?.Cancel()");
    }

    private static void NoFullPageClipAndPerformance()
    {
        TestSupport.False(HostSource.Contains("RectAnimation", StringComparison.Ordinal), "host RectAnimation");
        TestSupport.False(HostSource.Contains("motionSurface.Clip =", StringComparison.Ordinal)
            && HostSource.Contains("new RectangleGeometry", StringComparison.Ordinal), "host dynamic clip");
        string applySnapshot = Slice(
            OverlaySource,
            "private void ApplySnapshot(",
            "private void RequestIndexReveal(");
        TestSupport.False(applySnapshot.Contains("RouteMatrixItems.UpdateLayout()", StringComparison.Ordinal),
            "snapshot layout barrier");
        TestSupport.False(OverlaySource.Contains("StartupContentLayer.Clip = clip", StringComparison.Ordinal),
            "startup content clip");
        string rail = FlowRelayVisualSource.Read(
            "HardwareVision",
            "Views",
            "Shell",
            "TraceworkSignalRail.xaml.cs");
        TestSupport.False(rail.Contains("UpdateLayout()", StringComparison.Ordinal), "signal rail layout barrier");
    }

    private static NavigationTransitionPlan Plan(MotionLevel level) =>
        NavigationTransitionPlan.Create(
            MotionProfile.Create(level, level, string.Empty));

    private static NavigationRouteDescriptor Route(string key)
    {
        TestSupport.True(
            NavigationRouteDescriptor.TryCreate(key, key, key, key, out NavigationRouteDescriptor? route),
            key);
        return TestSupport.NotNull(route, key);
    }

    private static StartupSequenceSnapshot ReadyIndexSnapshot(long version, MotionLevel level) =>
        StartupSequenceSnapshot.Dormant(AppTheme.Tracework, level) with
        {
            Version = version,
            Phase = StartupSequencePhase.Index,
            IsActive = true,
            SurfaceMeasured = true,
            FirstFrameGateReleased = true,
            FirstFrameGateReleaseReason = "CompositorReady",
            VisualReady = true
        };

    private static Window CreateHost(FrameworkElement content) => new()
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

    private static void PumpUntil(
        Func<bool> condition,
        TimeSpan timeout,
        Func<string>? failure = null)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(5));
        }
        TestSupport.True(condition(), failure?.Invoke() ?? "condition before timeout");
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

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private static void Contains(string source, params string[] tokens)
    {
        foreach (string token in tokens)
        {
            TestSupport.True(source.Contains(token, StringComparison.Ordinal), token);
        }
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }
        return count;
    }

    private static string Slice(string source, string startToken, string endToken)
    {
        int start = source.IndexOf(startToken, StringComparison.Ordinal);
        int end = source.IndexOf(endToken, start, StringComparison.Ordinal);
        return source[start..end];
    }
}
