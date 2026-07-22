namespace HardwareVision.Tests;

internal static class TraceworkFullExpansionLayoutTests
{
    private sealed record PageSpec(string Page, string Subject, string Grid, string[] Bindings);

    private static readonly PageSpec[] Pages =
    [
        new("Gpu", "RENDER PIPELINE", "GpuRenderPipelineGrid", ["GpuDevices", "SelectedGpu", "Charts", "SensorRows", "InfoProjection.VisibleMetrics"]),
        new("Memory", "MEMORY TOPOLOGY", "MemoryTopologyGrid", ["MemoryModules", "OverviewProjection.PrimaryMetric", "ProfessionalProjection.VisibleMetrics", "HasMemoryModules"]),
        new("Disk", "STORAGE HEALTH", "StorageHealthGrid", ["DiskDevices", "OverviewProjection.PrimaryMetric", "ProfessionalProjection.VisibleMetrics", "StatusText"]),
        new("Network", "LINK TELEMETRY", "NetworkLinkTelemetryGrid", ["NetworkAdapters", "SelectedAdapter", "ShowVirtualAdapters", "ProfessionalProjection.VisibleMetrics"]),
        new("Motherboard", "PLATFORM IDENTITY", "PlatformIdentityGrid", ["BoardProjection.VisibleMetrics", "BiosProjection.VisibleMetrics", "SensorRows", "NoSensorData"]),
        new("AdvancedSensors", "SENSOR MATRIX", "AdvancedSensorMatrixGrid", ["SensorRows", "StatusText"]),
        new("GamePerformance", "CAPTURE CONTROL", "GameCaptureControlGrid", ["ProcessSearchText", "SelectedProcess", "StartCaptureCommand", "StopCaptureCommand", "Charts", "PerformanceLimitEvents", "RecentRecords"]),
        new("GameSessionReport", "SESSION DIAGNOSIS", "SessionDiagnosisGrid", ["BackCommand", "SelectedChart, Mode=TwoWay", "PerformanceLimitEvents", "HardwareMetrics", "NestedScrollViewerBehavior.BubbleMouseWheelAtBoundary"]),
        new("Settings", "SYSTEM CONTROL", "SettingsWorkspaceGrid", ["SelectThemeCommand", "SelectMotionLevelCommand", "AutoStartEnabled", "RefreshIntervalSeconds", "RecordGameSessions"]),
        new("MetricVisibility", "METRIC ROUTING", "MetricRoutingGrid", ["Categories", "SelectedCategory, Mode=TwoWay", "IsVisible, Mode=TwoWay", "RestoreDefaultsCommand", "ShowCoreOnlyCommand", "ShowAllProfessionalCommand"])
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        foreach (PageSpec spec in Pages)
        {
            PageSpec page = spec;
            tests.Add(($"Full expansion {page.Page} subject", () => AssertContains(page, page.Subject)));
            tests.Add(($"Full expansion {page.Page} responsive grid", () => AssertContains(page, $"x:Name=\"{page.Grid}\"")));
            tests.Add(($"Full expansion {page.Page} bindings", () => AssertBindings(page)));
            tests.Add(($"Full expansion {page.Page} motion roles bounded", () => AssertMotionRoles(page)));
            tests.Add(($"Full expansion {page.Page} forbidden static effects absent", () => AssertForbiddenAbsent(page)));
        }

        tests.Add(("Full expansion primitives capacity field", () => AssertPrimitive("TraceworkCapacityFieldStyle")));
        tests.Add(("Full expansion primitives capacity track", () => AssertPrimitive("TraceworkCapacityTrackStyle")));
        tests.Add(("Full expansion primitives topology matrix", () => AssertPrimitive("TraceworkTopologyMatrixStyle")));
        tests.Add(("Full expansion primitives control workspace", () => AssertPrimitive("TraceworkControlWorkspaceStyle")));
        tests.Add(("Full expansion primitives timeline field", () => AssertPrimitive("TraceworkTimelineFieldStyle")));
        tests.Add(("Full expansion primitives identity plate", () => AssertPrimitive("TraceworkIdentityPlateStyle")));
        tests.Add(("Full expansion capacity colors stay telemetry neutral", CapacityColorsStayTelemetryNeutral));
        tests.Add(("Full expansion pages contain no literal hex colors", PagesContainNoLiteralHexColors));
        tests.Add(("Full expansion Dashboard remains 7/5", () => AssertPilot("Dashboard", "WideColumnSpan=\"7\"", "WideColumn=\"7\"")));
        tests.Add(("Full expansion CPU remains 4/8", () => AssertPilot("Cpu", "WideColumnSpan=\"4\"", "WideColumn=\"4\"")));
        return tests;
    }

