using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class StartupProjectionRaceTests
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(4);

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup Projection race late projection after Lock",
            LateProjectionAfterLock),
        ("Startup Projection race same turn Projection first",
            () => SameDispatcherTurn(projectionFirst: true)),
        ("Startup Projection race same turn Lock first",
            () => SameDispatcherTurn(projectionFirst: false)),
        ("Startup Projection race pending layout crosses Bind to Lock",
            PendingLayoutCrossesBindToLock),
        ("Startup Projection race visible frame and pixel evidence",
            VisibleFrameAndPixelEvidence)
    ];

    private static void LateProjectionAfterLock() =>
        WithOverlay(MotionLevel.Standard, scope =>
        {
            scope.PrimeToBind();
            long lockVersion = scope.Publish(
                StartupSequencePhase.Lock,
                StartupInitialProjectionSnapshot.Pending,
                canCommit: true);

            StartupInitialProjectionSnapshot request =
                ResolvedProjection(101, postDataLayoutObserved: false);
            long requestVersion = scope.Publish(
                StartupSequencePhase.Lock,
                request,
                canCommit: true);
            long generationAfterRequest = ReadField<long>(
                scope.Overlay, "projectionRequestGeneration");
            TestSupport.True(
                scope.Overlay.IsProjectionPulsePending,
                "late Lock Projection request remains latched");

            StartupInitialProjectionSnapshot postLayout =
                request with { PostDataLayoutObserved = true };
            long postLayoutVersion = scope.Publish(
                StartupSequencePhase.Lock,
                postLayout,
                canCommit: true);

            TestSupport.True(
                lockVersion < requestVersion && requestVersion < postLayoutVersion,
                "late request snapshots preserve Lock then request then post-layout order");
            TestSupport.Equal(
                generationAfterRequest + 1,
                ReadField<long>(scope.Overlay, "projectionRequestGeneration"),
                "same pollingVersion post-layout updates the latched request once");
            AssertProjectionCompletesBeforeCommit(
                scope,
                postLayoutVersion,
                postLayout.PollingVersion,
                "late Lock");
        });

    private static void SameDispatcherTurn(bool projectionFirst) =>
        WithOverlay(MotionLevel.Standard, scope =>
        {
            scope.PrimeToBind();
            StartupInitialProjectionSnapshot projection =
                ResolvedProjection(
                    projectionFirst ? 201 : 202,
                    postDataLayoutObserved: true);

            if (projectionFirst)
            {
                scope.Publish(
                    StartupSequencePhase.Bind,
                    projection,
                    canCommit: true);
                scope.Publish(
                    StartupSequencePhase.Lock,
                    projection,
                    canCommit: true);
            }
            else
            {
                scope.Publish(
                    StartupSequencePhase.Lock,
                    StartupInitialProjectionSnapshot.Pending,
                    canCommit: true);
                scope.Publish(
                    StartupSequencePhase.Lock,
                    projection,
                    canCommit: true);
            }

            TestSupport.True(
                scope.Overlay.IsProjectionPulsePending
                    || scope.Overlay.IsProjectionPulseActive,
                $"{OrderName(projectionFirst)} request is latched in the same Dispatcher turn");
            AssertProjectionCompletesBeforeCommit(
                scope,
                scope.Overlay.Snapshot!.Version,
                projection.PollingVersion,
                OrderName(projectionFirst));
        });

    private static void PendingLayoutCrossesBindToLock() =>
        WithOverlay(MotionLevel.Standard, scope =>
        {
            scope.PrimeToBind();
            FrameworkElement targetAnchor = Element<FrameworkElement>(
                scope.Overlay, "ProjectionInputAnchor");
            double originalWidth = targetAnchor.Width;
            try
            {
                targetAnchor.Width = 0d;
                scope.Host.UpdateLayout();

                StartupInitialProjectionSnapshot request =
                    ResolvedProjection(301, postDataLayoutObserved: false);
                long bindVersion = scope.Publish(
                    StartupSequencePhase.Bind,
                    request,
                    canCommit: false);
                long requestGeneration = ReadField<long>(
                    scope.Overlay, "projectionRequestGeneration");
                TestSupport.True(
                    scope.Overlay.IsProjectionPulsePending,
                    "layout-pending Bind request is latched");

                long lockVersion = scope.Publish(
                    StartupSequencePhase.Lock,
                    request,
                    canCommit: true);
                TestSupport.Equal(
                    requestGeneration,
                    ReadField<long>(scope.Overlay, "projectionRequestGeneration"),
                    "Bind to Lock does not replace an unchanged request");

                StartupInitialProjectionSnapshot postLayout =
                    request with { PostDataLayoutObserved = true };
                long postLayoutVersion = scope.Publish(
                    StartupSequencePhase.Lock,
                    postLayout,
                    canCommit: true);
                TestSupport.Equal(
                    requestGeneration + 1,
                    ReadField<long>(scope.Overlay, "projectionRequestGeneration"),
                    "post-layout updates the existing request generation once");
                TestSupport.Equal(
                    "WaitingForAnchorLayout",
                    ReadRequestState(scope.Overlay),
                    "unready anchor enters the layout wait state");
                TestSupport.True(
                    scope.Overlay.IsProjectionPulsePending,
                    "request stays pending across Bind to Lock");
                TestSupport.True(
                    scope.Overlay.IsCommitPendingForProjection,
                    "pending layout blocks COMMIT");

                _ = scope.Host.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() =>
                    {
                        targetAnchor.Width = originalWidth;
                        scope.Host.UpdateLayout();
                    }));

                TestSupport.True(
                    bindVersion < lockVersion && lockVersion < postLayoutVersion,
                    "layout crossing snapshot versions are monotonic");
                TestSupport.Equal(
                    scope.StartedAt,
                    scope.Overlay.Snapshot!.StartedAt,
                    "layout retry preserves startup identity");
                AssertProjectionCompletesBeforeCommit(
                    scope,
                    postLayoutVersion,
                    postLayout.PollingVersion,
                    "pending layout");
            }
            finally
            {
                targetAnchor.Width = originalWidth;
            }
        });

    private static void VisibleFrameAndPixelEvidence() =>
        WithOverlay(MotionLevel.Full, scope =>
        {
            scope.PrimeToBind();
            StartupInitialProjectionSnapshot projection =
                ResolvedProjection(401, postDataLayoutObserved: true);
            scope.Publish(
                StartupSequencePhase.Bind,
                projection,
                canCommit: true);
            scope.Publish(
                StartupSequencePhase.Lock,
                projection,
                canCommit: true);

            PumpUntil(
                () => scope.Overlay.IsProjectionPulseActive
                    && scope.Overlay.LastProjectionRoute is not null,
                ObservationTimeout,
                "pixel evidence pulse starts with resolved geometry");

            FrameworkElement canvas = Element<FrameworkElement>(
                scope.Overlay, "ProjectionPulseCanvas");
            FrameworkElement source = Element<FrameworkElement>(
                scope.Overlay, "ProjectionSourceHorizontalSegment");
            FrameworkElement vertical = Element<FrameworkElement>(
                scope.Overlay, "ProjectionVerticalBridgeSegment");
            FrameworkElement target = Element<FrameworkElement>(
                scope.Overlay, "ProjectionTargetHorizontalSegment");
            FrameworkElement head = Element<FrameworkElement>(
                scope.Overlay, "ProjectionPulseHead");
            List<double> partialClipExtents = [];
            List<int> telemetryPixelCounts = [];
            List<int> headPixelCounts = [];

            DateTime deadline = DateTime.UtcNow + ObservationTimeout;
            while (!scope.Overlay.ProjectionPulseCompletedAt.HasValue
                && DateTime.UtcNow < deadline)
            {
                CapturePartialClip(source, partialClipExtents);
                CapturePartialClip(vertical, partialClipExtents);
                CapturePartialClip(target, partialClipExtents);
                RenderTargetBitmap bitmap = Render(canvas);
                telemetryPixelCounts.Add(CountVisiblePixels(bitmap));
                headPixelCounts.Add(CountHeadPixels(bitmap, head));
                Pump(TimeSpan.FromMilliseconds(8), DispatcherPriority.Background);
            }

            AssertProjectionCompletesBeforeCommit(
                scope,
                scope.Overlay.Snapshot!.Version,
                projection.PollingVersion,
                "pixel evidence");
            TestSupport.True(
                scope.Overlay.LastProjectionRoute?.TotalRouteLength > 24d,
                "projection route exceeds 24 DIP");
            TestSupport.True(
                partialClipExtents.Count > 0,
                "at least one route segment owns a partial Clip");
            TestSupport.True(
                partialClipExtents
                    .Select(value => Math.Round(value, 2))
                    .Distinct()
                    .Count() >= 2,
                "at least two distinct partial Clip extents were rendered");
            TestSupport.True(
                telemetryPixelCounts.Any(count => count >= 2),
                "route contains non-background telemetry pixels");
            TestSupport.True(
                headPixelCounts.Any(count => count >= 4),
                "Full motion Pulse Head contains non-background pixels");
        });

    private static void AssertProjectionCompletesBeforeCommit(
        ProjectionTestScope scope,
        long expectedSnapshotVersion,
        long expectedPollingVersion,
        string label)
    {
        TraceworkStartupSequenceOverlay overlay = scope.Overlay;
        PumpUntil(
            () => overlay.IsProjectionPulseActive
                || overlay.ProjectionPulseCompletedAt.HasValue,
            ObservationTimeout,
            $"{label} Projection request starts a route");
        PumpUntil(
            () => overlay.IsProjectionPulseVisibleFrameCommitted,
            ObservationTimeout,
            $"{label} Projection commits a visible frame");
        PumpUntil(
            () => overlay.ProjectionPulseCompletedAt.HasValue
                && overlay.CommitVisualStartedAt.HasValue,
            ObservationTimeout,
            $"{label} Projection completes before COMMIT starts");

        DateTimeOffset completedAt = overlay.ProjectionPulseCompletedAt!.Value;
        DateTimeOffset commitAt = overlay.CommitVisualStartedAt!.Value;
        TestSupport.True(
            completedAt < commitAt,
            $"{label} completion strictly precedes COMMIT");
        DateTimeOffset? visibleTimestamp = ReadTimestamp(
            overlay, "ProjectionPulseVisibleFrameCommittedAt");
        TestSupport.True(
            visibleTimestamp.HasValue,
            $"{label} visible frame timestamp is recorded");
        DateTimeOffset visibleAt = visibleTimestamp!.Value;
        TestSupport.True(
            visibleAt <= completedAt,
            $"{label} visible frame timestamp precedes completion");
        TestSupport.Equal(
            expectedSnapshotVersion,
            overlay.Snapshot!.Version,
            $"{label} snapshot version is preserved");
        TestSupport.Equal(
            scope.StartedAt,
            overlay.Snapshot.StartedAt,
            $"{label} startup identity is preserved");
        TestSupport.Equal(
            expectedPollingVersion,
            overlay.Snapshot.InitialProjection.PollingVersion,
            $"{label} pollingVersion is preserved");
        TestSupport.Equal(
            expectedPollingVersion,
            ReadField<long>(overlay, "pendingProjectionPollingVersion"),
            $"{label} latched pollingVersion is preserved");
        TestSupport.Equal(
            "Completed",
            ReadRequestState(overlay),
            $"{label} completes without ProjectionPulseVisualTimeout");

        long commitGeneration = ReadField<long>(
            overlay, "commitPresentationGeneration");
        DateTimeOffset firstCommitAt = commitAt;
        Pump(TimeSpan.FromMilliseconds(60), DispatcherPriority.Background);
        TestSupport.Equal(
            commitGeneration,
            ReadField<long>(overlay, "commitPresentationGeneration"),
            $"{label} COMMIT starts exactly once");
        TestSupport.Equal(
            firstCommitAt,
            overlay.CommitVisualStartedAt!.Value,
            $"{label} COMMIT timestamp is not replaced");
    }

    private static void WithOverlay(
        MotionLevel motionLevel,
        Action<ProjectionTestScope> assertion)
    {
        EnsureApplication();
        using ProjectionTestScope scope = new(motionLevel);
        scope.Show();
        assertion(scope);
    }

    private static StartupInitialProjectionSnapshot ResolvedProjection(
        long pollingVersion,
        bool postDataLayoutObserved) =>
        new(
            pollingVersion,
            Enum.GetValues<HardwareOverviewKind>()
                .Select(kind => new StartupProjectionSlotSnapshot(
                    kind,
                    StartupProjectionState.Value,
                    "projection race"))
                .ToArray(),
            DispatcherApplied: true,
            PostDataLayoutObserved: postDataLayoutObserved);

    private static T Element<T>(FrameworkElement root, string name)
        where T : FrameworkElement =>
        TestSupport.NotNull(root.FindName(name) as T, name);

    private static T ReadField<T>(object target, string fieldName)
    {
        FieldInfo field = TestSupport.NotNull(
            target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic),
            fieldName);
        object? value = field.GetValue(target);
        TestSupport.True(
            value is T,
            $"{fieldName} has expected type {typeof(T).Name}");
        return (T)value!;
    }

    private static DateTimeOffset? ReadTimestamp(
        object target,
        string propertyName)
    {
        PropertyInfo? property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        return property?.GetValue(target) as DateTimeOffset?;
    }

    private static string ReadRequestState(object target)
    {
        FieldInfo field = TestSupport.NotNull(
            target.GetType().GetField(
                "projectionRequestState",
                BindingFlags.Instance | BindingFlags.NonPublic),
            "projectionRequestState");
        return field.GetValue(target)?.ToString() ?? string.Empty;
    }

    private static void CapturePartialClip(
        FrameworkElement segment,
        ICollection<double> values)
    {
        if (segment.Visibility != Visibility.Visible
            || segment.Opacity <= 0d
            || segment.Clip is not RectangleGeometry clip)
        {
            return;
        }

        double finalExtent = Math.Max(segment.ActualWidth, segment.ActualHeight);
        double visibleExtent = Math.Max(clip.Rect.Width, clip.Rect.Height);
        if (visibleExtent > 0.1d && visibleExtent < finalExtent - 0.1d)
        {
            values.Add(visibleExtent);
        }
    }

    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        RenderTargetBitmap bitmap = new(
            width,
            height,
            96d,
            96d,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static int CountVisiblePixels(RenderTargetBitmap bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        byte[] pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        int count = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] >= 24)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountHeadPixels(
        RenderTargetBitmap bitmap,
        FrameworkElement head)
    {
        TranslateTransform? translation =
            head.RenderTransform as TranslateTransform;
        double left = Canvas.GetLeft(head);
        double top = Canvas.GetTop(head);
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return 0;
        }

        Rect bounds = new(
            left + (translation?.X ?? 0d) - 1d,
            top + (translation?.Y ?? 0d) - 1d,
            head.ActualWidth + 2d,
            head.ActualHeight + 2d);
        int pixelLeft = Math.Clamp(
            (int)Math.Floor(bounds.Left), 0, bitmap.PixelWidth - 1);
        int pixelTop = Math.Clamp(
            (int)Math.Floor(bounds.Top), 0, bitmap.PixelHeight - 1);
        int pixelRight = Math.Clamp(
            (int)Math.Ceiling(bounds.Right), pixelLeft + 1, bitmap.PixelWidth);
        int pixelBottom = Math.Clamp(
            (int)Math.Ceiling(bounds.Bottom), pixelTop + 1, bitmap.PixelHeight);
        int width = pixelRight - pixelLeft;
        int height = pixelBottom - pixelTop;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bitmap.CopyPixels(
            new Int32Rect(pixelLeft, pixelTop, width, height),
            pixels,
            stride,
            0);
        int count = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] >= 24)
            {
                count++;
            }
        }

        return count;
    }

    private static void PumpUntil(
        Func<bool> condition,
        TimeSpan timeout,
        string message)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background);
        }

        TestSupport.True(condition(), message);
    }

    private static void Pump(TimeSpan duration, DispatcherPriority priority)
    {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(
            duration,
            priority,
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
        }

        Application.Current!.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private static string OrderName(bool projectionFirst) =>
        projectionFirst ? "Projection-first" : "Lock-first";

    private sealed class ProjectionTestScope : IDisposable
    {
        private long version;
        private bool disposed;
        private readonly MotionLevel motionLevel;

        public ProjectionTestScope(MotionLevel motionLevel)
        {
            this.motionLevel = motionLevel;
            StartedAt = DateTimeOffset.UtcNow;
            Overlay = new TraceworkStartupSequenceOverlay();
            Host = new Window
            {
                Content = Overlay,
                Width = 1120d,
                Height = 720d,
                ShowInTaskbar = false
            };
        }

        public DateTimeOffset StartedAt { get; }

        public TraceworkStartupSequenceOverlay Overlay { get; }

        public Window Host { get; }

        public void Show()
        {
            Host.Show();
            Host.ApplyTemplate();
            Overlay.ApplyTemplate();
            Host.UpdateLayout();
            Overlay.UpdateLayout();
            Host.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }

        public void PrimeToBind()
        {
            Publish(
                StartupSequencePhase.Index,
                StartupInitialProjectionSnapshot.Pending,
                canCommit: false,
                snapshotMotionLevel: MotionLevel.Full);
            Pump(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background);
            Publish(
                StartupSequencePhase.Route,
                StartupInitialProjectionSnapshot.Pending,
                canCommit: false,
                snapshotMotionLevel: MotionLevel.Full);
            Pump(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background);
            Publish(
                StartupSequencePhase.Bind,
                StartupInitialProjectionSnapshot.Pending,
                canCommit: false,
                snapshotMotionLevel: MotionLevel.Full);
            PumpUntil(
                () => Overlay.IsProjectionLedgerReady,
                ObservationTimeout,
                "Projection ledger is ready before the race");
            if (motionLevel != MotionLevel.Full)
            {
                Publish(
                    StartupSequencePhase.Bind,
                    StartupInitialProjectionSnapshot.Pending,
                    canCommit: false);
            }
        }

        public long Publish(
            StartupSequencePhase phase,
            StartupInitialProjectionSnapshot projection,
            bool canCommit,
            MotionLevel? snapshotMotionLevel = null)
        {
            long snapshotVersion = ++version;
            Overlay.Snapshot = StartupSequenceSnapshot.Dormant(
                AppTheme.Tracework,
                snapshotMotionLevel ?? motionLevel) with
            {
                Version = snapshotVersion,
                Phase = phase,
                IsActive = true,
                HasCompleted = false,
                StartedAt = StartedAt,
                Milestones = Enum.GetValues<StartupMilestoneId>()
                    .Select(id => new StartupMilestoneSnapshot(
                        id,
                        StartupMilestoneSnapshot.GetName(id),
                        StartupMilestoneState.Ready,
                        StartupMilestoneSnapshot.GetStatusText(
                            StartupMilestoneState.Ready),
                        "projection race ready"))
                    .ToArray(),
                ShellReady = true,
                SurfaceMeasured = true,
                FirstFrameGateReleased = true,
                FirstFrameGateReleaseReason = "ProjectionRaceCompositorReady",
                VisualReady = true,
                InitialProjection = projection,
                CanCommit = canCommit
            };
            return snapshotVersion;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Overlay.RestoreFinalState();
            Host.Content = null;
            Host.Close();
            Host.Dispatcher.Invoke(
                () => { },
                DispatcherPriority.ApplicationIdle);
        }
    }
}
