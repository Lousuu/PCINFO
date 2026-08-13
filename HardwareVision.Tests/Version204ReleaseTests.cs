using HardwareVision.Utilities;

namespace HardwareVision.Tests;

internal static class Version204ReleaseTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Version 2.0.4 project version", () => Property("<Version>2.0.4</Version>")),
        ("Version 2.0.4 assembly version", () => Property("<AssemblyVersion>2.0.4.0</AssemblyVersion>")),
        ("Version 2.0.4 file version", () => Property("<FileVersion>2.0.4.0</FileVersion>")),
        ("Version 2.0.4 informational version", () => Property("<InformationalVersion>2.0.4</InformationalVersion>")),
        ("Version 2.0.4 runtime product version", () => TestSupport.Equal("2.0.4", ApplicationMetadata.ProductVersion, "product version")),
        ("Version 2.0.4 visible application version", () => TestSupport.Equal("HardwareVision 2.0.4", ApplicationMetadata.DisplayName, "visible version")),
        ("Version 2.0.4 Dashboard and shell share assembly identity", VisibleVersionUsesAssemblyIdentity)
    ];

    private static void Property(string value) => TestSupport.True(
        TraceworkPilotSource.Read("HardwareVision", "HardwareVision.csproj")
            .Contains(value, StringComparison.Ordinal),
        value);

    private static void VisibleVersionUsesAssemblyIdentity()
    {
        string dashboard = TraceworkPilotSource.Read("HardwareVision", "ViewModels", "DashboardViewModel.cs");
        string shell = TraceworkPilotSource.Read("HardwareVision", "ViewModels", "MainViewModel.cs");
        TestSupport.True(dashboard.Contains("ApplicationMetadata.DisplayName", StringComparison.Ordinal), "Dashboard version source");
        TestSupport.True(shell.Contains("ApplicationMetadata.DisplayName", StringComparison.Ordinal), "shell version source");
        TestSupport.False(dashboard.Contains("HardwareVision 2.0.4", StringComparison.Ordinal), "Dashboard hard-coded version");
        TestSupport.False(shell.Contains("HardwareVision 2.0.4", StringComparison.Ordinal), "shell hard-coded version");
    }
}
