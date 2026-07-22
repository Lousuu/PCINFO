using System.Globalization;

namespace HardwareVision.Tests;

internal static class TraceworkColorSystemTests
{
    private static readonly (string Key, string Value)[] Colors =
    [
        ("TraceworkCanvasColor", "#0B0D10"),
        ("TraceworkSurfaceColor", "#12171C"),
        ("TraceworkSurfaceRaisedColor", "#182027"),
        ("TraceworkSurfaceSoftColor", "#202932"),
        ("TraceworkDividerColor", "#34414B"),
        ("TraceworkTraceGreyColor", "#64717B"),
        ("TraceworkDustGreyColor", "#939CA3"),
        ("TraceworkPaperColor", "#E7E4DD"),
        ("TraceworkTextSecondaryColor", "#B2BAC0"),
        ("TraceworkIonVioletColor", "#8B7CFF"),
        ("TraceworkSignalCyanColor", "#58C4D6"),
        ("TraceworkPhosphorMintColor", "#79D9B1"),
        ("TraceworkWarmAmberColor", "#D6A75B"),
        ("TraceworkAlertCoralColor", "#D46A6A")
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string Name, Action Test)> tests = [];
        for (int index = 0; index < Colors.Length; index++)
        {
            (string key, string value) = Colors[index];
            tests.Add(($"Visual color {index + 1:00} {key}", () => AssertColor(key, value)));
        }

        tests.Add(("Visual color 15 semantic brushes are exposed", SemanticBrushesAreExposed));
        tests.Add(("Visual color 16 soft semantic brushes are exposed", SoftSemanticBrushesAreExposed));
        tests.Add(("Visual color 17 technical grid stays static and faint", TechnicalGridStaysStaticAndFaint));
        tests.Add(("Visual color 18 paper contrast passes on canvas", () => AssertContrast("#E7E4DD", "#0B0D10", 4.5d)));
        tests.Add(("Visual color 19 secondary contrast passes on surface", () => AssertContrast("#B2BAC0", "#12171C", 4.5d)));
        tests.Add(("Visual color 20 identity contrast passes on canvas", () => AssertContrast("#8B7CFF", "#0B0D10", 4.5d)));
        tests.Add(("Visual color 21 telemetry contrast passes on canvas", () => AssertContrast("#58C4D6", "#0B0D10", 4.5d)));
        tests.Add(("Visual color 22 active contrast passes on canvas", () => AssertContrast("#79D9B1", "#0B0D10", 4.5d)));
        tests.Add(("Visual color 23 attention contrast passes on canvas", () => AssertContrast("#D6A75B", "#0B0D10", 4.5d)));
        tests.Add(("Visual color 24 fault contrast passes on canvas", () => AssertContrast("#D46A6A", "#0B0D10", 4.5d)));
        tests.Add(("Visual color 25 paper contrast passes on surface", () => AssertContrast("#E7E4DD", "#12171C", 4.5d)));
        tests.Add(("Visual color 26 button text contrast passes", () => AssertContrast("#0B0D10", "#8B7CFF", 4.5d)));
        tests.Add(("Visual color 27 focus contrast passes on canvas", () => AssertContrast("#8B7CFF", "#0B0D10", 3d)));
        tests.Add(("Visual color 28 state colors are not page backgrounds", StateColorsAreNotPageBackgrounds));
        tests.Add(("Visual color 29 attention and fault are not ambient decoration", AttentionAndFaultAreNotAmbientDecoration));
        return tests;
    }

    private static void AssertColor(string key, string value)
    {
        string colors = TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Colors.xaml");
        TestSupport.True(colors.Contains($"x:Key=\"{key}\">{value}</Color>", StringComparison.Ordinal), $"{key} exact value");
    }

    private static void SemanticBrushesAreExposed()
    {
        string colors = TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Colors.xaml");
        foreach (string key in new[] { "Canvas", "Surface", "SurfaceRaised", "SurfaceSoft", "Divider", "TraceGrey", "DustGrey", "Paper", "TextSecondary", "Identity", "Telemetry", "Active", "Attention", "Fault" })
            TestSupport.True(colors.Contains($"x:Key=\"Tracework{key}Brush\"", StringComparison.Ordinal), $"{key} brush");
    }

    private static void SoftSemanticBrushesAreExposed()
    {
        string colors = TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Colors.xaml");
        foreach (string key in new[] { "IdentitySoft", "TelemetrySoft", "ActiveSoft", "AttentionSoft", "FaultSoft" })
            TestSupport.True(colors.Contains($"x:Key=\"Tracework{key}Brush\"", StringComparison.Ordinal), $"{key} brush");
    }

    private static void TechnicalGridStaysStaticAndFaint()
    {
        string colors = TraceworkPilotSource.Read("HardwareVision", "Themes", "Tracework", "Colors.xaml");
        int start = colors.IndexOf("x:Key=\"TraceworkTechnicalGridBrush\"", StringComparison.Ordinal);
        string grid = colors[start..Math.Min(colors.Length, start + 900)];
        TestSupport.True(grid.Contains("Opacity=\"0.03\"", StringComparison.Ordinal), "grid opacity");
        TestSupport.False(grid.Contains("Animation", StringComparison.Ordinal), "grid animation");
        TestSupport.False(grid.Contains("Timer", StringComparison.Ordinal), "grid timer");
    }

    private static void AssertContrast(string foreground, string background, double minimum)
    {
        double ratio = Contrast(foreground, background);
        TestSupport.True(ratio >= minimum, $"contrast {foreground}/{background} is {ratio:0.00}, minimum {minimum:0.00}");
    }

    private static void StateColorsAreNotPageBackgrounds()
    {
        foreach (string layout in new[] { "Dashboard/TraceworkDashboardLayout.xaml", "Cpu/TraceworkCpuLayout.xaml" })
        {
            string page = TraceworkPilotSource.Read("HardwareVision", "Views", layout.Replace('/', Path.DirectorySeparatorChar));
            int rootEnd = page.IndexOf('>');
            string root = page[..rootEnd];
            foreach (string key in new[] { "TraceworkIdentityBrush", "TraceworkTelemetryBrush", "TraceworkActiveBrush", "TraceworkAttentionBrush", "TraceworkFaultBrush" })
                TestSupport.False(root.Contains($"Background=\"{{DynamicResource {key}}}\"", StringComparison.Ordinal), $"{key} page background");
        }
    }

    private static void AttentionAndFaultAreNotAmbientDecoration()
    {
        string pages = TraceworkPilotSource.Read("HardwareVision", "Views", "Dashboard", "TraceworkDashboardLayout.xaml")
            + TraceworkPilotSource.Read("HardwareVision", "Views", "Cpu", "TraceworkCpuLayout.xaml");
        TestSupport.False(pages.Contains("TraceworkAttentionBrush", StringComparison.Ordinal), "ambient attention brush");
        TestSupport.False(pages.Contains("TraceworkFaultBrush", StringComparison.Ordinal), "ambient fault brush");
    }

    private static double Contrast(string first, string second)
    {
        static double Luminance(string hex)
        {
            double Channel(int offset)
            {
                double value = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
                return value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
            }
            return (0.2126d * Channel(1)) + (0.7152d * Channel(3)) + (0.0722d * Channel(5));
        }
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05d) / (Math.Min(a, b) + 0.05d);
    }
}

internal static class TraceworkPilotSource
{
    public static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    public static int Count(string text, string value) => text.Split(value, StringSplitOptions.None).Length - 1;

    public static string Root()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
            for (DirectoryInfo? candidate = new(origin); candidate is not null; candidate = candidate.Parent)
                if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml"))) return candidate.FullName;
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
