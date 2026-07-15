using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using HardwareVision.Views;

namespace HardwareVision.Tests;

internal static class SessionReportTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session report 01 performance-limit CSV is created", Run(LimitCsvIsCreatedAsync)),
        ("Session report 02 performance-limit file name follows session", LimitCsvFileName),
        ("Session report 03 performance-limit header is stable", LimitCsvHeader),
        ("Session report 04 multiple limit events round trip", Run(MultipleLimitEventsRoundTripAsync)),
        ("Session report 05 limit multi-value escaping round trips", LimitMultiValueEscaping),
        ("Session report 06 zero limit events writes header only", Run(ZeroLimitEventsAsync)),
        ("Session report 07 recording disabled creates no auxiliary files", RecordingDisabledCreatesNoFiles),
        ("Session report 08 foreign session limit event is skipped", Run(ForeignSessionLimitEventIsSkippedAsync)),
        ("Session report 09 foreign generation limit event is skipped", Run(ForeignGenerationLimitEventIsSkippedAsync)),
        ("Session report 10 old record without limit CSV loads", Run(OldRecordWithoutLimitCsvLoadsAsync)),
        ("Session report 11 timeline is created", Run(TimelineIsCreatedAsync)),
        ("Session report 12 inactive timeline creates no file", InactiveTimelineCreatesNoFile),
        ("Session report 13 timeline uses one-second throttle", Run(TimelineUsesOneSecondThrottleAsync)),
        ("Session report 14 frame recording does not multiply timeline", Run(FrameRateDoesNotMultiplyTimelineAsync)),
        ("Session report 15 CPU average core frequency is arithmetic mean", CpuAverageFrequency),
        ("Session report 16 CPU effective clock remains separate", CpuEffectiveClockSeparate),
        ("Session report 17 bus clock is excluded", CpuBusClockExcluded),
        ("Session report 18 CPU maximum core clock is correct", CpuMaximumClock),
        ("Session report 19 GPU core and memory clocks are distinct", GpuClocksAreDistinct),
        ("Session report 20 multiple GPUs stay separate", MultipleGpusStaySeparate),
        ("Session report 21 missing frequency serializes as empty", MissingFrequencyIsEmpty),
        ("Session report 22 CPU limit state follows tracker snapshot", CpuLimitState),
        ("Session report 23 GPU limit state follows tracker snapshot", GpuLimitState),
        ("Session report 24 timeline preserves session id", TimelinePreservesSessionId),
        ("Session report 25 timeline preserves generation", TimelinePreservesGeneration),
        ("Session report 26 old generation cannot enter new timeline", Run(OldGenerationRejectedAsync)),
        ("Session report 27 completed timeline releases file", Run(CompletedTimelineReleasesFileAsync)),
        ("Session report 28 full timeline queue does not block", Run(FullTimelineQueueDoesNotBlockAsync)),
        ("Session report 29 timeline drops enter summary", Run(TimelineDropsEnterSummaryAsync)),
        ("Session report 30 partial timeline is recovered", Run(PartialTimelineIsRecoveredAsync)),
        ("Session report 31 four session files load together", Run(FourFilesLoadTogetherAsync)),
        ("Session report 32 missing auxiliary file partially loads", Run(MissingAuxiliaryFilePartiallyLoadsAsync)),
        ("Session report 33 legacy summary remains readable", Run(LegacySummaryRemainsReadableAsync)),
        ("Session report 34 legacy summary events load without limit CSV", Run(LegacySummaryEventsLoadWithoutLimitCsvAsync)),
        ("Session report 35 chart series are generated", Run(ChartSeriesAreGeneratedAsync)),
        ("Session report 36 event interval aligns to elapsed time", Run(EventIntervalAlignmentAsync)),
        ("Session report 37 frequency curve works without events", Run(FrequencyWithoutEventsAsync)),
        ("Session report 38 event timeline works without frequency", Run(EventsWithoutFrequencyAsync)),
        ("Session report 39 corrupt file permits partial report", Run(CorruptFilePermitsPartialReportAsync)),
        ("Session report 40 missing hardware metadata renders placeholders", Run(MissingMetadataRendersPlaceholdersAsync)),
        ("Session report 41 CPU frequency curve points are correct", Run(CpuFrequencyCurvePointsAsync)),
        ("Session report 42 GPU frequency curve points are correct", Run(GpuFrequencyCurvePointsAsync)),
        ("Session report 43 thermal event creates overlay", Run(() => ExplicitEventCreatesOverlayAsync("Thermal Throttling"))),
        ("Session report 44 power event creates overlay", Run(() => ExplicitEventCreatesOverlayAsync("Power Limit"))),
        ("Session report 45 current EDP event creates overlay", Run(() => ExplicitEventCreatesOverlayAsync("Current / EDP"))),
        ("Session report 46 utilization idle is not anomalous", () => AssertReasonExcluded("Utilization / Idle")),
        ("Session report 47 sync boost is not anomalous", () => AssertReasonExcluded("Sync Boost")),
        ("Session report 48 application clock setting is not anomalous", () => AssertReasonExcluded("Application Clock Setting")),
        ("Session report 49 display clock setting is not anomalous", () => AssertReasonExcluded("Display Clock Setting")),
        ("Session report 50 multi-GPU charts are selectable separately", Run(MultiGpuChartsAreSeparateAsync)),
        ("Session report 51 limited average frequency is correct", LimitedAverageFrequency),
        ("Session report 52 normal average frequency is correct", NormalAverageFrequency),
        ("Session report 53 limited time ratio is correct", LimitedTimeRatio),
        ("Session report 54 most common reason is correct", MostCommonReason),
        ("Session report 55 downsampling preserves maximum", DownsamplingPreservesMaximum),
        ("Session report 56 downsampling preserves minimum", DownsamplingPreservesMinimum),
        ("Session report 57 downsampling preserves event start", DownsamplingPreservesEventStart),
        ("Session report 58 downsampling preserves event end", DownsamplingPreservesEventEnd),
        ("Session report 59 short event survives downsampling", ShortEventSurvivesDownsampling),
        ("Session report 60 long chart point count is bounded", LongChartPointCountIsBounded),
        ("Session report 61 large CSV is bounded before UI", Run(LargeCsvIsBoundedBeforeUiAsync)),
        ("Session report 62 closing detail releases report data", Run(ClosingDetailReleasesDataAsync)),
        ("Session report 63 game and detail views load runtime resources", Run(GameAndDetailViewsLoadRuntimeResourcesAsync)),
        ("Session report 64 report cache has fixed capacity", Run(ReportCacheHasFixedCapacityAsync))
    ];

    private static Action Run(Func<Task> test) => () => test().GetAwaiter().GetResult();

    private static async Task LimitCsvIsCreatedAsync()
    {
        using TempScope temp = new();
        string path = Path.Combine(temp.Path, "game.performance-limits.csv");
        await GamePerformanceLimitCsv.WriteAsync(path, Snapshot(Event()), Start, CancellationToken.None);
        True(File.Exists(path), "limit CSV should exist");
    }

    private static void LimitCsvFileName()
    {
        string path = GamePerformanceLimitCsv.GetPath(Path.Combine(Path.GetTempPath(), "session.csv"));
        Equal("session.performance-limits.csv", Path.GetFileName(path), "limit file name");
    }

    private static void LimitCsvHeader() => Equal(
        "SessionSchemaVersion,CaptureSessionId,CaptureGeneration,EventId,ProcessorType,DeviceId,StartedAt,EndedAt,ElapsedStartSeconds,ElapsedEndSeconds,DurationSeconds,ReasonCount,Reasons,RawReasonNames,Scopes,TriggerCount,WasMerged,SupportStatus,IsActiveFinalState,Source,RawIdentifiers,WasTruncatedSource,Notes",
        GamePerformanceLimitCsv.Header,
        "limit header");

    private static async Task MultipleLimitEventsRoundTripAsync()
    {
        using TempScope temp = new();
        string path = Path.Combine(temp.Path, "limits.csv");
        GamePerformanceLimitEvent first = Event(1, PerformanceLimitProcessorType.Cpu, "Thermal Throttling");
        GamePerformanceLimitEvent second = Event(2, PerformanceLimitProcessorType.Gpu, "Software Power Cap");
        await GamePerformanceLimitCsv.WriteAsync(path, Snapshot(first, second), Start, CancellationToken.None);
        PerformanceLimitCsvReadResult read = await GamePerformanceLimitCsv.ReadAsync(path, SessionId, Generation, CancellationToken.None);
        Equal(2, read.Events.Count, "event count");
        Equal("确认 3 次", read.Events[0].TriggerCountText, "current trigger-count text");
        Equal("发生过合并", read.Events[0].MergeText, "current merge text");
        GamePerformanceLimitEvent jsonRoundTrip = NotNull(
            JsonSerializer.Deserialize<GamePerformanceLimitEvent>(JsonSerializer.Serialize(first)),
            "current event JSON round trip");
        True(jsonRoundTrip.HasTriggerCount, "current JSON preserves trigger-count presence");
        True(jsonRoundTrip.HasWasMerged, "current JSON preserves merge-state presence");
    }

    private static void LimitMultiValueEscaping()
    {
        string joined = GamePerformanceLimitCsv.JoinValues(["Power;Limit", "Raw\\Identifier"]);
        IReadOnlyList<string> split = GamePerformanceLimitCsv.SplitValues(joined);
        Equal("Power;Limit", split[0], "semicolon escape");
        Equal("Raw\\Identifier", split[1], "backslash escape");
    }

    private static async Task ZeroLimitEventsAsync()
    {
        using TempScope temp = new();
        string path = Path.Combine(temp.Path, "limits.csv");
        await GamePerformanceLimitCsv.WriteAsync(path, Snapshot(), Start, CancellationToken.None);
        string[] lines = await File.ReadAllLinesAsync(path);
        Equal(1, lines.Length, "header-only event file");
        Equal(GamePerformanceLimitCsv.Header, lines[0], "header");
    }

    private static void RecordingDisabledCreatesNoFiles()
    {
        using TempScope temp = new();
        using CsvGameSessionRecorder recorder = new(temp.Path);
        Equal(0, Directory.GetFiles(temp.Path, "*.performance-limits.csv", SearchOption.AllDirectories).Length, "limit files");
        Equal(0, Directory.GetFiles(temp.Path, "*.hardware-timeline.csv", SearchOption.AllDirectories).Length, "timeline files");
    }

    private static async Task ForeignSessionLimitEventIsSkippedAsync()
    {
        using TempScope temp = new();
        GamePerformanceLimitEvent foreign = CloneEvent(Event(), sessionId: Guid.NewGuid());
        string path = Path.Combine(temp.Path, "limits.csv");
        await GamePerformanceLimitCsv.WriteAsync(path, Snapshot(foreign), Start, CancellationToken.None);
        Equal(1, (await File.ReadAllLinesAsync(path)).Length, "foreign event must be skipped");
    }

    private static async Task ForeignGenerationLimitEventIsSkippedAsync()
    {
        using TempScope temp = new();
        GamePerformanceLimitEvent foreign = CloneEvent(Event(), generation: Generation - 1);
        string path = Path.Combine(temp.Path, "limits.csv");
        await GamePerformanceLimitCsv.WriteAsync(path, Snapshot(foreign), Start, CancellationToken.None);
        Equal(1, (await File.ReadAllLinesAsync(path)).Length, "foreign generation must be skipped");
    }

    private static async Task OldRecordWithoutLimitCsvLoadsAsync()
    {
        using ReportFixture fixture = new();
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        Equal(SessionAuxiliaryFileStatus.NotRecorded, report.PerformanceLimitFileStatus, "old limit status");
    }

    private static async Task TimelineIsCreatedAsync()
    {
        using TempScope temp = new();
        string frame = Path.Combine(temp.Path, "game.csv");
        using GameHardwareTimelineRecorder recorder = TimelineRecorder();
        recorder.StartSession(StartInfo(), frame);
        recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, 0, Start);
        GameHardwareTimelineResult result = NotNull(await recorder.CompleteSessionAsync(SessionId, Generation), "timeline result");
        True(result.CompletedSuccessfully && File.Exists(result.FilePath), "timeline should be finalized");
    }

    private static void InactiveTimelineCreatesNoFile()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder recorder = TimelineRecorder();
        False(recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, 0, Start), "inactive recorder");
        Equal(0, Directory.GetFiles(temp.Path).Length, "inactive timeline files");
    }

    private static async Task TimelineUsesOneSecondThrottleAsync()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder recorder = TimelineRecorder();
        recorder.StartSession(StartInfo(), Path.Combine(temp.Path, "game.csv"));
        True(recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, 0, Start), "first sample");
        False(recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3100)], GamePerformanceLimitSnapshot.Empty, 500, Start.AddMilliseconds(500)), "half-second sample");
        True(recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3200)], GamePerformanceLimitSnapshot.Empty, 1000, Start.AddSeconds(1)), "one-second sample");
        GameHardwareTimelineResult result = NotNull(await recorder.CompleteSessionAsync(SessionId, Generation), "result");
        Equal(2L, result.WrittenSampleCount, "throttled rows");
    }

    private static async Task FrameRateDoesNotMultiplyTimelineAsync()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder recorder = TimelineRecorder();
        recorder.StartSession(StartInfo(), Path.Combine(temp.Path, "game.csv"));
        recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, 0, Start);
        for (int index = 0; index < 100; index++)
        {
            recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, index + 1, Start.AddMilliseconds(index + 1));
        }
        GameHardwareTimelineResult result = NotNull(await recorder.CompleteSessionAsync(SessionId, Generation), "result");
        Equal(1L, result.WrittenSampleCount, "hardware timeline is independent of frame rate");
    }

    private static void CpuAverageFrequency()
    {
        GameHardwareTimelineSample cpu = CpuSample([CpuClock("Core #1", 2000), CpuClock("Core #2", 4000)]);
        Nearly(3000, cpu.CpuAverageCoreClockMHz, "CPU average");
    }

    private static void CpuEffectiveClockSeparate()
    {
        GameHardwareTimelineSample cpu = CpuSample([CpuClock("Core #1", 4000), CpuClock("Core #1 Effective Clock", 1000)]);
        Nearly(4000, cpu.CpuAverageCoreClockMHz, "ordinary clock");
        Nearly(1000, cpu.CpuEffectiveClockMHz, "effective clock");
    }

    private static void CpuBusClockExcluded()
    {
        IReadOnlyList<GameHardwareTimelineSample> samples = GameHardwareTimelineSampler.CreateSamples(
            StartInfo(),
            [CpuClock("Bus Clock", 100), CpuClock("BCLK", 100)],
            GamePerformanceLimitSnapshot.Empty,
            Start,
            0);
        False(samples.Any(item => item.DeviceType == GameTimelineDeviceType.Cpu), "bus-only input must not create a CPU frequency row");
    }

    private static void CpuMaximumClock()
    {
        GameHardwareTimelineSample cpu = CpuSample([CpuClock("Core #1", 2200), CpuClock("Core #2", 4700), CpuClock("Core #3", 3100)]);
        Nearly(4700, cpu.CpuMaximumCoreClockMHz, "maximum core clock");
    }

    private static void GpuClocksAreDistinct()
    {
        GameHardwareTimelineSample gpu = GpuSamples([
            GpuClock("GPU Core Clock", 2100, "/gpu/0/core"),
            GpuClock("GPU Memory Clock", 8000, "/gpu/0/memory")])[0];
        Nearly(2100, gpu.GpuCoreClockMHz, "GPU core");
        Nearly(8000, gpu.GpuMemoryClockMHz, "GPU memory");
    }

    private static void MultipleGpusStaySeparate()
    {
        IReadOnlyList<GameHardwareTimelineSample> gpus = GpuSamples([
            GpuClock("GPU Core Clock", 2100, "/gpu/0/core", "GPU A"),
            GpuClock("GPU Core Clock", 1500, "/gpu/1/core", "GPU B")]);
        Equal(2, gpus.Count, "GPU row count");
        False(gpus[0].DeviceId == gpus[1].DeviceId, "GPU identifiers");
    }

    private static void MissingFrequencyIsEmpty()
    {
        GameHardwareTimelineSample sample = new()
        {
            CaptureSessionId = SessionId,
            CaptureGeneration = Generation,
            Timestamp = Start,
            DeviceType = GameTimelineDeviceType.Gpu,
            DeviceId = "/gpu/0",
            DeviceName = "GPU"
        };
        string[] fields = PresentMonCsvParser.ParseColumns(GameHardwareTimelineCsv.Format(sample)).ToArray();
        Equal(string.Empty, fields[17], "missing core clock field");
        Equal(string.Empty, fields[18], "missing memory clock field");
    }

    private static void CpuLimitState()
    {
        GamePerformanceLimitEvent limit = Event(1, PerformanceLimitProcessorType.Cpu, "Thermal Throttling", active: true);
        GameHardwareTimelineSample sample = GameHardwareTimelineSampler.CreateSamples(StartInfo(), [CpuClock("Core #1", 3000)], Snapshot(limit), Start, 0)
            .Single(item => item.DeviceType == GameTimelineDeviceType.Cpu);
        Equal(true, sample.CpuLimitActive, "CPU active limit");
        Equal("Thermal Throttling", sample.CpuLimitReasons[0], "CPU reason");
    }

    private static void GpuLimitState()
    {
        GamePerformanceLimitEvent limit = Event(1, PerformanceLimitProcessorType.Gpu, "Software Power Cap", active: true);
        GameHardwareTimelineSample sample = GameHardwareTimelineSampler.CreateSamples(StartInfo(), [GpuClock("GPU Core Clock", 2000, "/gpu/0/core")], Snapshot(limit), Start, 0)
            .Single(item => item.DeviceType == GameTimelineDeviceType.Gpu);
        Equal(true, sample.GpuLimitActive, "GPU active limit");
    }

    private static void TimelinePreservesSessionId()
    {
        GameHardwareTimelineSample sample = CpuSample([CpuClock("Core #1", 3000)]);
        Equal(SessionId, sample.CaptureSessionId, "timeline SessionId");
    }

    private static void TimelinePreservesGeneration()
    {
        GameHardwareTimelineSample sample = CpuSample([CpuClock("Core #1", 3000)]);
        Equal(Generation, sample.CaptureGeneration, "timeline generation");
    }

    private static async Task OldGenerationRejectedAsync()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder recorder = TimelineRecorder();
        recorder.StartSession(StartInfo(), Path.Combine(temp.Path, "game.csv"));
        False(recorder.RecordReadings(SessionId, Generation - 1, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, 0, Start), "foreign generation");
        GameHardwareTimelineResult result = NotNull(await recorder.CompleteSessionAsync(SessionId, Generation), "result");
        Equal(0L, result.WrittenSampleCount, "foreign samples written");
    }

    private static async Task CompletedTimelineReleasesFileAsync()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder recorder = TimelineRecorder();
        recorder.StartSession(StartInfo(), Path.Combine(temp.Path, "game.csv"));
        recorder.RecordReadings(SessionId, Generation, [CpuClock("Core #1", 3000)], GamePerformanceLimitSnapshot.Empty, 0, Start);
        GameHardwareTimelineResult result = NotNull(await recorder.CompleteSessionAsync(SessionId, Generation), "result");
        using FileStream exclusive = new(result.FilePath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        True(exclusive.Length > 0, "closed timeline file");
    }

    private static async Task FullTimelineQueueDoesNotBlockAsync()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder recorder = TimelineRecorder(capacity: 1);
        recorder.StartSession(StartInfo(), Path.Combine(temp.Path, "game.csv"));
        IReadOnlyList<SensorReading> readings = ManyGpuReadings(2000);
        Stopwatch stopwatch = Stopwatch.StartNew();
        recorder.RecordReadings(SessionId, Generation, readings, GamePerformanceLimitSnapshot.Empty, 0, Start);
        stopwatch.Stop();
        True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "TryWrite path must not block polling");
        _ = await recorder.CompleteSessionAsync(SessionId, Generation);
    }

    private static async Task TimelineDropsEnterSummaryAsync()
    {
        using TempScope temp = new();
        using GameHardwareTimelineRecorder timeline = TimelineRecorder(capacity: 1);
        await using CsvGameSessionRecorder recorder = new(temp.Path, 32, null, null, timeline);
        GameSessionStartInfo start = StartInfo();
        await recorder.StartAsync(start);
        recorder.TryRecord(Frame(0), SessionId, Generation);
        timeline.RecordReadings(SessionId, Generation, ManyGpuReadings(2000), GamePerformanceLimitSnapshot.Empty, 0, Start);
        GameSessionRecordInfo record = NotNull(await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true), "record");
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter() } };
        GameSessionSummary summary = JsonSerializer.Deserialize<GameSessionSummary>(await File.ReadAllTextAsync(record.SummaryPath!), options)!;
        True(summary.TimelineDroppedSampleCount.GetValueOrDefault() > 0, "timeline dropped count");
    }

    private static async Task PartialTimelineIsRecoveredAsync()
    {
        using TempScope temp = new();
        string partial = Path.Combine(temp.Path, "game.hardware-timeline.partial.csv");
        GameHardwareTimelineSample sample = CpuSample([CpuClock("Core #1", 3000)]);
        await File.WriteAllLinesAsync(partial, [GameHardwareTimelineCsv.Header, GameHardwareTimelineCsv.Format(sample)]);
        await GameHardwareTimelineRecorder.RecoverIncompleteAsync(temp.Path);
        False(File.Exists(partial), "partial should move");
        Equal(1, Directory.GetFiles(temp.Path, "*.hardware-timeline.incomplete.csv").Length, "incomplete timeline");
    }

    private static async Task FourFilesLoadTogetherAsync()
    {
        using ReportFixture fixture = new();
        await fixture.WriteLimitsAsync(Event());
        fixture.WriteTimeline(CpuTimeline(3000, 3200));
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        True(report.Summary is not null && report.ParsedFrameCount > 0, "summary and frames");
        Equal(SessionAuxiliaryFileStatus.Recorded, report.PerformanceLimitFileStatus, "limits");
        Equal(SessionAuxiliaryFileStatus.Recorded, report.HardwareTimelineFileStatus, "timeline");
    }

    private static async Task MissingAuxiliaryFilePartiallyLoadsAsync()
    {
        using ReportFixture fixture = new();
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        True(report.ParsedFrameCount > 0, "frames remain available");
        Equal(SessionAuxiliaryFileStatus.NotRecorded, report.HardwareTimelineFileStatus, "missing timeline");
    }

    private static async Task LegacySummaryRemainsReadableAsync()
    {
        using ReportFixture fixture = new();
        await File.WriteAllTextAsync(fixture.SummaryPath, "{\"ProcessName\":\"legacy.exe\",\"Duration\":\"00:00:02\",\"CsvFileName\":\"game.csv\"}");
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        Equal("legacy.exe", report.Summary?.ProcessName, "legacy summary process");
    }

    private static async Task LegacySummaryEventsLoadWithoutLimitCsvAsync()
    {
        using ReportFixture fixture = new();
        GameSessionSummary legacySummary = new()
        {
            CaptureSessionId = SessionId,
            CaptureGeneration = Generation,
            ProcessId = 42,
            ProcessName = "game.exe",
            CaptureStartedAt = Start,
            CaptureEndedAt = Start.AddMinutes(2),
            Duration = TimeSpan.FromMinutes(2),
            CompletedNormally = true,
            CsvFileName = Path.GetFileName(fixture.FramePath),
            PerformanceLimitEvents =
            [
                Event(2, startOffsetSeconds: 20),
                Event(1, startOffsetSeconds: 10)
            ]
        };
        System.Text.Json.Nodes.JsonObject legacyJson = NotNull(
            System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(legacySummary)) as System.Text.Json.Nodes.JsonObject,
            "legacy summary JSON");
        System.Text.Json.Nodes.JsonArray legacyEvents = NotNull(
            legacyJson[nameof(GameSessionSummary.PerformanceLimitEvents)] as System.Text.Json.Nodes.JsonArray,
            "legacy event array");
        for (int index = 0; index < legacyEvents.Count; index++)
        {
            System.Text.Json.Nodes.JsonObject item = NotNull(
                legacyEvents[index] as System.Text.Json.Nodes.JsonObject,
                "legacy event JSON");
            item.Remove(nameof(GamePerformanceLimitEvent.TriggerCount));
            item.Remove(nameof(GamePerformanceLimitEvent.WasMerged));
        }
        await File.WriteAllTextAsync(fixture.SummaryPath, legacyJson.ToJsonString());
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        Equal(SessionAuxiliaryFileStatus.Recorded, report.PerformanceLimitFileStatus, "legacy summary limit status");
        True(report.PerformanceLimitEventsLoadedFromLegacySummary, "legacy source marker");
        Equal(2, report.PerformanceLimitEvents.Count, "legacy event count");
        Equal(1L, report.PerformanceLimitEvents[0].EventId, "legacy events are chronological");
        False(report.PerformanceLimitEvents[0].HasTriggerCount, "legacy trigger count is absent");
        False(report.PerformanceLimitEvents[0].HasWasMerged, "legacy merge state is absent");
        Equal("确认次数未记录", report.PerformanceLimitEvents[0].TriggerCountText, "legacy trigger-count text");
        Equal("合并状态未记录", report.PerformanceLimitEvents[0].MergeText, "legacy merge text");
        Equal(2, Chart(report, "cpu-frequency").LimitIntervals.Count, "legacy chart overlays");

        using GameSessionReportViewModel viewModel = new(fixture.Record, new GameSessionReportService(), () => { });
        await viewModel.LoadAsync();
        True(viewModel.PerformanceLimitStatusText.Contains("旧版 summary.json", StringComparison.Ordinal), "legacy source status text");
    }

    private static async Task ChartSeriesAreGeneratedAsync()
    {
        using ReportFixture fixture = new();
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionChartModel fps = Chart(report, "fps");
        True(fps.Series.Count == 1 && fps.Series[0].Points.Count > 0, "FPS series");
    }

    private static async Task EventIntervalAlignmentAsync()
    {
        using ReportFixture fixture = new();
        await fixture.WriteLimitsAsync(Event(startOffsetSeconds: 10, durationSeconds: 2));
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionLimitInterval interval = Chart(report, "cpu-frequency").LimitIntervals.Single();
        Nearly(10, interval.StartSeconds, "event start elapsed");
        Nearly(12, interval.EndSeconds, "event end elapsed");
    }

    private static async Task FrequencyWithoutEventsAsync()
    {
        using ReportFixture fixture = new();
        fixture.WriteTimeline(CpuTimeline(3000, 3200));
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionChartModel chart = Chart(report, "cpu-frequency");
        True(chart.Series.Count > 0, "frequency curve");
        Equal(0, chart.LimitIntervals.Count, "no overlays");
    }

    private static async Task EventsWithoutFrequencyAsync()
    {
        using ReportFixture fixture = new();
        await fixture.WriteLimitsAsync(Event());
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionChartModel chart = Chart(report, "cpu-frequency");
        Equal(0, chart.Series.Count, "no fabricated frequency");
        Equal(1, chart.LimitIntervals.Count, "event timeline remains");
    }

    private static async Task CorruptFilePermitsPartialReportAsync()
    {
        using ReportFixture fixture = new();
        await File.WriteAllTextAsync(fixture.TimelinePath, "broken-header\nbroken-row");
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        True(report.ParsedFrameCount > 0, "frames survive corrupt timeline");
        Equal(SessionAuxiliaryFileStatus.Unavailable, report.HardwareTimelineFileStatus, "corrupt timeline status");
    }

    private static async Task MissingMetadataRendersPlaceholdersAsync()
    {
        using ReportFixture fixture = new();
        GameSessionReportViewModel viewModel = new(fixture.Record, new GameSessionReportService(), () => { });
        await viewModel.LoadAsync();
        GameSessionReportMetric cpu = viewModel.HardwareMetrics.Single(metric => metric.Label == "CPU");
        Equal("--", cpu.Value, "historical metadata placeholder");
        viewModel.Dispose();
    }

    private static async Task CpuFrequencyCurvePointsAsync()
    {
        using ReportFixture fixture = new();
        fixture.WriteTimeline(CpuTimeline(3000, 3400));
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionChartSeries series = Chart(report, "cpu-frequency").Series[0];
        Nearly(3000, series.Points[0].Value, "first CPU frequency");
        Nearly(3400, series.Points[^1].Value, "last CPU frequency");
    }

    private static async Task GpuFrequencyCurvePointsAsync()
    {
        using ReportFixture fixture = new();
        fixture.WriteTimeline(GpuTimeline("GPU A", "/gpu/0", 1500, 2100));
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionChartSeries series = report.Charts.Single(chart => chart.Key.StartsWith("gpu-frequency:", StringComparison.Ordinal)).Series[0];
        Nearly(1500, series.Points[0].Value, "first GPU frequency");
        Nearly(2100, series.Points[^1].Value, "last GPU frequency");
    }

    private static async Task ExplicitEventCreatesOverlayAsync(string reason)
    {
        using ReportFixture fixture = new();
        await fixture.WriteLimitsAsync(Event(reason: reason));
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        Equal(reason, Chart(report, "cpu-frequency").LimitIntervals.Single().Reasons.Single(), "overlay reason");
    }

    private static void AssertReasonExcluded(string reason)
    {
        long timestamp = 0;
        using GamePerformanceLimitTracker tracker = new(null, () => timestamp, 1000);
        tracker.StartSession(StartInfo());
        SensorReading reading = StateReading(reason, SensorCategory.Gpu);
        tracker.RecordReadings(SessionId, Generation, [reading], timestamp, Start);
        timestamp = 1000;
        tracker.RecordReadings(SessionId, Generation, [reading], timestamp, Start.AddSeconds(1));
        Equal(0, tracker.CurrentSnapshot.Events.Count, reason + " should be excluded");
    }

    private static async Task MultiGpuChartsAreSeparateAsync()
    {
        using ReportFixture fixture = new();
        List<GameHardwareTimelineSample> samples = [];
        samples.AddRange(GpuTimeline("GPU A", "/gpu/0", 1500, 1600));
        samples.AddRange(GpuTimeline("GPU B", "/gpu/1", 2100, 2200));
        fixture.WriteTimeline(samples);
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        SessionChartModel[] charts = report.Charts.Where(chart => chart.Key.StartsWith("gpu-frequency:", StringComparison.Ordinal)).ToArray();
        Equal(2, charts.Length, "separate GPU charts");
        False(charts[0].Key == charts[1].Key, "GPU chart keys");
    }

    private static void LimitedAverageFrequency()
    {
        SessionThrottleStatistics stats = Statistics();
        Nearly(2000, stats.LimitedAverageFrequencyMHz, "limited average");
    }

    private static void NormalAverageFrequency()
    {
        SessionThrottleStatistics stats = Statistics();
        Nearly(3143.75, stats.NormalAverageFrequencyMHz, "time-weighted normal average");
    }

    private static void LimitedTimeRatio()
    {
        SessionThrottleStatistics stats = Statistics();
        Nearly(20, stats.LimitedRatioPercent, "limited ratio");
    }

    private static void MostCommonReason()
    {
        List<SessionLimitInterval> intervals =
        [
            Interval(2, 4, "Thermal"),
            Interval(5, 6, "Power"),
            Interval(7, 8, "Thermal")
        ];
        SessionThrottleStatistics stats = SessionThrottleStatisticsCalculator.Calculate(Series(), intervals, 10);
        Equal("Thermal", stats.MostCommonReason, "common reason");
    }

    private static void DownsamplingPreservesMaximum()
    {
        IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(LongPoints(), [], 100);
        True(sampled.Any(point => point.Value == 9999), "maximum");
    }

    private static void DownsamplingPreservesMinimum()
    {
        IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(LongPoints(), [], 100);
        True(sampled.Any(point => point.Value == -9999), "minimum");
    }

    private static void DownsamplingPreservesEventStart()
    {
        IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(LongPoints(), [Interval(123, 129, "Thermal")], 100);
        True(sampled.Any(point => point.ElapsedSeconds == 123), "event start point");
    }

    private static void DownsamplingPreservesEventEnd()
    {
        IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(LongPoints(), [Interval(123, 129, "Thermal")], 100);
        True(sampled.Any(point => point.ElapsedSeconds == 129), "event end point");
    }

    private static void ShortEventSurvivesDownsampling()
    {
        IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(LongPoints(), [Interval(500, 501, "Thermal")], 80);
        True(sampled.Any(point => point.ElapsedSeconds == 500) && sampled.Any(point => point.ElapsedSeconds == 501), "short interval boundaries");
    }

    private static void LongChartPointCountIsBounded()
    {
        IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(LongPoints(100_000), [], 1500);
        True(sampled.Count <= 1500, "point cap");
    }

    private static async Task LargeCsvIsBoundedBeforeUiAsync()
    {
        using ReportFixture fixture = new(frameCount: 20_000);
        GameSessionReport report = await new GameSessionReportService().LoadAsync(fixture.Record);
        True(Chart(report, "fps").Series[0].Points.Count <= SessionChartDownsampler.DefaultMaximumPoints, "UI series cap");
        Equal(20_000L, report.ParsedFrameCount, "streamed row count");
    }

    private static async Task ClosingDetailReleasesDataAsync()
    {
        using ReportFixture fixture = new();
        GameSessionReportViewModel viewModel = new(fixture.Record, new GameSessionReportService(), () => { });
        await viewModel.LoadAsync();
        True(viewModel.Report is not null && viewModel.SelectedChart is not null, "loaded references");
        viewModel.Dispose();
        Null(viewModel.Report, "report reference after close");
        Null(viewModel.SelectedChart, "chart reference after close");
    }

    private static Task GameAndDetailViewsLoadRuntimeResourcesAsync()
    {
        using ReportFixture fixture = new();
        using GamePerformanceViewModel viewModel = new(
            new NoopGamePerformanceService(),
            Dispatcher.CurrentDispatcher,
            EmptyForegroundProcessTracker.Instance,
            null,
            new AppSettings(),
            new SettingsService(),
            sessionReportService: new FixedReportService(new GameSessionReport { Record = fixture.Record }));
        viewModel.SetActive(true);
        True(viewModel.IsUiRefreshTimerEnabled, "timer starts on active page");
        viewModel.SuspendRealtimeUiForSessionReport();
        False(viewModel.IsUiRefreshTimerEnabled, "detail pauses realtime timer");
        viewModel.ResumeRealtimeUiAfterSessionReport();
        True(viewModel.IsUiRefreshTimerEnabled, "timer resumes after close");

        if (Application.Current is null)
        {
            HardwareVision.App application = new();
            application.InitializeComponent();
        }
        GamePerformanceView gameView = new() { DataContext = viewModel };
        gameView.Measure(new Size(1280, 800));
        gameView.Arrange(new Rect(0, 0, 1280, 800));
        using GameSessionReportViewModel reportViewModel = new(
            fixture.Record,
            new FixedReportService(new GameSessionReport { Record = fixture.Record }),
            () => { });
        GameSessionReportView reportView = new() { DataContext = reportViewModel };
        reportView.Measure(new Size(1280, 800));
        reportView.Arrange(new Rect(0, 0, 1280, 800));
        True(gameView.DesiredSize.Width > 0d && reportView.DesiredSize.Width > 0d, "runtime XAML resources load");
        return Task.CompletedTask;
    }

    private static async Task ReportCacheHasFixedCapacityAsync()
    {
        GameSessionReportService service = new();
        List<ReportFixture> fixtures = [];
        try
        {
            for (int index = 0; index < 6; index++)
            {
                ReportFixture fixture = new(fileStem: "game-" + index.ToString(CultureInfo.InvariantCulture));
                fixtures.Add(fixture);
                _ = await service.LoadAsync(fixture.Record);
            }
            Equal(GameSessionReportService.MaximumCacheEntries, service.CacheCount, "fixed report cache capacity");
        }
        finally
        {
            foreach (ReportFixture fixture in fixtures) fixture.Dispose();
        }
    }

    private static SessionThrottleStatistics Statistics() => SessionThrottleStatisticsCalculator.Calculate(
        Series(),
        [Interval(3, 5, "Thermal")],
        10);

    private static SessionChartSeries Series() => new()
    {
        Name = "Clock",
        Unit = "MHz",
        Points =
        [
            new SessionChartPoint(0, 3000),
            new SessionChartPoint(2, 4000),
            new SessionChartPoint(3, 2100),
            new SessionChartPoint(5, 1900),
            new SessionChartPoint(8, 3500),
            new SessionChartPoint(10, 3500)
        ],
        Average = 3000,
        Minimum = 1900,
        Maximum = 4000
    };

    private static List<SessionChartPoint> LongPoints(int count = 5000)
    {
        List<SessionChartPoint> points = new(count);
        for (int index = 0; index < count; index++) points.Add(new SessionChartPoint(index, Math.Sin(index / 10d) * 100 + 1000));
        if (count > 300) points[200] = new SessionChartPoint(200, 9999);
        if (count > 400) points[300] = new SessionChartPoint(300, -9999);
        return points;
    }

    private static SessionLimitInterval Interval(double start, double end, string reason) => new()
    {
        StartSeconds = start,
        EndSeconds = end,
        ProcessorType = PerformanceLimitProcessorType.Cpu,
        Reasons = [reason]
    };

    private static GameHardwareTimelineRecorder TimelineRecorder(int capacity = 512) => new(
        null,
        null,
        () => 0,
        1000,
        TimeSpan.FromSeconds(1),
        capacity);

    private static GameHardwareTimelineSample CpuSample(IReadOnlyList<SensorReading> readings) =>
        GameHardwareTimelineSampler.CreateSamples(StartInfo(), readings, GamePerformanceLimitSnapshot.Empty, Start, 0)
            .Single(item => item.DeviceType == GameTimelineDeviceType.Cpu);

    private static IReadOnlyList<GameHardwareTimelineSample> GpuSamples(IReadOnlyList<SensorReading> readings) =>
        GameHardwareTimelineSampler.CreateSamples(StartInfo(), readings, GamePerformanceLimitSnapshot.Empty, Start, 0)
            .Where(item => item.DeviceType == GameTimelineDeviceType.Gpu).ToArray();

    private static SensorReading CpuClock(string name, double value) => new()
    {
        DeviceName = "Test CPU",
        SensorName = name,
        Category = SensorCategory.Cpu,
        Type = SensorType.Clock,
        Value = value,
        Unit = "MHz",
        IsAvailable = true,
        RawIdentifier = "/cpu/0/" + name
    };

    private static SensorReading GpuClock(string name, double value, string raw, string device = "GPU A") => new()
    {
        DeviceName = device,
        SensorName = name,
        Category = SensorCategory.Gpu,
        Type = SensorType.Clock,
        Value = value,
        Unit = "MHz",
        IsAvailable = true,
        RawIdentifier = raw
    };

    private static SensorReading StateReading(string name, SensorCategory category) => new()
    {
        DeviceName = category == SensorCategory.Cpu ? "CPU" : "GPU",
        SensorName = name,
        Category = category,
        Type = SensorType.State,
        Value = 1,
        Unit = "bool",
        IsAvailable = true,
        RawIdentifier = "/nvml/0/" + name
    };

    private static IReadOnlyList<SensorReading> ManyGpuReadings(int count)
    {
        List<SensorReading> result = new(count);
        for (int index = 0; index < count; index++)
        {
            result.Add(GpuClock("GPU Core Clock", 1000 + index, $"/gpu/{index}/core", "GPU " + index));
        }
        return result;
    }

    private static IReadOnlyList<GameHardwareTimelineSample> CpuTimeline(double first, double second) =>
    [
        TimelineSample(GameTimelineDeviceType.Cpu, "cpu", "Test CPU", 0, cpuClock: first),
        TimelineSample(GameTimelineDeviceType.Cpu, "cpu", "Test CPU", 60, cpuClock: second)
    ];

    private static IReadOnlyList<GameHardwareTimelineSample> GpuTimeline(string name, string id, double first, double second) =>
    [
        TimelineSample(GameTimelineDeviceType.Gpu, id, name, 0, gpuClock: first, gpuMemoryClock: 7000),
        TimelineSample(GameTimelineDeviceType.Gpu, id, name, 60, gpuClock: second, gpuMemoryClock: 7200)
    ];

    private static GameHardwareTimelineSample TimelineSample(
        GameTimelineDeviceType type,
        string id,
        string name,
        double elapsed,
        double? cpuClock = null,
        double? gpuClock = null,
        double? gpuMemoryClock = null) => new()
        {
            CaptureSessionId = SessionId,
            CaptureGeneration = Generation,
            Timestamp = Start.AddSeconds(elapsed),
            ElapsedSeconds = elapsed,
            DeviceType = type,
            DeviceId = id,
            DeviceName = name,
            CpuAverageCoreClockMHz = cpuClock,
            CpuMaximumCoreClockMHz = cpuClock,
            GpuCoreClockMHz = gpuClock,
            GpuMemoryClockMHz = gpuMemoryClock
        };

    private static GameFrameSample Frame(int index) => new()
    {
        CaptureSessionId = SessionId,
        Timestamp = Start.AddMilliseconds(index * 16.667),
        ProcessId = 42,
        ProcessName = "game.exe",
        Fps = 60,
        FrameTimeMs = 16.667,
        CpuBusyMs = 4,
        GpuTimeMs = 7,
        DisplayLatencyMs = 20
    };

    private static GameSessionStartInfo StartInfo() => new()
    {
        CaptureSessionId = SessionId,
        Generation = Generation,
        ProcessId = 42,
        ProcessName = "game.exe",
        CaptureStartedAt = Start
    };

    private static GamePerformanceLimitSnapshot Snapshot(params GamePerformanceLimitEvent[] events) => new()
    {
        CaptureSessionId = SessionId,
        Generation = Generation,
        IsTracking = false,
        CpuSupportStatus = PerformanceLimitSupportStatus.SupportedNormal,
        GpuSupportStatus = PerformanceLimitSupportStatus.SupportedNormal,
        Events = events
    };

    private static GamePerformanceLimitEvent Event(
        long id = 1,
        PerformanceLimitProcessorType type = PerformanceLimitProcessorType.Cpu,
        string reason = "Thermal Throttling",
        bool active = false,
        double startOffsetSeconds = 10,
        double durationSeconds = 2) => new()
        {
            EventId = id,
            CaptureSessionId = SessionId,
            Generation = Generation,
            ProcessorType = type,
            StartedAt = Start.AddSeconds(startOffsetSeconds),
            Duration = TimeSpan.FromSeconds(durationSeconds),
            IsActive = active,
            Reasons = [reason],
            RawReasonNames = ["Raw " + reason],
            Scopes = type == PerformanceLimitProcessorType.Gpu ? ["GPU A"] : ["CPU"],
            RawIdentifiers = ["/test/" + id],
            TriggerCount = 3,
            WasMerged = true
        };

    private static GamePerformanceLimitEvent CloneEvent(GamePerformanceLimitEvent source, Guid? sessionId = null, int? generation = null) => new()
    {
        EventId = source.EventId,
        CaptureSessionId = sessionId ?? source.CaptureSessionId,
        Generation = generation ?? source.Generation,
        ProcessorType = source.ProcessorType,
        StartedAt = source.StartedAt,
        Duration = source.Duration,
        IsActive = source.IsActive,
        Reasons = source.Reasons,
        RawReasonNames = source.RawReasonNames,
        Scopes = source.Scopes,
        RawIdentifiers = source.RawIdentifiers,
        TriggerCount = source.TriggerCount,
        WasMerged = source.WasMerged
    };

    private static SessionChartModel Chart(GameSessionReport report, string key) =>
        report.Charts.Single(chart => string.Equals(chart.Key, key, StringComparison.Ordinal));

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void Nearly(double expected, double? actual, string message, double tolerance = 0.0001)
    {
        if (!actual.HasValue || Math.Abs(expected - actual.Value) > tolerance) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Null(object? value, string message) => True(value is null, message);

    private static T NotNull<T>(T? value, string message) where T : class => value ?? throw new InvalidOperationException(message);

    private sealed class TempScope : IDisposable
    {
        public TempScope()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HardwareVision.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class ReportFixture : IDisposable
    {
        private readonly TempScope temp = new();

        public ReportFixture(int frameCount = 120, string fileStem = "game")
        {
            FramePath = Path.Combine(temp.Path, fileStem + ".csv");
            SummaryPath = Path.Combine(temp.Path, fileStem + ".summary.json");
            LimitPath = Path.Combine(temp.Path, fileStem + ".performance-limits.csv");
            TimelinePath = Path.Combine(temp.Path, fileStem + ".hardware-timeline.csv");
            using (StreamWriter writer = new(FramePath, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(GameCsvFormatting.Header);
                for (int index = 0; index < frameCount; index++) writer.WriteLine(GameCsvFormatting.FormatSample(Frame(index)));
            }
            GameSessionSummary summary = new()
            {
                CaptureSessionId = SessionId,
                CaptureGeneration = Generation,
                ProcessId = 42,
                ProcessName = "game.exe",
                CaptureStartedAt = Start,
                CaptureEndedAt = Start.AddMinutes(2),
                Duration = TimeSpan.FromMinutes(2),
                WrittenSampleCount = frameCount,
                AverageFps = 60,
                OnePercentLowFps = 50,
                ZeroPointOnePercentLowFps = 45,
                AverageFrameTimeMs = 16.667,
                AverageCpuBusyMs = 4,
                AverageGpuTimeMs = 7,
                AverageDisplayLatencyMs = 20,
                CompletedNormally = true,
                EndReason = GameSessionEndReason.UserStopped,
                CsvFileName = Path.GetFileName(FramePath)
            };
            File.WriteAllText(SummaryPath, JsonSerializer.Serialize(summary));
            Record = CreateRecord();
        }

        public string FramePath { get; }
        public string SummaryPath { get; }
        public string LimitPath { get; }
        public string TimelinePath { get; }
        public GameSessionRecordInfo Record { get; private set; }

        public async Task WriteLimitsAsync(params GamePerformanceLimitEvent[] events)
        {
            await GamePerformanceLimitCsv.WriteAsync(LimitPath, Snapshot(events), Start, CancellationToken.None);
            Record = CreateRecord();
        }

        public void WriteTimeline(IEnumerable<GameHardwareTimelineSample> samples)
        {
            File.WriteAllLines(TimelinePath, new[] { GameHardwareTimelineCsv.Header }.Concat(samples.Select(GameHardwareTimelineCsv.Format)));
            Record = CreateRecord();
        }

        private GameSessionRecordInfo CreateRecord() => new()
        {
            GameName = "game.exe",
            StartedAt = Start,
            Duration = TimeSpan.FromMinutes(2),
            IsComplete = true,
            CsvPath = FramePath,
            SummaryPath = SummaryPath,
            PerformanceLimitCsvPath = File.Exists(LimitPath) ? LimitPath : null,
            HardwareTimelineCsvPath = File.Exists(TimelinePath) ? TimelinePath : null,
            EndReason = GameSessionEndReason.UserStopped
        };

        public void Dispose() => temp.Dispose();
    }

    private sealed class FixedReportService(GameSessionReport report) : IGameSessionReportService
    {
        public Task<GameSessionReport> LoadAsync(GameSessionRecordInfo record, CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    private sealed class NoopGamePerformanceService : IGamePerformanceService
    {
        public event EventHandler<GameFrameSample>? FrameReceived { add { } remove { } }
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<GameCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public bool IsCaptureAvailable => true;
        public string StatusText => "idle";
        public GameCaptureState CaptureState => GameCaptureState.Idle;
        public string? CaptureToolPath => null;
        public IReadOnlyList<GameFrameSample> RecentSamples => [];
        public GamePerformanceSnapshot GetSnapshot(TimeSpan window) => new();
        public Task<IReadOnlyList<GameProcessInfo>> GetCandidateProcessesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameProcessInfo>>([]);
        public Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopCaptureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> ExportWindowCsvAsync(string directory, TimeSpan window, string? processName = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> ExportCacheCsvAsync(string directory, string? processName = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private static readonly Guid SessionId = Guid.Parse("4a84b066-fc92-4c36-999a-f9db59204586");
    private const int Generation = 7;
    private static readonly DateTimeOffset Start = new(2026, 7, 15, 12, 0, 0, TimeSpan.FromHours(8));
}
