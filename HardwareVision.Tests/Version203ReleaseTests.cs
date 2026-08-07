namespace HardwareVision.Tests;

internal static class Version203ReleaseTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Version 2.0.3 project version", () => Property("<Version>2.0.3</Version>")),
        ("Version 2.0.3 assembly version", () => Property("<AssemblyVersion>2.0.3.0</AssemblyVersion>")),
        ("Version 2.0.3 file version", () => Property("<FileVersion>2.0.3.0</FileVersion>")),
        ("Version 2.0.3 informational version", () => Property("<InformationalVersion>2.0.3</InformationalVersion>"))
    ];

    private static void Property(string value) => TestSupport.True(
        TraceworkPilotSource.Read("HardwareVision", "HardwareVision.csproj")
            .Contains(value, StringComparison.Ordinal),
        value);
}
