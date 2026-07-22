namespace HardwareVision.Tests;

internal static class PageRevealTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Page reveal 01 rectangle geometry", () => Has("new RectangleGeometry(start)")),
        ("Page reveal 02 rect animation", () => Has("RectangleGeometry.RectProperty, new RectAnimation")),
        ("Page reveal 03 directional geometry", Directions),
        ("Page reveal 04 actual size", ActualSize),
        ("Page reveal 05 reveal profile gate", () => Has("plan.PageRevealDuration > TimeSpan.Zero")),
        ("Page reveal 06 spatial profile gate", () => Has("plan.AllowsPageTranslation")),
        ("Page reveal 07 cubic reveal", () => Has("EasingMode = EasingMode.EaseInOut")),
        ("Page reveal 08 Full duration", () => TestSupport.Equal(TimeSpan.FromMilliseconds(150), Create(HardwareVision.Models.MotionLevel.Full).PageRevealDuration, "Full reveal")),
        ("Page reveal 09 Standard duration", () => TestSupport.Equal(TimeSpan.FromMilliseconds(118), Create(HardwareVision.Models.MotionLevel.Standard).PageRevealDuration, "Standard reveal")),
        ("Page reveal 10 clip cleanup", ClipCleanup),
        ("Page reveal 11 resize cancellation", ResizeCancellation),
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

    private static void ActualSize() { Has("motionSurface.ActualWidth"); Has("motionSurface.ActualHeight"); }
    private static void ClipCleanup() { Has("BeginAnimation(RectangleGeometry.RectProperty, null)"); Has("motionSurface.Clip = null"); }
    private static void ResizeCancellation() { Has("SizeChanged += OnHostSizeChanged"); Has("OnHostSizeChanged"); Has("RestoreFinalState()"); }

    private static void NoScale()
    {
        TestSupport.False(Code.Contains("ScaleTransform", StringComparison.Ordinal), "scale");
        TestSupport.False(Code.Contains("LayoutTransform", StringComparison.Ordinal), "layout transform");
    }

    private static void ContentRetained() => TestSupport.False(
        Code.Split('\n').Any(line => line.TrimStart().StartsWith("Content = null", StringComparison.Ordinal)), "content cleared");

    private static void ModuleValues()
    {
        foreach (string value in new[] { "PrimaryModuleDelay", "SecondaryModuleDelay", "PrimaryModuleStartOpacity", "SecondaryModuleStartOpacity", "PrimaryModuleOffset", "SecondaryModuleOffset" }) Has(value);
    }

    private static void OneMotionSurface() => TestSupport.Equal(1, Count(Code, "motionSurface ="), "motion surface assignment");
    private static void ReducedNoClip() => TestSupport.Equal(
        TimeSpan.Zero, Create(HardwareVision.Models.MotionLevel.Reduced).PageRevealDuration, "Reduced reveal");

    private static HardwareVision.Models.NavigationTransitionPlan Create(HardwareVision.Models.MotionLevel level) =>
        HardwareVision.Models.NavigationTransitionPlan.Create(HardwareVision.Models.MotionProfile.Create(level, level, string.Empty));

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }
}
