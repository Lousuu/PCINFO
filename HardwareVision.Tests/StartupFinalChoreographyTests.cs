using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Views.Shell;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;

namespace HardwareVision.Tests;

internal static class StartupFinalChoreographyTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"Startup Index Route Bottom Rail {iteration:00}/20", VerifyChoreographyRuntime));
            tests.Add(($"Startup Projection real-coordinate pulse {iteration:00}/20", VerifyProjectionRuntime));
        }

        return tests;
    }

    private static void VerifyChoreographyRuntime() => WithOverlay(1120d, 720d, overlay =>
    {
        overlay.Snapshot = Snapshot(
            1,
            StartupSequencePhase.Index,
            MotionLevel.Full,
            projectionCount: 0,
            terminalState: StartupMilestoneState.Ready);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();

        TextBlock title = Element<TextBlock>(overlay, "TraceworkTitleText");
        TextBlock subtitle = Element<TextBlock>(overlay, "StartupSubtitleText");
        FrameworkElement identity = Element<FrameworkElement>(overlay, "LedgerIdentityGroup");
        FrameworkElement environment = Element<FrameworkElement>(overlay, "LedgerEnvironmentGroup");
        FrameworkElement projection = Element<FrameworkElement>(overlay, "LedgerProjectionGroup");
        FrameworkElement rail = Element<FrameworkElement>(overlay, "StartupBottomRailLayer");
        TextBlock phaseText = Element<TextBlock>(overlay, "BottomCurrentPhaseText");
        TextBlock phaseCode = Element<TextBlock>(overlay, "BottomPhaseCode");

        TestSupport.Equal("TRACEWORK", title.Text, "title");
        TestSupport.Equal("启动中", subtitle.Text, "subtitle");
        TestSupport.True(title.HasAnimatedProperties, "title owns Index clock");
        TestSupport.True(subtitle.HasAnimatedProperties, "subtitle owns Index clock");
        TestSupport.True(identity.HasAnimatedProperties, "identity enters during Index");
        TestSupport.False(environment.HasAnimatedProperties, "environment waits for Route");
        TestSupport.False(projection.HasAnimatedProperties, "projection waits for Bind");
        TestSupport.True(rail.HasAnimatedProperties, "bottom rail enters during Index");
        TestSupport.Equal("01 / 05  构建系统索引", phaseText.Text, "Index phase text");
        TestSupport.Equal("INDEX", phaseCode.Text, "Index phase code");

        StartupMilestoneRow[] preparedRows = Rows(overlay);
        TestSupport.Equal(6, preparedRows.Length, "prepared row count");
        foreach (StartupMilestoneRow row in preparedRows)
        {
            RectangleGeometry upper = TestSupport.NotNull(
                Element<FrameworkElement>(row, "UpperRouteSegment").Clip as RectangleGeometry,
                "upper prepared clip");
            TestSupport.Equal(0d, upper.Rect.Height, "prepared segment height");
            TestSupport.Equal(0d, Element<FrameworkElement>(row, "MilestoneName").Opacity, "prepared name hidden");
        }

        overlay.Snapshot = Snapshot(
            2,
            StartupSequencePhase.Route,
            MotionLevel.Full,
            projectionCount: 0,
            terminalState: StartupMilestoneState.Ready);
        TestSupport.True(environment.HasAnimatedProperties, "environment enters during Route");
        TestSupport.Equal("02 / 05  接通核心服务", phaseText.Text, "Route phase text");
        TestSupport.Equal("ROUTE", phaseCode.Text, "Route phase code");
        StartupMilestoneRow first = Rows(overlay)[0];
        FrameworkElement routeName = Element<FrameworkElement>(first, "MilestoneName");
        FrameworkElement terminal = Element<FrameworkElement>(first, "TerminalLockFrame");
        TestSupport.True(routeName.HasAnimatedProperties, "route name reveal");
        TestSupport.True(terminal.HasAnimatedProperties, "pre-ready terminal arrival lock");

        terminal.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Snapshot = Snapshot(
            3,
            StartupSequencePhase.Route,
            MotionLevel.Full,
            projectionCount: 0,
            terminalState: StartupMilestoneState.Ready);
        terminal = Element<FrameworkElement>(Rows(overlay)[0], "TerminalLockFrame");
        TestSupport.False(terminal.HasAnimatedProperties, "same terminal state does not replay");

        overlay.Snapshot = Snapshot(
            4,
            StartupSequencePhase.Bind,
            MotionLevel.Full,
            projectionCount: 1,
            terminalState: StartupMilestoneState.Partial);
        terminal = Element<FrameworkElement>(Rows(overlay)[0], "TerminalLockFrame");
        TestSupport.True(terminal.HasAnimatedProperties, "later real terminal transition still feeds back");
        TestSupport.True(projection.HasAnimatedProperties, "projection group enters during Bind");
        TestSupport.Equal("03 / 05  建立硬件信号路由", phaseText.Text, "Bind phase text");
        TestSupport.Equal("BIND", phaseCode.Text, "Bind phase code");

        AssertPresentation(StartupSequencePhase.Index, 1, "INDEX", "构建系统索引");
        AssertPresentation(StartupSequencePhase.Route, 2, "ROUTE", "接通核心服务");
        AssertPresentation(StartupSequencePhase.Bind, 3, "BIND", "建立硬件信号路由");
        AssertPresentation(StartupSequencePhase.Lock, 4, "LOCK", "锁定首个遥测快照");
        AssertPresentation(StartupSequencePhase.Reveal, 5, "REVEAL", "提交主界面");
        StartupPhasePresentation failed = TestSupport.NotNull(
            StartupPhasePresentation.Create(StartupSequencePhase.Reveal, "telemetry timeout"),
            "failure presentation");
        TestSupport.Equal("FAILED", failed.PhaseCode, "failure code");
        TestSupport.Equal("启动降级：telemetry timeout", failed.DisplayText, "failure text");
        TestSupport.True(
            StartupPhasePresentation.Create(StartupSequencePhase.Dormant, null) is null,
            "Dormant hides rail content");

        overlay.Snapshot = Snapshot(
            5,
            StartupSequencePhase.Reveal,
            MotionLevel.Full,
            projectionCount: 1,
            terminalState: StartupMilestoneState.Partial) with
        {
            FailureMessage = "telemetry timeout"
        };
        TestSupport.Equal("启动降级：telemetry timeout", phaseText.Text, "runtime failure text");
        TestSupport.Equal("FAILED", phaseCode.Text, "runtime failure code");
        VerifyChoreographyMotionVariants();
    });

    private static void VerifyProjectionRuntime() => WithOverlay(1120d, 720d, (overlay, host) =>
    {
        overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full, 1);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Full, 1);
        overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, MotionLevel.Full, 1);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
        FrameworkElement coordinateRoot = Element<FrameworkElement>(overlay, "OverlayRoot");
        FrameworkElement sourceAnchor = Element<FrameworkElement>(Rows(overlay)[3], "RouteOutputAnchor");
        FrameworkElement targetAnchor = Element<FrameworkElement>(overlay, "ProjectionInputAnchor");
        Point source = sourceAnchor.TranslatePoint(new Point(0.5d, 0.5d), coordinateRoot);
        Point target = targetAnchor.TranslatePoint(new Point(0d, 0.5d), coordinateRoot);
        TestSupport.True(
            target.X - source.X >= 24d,
            $"live anchor distance source={source} target={target}");
        overlay.Snapshot = Snapshot(4, StartupSequencePhase.Bind, MotionLevel.Full, 3);

        TextBlock previous = Element<TextBlock>(overlay, "ProjectionPreviousValue");
        TextBlock current = Element<TextBlock>(overlay, "ProjectionCurrentValue");
        Path track = Element<Path>(overlay, "ProjectionPulseTrack");
        FrameworkElement head = Element<FrameworkElement>(overlay, "ProjectionPulseHead");
        FrameworkElement canvas = Element<FrameworkElement>(overlay, "ProjectionPulseCanvas");
        TestSupport.Equal("1 / 6 RESOLVED", previous.Text, "previous projection value");
        TestSupport.Equal("3 / 6 RESOLVED", current.Text, "current projection value");
        TestSupport.True(previous.HasAnimatedProperties, "previous value exit clock");
        TestSupport.True(current.HasAnimatedProperties, "current value entry clock");
        PathGeometry geometry = TestSupport.NotNull(track.Data as PathGeometry, "live pulse geometry");
        TestSupport.True(geometry.Bounds.Width >= 24d, "live pulse has useful distance");
        TestSupport.True(head.HasAnimatedProperties, "Full pulse head animates");
        AssertLiveEndpoints(overlay, geometry);

        host.Width = 1600d;
        host.Height = 900d;
        host.UpdateLayout();
        overlay.Snapshot = Snapshot(5, StartupSequencePhase.Bind, MotionLevel.Full, 4);
        PathGeometry resized = TestSupport.NotNull(track.Data as PathGeometry, "resized live pulse geometry");
        TestSupport.True(resized.Bounds.Width >= 24d, "resized pulse has useful distance");
        AssertLiveEndpoints(overlay, resized);

        track.BeginAnimation(UIElement.OpacityProperty, null);
        track.Data = null;
        overlay.Snapshot = Snapshot(6, StartupSequencePhase.Bind, MotionLevel.Full, 4);
        TestSupport.True(track.Data is null, "unchanged projection does not pulse");

        overlay.RestoreFinalState();
        TestSupport.Equal(0d, canvas.Opacity, "pulse canvas cleanup");
        TestSupport.Equal(string.Empty, previous.Text, "previous projection cleanup");
        TestSupport.Equal("4 / 6 RESOLVED", current.Text, "current projection cleanup");
        TestSupport.False(head.HasAnimatedProperties, "pulse head clock cleanup");
        VerifyProjectionMotionVariants();
    });

    private static void VerifyChoreographyMotionVariants()
    {
        WithOverlay(1120d, 720d, standard =>
        {
            standard.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Standard, 0);
            standard.UpdateLayout();
            standard.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            TextBlock title = Element<TextBlock>(standard, "TraceworkTitleText");
            TestSupport.True(title.HasAnimatedProperties, "Standard title opacity");
            TestSupport.False(
                ((TranslateTransform)title.RenderTransform).HasAnimatedProperties,
                "Standard title has no translation");
            standard.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Standard, 0);
            FrameworkElement phaseClip = Element<FrameworkElement>(standard, "BottomPhaseTextClipHost");
            TestSupport.True(
                phaseClip.Clip is RectangleGeometry { HasAnimatedProperties: true },
                "Standard bottom text clip");
        });

        WithOverlay(1120d, 720d, reduced =>
        {
            reduced.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Reduced, 0);
            reduced.UpdateLayout();
            reduced.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            FrameworkElement titleGroup = Element<FrameworkElement>(reduced, "StartupTitleGroup");
            TestSupport.True(titleGroup.HasAnimatedProperties, "Reduced grouped title opacity");
            TextBlock title = Element<TextBlock>(reduced, "TraceworkTitleText");
            TestSupport.False(
                ((TranslateTransform)title.RenderTransform).HasAnimatedProperties,
                "Reduced title has no translation");
            reduced.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Reduced, 0);
            TestSupport.True(
                Element<FrameworkElement>(reduced, "RouteMatrixItems").HasAnimatedProperties,
                "Reduced route matrix opacity");
        });

        EnsureApplication();
        TraceworkStartupSequenceOverlay off = new()
        {
            Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Off, 0)
        };
        TestSupport.Equal(Visibility.Collapsed, off.Visibility, "Off overlay hidden");
        TestSupport.False(
            Element<FrameworkElement>(off, "TraceworkTitleText").HasAnimatedProperties,
            "Off title has no clock");
    }

    private static void VerifyProjectionMotionVariants()
    {
        WithOverlay(1120d, 720d, standard =>
        {
            PrepareProjectionPhase(standard, MotionLevel.Standard);
            standard.Snapshot = Snapshot(4, StartupSequencePhase.Bind, MotionLevel.Standard, 2);
            Path track = Element<Path>(standard, "ProjectionPulseTrack");
            FrameworkElement head = Element<FrameworkElement>(standard, "ProjectionPulseHead");
            TestSupport.True(track.Data is PathGeometry, "Standard track uses live path");
            TestSupport.False(head.HasAnimatedProperties, "Standard has no moving pulse head");
        });

        WithOverlay(1120d, 720d, reduced =>
        {
            PrepareProjectionPhase(reduced, MotionLevel.Reduced);
            reduced.Snapshot = Snapshot(4, StartupSequencePhase.Bind, MotionLevel.Reduced, 2);
            TestSupport.True(
                Element<Path>(reduced, "ProjectionPulseTrack").Data is null,
                "Reduced has no projection pulse");
            TestSupport.False(
                ((TranslateTransform)Element<TextBlock>(reduced, "ProjectionCurrentValue").RenderTransform)
                    .HasAnimatedProperties,
                "Reduced projection has no translation");
        });
    }

    private static void PrepareProjectionPhase(
        TraceworkStartupSequenceOverlay overlay,
        MotionLevel level)
    {
        overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, level, 1);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
        overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, level, 1);
        overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, level, 1);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
    }

    private static void AssertPresentation(
        StartupSequencePhase phase,
        int step,
        string code,
        string label)
    {
        StartupPhasePresentation presentation = TestSupport.NotNull(
            StartupPhasePresentation.Create(phase, null),
            phase.ToString());
        TestSupport.Equal(step, presentation.StepNumber, $"{phase} step");
        TestSupport.Equal(5, presentation.StepCount, $"{phase} count");
        TestSupport.Equal(code, presentation.PhaseCode, $"{phase} code");
        TestSupport.Equal(label, presentation.ChineseLabel, $"{phase} label");
        TestSupport.Equal(step - 1, presentation.CompletedStepCount, $"{phase} completed");
    }

    private static void AssertLiveEndpoints(
        TraceworkStartupSequenceOverlay overlay,
        PathGeometry geometry)
    {
        StartupMilestoneRow sensorRow = Rows(overlay)[3];
        FrameworkElement sourceAnchor = Element<FrameworkElement>(sensorRow, "RouteOutputAnchor");
        FrameworkElement targetAnchor = Element<FrameworkElement>(overlay, "ProjectionInputAnchor");
        FrameworkElement root = Element<FrameworkElement>(overlay, "OverlayRoot");
        Point expectedSource = sourceAnchor.TranslatePoint(
            new Point(sourceAnchor.ActualWidth / 2d, sourceAnchor.ActualHeight / 2d),
            root);
        Point expectedTarget = targetAnchor.TranslatePoint(
            new Point(0d, targetAnchor.ActualHeight / 2d),
            root);
        PathFigure figure = geometry.Figures[0];
        Point actualTarget = ((LineSegment)figure.Segments[figure.Segments.Count - 1]).Point;
        TestSupport.True((figure.StartPoint - expectedSource).Length < 0.01d, "pulse source is SENSOR BUS anchor");
        TestSupport.True((actualTarget - expectedTarget).Length < 0.01d, "pulse target is projection anchor");
    }

    private static T Element<T>(FrameworkElement owner, string name) where T : class =>
        TestSupport.NotNull(owner.FindName(name) as T, name);

    private static StartupMilestoneRow[] Rows(TraceworkStartupSequenceOverlay overlay)
    {
        ItemsControl matrix = Element<ItemsControl>(overlay, "RouteMatrixItems");
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
                    index < projectionCount ? StartupProjectionState.Value : StartupProjectionState.Pending,
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
