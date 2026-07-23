using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Views.Shell;
using Point = System.Windows.Point;

namespace HardwareVision.Tests;

internal static class StartupFinalVisualPolishTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"First-frame background {iteration:00}/20", VerifyFirstFrameBackground));
            tests.Add(($"Projection alignment {iteration:00}/20", VerifyProjectionAlignment));
            tests.Add(($"Dormant channel {iteration:00}/20", VerifyDormantChannel));
            tests.Add(($"Source port layout {iteration:00}/20", VerifySourcePortLayout));
            tests.Add(($"Commit minimum presentation {iteration:00}/20", VerifyCommitPresentation));
            tests.Add(($"Bottom rail style {iteration:00}/20", VerifyBottomRailStyle));
            tests.Add(($"Bottom rail atomic transition {iteration:00}/20", VerifyAtomicTransition));
        }

        return tests;
    }

    private static void VerifyFirstFrameBackground()
    {
        string window = Read("HardwareVision", "MainWindow.xaml");
        string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        string overlay = Read(
            "HardwareVision",
            "Views",
            "Shell",
            "TraceworkStartupSequenceOverlay.xaml");
        string app = Read("HardwareVision", "App.xaml.cs");

        TestSupport.True(window.Contains("Background=\"#0B0E11\"", StringComparison.Ordinal), "window static background");
        TestSupport.True(window.Contains("x:Name=\"MainWindowRoot\"", StringComparison.Ordinal), "window root");
        TestSupport.True(window.Contains("MainWindowRoot\"\r\n          Background=\"#0B0E11\"", StringComparison.Ordinal)
            || window.Contains("MainWindowRoot\"\n          Background=\"#0B0E11\"", StringComparison.Ordinal), "root static background");
        TestSupport.True(shell.Count(value => value == '#') >= 2, "shell has two static dark surfaces");
        TestSupport.True(overlay.Contains("x:Name=\"OverlayRoot\"\r\n          Background=\"#0B0E11\"", StringComparison.Ordinal)
            || overlay.Contains("x:Name=\"OverlayRoot\"\n          Background=\"#0B0E11\"", StringComparison.Ordinal), "overlay root static background");
        int prepare = app.IndexOf("mainWindow.PrepareFirstFrame();", StringComparison.Ordinal);
        int show = app.IndexOf("mainWindow.Show();", StringComparison.Ordinal);
        TestSupport.True(prepare >= 0 && show > prepare, "prepare occurs before show");
        TestSupport.False(window.Contains("AllowsTransparency=\"True\"", StringComparison.Ordinal), "window is opaque");
        TestSupport.False(window.Contains("Opacity=\"0\"", StringComparison.Ordinal), "window is not opacity-hidden");
    }

    private static void VerifyProjectionAlignment()
    {
        foreach ((double width, double height) in Sizes())
        {
            WithOverlay(width, height, overlay =>
            {
                PrepareBind(overlay, MotionLevel.Full, 4);
                PumpUntil(() => overlay.IsProjectionLedgerReady, TimeSpan.FromMilliseconds(500));
                Pump(TimeSpan.FromMilliseconds(120));
                FrameworkElement root = Element<FrameworkElement>(overlay, "OverlayRoot");
                TextBlock title = Element<TextBlock>(overlay, "ProjectionTitleLabel");
                FrameworkElement value = Element<FrameworkElement>(overlay, "ProjectionValueClipHost");
                FrameworkElement port = Element<FrameworkElement>(overlay, "ProjectionInputPort");
                double titleX = Left(title, root);
                foreach (string name in new[]
                         {
                             "LedgerNodeLabel",
                             "LedgerLaunchLabel",
                             "LedgerThemeLabel",
                             "LedgerMotionLabel",
                             "LedgerVersionLabel"
                         })
                {
                    TestSupport.True(
                        Math.Abs(Left(Element<TextBlock>(overlay, name), root) - titleX) <= 1d,
                        $"{name} left edge");
                }

                TestSupport.True(Math.Abs(Left(value, root) - titleX) <= 1d, "projection value left edge");
                double portLeft = Left(port, root);
                double portRight = portLeft + port.ActualWidth;
                TestSupport.True(portRight < titleX, "input port is left of title");
                TestSupport.True(titleX - portRight is >= 10d and <= 14d, "input port gap");
                TraceworkStartupSequenceOverlay.ProjectionRoute route =
                    overlay.LastProjectionRoute
                    ?? throw new InvalidOperationException("projection route");
                TestSupport.True(route.Target.Y < route.Source.Y, "projection route bends upward");
            });
        }
    }

    private static void VerifyDormantChannel()
    {
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full, 0);
            TestSupport.Equal(0d, Element<FrameworkElement>(overlay, "ProjectionDormantSourceSegment").Opacity, "Index dormant hidden");
            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Full, 0);
            TestSupport.Equal(0d, Element<FrameworkElement>(overlay, "ProjectionDormantSourceSegment").Opacity, "Route dormant hidden");
            PrepareBind(overlay, MotionLevel.Full, 5, startVersion: 3);
            PumpUntil(() => overlay.IsProjectionLedgerReady, TimeSpan.FromMilliseconds(500));
            Pump(TimeSpan.FromMilliseconds(120));
            FrameworkElement dormantSource = Element<FrameworkElement>(overlay, "ProjectionDormantSourceSegment");
            FrameworkElement dormantVertical = Element<FrameworkElement>(overlay, "ProjectionDormantVerticalSegment");
            FrameworkElement dormantTarget = Element<FrameworkElement>(overlay, "ProjectionDormantTargetSegment");
            FrameworkElement activeSource = Element<FrameworkElement>(overlay, "ProjectionSourceHorizontalSegment");
            FrameworkElement activeVertical = Element<FrameworkElement>(overlay, "ProjectionVerticalBridgeSegment");
            FrameworkElement activeTarget = Element<FrameworkElement>(overlay, "ProjectionTargetHorizontalSegment");
            TestSupport.Nearly(0.12d, dormantSource.Opacity, "Full dormant opacity", 0.02d);
            AssertSameGeometry(dormantSource, activeSource, "source");
            AssertSameGeometry(dormantVertical, activeVertical, "vertical");
            AssertSameGeometry(dormantTarget, activeTarget, "target");
            TestSupport.True(overlay.IsProjectionPulseActive, "pulse overlays dormant channel");
            PumpUntil(() => !overlay.IsProjectionPulseActive, TimeSpan.FromMilliseconds(900));
            TestSupport.Nearly(0.12d, dormantSource.Opacity, "dormant remains after pulse", 0.001d);
            overlay.Snapshot = Snapshot(7, StartupSequencePhase.Lock, MotionLevel.Full, 6, StartupMilestoneState.Ready);
            TestSupport.Nearly(0.12d, dormantSource.Opacity, "Lock keeps dormant", 0.001d);
            overlay.Snapshot = Snapshot(8, StartupSequencePhase.Reveal, MotionLevel.Full, 6, StartupMilestoneState.Ready);
            TestSupport.Equal(0d, dormantSource.Opacity, "Reveal removes dormant");
        });

        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareBind(overlay, MotionLevel.Reduced, 3);
            PumpUntil(() => overlay.IsProjectionLedgerReady, TimeSpan.FromMilliseconds(300));
            FrameworkElement dormant =
                Element<FrameworkElement>(overlay, "ProjectionDormantSourceSegment");
            PumpUntil(
                () => Math.Abs(dormant.Opacity - 0.08d) <= 0.002d,
                TimeSpan.FromMilliseconds(300));
            TestSupport.Nearly(
                0.08d,
                dormant.Opacity,
                "Reduced dormant opacity",
                0.001d);
            TestSupport.False(overlay.IsProjectionPulseActive, "Reduced has no active pulse");
        });

        TestSupport.Equal(0d, TraceworkStartupSequenceOverlay.ResolveProjectionDormantOpacity(MotionLevel.Off), "Off dormant opacity");
        string xaml = Read("HardwareVision", "Views", "Shell", "TraceworkStartupSequenceOverlay.xaml");
        TestSupport.False(xaml.Contains("RepeatBehavior=\"Forever\"", StringComparison.Ordinal), "dormant is not looping");
    }

    private static void VerifySourcePortLayout()
    {
        foreach ((double width, double height) in Sizes())
        {
            WithOverlay(width, height, overlay =>
            {
                PrepareBind(overlay, MotionLevel.Full, 4);
                PumpUntil(() => overlay.IsProjectionLedgerReady, TimeSpan.FromMilliseconds(500));
                StartupMilestoneRow sensor = Rows(overlay)[3];
                FrameworkElement detail = Element<FrameworkElement>(sensor, "MilestoneDetail");
                FrameworkElement port = Element<FrameworkElement>(sensor, "RouteOutputPort");
                FrameworkElement anchor = Element<FrameworkElement>(sensor, "RouteOutputAnchor");
                double distance = Left(port, sensor) - (Left(detail, sensor) + detail.ActualWidth);
                TestSupport.True(distance is >= 12d and <= 24d, "detail-to-port distance");
                TestSupport.Equal(34d, sensor.ActualHeight, "fixed row height");
                TestSupport.Equal(6d, port.ActualWidth, "port width");
                TestSupport.True(Math.Abs((Left(anchor, sensor) + 0.5d) - (Left(port, sensor) + 3d)) <= 0.5d, "anchor center");
                double expectedMax = width >= 1120d ? 420d : width >= 1000d ? 300d : 220d;
                TestSupport.Equal(
                    expectedMax,
                    Element<TextBlock>(sensor, "MilestoneDetail").MaxWidth,
                    $"responsive detail cap at requested {width}, actual {overlay.ActualWidth}");
            });
        }

        string row = Read("HardwareVision", "Views", "Shell", "StartupMilestoneRow.xaml");
        TestSupport.True(row.Contains("TextTrimming=\"CharacterEllipsis\"", StringComparison.Ordinal), "detail remains trimmed");
        TestSupport.True(row.Contains("<ColumnDefinition Width=\"16\" />", StringComparison.Ordinal), "fixed detail port gap");
        TestSupport.False(row.Contains("HorizontalAlignment=\"Right\"\r\n              VerticalAlignment=\"Center\"\r\n              Opacity=\"0\"", StringComparison.Ordinal), "port no longer floats right");
    }

    private static void VerifyCommitPresentation()
    {
        TestSupport.Equal(TimeSpan.FromMilliseconds(1250), StartupSequenceService.ResolveStartupLockDuration(MotionLevel.Full), "Full lock duration");
        TestSupport.Equal(TimeSpan.FromMilliseconds(950), StartupSequenceService.ResolveStartupLockDuration(MotionLevel.Standard), "Standard lock duration");
        TestSupport.Equal(TimeSpan.FromMilliseconds(360), StartupSequenceService.ResolveStartupLockDuration(MotionLevel.Reduced), "Reduced lock duration");
        TestSupport.Equal(TimeSpan.Zero, StartupSequenceService.ResolveStartupLockDuration(MotionLevel.Off), "Off lock duration");
        TestSupport.Equal(TimeSpan.FromMilliseconds(350), TraceworkStartupSequenceOverlay.ResolveCommitStableHoldDuration(MotionLevel.Full), "Full stable hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(250), TraceworkStartupSequenceOverlay.ResolveCommitStableHoldDuration(MotionLevel.Standard), "Standard stable hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(180), TraceworkStartupSequenceOverlay.ResolveCommitStableHoldDuration(MotionLevel.Reduced), "Reduced stable hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(200), TraceworkStartupSequenceOverlay.ResolveCommitRevealCompensationCap(MotionLevel.Full), "Full compensation cap");
        TestSupport.Equal(TimeSpan.FromMilliseconds(150), TraceworkStartupSequenceOverlay.ResolveCommitRevealCompensationCap(MotionLevel.Standard), "Standard compensation cap");
        TestSupport.Equal(TimeSpan.FromMilliseconds(80), TraceworkStartupSequenceOverlay.ResolveCommitRevealCompensationCap(MotionLevel.Reduced), "Reduced compensation cap");

        WithOverlay(1120d, 720d, overlay =>
        {
            PrepareBind(overlay, MotionLevel.Full, 1);
            PumpUntil(() => overlay.IsProjectionPulseActive, TimeSpan.FromMilliseconds(500));
            overlay.Snapshot = Snapshot(
                4,
                StartupSequencePhase.Lock,
                MotionLevel.Full,
                1,
                StartupMilestoneState.Ready) with { CanCommit = true };
            TestSupport.True(overlay.IsCommitPendingForProjection, "Commit defers for projection");
            TestSupport.True(overlay.CommitVisualStartedAt is null, "Commit start not recorded early");
            PumpUntil(() => overlay.CommitVisualStartedAt.HasValue, TimeSpan.FromMilliseconds(900));
            TestSupport.False(overlay.IsCommitPendingForProjection, "Commit starts after pulse");
            FrameworkElement group = Element<FrameworkElement>(overlay, "CommitGroup");
            FrameworkElement commitLock = Element<FrameworkElement>(overlay, "CommitLock");
            TestSupport.Equal(0.70d, (double)group.GetAnimationBaseValue(UIElement.OpacityProperty), "Commit group stable opacity");
            TestSupport.Equal(0.70d, (double)commitLock.GetAnimationBaseValue(UIElement.OpacityProperty), "Commit lock stable opacity");
            overlay.Snapshot = Snapshot(
                5,
                StartupSequencePhase.Reveal,
                MotionLevel.Full,
                1,
                StartupMilestoneState.Ready) with { CanCommit = true };
            TestSupport.True(overlay.IsCommitRevealCompensationPending, "early Reveal is bounded");
            PumpUntil(() => overlay.IsRevealVisualStateEntered, TimeSpan.FromMilliseconds(300));
            TestSupport.True(group.HasAnimatedProperties || group.Opacity == 0d, "Commit exits in Reveal");
        });

        string code = Read(
            "HardwareVision",
            "Views",
            "Shell",
            "TraceworkStartupSequenceOverlay.xaml.cs");
        TestSupport.True(code.Contains("string.IsNullOrWhiteSpace(snapshot.FailureMessage)", StringComparison.Ordinal), "failure bypasses compensation");
        TestSupport.False(code.Contains("DispatcherTimer", StringComparison.Ordinal), "Commit has no timer");
    }

    private static void VerifyBottomRailStyle()
    {
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full, 0);
            PumpUntil(() => overlay.IsBottomRailReady, TimeSpan.FromMilliseconds(400));
            Pump(TimeSpan.FromMilliseconds(120));
            TextBlock left = Element<TextBlock>(overlay, "BottomRailLabel");
            TextBlock code = Element<TextBlock>(overlay, "BottomCurrentPhaseCode");
            TextBlock middle = Element<TextBlock>(overlay, "BottomCurrentPhaseText");
            FrameworkElement root = Element<FrameworkElement>(overlay, "OverlayRoot");
            TestSupport.Equal(left.FontSize, code.FontSize, "edge font size");
            TestSupport.Equal(left.FontWeight, code.FontWeight, "edge font weight");
            TestSupport.Equal(left.LineHeight, code.LineHeight, "edge line height");
            TestSupport.Equal(11d, left.FontSize, "edge size");
            TestSupport.Equal(18d, left.LineHeight, "edge line height");
            TestSupport.Equal(15d, middle.FontSize, "middle hierarchy");
            double leftCenter = Top(left, root) + (left.ActualHeight / 2d);
            double codeCenter = Top(code, root) + (code.ActualHeight / 2d);
            TestSupport.True(Math.Abs(leftCenter - codeCenter) <= 1d, "edge baseline centers");

            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Full, 0);
            Pump(TimeSpan.FromMilliseconds(250));
            overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, MotionLevel.Full, 0);
            PumpUntil(() => code.Text == "BIND", TimeSpan.FromMilliseconds(300));
            SolidColorBrush codeBrush = TestSupport.NotNull(code.Foreground as SolidColorBrush, "Bind code brush");
            SolidColorBrush expected = TestSupport.NotNull(overlay.FindResource("TraceworkTelemetryBrush") as SolidColorBrush, "telemetry brush");
            TestSupport.Equal(expected.Color, codeBrush.Color, "Bind telemetry color");
        });
    }

    private static void VerifyAtomicTransition()
    {
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, MotionLevel.Full, 0);
            PumpUntil(() => overlay.IsBottomRailReady, TimeSpan.FromMilliseconds(400));
            Pump(TimeSpan.FromMilliseconds(150));
            TextBlock currentText = Element<TextBlock>(overlay, "BottomCurrentPhaseText");
            TextBlock currentCode = Element<TextBlock>(overlay, "BottomCurrentPhaseCode");
            string indexText = currentText.Text;
            TestSupport.Equal("INDEX", currentCode.Text, "Index code");

            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, MotionLevel.Full, 0);
            TextBlock previousText = Element<TextBlock>(overlay, "BottomPreviousPhaseText");
            TextBlock previousCode = Element<TextBlock>(overlay, "BottomPreviousPhaseCode");
            TestSupport.Equal(indexText, previousText.Text, "old text preserved");
            TestSupport.Equal("INDEX", previousCode.Text, "old code preserved");
            TestSupport.Equal("ROUTE", currentCode.Text, "new code prepared");
            TestSupport.True(
                ((TranslateTransform)previousText.RenderTransform).HasAnimatedProperties
                && ((TranslateTransform)previousCode.RenderTransform).HasAnimatedProperties,
                "Previous text and code share TranslateY");
            TestSupport.True(
                ((TranslateTransform)currentText.RenderTransform).HasAnimatedProperties
                && ((TranslateTransform)currentCode.RenderTransform).HasAnimatedProperties,
                "Current text and code share TranslateY");
            Border routeTrack = Element<Border>(overlay, "PhaseSegmentRoute");
            SolidColorBrush identity = TestSupport.NotNull(overlay.FindResource("TraceworkIdentityBrush") as SolidColorBrush, "identity brush");
            SolidColorBrush routeBrush = TestSupport.NotNull(routeTrack.Background as SolidColorBrush, "route track brush");
            TestSupport.Equal(identity.Color, routeBrush.Color, "track commits with current presentation");
            Pump(TimeSpan.FromMilliseconds(250));

            overlay.Snapshot = Snapshot(3, StartupSequencePhase.Bind, MotionLevel.Full, 0);
            TestSupport.Equal("ROUTE", previousCode.Text, "Route pairs with outgoing text");
            TestSupport.Equal("BIND", currentCode.Text, "Bind pairs with incoming text");
            int queuedBefore = overlay.PendingBottomPhaseCount;
            overlay.Snapshot = Snapshot(4, StartupSequencePhase.Bind, MotionLevel.Full, 0);
            TestSupport.Equal(queuedBefore, overlay.PendingBottomPhaseCount, "same phase does not replay");
            overlay.RestoreFinalState();
            TestSupport.Equal(string.Empty, previousCode.Text, "previous code cleanup");
            TestSupport.Equal(string.Empty, currentCode.Text, "current code cleanup");
        });

        VerifyAtomicMode(MotionLevel.Standard);
        VerifyAtomicMode(MotionLevel.Reduced);
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(
                1,
                StartupSequencePhase.Index,
                MotionLevel.Full,
                0,
                failureMessage: "startup degraded");
            PumpUntil(() => overlay.IsBottomRailReady, TimeSpan.FromMilliseconds(400));
            Pump(TimeSpan.FromMilliseconds(120));
            TextBlock failureText = Element<TextBlock>(overlay, "BottomCurrentPhaseText");
            TextBlock failureCode = Element<TextBlock>(overlay, "BottomCurrentPhaseCode");
            TestSupport.Equal("FAILED", failureCode.Text, "failure code");
            TestSupport.True(!string.IsNullOrWhiteSpace(failureText.Text), "failure copy");
            SolidColorBrush critical = TestSupport.NotNull(
                overlay.FindResource("CriticalBrush") as SolidColorBrush,
                "critical brush");
            TestSupport.Equal(
                critical.Color,
                TestSupport.NotNull(failureText.Foreground as SolidColorBrush, "failure text brush").Color,
                "failure text color");
            TestSupport.Equal(
                critical.Color,
                TestSupport.NotNull(failureCode.Foreground as SolidColorBrush, "failure code brush").Color,
                "failure code color");
        });
        string code = Read(
            "HardwareVision",
            "Views",
            "Shell",
            "TraceworkStartupSequenceOverlay.xaml.cs");
        TestSupport.True(
            code.Contains("if (level == MotionLevel.Off)", StringComparison.Ordinal)
            && code.Contains("ApplyPhaseSegmentStates(presentation);", StringComparison.Ordinal),
            "Off uses direct atomic commit");
    }

    private static void VerifyAtomicMode(MotionLevel level)
    {
        WithOverlay(1120d, 720d, overlay =>
        {
            overlay.Snapshot = Snapshot(1, StartupSequencePhase.Index, level, 0);
            if (level != MotionLevel.Off)
            {
                PumpUntil(() => overlay.IsBottomRailReady, TimeSpan.FromMilliseconds(400));
                Pump(TimeSpan.FromMilliseconds(170));
            }
            overlay.Snapshot = Snapshot(2, StartupSequencePhase.Route, level, 0);
            TextBlock previousText = Element<TextBlock>(overlay, "BottomPreviousPhaseText");
            TextBlock previousCode = Element<TextBlock>(overlay, "BottomPreviousPhaseCode");
            TextBlock currentText = Element<TextBlock>(overlay, "BottomCurrentPhaseText");
            TextBlock currentCode = Element<TextBlock>(overlay, "BottomCurrentPhaseCode");
            if (level == MotionLevel.Off)
            {
                TestSupport.Equal(0d, previousText.Opacity, "Off previous text hidden");
                TestSupport.Equal(0d, previousCode.Opacity, "Off previous code hidden");
                TestSupport.Equal(1d, currentText.Opacity, "Off current text visible");
                TestSupport.Equal(1d, currentCode.Opacity, "Off current code visible");
            }
            else
            {
                TestSupport.True(previousText.HasAnimatedProperties, $"{level} previous text clock");
                TestSupport.True(previousCode.HasAnimatedProperties, $"{level} previous code clock");
                TestSupport.True(currentText.HasAnimatedProperties, $"{level} current text clock");
                TestSupport.True(currentCode.HasAnimatedProperties, $"{level} current code clock");
            }
        });
    }

    private static void PrepareBind(
        TraceworkStartupSequenceOverlay overlay,
        MotionLevel level,
        int projectionCount,
        long startVersion = 1)
    {
        overlay.Snapshot = Snapshot(startVersion, StartupSequencePhase.Index, level, projectionCount);
        overlay.UpdateLayout();
        overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        overlay.UpdateLayout();
        overlay.Snapshot = Snapshot(startVersion + 1, StartupSequencePhase.Route, level, projectionCount);
        overlay.Snapshot = Snapshot(startVersion + 2, StartupSequencePhase.Bind, level, projectionCount);
        overlay.UpdateLayout();
    }

    private static StartupSequenceSnapshot Snapshot(
        long version,
        StartupSequencePhase phase,
        MotionLevel level,
        int projectionCount,
        StartupMilestoneState milestoneState = StartupMilestoneState.Wait,
        string? failureMessage = null)
    {
        HardwareOverviewKind[] kinds =
        [
            HardwareOverviewKind.Cpu,
            HardwareOverviewKind.Gpu,
            HardwareOverviewKind.Memory,
            HardwareOverviewKind.Disk,
            HardwareOverviewKind.Network,
            HardwareOverviewKind.System
        ];
        StartupInitialProjectionSnapshot projection = new(
            1,
            kinds.Select((kind, index) => new StartupProjectionSlotSnapshot(
                kind,
                index < projectionCount ? StartupProjectionState.Value : StartupProjectionState.Pending,
                index < projectionCount ? "resolved" : "pending")).ToArray(),
            DispatcherApplied: projectionCount == 6,
            PostDataLayoutObserved: projectionCount == 6);
        return StartupSequenceSnapshot.Dormant(AppTheme.Tracework, level) with
        {
            Version = version,
            Phase = phase,
            IsActive = true,
            VisualReady = true,
            FailureMessage = failureMessage,
            InitialProjection = projection,
            Milestones = Enum.GetValues<StartupMilestoneId>()
                .Select(id => new StartupMilestoneSnapshot(
                    id,
                    StartupMilestoneSnapshot.GetName(id),
                    milestoneState,
                    StartupMilestoneSnapshot.GetStatusText(milestoneState),
                    id == StartupMilestoneId.SensorBus
                        ? "sensor detail for projection source"
                        : milestoneState.ToString()))
                .ToArray()
        };
    }

    private static void WithOverlay(
        double width,
        double height,
        Action<TraceworkStartupSequenceOverlay> assertion)
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
            assertion(overlay);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
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

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(10));
        }

        TestSupport.True(condition(), "condition reached before timeout");
    }

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

    private static void AssertSameGeometry(
        FrameworkElement expected,
        FrameworkElement actual,
        string name)
    {
        TestSupport.Nearly(Canvas.GetLeft(expected), Canvas.GetLeft(actual), $"{name} left", 0.01d);
        TestSupport.Nearly(Canvas.GetTop(expected), Canvas.GetTop(actual), $"{name} top", 0.01d);
        TestSupport.Nearly(expected.Width, actual.Width, $"{name} width", 0.01d);
        TestSupport.Nearly(expected.Height, actual.Height, $"{name} height", 0.01d);
    }

    private static double Left(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0d, 0d), root).X;

    private static double Top(FrameworkElement element, FrameworkElement root) =>
        element.TranslatePoint(new Point(0d, 0d), root).Y;

    private static T Element<T>(FrameworkElement owner, string name) where T : class =>
        TestSupport.NotNull(owner.FindName(name) as T, name);

    private static (double Width, double Height)[] Sizes() =>
    [
        (1107d, 685d),
        (1120d, 720d),
        (1600d, 900d)
    ];

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
}
