namespace HardwareVision.Tests;

internal static class RelayBandVisualTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Relay band visual 01 layered template", LayeredTemplate),
        ("Relay band visual 02 leading edge", LeadingEdge),
        ("Relay band visual 03 trailing edge", () => HasXaml("PART_TrailingEdge")),
        ("Relay band visual 04 two traces", TwoTraces),
        ("Relay band visual 05 two nodes", TwoNodes),
        ("Relay band visual 06 internal pulse", () => HasXaml("PART_InternalPulse")),
        ("Relay band visual 07 route code", () => HasXaml("PART_RouteCode")),
        ("Relay band visual 08 width clamp", () => HasCode("Math.Clamp(ActualWidth * 0.22d, 160d, 300d)")),
        ("Relay band visual 09 height clamp", () => HasCode("Math.Clamp(ActualHeight * 0.22d, 120d, 220d)")),
        ("Relay band visual 10 cubic motion", CubicMotion),
        ("Relay band visual 11 center equals commit", CenterEqualsCommit),
        ("Relay band visual 12 Full pulse only", () => HasCode("snapshot.Plan.EffectiveLevel != MotionLevel.Full")),
        ("Relay band visual 13 Standard retains nodes", () => HasCode("PlayNode(nodeA")),
        ("Relay band visual 14 Reduced hides internals", ReducedHidesInternals),
        ("Relay band visual 15 center lock profiles", CenterLockProfiles),
        ("Relay band visual 16 committed lock only", CommittedLockOnly),
        ("Relay band visual 17 cleanup", Cleanup),
        ("Relay band visual 18 no loop", NoLoop)
    ];

    private static string Xaml => FlowRelayVisualSource.Read("HardwareVision", "Themes", "Tracework", "NavigationMotion.xaml");
    private static string Code => FlowRelayVisualSource.Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
    private static void HasXaml(string value) => TestSupport.True(Xaml.Contains(value, StringComparison.Ordinal), value);
    private static void HasCode(string value) => TestSupport.True(Code.Contains(value, StringComparison.Ordinal), value);

    private static void LayeredTemplate()
    {
        foreach (string part in new[] { "PART_BandRoot", "PART_BandBody", "PART_LeadingEdge", "PART_TrailingEdge", "PART_TraceA", "PART_TraceB", "PART_NodeA", "PART_NodeB", "PART_InternalPulse", "PART_RouteCode", "PART_CenterLock", "PART_Translate" })
            HasXaml(part);
    }

    private static void LeadingEdge() { HasXaml("PART_LeadingEdge"); HasXaml("Width=\"2\""); }
    private static void TwoTraces() { HasXaml("PART_TraceA"); HasXaml("PART_TraceB"); }
    private static void TwoNodes() { HasXaml("PART_NodeA"); HasXaml("PART_NodeB"); }
    private static void CubicMotion() { HasCode("EasingDoubleKeyFrame"); HasCode("EasingMode = EasingMode.EaseInOut"); }
    private static void CenterEqualsCommit() { HasCode("KeyTime.FromTimeSpan(snapshot.Plan.CommitTime)"); HasCode("new EasingDoubleKeyFrame(center"); }

    private static void ReducedHidesInternals()
    {
        HasCode("MotionLevel.Reduced");
        HasCode("ClearElement(nodeA, 0d, Visibility.Collapsed)");
        HasCode("ClearElement(nodeB, 0d, Visibility.Collapsed)");
        HasCode("ClearElement(internalPulse, 0d, Visibility.Collapsed)");
    }

    private static void CenterLockProfiles()
    {
        foreach (string duration in new[] { "(18)", "(28)", "(14)", "(10)", "(22)" }) HasCode($"TimeSpan.FromMilliseconds{duration}");
    }

    private static void CommittedLockOnly()
    {
        HasCode("snapshot.Phase == NavigationTransitionPhase.Relay");
        HasCode("snapshot.HasCommitted");
        HasCode("committedVersion != snapshot.Version");
    }

    private static void Cleanup()
    {
        HasCode("ClearElement(bandRoot");
        HasCode("BeginAnimation(TranslateTransform.XProperty, null)");
        HasCode("SizeChanged +=");
    }

    private static void NoLoop()
    {
        TestSupport.False(Code.Contains("RepeatBehavior.Forever", StringComparison.Ordinal), "looping animation");
        TestSupport.False(Code.Contains("DispatcherTimer", StringComparison.Ordinal), "timer");
    }
}
