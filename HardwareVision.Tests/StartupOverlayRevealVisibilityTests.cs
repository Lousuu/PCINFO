using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class StartupOverlayRevealVisibilityTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup reveal background exits concurrently", RevealCreatesClocks),
        ("Startup reveal content exits concurrently", RevealCreatesClocks),
        ("Startup reveal bottom rail exits concurrently", RevealCreatesClocks),
        ("Startup reveal cleanup collapses root", CleanupRestoresFinalState)
    ];

    private static void RevealCreatesClocks()
    {
        TraceworkStartupSequenceOverlay overlay = CreateOverlay();
        Window host = Host(overlay);
        try
        {
            overlay.Snapshot = Snapshot(StartupSequencePhase.Reveal, active: true, complete: false);
            FrameworkElement background = (FrameworkElement)overlay.FindName("StartupBackgroundLayer");
            FrameworkElement content = (FrameworkElement)overlay.FindName("StartupContentLayer");
            FrameworkElement rail = (FrameworkElement)overlay.FindName("StartupBottomRailLayer");
            FrameworkElement hold = (FrameworkElement)overlay.FindName("RevealPresentationHold");
            TestSupport.True(hold.HasAnimatedProperties, "Reveal hold clock");
            PumpUntil(
                () => background.HasAnimatedProperties
                    || overlay.Visibility == Visibility.Collapsed,
                TimeSpan.FromMilliseconds(260));
            if (overlay.Visibility == Visibility.Collapsed)
            {
                TestSupport.Equal(0d, background.Opacity, "background completed");
                TestSupport.Equal(0d, content.Opacity, "content completed");
                TestSupport.Equal(0d, rail.Opacity, "rail completed");
            }
            else
            {
                TestSupport.True(background.HasAnimatedProperties, "background clock");
                TestSupport.True(content.HasAnimatedProperties, "content clock");
                TestSupport.True(rail.HasAnimatedProperties, "rail clock");
            }
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static void CleanupRestoresFinalState()
    {
        TraceworkStartupSequenceOverlay overlay = CreateOverlay();
        overlay.Snapshot = Snapshot(StartupSequencePhase.Reveal, true, false);
        overlay.RestoreFinalState();
        TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "collapsed");
        TestSupport.False(overlay.HasAnimatedProperties, "root clocks cleared");
    }

    private static TraceworkStartupSequenceOverlay CreateOverlay()
    {
        if (Application.Current is null) { HardwareVision.App app = new(); app.InitializeComponent(); }
        TraceworkStartupSequenceOverlay overlay = new();
        overlay.Measure(new Size(1120, 720));
        overlay.Arrange(new Rect(0, 0, 1120, 720));
        return overlay;
    }

    private static Window Host(TraceworkStartupSequenceOverlay overlay)
    {
        Window host = new()
        {
            Content = overlay,
            Width = 1120,
            Height = 720,
            Left = -32000,
            Top = -32000,
            Opacity = 0,
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

    private static StartupSequenceSnapshot Snapshot(StartupSequencePhase phase, bool active, bool complete) =>
        StartupSequenceSnapshot.Dormant(AppTheme.Tracework, MotionLevel.Full) with
        { Version = 10, Phase = phase, IsActive = active, HasCompleted = complete, VisualReady = true };
}
