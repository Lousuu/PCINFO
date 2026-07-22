using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Themes;

namespace HardwareVision.Tests;

internal static class SystemRewireColdTemplateTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string, Action)> tests = [];
        for (int index = 1; index <= 10; index++)
        {
            tests.Add(($"System rewire cold-template {index:00} first Full trace has animation clocks", () => ColdTemplateReplays(MotionLevel.Full, 18d)));
        }
        for (int index = 11; index <= 20; index++)
        {
            tests.Add(($"System rewire cold-template {index:00} first Standard trace has animation clocks", () => ColdTemplateReplays(MotionLevel.Standard, 12d)));
        }
        return tests;
    }

    private static void ColdTemplateReplays(MotionLevel level, double expectedOffset)
    {
        Application application = GetApplication();
        SystemRewireOverlay overlay = new()
        {
            Snapshot = Snapshot(level),
            Style = (Style)application.FindResource("SystemRewireOverlayStyle")
        };
        UserControl host = new() { Content = overlay };
        host.Measure(new Size(1120, 720));
        host.Arrange(new Rect(0, 0, 1120, 720));
        host.UpdateLayout();
        TestSupport.True(overlay.EnsureTemplateReady(), "template ready");

        TranslateTransform source = TestSupport.NotNull(
            overlay.Template.FindName("PART_SourceTranslateTransform", overlay) as TranslateTransform,
            "source transform");
        TranslateTransform target = TestSupport.NotNull(
            overlay.Template.FindName("PART_TargetTranslateTransform", overlay) as TranslateTransform,
            "target transform");
        TestSupport.True(source.HasAnimatedProperties, "source animation clock");
        TestSupport.True(target.HasAnimatedProperties, "target animation clock");
        TestSupport.Equal(expectedOffset, overlay.Snapshot!.Plan.TraceOffset, "trace offset");
        TestSupport.Equal(Visibility.Visible, overlay.Visibility, "visible first trace");

        overlay.Snapshot = ThemeTransitionSnapshot.Idle(AppTheme.Tracework) with { Version = 2 };
        TestSupport.False(source.HasAnimatedProperties, "source clock cleaned at idle");
        TestSupport.False(target.HasAnimatedProperties, "target clock cleaned at idle");
    }

    private static ThemeTransitionSnapshot Snapshot(MotionLevel level)
    {
        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(MotionProfile.Create(level, level, string.Empty));
        return new ThemeTransitionSnapshot(1, ThemeTransitionPhase.Trace, AppTheme.Classic, AppTheme.Tracework,
            plan, true, true, false, null, null);
    }

    private static Application GetApplication()
    {
        if (Application.Current is not null)
        {
            return Application.Current;
        }
        HardwareVision.App application = new();
        application.InitializeComponent();
        return application;
    }
}
