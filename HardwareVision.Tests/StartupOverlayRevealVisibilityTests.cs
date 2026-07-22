using System.Windows;
using System.Windows.Media.Animation;
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
        overlay.Snapshot = Snapshot(StartupSequencePhase.Reveal, active: true, complete: false);
        FrameworkElement background = (FrameworkElement)overlay.FindName("StartupBackgroundLayer");
        FrameworkElement content = (FrameworkElement)overlay.FindName("StartupContentLayer");
        FrameworkElement rail = (FrameworkElement)overlay.FindName("StartupBottomRailLayer");
        TestSupport.True(background.HasAnimatedProperties, "background clock");
        TestSupport.True(content.HasAnimatedProperties, "content clock");
        TestSupport.True(rail.HasAnimatedProperties, "rail clock");
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

    private static StartupSequenceSnapshot Snapshot(StartupSequencePhase phase, bool active, bool complete) =>
        StartupSequenceSnapshot.Dormant(AppTheme.Tracework, MotionLevel.Full) with
        { Version = 10, Phase = phase, IsActive = active, HasCompleted = complete, VisualReady = true };
}
