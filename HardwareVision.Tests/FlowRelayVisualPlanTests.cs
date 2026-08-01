using HardwareVision.Models;

namespace HardwareVision.Tests;

internal static class FlowRelayVisualPlanTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow visual plan 01 Full enter duration", () => Equal(MotionLevel.Full, p => p.PageEnterDuration, TimeSpan.FromMilliseconds(120), "Full enter")),
        ("Flow visual plan 02 Standard enter duration", () => Equal(MotionLevel.Standard, p => p.PageEnterDuration, TimeSpan.FromMilliseconds(95), "Standard enter")),
        ("Flow visual plan 03 Full page opacity", () => Equal(MotionLevel.Full, p => p.PageStartOpacity, 0.78d, "Full opacity")),
        ("Flow visual plan 04 Standard page opacity", () => Equal(MotionLevel.Standard, p => p.PageStartOpacity, 0.84d, "Standard opacity")),
        ("Flow visual plan 05 Reduced page opacity", () => Equal(MotionLevel.Reduced, p => p.PageStartOpacity, 0.90d, "Reduced opacity")),
        ("Flow visual plan 06 Full page offset", () => Equal(MotionLevel.Full, p => p.PageSettleOffset, 5d, "Full offset")),
        ("Flow visual plan 07 Standard page offset", () => Equal(MotionLevel.Standard, p => p.PageSettleOffset, 4d, "Standard offset")),
        ("Flow visual plan 08 Full module delays", FullModuleDelays),
        ("Flow visual plan 09 Standard module delays", StandardModuleDelays),
        ("Flow visual plan 10 Full module opacity", FullModuleOpacity),
        ("Flow visual plan 11 Standard module opacity", StandardModuleOpacity),
        ("Flow visual plan 12 Full module offsets", FullModuleOffsets),
        ("Flow visual plan 13 Standard module offsets", StandardModuleOffsets),
        ("Flow visual plan 14 Reduced has no reveal", ReducedHasNoReveal),
        ("Flow visual plan 15 Off remains clockless", OffRemainsClockless),
        ("Flow visual plan 16 commit timing meets latency budget", CommitTimingMeetsLatencyBudget)
    ];

    private static NavigationTransitionPlan Plan(MotionLevel level) =>
        NavigationTransitionPlan.Create(MotionProfile.Create(level, level, string.Empty));

    private static void Equal<T>(MotionLevel level, Func<NavigationTransitionPlan, T> select, T expected, string message) =>
        TestSupport.Equal(expected, select(Plan(level)), message);

    private static void FullModuleDelays()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(TimeSpan.FromMilliseconds(10), plan.PrimaryEnterDelay, "Full primary delay");
        TestSupport.Equal(TimeSpan.FromMilliseconds(34), plan.SecondaryEnterDelay, "Full secondary delay");
    }

    private static void StandardModuleDelays()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Standard);
        TestSupport.Equal(TimeSpan.FromMilliseconds(8), plan.PrimaryEnterDelay, "Standard primary delay");
        TestSupport.Equal(TimeSpan.FromMilliseconds(24), plan.SecondaryEnterDelay, "Standard secondary delay");
    }

    private static void FullModuleOpacity()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(0.84d, plan.PrimaryModuleStartOpacity, "Full primary opacity");
        TestSupport.Equal(0.72d, plan.SecondaryModuleStartOpacity, "Full secondary opacity");
    }

    private static void StandardModuleOpacity()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Standard);
        TestSupport.Equal(0.88d, plan.PrimaryModuleStartOpacity, "Standard primary opacity");
        TestSupport.Equal(0.80d, plan.SecondaryModuleStartOpacity, "Standard secondary opacity");
    }

    private static void FullModuleOffsets()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Full);
        TestSupport.Equal(4d, plan.PrimaryModuleOffset, "Full primary offset");
        TestSupport.Equal(6d, plan.SecondaryModuleOffset, "Full secondary offset");
    }

    private static void StandardModuleOffsets()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Standard);
        TestSupport.Equal(3d, plan.PrimaryModuleOffset, "Standard primary offset");
        TestSupport.Equal(4d, plan.SecondaryModuleOffset, "Standard secondary offset");
    }

    private static void ReducedHasNoReveal()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Reduced);
        TestSupport.Equal(TimeSpan.Zero, plan.PageRevealDuration, "Reduced reveal duration");
        TestSupport.False(plan.AllowsPageTranslation, "Reduced spatial page motion");
        TestSupport.False(plan.AllowsModuleStagger, "Reduced module stagger");
    }

    private static void OffRemainsClockless()
    {
        NavigationTransitionPlan plan = Plan(MotionLevel.Off);
        TestSupport.False(plan.UsesClock, "Off clock");
        TestSupport.Equal(TimeSpan.Zero, plan.TotalDuration, "Off total");
        TestSupport.Equal(TimeSpan.Zero, plan.PageRevealDuration, "Off reveal");
    }

    private static void CommitTimingMeetsLatencyBudget()
    {
        TestSupport.True(Plan(MotionLevel.Full).CommitTime <= TimeSpan.FromMilliseconds(70), "Full commit");
        TestSupport.True(Plan(MotionLevel.Standard).CommitTime <= TimeSpan.FromMilliseconds(45), "Standard commit");
        TestSupport.Equal(TimeSpan.Zero, Plan(MotionLevel.Reduced).CommitTime, "Reduced commit");
    }
}

internal static class FlowRelayVisualSource
{
    public static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    public static string Root
    {
        get
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
}
