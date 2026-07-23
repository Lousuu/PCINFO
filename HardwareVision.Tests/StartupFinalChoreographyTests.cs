using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Views.Shell;
using Point = System.Windows.Point;

namespace HardwareVision.Tests;

internal static class StartupFinalChoreographyTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"Startup Projection geometry {iteration:00}/20", VerifyProjectionGeometry));
            tests.Add(($"Startup Projection queue {iteration:00}/20", VerifyProjectionQueue));
            tests.Add(($"Startup Route choreography {iteration:00}/20", VerifyRouteChoreography));
            tests.Add(($"Startup Bottom Rail Reduced {iteration:00}/20", VerifyBottomRailAndReduced));
        }

        return tests;
    }

    private static void VerifyProjectionGeometry()
    {
        VerifyProjectionGeometryAtSize(1120d, 720d);
        VerifyProjectionGeometryAtSize(1600d, 900d);

        Point source = new(100d, 200d);
        Point downwardTarget = new(300d, 320d);
        TestSupport.True(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                downwardTarget,
                out TraceworkStartupSequenceOverlay.ProjectionRoute downward),
            "downward route");
        TestSupport.Equal(320d, downward.TotalRouteLength, "downward total route length");
        TestSupport.Equal(120d, downward.VerticalBridgeLength, "downward bridge length");
        TestSupport.Equal(200d, downward.CorridorX, "downward midpoint corridor");

        Point upwardTarget = new(300d, 80d);
        TestSupport.True(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                upwardTarget,
                out TraceworkStartupSequenceOverlay.ProjectionRoute upward),
            "upward route");
        TestSupport.Equal(120d, upward.VerticalBridgeLength, "upward bridge length");
        TestSupport.Equal(200d, upward.CorridorX, "upward midpoint corridor");

        TestSupport.True(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                new Point(180d, 204d),
                out TraceworkStartupSequenceOverlay.ProjectionRoute shortHorizontal),
            "short aligned route");
        TestSupport.False(shortHorizontal.UsesThreeSegments, "short route uses one segment");
        TestSupport.Equal(80d, shortHorizontal.SourceHorizontalLength, "short horizontal length");
        TestSupport.False(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                new Point(180d, 205d),
                out _),
            "short misaligned route suppressed");
        TestSupport.False(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                new Point(90d, 200d),
                out _),
            "reverse route suppressed");

        double fullDuration =
            TraceworkStartupSequenceOverlay.ResolveProjectionRouteDurationMilliseconds(
                downward.TotalRouteLength,
                MotionLevel.Full);
        double standardDuration =
            TraceworkStartupSequenceOverlay.ResolveProjectionRouteDurationMilliseconds(
                downward.TotalRouteLength,
                MotionLevel.Standard);
        TestSupport.True(fullDuration is >= 360d and <= 520d, "Full length duration");
        TestSupport.True(standardDuration is >= 260d and <= 380d, "Standard length duration");
    }

    private static void VerifyProjectionGeometryAtSize(double width, double height) =>
        WithOverlay(width, height, (overlay, _) =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 5);
            TextBlock previous = Element<TextBlock>(overlay, "ProjectionPreviousValue");
            TextBlock current = Element<TextBlock>(overlay, "ProjectionCurrentValue");
            TestSupport.Equal("0 / 6 RESOLVED", previous.Text, "Bind replay previous value");
            TestSupport.Equal("5 / 6 RESOLVED", current.Text, "Bind replay current value");
            TestSupport.False(overlay.IsProjectionLedgerReady, "ledger waits for entry completion");
            TestSupport.True(overlay.IsProjectionPulsePending, "ledger queues latest route");
            Pump(TimeSpan.FromMilliseconds(220));
            TestSupport.True(overlay.IsProjectionLedgerReady, "ledger ready after entry");
            TestSupport.True(overlay.IsProjectionPulseActive, "route active after ledger ready");

            StartupMilestoneRow sensorRow = Rows(overlay)[3];
            FrameworkElement sourcePort = Element<FrameworkElement>(sensorRow, "RouteOutputPort");
            FrameworkElement sourceAnchor = Element<FrameworkElement>(sensorRow, "RouteOutputAnchor");
            FrameworkElement targetPort = Element<FrameworkElement>(overlay, "ProjectionInputPort");
            FrameworkElement targetAnchor = Element<FrameworkElement>(overlay, "ProjectionInputAnchor");
            FrameworkElement root = Element<FrameworkElement>(overlay, "OverlayRoot");
            TestSupport.Equal(6d, sourcePort.ActualWidth, "source port width");
            TestSupport.Equal(6d, sourcePort.ActualHeight, "source port height");
            TestSupport.Equal(1d, sourcePort.Opacity, "source port active");
            TestSupport.Equal(6d, targetPort.ActualWidth, "target port width");
            TestSupport.Equal(6d, targetPort.ActualHeight, "target port height");
            TestSupport.Equal(1d, targetPort.Opacity, "target port active");

            Point source = sourceAnchor.TranslatePoint(
                new Point(sourceAnchor.ActualWidth / 2d, sourceAnchor.ActualHeight / 2d),
                root);
            Point target = targetAnchor.TranslatePoint(
                new Point(targetAnchor.ActualWidth / 2d, targetAnchor.ActualHeight / 2d),
                root);
            TraceworkStartupSequenceOverlay.ProjectionRoute route =
                overlay.LastProjectionRoute
                ?? throw new InvalidOperationException("captured playback route");
            TestSupport.True(
                (route.Source - source).Length < 2d,
                "playback source matches visible port");
            TestSupport.True(
                (route.Target - target).Length < 2d,
                "playback target matches visible port");
            FrameworkElement sourceSegment =
                Element<FrameworkElement>(overlay, "ProjectionSourceHorizontalSegment");
            FrameworkElement verticalSegment =
                Element<FrameworkElement>(overlay, "ProjectionVerticalBridgeSegment");
            FrameworkElement targetSegment =
                Element<FrameworkElement>(overlay, "ProjectionTargetHorizontalSegment");
            double corridorX = Canvas.GetLeft(verticalSegment) + 0.5d;

            TestSupport.True(
                route.Target.X - route.Source.X >= 96d,
                "responsive route corridor");
            TestSupport.True(
                corridorX >= route.Source.X + 48d,
                "corridor source clearance");
            TestSupport.True(
                corridorX <= route.Target.X - 48d,
                "corridor target clearance");
            TestSupport.True(
                Math.Abs(corridorX - route.CorridorX) < 0.01d,
                "corridor matches playback route");
            TestSupport.True(
                Math.Abs(
                    route.CorridorX
                    - (route.Source.X
                        + ((route.Target.X - route.Source.X) * 0.5d))) < 0.01d,
                "corridor midpoint");
            TestSupport.True(sourceSegment.Width >= 40d, "source segment minimum");
            TestSupport.True(targetSegment.Width >= 40d, "target segment minimum");
            Near(route.Source.X, Canvas.GetLeft(sourceSegment), "source segment left");
            Near(route.Source.Y - 0.5d, Canvas.GetTop(sourceSegment), "source segment top");
            Near(corridorX - 0.5d, Canvas.GetLeft(verticalSegment), "vertical left");
            Near(
                Math.Min(route.Source.Y, route.Target.Y),
                Canvas.GetTop(verticalSegment),
                "vertical minimum Y");
            Near(
                Math.Abs(route.Target.Y - route.Source.Y),
                verticalSegment.Height,
                "vertical bridge height");
            Near(corridorX, Canvas.GetLeft(targetSegment), "target segment left");
            Near(route.Target.Y - 0.5d, Canvas.GetTop(targetSegment), "target segment top");
            TestSupport.True(
                Math.Abs(
                    Canvas.GetLeft(targetSegment)
                    + targetSegment.Width
                    - route.Target.X) < 0.01d,
                "route reaches target center");
            foreach (FrameworkElement segment in new[]
                     {
                         sourceSegment,
                         verticalSegment,
                         targetSegment
                     })
            {
                RectangleGeometry clip = TestSupport.NotNull(
                    segment.Clip as RectangleGeometry,
                    "independent segment clip");
                TestSupport.True(clip.HasAnimatedProperties, "segment owns reveal clock");
            }

            FrameworkElement head = Element<FrameworkElement>(overlay, "ProjectionPulseHead");
            TestSupport.Equal(5d, head.Width, "pulse head width");
            TestSupport.Equal(5d, head.Height, "pulse head height");
            TestSupport.True(head.HasAnimatedProperties, "Full pulse head clocks");
        });

    private static void VerifyProjectionQueue() =>
        WithOverlay(1120d, 720d, (overlay, _) =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 1);
            Pump(TimeSpan.FromMilliseconds(220));
            TestSupport.True(overlay.IsProjectionPulseActive, "first route active");
            FrameworkElement sourceSegment =
                Element<FrameworkElement>(overlay, "ProjectionSourceHorizontalSegment");
            RectangleGeometry firstClip = TestSupport.NotNull(
                sourceSegment.Clip as RectangleGeometry,
                "first route clip");

            for (int count = 2; count <= 6; count++)
            {
                overlay.Snapshot = Snapshot(
                    count + 3,
                    StartupSequencePhase.Bind,
                    MotionLevel.Full,
                    count);
            }

            TestSupport.True(overlay.IsProjectionPulseActive, "active route preserved");
            TestSupport.True(overlay.IsProjectionPulsePending, "latest update pending");
            TestSupport.Equal(6, overlay.LatestPendingResolvedCount, "latest pending count");
            TestSupport.Equal(6, overlay.LastPresentedResolvedCount, "value updates immediately");
            TestSupport.Equal(
                "6 / 6 RESOLVED",
                Element<TextBlock>(overlay, "ProjectionCurrentValue").Text,
                "latest value visible");
            TestSupport.True(
                ReferenceEquals(firstClip, sourceSegment.Clip),
                "new snapshots do not replace active route");

            Pump(TimeSpan.FromMilliseconds(700));
            TestSupport.True(overlay.IsProjectionPulseActive, "one coalesced replay active");
            TestSupport.False(overlay.IsProjectionPulsePending, "pending consumed once");
            TestSupport.False(
                ReferenceEquals(firstClip, sourceSegment.Clip),
                "coalesced replay uses latest geometry");

            overlay.Snapshot = Snapshot(
                20,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                6);
            TestSupport.False(overlay.IsProjectionPulseActive, "Reveal stops active route");
            TestSupport.False(overlay.IsProjectionPulsePending, "Reveal clears pending");
            TestSupport.Equal(
                0d,
                Element<FrameworkElement>(overlay, "ProjectionPulseCanvas").Opacity,
                "Reveal hides route canvas");
            TestSupport.False(
                Element<FrameworkElement>(overlay, "ProjectionPulseHead").HasAnimatedProperties,
                "Reveal clears head clocks");
        });

    private static void VerifyRouteChoreography() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(
                1,
                StartupSequencePhase.Index,
                MotionLevel.Full,
                0,
                StartupMilestoneState.Pending);
            overlay.UpdateLayout();
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            overlay.UpdateLayout();
            overlay.Snapshot = Snapshot(
                2,
                StartupSequencePhase.Route,
                MotionLevel.Full,
                0,
                StartupMilestoneState.Pending);
            StartupMilestoneRow[] rows = Rows(overlay);
            TestSupport.Equal(6, rows.Length, "six route rows");
            TestSupport.Equal(
                170,
                StartupMilestoneRow.FullRouteRowIntervalMilliseconds,
                "Full row interval");
            TestSupport.Equal(
                110,
                StartupMilestoneRow.StandardRouteRowIntervalMilliseconds,
                "Standard row interval");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(1050),
                StartupSequenceService.ResolveTraceworkRouteDuration(MotionLevel.Full),
                "Full route duration");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(680),
                StartupSequenceService.ResolveTraceworkRouteDuration(MotionLevel.Standard),
                "Standard route duration");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(4210),
                StartupSequenceService.ResolveTraceworkHardCutoff(MotionLevel.Full),
                "Full hard cutoff");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(3460),
                StartupSequenceService.ResolveTraceworkHardCutoff(MotionLevel.Standard),
                "Standard hard cutoff");

            StartupMilestoneRow first = rows[0];
            FrameworkElement node = Element<FrameworkElement>(first, "MilestoneNode");
            FrameworkElement name = Element<FrameworkElement>(first, "MilestoneName");
            FrameworkElement status = Element<FrameworkElement>(first, "MilestoneStatus");
            FrameworkElement detail = Element<FrameworkElement>(first, "MilestoneDetail");
            FrameworkElement lower = Element<FrameworkElement>(first, "LowerRouteSegment");
            FrameworkElement pending = Element<FrameworkElement>(first, "PendingFrame");
            TestSupport.True(node.HasAnimatedProperties, "node reveal clock");
            TestSupport.True(name.HasAnimatedProperties, "name follows node arrival");
            TestSupport.True(status.HasAnimatedProperties, "status reveal clock");
            TestSupport.True(detail.HasAnimatedProperties, "detail reveal clock");
            TestSupport.True(
                lower.Clip is RectangleGeometry { HasAnimatedProperties: true },
                "lower route delayed clock");
            TestSupport.True(pending.HasAnimatedProperties, "pending uses independent frame");
            TestSupport.True(
                rows.Last().FindName("LowerRouteSegment")
                    is FrameworkElement { Visibility: Visibility.Hidden },
                "last row has no lower segment");

            first.ClearTransientState();
            overlay.Snapshot = Snapshot(
                3,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                1,
                StartupMilestoneState.Wait);
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                1,
                StartupMilestoneState.Pending);
            first = Rows(overlay)[0];
            node = Element<FrameworkElement>(first, "MilestoneNode");
            pending = Element<FrameworkElement>(first, "PendingFrame");
            TestSupport.False(node.HasAnimatedProperties, "pending does not replace node opacity");
            TestSupport.True(pending.HasAnimatedProperties, "pending transition uses frame");
        });

    private static void VerifyBottomRailAndReduced()
    {
        WithOverlay(1120d, 720d, full =>
        {
            full.Snapshot = Snapshot(
                1,
                StartupSequencePhase.Index,
                MotionLevel.Full,
                0);
            full.UpdateLayout();
            TextBlock phaseText = Element<TextBlock>(full, "BottomCurrentPhaseText");
            Border indexSegment = Element<Border>(full, "PhaseSegmentIndex");
            TestSupport.False(full.IsBottomRailReady, "rail not ready before entry");
            TestSupport.Equal(string.Empty, phaseText.Text, "phase withheld before rail ready");
            TestSupport.True(indexSegment.Clip is null, "Index track not animated while hidden");
            Pump(TimeSpan.FromMilliseconds(330));
            TestSupport.True(full.IsBottomRailReady, "rail ready after visible entry");
            TestSupport.Equal(
                "01 / 05  构建系统索引",
                phaseText.Text,
                "Index phase plays after entry");
            RectangleGeometry firstClip = TestSupport.NotNull(
                indexSegment.Clip as RectangleGeometry,
                "Index track reveal");
            full.Snapshot = Snapshot(
                2,
                StartupSequencePhase.Index,
                MotionLevel.Full,
                0);
            TestSupport.True(
                ReferenceEquals(firstClip, indexSegment.Clip),
                "same phase does not replay");
        });

        WithOverlay(1120d, 720d, reduced =>
        {
            reduced.Snapshot = Snapshot(
                1,
                StartupSequencePhase.Index,
                MotionLevel.Reduced,
                0);
            reduced.UpdateLayout();
            TextBlock title = Element<TextBlock>(reduced, "TraceworkTitleText");
            TextBlock subtitle = Element<TextBlock>(reduced, "StartupSubtitleText");
            FrameworkElement titleGroup = Element<FrameworkElement>(reduced, "StartupTitleGroup");
            TestSupport.Equal(1d, title.Opacity, "Reduced TRACEWORK visible");
            TestSupport.Equal(1d, subtitle.Opacity, "Reduced subtitle visible");
            TestSupport.True(titleGroup.HasAnimatedProperties, "Reduced group fade");
            TestSupport.False(
                ((TranslateTransform)title.RenderTransform).HasAnimatedProperties,
                "Reduced title has no translation");
            TestSupport.True(reduced.IsBottomRailReady, "Reduced rail ready with group");
            TestSupport.Equal(
                "01 / 05  构建系统索引",
                Element<TextBlock>(reduced, "BottomCurrentPhaseText").Text,
                "Reduced phase enters with rail");
            reduced.Snapshot = Snapshot(
                2,
                StartupSequencePhase.Bind,
                MotionLevel.Reduced,
                2);
            TestSupport.False(
                reduced.IsProjectionPulseActive,
                "Reduced has no projection route");
        });
    }

    private static void PrepareProjectionBind(
        TraceworkStartupSequenceOverlay overlay,
        MotionLevel level,
        int projectionCount)
    {
        overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, level, projectionCount);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, level, projectionCount);
        overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, level, projectionCount);
        overlay.UpdateLayout();
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

    private static T Element<T>(FrameworkElement owner, string name) where T : class =>
        TestSupport.NotNull(owner.FindName(name) as T, name);

    private static void Near(double expected, double actual, string message) =>
        TestSupport.True(
            Math.Abs(expected - actual) < 0.5d,
            $"{message}: expected {expected:0.###}, got {actual:0.###}");

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

    private static void WithOverlay(
        double width,
        double height,
        Action<TraceworkStartupSequenceOverlay> assertion) =>
        WithOverlay(width, height, (overlay, _) => assertion(overlay));

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
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            host.UpdateLayout();
            assertion(overlay, host);
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
        int projectionCount,
        StartupMilestoneState terminalState = StartupMilestoneState.Wait)
    {
        StartupInitialProjectionSnapshot projection = new(
            version,
            Enum.GetValues<HardwareOverviewKind>()
                .Where(kind => kind is HardwareOverviewKind.Cpu
                    or HardwareOverviewKind.Gpu
                    or HardwareOverviewKind.Memory
                    or HardwareOverviewKind.Disk
                    or HardwareOverviewKind.Network
                    or HardwareOverviewKind.System)
                .Select((kind, index) => new StartupProjectionSlotSnapshot(
                    kind,
                    index < projectionCount
                        ? StartupProjectionState.Value
                        : StartupProjectionState.Pending,
                    index < projectionCount ? "resolved" : "pending"))
                .ToArray(),
            DispatcherApplied: projectionCount == 6,
            PostDataLayoutObserved: projectionCount == 6);
        return StartupSequenceSnapshot.Dormant(AppTheme.Tracework, level) with
        {
            Version = version,
            Phase = phase,
            IsActive = true,
            VisualReady = true,
            InitialProjection = projection,
            Milestones = Enum.GetValues<StartupMilestoneId>()
                .Select(id => new StartupMilestoneSnapshot(
                    id,
                    StartupMilestoneSnapshot.GetName(id),
                    terminalState,
                    StartupMilestoneSnapshot.GetStatusText(terminalState),
                    terminalState.ToString()))
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
}
