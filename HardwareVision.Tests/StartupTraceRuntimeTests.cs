using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class StartupTraceRuntimeTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests =
        [
            ("Startup trace runtime named row contract", NamedRowContract),
            ("Startup trace runtime commit requires lock gate", CommitRequiresLockGate),
            ("Startup trace runtime commit owns center clip clock", CommitCenterClipClock),
            ("Startup trace runtime cleanup removes transforms and clips", CleanupRemovesTransientState),
            ("Startup trace runtime Off creates no clocks", OffCreatesNoClocks),
            ("Startup trace runtime Reduced animates matrix as one", ReducedAnimatesWholeMatrix),
            ("Startup trace runtime status changes animate once", StatusChangesAnimateOnce)
        ];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            int pass = iteration;
            tests.Add(($"Startup Route runtime {pass:00}/20", VerifyRouteRuntime));
            tests.Add(($"Startup Projection runtime {pass:00}/20", VerifyProjectionRuntime));
            tests.Add(($"Startup Reveal runtime {pass:00}/20", VerifyRevealRuntime));
        }

        return tests;
    }

    private static void NamedRowContract() => WithOverlay(MotionLevel.Reduced, overlay =>
    {
        StartupMilestoneRow row = Rows(overlay).First();
        foreach (string name in new[]
                 {
                     "RowRoot", "UpperRouteSegment", "LowerRouteSegment", "MilestoneNode",
                     "MilestoneName", "MilestoneStatus", "MilestoneDetail", "PendingFrame",
                     "RouteOutputPort", "RouteOutputAnchor"
                 })
        {
            TestSupport.NotNull(row.FindName(name) as FrameworkElement, name);
        }

        TestSupport.Equal(34d, row.Height, "row height");
        TestSupport.Equal(Visibility.Hidden, ((FrameworkElement)row.FindName("UpperRouteSegment")).Visibility, "first upper hidden");
        TestSupport.Equal(Visibility.Visible, ((FrameworkElement)row.FindName("LowerRouteSegment")).Visibility, "first lower visible");
        StartupMilestoneRow last = Rows(overlay).Last();
        TestSupport.Equal(Visibility.Visible, ((FrameworkElement)last.FindName("UpperRouteSegment")).Visibility, "last upper visible");
        TestSupport.Equal(Visibility.Hidden, ((FrameworkElement)last.FindName("LowerRouteSegment")).Visibility, "last lower hidden");
    });

    private static void CommitRequiresLockGate() => WithOverlay(MotionLevel.Full, overlay =>
    {
        FrameworkElement commit = (FrameworkElement)overlay.FindName("CommitGroup");
        FrameworkElement root = (FrameworkElement)overlay.FindName("CommitExitRoot");
        FrameworkElement graphic = (FrameworkElement)overlay.FindName("CommitGraphicLayer");
        FrameworkElement rail = (FrameworkElement)overlay.FindName("StartupBottomRailLayer");
        Visibility railVisibility = rail.Visibility;
        bool bottomRailReady = overlay.IsBottomRailReady;
        bool revealEntered = overlay.IsRevealVisualStateEntered;
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: false);
        TestSupport.Equal(Visibility.Collapsed, commit.Visibility, "Lock without readiness");
        overlay.Snapshot = Snapshot(3, StartupSequencePhase.Lock, MotionLevel.Full, canCommit: true);
        TestSupport.Equal(StartupSequencePhase.Lock, overlay.Snapshot!.Phase, "Lock phase applied");
        TestSupport.True(overlay.Snapshot.CanCommit, "Lock snapshot permits COMMIT");
        TestSupport.Equal(Visibility.Collapsed, commit.Visibility, "Lock with readiness defers COMMIT until Render");
        TestSupport.False(overlay.CommitVisualStartedAt.HasValue, "COMMIT has not started synchronously");

        PumpRenderTurn(overlay.Dispatcher);

        TestSupport.Equal(Visibility.Visible, commit.Visibility, "Lock with readiness shows COMMIT after Render");
        TestSupport.True(overlay.CommitVisualStartedAt.HasValue, "COMMIT starts after Render");
        DateTimeOffset? commitVisualStartedAt = overlay.CommitVisualStartedAt;
        TestSupport.True(graphic.HasAnimatedProperties, "commit graphic opacity clock");
        TestSupport.False(overlay.IsProjectionPulsePending, "COMMIT has no pending Projection blocker");
        TestSupport.False(overlay.IsProjectionPulseActive, "COMMIT has no active Projection blocker");
        TestSupport.False(
            overlay.CurrentProjectionRequestState == TraceworkStartupSequenceOverlay.ProjectionRequestState.TimedOut,
            "COMMIT starts without ProjectionPulseVisualTimeout");
        TestSupport.Equal(railVisibility, rail.Visibility, "COMMIT Render turn preserves bottom rail visibility");
        TestSupport.Equal(bottomRailReady, overlay.IsBottomRailReady, "COMMIT Render turn preserves bottom rail readiness");
        TestSupport.Equal(revealEntered, overlay.IsRevealVisualStateEntered, "COMMIT Render turn preserves Reveal state");

        PumpRenderTurn(overlay.Dispatcher);

        TestSupport.Equal(commitVisualStartedAt, overlay.CommitVisualStartedAt, "second Render does not restart COMMIT");
        overlay.Snapshot = Snapshot(4, StartupSequencePhase.Reveal, MotionLevel.Full);
        TestSupport.Equal(Visibility.Visible, commit.Visibility, "Reveal begins with commit exit");
        TestSupport.True(overlay.IsCommitRevealCompensationPending, "Reveal preserves minimum presentation");
        PumpUntil(
            () => root.HasAnimatedProperties || commit.Visibility == Visibility.Collapsed,
            TimeSpan.FromMilliseconds(800));
        TestSupport.True(root.HasAnimatedProperties || commit.Visibility == Visibility.Collapsed, "commit root exit clock");
    });

    private static void CommitCenterClipClock() => WithOverlay(MotionLevel.Standard, overlay =>
    {
        FrameworkElement commit = (FrameworkElement)overlay.FindName("CommitGroup");
        FrameworkElement rail = (FrameworkElement)overlay.FindName("StartupBottomRailLayer");
        Visibility railVisibility = rail.Visibility;
        bool bottomRailReady = overlay.IsBottomRailReady;
        bool revealEntered = overlay.IsRevealVisualStateEntered;
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Lock, MotionLevel.Standard, canCommit: true);
        TestSupport.Equal(StartupSequencePhase.Lock, overlay.Snapshot!.Phase, "center clip Lock phase applied");
        TestSupport.True(overlay.Snapshot.CanCommit, "center clip Lock snapshot permits COMMIT");
        TestSupport.Equal(Visibility.Collapsed, commit.Visibility, "center clip COMMIT defers until Render");
        TestSupport.False(overlay.CommitVisualStartedAt.HasValue, "center clip COMMIT has not started synchronously");

        PumpRenderTurn(overlay.Dispatcher);

        TestSupport.Equal(Visibility.Visible, commit.Visibility, "center clip COMMIT starts after Render");
        TestSupport.True(overlay.CommitVisualStartedAt.HasValue, "center clip COMMIT records its Render start");
        DateTimeOffset? commitVisualStartedAt = overlay.CommitVisualStartedAt;
        FrameworkElement center = (FrameworkElement)overlay.FindName("CommitCenterClipHost");
        RectangleGeometry centerClip = TestSupport.NotNull(center.Clip as RectangleGeometry, "center rectangle clip");
        TestSupport.True(centerClip.HasAnimatedProperties, "center clip clock");
        TestSupport.True(((FrameworkElement)overlay.FindName("CommitText")).HasAnimatedProperties, "delayed commit text clock");
        TestSupport.False(overlay.IsProjectionPulsePending, "center clip COMMIT has no pending Projection blocker");
        TestSupport.False(overlay.IsProjectionPulseActive, "center clip COMMIT has no active Projection blocker");
        TestSupport.False(
            overlay.CurrentProjectionRequestState == TraceworkStartupSequenceOverlay.ProjectionRequestState.TimedOut,
            "center clip COMMIT starts without ProjectionPulseVisualTimeout");
        TestSupport.Equal(railVisibility, rail.Visibility, "center clip COMMIT preserves bottom rail visibility");
        TestSupport.Equal(bottomRailReady, overlay.IsBottomRailReady, "center clip COMMIT preserves bottom rail readiness");
        TestSupport.Equal(revealEntered, overlay.IsRevealVisualStateEntered, "center clip COMMIT preserves Reveal state");

        PumpRenderTurn(overlay.Dispatcher);

        TestSupport.Equal(commitVisualStartedAt, overlay.CommitVisualStartedAt, "second Render does not restart center clip COMMIT");
        TestSupport.True(ReferenceEquals(centerClip, center.Clip), "center clip clock owner is not replaced");
        TestSupport.True(centerClip.HasAnimatedProperties, "center clip clock remains owned by COMMIT");
    });

    private static void CleanupRemovesTransientState() => WithOverlay(MotionLevel.Full, overlay =>
    {
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Reveal, MotionLevel.Full);
        overlay.RestoreFinalState();
        FrameworkElement content = (FrameworkElement)overlay.FindName("StartupContentLayer");
        TestSupport.True(content.Clip is null, "content clip cleared");
        TestSupport.Equal(0d, ((TranslateTransform)content.RenderTransform).X, "content translation cleared");
        TestSupport.False(content.HasAnimatedProperties, "content clocks cleared");
        TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "overlay collapsed");
    });

    private static void OffCreatesNoClocks()
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new()
        {
            Snapshot = Snapshot(1, StartupSequencePhase.Route, MotionLevel.Off)
        };
        TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "Off collapsed");
        TestSupport.False(overlay.HasAnimatedProperties, "Off root clocks");
        TestSupport.False(((FrameworkElement)overlay.FindName("RouteMatrixItems")).HasAnimatedProperties, "Off matrix clocks");
    }

    private static void ReducedAnimatesWholeMatrix() => WithOverlay(MotionLevel.Reduced, overlay =>
    {
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Reduced);
        Pump(TimeSpan.FromMilliseconds(20));
        FrameworkElement matrix = (FrameworkElement)overlay.FindName("RouteMatrixItems");
        TestSupport.True(matrix.HasAnimatedProperties, "one matrix opacity clock");
        TestSupport.False(Rows(overlay)[0].FindName("MilestoneName") is FrameworkElement { HasAnimatedProperties: true }, "no per-field reduced clock");
    });

    private static void StatusChangesAnimateOnce() => WithOverlay(MotionLevel.Standard, overlay =>
    {
        overlay.Snapshot = Snapshot(
            2,
            StartupSequencePhase.Route,
            MotionLevel.Standard);
        Pump(TimeSpan.FromMilliseconds(20));
        overlay.Snapshot = Snapshot(
            3,
            StartupSequencePhase.Bind,
            MotionLevel.Standard,
            firstState: StartupMilestoneState.Pending);
        StartupMilestoneRow row = Rows(overlay)[0];
        FrameworkElement node = (FrameworkElement)row.FindName("MilestoneNode");
        FrameworkElement status = (FrameworkElement)row.FindName("MilestoneStatus");
        FrameworkElement pending = (FrameworkElement)row.FindName("PendingFrame");
        TestSupport.False(node.HasAnimatedProperties, "pending does not replace node clock");
        TestSupport.False(status.HasAnimatedProperties, "pending does not replace status reveal");
        TestSupport.True(pending.HasAnimatedProperties, "pending frame clock");
        row.ClearTransientState();
        overlay.Snapshot = Snapshot(
            4,
            StartupSequencePhase.Bind,
            MotionLevel.Standard,
            firstState: StartupMilestoneState.Pending);
        row = Rows(overlay)[0];
        node = (FrameworkElement)row.FindName("MilestoneNode");
        status = (FrameworkElement)row.FindName("MilestoneStatus");
        pending = (FrameworkElement)row.FindName("PendingFrame");
        TestSupport.False(
            node.HasAnimatedProperties
                || status.HasAnimatedProperties
                || pending.HasAnimatedProperties,
            "unchanged state does not replay");
        overlay.Snapshot = Snapshot(
            5,
            StartupSequencePhase.Bind,
            MotionLevel.Standard,
            firstState: StartupMilestoneState.Ready);
        row = Rows(overlay)[0];
        FrameworkElement frame = (FrameworkElement)row.FindName("TerminalLockFrame");
        TestSupport.True(frame.HasAnimatedProperties, "terminal lock frame clock");
    });

    private static void VerifyRouteRuntime() => WithOverlay(MotionLevel.Full, overlay =>
    {
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Full);
        Pump(TimeSpan.FromMilliseconds(20));
        StartupMilestoneRow[] rows = Rows(overlay);
        TestSupport.Equal(6, rows.Length, "six route rows");
        StartupMilestoneRow first = rows[0];
        FrameworkElement lower = (FrameworkElement)first.FindName("LowerRouteSegment");
        FrameworkElement name = (FrameworkElement)first.FindName("MilestoneName");
        FrameworkElement status = (FrameworkElement)first.FindName("MilestoneStatus");
        FrameworkElement detail = (FrameworkElement)first.FindName("MilestoneDetail");
        RectangleGeometry routeClip = TestSupport.NotNull(
            lower.Clip as RectangleGeometry,
            "route clip");
        TestSupport.True(
            routeClip.HasAnimatedProperties || routeClip.Rect.Height >= 17d,
            "route clip clock or committed final geometry");
        TestSupport.True(
            name.HasAnimatedProperties || name.Opacity >= 0.999d,
            "name opacity clock or committed final value");
        TranslateTransform nameTransform = TestSupport.NotNull(
            name.RenderTransform as TranslateTransform,
            "name translation");
        TestSupport.True(
            nameTransform.HasAnimatedProperties
                || Math.Abs(nameTransform.X) < 0.001d,
            "name translation clock or committed final value");
        TestSupport.True(
            status.HasAnimatedProperties || status.Opacity >= 0.999d,
            "status clock or committed final value");
        TestSupport.True(
            detail.HasAnimatedProperties || detail.Opacity >= 0.999d,
            "detail clock or committed final value");
        TestSupport.True(rows.Last().FindName("LowerRouteSegment") is FrameworkElement { Visibility: Visibility.Hidden }, "last segment terminal");
    });

    private static void VerifyProjectionRuntime() => WithOverlay(MotionLevel.Full, overlay =>
    {
        overlay.Snapshot = Snapshot(
            2,
            StartupSequencePhase.Bind,
            MotionLevel.Full,
            projectionCount: 3,
            sensorState: StartupMilestoneState.Ready,
            postDataLayoutObserved: true);
        TextBlock previous = (TextBlock)overlay.FindName("ProjectionPreviousValue");
        TextBlock value = (TextBlock)overlay.FindName("ProjectionCurrentValue");
        TestSupport.True(previous.HasAnimatedProperties, "previous projection exit clock");
        TestSupport.True(value.HasAnimatedProperties, "current projection entry clock");
        TestSupport.True(((TranslateTransform)previous.RenderTransform).HasAnimatedProperties, "previous projection translation");
        TestSupport.True(((TranslateTransform)value.RenderTransform).HasAnimatedProperties, "current projection translation");
        TestSupport.True(value.Text.Contains("3 / 6 RESOLVED", StringComparison.Ordinal), "real projection counts");
        TestSupport.False(
            overlay.IsProjectionPulsePlaybackLatched
                || overlay.IsProjectionPulseActive
                || overlay.IsProjectionPulsePending,
            "partial projection updates values without authorizing a route pulse");
        overlay.Snapshot = Snapshot(
            3,
            StartupSequencePhase.Bind,
            MotionLevel.Full,
            projectionCount: 6,
            sensorState: StartupMilestoneState.Ready,
            postDataLayoutObserved: true);
        PumpUntil(
            () => overlay.ProjectionPulseStartedCount == 1,
            TimeSpan.FromMilliseconds(1000));
        TestSupport.Equal(
            1,
            overlay.ProjectionPulseStartedCount,
            "terminal projection starts exactly one route pulse");
        Pump(TimeSpan.FromMilliseconds(100));
        FrameworkElement source =
            (FrameworkElement)overlay.FindName("ProjectionSourceHorizontalSegment");
        FrameworkElement vertical =
            (FrameworkElement)overlay.FindName("ProjectionVerticalBridgeSegment");
        FrameworkElement target =
            (FrameworkElement)overlay.FindName("ProjectionTargetHorizontalSegment");
        foreach (FrameworkElement segment in new[] { source, vertical, target })
        {
            RectangleGeometry clip = TestSupport.NotNull(
                segment.Clip as RectangleGeometry,
                "projection segment clip");
            TestSupport.True(
                clip.HasAnimatedProperties
                || clip.Rect.Width >= segment.ActualWidth - 0.01d
                || clip.Rect.Height >= segment.ActualHeight - 0.01d,
                "projection segment animating or committed");
        }
    });

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(10));
        }

        TestSupport.True(condition(), "projection pulse became active");
    }

    private static void VerifyRevealRuntime() => WithOverlay(MotionLevel.Full, overlay =>
    {
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Reveal, MotionLevel.Full);
        FrameworkElement background = (FrameworkElement)overlay.FindName("StartupBackgroundLayer");
        FrameworkElement content = (FrameworkElement)overlay.FindName("StartupContentLayer");
        FrameworkElement rail = (FrameworkElement)overlay.FindName("StartupBottomRailLayer");
        PumpUntil(
            () => overlay.HasAnimatedProperties
                || overlay.Opacity == 0d
                || overlay.Visibility == Visibility.Collapsed,
            TimeSpan.FromMilliseconds(1000));
        if (overlay.Visibility == Visibility.Collapsed
            || !overlay.HasAnimatedProperties && overlay.Opacity == 0d)
        {
            TestSupport.Equal(0d, overlay.Opacity, "overlay exit completed");
        }
        else
        {
            TestSupport.True(overlay.HasAnimatedProperties, "whole overlay exit clock");
            TestSupport.Equal(1d, background.Opacity, "background remains stable");
            TestSupport.Equal(1d, content.Opacity, "content remains stable");
            TestSupport.Equal(1d, rail.Opacity, "rail remains stable");
            TestSupport.True(content.Clip is null, "content has no full-page clip");
            TestSupport.True(content.RenderTransform is TranslateTransform { HasAnimatedProperties: true }, "content translate clock");
        }
    });

    private static StartupMilestoneRow[] Rows(TraceworkStartupSequenceOverlay overlay)
    {
        ItemsControl matrix = (ItemsControl)overlay.FindName("RouteMatrixItems");
        return Enumerable.Range(0, matrix.Items.Count)
            .Select(index => matrix.ItemContainerGenerator.ContainerFromIndex(index))
            .OfType<DependencyObject>()
            .SelectMany(Descendants<StartupMilestoneRow>)
            .ToArray();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (T child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return child;
            }
        }
    }

    private static void WithOverlay(MotionLevel level, Action<TraceworkStartupSequenceOverlay> assertion)
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new()
        {
            Snapshot = Snapshot(1, StartupSequencePhase.Index, level)
        };
        Window host = new()
        {
            Content = overlay,
            Width = 1120,
            Height = 720,
            Left = -32000,
            Top = -32000,
            Opacity = 1,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            host.UpdateLayout();
            Pump(TimeSpan.FromMilliseconds(20));
            assertion(overlay);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static StartupSequenceSnapshot Snapshot(
        long version,
        StartupSequencePhase phase,
        MotionLevel level,
        bool canCommit = false,
        int projectionCount = 0,
        StartupMilestoneState? firstState = null,
        StartupMilestoneState? sensorState = null,
        bool postDataLayoutObserved = false)
    {
        StartupInitialProjectionSnapshot projection = new(
            1,
            Enum.GetValues<HardwareOverviewKind>()
                .Where(kind => kind is HardwareOverviewKind.Cpu
                    or HardwareOverviewKind.Gpu
                    or HardwareOverviewKind.Memory
                    or HardwareOverviewKind.Disk
                    or HardwareOverviewKind.Network
                    or HardwareOverviewKind.System)
                .Select((kind, index) => new StartupProjectionSlotSnapshot(
                    kind,
                    index < projectionCount ? StartupProjectionState.Value : StartupProjectionState.Pending,
                    index < projectionCount ? "resolved" : "pending"))
                .ToArray(),
            DispatcherApplied: postDataLayoutObserved || projectionCount == 6,
            PostDataLayoutObserved: postDataLayoutObserved || projectionCount == 6);
        return StartupSequenceSnapshot.Dormant(AppTheme.Tracework, level) with
        {
            Version = version,
            Phase = phase,
            IsActive = true,
            SurfaceMeasured = true,
            FirstFrameGateReleased = true,
            FirstFrameGateReleaseReason = "CompositorReady",
            VisualReady = true,
            InitialProjection = projection,
            CanCommit = canCommit,
            Milestones = Enum.GetValues<StartupMilestoneId>()
                .Select((id, index) =>
                {
                    StartupMilestoneState? requested = id == StartupMilestoneId.SensorBus
                        ? sensorState
                        : index == 0 ? firstState : null;
                    return requested.HasValue
                        ? new StartupMilestoneSnapshot(
                            id,
                            StartupMilestoneSnapshot.GetName(id),
                            requested.Value,
                            StartupMilestoneSnapshot.GetStatusText(requested.Value),
                            "state")
                        : StartupMilestoneSnapshot.Waiting(id);
                })
                .ToArray()
        };
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

    private static void PumpRenderTurn(Dispatcher dispatcher)
    {
        DispatcherFrame frame = new();
        dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
