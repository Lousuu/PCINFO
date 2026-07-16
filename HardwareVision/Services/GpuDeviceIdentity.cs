using System.Text.RegularExpressions;

namespace HardwareVision.Services;

internal static partial class GpuDeviceIdentity
{
    [GeneratedRegex(@"GPU-[0-9A-Fa-f-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex UuidPattern();

    [GeneratedRegex(@"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{4}:)?[0-9A-Fa-f]{2}:[0-9A-Fa-f]{2}\.[0-7](?![0-9A-Fa-f])", RegexOptions.CultureInvariant)]
    private static partial Regex PciPattern();

    [GeneratedRegex(@"/(?:nvml|gpu(?:-[^/]+)?)/(?<index>[0-9]+)(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceIndexPattern();

    public static string? TryExtractStableKey(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;
        string value = identifier.Trim();
        Match uuid = UuidPattern().Match(value);
        if (uuid.Success) return "uuid:" + uuid.Value.ToUpperInvariant();

        Match pci = PciPattern().Match(value);
        if (pci.Success) return "pci:" + pci.Value.ToUpperInvariant();

        int pnp = value.IndexOf("PCI\\", StringComparison.OrdinalIgnoreCase);
        if (pnp >= 0) return "pnp:" + value[pnp..].ToUpperInvariant();

        Match deviceIndex = DeviceIndexPattern().Match(value.Replace('\\', '/'));
        if (deviceIndex.Success) return "gpu-index:" + deviceIndex.Groups["index"].Value;

        if (value.StartsWith('/'))
        {
            int first = value.IndexOf('/', 1);
            int second = first < 0 ? -1 : value.IndexOf('/', first + 1);
            if (second > 0) return "lhm:" + value[..second].ToLowerInvariant();
        }

        return null;
    }

    public static string? TryExtractCommonStableKey(IReadOnlyList<string> identifiers)
    {
        string? common = null;
        for (int index = 0; index < identifiers.Count; index++)
        {
            string? candidate = TryExtractStableKey(identifiers[index]);
            if (candidate is null) continue;
            if (common is null)
            {
                common = candidate;
            }
            else if (!string.Equals(common, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return common;
    }
}
