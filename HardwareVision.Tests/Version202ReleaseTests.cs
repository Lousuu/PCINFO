namespace HardwareVision.Tests;

internal static class Version202ReleaseTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Version 2.0.2 project version", () => Property("<Version>2.0.2</Version>")),
        ("Version 2.0.2 assembly version", () => Property("<AssemblyVersion>2.0.2.0</AssemblyVersion>")),
        ("Version 2.0.2 file version", () => Property("<FileVersion>2.0.2.0</FileVersion>")),
        ("Version 2.0.2 informational version", () => Property("<InformationalVersion>2.0.2</InformationalVersion>"))
    ];
    private static void Property(string value) => TestSupport.True(
        TraceworkPilotSource.Read("HardwareVision", "HardwareVision.csproj").Contains(value, StringComparison.Ordinal), value);
}
