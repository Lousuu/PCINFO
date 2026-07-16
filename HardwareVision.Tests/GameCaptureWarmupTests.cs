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
        yield return ("Warmup 08 stable 60 FPS rejects isolated micro frame", StableSixtyRejectsMicroFrame);
        yield return ("Warmup 09 sustained transition to 240 FPS is confirmed", SustainedTransitionTo240IsConfirmed);
        yield return ("Warmup 10 1000 FPS rejects isolated 10000 FPS frame", ThousandFpsRejectsTenThousandSpike);
        yield return ("Warmup 11 duplicate capture elapsed is rejected", DuplicateCaptureElapsedIsRejected);
        yield return ("Warmup 12 duplicate explicit timestamp is rejected", DuplicateExplicitTimestampIsRejected);
        yield return ("Warmup 13 regressed explicit timestamp is rejected", RegressedExplicitTimestampIsRejected);
        yield return ("Warmup 14 swap chain windows remain isolated", SwapChainWindowsRemainIsolated);
        yield return ("Warmup 15 presentation confidence keeps game primary", PresentationConfidenceKeepsGamePrimary);
        yield return ("Warmup 16 short primary interruption does not switch", ShortPrimaryInterruptionDoesNotSwitch);
        yield return ("Warmup 17 confirmed candidate replaces inactive primary", ConfirmedCandidateReplacesInactivePrimary);
        yield return ("Warmup 18 isolated GPU time spike only clears GPU time", GpuTimeSpikeOnlyClearsGpuTime);
        yield return ("Warmup 19 display latency spike does not pollute statistics", DisplayLatencySpikeDoesNotPolluteStatistics);
        yield return ("Warmup 20 FPS is recomputed from validated frame time", FpsIsRecomputedFromFrameTime);
        yield return ("Warmup 21 rejected micro frame never reaches CSV", TestSupport.Run(RejectedMicroFrameNeverReachesCsvAsync));
        yield return ("Warmup 22 legitimate slow frame is preserved", LegitimateSlowFrameIsPreserved);
        yield return ("Warmup 23 sustained auxiliary level is confirmed", SustainedAuxiliaryLevelIsConfirmed);
        yield return ("Warmup 24 missing timestamps use explicit fallback diagnostics", MissingTimestampsUseFallbackDiagnostics);
        yield return ("Warmup 25 negative auxiliary field does not drop frame", NegativeAuxiliaryFieldDoesNotDropFrame);
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
        TestSupport.Equal(GameFrameSampleQuality.RegressedCaptureElapsed, result.Quality, "quality");
    }

    private static void NewSessionStartsCold()
    {
        GameFrameValidationPipeline first = NewPipeline();
        first.AcceptHeader();
        GameFrameValidationPipeline second = NewPipeline();
        second.AcceptHeader();
        TestSupport.False(second.Process(Sample(16d, null), DateTimeOffset.UtcNow).IsAccepted, "new pipeline warms up");
    }

    private static void StableSixtyRejectsMicroFrame()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(1000d / 60d, "main", out DateTimeOffset next);
        GameFrameValidationResult result = pipeline.Process(Sample(0.001d, "main"), next);
        TestSupport.False(result.IsAccepted, "micro frame accepted");
        TestSupport.Equal(GameFrameSampleQuality.FrameTimeOutlier, result.Quality, "micro frame quality");
        TestSupport.Equal(1L, pipeline.GetDiagnostics(next).FrameTimeOutlierSampleCount, "outlier count");
    }

    private static void SustainedTransitionTo240IsConfirmed()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(1000d / 60d, "main", out DateTimeOffset next);
        GameFrameValidationResult result = default;
        for (int index = 0; index < 4; index++)
        {
            result = pipeline.Process(Sample(1000d / 240d, "main"), next.AddMilliseconds(index * 5));
        }
        TestSupport.True(result.IsAccepted, "sustained 240 FPS was not accepted");
        TestSupport.Nearly(240d, result.Sample!.Fps, "transition FPS");
        TestSupport.True(pipeline.GetDiagnostics(next).StableLevelTransitionConfirmedCount >= 1, "transition confirmation");
    }

    private static void ThousandFpsRejectsTenThousandSpike()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(1d, "fast", out DateTimeOffset next);
        GameFrameValidationResult result = pipeline.Process(Sample(0.1d, "fast"), next);
        TestSupport.False(result.IsAccepted, "10000 FPS spike accepted");
        TestSupport.Equal(GameFrameSampleQuality.FrameTimeOutlier, result.Quality, "10000 FPS quality");
    }

    private static void DuplicateCaptureElapsedIsRejected()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        pipeline.Process(Sample(10d, null, elapsed: 1d), now);
        GameFrameValidationResult result = pipeline.Process(Sample(10d, null, elapsed: 1d), now.AddMilliseconds(1));
        TestSupport.False(result.IsAccepted, "duplicate capture elapsed accepted");
        TestSupport.Equal(GameFrameSampleQuality.DuplicateCaptureElapsed, result.Quality, "duplicate capture quality");
    }

    private static void DuplicateExplicitTimestampIsRejected()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        pipeline.Process(Sample(10d, null, timestamp: timestamp, hasExplicitTimestamp: true), timestamp);
        GameFrameValidationResult result = pipeline.Process(
            Sample(10d, null, timestamp: timestamp, hasExplicitTimestamp: true),
            timestamp.AddMilliseconds(1));
        TestSupport.False(result.IsAccepted, "duplicate explicit timestamp accepted");
        TestSupport.Equal(GameFrameSampleQuality.DuplicateExplicitTimestamp, result.Quality, "duplicate explicit quality");
    }

    private static void RegressedExplicitTimestampIsRejected()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        pipeline.Process(Sample(10d, null, timestamp: timestamp, hasExplicitTimestamp: true), timestamp);
        GameFrameValidationResult result = pipeline.Process(
            Sample(10d, null, timestamp: timestamp.AddMilliseconds(-1), hasExplicitTimestamp: true),
            timestamp.AddMilliseconds(1));
        TestSupport.False(result.IsAccepted, "regressed explicit timestamp accepted");
        TestSupport.Equal(GameFrameSampleQuality.RegressedExplicitTimestamp, result.Quality, "regressed explicit quality");
    }

    private static void SwapChainWindowsRemainIsolated()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(16d, "main", out DateTimeOffset next);
        for (int index = 0; index < 6; index++)
        {
            pipeline.Process(Sample(1d, "overlay"), next.AddMilliseconds(index));
        }
        GameFrameValidationResult result = pipeline.Process(Sample(4d, "main"), next.AddMilliseconds(10));
        TestSupport.False(result.IsAccepted, "overlay window contaminated primary baseline");
        TestSupport.Equal(GameFrameSampleQuality.FrameTimeOutlier, result.Quality, "isolated-window quality");
    }

    private static void PresentationConfidenceKeepsGamePrimary()
    {
        GameFrameValidationPipeline pipeline = new(new GameCaptureWarmupOptions
        {
            MinimumCandidateFrames = 8,
            MinimumStableDuration = TimeSpan.FromMilliseconds(20),
            MaximumWarmupDuration = TimeSpan.FromMilliseconds(200),
            StabilityWindowSize = 12,
            MinimumStableValues = 6,
            FrameTimeTransitionConfirmationFrames = 3,
            AuxiliaryTransitionConfirmationFrames = 3
        });
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 10; index++)
        {
            pipeline.Process(Sample(5d, "overlay", presentMode: "Composed: Flip", frameType: "Application"), start.AddMilliseconds(index * 4));
            pipeline.Process(Sample(5d, "overlay", presentMode: "Composed: Flip", frameType: "Application"), start.AddMilliseconds(index * 4 + 1));
            pipeline.Process(Sample(16d, "game", presentMode: "Hardware: Independent Flip", frameType: "Application"), start.AddMilliseconds(index * 4 + 2));
        }
        TestSupport.Equal("game", pipeline.GetDiagnostics(start.AddMilliseconds(50)).PrimarySwapChainAddress, "game primary");
    }

    private static void ShortPrimaryInterruptionDoesNotSwitch()
    {
        GameFrameValidationPipeline pipeline = NewPipeline(new GameCaptureWarmupOptions
        {
            MinimumCandidateFrames = 4,
            MinimumStableDuration = TimeSpan.FromMilliseconds(20),
            MaximumWarmupDuration = TimeSpan.FromMilliseconds(100),
            StabilityWindowSize = 4,
            MinimumStableValues = 3,
            FrameTimeTransitionConfirmationFrames = 3,
            AuxiliaryTransitionConfirmationFrames = 3,
            PrimarySwapChainInactivity = TimeSpan.FromMilliseconds(100),
            SwapChainSwitchConfirmation = TimeSpan.FromMilliseconds(20),
            MinimumSwitchCandidateFrames = 3
        });
        DateTimeOffset next = Warm(pipeline, 16d, "main");
        for (int index = 0; index < 5; index++)
        {
            pipeline.Process(Sample(8d, "candidate"), next.AddMilliseconds(index * 10));
        }
        TestSupport.Equal("main", pipeline.GetDiagnostics(next.AddMilliseconds(50)).PrimarySwapChainAddress, "short interruption switched primary");
    }

    private static void ConfirmedCandidateReplacesInactivePrimary()
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        DateTimeOffset next = Warm(pipeline, 16d, "main");
        GameFrameValidationResult result = default;
        bool switched = false;
        for (int index = 0; index < 8; index++)
        {
            result = pipeline.Process(Sample(8d, "candidate"), next.AddMilliseconds(50 + index * 10));
            switched |= result.PrimarySwapChainChanged;
        }
        TestSupport.True(result.IsAccepted, "confirmed candidate was not accepted");
        TestSupport.True(switched, "switch diagnostic missing");
        TestSupport.Equal("candidate", pipeline.GetDiagnostics(next.AddMilliseconds(150)).PrimarySwapChainAddress, "candidate not promoted");
    }

    private static void GpuTimeSpikeOnlyClearsGpuTime()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, "main", out DateTimeOffset next);
        for (int index = 0; index < 10; index++)
        {
            pipeline.Process(Sample(10d, "main", gpuTime: 3d, latency: 20d), next.AddMilliseconds(index));
        }
        GameFrameValidationResult result = pipeline.Process(
            Sample(10d, "main", cpuBusy: 2d, gpuTime: 500d, latency: 20d),
            next.AddMilliseconds(20));
        TestSupport.True(result.IsAccepted, "auxiliary spike dropped frame");
        TestSupport.Equal<double?>(null, result.Sample!.GpuTimeMs, "GPU spike retained");
        TestSupport.Nearly(2d, result.Sample.CpuBusyMs, "CPU field changed");
        TestSupport.Nearly(20d, result.Sample.DisplayLatencyMs, "display field changed");
    }

    private static void DisplayLatencySpikeDoesNotPolluteStatistics()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, "main", out DateTimeOffset next);
        List<GameFrameSample> accepted = [];
        for (int index = 0; index < 10; index++)
        {
            GameFrameValidationResult result = pipeline.Process(
                Sample(10d, "main", latency: 20d),
                next.AddMilliseconds(index));
            if (result.IsAccepted) accepted.Add(result.Sample!);
        }
        GameFrameValidationResult spike = pipeline.Process(
            Sample(10d, "main", latency: 5000d),
            next.AddMilliseconds(20));
        accepted.Add(spike.Sample!);
        GamePerformanceSnapshot snapshot = GameFrameStatisticsCalculator.Calculate(accepted, TimeSpan.FromHours(1));
        TestSupport.Nearly(20d, snapshot.AverageDisplayLatencyMs, "display spike polluted average");
    }

    private static void FpsIsRecomputedFromFrameTime()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, "main", out DateTimeOffset next);
        GameFrameSample source = Sample(10d, "main").WithFps(999_999d);
        GameFrameValidationResult result = pipeline.Process(source, next);
        TestSupport.Nearly(100d, result.Sample!.Fps, "FPS was not recomputed");
    }

    private static Task RejectedMicroFrameNeverReachesCsvAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid sessionId = Guid.NewGuid();
        GameFrameValidationPipeline pipeline = NewPipeline();
        pipeline.AcceptHeader();
        await using CsvGameSessionRecorder recorder = new(directory, frameStorageModeProvider: () => GameSessionFrameStorageMode.PlainCsv);
        GameSessionStartInfo info = TestSupport.StartInfo(sessionId);
        await recorder.StartAsync(info);
        DateTimeOffset start = info.CaptureStartedAt;
        for (int index = 0; index < 14; index++)
        {
            GameFrameValidationResult validated = pipeline.Process(
                Sample(1000d / 60d, "main", elapsed: index / 60d).WithSession(sessionId),
                start.AddMilliseconds(index * 17));
            if (validated.IsAccepted) recorder.TryRecord(validated.Sample!, sessionId, info.Generation);
        }
        GameFrameValidationResult spike = pipeline.Process(
            Sample(0.001d, "main", elapsed: 1d).WithSession(sessionId),
            start.AddSeconds(1));
        TestSupport.False(spike.IsAccepted, "micro frame reached recorder path");
        GameSessionRecordInfo record = (await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))!;
        string csv = await File.ReadAllTextAsync(record.CsvPath);
        TestSupport.False(csv.Contains("0.001", StringComparison.Ordinal), "micro frame written to CSV");
    });

    private static void LegitimateSlowFrameIsPreserved()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, "main", out DateTimeOffset next);
        GameFrameValidationResult result = pipeline.Process(Sample(1000d, "main"), next);
        TestSupport.True(result.IsAccepted, "legitimate slow frame was treated as a fast outlier");
        TestSupport.Nearly(1d, result.Sample!.Fps, "slow-frame FPS");
    }

    private static void SustainedAuxiliaryLevelIsConfirmed()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, "main", out DateTimeOffset next);
        for (int index = 0; index < 10; index++)
        {
            pipeline.Process(Sample(10d, "main", gpuTime: 3d), next.AddMilliseconds(index));
        }
        GameFrameValidationResult result = default;
        for (int index = 0; index < 4; index++)
        {
            result = pipeline.Process(Sample(10d, "main", gpuTime: 500d), next.AddMilliseconds(20 + index));
        }
        TestSupport.True(result.IsAccepted, "sustained auxiliary level dropped frame");
        TestSupport.Nearly(500d, result.Sample!.GpuTimeMs, "sustained auxiliary level not accepted");
    }

    private static void MissingTimestampsUseFallbackDiagnostics()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, null, out DateTimeOffset next);
        GameFrameValidationResult result = pipeline.Process(Sample(10d, null), next);
        GameFrameQualityDiagnostics diagnostics = pipeline.GetDiagnostics(next);
        TestSupport.True(result.IsAccepted, "timestamp-less compatibility frame rejected");
        TestSupport.True(diagnostics.MissingTimestampSampleCount > 0L, "missing timestamp count");
        TestSupport.True(diagnostics.CompatibilityFallbackSampleCount > 0L, "fallback sample count");
        TestSupport.True(diagnostics.UsedCompatibilityFallback, "fallback mode flag");
    }

    private static void NegativeAuxiliaryFieldDoesNotDropFrame()
    {
        GameFrameValidationPipeline pipeline = WarmPipeline(10d, "main", out DateTimeOffset next);
        GameFrameValidationResult result = pipeline.Process(Sample(10d, "main", gpuTime: -1d), next);
        TestSupport.True(result.IsAccepted, "negative auxiliary field dropped frame");
        TestSupport.Equal<double?>(null, result.Sample!.GpuTimeMs, "negative auxiliary field retained");
        TestSupport.True(pipeline.GetDiagnostics(next).InvalidAuxiliaryMetricFieldCount >= 1L, "negative auxiliary diagnostic");
    }

    private static GameFrameValidationPipeline WarmPipeline(double frameTime, string? swapChain, out DateTimeOffset next)
    {
        GameFrameValidationPipeline pipeline = NewPipeline();
        next = Warm(pipeline, frameTime, swapChain);
        return pipeline;
    }

    private static DateTimeOffset Warm(GameFrameValidationPipeline pipeline, double frameTime, string? swapChain)
    {
        pipeline.AcceptHeader();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        for (int index = 0; index < 12; index++)
        {
            pipeline.Process(Sample(frameTime, swapChain), start.AddMilliseconds(index * 10));
        }
        return start.AddMilliseconds(150);
    }

    private static GameFrameValidationPipeline NewPipeline(GameCaptureWarmupOptions? options = null) => new(options ?? new GameCaptureWarmupOptions
    {
        MinimumCandidateFrames = 4,
        MinimumStableDuration = TimeSpan.FromMilliseconds(20),
        MaximumWarmupDuration = TimeSpan.FromMilliseconds(100),
        StabilityWindowSize = 4,
        MinimumStableValues = 3,
        FrameTimeTransitionConfirmationFrames = 3,
        AuxiliaryTransitionConfirmationFrames = 3,
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
        double? elapsed = null,
        DateTimeOffset? timestamp = null,
        bool hasExplicitTimestamp = false,
        string? presentMode = null,
        string? frameType = null) => new()
    {
        CaptureSessionId = Guid.NewGuid(),
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        HasExplicitTimestamp = hasExplicitTimestamp,
        ProcessId = 1,
        ProcessName = "game.exe",
        FrameTimeMs = frameTime,
        Fps = 1000d / frameTime,
        CpuBusyMs = cpuBusy,
        GpuTimeMs = gpuTime,
        DisplayLatencyMs = latency,
        SwapChainAddress = swapChain,
        CaptureElapsedSeconds = elapsed,
        PresentMode = presentMode,
        FrameType = frameType
    };

    private static GameFrameSample WithFps(this GameFrameSample sample, double fps) => Copy(sample, fps: fps);

    private static GameFrameSample WithSession(this GameFrameSample sample, Guid sessionId) => Copy(sample, sessionId: sessionId);

    private static GameFrameSample Copy(GameFrameSample sample, double? fps = null, Guid? sessionId = null) => new()
    {
        CaptureSessionId = sessionId ?? sample.CaptureSessionId,
        Timestamp = sample.Timestamp,
        HasExplicitTimestamp = sample.HasExplicitTimestamp,
        CaptureElapsedSeconds = sample.CaptureElapsedSeconds,
        ProcessId = sample.ProcessId,
        ProcessName = sample.ProcessName,
        FrameTimeMs = sample.FrameTimeMs,
        Fps = fps ?? sample.Fps,
        CpuBusyMs = sample.CpuBusyMs,
        CpuWaitMs = sample.CpuWaitMs,
        GpuLatencyMs = sample.GpuLatencyMs,
        GpuTimeMs = sample.GpuTimeMs,
        GpuBusyMs = sample.GpuBusyMs,
        GpuWaitMs = sample.GpuWaitMs,
        RenderLatencyMs = sample.RenderLatencyMs,
        DisplayLatencyMs = sample.DisplayLatencyMs,
        DisplayedTimeMs = sample.DisplayedTimeMs,
        ClickToPhotonLatencyMs = sample.ClickToPhotonLatencyMs,
        Runtime = sample.Runtime,
        PresentMode = sample.PresentMode,
        SwapChainAddress = sample.SwapChainAddress,
        FrameType = sample.FrameType
    };
}
