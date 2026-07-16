using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionReportAccuracyTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Report accuracy 01 native elapsed timestamp aligns frames", TestSupport.Run(NativeElapsedTimestampAlignsAsync)),
        ("Report accuracy 02 wall clock timestamp aligns frames", TestSupport.Run(WallClockTimestampAlignsAsync)),
        ("Report accuracy 03 legacy frame time accumulates", TestSupport.Run(LegacyFrameTimeAccumulatesAsync)),
        ("Report accuracy 04 non-monotonic time is clamped", TestSupport.Run(NonMonotonicTimeIsClampedAsync)),
        ("Report accuracy 05 out-of-range time warns", TestSupport.Run(OutOfRangeTimeWarnsAsync)),
        ("Report accuracy 06 gap-aware coverage excludes missing span", GapAwareCoverageExcludesMissingSpan),
        ("Report accuracy 07 raw statistics preserve spike before downsampling", RawStatisticsPreserveSpike),
        ("Report accuracy 08 synthetic three-hour frame streams stay bounded", SyntheticThreeHourFrameStreamsStayBounded),
        ("Report accuracy 09 three-hour one-second timeline streams and bounds UI", TestSupport.Run(ThreeHourTimelineStreamsAndBoundsUiAsync)),
        ("Report accuracy 10 historical micro frame is filtered", TestSupport.Run(HistoricalMicroFrameIsFilteredAsync)),
        ("Report accuracy 11 duplicate capture elapsed is filtered", TestSupport.Run(DuplicateCaptureElapsedIsFilteredAsync)),
        ("Report accuracy 12 auxiliary spike is sanitized", TestSupport.Run(AuxiliarySpikeIsSanitizedAsync)),
        ("Report accuracy 13 plain and gzip validation agree", TestSupport.Run(PlainAndGZipValidationAgreeAsync)),
        ("Report accuracy 14 sustained 1000 FPS is not underestimated", TestSupport.Run(SustainedThousandFpsIsNotUnderestimatedAsync)),
        ("Report accuracy 15 parser validation recorder report stays consistent", TestSupport.Run(EndToEndValidationAndReportAgreeAsync)),
        ("Report accuracy 16 regressed explicit timestamp is filtered", TestSupport.Run(RegressedExplicitTimestampIsFilteredAsync))
    ];

    private static Task NativeElapsedTimestampAlignsAsync() => WithReportAsync(
        durationSeconds: 10d,
        samples:
        [
            TestSupport.Frame(Guid.Empty, 0d),
            TestSupport.Frame(Guid.Empty, 5d),
            TestSupport.Frame(Guid.Empty, 10d)
        ],
        async report =>
        {
            TestSupport.Equal(FrameTimeAxisSource.NativeTimestamp, report.FrameTimeAxisSource, "time-axis source");
            TestSupport.Nearly(10d, Fps(report).Points[^1].ElapsedSeconds, "last native elapsed time");
            await Task.CompletedTask;
        });

    private static Task WallClockTimestampAlignsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-1);
        GameSessionReport report = await WriteAndLoadAsync(
            directory,
            started,
            8d,
            [
                WallClockFrame(started),
                WallClockFrame(started.AddSeconds(4)),
                WallClockFrame(started.AddSeconds(8))
            ]);
        TestSupport.Equal(FrameTimeAxisSource.WallClockTimestamp, report.FrameTimeAxisSource, "time-axis source");
        TestSupport.Nearly(8d, Fps(report).Points[^1].ElapsedSeconds, "last wall-clock elapsed time");
    });

    private static Task LegacyFrameTimeAccumulatesAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string csv = Path.Combine(directory, "legacy.csv");
        string summaryPath = Path.Combine(directory, "legacy.summary.json");
        await File.WriteAllLinesAsync(csv,
        [
            "ProcessId,ProcessName,FrameTimeMs,FPS",
            "42,legacy,100,10",
            "42,legacy,200,5"
        ]);
        await WriteSummaryAsync(summaryPath, id, started, 1d, Path.GetFileName(csv), writtenSamples: 2);
        GameSessionReport report = await new GameSessionReportService().LoadAsync(Record(csv, summaryPath, started, 1d));
        TestSupport.Equal(FrameTimeAxisSource.AccumulatedFrameTimeFallback, report.FrameTimeAxisSource, "legacy time-axis source");
        TestSupport.Nearly(0.3d, Fps(report).Points[^1].ElapsedSeconds, "accumulated frame duration");
        TestSupport.True(report.UsedHistoricalValidationFallback, "legacy fallback diagnostic");
        TestSupport.True(report.Warnings.Any(item => item.Contains("降级校验", StringComparison.Ordinal)), "legacy fallback warning");
    });

    private static Task NonMonotonicTimeIsClampedAsync() => WithReportAsync(
        10d,
        [TestSupport.Frame(Guid.Empty, 0d), TestSupport.Frame(Guid.Empty, 5d), TestSupport.Frame(Guid.Empty, 3d)],
        report =>
        {
            IReadOnlyList<SessionChartPoint> points = Fps(report).Points;
            TestSupport.True(points.Zip(points.Skip(1)).All(pair => pair.First.ElapsedSeconds <= pair.Second.ElapsedSeconds), "frame axis decreased");
            TestSupport.True(report.Warnings.Count > 0, "non-monotonic timestamp warning missing");
            return Task.CompletedTask;
        });

    private static Task OutOfRangeTimeWarnsAsync() => WithReportAsync(
        10d,
        [TestSupport.Frame(Guid.Empty, 0d), TestSupport.Frame(Guid.Empty, 100d)],
        report =>
        {
            TestSupport.True(report.Warnings.Count > 0, "out-of-range timestamp warning missing");
            TestSupport.True(Fps(report).Points[^1].ElapsedSeconds <= 10d, "out-of-range timestamp was not clamped");
            return Task.CompletedTask;
        });

    private static void GapAwareCoverageExcludesMissingSpan()
    {
        List<SessionChartPoint> points = [];
        for (int second = 0; second <= 20; second++) points.Add(new SessionChartPoint(second, 2000d));
        for (int second = 80; second <= 100; second++) points.Add(new SessionChartPoint(second, 1000d));
        SessionThrottleStatistics statistics = SessionThrottleStatisticsCalculator.CalculateRaw(points, [], 100d);
        TestSupport.True(statistics.GapCount >= 1, "middle gap was not detected");
        TestSupport.True(statistics.DataCoveragePercent is > 35d and < 45d, "gap coverage was not excluded");
        TestSupport.False(statistics.HasSufficientFrequencyCoverage, "low coverage was treated as sufficient");
        TestSupport.True(statistics.AverageFrequencyMHz is null, "insufficient coverage exposed a misleading average");
    }

    private static void RawStatisticsPreserveSpike()
    {
        List<SessionChartPoint> raw = Enumerable.Range(0, 10_000)
            .Select(index => new SessionChartPoint(index * 0.1d, index == 4999 ? 5000d : 1000d))
            .ToList();
        SessionThrottleStatistics statistics = SessionThrottleStatisticsCalculator.CalculateRaw(raw, [], 999.9d);
        IReadOnlyList<SessionChartPoint> displayed = SessionChartDownsampler.Downsample(raw, [], 1500);
        TestSupport.Nearly(5000d, statistics.MaximumFrequencyMHz, "raw maximum");
        TestSupport.True(displayed.Count <= 1500, "display series exceeded point cap");
    }

    private static void SyntheticThreeHourFrameStreamsStayBounded()
    {
        foreach (int fps in new[] { 60, 120, 240 })
        {
            int retained = 0;
            long frames = (long)fps * 3L * 60L * 60L;
            PresentMonCsvParser parser = new(Guid.NewGuid(), 42, "synthetic-game");
            TestSupport.Equal(
                PresentMonCsvParseKind.HeaderAccepted,
                parser.ParseLine("Application,ProcessID,FrameTime").Kind,
                "synthetic CSV header");
            string row = $"synthetic-game,42,{(1000d / fps).ToString("0.######", CultureInfo.InvariantCulture)}";
            Measurement measurement = TestSupport.Measure($"synthetic-3h-{fps}fps", () =>
            {
                long bucket = Math.Max(1L, (long)Math.Ceiling(frames / 1500d));
                for (long index = 0; index < frames; index++)
                {
                    PresentMonCsvParseResult parsed = parser.ParseLine(row);
                    if (parsed.Kind == PresentMonCsvParseKind.Sample
                        && index % bucket == 0
                        && retained < 1500)
                    {
                        retained++;
                    }
                }
            });
            TestSupport.Equal(1500, retained, $"retained points at {fps} FPS");
            TestSupport.Equal(frames, parser.SampleCreationCount, $"parsed CSV rows at {fps} FPS");
            TestSupport.True(measurement.Elapsed < TimeSpan.FromSeconds(60), $"synthetic CSV stream failed at {fps} FPS");
        }
    }

    private static Task ThreeHourTimelineStreamsAndBoundsUiAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset started = DateTimeOffset.UtcNow.AddHours(-3);
        string csv = Path.Combine(directory, "three-hour.csv");
        string timeline = Path.Combine(directory, "three-hour.hardware-timeline.csv");
        await File.WriteAllTextAsync(csv, GameCsvFormatting.Header + Environment.NewLine);
        await using (StreamWriter writer = new(timeline))
        {
            await writer.WriteLineAsync(GameHardwareTimelineCsv.Header);
            for (int second = 0; second <= 10_800; second++)
            {
                await writer.WriteLineAsync(GameHardwareTimelineCsv.Format(new GameHardwareTimelineSample
                {
                    CaptureSessionId = id,
                    CaptureGeneration = 1,
                    Timestamp = started.AddSeconds(second),
                    ElapsedSeconds = second,
                    DeviceType = GameTimelineDeviceType.Cpu,
                    DeviceId = "cpu",
                    DeviceName = "Synthetic CPU",
                    CpuAverageCoreClockMHz = 3000d + Math.Sin(second / 60d) * 500d
                }));
            }
        }

        GameSessionRecordInfo record = new()
        {
            GameName = "three-hour",
            StartedAt = started,
            Duration = TimeSpan.FromHours(3),
            IsComplete = true,
            CsvPath = csv,
            HardwareTimelineCsvPath = timeline
        };
        GameSessionReport? report = null;
        Measurement measurement = TestSupport.Measure("timeline-3h-1s-report-read", () =>
        {
            report = new GameSessionReportService().LoadAsync(record).GetAwaiter().GetResult();
        });
        SessionChartModel chart = report!.Charts.Single(item => item.Key == "cpu-frequency");
        int retained = chart.Series.Sum(series => series.Points.Count);
        Console.WriteLine($"MEASURE timeline-3h-1s: inputRows=10801; retainedChartPoints={retained}");
        TestSupport.True(chart.Series.All(series => series.Points.Count <= 1500), "timeline UI series exceeded point cap");
        TestSupport.True(measurement.Elapsed < TimeSpan.FromSeconds(30), "three-hour timeline report exceeded synthetic threshold");
    });

    private static Task HistoricalMicroFrameIsFilteredAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<GameFrameSample> samples = [];
        for (int index = 0; index < 25; index++)
        {
            double fps = index == 12 ? 1_000_000d : 60d;
            samples.Add(TestSupport.Frame(Guid.Empty, index / 60d, fps, started.AddSeconds(index / 60d)));
        }
        GameSessionReport report = await WriteAndLoadAsync(directory, started, 1d, samples);
        TestSupport.Equal(25L, report.RawFrameRowCount, "raw row count");
        TestSupport.Equal(25L, report.ParsedFrameCount, "parsed row count");
        TestSupport.Equal(24L, report.AcceptedFrameCount, "accepted row count");
        TestSupport.Equal(1L, report.FilteredFrameCount, "filtered row count");
        TestSupport.True(report.RawMaximumFps > 100_000d, "raw diagnostic maximum missing");
        TestSupport.True(report.SustainedMaximumFps is > 59d and < 61d, "robust maximum polluted");
        TestSupport.Equal(1L, report.FrameQualityDiagnostics.FrameTimeOutlierSampleCount, "historical outlier count");
    });

    private static Task DuplicateCaptureElapsedIsFilteredAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<GameFrameSample> samples = [];
        for (int index = 0; index < 16; index++)
        {
            double elapsed = index == 9 ? 8d / 60d : index / 60d;
            samples.Add(TestSupport.Frame(Guid.Empty, elapsed, 60d, started.AddSeconds(index / 60d)));
        }
        GameSessionReport report = await WriteAndLoadAsync(directory, started, 1d, samples);
        TestSupport.Equal(15L, report.AcceptedFrameCount, "duplicate timestamp accepted");
        TestSupport.Equal(1L, report.FrameQualityDiagnostics.DuplicateCaptureElapsedSampleCount, "duplicate timestamp diagnostic");
    });

    private static Task AuxiliarySpikeIsSanitizedAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<GameFrameSample> samples = [];
        for (int index = 0; index < 20; index++)
        {
            samples.Add(new GameFrameSample
            {
                Timestamp = started.AddSeconds(index / 60d),
                HasExplicitTimestamp = true,
                CaptureElapsedSeconds = index / 60d,
                ProcessId = 42,
                ProcessName = "synthetic-game",
                FrameTimeMs = 1000d / 60d,
                Fps = 60d,
                DisplayLatencyMs = index == 12 ? 5000d : 20d,
                GpuTimeMs = 4d
            });
        }
        GameSessionReport report = await WriteAndLoadAsync(directory, started, 1d, samples);
        TestSupport.Nearly(20d, report.AverageDisplayLatencyMs, "display latency outlier polluted report");
        TestSupport.Equal(1L, report.FrameQualityDiagnostics.DisplayLatencySanitizedCount, "display cleaning count");
        TestSupport.Equal(20L, report.AcceptedFrameCount, "auxiliary spike dropped frame");
    });

    private static Task PlainAndGZipValidationAgreeAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<string> lines = [GameCsvFormatting.Header];
        for (int index = 0; index < 30; index++)
        {
            double fps = index == 15 ? 10_000d : 120d;
            lines.Add(GameCsvFormatting.FormatSample(TestSupport.Frame(
                Guid.NewGuid(),
                index / 120d,
                fps,
                started.AddSeconds(index / 120d))));
        }
        string plain = Path.Combine(directory, "same.csv");
        string gzip = Path.Combine(directory, "same.csv.gz");
        await File.WriteAllLinesAsync(plain, lines);
        await using (FileStream file = File.Create(gzip))
        await using (GZipStream compressed = new(file, CompressionLevel.Fastest))
        await using (StreamWriter writer = new(compressed))
        {
            foreach (string line in lines) await writer.WriteLineAsync(line);
        }
        GameSessionReport plainReport = await new GameSessionReportService().LoadAsync(Record(plain, string.Empty, started, 1d));
        GameSessionReport gzipReport = await new GameSessionReportService().LoadAsync(Record(gzip, string.Empty, started, 1d));
        TestSupport.Equal(plainReport.AcceptedFrameCount, gzipReport.AcceptedFrameCount, "gzip accepted count");
        TestSupport.Nearly(plainReport.AverageFps!.Value, gzipReport.AverageFps, "gzip average FPS");
        TestSupport.Nearly(plainReport.SustainedMaximumFps!.Value, gzipReport.SustainedMaximumFps, "gzip robust maximum");
    });

    private static Task SustainedThousandFpsIsNotUnderestimatedAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        GameFrameSample[] samples = Enumerable.Range(0, 40)
            .Select(index => TestSupport.Frame(Guid.Empty, index / 1000d, 1000d, started.AddMilliseconds(index)))
            .ToArray();
        GameSessionReport report = await WriteAndLoadAsync(directory, started, 1d, samples);
        TestSupport.True(report.SustainedMaximumFps is > 995d and < 1005d, "stable 1000 FPS underestimated");
        TestSupport.Equal(40L, report.AcceptedFrameCount, "stable 1000 FPS filtered");
    });

    private static Task EndToEndValidationAndReportAgreeAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid sessionId = Guid.NewGuid();
        GameSessionStartInfo info = TestSupport.StartInfo(sessionId);
        await using CsvGameSessionRecorder recorder = new(directory, frameStorageModeProvider: () => GameSessionFrameStorageMode.PlainCsv);
        await recorder.StartAsync(info);
        PresentMonCsvParser parser = new(sessionId, 42, "synthetic-game");
        GameFrameValidationPipeline validation = new();
        PresentMonCsvParseResult header = parser.ParseLine(
            "Application,ProcessID,SwapChainAddress,Timestamp,CaptureElapsedSeconds,FrameTime,CPUBusy,GPUTime,DisplayLatency,PresentMode,FrameType");
        TestSupport.Equal(PresentMonCsvParseKind.HeaderAccepted, header.Kind, "end-to-end header");
        validation.AcceptHeader();
        int recorded = 0;
        List<GameFrameSample> liveAccepted = [];
        for (int index = 0; index < 30; index++)
        {
            double frameTime = index == 24 ? 0.001d : 1000d / 60d;
            DateTimeOffset timestamp = info.CaptureStartedAt.AddSeconds(index / 60d);
            string row = $"synthetic-game,42,0xMAIN,{timestamp:O},{(index / 60d).ToString("R", CultureInfo.InvariantCulture)},{frameTime.ToString("R", CultureInfo.InvariantCulture)},3,4,20,Independent Flip,Application";
            GameFrameSample sample = TestSupport.NotNull(parser.ParseLine(row).Sample, "end-to-end sample");
            GameFrameValidationResult validated = validation.Process(sample, timestamp);
            if (validated.IsAccepted)
            {
                TestSupport.True(recorder.TryRecord(validated.Sample!, sessionId, info.Generation), "end-to-end recorder queue");
                liveAccepted.Add(validated.Sample!);
                recorded++;
            }
        }
        GameFrameQualityDiagnostics diagnostics = validation.GetDiagnostics(info.CaptureStartedAt.AddSeconds(1));
        recorder.SetFrameQualityDiagnostics(sessionId, info.Generation, diagnostics);
        GameSessionRecordInfo record = (await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))!;
        GameSessionReport report = await new GameSessionReportService().LoadAsync(record);
        GamePerformanceSnapshot live = GameFrameStatisticsCalculator.Calculate(liveAccepted, TimeSpan.FromHours(1));
        TestSupport.Equal((long)recorded, report.AcceptedFrameCount, "realtime and report accepted count");
        TestSupport.Nearly(live.AverageFps!.Value, report.AverageFps, "realtime and report average FPS", 0.002d);
        TestSupport.Nearly(live.AverageGpuTimeMs!.Value, report.AverageGpuTimeMs, "realtime and report GPU Time");
        TestSupport.Nearly(live.AverageDisplayLatencyMs!.Value, report.AverageDisplayLatencyMs, "realtime and report display latency");
        TestSupport.True(report.RawMaximumFps > 100_000d, "live raw maximum was not retained in summary");
        TestSupport.True(report.SustainedMaximumFps is > 59d and < 61d, "end-to-end robust maximum");
        TestSupport.True(report.FrameQualityDiagnostics.FrameTimeOutlierSampleCount >= 1L, "end-to-end outlier diagnostic");
    });

    private static Task RegressedExplicitTimestampIsFilteredAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        List<GameFrameSample> samples = [];
        for (int index = 0; index < 16; index++)
        {
            DateTimeOffset timestamp = index == 9
                ? started.AddSeconds(7d / 60d)
                : started.AddSeconds(index / 60d);
            samples.Add(TestSupport.Frame(Guid.Empty, index / 60d, 60d, timestamp));
        }
        GameSessionReport report = await WriteAndLoadAsync(directory, started, 1d, samples);
        TestSupport.Equal(15L, report.AcceptedFrameCount, "regressed explicit timestamp accepted");
        TestSupport.Equal(1L, report.FrameQualityDiagnostics.RegressedExplicitTimestampSampleCount, "regressed explicit diagnostic");
    });

    private static Task WithReportAsync(
        double durationSeconds,
        IReadOnlyList<GameFrameSample> samples,
        Func<GameSessionReport, Task> assertion) => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionReport report = await WriteAndLoadAsync(directory, DateTimeOffset.UtcNow, durationSeconds, samples);
        await assertion(report);
    });

    private static async Task<GameSessionReport> WriteAndLoadAsync(
        string directory,
        DateTimeOffset started,
        double durationSeconds,
        IReadOnlyList<GameFrameSample> sourceSamples)
    {
        Guid id = Guid.NewGuid();
        string csv = Path.Combine(directory, "session.csv");
        string summaryPath = Path.Combine(directory, "session.summary.json");
        List<string> lines = [GameCsvFormatting.Header];
        foreach (GameFrameSample source in sourceSamples)
        {
            GameFrameSample sample = new()
            {
                CaptureSessionId = id,
                Timestamp = source.Timestamp,
                HasExplicitTimestamp = source.HasExplicitTimestamp,
                CaptureElapsedSeconds = source.CaptureElapsedSeconds,
                ProcessId = source.ProcessId,
                ProcessName = source.ProcessName,
                Fps = source.Fps,
                FrameTimeMs = source.FrameTimeMs,
                CpuBusyMs = source.CpuBusyMs,
                CpuWaitMs = source.CpuWaitMs,
                GpuLatencyMs = source.GpuLatencyMs,
                GpuTimeMs = source.GpuTimeMs,
                GpuBusyMs = source.GpuBusyMs,
                GpuWaitMs = source.GpuWaitMs,
                RenderLatencyMs = source.RenderLatencyMs,
                DisplayLatencyMs = source.DisplayLatencyMs,
                DisplayedTimeMs = source.DisplayedTimeMs,
                ClickToPhotonLatencyMs = source.ClickToPhotonLatencyMs,
                SwapChainAddress = source.SwapChainAddress,
                PresentMode = source.PresentMode,
                FrameType = source.FrameType
            };
            lines.Add(GameCsvFormatting.FormatSample(sample));
        }
        await File.WriteAllLinesAsync(csv, lines);
        await WriteSummaryAsync(summaryPath, id, started, durationSeconds, Path.GetFileName(csv), sourceSamples.Count);
        return await new GameSessionReportService().LoadAsync(Record(csv, summaryPath, started, durationSeconds));
    }

    private static async Task WriteSummaryAsync(
        string summaryPath,
        Guid id,
        DateTimeOffset started,
        double duration,
        string csvFileName,
        long writtenSamples)
    {
        GameSessionSummary summary = new()
        {
            SessionSchemaVersion = 2,
            CaptureSessionId = id,
            CaptureGeneration = 1,
            ProcessId = 42,
            ProcessName = "synthetic-game",
            CaptureStartedAt = started,
            CaptureEndedAt = started.AddSeconds(duration),
            Duration = TimeSpan.FromSeconds(duration),
            WrittenSampleCount = writtenSamples,
            CsvFileName = csvFileName,
            CompletedNormally = true
        };
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter() } };
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, options));
    }

    private static GameSessionRecordInfo Record(
        string csv,
        string summary,
        DateTimeOffset started,
        double duration) => new()
    {
        GameName = "synthetic-game",
        StartedAt = started,
        Duration = TimeSpan.FromSeconds(duration),
        IsComplete = true,
        CsvPath = csv,
        SummaryPath = summary
    };

    private static SessionChartSeries Fps(GameSessionReport report) =>
        report.Charts.Single(chart => chart.Key == "fps").Series.Single();

    private static GameFrameSample WallClockFrame(DateTimeOffset timestamp) => new()
    {
        Timestamp = timestamp,
        HasExplicitTimestamp = true,
        ProcessId = 42,
        ProcessName = "synthetic-game",
        Fps = 60d,
        FrameTimeMs = 1000d / 60d
    };
}
