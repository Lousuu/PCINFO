namespace HardwareVision.Tests;

internal static class TraceworkClassicPageProtectionTests
{
    private sealed record ClassicSpec(string Page, string Hash);

    private static readonly ClassicSpec[] Pages =
    [
        new("Gpu", "E68921F3B2208C85397578CA7AFDF6E6EFE9D1967CC2F8FF01B67851B81421DF"),
        new("Memory", "79A728DB8A9F1BC23C804B660E3F9E84573A699B07424008297AC45DD3F2C07B"),
        new("Disk", "9FA758E009ABE2A435275C6D21047577E7CA04B96D337CB739317EBAA50E1014"),
        new("Network", "F751897B5D24948C0B2B7096CC296C3F50B71862571B2248D412912225D3D7DC"),
        new("Motherboard", "57A852FA89732B35EE70B93B7491FC7ABB396A503F25A87FF49FB80B84CA1320"),
        new("AdvancedSensors", "D28F13601A48BE7C6D9E4EDBE63659D3BEB17C8578690D42E1D964D913266D76"),
        new("GamePerformance", "019B7224E4459337BECAAF51BA52A563D5FCCF6916D2D2C9AA5B9D206F5F02B8"),
        new("GameSessionReport", "4583D0DB287AE93D2F002AB498515F13F4F45CC33138A9CC8C3E789A8BE08468"),
        new("Settings", "A24FE934DEAB69B9B1054A0F98C6BE2DA84B310B790B0279826776537A4227E2"),
        new("MetricVisibility", "B9256323F49F890D8363C65A4C81447499036BDEEFC50E2DBF93515D3D4E4C5D")
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        foreach (ClassicSpec spec in Pages)
        {
            ClassicSpec page = spec;
            tests.Add(($"Classic expansion {page.Page} normalized hash", () => AssertHash(page)));
            tests.Add(($"Classic expansion {page.Page} dual template", () => AssertDualTemplate(page)));
        }
        return tests;
    }

    private static void AssertHash(ClassicSpec page)
    {
        string source = TraceworkPilotSource.Read("HardwareVision", "Views", page.Page, $"Classic{page.Page}Layout.xaml");
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        TestSupport.Equal(page.Hash, actual, $"Classic {page.Page}");
    }

    private static void AssertDualTemplate(ClassicSpec page)
    {
        string source = TraceworkPilotSource.Read("HardwareVision", "Views", $"{page.Page}View.xaml");
        TestSupport.True(source.Contains($"Classic{page.Page}Template", StringComparison.Ordinal), $"{page.Page} Classic template");
        TestSupport.True(source.Contains($"Tracework{page.Page}Template", StringComparison.Ordinal), $"{page.Page} Tracework template");
    }
}
