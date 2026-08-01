using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Interop;
using HardwareVision.Models;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class Startup202VisualPolishTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int iteration = 1; iteration <= 20; iteration++)
        {
            tests.Add(($"2.0.2 First Frame three-render boundary {iteration:00}/20", VerifyTwoRenderBoundary));
            tests.Add(($"2.0.2 First Frame off-screen staging {iteration:00}/20", VerifyOffscreenStaging));
            tests.Add(($"2.0.2 First Frame final-placement restore {iteration:00}/20", VerifyFinalPlacementRestore));
            tests.Add(($"2.0.2 First Frame 500 ms fail-open {iteration:00}/20", VerifyFailOpen));
            tests.Add(($"2.0.2 First Frame closing generation {iteration:00}/20", VerifyClosingGeneration));
            tests.Add(($"2.0.2 DWM explicit caption color {iteration:00}/20", VerifyDwmExplicitColors));
            tests.Add(($"2.0.2 DWM unsupported fallback {iteration:00}/20", VerifyDwmFallback));
            tests.Add(($"2.0.2 COMMIT opacity hierarchy {iteration:00}/20", VerifyCommitHierarchy));
            tests.Add(($"2.0.2 COMMIT stable hold duration {iteration:00}/20", VerifyCommitDurations));
            tests.Add(($"2.0.2 COMMIT unified exit {iteration:00}/20", VerifyCommitExit));
            tests.Add(($"2.0.2 COMMIT failure cancellation bypass {iteration:00}/20", VerifyCommitBypass));
            tests.Add(($"2.0.2 startup visual no-regression {iteration:00}/20", VerifyExistingChoreography));
        }
        return tests;
    }

    private static void VerifyTwoRenderBoundary()
    {
        string source = WindowSource;
        int first = source.IndexOf("CommitFirstOffscreenRenderedFrame(generation)", StringComparison.Ordinal);
        int second = source.IndexOf("ApplyFinalFirstFramePlacement(generation)", StringComparison.Ordinal);
        int third = source.IndexOf("CommitFinalPositionRenderedFrame(generation)", StringComparison.Ordinal);
        int firstFlush = source.IndexOf("TryFlushNativeComposition()", first, StringComparison.Ordinal);
        int secondSchedule = source.IndexOf("DispatcherPriority.Render", firstFlush, StringComparison.Ordinal);
        int release = source.IndexOf("FirstFrameGatePhase.Released", third, StringComparison.Ordinal);
        TestSupport.True(first >= 0 && second > first && third > second, "three distinct render callbacks");
        TestSupport.True(firstFlush > first && secondSchedule > firstFlush, "first render flushes before second render");
        TestSupport.True(release > third, "release occurs only after final-position render callback");
        TestSupport.True(source.Contains("FirstOffscreenRenderCommitted", StringComparison.Ordinal), "offscreen render state");
        TestSupport.True(source.Contains("OffscreenCompositionFlushed", StringComparison.Ordinal), "offscreen composition state");
        TestSupport.True(source.Contains("FinalPlacementAppliedHidden", StringComparison.Ordinal), "hidden placement state");
        TestSupport.True(source.Contains("FinalPositionRenderCommitted", StringComparison.Ordinal), "final render state");
        TestSupport.True(source.Contains("FinalPositionCompositionFlushed", StringComparison.Ordinal), "final composition state");
        TestSupport.False(source.Contains("CompositionTarget.Rendering", StringComparison.Ordinal), "no rendering subscription");
    }

    private static void VerifyOffscreenStaging()
    {
        PhysicalPixelBounds staging = WindowPlacementInterop.ResolveOffscreenBounds(
            -1920,
            -200,
            1120,
            720);
        TestSupport.Equal(-3168, staging.Left, "staging left");
        TestSupport.Equal(-1048, staging.Top, "staging top");
        TestSupport.True(staging.Left + staging.Width < -1920, "entire window left of virtual desktop");
        TestSupport.True(staging.Top + staging.Height < -200, "entire window above virtual desktop");
        Contains(WindowSource,
            "TryStageWindowOffscreen",
            "WindowStartupLocation = WindowStartupLocation.Manual",
            "ShowActivated = false");
    }

    private static void VerifyFinalPlacementRestore()
    {
        string source = WindowSource;
        int capture = source.IndexOf("firstFramePlacement = CaptureFirstFramePlacement(handle);", StringComparison.Ordinal);
        int stage = source.IndexOf("TryStageWindowOffscreen", capture, StringComparison.Ordinal);
        int restore = source.IndexOf("RestoreFirstFramePlacement();", source.IndexOf("ApplyFinalFirstFramePlacement", StringComparison.Ordinal), StringComparison.Ordinal);
        int opacity = source.IndexOf("Opacity = 1d;", source.IndexOf("CompleteFirstFrameRelease", restore, StringComparison.Ordinal), StringComparison.Ordinal);
        TestSupport.True(capture >= 0 && stage > capture, "placement captured before staging");
        TestSupport.True(restore > stage, "final placement restored after staging");
        TestSupport.True(opacity > restore, "placement restored before visibility release");
        Contains(source,
            "firstFramePlacement.Physical.Bounds",
            "firstFramePlacement.StartupLocation",
            "firstFramePlacement.ShowActivated",
            "TryApplyWindowBounds");
        TestSupport.False(source.Contains("Left = firstFramePlacement", StringComparison.Ordinal), "physical X is not assigned to WPF Left");
        TestSupport.False(source.Contains("Top = firstFramePlacement", StringComparison.Ordinal), "physical Y is not assigned to WPF Top");
    }

    private static void VerifyFailOpen()
    {
        TestSupport.Equal(TimeSpan.FromMilliseconds(500), MainWindow.FirstFrameFailOpenTimeout, "fail-open timeout");
        string source = WindowSource;
        Contains(source,
            "Task.Delay(FirstFrameFailOpenDelay)",
            "DispatcherPriority.Send",
            "FailOpenFirstFrame(generation)",
            "FirstFrameGatePhase.FailOpenReleased",
            "RestoreFirstFramePlacement();",
            "TryFlushNativeComposition()");
        TestSupport.False(source.Contains("DispatcherTimer", StringComparison.Ordinal), "no timer");
        TestSupport.Equal(1, Count(source, "ReleaseFirstFrameGateAfterTimeoutAsync(generation)"), "one timeout scheduled");
    }

    private static void VerifyClosingGeneration()
    {
        string source = WindowSource;
        int closing = source.IndexOf("private void OnClosing", StringComparison.Ordinal);
        int invalidation = source.IndexOf("InvalidateFirstFrameGate();", closing, StringComparison.Ordinal);
        int cancelled = source.IndexOf("FirstFrameGatePhase.Cancelled", source.IndexOf("private void InvalidateFirstFrameGate", StringComparison.Ordinal), StringComparison.Ordinal);
        TestSupport.True(closing >= 0 && invalidation > closing, "closing invalidates generation");
        TestSupport.True(cancelled > invalidation, "armed gate becomes cancelled");
        Contains(source,
            "generation != Volatile.Read(ref firstFrameGateGeneration)",
            "!isWindowClosing",
            "FirstFrameGatePhase.Cancelled");
        string invalidate = Slice(source, "private void InvalidateFirstFrameGate()", "private bool TryTransitionFirstFrame");
        TestSupport.False(invalidate.Contains("Opacity =", StringComparison.Ordinal), "closing invalidation cannot touch opacity");
        TestSupport.False(invalidate.Contains("Activate(", StringComparison.Ordinal), "closing invalidation cannot activate");
    }

    private static void VerifyDwmExplicitColors()
    {
        TestSupport.Equal(0x00110E0B, MainWindow.ToColorRef(0x0B, 0x0E, 0x11), "caption COLORREF");
        TestSupport.Equal(0x002D2620, MainWindow.ToColorRef(0x20, 0x26, 0x2D), "border COLORREF");
        TestSupport.Equal(0x00F7F3EE, MainWindow.ToColorRef(0xEE, 0xF3, 0xF7), "text COLORREF");
        Contains(WindowSource,
            "DwmBorderColor = 34",
            "DwmCaptionColor = 35",
            "DwmTextColor = 36",
            "TraceworkCaptionColorRef = 0x00110E0B",
            "TraceworkBorderColorRef = 0x002D2620",
            "TraceworkTextColorRef = 0x00F7F3EE");
    }

    private static void VerifyDwmFallback()
    {
        string source = WindowSource;
        int attribute20 = source.IndexOf("DwmUseImmersiveDarkMode,", source.IndexOf("private void ApplyNativeWindowTheme", StringComparison.Ordinal), StringComparison.Ordinal);
        int legacy19 = source.IndexOf("DwmUseImmersiveDarkModeLegacy,", attribute20, StringComparison.Ordinal);
        int border34 = source.IndexOf("DwmBorderColor,", legacy19, StringComparison.Ordinal);
        int caption35 = source.IndexOf("DwmCaptionColor,", border34, StringComparison.Ordinal);
        int text36 = source.IndexOf("DwmTextColor,", caption35, StringComparison.Ordinal);
        TestSupport.True(attribute20 >= 0 && legacy19 > attribute20, "19 follows failed 20");
        TestSupport.True(border34 > legacy19 && caption35 > border34 && text36 > caption35, "explicit color order");
        Contains(source,
            "if (immersiveResult != 0)",
            "DwmDefaultColor = unchecked((int)0xFFFFFFFF)",
            "themeService.ThemeChanged += OnThemeChanged",
            "themeService.ThemeChanged -= OnThemeChanged",
            "DllNotFoundException",
            "EntryPointNotFoundException");
        TestSupport.False(source.Contains("throw;", StringComparison.Ordinal), "DWM remains fail-open");
    }

    private static void VerifyCommitHierarchy()
    {
        string xaml = OverlayXaml;
        int root = xaml.IndexOf("x:Name=\"CommitExitRoot\"", StringComparison.Ordinal);
        int graphic = xaml.IndexOf("x:Name=\"CommitGraphicLayer\"", root, StringComparison.Ordinal);
        int lockIndex = xaml.IndexOf("x:Name=\"CommitLock\"", graphic, StringComparison.Ordinal);
        int text = xaml.IndexOf("x:Name=\"CommitText\"", lockIndex, StringComparison.Ordinal);
        TestSupport.True(root >= 0 && graphic > root && lockIndex > graphic && text > lockIndex, "commit layer order");
        Contains(xaml, "Opacity=\"0.82\"", "Foreground=\"#B9F3D6\"", "Background=\"#8FE5BE\"", "Background=\"#A8EDCB\"");

        WithOverlay(overlay =>
        {
            overlay.Snapshot = Snapshot(1, MotionLevel.Full);
            TestSupport.Equal(1d, Base(overlay, "CommitExitRoot"), "root stable base");
            TestSupport.Equal(0.82d, Base(overlay, "CommitGraphicLayer"), "graphic stable base");
            TestSupport.Equal(1d, Base(overlay, "CommitLock"), "lock stable base");
            TestSupport.Equal(1d, Base(overlay, "CommitText"), "text stable base");
        });
    }

    private static void VerifyCommitDurations()
    {
        TestSupport.Equal(TimeSpan.FromMilliseconds(180), TraceworkStartupSequenceOverlay.ResolveCommitBuildDuration(MotionLevel.Full), "Full build");
        TestSupport.Equal(TimeSpan.FromMilliseconds(180), TraceworkStartupSequenceOverlay.ResolveCommitBuildDuration(MotionLevel.Standard), "Standard build");
        TestSupport.Equal(TimeSpan.FromMilliseconds(90), TraceworkStartupSequenceOverlay.ResolveCommitBuildDuration(MotionLevel.Reduced), "Reduced build");
        TestSupport.Equal(TimeSpan.FromMilliseconds(480), TraceworkStartupSequenceOverlay.ResolveCommitStableHoldDuration(MotionLevel.Full), "Full hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(360), TraceworkStartupSequenceOverlay.ResolveCommitStableHoldDuration(MotionLevel.Standard), "Standard hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(180), TraceworkStartupSequenceOverlay.ResolveCommitStableHoldDuration(MotionLevel.Reduced), "Reduced hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(660), TraceworkStartupSequenceOverlay.ResolveCommitMinimumPresentationDuration(MotionLevel.Full), "Full total");
        TestSupport.Equal(TimeSpan.FromMilliseconds(540), TraceworkStartupSequenceOverlay.ResolveCommitMinimumPresentationDuration(MotionLevel.Standard), "Standard total");
        TestSupport.Equal(TimeSpan.FromMilliseconds(270), TraceworkStartupSequenceOverlay.ResolveCommitMinimumPresentationDuration(MotionLevel.Reduced), "Reduced total");
    }

    private static void VerifyCommitExit()
    {
        TestSupport.Equal(TimeSpan.FromMilliseconds(90), TraceworkStartupSequenceOverlay.ResolveCommitExitDuration(MotionLevel.Full), "unified exit");
        WithOverlay(overlay =>
        {
            FrameworkElement root = Element(overlay, "CommitExitRoot");
            FrameworkElement graphic = Element(overlay, "CommitGraphicLayer");
            FrameworkElement text = Element(overlay, "CommitText");
            overlay.Snapshot = Snapshot(1, MotionLevel.Reduced);
            overlay.Snapshot = Snapshot(2, MotionLevel.Reduced, StartupSequencePhase.Reveal);
            PumpUntil(() => root.HasAnimatedProperties || overlay.Visibility == Visibility.Collapsed, TimeSpan.FromMilliseconds(1000));
            TestSupport.False(graphic.HasAnimatedProperties, "graphic has no independent exit clock");
            TestSupport.False(text.HasAnimatedProperties, "text has no independent exit clock");
            PumpUntil(() => overlay.Visibility == Visibility.Collapsed, TimeSpan.FromMilliseconds(500));
            TestSupport.False(root.HasAnimatedProperties, "root exit clock cleared");
            TestSupport.Equal(1d, root.Opacity, "root prepared base");
            TestSupport.Equal(0.82d, graphic.Opacity, "graphic prepared base");
            TestSupport.Equal(1d, text.Opacity, "text prepared base");
        });
    }

    private static void VerifyCommitBypass()
    {
        WithOverlay(overlay =>
        {
            StartupSequenceSnapshot failure = Snapshot(1, MotionLevel.Full) with
            {
                FailureMessage = "degraded"
            };
            overlay.Snapshot = failure;
            TestSupport.Equal(Visibility.Collapsed, Element(overlay, "CommitGroup").Visibility, "failure bypass");
            TestSupport.True(overlay.CommitVisualStartedAt is null, "failure does not start hold");

            overlay.Snapshot = Snapshot(2, MotionLevel.Off);
            TestSupport.Equal(Visibility.Collapsed, Element(overlay, "CommitGroup").Visibility, "Off bypass");
            overlay.RestoreFinalState();
            TestSupport.False(Element(overlay, "CommitExitRoot").HasAnimatedProperties, "cancellation clears root");
            TestSupport.False(Element(overlay, "CommitGraphicLayer").HasAnimatedProperties, "cancellation clears graphic");
            TestSupport.False(Element(overlay, "CommitText").HasAnimatedProperties, "cancellation clears text");
        });
    }

    private static void VerifyExistingChoreography()
    {
        TestSupport.Equal(TimeSpan.FromMilliseconds(120), TraceworkStartupSequenceOverlay.ResolveRevealHoldDuration(MotionLevel.Full), "Reveal Full hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(100), TraceworkStartupSequenceOverlay.ResolveRevealHoldDuration(MotionLevel.Standard), "Reveal Standard hold");
        TestSupport.Equal(TimeSpan.FromMilliseconds(60), TraceworkStartupSequenceOverlay.ResolveRevealHoldDuration(MotionLevel.Reduced), "Reveal Reduced hold");
        string source = OverlaySource;
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        Contains(normalized,
            "MotionLevel.Full\n            ? TimeSpan.FromMilliseconds(180)\n            : TimeSpan.FromMilliseconds(120)",
            "ApplyProjectionTransition(snapshot)",
            "StopProjectionPulseForReveal()",
            "CommitRevealBottomRail(snapshot)");
        string app = Read("HardwareVision", "App.xaml.cs");
        int prepare = app.IndexOf("mainWindow.PrepareFirstFrame();", StringComparison.Ordinal);
        int show = app.IndexOf("mainWindow.Show();", StringComparison.Ordinal);
        TestSupport.True(prepare >= 0 && show > prepare, "prepare/show contract unchanged");
    }

    private static StartupSequenceSnapshot Snapshot(
        long version,
        MotionLevel motion,
        StartupSequencePhase phase = StartupSequencePhase.Lock) =>
        StartupSequenceSnapshot.Dormant(AppTheme.Tracework, motion) with
        {
            Version = version,
            Phase = phase,
            IsActive = true,
            HasCompleted = false,
            ShellReady = true,
            VisualReady = true,
            CanCommit = true,
            InitialProjection = StartupInitialProjectionSnapshot.Pending with
            {
                PollingVersion = version,
                Slots = StartupInitialProjectionSnapshot.Pending.Slots
                    .Select(slot => slot with
                    {
                        State = StartupProjectionState.Value,
                        Detail = "ready"
                    })
                    .ToArray(),
                DispatcherApplied = true,
                PostDataLayoutObserved = true
            },
            Milestones = Enum.GetValues<StartupMilestoneId>()
                .Select(id => new StartupMilestoneSnapshot(
                    id,
                    StartupMilestoneSnapshot.GetName(id),
                    StartupMilestoneState.Ready,
                    StartupMilestoneSnapshot.GetStatusText(StartupMilestoneState.Ready),
                    "ready"))
                .ToArray()
        };

    private static void WithOverlay(Action<TraceworkStartupSequenceOverlay> assertion)
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new();
        Window host = new()
        {
            Content = overlay,
            Width = 1120d,
            Height = 720d,
            Left = -32000d,
            Top = -32000d,
            Opacity = 0d,
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

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            DispatcherFrame frame = new();
            DispatcherTimer timer = new(
                TimeSpan.FromMilliseconds(5),
                DispatcherPriority.Background,
                (_, _) => frame.Continue = false,
                Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
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

    private static FrameworkElement Element(FrameworkElement owner, string name) =>
        TestSupport.NotNull(owner.FindName(name) as FrameworkElement, name);

    private static double Base(FrameworkElement owner, string name) =>
        (double)Element(owner, name).GetAnimationBaseValue(UIElement.OpacityProperty);

    private static void Contains(string source, params string[] tokens)
    {
        foreach (string token in tokens)
        {
            TestSupport.True(source.Contains(token, StringComparison.Ordinal), token);
        }
    }

    private static string Slice(string source, string startToken, string endToken)
    {
        int start = source.IndexOf(startToken, StringComparison.Ordinal);
        int end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        TestSupport.True(start >= 0 && end > start, $"slice {startToken}");
        return source[start..end];
    }

    private static int Count(string source, string token)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
    private static string WindowSource => Read("HardwareVision", "MainWindow.xaml.cs");
    private static string OverlayXaml => Read("HardwareVision", "Views", "Shell", "TraceworkStartupSequenceOverlay.xaml");
    private static string OverlaySource => Read("HardwareVision", "Views", "Shell", "TraceworkStartupSequenceOverlay.xaml.cs");
}
