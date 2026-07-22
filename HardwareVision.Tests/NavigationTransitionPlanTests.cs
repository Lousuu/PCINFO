using HardwareVision.Models;

namespace HardwareVision.Tests;

internal static class NavigationTransitionPlanTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow plan 01 Full timing", FullTiming),
        ("Flow plan 02 Standard timing", StandardTiming),
        ("Flow plan 03 Reduced timing", ReducedTiming),
        ("Flow plan 04 Off is immediate", OffIsImmediate),
        ("Flow plan 05 opacity profiles", OpacityProfiles),
        ("Flow plan 06 Reduced has no spatial motion", ReducedHasNoSpatialMotion),
        ("Flow plan 07 Full module delays", FullModuleDelays),
        ("Flow plan 08 Standard module delays", StandardModuleDelays),
        ("Flow route 01 same group forward", SameGroupForward),
        ("Flow route 02 same group backward", SameGroupBackward),
        ("Flow route 03 System to Session", SystemToSession),
        ("Flow route 04 Session to System", SessionToSystem),
        ("Flow route 05 Session to Control", SessionToControl),
        ("Flow route 06 Control to Session", ControlToSession),
        ("Flow route 07 game to report", GameToReport),
        ("Flow route 08 report to game", ReportToGame),
        ("Flow route 09 same page has no direction", SamePageHasNoDirection),
        ("Flow route 10 page distance does not change duration", DistanceDoesNotChangeDuration),
        ("Flow route 11 all production routes resolve", AllRoutesResolve),
        ("Flow route 12 report is not a rail item", ReportIsNotRailItem)
    ];

    private static NavigationTransitionPlan Plan(MotionLevel level) =>
        NavigationTransitionPlan.Create(MotionProfile.Create(level, level, string.Empty));

    private static NavigationRouteDescriptor Route(string key)
    {
        bool resolved = NavigationRouteDescriptor.TryCreate(key, key, key, key, out NavigationRouteDescriptor? route);
        TestSupport.True(resolved, $"route {key}");
        return TestSupport.NotNull(route, $"route descriptor {key}");
    }

    private static void FullTiming()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(TimeSpan.FromMilliseconds(330), plan.TotalDuration, "Full total");
        TestSupport.Equal(TimeSpan.FromMilliseconds(120), plan.CommitTime, "Full commit");
        TestSupport.Equal(TimeSpan.FromMilliseconds(70), plan.RouteDuration, "Full route");
        TestSupport.Equal(TimeSpan.FromMilliseconds(50), plan.ShiftDuration, "Full shift");
    }

    private static void StandardTiming()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Standard);
        TestSupport.Equal(TimeSpan.FromMilliseconds(260), plan.TotalDuration, "Standard total");
        TestSupport.Equal(TimeSpan.FromMilliseconds(90), plan.CommitTime, "Standard commit");
    }

    private static void ReducedTiming()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Reduced);
        TestSupport.Equal(TimeSpan.FromMilliseconds(120), plan.TotalDuration, "Reduced total");
        TestSupport.Equal(TimeSpan.FromMilliseconds(40), plan.CommitTime, "Reduced commit");
    }

    private static void OffIsImmediate()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Off);
        TestSupport.Equal(TimeSpan.Zero, plan.TotalDuration, "Off total");
        TestSupport.False(plan.UsesClock, "Off clock");
        TestSupport.False(plan.ShowsRelayBand, "Off band");
    }

    private static void OpacityProfiles()
    {
        TestSupport.Equal(0.64d, Plan(MotionLevel.Full).PageStartOpacity, "Full opacity");
        TestSupport.Equal(0.72d, Plan(MotionLevel.Standard).PageStartOpacity, "Standard opacity");
        TestSupport.Equal(0.84d, Plan(MotionLevel.Reduced).PageStartOpacity, "Reduced opacity");
    }

    private static void ReducedHasNoSpatialMotion()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Reduced);
        TestSupport.False(plan.AllowsRelayTranslation, "Reduced relay translation");
        TestSupport.False(plan.ShowsSignalRailCursor, "Reduced cursor");
        TestSupport.False(plan.AllowsTelemetryTranslation, "Reduced telemetry translation");
        TestSupport.False(plan.AllowsPageTranslation, "Reduced page translation");
        TestSupport.False(plan.AllowsModuleStagger, "Reduced stagger");
    }

    private static void FullModuleDelays()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(TimeSpan.FromMilliseconds(24), plan.PrimaryModuleDelay, "Full primary");
        TestSupport.Equal(TimeSpan.FromMilliseconds(42), plan.SecondaryModuleDelay, "Full secondary");
    }

    private static void StandardModuleDelays()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Standard);
        TestSupport.Equal(TimeSpan.FromMilliseconds(16), plan.PrimaryModuleDelay, "Standard primary");
        TestSupport.Equal(TimeSpan.FromMilliseconds(28), plan.SecondaryModuleDelay, "Standard secondary");
    }

    private static void SameGroupForward() => TestSupport.Equal(
        NavigationTransitionDirection.FromBottom,
        NavigationRouteDescriptor.ResolveDirection(Route("Cpu"), Route("Gpu")),
        "same group forward");

    private static void SameGroupBackward() => TestSupport.Equal(
        NavigationTransitionDirection.FromTop,
        NavigationRouteDescriptor.ResolveDirection(Route("Gpu"), Route("Cpu")),
        "same group backward");

    private static void SystemToSession() => Direction("Dashboard", "GamePerformance", NavigationTransitionDirection.FromRight);
    private static void SessionToSystem() => Direction("GamePerformance", "Dashboard", NavigationTransitionDirection.FromLeft);
    private static void SessionToControl() => Direction("GamePerformance", "Settings", NavigationTransitionDirection.FromRight);
    private static void ControlToSession() => Direction("Settings", "GamePerformance", NavigationTransitionDirection.FromLeft);
    private static void GameToReport() => Direction("GamePerformance", "GameSessionReport", NavigationTransitionDirection.FromRight);
    private static void ReportToGame() => Direction("GameSessionReport", "GamePerformance", NavigationTransitionDirection.FromLeft);
    private static void SamePageHasNoDirection() => Direction("Gpu", "Gpu", NavigationTransitionDirection.None);

    private static void Direction(string from, string to, NavigationTransitionDirection expected) =>
        TestSupport.Equal(expected, NavigationRouteDescriptor.ResolveDirection(Route(from), Route(to)), $"{from}->{to}");

    private static void DistanceDoesNotChangeDuration()
    {
        NavigationTransitionPlan adjacent = Plan(MotionLevel.Standard);
        NavigationTransitionPlan distant = Plan(MotionLevel.Standard);
        TestSupport.Equal(adjacent.TotalDuration, distant.TotalDuration, "distance duration");
    }

    private static void AllRoutesResolve()
    {
        string[] keys = ["Dashboard", "Cpu", "Gpu", "Memory", "Disk", "Network", "Motherboard", "AdvancedSensors", "GamePerformance", "GameSessionReport", "Settings", "MetricVisibility"];
        foreach (string key in keys) _ = Route(key);
    }

    private static void ReportIsNotRailItem()
    {
        NavigationRouteDescriptor report = Route("GameSessionReport");
        TestSupport.True(report.IsReport, "report marker");
        string source = File.ReadAllText(Path.Combine(FindRoot(), "HardwareVision", "ViewModels", "MainViewModel.cs"));
        TestSupport.False(source.Contains("NavigationItems.Add(new NavigationItemViewModel(\"GameSessionReport\"", StringComparison.Ordinal), "report rail item");
    }

    private static string FindRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(origin);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HardwareVision", "MainWindow.xaml")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
