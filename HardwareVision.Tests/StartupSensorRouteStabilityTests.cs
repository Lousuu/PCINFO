using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Views.Shell;
using Point = System.Windows.Point;

namespace HardwareVision.Tests;

internal static class StartupSensorRouteStabilityTests
{
    private const string PendingDetail = "Awaiting first shared polling sample";
    private const string FinalDetail = "218 readings received";

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup sensor route 01 pending hides ports", PendingHidesPorts),
        ("Startup sensor route 02 final detail precedes ports", FinalDetailPrecedesPorts),
        ("Startup sensor route 03 ports remain adjacent to final text", PortsRemainAdjacent),
        ("Startup sensor route 04 detail shrink has no old anchor frame", DetailShrinkHasNoOldAnchorFrame),
        ("Startup sensor route 05 rapid details authorize once", RapidDetailsAuthorizeOnce),
        ("Startup sensor route 06 motion levels preserve choreography", MotionLevelsPreserveChoreography),
        ("Startup sensor route 07 terminal failure remains bounded", TerminalFailureRemainsBounded)
    ];

    private static void PendingHidesPorts() =>
        WithOverlay(1200d, 720d, (overlay, _) =>
        {
            PreparePendingBind(overlay, MotionLevel.Full, 5);
            StartupMilestoneRow row = SensorRow(overlay);
            using FrameSampler sampler = new(overlay, row);
            Pump(TimeSpan.FromMilliseconds(120));
            sampler.Stop();

            TestSupport.True(sampler.Frames.Count >= 2, "pending real frames");
            foreach (SensorFrame frame in sampler.Frames)
            {
                TestSupport.True(frame.SourceOpacity <= 0.001d, "pending source port hidden");
                TestSupport.True(frame.TargetOpacity <= 0.001d, "pending target port hidden");
                TestSupport.False(frame.RouteVisible, "pending route hidden");
                TestSupport.Equal(0, frame.PulseStartedCount, "pending pulse not started");
            }
            TestSupport.Equal(0, row.ProjectionPortRevealCount, "pending source reveal count");
            TestSupport.Equal(0, overlay.ProjectionInputPortRevealCount, "pending target reveal count");
        });

    private static void FinalDetailPrecedesPorts() =>
        WithOverlay(1200d, 720d, (overlay, _) =>
        {
            PreparePendingBind(overlay, MotionLevel.Full, 5);
            StartupMilestoneRow row = SensorRow(overlay);
            using FrameSampler sampler = new(overlay, row);
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                6,
                FinalDetail,
                StartupMilestoneState.Ready,
                postDataLayoutObserved: true);
            PumpUntil(
                () => overlay.ProjectionPulseStartedCount == 1,
                TimeSpan.FromMilliseconds(1200),
                "pulse starts after sensor gate");
            Pump(TimeSpan.FromMilliseconds(40));
            sampler.Stop();

            int detailCompleted = First(sampler.Frames, frame => frame.FinalDetailPresented);
            int finalLayout = First(sampler.Frames, frame => frame.FinalLayoutObserved);
            int sourceVisible = First(sampler.Frames, frame => frame.SourceOpacity > 0.001d);
            int targetVisible = First(sampler.Frames, frame => frame.TargetOpacity > 0.001d);
            int routeVisible = First(sampler.Frames, frame => frame.RouteVisible);
            int pulseStarted = First(sampler.Frames, frame => frame.PulseStartedCount == 1);
            TestSupport.True(detailCompleted >= 0, "DetailCompleted frame");
            TestSupport.True(finalLayout > detailCompleted, "DetailCompleted < FinalLayoutObserved");
            TestSupport.True(sourceVisible > finalLayout, "FinalLayoutObserved < SourcePortVisible");
            TestSupport.True(targetVisible >= sourceVisible, "SourcePortVisible <= TargetPortVisible");
            TestSupport.True(routeVisible > targetVisible, "TargetPortVisible < RouteVisible");
            TestSupport.True(pulseStarted >= routeVisible, "RouteVisible <= PulseStarted");
            TestSupport.Equal(1, row.ProjectionPortRevealCount, "source reveals once");
            TestSupport.Equal(1, overlay.ProjectionInputPortRevealCount, "target reveals once");
        });

    private static void PortsRemainAdjacent() =>
        WithOverlay(1200d, 720d, (overlay, _) =>
        {
            PrepareTerminalRoute(overlay, MotionLevel.Full, 5, FinalDetail, StartupMilestoneState.Ready);
            StartupMilestoneRow row = SensorRow(overlay);
            FrameworkElement root = Element<FrameworkElement>(overlay, "OverlayRoot");
            TextBlock detail = row.CurrentDetailElement;
            FrameworkElement sourcePort = row.RouteOutputPortElement;
            FrameworkElement targetPort = Element<FrameworkElement>(overlay, "ProjectionInputPort");
            FrameworkElement projectionValue = Element<FrameworkElement>(overlay, "ProjectionValueClipHost");

            double detailGap = Left(sourcePort, row) - (Left(detail, row) + detail.ActualWidth);
            double projectionGap = Left(projectionValue, root)
                - (Left(targetPort, root) + targetPort.ActualWidth);
            TestSupport.True(Math.Abs(detailGap - 16d) <= 1d, "final detail-to-source gap");
            TestSupport.True(Math.Abs(projectionGap - 12d) <= 1d, "target-to-projection gap");
            TestSupport.True(row.DetailSlotWidth < 220d, "short final detail has no fixed empty slot");

            double shortPortX = CenterX(sourcePort, root);
            overlay.Snapshot = Snapshot(
                5,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                5,
                "218 readings received from shared polling source",
                StartupMilestoneState.Ready);
            PumpUntil(
                () => !row.IsDetailTransitionActive,
                TimeSpan.FromMilliseconds(500),
                "long detail settles");
            TestSupport.True(CenterX(sourcePort, root) > shortPortX, "long text naturally moves adjacent port right");
        });

    private static void DetailShrinkHasNoOldAnchorFrame() =>
        WithOverlay(1200d, 720d, (overlay, _) =>
        {
            PreparePendingBind(overlay, MotionLevel.Full, 5);
            StartupMilestoneRow row = SensorRow(overlay);
            FrameworkElement root = Element<FrameworkElement>(overlay, "OverlayRoot");
            double pendingRight = Right(row.CurrentDetailElement, root);
            using FrameSampler sampler = new(overlay, row);
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                5,
                "18 readings received",
                StartupMilestoneState.Ready);
            PumpUntil(
                () => overlay.AreSensorProjectionPortsVisibleFrameCommitted,
                TimeSpan.FromMilliseconds(900),
                "final ports become visible");
            Pump(TimeSpan.FromMilliseconds(40));
            sampler.Stop();

            List<SensorFrame> visible = sampler.Frames
                .Where(frame => frame.SourceOpacity > 0.001d)
                .ToList();
            TestSupport.True(visible.Count >= 2, "visible source frames sampled");
            double finalX = visible[^1].SourceX;
            TestSupport.True(finalX < pendingRight, "final source is left of old pending text edge");
            foreach (SensorFrame frame in visible)
            {
                TestSupport.True(Math.Abs(frame.SourceX - finalX) <= 0.75d, "source first appears only at final anchor");
                TestSupport.True(!frame.RouteVisible || Math.Abs(frame.RouteSourceX - frame.SourceX) <= 0.75d, "no disconnected route frame");
            }
        });

    private static void RapidDetailsAuthorizeOnce() =>
        WithOverlay(1200d, 720d, (overlay, _) =>
        {
            PreparePendingBind(overlay, MotionLevel.Full, 5);
            StartupMilestoneRow row = SensorRow(overlay);
            using FrameSampler sampler = new(overlay, row);
            overlay.Snapshot = Snapshot(4, StartupSequencePhase.Bind, MotionLevel.Full, 6, "1 reading received", StartupMilestoneState.Ready, true);
            Pump(TimeSpan.FromMilliseconds(20));
            overlay.Snapshot = Snapshot(5, StartupSequencePhase.Bind, MotionLevel.Full, 6, "37 readings received", StartupMilestoneState.Ready, true);
            Pump(TimeSpan.FromMilliseconds(20));
            overlay.Snapshot = Snapshot(6, StartupSequencePhase.Bind, MotionLevel.Full, 6, FinalDetail, StartupMilestoneState.Ready, true);
            PumpUntil(
                () => overlay.ProjectionPulseStartedCount == 1,
                TimeSpan.FromMilliseconds(1200),
                "latest rapid detail starts one pulse");
            Pump(TimeSpan.FromMilliseconds(80));
            sampler.Stop();

            TestSupport.Equal(FinalDetail, row.PresentedDetailText, "latest detail presented");
            TestSupport.Equal(FinalDetail, row.CurrentDetailElement.Text, "latest detail visible");
            TestSupport.Equal(string.Empty, row.PreviousDetailElement.Text, "stale previous cleared");
            TestSupport.Equal(1, row.ProjectionPortRevealCount, "rapid source authorizes once");
            TestSupport.Equal(1, overlay.ProjectionInputPortRevealCount, "rapid target authorizes once");
            TestSupport.Equal(1, overlay.ProjectionPulseStartedCount, "rapid pulse starts once");
            TestSupport.False(
                sampler.Frames.Any(frame => frame.RouteVisible
                    && frame.CurrentDetail is "1 reading received" or "37 readings received"),
                "stale detail never owns visible route");
        });

    private static void MotionLevelsPreserveChoreography()
    {
        foreach (MotionLevel level in Enum.GetValues<MotionLevel>())
        {
            WithOverlay(1200d, 720d, (overlay, _) =>
            {
                if (level == MotionLevel.Off)
                {
                    overlay.Snapshot = Snapshot(1, StartupSequencePhase.Bind, level, 6, FinalDetail, StartupMilestoneState.Ready, true);
                    Pump(TimeSpan.FromMilliseconds(40));
                    TestSupport.Equal(0, overlay.ProjectionPulseStartedCount, "Off keeps zero pulse");
                    TestSupport.False(overlay.IsProjectionPulseActive, "Off pulse inactive");
                    return;
                }

                PreparePendingBind(overlay, level, level is MotionLevel.Full or MotionLevel.Standard ? 5 : 4);
                StartupMilestoneRow row = SensorRow(overlay);
                using FrameSampler sampler = new(overlay, row);
                int projectionCount = level is MotionLevel.Full or MotionLevel.Standard ? 6 : 5;
                overlay.Snapshot = Snapshot(
                    4,
                    StartupSequencePhase.Bind,
                    level,
                    projectionCount,
                    FinalDetail,
                    StartupMilestoneState.Ready,
                    postDataLayoutObserved: true);
                PumpUntil(
                    () => overlay.AreSensorProjectionPortsVisibleFrameCommitted,
                    TimeSpan.FromMilliseconds(1000),
                    $"{level} ports visible");
                Pump(TimeSpan.FromMilliseconds(80));
                sampler.Stop();

                TestSupport.Equal(1, row.ProjectionPortRevealCount, $"{level} source effect once");
                TestSupport.Equal(1, overlay.ProjectionInputPortRevealCount, $"{level} target effect once");
                TestSupport.True(sampler.Frames.Any(frame => frame.RouteVisible), $"{level} route frame");
                if (level is MotionLevel.Full or MotionLevel.Standard)
                {
                    TestSupport.True(
                        sampler.Frames.Any(frame => frame.PreviousOpacity > 0d && frame.CurrentOpacity < 1d),
                        $"{level} real two-layer text frame");
                    PumpUntil(
                        () => overlay.ProjectionPulseStartedCount == 1,
                        TimeSpan.FromMilliseconds(600),
                        $"{level} original pulse");
                }
                else
                {
                    TestSupport.Equal(0, overlay.ProjectionPulseStartedCount, "Reduced keeps zero pulse");
                }
            });
        }
    }

    private static void TerminalFailureRemainsBounded()
    {
        foreach (StartupMilestoneState state in new[]
                 {
                     StartupMilestoneState.Partial,
                     StartupMilestoneState.Failed
                 })
        {
            WithOverlay(1200d, 720d, (overlay, _) =>
            {
                PreparePendingBind(overlay, MotionLevel.Reduced, 4);
                StartupMilestoneRow row = SensorRow(overlay);
                string detail = state == StartupMilestoneState.Partial
                    ? "Partial sensor data available"
                    : "Shared polling sample unavailable";
                overlay.Snapshot = Snapshot(4, StartupSequencePhase.Bind, MotionLevel.Reduced, 4, detail, state);
                PumpUntil(
                    () => overlay.AreSensorProjectionPortsVisibleFrameCommitted
                        && overlay.LastDormantProjectionRoute.HasValue,
                    TimeSpan.FromMilliseconds(800),
                    $"{state} terminal fail-open route");
                TestSupport.Equal(detail, row.CurrentDetailElement.Text, $"{state} final detail");
                TestSupport.Equal(1, row.ProjectionPortRevealCount, $"{state} bounded source reveal");
                TestSupport.Equal(0, overlay.ProjectionPulseStartedCount, $"{state} no unauthorized pulse");
            });
        }
    }

    private static void PreparePendingBind(
        TraceworkStartupSequenceOverlay overlay,
        MotionLevel level,
        int projectionCount)
    {
        overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, level, projectionCount, PendingDetail, StartupMilestoneState.Pending);
        PumpUntil(() => Rows(overlay).Length == Enum.GetValues<StartupMilestoneId>().Length, TimeSpan.FromMilliseconds(500), "rows generated");
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, level, projectionCount, PendingDetail, StartupMilestoneState.Pending);
        overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, level, projectionCount, PendingDetail, StartupMilestoneState.Pending);
        PumpUntil(
            () => overlay.IsProjectionLedgerReady
                && !overlay.IsProjectionValueTransitionActive
                && !overlay.IsProjectionValueTransitionPending,
            TimeSpan.FromMilliseconds(700),
            "pending Bind settles");
        TestSupport.True(SensorRow(overlay).RouteOutputPortElement.Opacity <= 0.001d, "pending source stays hidden");
    }

    private static void PrepareTerminalRoute(
        TraceworkStartupSequenceOverlay overlay,
        MotionLevel level,
        int projectionCount,
        string detail,
        StartupMilestoneState state)
    {
        PreparePendingBind(overlay, level, projectionCount);
        overlay.Snapshot = Snapshot(4, StartupSequencePhase.Bind, level, projectionCount, detail, state);
        PumpUntil(
            () => overlay.AreSensorProjectionPortsVisibleFrameCommitted
                && overlay.LastDormantProjectionRoute.HasValue,
            TimeSpan.FromMilliseconds(1000),
            "terminal route visible");
    }

    private static StartupSequenceSnapshot Snapshot(
        long version,
        StartupSequencePhase phase,
        MotionLevel level,
        int projectionCount,
        string sensorDetail,
        StartupMilestoneState sensorState,
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
            DispatcherApplied: true,
            PostDataLayoutObserved: postDataLayoutObserved);
        return StartupSequenceSnapshot.Dormant(AppTheme.Tracework, level) with
        {
            Version = version,
            Phase = phase,
            IsActive = true,
            VisualReady = true,
            InitialProjection = projection,
            Milestones = Enum.GetValues<StartupMilestoneId>()
                .Select(id =>
                {
                    StartupMilestoneState state = id == StartupMilestoneId.SensorBus
                        ? sensorState
                        : StartupMilestoneState.Ready;
                    return new StartupMilestoneSnapshot(
                        id,
                        StartupMilestoneSnapshot.GetName(id),
                        state,
                        StartupMilestoneSnapshot.GetStatusText(state),
                        id == StartupMilestoneId.SensorBus ? sensorDetail : "ready");
                })
                .ToArray()
        };
    }

    private static int First(IReadOnlyList<SensorFrame> frames, Func<SensorFrame, bool> predicate)
    {
        for (int index = 0; index < frames.Count; index++)
        {
            if (predicate(frames[index]))
            {
                return index;
            }
        }
        return -1;
    }

    private static double Left(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0d, 0d), root).X;

    private static double Right(FrameworkElement element, FrameworkElement root) =>
        Left(element, root) + element.ActualWidth;

    private static double CenterX(FrameworkElement element, FrameworkElement root) =>
        Left(element, root) + (element.ActualWidth / 2d);

    private static StartupMilestoneRow SensorRow(TraceworkStartupSequenceOverlay overlay) =>
        Rows(overlay).Single(row => row.RouteOutputPortElement.Tag is true);

    private static StartupMilestoneRow[] Rows(TraceworkStartupSequenceOverlay overlay)
    {
        ItemsControl matrix = Element<ItemsControl>(overlay, "RouteMatrixItems");
        return Enumerable.Range(0, matrix.Items.Count)
            .Select(index => matrix.ItemContainerGenerator.ContainerFromIndex(index))
            .OfType<DependencyObject>()
            .SelectMany(Descendants<StartupMilestoneRow>)
            .ToArray();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
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

    private static T Element<T>(FrameworkElement owner, string name) where T : class =>
        TestSupport.NotNull(owner.FindName(name) as T, name);

    private static void WithOverlay(
        double width,
        double height,
        Action<TraceworkStartupSequenceOverlay, Window> assertion)
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new();
        Window host = new()
        {
            Content = overlay,
            Width = width,
            Height = height,
            Left = -32000,
            Top = -32000,
            Opacity = 1d,
            ShowActivated = false,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            PumpUntil(() => overlay.IsLoaded, TimeSpan.FromMilliseconds(500), "overlay loaded");
            assertion(overlay, host);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout, string message)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(16));
        }
        TestSupport.True(condition(), message);
    }

    private static void Pump(TimeSpan duration)
    {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(duration, DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
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

    private sealed class FrameSampler : IDisposable
    {
        private readonly TraceworkStartupSequenceOverlay overlay;
        private readonly StartupMilestoneRow row;
        private readonly FrameworkElement root;
        private readonly FrameworkElement sourcePort;
        private readonly FrameworkElement targetPort;
        private readonly FrameworkElement dormantSource;
        private readonly FrameworkElement activeSource;
        private bool stopped;

        public FrameSampler(TraceworkStartupSequenceOverlay overlay, StartupMilestoneRow row)
        {
            this.overlay = overlay;
            this.row = row;
            root = Element<FrameworkElement>(overlay, "OverlayRoot");
            sourcePort = row.RouteOutputPortElement;
            targetPort = Element<FrameworkElement>(overlay, "ProjectionInputPort");
            dormantSource = Element<FrameworkElement>(overlay, "ProjectionDormantSourceSegment");
            activeSource = Element<FrameworkElement>(overlay, "ProjectionSourceHorizontalSegment");
            CompositionTarget.Rendering += OnRendering;
        }

        public List<SensorFrame> Frames { get; } = [];

        public void Stop()
        {
            if (stopped)
            {
                return;
            }
            stopped = true;
            CompositionTarget.Rendering -= OnRendering;
        }

        public void Dispose() => Stop();

        private void OnRendering(object? sender, EventArgs args)
        {
            _ = sender;
            if (args is not RenderingEventArgs || !overlay.IsLoaded || !row.IsLoaded)
            {
                return;
            }
            bool dormantVisible = dormantSource.Visibility == Visibility.Visible
                && dormantSource.Opacity > 0d
                && dormantSource.ActualWidth > 0d;
            bool activeVisible = activeSource.Visibility == Visibility.Visible
                && activeSource.Opacity > 0d
                && activeSource.ActualWidth > 0d;
            double routeSourceX = overlay.LastDormantProjectionRoute?.Source.X
                ?? overlay.LastProjectionRoute?.Source.X
                ?? double.NaN;
            Frames.Add(new SensorFrame(
                row.IsFinalSensorDetailPresented && !row.IsDetailTransitionActive,
                overlay.IsSensorFinalLayoutObserved,
                sourcePort.Opacity,
                targetPort.Opacity,
                CenterX(sourcePort, root),
                routeSourceX,
                dormantVisible || activeVisible,
                overlay.ProjectionPulseStartedCount,
                row.PreviousDetailElement.Text,
                row.CurrentDetailElement.Text,
                row.PreviousDetailElement.Opacity,
                row.CurrentDetailElement.Opacity));
        }
    }

    private readonly record struct SensorFrame(
        bool FinalDetailPresented,
        bool FinalLayoutObserved,
        double SourceOpacity,
        double TargetOpacity,
        double SourceX,
        double RouteSourceX,
        bool RouteVisible,
        int PulseStartedCount,
        string PreviousDetail,
        string CurrentDetail,
        double PreviousOpacity,
        double CurrentOpacity);
}
