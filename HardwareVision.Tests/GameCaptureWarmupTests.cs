using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class GameCaptureWarmupTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return ("Warmup 01 startup outlier never reaches accepted stream", StartupOutlierIsDiscarded);
        yield return ("Warmup 02 stable 1000 FPS is accepted", StableThousandFpsIsAccepted);
        yield return ("Warmup 03 auxiliary zero is valid and huge value is null", AuxiliaryMetricsAreSanitizedPerField);
        yield return ("Warmup 04 dominant swap chain excludes overlay", DominantSwapChainExcludesOverlay);
        yield return ("Warmup 05 legacy single stream remains compatible", LegacySingleStreamWorks);
        yield return ("Warmup 06 non-monotonic elapsed time is rejected", NonMonotonicElapsedIsRejected);
        yield return ("Warmup 07 each new session starts cold", NewSessionStartsCold);
    }

    private static void StartupOutlierIsDiscarded()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        TestSupport.False(pipeline.Process(Sample(0.001, "0x1"), start).IsAccepted, "first outlier");
        for (int index = 1; index <= 4; index++)
        {
            pipeline.Process(Sample(1000d / 60d, "0x1"), start.AddMilliseconds(index * 10));
        }
        GameFrameValidationResult result = pipeline.Process(Sample(1000d / 60d, "0x1"), start.AddMilliseconds(50));
        TestSupport.True(result.IsAccepted, "stable frame accepted");
        TestSupport.Nearly(60d, result.Sample!.Fps, "accepted FPS");
        TestSupport.True(pipeline.GetDiagnostics(start.AddMilliseconds(50)).WarmupDiscardedSampleCount >= 4, "warmup count");
    }

    private static void StableThousandFpsIsAccepted()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        GameFrameValidationResult result = default;
        for (int index = 0; index < 7; index++)
        {
            result = pipeline.Process(Sample(1d, "0xFAST"), start.AddMilliseconds(index * 10));
        }
        TestSupport.True(result.IsAccepted, "stable high FPS accepted");
        TestSupport.Nearly(1000d, result.Sample!.Fps, "1000 FPS preserved");
    }

    private static void AuxiliaryMetricsAreSanitizedPerField()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 6; index++)
        {
            pipeline.Process(Sample(10d, null), start.AddMilliseconds(index * 10));
        }
        GameFrameSample source = Sample(10d, null, cpuBusy: 0d, gpuTime: 999_999d, latency: 20d);
        GameFrameValidationResult result = pipeline.Process(source, start.AddMilliseconds(70));
        TestSupport.True(result.IsAccepted, "frame remains accepted");
        TestSupport.Nearly(0d, result.Sample!.CpuBusyMs, "zero remains valid");
        TestSupport.Equal<double?>(null, result.Sample.GpuTimeMs, "huge GPU field sanitized");
        TestSupport.Nearly(20d, result.Sample.DisplayLatencyMs, "other field retained");
    }

    private static void DominantSwapChainExcludesOverlay()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        pipeline.Process(Sample(5d, "overlay"), start);
        for (int index = 1; index <= 6; index++)
        {
            pipeline.Process(Sample(16d, "main"), start.AddMilliseconds(index * 10));
        }
        GameFrameValidationResult main = pipeline.Process(Sample(16d, "main"), start.AddMilliseconds(80));
        GameFrameValidationResult overlay = pipeline.Process(Sample(5d, "overlay"), start.AddMilliseconds(90));
        TestSupport.True(main.IsAccepted, "main accepted");
        TestSupport.False(overlay.IsAccepted, "overlay rejected");
        TestSupport.Equal(GameFrameSampleQuality.NonPrimarySwapChain, overlay.Quality, "overlay quality");
        TestSupport.Equal("main", pipeline.GetDiagnostics(start.AddMilliseconds(90)).PrimarySwapChainAddress, "selected primary");
    }

    private static void LegacySingleStreamWorks()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        GameFrameValidationResult result = default;
        for (int index = 0; index < 7; index++)
        {
            result = pipeline.Process(Sample(8d, null), start.AddMilliseconds(index * 10));
        }
        TestSupport.True(result.IsAccepted, "single stream accepted");
        TestSupport.True(pipeline.GetDiagnostics(start.AddMilliseconds(70)).UsedCompatibilityFallback, "fallback diagnostic");
    }

    private static void NonMonotonicElapsedIsRejected()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        pipeline.Process(Sample(10d, null, elapsed: 1d), now);
        GameFrameValidationResult result = pipeline.Process(Sample(10d, null, elapsed: 0.5d), now.AddMilliseconds(10));
        TestSupport.False(result.IsAccepted, "regression rejected");
        TestSupport.Equal(GameFrameSampleQuality.NonMonotonicTimestamp, result.Quality, "quality");
    }

    private static void NewSessionStartsCold()
    {
        GameFrameValidationPipeline first = NewPipeline();
        first.AcceptHeader();
        GameFrameValidationPipeline second = NewPipeline();
        second.AcceptHeader();
        TestSupport.False(second.Process(Sample(16d, null), DateTimeOffset.UtcNow).IsAccepted, "new pipeline warms up");
    }

    private static GameFrameValidationPipeline NewPipeline() => new(new GameCaptureWarmupOptions
    {
        MinimumCandidateFrames = 4,
        MinimumStableDuration = TimeSpan.FromMilliseconds(20),
        MaximumWarmupDuration = TimeSpan.FromMilliseconds(100),
        StabilityWindowSize = 4,
        MinimumStableValues = 3,
        PrimarySwapChainInactivity = TimeSpan.FromMilliseconds(20),
        SwapChainSwitchConfirmation = TimeSpan.FromMilliseconds(20),
        MinimumSwitchCandidateFrames = 3
    });

    private static GameFrameSample Sample(
        double frameTime,
        string? swapChain,
        double? cpuBusy = 2d,
        double? gpuTime = 3d,
        double? latency = 4d,
        double? elapsed = null) => new()
    {
        CaptureSessionId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        ProcessId = 1,
        ProcessName = "game.exe",
        FrameTimeMs = frameTime,
        Fps = 1000d / frameTime,
        CpuBusyMs = cpuBusy,
        GpuTimeMs = gpuTime,
        DisplayLatencyMs = latency,
        SwapChainAddress = swapChain,
        CaptureElapsedSeconds = elapsed
    };
}
