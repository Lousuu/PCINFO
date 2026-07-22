namespace HardwareVision.Tests;

internal static class SettingsWorkspaceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Settings workspace 01 Control Index removed", () => Absent("CONTROL INDEX")),
        ("Settings workspace 02 category rail removed", () => Absent("SettingsCategoryRail")),
        ("Settings workspace 03 wide full width", () => Contains("WideColumnSpan=\"12\"")),
        ("Settings workspace 04 standard full width", () => Contains("StandardColumnSpan=\"8\"")),
        ("Settings workspace 05 workspace has no outer frame", () => Contains("Background=\"Transparent\"", "BorderThickness=\"0\"", "Padding=\"0\"")),
        ("Settings workspace 06 theme bindings remain", () => Contains("SelectThemeCommand", "ClassicTheme", "TraceworkTheme")),
        ("Settings workspace 07 motion bindings remain", () => Contains("SelectMotionLevelCommand", "MotionOptions")),
        ("Settings workspace 08 startup settings remain", () => Contains("AutoStartEnabled", "StartMinimizedToTray", "CloseToTray")),
        ("Settings workspace 09 polling settings remain", () => Contains("RefreshIntervalSeconds", "BackgroundRefreshIntervalSeconds")),
        ("Settings workspace 10 hardware rescan remains", () => Contains("RescanHardwareCommand")),
        ("Settings workspace 11 session and diagnostics remain", () => Contains("RecordGameSessions", "OpenGameSessionDirectoryCommand", "ExportSensorDiagnosticsCommand")),
        ("Settings workspace 12 no new persistence binding", PersistenceGuard)
    ];

    private static string Source => TraceworkPilotSource.Read("HardwareVision", "Views", "Settings", "TraceworkSettingsLayout.xaml");
    private static void Contains(params string[] values) { foreach (string value in values) TestSupport.True(Source.Contains(value, StringComparison.Ordinal), value); }
    private static void Absent(string value) => TestSupport.False(Source.Contains(value, StringComparison.Ordinal), value);
    private static void PersistenceGuard() { Absent("SaveSettingsAsync"); Absent("SettingsService"); }
}
