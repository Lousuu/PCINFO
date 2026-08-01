using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class GameFpsCadenceTests
{
    private const string Header =
        "Application,ProcessID,SwapChainAddress,PresentRuntime,FrameTime,MsBetweenPresents,MsBetweenDisplayChange,FrameType";

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("FPS cadence 01 no frame generation uses display cadence", NoFrameGeneration),
        ("FPS cadence 02 two-times frame generation reports displayed FPS", TwoTimesFrameGeneration),
        ("FPS cadence 03 variable frame generation keeps per-row display cadence", VariableFrameGeneration),
        ("FPS cadence 04 missing display cadence falls back to present", DisplayMissingFallsBackToPresent),
        ("FPS cadence 05 missing display and present falls back to application", PresentMissingFallsBackToApplication),
        ("FPS cadence 06 legacy stored frame remains compatible", LegacyStoredFrameIsCompatible),
        ("FPS cadence 07 optional CSV cadence columns append after legacy fields", CsvColumnsAreAppendOnly),
        ("FPS cadence 08 alternating generated rows keep display cadence", AlternatingGeneratedRows),
        ("FPS cadence 09 dropped and repeated rows use recorded display cadence", DroppedAndRepeatedRows),
        ("FPS cadence 10 multiple swap chains retain identity and cadence", MultipleSwapChains),
        ("FPS cadence 11 frame generation on off transition is not hardcoded", FrameGenerationTransition)
    ];

    private static void NoFrameGeneration()
    {
        GameFrameSample sample = Parse("game.exe,42,0x1,DXGI,8.333,8.333,8.333,Application");
        TestSupport.Nearly(1000d / 8.333d, sample.PrimaryFps!.Value, "primary FPS");
        TestSupport.Equal(GameFpsSource.DisplayCadence, sample.PrimaryFpsSource, "source");
    }

    private static void TwoTimesFrameGeneration()
    {
        GameFrameSample application = Parse(
            "game.exe,42,0x1,DXGI,16.667,8.333,8.333,Application");
        GameFrameSample generated = Parse(
            "game.exe,42,0x1,DXGI,NA,8.333,8.333,IntelXEFG");
        TestSupport.Nearly(1000d / 16.667d, application.ApplicationFps!.Value, "application FPS");
        TestSupport.Nearly(1000d / 8.333d, application.DisplayedFps!.Value, "displayed FPS");
        TestSupport.Nearly(1000d / 8.333d, application.PrimaryFps!.Value, "application row primary FPS");
        TestSupport.Nearly(1000d / 8.333d, generated.PrimaryFps!.Value, "generated row primary FPS");
    }

    private static void VariableFrameGeneration()
    {
        double[] displayTimes = [8.333d, 6.944d, 10d, 8.333d];
        foreach (double displayTime in displayTimes)
        {
            GameFrameSample sample = Parse(
                $"game.exe,42,0x1,DXGI,16.667,8.333,{displayTime.ToString(System.Globalization.CultureInfo.InvariantCulture)},Application");
            TestSupport.Nearly(1000d / displayTime, sample.PrimaryFps!.Value, "variable display FPS");
            TestSupport.Equal(GameFpsSource.DisplayCadence, sample.PrimaryFpsSource, "variable source");
        }
    }

    private static void DisplayMissingFallsBackToPresent()
    {
        GameFrameSample sample = Parse("game.exe,42,0x1,DXGI,16.667,8.333,NA,Application");
        TestSupport.Nearly(1000d / 8.333d, sample.PrimaryFps!.Value, "present fallback FPS");
        TestSupport.Equal(GameFpsSource.PresentCadence, sample.PrimaryFpsSource, "present fallback source");
    }

    private static void PresentMissingFallsBackToApplication()
    {
        GameFrameSample sample = Parse("game.exe,42,0x1,DXGI,16.667,NA,NA,Application");
        TestSupport.Nearly(1000d / 16.667d, sample.PrimaryFps!.Value, "application fallback FPS");
        TestSupport.Equal(GameFpsSource.ApplicationCadence, sample.PrimaryFpsSource, "application fallback source");
    }

    private static void LegacyStoredFrameIsCompatible()
    {
        PresentMonCsvParser parser = new(Guid.NewGuid(), 42, "game.exe");
        TestSupport.Equal(
            PresentMonCsvParseKind.HeaderAccepted,
            parser.ParseLine("CaptureSessionId,ProcessID,FrameTimeMs").Kind,
            "legacy header");
        GameFrameSample sample = parser.ParseLine($"{Guid.NewGuid():N},42,16.667").Sample!;
        TestSupport.Nearly(1000d / 16.667d, sample.PrimaryFps!.Value, "legacy FPS");
        TestSupport.Equal(GameFpsSource.CompatibilityFallback, sample.PrimaryFpsSource, "legacy source");
    }

    private static void CsvColumnsAreAppendOnly()
    {
        string[] fields = PresentMonCsvParser.ParseColumns(GameCsvFormatting.Header).ToArray();
        TestSupport.Equal("CaptureElapsedSeconds", fields[20], "last legacy column");
        TestSupport.Equal("ApplicationFrameTimeMs", fields[21], "first appended column");
        TestSupport.Equal("PrimaryFpsSource", fields[^1], "source column");
    }

    private static void AlternatingGeneratedRows()
    {
        string[] frameTypes = ["Application", "IntelXEFG", "Application", "IntelXEFG"];
        foreach (string frameType in frameTypes)
        {
            GameFrameSample sample = Parse(
                $"game.exe,42,0x1,DXGI,16.667,8.333,8.333,{frameType}");
            TestSupport.Nearly(1000d / 8.333d, sample.PrimaryFps!.Value, frameType);
        }
    }

    private static void DroppedAndRepeatedRows()
    {
        GameFrameSample repeated = Parse(
            "game.exe,42,0x1,DXGI,16.667,8.333,16.667,Repeated");
        GameFrameSample displayed = Parse(
            "game.exe,42,0x1,DXGI,16.667,8.333,8.333,Application");
        TestSupport.Nearly(1000d / 16.667d, repeated.PrimaryFps!.Value, "repeat cadence");
        TestSupport.Nearly(1000d / 8.333d, displayed.PrimaryFps!.Value, "display cadence");
    }

    private static void MultipleSwapChains()
    {
        GameFrameSample game = Parse(
            "game.exe,42,0xGAME,DXGI,16.667,8.333,8.333,Application");
        GameFrameSample overlay = Parse(
            "game.exe,42,0xOVERLAY,DXGI,33.333,33.333,33.333,Application");
        TestSupport.Equal("0xGAME", game.SwapChainAddress, "game swap chain");
        TestSupport.Equal("0xOVERLAY", overlay.SwapChainAddress, "overlay swap chain");
        TestSupport.True(game.PrimaryFps > overlay.PrimaryFps, "cadences remain isolated");
    }

    private static void FrameGenerationTransition()
    {
        GameFrameSample off = Parse(
            "game.exe,42,0x1,DXGI,16.667,16.667,16.667,Application");
        GameFrameSample on = Parse(
            "game.exe,42,0x1,DXGI,16.667,8.333,8.333,Application");
        TestSupport.Nearly(1000d / 16.667d, off.PrimaryFps!.Value, "FG off");
        TestSupport.Nearly(1000d / 8.333d, on.PrimaryFps!.Value, "FG on");
        TestSupport.False(
            File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "HardwareVision",
                    "Services",
                    "PresentMonCsvParser.cs"))
                .Contains("* 2", StringComparison.Ordinal),
            "no hardcoded FPS multiplier");
    }

    private static GameFrameSample Parse(string row)
    {
        PresentMonCsvParser parser = new(Guid.NewGuid(), 42, "game.exe");
        TestSupport.Equal(
            PresentMonCsvParseKind.HeaderAccepted,
            parser.ParseLine(Header).Kind,
            "header");
        PresentMonCsvParseResult result = parser.ParseLine(row);
        TestSupport.Equal(PresentMonCsvParseKind.Sample, result.Kind, "sample");
        return result.Sample!;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? candidate = new(Directory.GetCurrentDirectory());
        while (candidate is not null)
        {
            if (File.Exists(
                    Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
