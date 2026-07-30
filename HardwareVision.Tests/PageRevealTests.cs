namespace HardwareVision.Tests;

internal static class PageRevealTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Page reveal 01 no full-page rectangle geometry", () => TestSupport.False(Code.Contains("new RectangleGeometry(start)", StringComparison.Ordinal), "full-page clip")),
        ("Page reveal 02 no full-page rect animation", () => TestSupport.False(Code.Contains("RectangleGeometry.RectProperty, new RectAnimation", StringComparison.Ordinal), "full-page rect animation")),
        ("Page reveal 03 directional geometry", Directions),
        ("Page reveal 04 no layout-size dependency", ActualSize),
        ("Page reveal 05 explicit exit and enter", () => { Has("PlayExit("); Has("PlayEnter("); }),
        ("Page reveal 06 spatial profile gate", () => Has("plan.AllowsPageTranslation")),
        ("Page reveal 07 cubic exit and enter", () => { Has("EasingMode.EaseIn"); Has("EasingMode.EaseOut"); }),
        ("Page reveal 08 Full duration", () => TestSupport.Equal(TimeSpan.FromMilliseconds(120), Create(HardwareVision.Models.MotionLevel.Full).PageEnterDuration, "Full enter")),
        ("Page reveal 09 Standard duration", () => TestSupport.Equal(TimeSpan.FromMilliseconds(95), Create(HardwareVision.Models.MotionLevel.Standard).PageEnterDuration, "Standard enter")),
        ("Page reveal 10 clip cleanup", ClipCleanup),
        ("Page reveal 11 valid resize continues motion", ResizeContinuation),
        ("Page reveal 12 no scale", NoScale),
        ("Page reveal 13 content retained", ContentRetained),
        ("Page reveal 14 module profile values", ModuleValues),
        ("Page reveal 15 one motion surface", OneMotionSurface),
        ("Page reveal 16 reduced no clip", ReducedNoClip)
    ];

    private static string Code => FlowRelayVisualSource.Read("HardwareVision", "Controls", "MotionTransitionHost.cs");
    private static string Plan => FlowRelayVisualSource.Read("HardwareVision", "Models", "NavigationTransitionPlan.cs");
    private static void Has(string value) => TestSupport.True(Code.Contains(value, StringComparison.Ordinal), value);
    private static void PlanHas(string value) => TestSupport.True(Plan.Contains(value, StringComparison.Ordinal), value);

    private static void Directions()
    {
        foreach (string direction in new[] { "FromRight", "FromLeft", "FromBottom", "FromTop" }) Has(direction);
    }

    private static void ActualSize()
    {
        TestSupport.False(Code.Contains("motionSurface.ActualWidth", StringComparison.Ordinal), "no width barrier");
        TestSupport.False(Code.Contains("motionSurface.ActualHeight", StringComparison.Ordinal), "no height barrier");
    }
    private static void ClipCleanup() => Has("motionSurface.Clip = null");
    private static void ResizeContinuation()
    {
        Has("SizeChanged += OnHostSizeChanged");
        Has("HostSizeChangedDuringMotion");
        TestSupport.False(
            Code.Contains("if (explicitSettleActive)\n        {\n            CancelTransition();", StringComparison.Ordinal),
            "valid resize must not cancel");
    }

    private static void NoScale()
    {
        TestSupport.False(Code.Contains("ScaleTransform", StringComparison.Ordinal), "scale");
        TestSupport.False(Code.Contains("LayoutTransform", StringComparison.Ordinal), "layout transform");
    }

    private static void ContentRetained() => TestSupport.False(
        Code.Split('\n').Any(line => line.TrimStart().StartsWith("Content = null", StringComparison.Ordinal)), "content cleared");

    private static void ModuleValues()
    {
        foreach (string value in new[] { "PrimaryEnterDelay", "SecondaryEnterDelay", "PrimaryModuleStartOpacity", "SecondaryModuleStartOpacity", "PrimaryModuleOffset", "SecondaryModuleOffset" }) Has(value);
    }

    private static void OneMotionSurface() => TestSupport.Equal(1, Count(Code, "motionSurface ="), "motion surface assignment");
    private static void ReducedNoClip() => TestSupport.Equal(
        false, Create(HardwareVision.Models.MotionLevel.Reduced).AllowsPageTranslation, "Reduced spatial reveal");

    private static HardwareVision.Models.NavigationTransitionPlan Create(HardwareVision.Models.MotionLevel level) =>
        HardwareVision.Models.NavigationTransitionPlan.Create(HardwareVision.Models.MotionProfile.Create(level, level, string.Empty));

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}
