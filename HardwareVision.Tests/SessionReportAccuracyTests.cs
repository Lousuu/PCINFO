using System.Globalization;
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
        ("Report accuracy 09 three-hour one-second timeline streams and bounds UI", TestSupport.Run(ThreeHourTimelineStreamsAndBoundsUiAsync))
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
                FrameTimeMs = source.FrameTimeMs
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
