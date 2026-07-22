namespace HardwareVision.Tests;

internal static class PerformanceRegressionGuardTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Performance guard 01 no added DispatcherTimer", () => NoNewApi("DispatcherTimer")),
        ("Performance guard 02 no CompositionTarget Rendering", () => NoNewApi("CompositionTarget.Rendering")),
        ("Performance guard 03 no infinite animation", InfiniteAnimation),
        ("Performance guard 04 no duplicate PageHost", SinglePageHost),
        ("Performance guard 05 no duplicate CurrentPage binding", SingleCurrentPage),
        ("Performance guard 06 no blur shader or screenshot", ForbiddenVisuals),
        ("Performance guard 07 advanced projection creates snapshots", SnapshotProjection),
        ("Performance guard 08 responsive placements avoid objects", PlacementShape),
        ("Performance guard 09 chart skips hidden rendering", ChartVisibility),
        ("Performance guard 10 no new third party dependency", Dependencies)
    ];

    private static string Root => FindRoot();
    private static IEnumerable<string> Sources() => Directory.EnumerateFiles(Path.Combine(Root, "HardwareVision"), "*.*", SearchOption.AllDirectories).Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));
    private static void NoNewApi(string api)
    {
        string changedScope = string.Join('\n', new[] { Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs"), Read("HardwareVision", "Controls", "TraceworkResponsiveGrid.cs"), Read("HardwareVision", "Themes", "Controls.xaml") });
        TestSupport.False(changedScope.Contains(api, StringComparison.Ordinal), api);
    }
    private static void InfiniteAnimation() { string controls = Read("HardwareVision", "Themes", "Controls.xaml"); TestSupport.False(controls.Contains("RepeatBehavior=\"Forever\"", StringComparison.Ordinal), "forever"); TestSupport.False(controls.Contains("AutoReverse=\"True\"", StringComparison.Ordinal), "auto reverse"); }
    private static void SinglePageHost() { string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml"); TestSupport.Equal(1, TraceworkPilotSource.Count(shell, "x:Name=\"PageHost\""), "PageHost"); }
    private static void SingleCurrentPage() { string shell = Read("HardwareVision", "Views", "Shell", "MainShellHost.xaml"); TestSupport.Equal(1, TraceworkPilotSource.Count(shell, "Content=\"{Binding CurrentPage}\""), "CurrentPage binding"); }
    private static void ForbiddenVisuals() { string changed = string.Join('\n', Sources().Where(path => path.Contains("Tracework", StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText)); foreach (string value in new[] { "BlurEffect", "PixelShader", "RenderTargetBitmap", "VisualBrush" }) TestSupport.False(changed.Contains(value, StringComparison.Ordinal), value); }
    private static void SnapshotProjection() { string source = Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs"); TestSupport.True(source.Contains("DetailSensorRowSnapshot[]", StringComparison.Ordinal), "snapshot array"); TestSupport.False(source.Contains("Select(DetailSensorRowViewModel.FromReading)", StringComparison.Ordinal), "temporary rows"); }
    private static void PlacementShape() { string source = Read("HardwareVision", "Controls", "TraceworkResponsiveGrid.cs"); TestSupport.True(source.Contains("readonly record struct Placement", StringComparison.Ordinal), "value placement"); }
    private static void ChartVisibility() { string source = Read("HardwareVision", "Controls", "RealtimeLineChart.cs"); TestSupport.True(source.Contains("if (!IsVisible)", StringComparison.Ordinal), "hidden render guard"); TestSupport.True(source.Contains("pen.Freeze()", StringComparison.Ordinal), "frozen pen"); }
    private static void Dependencies() { string project = Read("HardwareVision", "HardwareVision.csproj"); TestSupport.Equal(4, TraceworkPilotSource.Count(project, "<PackageReference"), "package count"); }
    private static string FindRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(origin);
            while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "HardwareVision", "HardwareVision.csproj"))) return directory.FullName; directory = directory.Parent; }
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
