using System.Text.RegularExpressions;

namespace HardwareVision.Tests;

internal static class FlowRelayControlTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow controls 01 single RelayBand overlay", SingleRelayBandOverlay),
        ("Flow controls 02 overlay z-order", OverlayZOrder),
        ("Flow controls 03 RelayBand shares PageHost bounds", RelayBandSharesPageHostBounds),
        ("Flow controls 04 RelayBand is non-focusable", RelayBandIsNonFocusable),
        ("Flow controls 05 RelayBand geometry clamps", RelayBandGeometryClamps),
        ("Flow controls 06 RelayBand supports four directions", RelayBandSupportsFourDirections),
        ("Flow controls 07 Reduced band has no translation", ReducedBandHasNoTranslation),
        ("Flow controls 08 single SignalRail pulse", SingleSignalRailCursor),
        ("Flow controls 09 cursor dimensions and accessibility", CursorDimensionsAndAccessibility),
        ("Flow controls 10 cursor animates transform only", CursorAnimatesTransformOnly),
        ("Flow controls 11 cursor uses real button geometry", CursorUsesRealButtonGeometry),
        ("Flow controls 12 Telemetry host is single", TelemetryHostIsSingle),
        ("Flow controls 13 Telemetry keeps live fields outside", TelemetryKeepsLiveFieldsOutside),
        ("Flow controls 14 Telemetry automation is polite", TelemetryAutomationIsPolite),
        ("Flow controls 15 formal PageHost disables auto mode", FormalPageHostDisablesAutoMode),
        ("Flow controls 16 PageHost exposes explicit settle", PageHostExposesExplicitSettle),
        ("Flow controls 17 PageHost remains single", PageHostRemainsSingle),
        ("Flow controls 18 CurrentPage binding remains single", CurrentPageBindingRemainsSingle),
        ("Flow controls 19 module roles are bounded", ModuleRolesAreBounded),
        ("Flow controls 20 item rows have no roles", ItemRowsHaveNoRoles),
        ("Flow controls 21 motion resources are merged", MotionResourcesAreMerged),
        ("Flow controls 22 new controls avoid forbidden animation APIs", NewControlsAvoidForbiddenApis)
    ];

    private static string Root => FindRoot();
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    private static void SingleRelayBandOverlay()
    {
        string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        TestSupport.Equal(1, Regex.Matches(shell, "<controls:RelayBandOverlay\\b").Count, "relay overlay count");
    }

    private static void OverlayZOrder()
    {
        string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        TestSupport.True(shell.Contains("Panel.ZIndex=\"2\"", StringComparison.Ordinal), "PageHost z-index");
        TestSupport.True(shell.Contains("Panel.ZIndex=\"40\"", StringComparison.Ordinal), "Relay z-index");
        TestSupport.True(shell.Contains("Panel.ZIndex=\"100\"", StringComparison.Ordinal), "Rewire z-index");
    }

    private static void RelayBandSharesPageHostBounds()
    {
        string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml");
        TestSupport.True(shell.Contains("Margin=\"{Binding Margin, ElementName=PageHost}\"", StringComparison.Ordinal), "relay margin binding");
        TestSupport.True(shell.Contains("MaxWidth=\"{Binding MaxWidth, ElementName=PageHost}\"", StringComparison.Ordinal), "relay max width binding");
    }

    private static void RelayBandIsNonFocusable()
    {
        string source = Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
        TestSupport.True(source.Contains("Focusable = false", StringComparison.Ordinal), "relay focusable");
        TestSupport.True(source.Contains("IsTabStop = false", StringComparison.Ordinal), "relay tab stop");
        TestSupport.True(source.Contains("IsHitTestVisible = false", StringComparison.Ordinal), "relay idle hit testing");
    }

    private static void RelayBandGeometryClamps()
    {
        string source = Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
        TestSupport.True(source.Contains("ActualWidth * 0.22d, 160d, 300d", StringComparison.Ordinal), "horizontal clamp");
        TestSupport.True(source.Contains("ActualHeight * 0.22d, 120d, 220d", StringComparison.Ordinal), "vertical clamp");
    }

    private static void RelayBandSupportsFourDirections()
    {
        string source = Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
        foreach (string direction in new[] { "FromLeft", "FromRight", "FromTop", "FromBottom" })
            TestSupport.True(source.Contains(direction, StringComparison.Ordinal), direction);
    }

    private static void ReducedBandHasNoTranslation()
    {
        string source = Read("HardwareVision", "Controls", "RelayBandOverlay.cs");
        TestSupport.True(source.Contains("!snapshot.Plan.AllowsRelayTranslation", StringComparison.Ordinal), "reduced translation gate");
    }

    private static void SingleSignalRailCursor()
    {
        string xaml = Read("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml");
        TestSupport.Equal(1, Regex.Matches(xaml, "x:Name=\"RoutePulse\"").Count, "pulse count");
    }

    private static void CursorDimensionsAndAccessibility()
    {
        string xaml = Read("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml");
        TestSupport.True(xaml.Contains("Width=\"6\"", StringComparison.Ordinal), "cursor width");
        TestSupport.True(xaml.Contains("Height=\"2\"", StringComparison.Ordinal), "cursor height");
        TestSupport.True(xaml.Contains("Focusable=\"False\"", StringComparison.Ordinal), "cursor focus");
        TestSupport.True(xaml.Contains("IsHitTestVisible=\"False\"", StringComparison.Ordinal), "cursor hit test");
    }

    private static void CursorAnimatesTransformOnly()
    {
        string source = Read("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml.cs");
        TestSupport.True(source.Contains("TranslateTransform.YProperty", StringComparison.Ordinal), "cursor translate");
        foreach (string forbidden in new[] { "Canvas.LeftProperty", "MarginProperty", "WidthProperty", "HeightProperty" })
            TestSupport.False(source.Contains(forbidden, StringComparison.Ordinal), forbidden);
    }

    private static void CursorUsesRealButtonGeometry()
    {
        string source = Read("HardwareVision", "Views", "Shell", "TraceworkSignalRail.xaml.cs");
        TestSupport.True(source.Contains("TransformToAncestor(this)", StringComparison.Ordinal), "cursor ancestor geometry");
        TestSupport.True(source.Contains("item.IsSelected", StringComparison.Ordinal), "cursor real selection");
    }

    private static void TelemetryHostIsSingle()
    {
        string xaml = Read("HardwareVision", "Views", "Shell", "TraceworkTelemetrySpine.xaml");
        TestSupport.Equal(1, Regex.Matches(xaml, "<controls:TelemetryTitleTransitionHost\\b").Count, "telemetry host count");
    }

    private static void TelemetryKeepsLiveFieldsOutside()
    {
        string xaml = Read("HardwareVision", "Views", "Shell", "TraceworkTelemetrySpine.xaml");
        TestSupport.True(xaml.Contains("Text=\"LIVE\"", StringComparison.Ordinal), "live remains");
        TestSupport.True(xaml.Contains("Text=\"{Binding StatusText}\"", StringComparison.Ordinal), "status remains");
        TestSupport.False(Read("HardwareVision", "Controls", "TelemetryTitleTransitionHost.cs").Contains("StatusText", StringComparison.Ordinal), "status not animated");
    }

    private static void TelemetryAutomationIsPolite() => TestSupport.True(
        Read("HardwareVision", "Themes", "Tracework", "NavigationMotion.xaml").Contains("AutomationProperties.LiveSetting=\"Polite\"", StringComparison.Ordinal),
        "polite title");

    private static void FormalPageHostDisablesAutoMode() => TestSupport.True(
        Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml").Contains("IsAutoTransitionEnabled=\"False\"", StringComparison.Ordinal),
        "formal auto mode");

    private static void PageHostExposesExplicitSettle()
    {
        string host = Read("HardwareVision", "Controls", "MotionTransitionHost.cs");
        TestSupport.True(host.Contains("public void PlaySettle", StringComparison.Ordinal), "PlaySettle");
        TestSupport.True(host.Contains("public void CancelTransition", StringComparison.Ordinal), "CancelTransition");
        TestSupport.True(host.Contains("public void RestoreFinalState", StringComparison.Ordinal), "RestoreFinalState");
    }

    private static void PageHostRemainsSingle()
    {
        string all = string.Join('\n', Directory.EnumerateFiles(Path.Combine(Root, "HardwareVision", "Views"), "*.xaml", SearchOption.AllDirectories).Select(File.ReadAllText));
        TestSupport.Equal(1, Regex.Matches(all, "x:Name=\"PageHost\"").Count, "PageHost count");
    }

    private static void CurrentPageBindingRemainsSingle()
    {
        string all = string.Join('\n', Directory.EnumerateFiles(Path.Combine(Root, "HardwareVision", "Views"), "*.xaml", SearchOption.AllDirectories).Select(File.ReadAllText));
        TestSupport.Equal(1, Regex.Matches(all, "Binding\\s+CurrentPage\\b").Count, "CurrentPage bindings");
    }

    private static void ModuleRolesAreBounded()
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Root, "HardwareVision", "Views"), "Tracework*Layout.xaml", SearchOption.AllDirectories))
        {
            string xaml = File.ReadAllText(file);
            TestSupport.True(Regex.Matches(xaml, "NavigationMotion.Role=\"Primary\"").Count <= 1, $"primary {file}");
            TestSupport.True(Regex.Matches(xaml, "NavigationMotion.Role=\"Secondary\"").Count <= 1, $"secondary {file}");
        }
    }

    private static void ItemRowsHaveNoRoles()
    {
        foreach (string file in Directory.EnumerateFiles(Path.Combine(Root, "HardwareVision", "Views"), "Tracework*Layout.xaml", SearchOption.AllDirectories))
        {
            string xaml = File.ReadAllText(file);
            TestSupport.False(Regex.IsMatch(xaml, "<(DataGrid|ItemsControl)[^>]*NavigationMotion.Role", RegexOptions.Singleline), $"row role {file}");
        }
    }

    private static void MotionResourcesAreMerged() => TestSupport.True(
        Read("HardwareVision", "App.xaml").Contains("Themes/Tracework/NavigationMotion.xaml", StringComparison.Ordinal),
        "navigation resource merge");

    private static void NewControlsAvoidForbiddenApis()
    {
        string sources = string.Join('\n', new[] { "RelayBandOverlay.cs", "TelemetryTitleTransitionHost.cs", "NavigationMotion.cs" }.Select(file => Read("HardwareVision", "Controls", file)));
        foreach (string forbidden in new[] { "DispatcherTimer", "CompositionTarget.Rendering", "RepeatBehavior", "BlurEffect", "PixelShader", "RenderTargetBitmap", "VisualBrush", "LayoutTransform" })
            TestSupport.False(sources.Contains(forbidden, StringComparison.Ordinal), forbidden);
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
