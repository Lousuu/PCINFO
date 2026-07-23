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
            tests.Add(($"Reveal late-snapshot {iteration:00}/20", VerifyRevealLateSnapshot));
            tests.Add(($"Delayed Clip {iteration:00}/20", VerifyDelayedClips));
            tests.Add(($"Route continuity {iteration:00}/20", VerifyRouteChoreography));
            tests.Add(($"Projection compact geometry {iteration:00}/20", VerifyProjectionGeometry));
            tests.Add(($"Projection value coalescing {iteration:00}/20", VerifyProjectionQueue));
            tests.Add(($"PollingVersion reset {iteration:00}/20", VerifyPollingVersionReset));
            tests.Add(($"Projection-to-Commit sequencing {iteration:00}/20", VerifyProjectionCommit));
            tests.Add(($"Readiness settle {iteration:00}/20", TestSupport.Run(VerifyReadinessSettleAsync)));
        }

        return tests;
    }

    private static void VerifyProjectionGeometry()
    {
        VerifyProjectionGeometryAtSize(1107d, 685d);
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
                new Point(180d, 200.5d),
                out TraceworkStartupSequenceOverlay.ProjectionRoute shortHorizontal),
            "short aligned route");
        TestSupport.False(shortHorizontal.UsesThreeSegments, "short route uses one segment");
        TestSupport.Equal(80d, shortHorizontal.SourceHorizontalLength, "short horizontal length");
        TestSupport.True(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                new Point(180d, 201.1d),
                out TraceworkStartupSequenceOverlay.ProjectionRoute shortBridge),
            "misaligned route uses compact bridge");
        TestSupport.True(shortBridge.UsesThreeSegments, "misaligned compact bridge");
        foreach (double compactWidth in new[] { 36d, 48d, 72d })
        {
            TestSupport.True(
                TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                    source,
                    new Point(source.X + compactWidth, 230d),
                    out TraceworkStartupSequenceOverlay.ProjectionRoute compact),
                $"{compactWidth:0} DIP compact route");
            TestSupport.True(compact.UsesThreeSegments, "compact uses three segments");
            double minimum = compactWidth >= 72d ? 24d : 12d;
            TestSupport.True(compact.SourceHorizontalLength >= minimum, "compact source minimum");
            TestSupport.True(compact.TargetHorizontalLength >= minimum, "compact target minimum");
        }
        TestSupport.False(
            TraceworkStartupSequenceOverlay.TryCreateProjectionRoute(
                source,
                new Point(130d, 230d),
                out _),
            "24-36 DIP vertical route suppressed");
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
            PumpUntil(() => overlay.IsProjectionLedgerReady, TimeSpan.FromMilliseconds(500));
            TestSupport.True(overlay.IsProjectionLedgerReady, "ledger ready after entry");
            TestSupport.True(overlay.IsProjectionPulseActive, "route active after ledger ready");
            Pump(TimeSpan.FromMilliseconds(100));

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
                route.Target.X - route.Source.X >= 36d,
                "responsive route corridor");
            TestSupport.True(
                corridorX >= route.Source.X + 24d,
                "corridor source clearance");
            TestSupport.True(
                corridorX <= route.Target.X - 24d,
                "corridor target clearance");
            TestSupport.True(
                Math.Abs(corridorX - route.CorridorX) < 0.01d,
                "corridor matches playback route");
            TestSupport.True(
                Math.Abs(
                    route.CorridorX
                    - (route.Source.X
                        + ((route.Target.X - route.Source.X) * 0.5d))) <= 0.5d,
                "corridor midpoint");
            TestSupport.True(sourceSegment.Width >= 24d, "source segment minimum");
            TestSupport.True(targetSegment.Width >= 24d, "target segment minimum");
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
                    - route.Target.X) <= 0.5d,
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
                TestSupport.True(
                    clip.HasAnimatedProperties
                    || clip.Rect.Width >= segment.ActualWidth - 0.01d
                    || clip.Rect.Height >= segment.ActualHeight - 0.01d,
                    "segment is animating or committed");
            }

            FrameworkElement head = Element<FrameworkElement>(overlay, "ProjectionPulseHead");
            TestSupport.Equal(5d, head.Width, "pulse head width");
            TestSupport.Equal(5d, head.Height, "pulse head height");
            TestSupport.True(head.HasAnimatedProperties, "Full pulse head clocks");
            foreach (double coordinate in new[]
                     {
                         Canvas.GetLeft(sourceSegment),
                         Canvas.GetTop(sourceSegment),
                         Canvas.GetLeft(verticalSegment),
                         Canvas.GetTop(verticalSegment),
                         Canvas.GetLeft(targetSegment),
                         Canvas.GetTop(targetSegment)
                     })
            {
                TestSupport.True(
                    Math.Abs((coordinate * 2d) - Math.Round(coordinate * 2d)) < 0.01d,
                    "segment position aligns to 0.5 DIP");
            }
            double finalHeadX = Canvas.GetLeft(head)
                + 2.5d
                + route.SourceHorizontalLength
                + route.TargetHorizontalLength;
            double finalHeadY = Canvas.GetTop(head)
                + 2.5d
                + route.Target.Y
                - route.Source.Y;
            TestSupport.True(
                Math.Abs(finalHeadX - route.Target.X) <= 0.5d,
                "pulse head final X");
            TestSupport.True(
                Math.Abs(finalHeadY - route.Target.Y) <= 0.5d,
                "pulse head final Y");
            TestSupport.True(
                Element<FrameworkElement>(overlay, "OverlayRoot").UseLayoutRounding,
                "logical DIP layout rounding");
            TestSupport.True(sourcePort.SnapsToDevicePixels, "source snaps to device pixels");
            TestSupport.True(targetPort.SnapsToDevicePixels, "target snaps to device pixels");
        });

    private static void VerifyProjectionQueue() =>
        WithOverlay(1120d, 720d, (overlay, _) =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 1);
            PumpUntil(() => overlay.IsProjectionPulseActive, TimeSpan.FromMilliseconds(500));
            PumpUntil(
                () => !overlay.IsProjectionValueTransitionActive,
                TimeSpan.FromMilliseconds(200));
            TestSupport.True(overlay.IsProjectionPulseActive, "first route active");
            TestSupport.False(
                overlay.IsProjectionValueTransitionActive,
                "first value transition settled");
            FrameworkElement sourceSegment =
                Element<FrameworkElement>(overlay, "ProjectionSourceHorizontalSegment");
            RectangleGeometry firstClip = TestSupport.NotNull(
                sourceSegment.Clip as RectangleGeometry,
                "first route clip");
            long pulseGenerationBeforeBurst = overlay.ProjectionPulseGeneration;
            long valueGenerationBeforeBurst = overlay.ProjectionValueGeneration;

            for (int count = 2; count <= 6; count++)
            {
                overlay.Snapshot = Snapshot(
                    count + 3,
                    StartupSequencePhase.Bind,
                    MotionLevel.Full,
                    count,
                    pollingVersion: 1);
            }

            TestSupport.True(overlay.IsProjectionPulseActive, "active route preserved");
            TestSupport.True(overlay.IsProjectionPulsePending, "latest update pending");
            TestSupport.True(
                overlay.IsProjectionValueTransitionActive,
                "current value transition preserved");
            TestSupport.True(
                overlay.IsProjectionValueTransitionPending,
                "latest value coalesced");
            TestSupport.Equal(6, overlay.LatestPendingResolvedCount, "latest pending count");
            TestSupport.Equal(6, overlay.LastPresentedResolvedCount, "value updates immediately");
            TestSupport.Equal(
                "2 / 6 RESOLVED",
                Element<TextBlock>(overlay, "ProjectionCurrentValue").Text,
                "active target is not replaced");
            TestSupport.True(
                ReferenceEquals(firstClip, sourceSegment.Clip),
                "new snapshots do not replace active route");

            PumpUntil(
                () => Element<TextBlock>(overlay, "ProjectionCurrentValue").Text
                    == "6 / 6 RESOLVED",
                TimeSpan.FromMilliseconds(500));
            TestSupport.Equal(
                "6 / 6 RESOLVED",
                Element<TextBlock>(overlay, "ProjectionCurrentValue").Text,
                "coalesced latest target");
            PumpUntil(
                () => !overlay.IsProjectionValueTransitionActive,
                TimeSpan.FromMilliseconds(500));
            TestSupport.False(
                overlay.IsProjectionValueTransitionActive,
                "coalesced value settled");
            TestSupport.False(
                overlay.IsProjectionValueTransitionPending,
                "no third value replay");
            TestSupport.Equal(6, overlay.DisplayedProjectionResolvedCount, "stable final value");
            TestSupport.Equal(
                valueGenerationBeforeBurst + 2,
                overlay.ProjectionValueGeneration,
                "one current plus one coalesced value transition");
            TestSupport.Equal(
                string.Empty,
                Element<TextBlock>(overlay, "ProjectionPreviousValue").Text,
                "previous layer cleared");

            PumpUntil(
                () => !ReferenceEquals(firstClip, sourceSegment.Clip),
                TimeSpan.FromMilliseconds(800));
            TestSupport.False(overlay.IsProjectionPulsePending, "pending consumed once");
            TestSupport.Equal(
                pulseGenerationBeforeBurst + 1,
                overlay.ProjectionPulseGeneration,
                "exactly one coalesced pulse replay");
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
                205,
                StartupMilestoneRow.FullRouteRowIntervalMilliseconds,
                "Full row interval");
            TestSupport.Equal(
                120,
                StartupMilestoneRow.StandardRouteRowIntervalMilliseconds,
                "Standard row interval");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(1220),
                StartupSequenceService.ResolveTraceworkRouteDuration(MotionLevel.Full),
                "Full route duration");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(720),
                StartupSequenceService.ResolveTraceworkRouteDuration(MotionLevel.Standard),
                "Standard route duration");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(4500),
                StartupSequenceService.ResolveTraceworkHardCutoff(MotionLevel.Full),
                "Full hard cutoff");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(3620),
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
            RectangleGeometry secondUpper = TestSupport.NotNull(
                Element<FrameworkElement>(rows[1], "UpperRouteSegment").Clip
                    as RectangleGeometry,
                "next upper committed clip");
            TestSupport.Equal(0d, secondUpper.Rect.Height, "next upper hidden before arrival");
            PumpUntil(
                () => secondUpper.Rect.Height >= 16.999d,
                TimeSpan.FromMilliseconds(500));
            TestSupport.Equal(17d, secondUpper.Rect.Height, "next upper visible after connection");
            TestSupport.True(
                Element<FrameworkElement>(rows[1], "MilestoneNode").Opacity > 0d,
                "next node follows completed connection");

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

    private static void VerifyRevealLateSnapshot() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 6);
            PumpUntil(
                () => Element<TextBlock>(overlay, "ProjectionCurrentValue").Text
                    == "6 / 6 RESOLVED",
                TimeSpan.FromMilliseconds(500));
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Lock,
                MotionLevel.Full,
                6,
                pollingVersion: 1) with { CanCommit = true };
            overlay.Snapshot = Snapshot(
                5,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                6,
                pollingVersion: 1) with { CanCommit = true };
            PumpUntil(
                () => overlay.IsRevealVisualStateEntered,
                TimeSpan.FromMilliseconds(500));
            TestSupport.True(overlay.IsRevealVisualStateEntered, "Reveal state entered");

            FrameworkElement background =
                Element<FrameworkElement>(overlay, "StartupBackgroundLayer");
            FrameworkElement content =
                Element<FrameworkElement>(overlay, "StartupContentLayer");
            FrameworkElement rail =
                Element<FrameworkElement>(overlay, "StartupBottomRailLayer");
            PumpUntil(
                () => background.Opacity < 0.99d
                    && content.Opacity < 0.99d
                    && rail.Opacity < 0.99d,
                TimeSpan.FromMilliseconds(300));
            double backgroundOpacity = background.Opacity;
            double contentOpacity = content.Opacity;
            double railOpacity = rail.Opacity;
            TestSupport.True(backgroundOpacity < 1d, "background is exiting");
            TestSupport.True(contentOpacity < 1d, "content is exiting");
            TestSupport.True(railOpacity < 1d, "rail is exiting");
            TestSupport.True(content.HasAnimatedProperties, "exit clock active");

            overlay.Snapshot = Snapshot(
                6,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                6,
                pollingVersion: 1) with { CanCommit = true };
            TestSupport.True(
                background.Opacity <= backgroundOpacity + 0.02d,
                "same-phase snapshot cannot restore background");
            TestSupport.True(
                content.Opacity <= contentOpacity + 0.02d,
                "same-phase snapshot cannot restore content");
            TestSupport.True(content.HasAnimatedProperties, "same exit clock remains");

            overlay.Snapshot = Snapshot(
                7,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                2,
                pollingVersion: 2) with { CanCommit = true };
            TestSupport.True(
                background.Opacity <= backgroundOpacity + 0.02d,
                "late projection cannot restore background");
            TestSupport.True(
                content.Opacity <= contentOpacity + 0.02d,
                "late projection cannot restore content");
            TestSupport.Equal(
                "2 / 6 RESOLVED",
                Element<TextBlock>(overlay, "ProjectionCurrentValue").Text,
                "late snapshot may update final text");

            overlay.Snapshot = Snapshot(
                8,
                StartupSequencePhase.Complete,
                MotionLevel.Full,
                2,
                pollingVersion: 2) with
            {
                IsActive = false,
                HasCompleted = true
            };
            TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "Complete collapses overlay");
        });

    private static void VerifyDelayedClips()
    {
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full, 0);
            overlay.UpdateLayout();
            RectangleGeometry bottomClip = TestSupport.NotNull(
                Element<FrameworkElement>(overlay, "StartupBottomRailLayer").Clip
                    as RectangleGeometry,
                "bottom rail entry clip");
            TestSupport.Equal(0d, bottomClip.Rect.Width, "bottom clip hidden at time zero");
            TestSupport.Equal(
                Visibility.Collapsed,
                Element<FrameworkElement>(Rows(overlay)[3], "RouteOutputPort").Visibility,
                "source collapsed in Index");

            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Full, 0);
            StartupMilestoneRow[] rows = Rows(overlay);
            FrameworkElement sourcePort =
                Element<FrameworkElement>(rows[3], "RouteOutputPort");
            TestSupport.Equal(Visibility.Visible, sourcePort.Visibility, "source visible in Route");
            TestSupport.Equal(
                0d,
                (double)sourcePort.GetAnimationBaseValue(UIElement.OpacityProperty),
                "source base remains hidden before SENSOR BUS arrival");
            RectangleGeometry secondUpper = TestSupport.NotNull(
                Element<FrameworkElement>(rows[1], "UpperRouteSegment").Clip
                    as RectangleGeometry,
                "second upper clip");
            RectangleGeometry secondLower = TestSupport.NotNull(
                Element<FrameworkElement>(rows[1], "LowerRouteSegment").Clip
                    as RectangleGeometry,
                "second lower clip");
            TestSupport.Equal(0d, secondUpper.Rect.Height, "delayed upper remains hidden");
            TestSupport.Equal(0d, secondLower.Rect.Height, "delayed lower remains hidden");
            Pump(TimeSpan.FromMilliseconds(300));
            TestSupport.Equal(17d, secondUpper.Rect.Height, "upper commits final state");
            TestSupport.False(secondUpper.HasAnimatedProperties, "upper clock cleared");
            Pump(TimeSpan.FromMilliseconds(1150));
            TestSupport.Equal(17d, secondLower.Rect.Height, "lower commits final state");
            TestSupport.False(secondLower.HasAnimatedProperties, "lower clock cleared");
            PumpUntil(
                () => sourcePort.Opacity >= 0.349d,
                TimeSpan.FromMilliseconds(300));
            TestSupport.True(sourcePort.Opacity >= 0.349d, "source appears at SENSOR BUS");
            TestSupport.Equal(
                Element<FrameworkElement>(overlay, "StartupBottomRailLayer").ActualWidth,
                bottomClip.Rect.Width,
                "bottom clip commits final width");
            TestSupport.False(bottomClip.HasAnimatedProperties, "bottom clip clock cleared");

            overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, MotionLevel.Full, 0);
            sourcePort = Element<FrameworkElement>(
                Rows(overlay)[3],
                "RouteOutputPort");
            FrameworkElement targetPort =
                Element<FrameworkElement>(overlay, "ProjectionInputPort");
            TestSupport.Equal(0d, targetPort.Opacity, "target hidden at Bind entry");
            PumpUntil(
                () => sourcePort.Opacity >= 0.999d && targetPort.Opacity >= 0.999d,
                TimeSpan.FromMilliseconds(400));
            TestSupport.Equal(1d, sourcePort.Opacity, "source activates in Bind");
            TestSupport.Equal(1d, targetPort.Opacity, "target activates at ledger Ready");
        });

        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 1);
            PumpUntil(() => overlay.IsProjectionPulseActive, TimeSpan.FromMilliseconds(500));
            TestSupport.True(overlay.IsProjectionPulseActive, "projection pulse started");
            RectangleGeometry vertical = TestSupport.NotNull(
                Element<FrameworkElement>(overlay, "ProjectionVerticalBridgeSegment").Clip
                    as RectangleGeometry,
                "vertical clip");
            RectangleGeometry target = TestSupport.NotNull(
                Element<FrameworkElement>(overlay, "ProjectionTargetHorizontalSegment").Clip
                    as RectangleGeometry,
                "target clip");
            TestSupport.Equal(
                0d,
                ((Rect)vertical.GetAnimationBaseValue(
                    RectangleGeometry.RectProperty)).Height,
                "vertical base hidden during delay");
            TestSupport.Equal(
                0d,
                ((Rect)target.GetAnimationBaseValue(
                    RectangleGeometry.RectProperty)).Width,
                "target base hidden during delay");
            TestSupport.True(vertical.HasAnimatedProperties, "vertical owns delayed clock");
            TestSupport.True(target.HasAnimatedProperties, "target owns delayed clock");
        });

        VerifyBottomRailAndReduced();
    }

    private static void VerifyPollingVersionReset() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareProjectionBind(
                overlay,
                MotionLevel.Full,
                5,
                pollingVersion: 10);
            Pump(TimeSpan.FromMilliseconds(350));
            TestSupport.Equal(5, overlay.DisplayedProjectionResolvedCount, "version 10 stable");
            long oldPulseGeneration = overlay.ProjectionPulseGeneration;

            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                1,
                pollingVersion: 11);
            TestSupport.Equal(0, overlay.DisplayedProjectionResolvedCount, "new version resets baseline");
            TestSupport.True(overlay.IsProjectionValueTransitionActive, "new version animates from zero");
            TestSupport.True(
                overlay.ProjectionPulseGeneration > oldPulseGeneration,
                "new version invalidates old pulse generation");

            overlay.Snapshot = Snapshot(
                5,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                6,
                pollingVersion: 10);
            TestSupport.Equal(
                "1 / 6 RESOLVED",
                Element<TextBlock>(overlay, "ProjectionCurrentValue").Text,
                "late old version ignored");
            Pump(TimeSpan.FromMilliseconds(200));
            TestSupport.Equal(1, overlay.DisplayedProjectionResolvedCount, "version 11 first target");

            overlay.Snapshot = Snapshot(
                6,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                3,
                pollingVersion: 11);
            Pump(TimeSpan.FromMilliseconds(200));
            TestSupport.Equal(3, overlay.DisplayedProjectionResolvedCount, "version 11 continues");
        });

    private static void VerifyProjectionCommit()
    {
        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 1);
            PumpUntil(() => overlay.IsProjectionPulseActive, TimeSpan.FromMilliseconds(500));
            TestSupport.True(overlay.IsProjectionPulseActive, "pulse active before Lock");
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Lock,
                MotionLevel.Full,
                1,
                pollingVersion: 1) with { CanCommit = true };
            FrameworkElement commit = Element<FrameworkElement>(overlay, "CommitGroup");
            TestSupport.True(overlay.IsCommitPendingForProjection, "commit waits for pulse");
            TestSupport.Equal(Visibility.Collapsed, commit.Visibility, "commit withheld");
            Pump(TimeSpan.FromMilliseconds(700));
            TestSupport.False(overlay.IsProjectionPulseActive, "pulse completed");
            TestSupport.False(overlay.IsCommitPendingForProjection, "commit deferral consumed");
            TestSupport.Equal(Visibility.Visible, commit.Visibility, "commit follows pulse");

            overlay.Snapshot = Snapshot(
                5,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                1,
                pollingVersion: 1) with { CanCommit = true };
            TestSupport.False(overlay.IsProjectionPulseActive, "Reveal has no active pulse");
        });

        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareProjectionBind(overlay, MotionLevel.Full, 1);
            PumpUntil(() => overlay.IsProjectionPulseActive, TimeSpan.FromMilliseconds(500));
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                1,
                pollingVersion: 1);
            TestSupport.False(overlay.IsProjectionPulseActive, "Reveal fallback stops abnormal pulse");
            TestSupport.Equal(
                0d,
                Element<FrameworkElement>(overlay, "ProjectionPulseCanvas").Opacity,
                "Reveal fallback clears pulse canvas");
        });
    }

    private static async Task VerifyReadinessSettleAsync()
    {
        StepClock clock = new();
        using StartupSequenceService service = CreateSettleService(clock);
        Task sequence = service.StartAsync();
        await clock.ReleaseNextAsync();
        await clock.ReleaseNextAsync();
        await clock.ReleaseNextAsync();
        await clock.ReleaseNextAsync();
        await clock.WaitForDelayCountAsync(5);
        TestSupport.False(service.CurrentSnapshot.CanCommit, "settle begins unresolved");

        service.ReportMilestone(
            StartupMilestoneId.SensorBus,
            StartupMilestoneState.Ready,
            "late real sample");
        service.ReportInitialProjection(Projection(2, 6));
        service.ReportPostDataLayout(2);
        await clock.ReleaseNextAsync();
        await clock.ReleaseNextAsync();
        await clock.ReleaseNextAsync();
        await sequence;
        StartupMilestoneSnapshot sensor = service.CurrentSnapshot.Milestones.Single(
            item => item.Id == StartupMilestoneId.SensorBus);
        TestSupport.Equal(StartupMilestoneState.Ready, sensor.State, "late data avoids Partial");
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "late-ready sequence completes");
        TestSupport.Equal(
            TimeSpan.FromMilliseconds(180),
            StartupSequenceService.ResolveReadinessSettleDuration(MotionLevel.Full),
            "Full settle duration");

        StepClock timeoutClock = new();
        using StartupSequenceService timeoutService = CreateSettleService(timeoutClock);
        Task timeoutSequence = timeoutService.StartAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutClock.ReleaseNextAsync();
        await timeoutSequence;
        StartupMilestoneSnapshot timedOutSensor =
            timeoutService.CurrentSnapshot.Milestones.Single(
                item => item.Id == StartupMilestoneId.SensorBus);
        TestSupport.Equal(
            StartupMilestoneState.Partial,
            timedOutSensor.State,
            "Partial only after settle expires");
    }

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
            full.Snapshot = Snapshot(
                2,
                StartupSequencePhase.Route,
                MotionLevel.Full,
                0);
            full.Snapshot = Snapshot(
                3,
                StartupSequencePhase.Bind,
                MotionLevel.Full,
                0);
            TestSupport.Equal(3, full.PendingBottomPhaseCount, "monotonic phases queued");
            PumpUntil(() => full.IsBottomRailReady, TimeSpan.FromMilliseconds(400));
            TestSupport.True(full.IsBottomRailReady, "rail ready after visible entry");
            TestSupport.Equal(
                "01 / 05  构建系统索引",
                phaseText.Text,
                "Index phase plays after entry");
            RectangleGeometry firstClip = TestSupport.NotNull(
                indexSegment.Clip as RectangleGeometry,
                "Index track reveal");
            full.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Bind,
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

        VerifyBottomRailWithService();
    }

    private static void VerifyBottomRailWithService() =>
        WithOverlay(1120d, 720d, overlay =>
        {
            StepClock clock = new();
            using StartupSequenceService service = CreateReadyService(clock);
            service.SnapshotChanged += (_, args) =>
                overlay.Dispatcher.BeginInvoke(
                    new Action(() => overlay.Snapshot = args.CurrentSnapshot));
            Task sequence = service.StartAsync();
            PumpUntil(
                () => overlay.Snapshot?.Phase == StartupSequencePhase.Index,
                TimeSpan.FromMilliseconds(500));
            PumpUntil(() => overlay.IsBottomRailReady, TimeSpan.FromMilliseconds(400));
            TestSupport.Equal(
                "01 / 05  构建系统索引",
                Element<TextBlock>(overlay, "BottomCurrentPhaseText").Text,
                "real service presents Index");
            TestSupport.Equal(
                TimeSpan.FromMilliseconds(120),
                TraceworkStartupSequenceOverlay.ResolveBottomPhaseMinimumVisibleDuration(
                    MotionLevel.Full,
                    StartupSequencePhase.Index),
                "Full minimum Index visibility");

            clock.ReleaseNextAsync().GetAwaiter().GetResult();
            PumpUntil(
                () => overlay.Snapshot?.Phase == StartupSequencePhase.Route,
                TimeSpan.FromMilliseconds(500));
            TestSupport.Equal(
                StartupSequencePhase.Route,
                overlay.Snapshot!.Phase,
                "service remains in Route until route clock release");

            for (int remaining = 0; remaining < 4; remaining++)
            {
                clock.ReleaseNextAsync().GetAwaiter().GetResult();
                Pump(TimeSpan.FromMilliseconds(20));
            }
            PumpUntil(
                () => service.CurrentSnapshot.HasCompleted,
                TimeSpan.FromMilliseconds(500));
            sequence.GetAwaiter().GetResult();
            TestSupport.True(service.CurrentSnapshot.HasCompleted, "real service lifecycle completes");
        });

    private static void PrepareProjectionBind(
        TraceworkStartupSequenceOverlay overlay,
        MotionLevel level,
        int projectionCount,
        long pollingVersion = 1)
    {
        overlay.Snapshot = Snapshot(
            1,
            StartupSequencePhase.Index,
            level,
            projectionCount,
            pollingVersion: pollingVersion);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
        overlay.Snapshot = Snapshot(
            2,
            StartupSequencePhase.Route,
            level,
            projectionCount,
            pollingVersion: pollingVersion);
        overlay.Snapshot = Snapshot(
            3,
            StartupSequencePhase.Bind,
            level,
            projectionCount,
            pollingVersion: pollingVersion);
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

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(20));
        }
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
        StartupMilestoneState terminalState = StartupMilestoneState.Wait,
        long pollingVersion = 1)
    {
        StartupInitialProjectionSnapshot projection = new(
            pollingVersion,
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

    private static StartupSequenceService CreateSettleService(StepClock clock)
    {
        StartupSequenceService service = new(
            AppTheme.Tracework,
            MotionLevel.Full,
            clock,
            clock);
        foreach (StartupMilestoneId id in Enum.GetValues<StartupMilestoneId>())
        {
            if (id == StartupMilestoneId.SensorBus
                || id == StartupMilestoneId.ShellSurface)
            {
                continue;
            }

            service.ReportMilestone(id, StartupMilestoneState.Ready, "ready");
        }

        service.ReportMilestone(
            StartupMilestoneId.SensorBus,
            StartupMilestoneState.Pending,
            "waiting");
        service.ReportSurfaceReady(1120d, 720d, "ready");
        service.ReportInitialProjection(Projection(1, 0));
        return service;
    }

    private static StartupSequenceService CreateReadyService(StepClock clock)
    {
        StartupSequenceService service = new(
            AppTheme.Tracework,
            MotionLevel.Full,
            clock,
            clock);
        foreach (StartupMilestoneId id in Enum.GetValues<StartupMilestoneId>())
        {
            if (id == StartupMilestoneId.ShellSurface)
            {
                continue;
            }

            service.ReportMilestone(id, StartupMilestoneState.Ready, "ready");
        }

        service.ReportSurfaceReady(1120d, 720d, "ready");
        service.ReportInitialProjection(Projection(1, 6));
        service.ReportPostDataLayout(1);
        return service;
    }

    private static StartupInitialProjectionSnapshot Projection(
        long pollingVersion,
        int resolvedCount) =>
        new(
            pollingVersion,
            Enum.GetValues<HardwareOverviewKind>()
                .Where(kind => kind is HardwareOverviewKind.Cpu
                    or HardwareOverviewKind.Gpu
                    or HardwareOverviewKind.Memory
                    or HardwareOverviewKind.Disk
                    or HardwareOverviewKind.Network
                    or HardwareOverviewKind.System)
                .Select((kind, index) => new StartupProjectionSlotSnapshot(
                    kind,
                    index < resolvedCount
                        ? StartupProjectionState.Value
                        : StartupProjectionState.Pending,
                    index < resolvedCount ? "resolved" : "pending"))
                .ToArray(),
            DispatcherApplied: resolvedCount == 6,
            PostDataLayoutObserved: false);

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
    }

    private sealed class StepClock : IStartupSequenceClock
    {
        private readonly object sync = new();
        private readonly Queue<TaskCompletionSource> pending = new();
        private int delayCount;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TaskCompletionSource completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync)
            {
                delayCount++;
                pending.Enqueue(completion);
            }
            return completion.Task.WaitAsync(cancellationToken);
        }

        public async Task ReleaseNextAsync()
        {
            TaskCompletionSource? completion = null;
            for (int attempt = 0; attempt < 1000 && completion is null; attempt++)
            {
                lock (sync)
                {
                    if (pending.Count > 0)
                    {
                        completion = pending.Dequeue();
                    }
                }

                if (completion is null)
                {
                    await Task.Delay(1);
                }
            }

            TestSupport.NotNull(completion, "pending startup delay").TrySetResult();
            await Task.Yield();
        }

        public async Task WaitForDelayCountAsync(int expected)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                lock (sync)
                {
                    if (delayCount >= expected)
                    {
                        return;
                    }
                }

                await Task.Delay(1);
            }

            throw new InvalidOperationException(
                $"Startup delay count did not reach {expected}.");
        }
    }
}