    private static string Read(PageSpec page) =>
        TraceworkPilotSource.Read("HardwareVision", "Views", page.Page, $"Tracework{page.Page}Layout.xaml");

    private static void AssertContains(PageSpec page, string value) =>
        TestSupport.True(Read(page).Contains(value, StringComparison.Ordinal), $"{page.Page}: {value}");

    private static void AssertBindings(PageSpec page)
    {
        string source = Read(page);
        foreach (string binding in page.Bindings)
            TestSupport.True(source.Contains(binding, StringComparison.Ordinal), $"{page.Page} binding {binding}");
    }

    private static void AssertMotionRoles(PageSpec page)
    {
        string source = Read(page);
        TestSupport.True(TraceworkPilotSource.Count(source, "NavigationMotion.Role=\"Primary\"") <= 1, $"{page.Page} primary role");
        TestSupport.True(TraceworkPilotSource.Count(source, "NavigationMotion.Role=\"Secondary\"") <= 1, $"{page.Page} secondary role");
        TestSupport.False(source.Contains("ItemContainerStyle=\"{StaticResource NavigationMotion", StringComparison.Ordinal), $"{page.Page} item motion");
    }

    private static void AssertForbiddenAbsent(PageSpec page)
    {
        string source = Read(page);
        foreach (string forbidden in new[] { "DispatcherTimer", "CompositionTarget.Rendering", "BlurEffect", "ShaderEffect", "VisualBrush", "<ImageBrush" })
            TestSupport.False(source.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{page.Page} {forbidden}");
    }

    private static void AssertPrimitive(string key)
    {
        string source = TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Pages.xaml");
        TestSupport.True(source.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal), key);
    }

    private static void CapacityColorsStayTelemetryNeutral()
    {
        string source = TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Pages.xaml");
        int start = source.IndexOf("TraceworkCapacityFieldStyle", StringComparison.Ordinal);
        int end = source.IndexOf("TraceworkTopologyMatrixStyle", start, StringComparison.Ordinal);
        string capacity = source[start..end];
        TestSupport.True(capacity.Contains("TraceworkTelemetryBrush", StringComparison.Ordinal), "telemetry capacity brush");
        TestSupport.False(capacity.Contains("TraceworkAttentionBrush", StringComparison.Ordinal), "no amber capacity decoration");
        TestSupport.False(capacity.Contains("TraceworkFaultBrush", StringComparison.Ordinal), "no coral capacity decoration");
    }

    private static void PagesContainNoLiteralHexColors()
    {
        foreach (PageSpec page in Pages)
        {
            string source = Read(page);
            TestSupport.False(System.Text.RegularExpressions.Regex.IsMatch(source, "#[0-9A-Fa-f]{6,8}"), $"{page.Page} literal color");
        }
    }

    private static void AssertPilot(string page, params string[] values)
    {
        string source = TraceworkPilotSource.Read("HardwareVision", "Views", page, $"Tracework{page}Layout.xaml");
        foreach (string value in values)
            TestSupport.True(source.Contains(value, StringComparison.Ordinal), $"{page} {value}");
    }
}
