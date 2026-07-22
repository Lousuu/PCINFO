using System.Windows;
using System.Windows.Media;
using HardwareVision.Controls;
using HardwareVision.Models;

namespace HardwareVision.Tests;

internal static class SystemRewireFirstTransitionTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("System rewire first transition Reduced has no translate clock", () => NoTranslate(MotionLevel.Reduced)),
        ("System rewire first transition Off has no clocks", () => NoTranslate(MotionLevel.Off)),
        ("System rewire first transition duplicate phase does not replace clock", DuplicatePhase),
        ("System rewire first transition startup suppression collapses overlay", Suppression)
    ];

    private static void NoTranslate(MotionLevel level)
    {
        SystemRewireOverlay overlay = Create(level);
        TranslateTransform source = (TranslateTransform)overlay.Template.FindName("PART_SourceTranslateTransform", overlay);
        TestSupport.False(source.HasAnimatedProperties, $"{level} translate clock");
    }

    private static void DuplicatePhase()
    {
        SystemRewireOverlay overlay = Create(MotionLevel.Full);
        TranslateTransform source = (TranslateTransform)overlay.Template.FindName("PART_SourceTranslateTransform", overlay);
        overlay.Snapshot = overlay.Snapshot! with { };
        TestSupport.True(source.HasAnimatedProperties, "duplicate retains the one active clock");
    }

    private static void Suppression()
    {
        SystemRewireOverlay overlay = Create(MotionLevel.Full);
        overlay.IsSuppressed = true;
        TestSupport.Equal(Visibility.Collapsed, overlay.Visibility, "suppressed visibility");
        TestSupport.False(overlay.IsHitTestVisible, "suppressed hit testing");
    }

    private static SystemRewireOverlay Create(MotionLevel level)
    {
        if (Application.Current is null) { HardwareVision.App app = new(); app.InitializeComponent(); }
        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(MotionProfile.Create(level, level, string.Empty));
        SystemRewireOverlay overlay = new()
        {
            Style = (Style)Application.Current!.FindResource("SystemRewireOverlayStyle"),
            Snapshot = new ThemeTransitionSnapshot(1, ThemeTransitionPhase.Trace, AppTheme.Classic, AppTheme.Tracework,
                plan, plan.IsOverlayEnabled, plan.BlocksInteraction, false, null, null)
        };
        overlay.Measure(new Size(1120, 720));
        overlay.Arrange(new Rect(0, 0, 1120, 720));
        overlay.UpdateLayout();
        overlay.EnsureTemplateReady();
        return overlay;
    }
}
