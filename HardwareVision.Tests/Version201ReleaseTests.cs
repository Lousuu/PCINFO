namespace HardwareVision.Tests;

internal static class Version201ReleaseTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Version 2.0.1 project version", () => Property("<Version>2.0.1</Version>")),
        ("Version 2.0.1 assembly version", () => Property("<AssemblyVersion>2.0.1.0</AssemblyVersion>")),
        ("Version 2.0.1 file version", () => Property("<FileVersion>2.0.1.0</FileVersion>")),
        ("Version 2.0.1 informational version", () => Property("<InformationalVersion>2.0.1</InformationalVersion>"))
    ];
    private static void Property(string value) => TestSupport.True(
        TraceworkPilotSource.Read("HardwareVision", "HardwareVision.csproj").Contains(value, StringComparison.Ordinal), value);
}
