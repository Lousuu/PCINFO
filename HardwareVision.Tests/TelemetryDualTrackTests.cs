namespace HardwareVision.Tests;

internal static class TelemetryDualTrackTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Telemetry dual track 01 clipped viewport", ClippedViewport),
        ("Telemetry dual track 02 source layer", () => HasXaml("PART_SourceLayer")),
        ("Telemetry dual track 03 target layer", () => HasXaml("PART_TargetLayer")),
        ("Telemetry dual track 04 independent text", IndependentText),
        ("Telemetry dual track 05 source opacity", () => HasCode("Fade(sourceLayer, 0.28d, 0d")),
        ("Telemetry dual track 06 target opacity", () => HasCode("Fade(targetLayer, 0.18d, 1d")),
        ("Telemetry dual track 07 Full horizontal offsets", () => Offset("sourceHorizontal = full ? 8d : 6d", "targetHorizontal = full ? 10d : 7d")),
        ("Telemetry dual track 08 Full vertical offsets", () => Offset("sourceVertical = full ? 6d : 4d", "targetVertical = full ? 8d : 5d")),
        ("Telemetry dual track 09 Standard horizontal offsets", () => Offset("sourceHorizontal = full ? 8d : 6d", "targetHorizontal = full ? 10d : 7d")),
        ("Telemetry dual track 10 Standard vertical offsets", () => Offset("sourceVertical = full ? 6d : 4d", "targetVertical = full ? 8d : 5d")),
        ("Telemetry dual track 11 all directions", AllDirections),
        ("Telemetry dual track 12 code parallax", () => HasCode("* -0.4d")),
        ("Telemetry dual track 13 Reduced crossfade", () => HasCode("!snapshot.Plan.AllowsTelemetryTranslation")),
        ("Telemetry dual track 14 commit announces target only", TargetOnlyAutomation),
        ("Telemetry dual track 15 subtitle settles last", SubtitleSettlesLast),
        ("Telemetry dual track 16 cleanup", Cleanup)
    ];

    private static string Xaml => FlowRelayVisualSource.Read("HardwareVision", "Themes", "Tracework", "NavigationMotion.xaml");
    private static string Code => FlowRelayVisualSource.Read("HardwareVision", "Controls", "TelemetryTitleTransitionHost.cs");
    private static void HasXaml(string value) => TestSupport.True(Xaml.Contains(value, StringComparison.Ordinal), value);
    private static void HasCode(string value) => TestSupport.True(Code.Contains(value, StringComparison.Ordinal), value);

    private static void ClippedViewport()
    {
        HasXaml("PART_TrackViewport");
        HasXaml("ClipToBounds=\"True\"");
    }

    private static void IndependentText()
    {
        foreach (string part in new[] { "PART_SourceCode", "PART_SourceTitle", "PART_SourceSubtitle", "PART_TargetCode", "PART_TargetTitle", "PART_TargetSubtitle" })
            HasXaml(part);
        HasCode("snapshot.OriginTitle");
        HasCode("snapshot.TargetTitle");
    }

    private static void Offset(string source, string target)
    {
        HasCode(source);
        HasCode(target);
    }

    private static void AllDirections()
    {
        foreach (string direction in new[] { "FromRight", "FromLeft", "FromBottom", "FromTop" }) HasCode(direction);
    }

    private static void TargetOnlyAutomation()
    {
        HasCode("AutomationLiveSetting.Off");
        HasCode("committed ? AutomationLiveSetting.Polite");
        HasCode("committed ? string.Empty : currentTitle");
    }

    private static void SubtitleSettlesLast()
    {
        HasCode("Math.Min(70d");
        HasCode("Fade(targetSubtitle, 0d, 1d");
    }

    private static void Cleanup()
    {
        foreach (string part in new[] { "sourceLayer", "targetLayer", "sourceTranslate", "targetTranslate", "sourceCodeTranslate", "targetCodeTranslate" })
            HasCode(part);
        HasCode("BeginAnimation(TranslateTransform.XProperty, null)");
        HasCode("BeginAnimation(OpacityProperty, null)");
    }
}
