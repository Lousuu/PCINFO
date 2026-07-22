using System.Text.RegularExpressions;

namespace HardwareVision.Tests;

internal static class SignalRailRouteVisualTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Signal route 01 one RouteSegment", () => Count("RouteSegment", 1)),
        ("Signal route 02 one RoutePulse", () => Count("RoutePulse", 1)),
        ("Signal route 03 one PulseTrail", () => Count("PulseTrail", 1)),
        ("Signal route 04 one ArrivalLock", () => Count("ArrivalLock", 1)),
        ("Signal route 05 pulse dimensions", PulseDimensions),
        ("Signal route 06 trail dimensions", TrailDimensions),
        ("Signal route 07 arrival dimensions", ArrivalDimensions),
        ("Signal route 08 geometry uses real buttons", GeometryUsesRealButtons),
        ("Signal route 09 selected source is real", SelectedSourceIsReal),
        ("Signal route 10 Full enables trail", FullEnablesTrail),
        ("Signal route 11 Standard omits trail", StandardOmitsTrail),
        ("Signal route 12 Reduced and Off bypass", ReducedAndOffBypass),
        ("Signal route 13 arrival uses selected target", ArrivalUsesSelectedTarget),
        ("Signal route 14 cleanup clears every visual", CleanupClearsEveryVisual),
        ("Signal route 15 no layout animation", NoLayoutAnimation),
        ("Signal route 16 route layer is inaccessible", RouteLayerIsInaccessible),
        ("Signal route 17 arrival duration", ArrivalDuration),
        ("Signal route 18 ease in out", EaseInOut)
    ];

    private static string Xaml => FlowRelayVisualSource.Read("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml");
    private static string Code => FlowRelayVisualSource.Read("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml.cs");

    private static void Count(string name, int expected) => TestSupport.Equal(
        expected, Regex.Matches(Xaml, $"x:Name=\"{name}\"").Count, name);

    private static void PulseDimensions()
    {
        string block = Slice(Xaml, "x:Name=\"RoutePulse\"", "</Border>");
        TestSupport.True(block.Contains("Width=\"6\"", StringComparison.Ordinal), "pulse width");
        TestSupport.True(block.Contains("Height=\"2\"", StringComparison.Ordinal), "pulse height");
    }

    private static void TrailDimensions()
    {
        string block = Slice(Xaml, "x:Name=\"PulseTrail\"", "</Border>");
        TestSupport.True(block.Contains("Width=\"10\"", StringComparison.Ordinal), "trail width");
        TestSupport.True(block.Contains("Height=\"1\"", StringComparison.Ordinal), "trail height");
    }

    private static void ArrivalDimensions()
    {
        string block = Slice(Xaml, "x:Name=\"ArrivalLock\"", "</Border>");
        TestSupport.True(block.Contains("Width=\"8\"", StringComparison.Ordinal), "arrival width");
        TestSupport.True(block.Contains("Height=\"2\"", StringComparison.Ordinal), "arrival height");
    }

    private static void GeometryUsesRealButtons() => TestSupport.True(
        Code.Contains("TransformToAncestor(this)", StringComparison.Ordinal), "real button center");

    private static void SelectedSourceIsReal() => TestSupport.True(
        Code.Contains("FindButton(snapshot.OriginPage, requireSelected: true)", StringComparison.Ordinal), "selected source");

    private static void FullEnablesTrail() => TestSupport.True(
        Code.Contains("snapshot.Plan.EffectiveLevel == MotionLevel.Full", StringComparison.Ordinal)
        && Code.Contains("PulseTrail.Visibility = Visibility.Visible", StringComparison.Ordinal), "Full trail");

    private static void StandardOmitsTrail() => TestSupport.True(
        Code.Contains("if (snapshot.Plan.EffectiveLevel == MotionLevel.Full)", StringComparison.Ordinal), "trail Full gate");

    private static void ReducedAndOffBypass() => TestSupport.True(
        Code.Contains("!snapshot.Plan.ShowsSignalRailCursor", StringComparison.Ordinal), "profile cursor gate");

    private static void ArrivalUsesSelectedTarget() => TestSupport.True(
        Code.Contains("FindButton(snapshot.TargetPage, requireSelected: true)", StringComparison.Ordinal), "selected target");

    private static void CleanupClearsEveryVisual()
    {
        foreach (string visual in new[] { "RouteSegment", "RoutePulse", "PulseTrail", "ArrivalLock" })
            TestSupport.True(Code.Contains($"ClearVisual({visual}", StringComparison.Ordinal), visual);
    }

    private static void NoLayoutAnimation()
    {
        foreach (string forbidden in new[] { "Canvas.LeftProperty", "MarginProperty", "WidthProperty", "HeightProperty", "ScaleTransform" })
            TestSupport.False(Code.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    private static void RouteLayerIsInaccessible()
    {
        TestSupport.True(Xaml.Contains("x:Name=\"RouteVisualLayer\"", StringComparison.Ordinal), "route layer");
        TestSupport.True(Xaml.Contains("IsHitTestVisible=\"False\"", StringComparison.Ordinal), "no hit test");
        TestSupport.True(Xaml.Contains("IsTabStop=\"False\"", StringComparison.Ordinal), "no tab stop");
    }

    private static void ArrivalDuration()
    {
        TestSupport.True(Code.Contains("TimeSpan.FromMilliseconds(52)", StringComparison.Ordinal), "Full lock");
        TestSupport.True(Code.Contains("TimeSpan.FromMilliseconds(38)", StringComparison.Ordinal), "Standard lock");
    }

    private static void EaseInOut() => TestSupport.True(
        Code.Contains("EasingMode = EasingMode.EaseInOut", StringComparison.Ordinal), "pulse easing");

    private static string Slice(string source, string start, string end)
    {
        int first = source.IndexOf(start, StringComparison.Ordinal);
        int last = source.IndexOf(end, first, StringComparison.Ordinal);
        TestSupport.True(first >= 0 && last > first, start);
        return source[first..last];
    }
}
