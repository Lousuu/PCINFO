using System.Windows;
using HardwareVision.Models;
using HardwareVision.Views.Shell;

namespace HardwareVision.Tests;

internal static class StartupFirstFrameTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup first frame Tracework Full has static cover", () => Cover(AppTheme.Tracework, MotionLevel.Full, Visibility.Visible)),
        ("Startup first frame Tracework Standard has static cover", () => Cover(AppTheme.Tracework, MotionLevel.Standard, Visibility.Visible)),
        ("Startup first frame Tracework Off has no cover", () => Cover(AppTheme.Tracework, MotionLevel.Off, Visibility.Collapsed)),
        ("Startup first frame Classic has no cover", () => Cover(AppTheme.Classic, MotionLevel.Full, Visibility.Collapsed))
    ];

    private static void Cover(AppTheme theme, MotionLevel motion, Visibility expected)
    {
        EnsureApplication();
        TraceworkStartupSequenceOverlay overlay = new();
        overlay.Measure(new Size(1120, 720));
        overlay.Arrange(new Rect(0, 0, 1120, 720));
        overlay.PrepareFirstFrame(theme, motion);
        TestSupport.Equal(expected, overlay.Visibility, "cover visibility");
        TestSupport.Equal(expected == Visibility.Visible, overlay.IsHitTestVisible, "cover hit testing");
        FrameworkElement background = (FrameworkElement)overlay.FindName("StartupBackgroundLayer");
        FrameworkElement content = (FrameworkElement)overlay.FindName("StartupContentLayer");
        FrameworkElement rail = (FrameworkElement)overlay.FindName("StartupBottomRailLayer");
        FrameworkElement commit = (FrameworkElement)overlay.FindName("CommitGroup");
        if (expected == Visibility.Visible)
        {
            TestSupport.Equal(1d, background.Opacity, "static background visible");
            TestSupport.Equal(0d, content.Opacity, "Dormant content hidden");
            TestSupport.Equal(0d, rail.Opacity, "Dormant rail hidden");
            TestSupport.Equal(Visibility.Collapsed, commit.Visibility, "Dormant COMMIT hidden");
        }
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            HardwareVision.App app = new();
            app.InitializeComponent();
        }
    }
}
