using System.Reflection;

namespace HardwareVision.Utilities;

public static class ApplicationMetadata
{
    public const string ProductName = "HardwareVision";

    public static string ProductVersion { get; } =
        typeof(ApplicationMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(ApplicationMetadata).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    public static string DisplayName { get; } = $"{ProductName} {ProductVersion}";
}
