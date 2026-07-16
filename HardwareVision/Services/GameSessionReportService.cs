using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class GameSessionReportService : IGameSessionReportService
{
    internal const int MaximumCacheEntries = 4;
    internal const int MaximumDisplayedLimitEvents = 200;
    private const int FrameBuckets = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object cacheLock = new();
    private readonly Dictionary<string, GameSessionReport> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> cacheOrder = new();

    internal int CacheCount
    {
        get { lock (cacheLock) return cache.Count; }
    }

    public Task<GameSessionReport> LoadAsync(
        GameSessionRecordInfo record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        string key = CreateCacheKey(record);
        lock (cacheLock)
        {
            if (cache.TryGetValue(key, out GameSessionReport? cached)) return Task.FromResult(cached);
        }

        return Task.Run(async () =>
        {
            GameSessionReport report = await LoadCoreAsync(record, cancellationToken).ConfigureAwait(false);
            lock (cacheLock)
            {
                if (!cache.ContainsKey(key))
                {
                    while (cache.Count >= MaximumCacheEntries && cacheOrder.Count > 0)
                    {
                        cache.Remove(cacheOrder.Dequeue());
                    }
                    cache.Add(key, report);
                    cacheOrder.Enqueue(key);
                }
            }
            return report;
        }, cancellationToken);
    }

    private static async Task<GameSessionReport> LoadCoreAsync(
        GameSessionRecordInfo record,
        CancellationToken cancellationToken)
    {
        List<string> warnings = [];
        string? summaryPath = ResolveRelatedPath(
            record.CsvPath,
            record.SummaryPath,
            summaryFileName: null,
            SessionFileKind.SummaryJson,
            static framePath => GameSessionFileNaming.GetRelatedPath(framePath, ".summary.json"),
            warnings);
        GameSessionSummary? summary = await ReadSummaryAsync(summaryPath, warnings, cancellationToken).ConfigureAwait(false);
        if (summary?.SessionSchemaVersion > 4)
        {
            warnings.Add($"summary.json 来自未来架构版本 v{summary.SessionSchemaVersion}，将按已知字段读取");
        }
        Guid? sessionId = summary is null || summary.CaptureSessionId == Guid.Empty ? null : summary.CaptureSessionId;
        int? generation = summary?.CaptureGeneration;

        int warningsBeforeLimitPath = warnings.Count;
        string? limitPath = ResolveRelatedPath(
            record.CsvPath,
            record.PerformanceLimitCsvPath,
            summary?.PerformanceLimitCsvFileName,
            SessionFileKind.PerformanceLimitsCsv,
            GamePerformanceLimitCsv.GetPath,
            warnings);
        bool limitPathRejected = limitPath is null
            && (!string.IsNullOrWhiteSpace(record.PerformanceLimitCsvPath)
                || !string.IsNullOrWhiteSpace(summary?.PerformanceLimitCsvFileName))
            && warnings.Count > warningsBeforeLimitPath;
        PerformanceLimitCsvReadResult limitResult;
        try
        {
            limitResult = limitPath is null
                ? PerformanceLimitCsvReadResult.NotPresent
                : await GamePerformanceLimitCsv.ReadAsync(limitPath, sessionId, generation, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(limitResult.Warnings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("性能限制 CSV 无法读取");
            AppLogger.LogError("Performance-limit CSV could not be read.", exception,
                $"session-report-limits:{Path.GetFileName(limitPath)}", TimeSpan.FromMinutes(5));
            limitResult = new PerformanceLimitCsvReadResult(true, false, [], []);
        }

        bool limitEventsLoadedFromLegacySummary = false;
        IReadOnlyList<GamePerformanceLimitEvent> limitEvents = limitResult.Events;
        if (!limitResult.IsPresent && summary?.PerformanceLimitEvents is not null)
        {
            limitEvents = ReadLegacySummaryLimitEvents(summary, sessionId, generation, warnings);
            limitEventsLoadedFromLegacySummary = true;
        }

        FrameReadResult frames = await ReadFramesAsync(record, summary, warnings, cancellationToken).ConfigureAwait(false);
        int warningsBeforeTimelinePath = warnings.Count;
        string? timelinePath = ResolveRelatedPath(
            record.CsvPath,
            record.HardwareTimelineCsvPath,
            summary?.HardwareTimelineCsvFileName,
            SessionFileKind.HardwareTimelineCsv,
            GameHardwareTimelineCsv.GetPath,
            warnings);
        bool timelinePathRejected = timelinePath is null
            && (!string.IsNullOrWhiteSpace(record.HardwareTimelineCsvPath)
                || !string.IsNullOrWhiteSpace(summary?.HardwareTimelineCsvFileName))
            && warnings.Count > warningsBeforeTimelinePath;
        TimelineReadResult timeline = await ReadTimelineAsync(
            timelinePath,
            sessionId,
            generation,
            warnings,
            cancellationToken).ConfigureAwait(false);

        double durationSeconds = Math.Max(
            summary?.Duration.TotalSeconds ?? record.Duration.TotalSeconds,
            Math.Max(frames.DurationSeconds, timeline.DurationSeconds));
        IReadOnlyList<SessionChartModel> charts = BuildCharts(
            frames,
            timeline,
            limitEvents,
            summary?.CaptureStartedAt ?? record.StartedAt,
            durationSeconds,
            timeline.IsPresent);
        IReadOnlyList<GamePerformanceLimitEvent> displayEvents = LimitEvents(limitEvents);

        return new GameSessionReport
        {
            Record = record,
            Summary = summary,
            Charts = charts,
            PerformanceLimitEvents = displayEvents,
            PerformanceLimitEventsLoadedFromLegacySummary = limitEventsLoadedFromLegacySummary,
            PerformanceLimitFileStatus = limitPathRejected
                ? SessionAuxiliaryFileStatus.Unavailable
                : !limitResult.IsPresent && !limitEventsLoadedFromLegacySummary
                ? SessionAuxiliaryFileStatus.NotRecorded
                : !limitResult.IsValid
                    ? SessionAuxiliaryFileStatus.Unavailable
                    : limitEvents.Count == 0
                        ? SessionAuxiliaryFileStatus.RecordedNoData
                        : SessionAuxiliaryFileStatus.Recorded,
            HardwareTimelineFileStatus = timelinePathRejected
                ? SessionAuxiliaryFileStatus.Unavailable
                : !timeline.IsPresent
                ? SessionAuxiliaryFileStatus.NotRecorded
                : !timeline.IsValid
                    ? SessionAuxiliaryFileStatus.Unavailable
                    : timeline.RowCount == 0
                        ? SessionAuxiliaryFileStatus.RecordedNoData
                        : SessionAuxiliaryFileStatus.Recorded,
            RawFrameRowCount = frames.RawRowCount,
            ParsedFrameCount = frames.ParsedFrameCount,
            AcceptedFrameCount = frames.FrameCount,
            FilteredFrameCount = Math.Max(0L, frames.RawRowCount - frames.FrameCount),
            FrameCsvIsPartial = frames.IsPartial,
            FrameCsvFailureRow = frames.FailureRow,
            FrameTimeAxisSource = frames.TimeAxisSource,
            MinimumFps = frames.MinimumFps,
            MaximumFps = frames.SustainedMaximumFps,
            RawMaximumFps = Maximum(frames.RawMaximumFps, summary?.RawMaximumFps),
            SustainedMaximumFps = frames.SustainedMaximumFps ?? summary?.SustainedMaximumFps,
            AverageFps = frames.AverageFps,
            OnePercentLowFps = frames.OnePercentLowFps,
            ZeroPointOnePercentLowFps = frames.ZeroPointOnePercentLowFps,
            AverageFrameTimeMs = frames.AverageFrameTimeMs,
            AverageCpuBusyMs = frames.AverageCpuBusyMs,
            AverageGpuTimeMs = frames.AverageGpuTimeMs,
            AverageDisplayLatencyMs = frames.AverageDisplayLatencyMs,
            FrameQualityDiagnostics = CombineFrameDiagnostics(frames.Diagnostics, summary),
            UsedHistoricalValidationFallback = frames.Diagnostics.UsedCompatibilityFallback,
            LastFps = frames.LastFps,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static GameFrameQualityDiagnostics CombineFrameDiagnostics(
        GameFrameQualityDiagnostics replay,
        GameSessionSummary? summary)
    {
        if (summary is null) return replay;
        return new GameFrameQualityDiagnostics
        {
            WarmupCandidateSampleCount = summary.WarmupCandidateSampleCount ?? replay.WarmupCandidateSampleCount,
            WarmupDiscardedSampleCount = summary.WarmupDiscardedSampleCount ?? replay.WarmupDiscardedSampleCount,
            NonPrimarySwapChainSampleCount = (summary.NonPrimarySwapChainSampleCount ?? 0L) + replay.NonPrimarySwapChainSampleCount,
            InvalidFrameTimeSampleCount = (summary.InvalidFrameTimeSampleCount ?? 0L) + replay.InvalidFrameTimeSampleCount,
            FrameTimeOutlierSampleCount = (summary.FrameTimeOutlierSampleCount ?? 0L) + replay.FrameTimeOutlierSampleCount,
            DuplicateCaptureElapsedSampleCount = (summary.DuplicateCaptureElapsedSampleCount ?? 0L) + replay.DuplicateCaptureElapsedSampleCount,
            RegressedCaptureElapsedSampleCount = (summary.RegressedCaptureElapsedSampleCount ?? 0L) + replay.RegressedCaptureElapsedSampleCount,
            DuplicateExplicitTimestampSampleCount = (summary.DuplicateExplicitTimestampSampleCount ?? 0L) + replay.DuplicateExplicitTimestampSampleCount,
            RegressedExplicitTimestampSampleCount = (summary.RegressedExplicitTimestampSampleCount ?? 0L) + replay.RegressedExplicitTimestampSampleCount,
            MissingTimestampSampleCount = (summary.MissingTimestampSampleCount ?? 0L) + replay.MissingTimestampSampleCount,
            CompatibilityFallbackSampleCount = (summary.CompatibilityFallbackSampleCount ?? 0L) + replay.CompatibilityFallbackSampleCount,
            StableLevelTransitionCandidateSampleCount = (summary.StableLevelTransitionCandidateSampleCount ?? 0L) + replay.StableLevelTransitionCandidateSampleCount,
            StableLevelTransitionConfirmedCount = (summary.StableLevelTransitionConfirmedCount ?? 0L) + replay.StableLevelTransitionConfirmedCount,
            SanitizedMetricFieldCount = (summary.SanitizedMetricFieldCount ?? 0L) + replay.SanitizedMetricFieldCount,
            InvalidAuxiliaryMetricFieldCount = (summary.InvalidAuxiliaryMetricFieldCount ?? 0L) + replay.InvalidAuxiliaryMetricFieldCount,
            AuxiliaryMetricOutlierFieldCount = (summary.AuxiliaryMetricOutlierFieldCount ?? 0L) + replay.AuxiliaryMetricOutlierFieldCount,
            CpuBusySanitizedCount = (summary.CpuBusySanitizedCount ?? 0L) + replay.CpuBusySanitizedCount,
            CpuWaitSanitizedCount = (summary.CpuWaitSanitizedCount ?? 0L) + replay.CpuWaitSanitizedCount,
            GpuLatencySanitizedCount = (summary.GpuLatencySanitizedCount ?? 0L) + replay.GpuLatencySanitizedCount,
            GpuTimeSanitizedCount = (summary.GpuTimeSanitizedCount ?? 0L) + replay.GpuTimeSanitizedCount,
            GpuBusySanitizedCount = (summary.GpuBusySanitizedCount ?? 0L) + replay.GpuBusySanitizedCount,
            GpuWaitSanitizedCount = (summary.GpuWaitSanitizedCount ?? 0L) + replay.GpuWaitSanitizedCount,
            RenderLatencySanitizedCount = (summary.RenderLatencySanitizedCount ?? 0L) + replay.RenderLatencySanitizedCount,
            DisplayLatencySanitizedCount = (summary.DisplayLatencySanitizedCount ?? 0L) + replay.DisplayLatencySanitizedCount,
            DisplayedTimeSanitizedCount = (summary.DisplayedTimeSanitizedCount ?? 0L) + replay.DisplayedTimeSanitizedCount,
            ClickToPhotonLatencySanitizedCount = (summary.ClickToPhotonLatencySanitizedCount ?? 0L) + replay.ClickToPhotonLatencySanitizedCount,
            PrimarySwapChainAddress = replay.PrimarySwapChainAddress ?? summary.PrimarySwapChainAddress,
            SwapChainSwitchCount = replay.SwapChainSwitchCount + (summary.SwapChainSwitchCount ?? 0),
            CaptureWarmupDurationSeconds = summary.CaptureWarmupDurationSeconds ?? replay.CaptureWarmupDurationSeconds,
            UsedCompatibilityFallback = replay.UsedCompatibilityFallback || summary.UsedCompatibilityFallback == true,
            PrimarySwapChainSelectionUncertain = replay.PrimarySwapChainSelectionUncertain || summary.PrimarySwapChainSelectionUncertain == true,
            RawMaximumFps = Maximum(replay.RawMaximumFps, summary.RawMaximumFps),
            SustainedMaximumFps = replay.SustainedMaximumFps ?? summary.SustainedMaximumFps
        };
    }

    private static double? Maximum(double? left, double? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return Math.Max(left.Value, right.Value);
    }

    private static IReadOnlyList<GamePerformanceLimitEvent> ReadLegacySummaryLimitEvents(
        GameSessionSummary summary,
        Guid? expectedSessionId,
        int? expectedGeneration,
        List<string> warnings)
    {
        IReadOnlyList<GamePerformanceLimitEvent> source = summary.PerformanceLimitEvents ?? [];
        List<GamePerformanceLimitEvent> result = new(source.Count);
        bool ignoredMismatchedEvent = false;
        for (int index = 0; index < source.Count; index++)
        {
            GamePerformanceLimitEvent? item = source[index];
            if (item is null
                || expectedSessionId.HasValue && item.CaptureSessionId != expectedSessionId.Value
                || expectedGeneration.HasValue && item.Generation != expectedGeneration.Value)
            {
                ignoredMismatchedEvent = true;
                continue;
            }

            result.Add(item);
        }

        if (ignoredMismatchedEvent)
        {
            warnings.Add("已忽略旧版 summary.json 中 SessionId 或 generation 不匹配的限制事件");
        }

        result.Sort(static (left, right) =>
        {
            int startedAtComparison = left.StartedAt.CompareTo(right.StartedAt);
            return startedAtComparison != 0
                ? startedAtComparison
                : left.EventId.CompareTo(right.EventId);
        });
        return result;
    }

    private static IReadOnlyList<GamePerformanceLimitEvent> LimitEvents(IReadOnlyList<GamePerformanceLimitEvent> events)
    {
        if (events.Count <= MaximumDisplayedLimitEvents) return events;
        GamePerformanceLimitEvent[] result = new GamePerformanceLimitEvent[MaximumDisplayedLimitEvents];
        int sourceStart = events.Count - result.Length;
        for (int index = 0; index < result.Length; index++) result[index] = events[sourceStart + index];
        return result;
    }

    private static async Task<GameSessionSummary?> ReadSummaryAsync(
        string? path,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            warnings.Add("该记录没有可用的 summary.json");
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            GameSessionSummary? summary = await JsonSerializer.DeserializeAsync<GameSessionSummary>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (summary is null) warnings.Add("summary.json 内容为空");
            return summary;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add("summary.json 损坏或无法读取");
            AppLogger.LogError("Game session report summary could not be read.", exception,
                $"session-report-summary:{Path.GetFileName(path)}", TimeSpan.FromMinutes(5));
            return null;
        }
    }

    private static async Task<FrameReadResult> ReadFramesAsync(
        GameSessionRecordInfo record,
        GameSessionSummary? summary,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(record.CsvPath))
        {
            warnings.Add("逐帧 CSV 不存在");
            return FrameReadResult.Empty;
        }

        FrameReadResult? result = null;
        try
        {
            long estimatedRows = summary?.WrittenSampleCount > 0
                ? summary.WrittenSampleCount
                : Math.Max(1L, new FileInfo(record.CsvPath).Length / 220L);
            int bucketSize = Math.Max(1, (int)Math.Ceiling(estimatedRows / (double)FrameBuckets));
            result = new(
                bucketSize,
                summary?.CaptureStartedAt ?? record.StartedAt,
                Math.Max(summary?.Duration.TotalSeconds ?? 0d, record.Duration.TotalSeconds),
                warnings);
            PresentMonCsvParser parser = new(
                summary?.CaptureSessionId ?? Guid.Empty,
                summary?.ProcessId ?? 0,
                summary?.ProcessName ?? record.GameName);
            GameFrameValidationPipeline validation = new(new GameCaptureWarmupOptions
            {
                HistoricalReplayMode = true
            });
            using StreamReader reader = GameSessionFrameStreamFactory.Shared.OpenTextReader(record.CsvPath);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                PresentMonCsvParseResult parsed = parser.ParseLine(line);
                if (parsed.Kind == PresentMonCsvParseKind.HeaderAccepted)
                {
                    validation.AcceptHeader();
                    continue;
                }

                if (parsed.IsDataRow)
                {
                    result.RecordRawRow();
                }

                if (parsed.Kind == PresentMonCsvParseKind.Sample && parsed.Sample is GameFrameSample sample)
                {
                    result.RecordParsedFrame();
                    GameFrameValidationResult validated = validation.Process(
                        sample,
                        result.ResolveObservedAt(sample));
                    if (validated.IsAccepted && validated.Sample is not null)
                    {
                        result.Add(validated.Sample);
                    }
                }
            }
            validation.Complete();
            result.Complete(validation.GetDiagnostics(DateTimeOffset.UtcNow));
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add(GameSessionFrameStreamFactory.Shared.IsCompressed(record.CsvPath)
                ? "压缩记录只能部分读取"
                : "逐帧 CSV 只能部分读取");
            AppLogger.LogError("Frame CSV could not be fully read for session report.", exception,
                $"session-report-frames:{Path.GetFileName(record.CsvPath)}", TimeSpan.FromMinutes(5));
            if (result is null) return FrameReadResult.Empty;
            result.MarkPartial(exception);
            result.Complete();
            return result;
        }
    }

    private static async Task<TimelineReadResult> ReadTimelineAsync(
        string? path,
        Guid? sessionId,
        int? generation,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return TimelineReadResult.NotPresent;
        TimelineReadResult result = new(true, true);
        try
        {
            using StreamReader reader = new(path, Encoding.UTF8, true, 32 * 1024);
            string? header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(header))
            {
                warnings.Add("硬件时间线 CSV 列头不受支持");
                return new TimelineReadResult(true, false);
            }
            SessionCsvColumnMap columns = SessionCsvColumnMap.Create(header);
            if (!columns.HasRequired(
                    out string? missing,
                    "CaptureSessionId",
                    "CaptureGeneration",
                    "Timestamp",
                    "ElapsedSeconds",
                    "DeviceType",
                    "DeviceId",
                    "DeviceName"))
            {
                warnings.Add($"硬件时间线 CSV 缺少必需列：{missing}");
                return new TimelineReadResult(true, false);
            }
            int invalidRows = 0;
            bool warnedFutureSchema = false;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                if (!warnedFutureSchema && columns.Has("SessionSchemaVersion"))
                {
                    IReadOnlyList<string> fields = PresentMonCsvParser.ParseColumns(line);
                    if (int.TryParse(
                            columns.Get(fields, "SessionSchemaVersion"),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int schemaVersion)
                        && schemaVersion > 2)
                    {
                        warnings.Add($"纭欢鏃堕棿绾?CSV 鏉ヨ嚜鏈潵鏋舵瀯鐗堟湰 v{schemaVersion}锛屽皢鎸夊凡鐭ュ垪璇诲彇");
                        warnedFutureSchema = true;
                    }
                }

                if (GameHardwareTimelineCsv.TryParse(line, columns, sessionId, generation, out GameHardwareTimelineSample? sample)
                    && sample is not null)
                {
                    result.Add(sample);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    invalidRows++;
                }
            }
            if (invalidRows > 0) warnings.Add($"硬件时间线已跳过 {invalidRows} 个损坏或会话不匹配的行");
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add("硬件时间线 CSV 只能部分读取");
            AppLogger.LogError("Hardware timeline could not be fully read for session report.", exception,
                $"session-report-timeline:{Path.GetFileName(path)}", TimeSpan.FromMinutes(5));
            result.IsValid = false;
            return result;
        }
    }

    private static IReadOnlyList<SessionChartModel> BuildCharts(
        FrameReadResult frames,
        TimelineReadResult timeline,
        IReadOnlyList<GamePerformanceLimitEvent> events,
        DateTimeOffset startedAt,
        double durationSeconds,
        bool timelinePresent)
    {
        List<SessionChartModel> charts = [];
        AddFrameChart(charts, "fps", "FPS", "逐帧帧率；历史页面不表示实时当前值", "FPS", frames.Fps, durationSeconds);
        AddFrameChart(charts, "frame-time", "Frame Time", "逐帧耗时", "ms", frames.FrameTime, durationSeconds);
        AddFrameChart(charts, "cpu-busy", "CPU Busy", "PresentMon CPUBusy", "ms", frames.CpuBusy, durationSeconds);
        AddFrameChart(charts, "gpu-time", "GPU Time", "PresentMon GPUTime", "ms", frames.GpuTime, durationSeconds);
        AddFrameChart(charts, "display-latency", "Display Latency", "PresentMon DisplayLatency", "ms", frames.DisplayLatency, durationSeconds);

        TimelineDeviceData? cpu = timeline.FindFirst(GameTimelineDeviceType.Cpu);
        IReadOnlyList<SessionLimitInterval> cpuIntervals = BuildIntervals(events, PerformanceLimitProcessorType.Cpu, null, 1, startedAt, cpu);
        charts.Add(BuildMultiSeriesChart(
            "cpu-frequency",
            "CPU 频率与限制",
            "普通核心 Clock 算术平均 / Effective Clock 独立口径 / 最大有效核心 Clock；Bus Clock 已排除",
            durationSeconds,
            cpuIntervals,
            timelinePresent ? "该会话没有可用 CPU 频率数据" : "该会话未记录硬件频率时间序列",
            ("平均核心频率", "MHz", cpu?.CpuAverageClock),
            ("有效频率", "MHz", cpu?.CpuEffectiveClock),
            ("最大核心频率", "MHz", cpu?.CpuMaximumClock)));

        List<TimelineDeviceData> gpus = timeline.FindAll(GameTimelineDeviceType.Gpu);
        gpus.Sort(static (left, right) => GpuDisplayPriority(right).CompareTo(GpuDisplayPriority(left)));
        for (int index = 0; index < gpus.Count; index++)
        {
            TimelineDeviceData gpu = gpus[index];
            IReadOnlyList<SessionLimitInterval> gpuIntervals = BuildIntervals(
                events,
                PerformanceLimitProcessorType.Gpu,
                gpu.DeviceName,
                gpus.Count,
                startedAt,
                gpu);
            charts.Add(BuildMultiSeriesChart(
                "gpu-frequency:" + gpu.DeviceId,
                "GPU 频率与限制 · " + gpu.DeviceName,
                "GPU Core Clock 与 Memory Clock 分开记录",
                durationSeconds,
                gpuIntervals,
                "该会话没有此 GPU 的频率数据",
                ("核心频率", "MHz", gpu.GpuCoreClock),
                ("显存频率", "MHz", gpu.GpuMemoryClock)));
        }
        if (gpus.Count == 0)
        {
            IReadOnlyList<SessionLimitInterval> intervals = BuildIntervals(events, PerformanceLimitProcessorType.Gpu, null, 1, startedAt, null);
            charts.Add(BuildMultiSeriesChart(
                "gpu-frequency",
                "GPU 频率与限制",
                "GPU Core Clock 与 Memory Clock 分开记录",
                durationSeconds,
                intervals,
                timelinePresent ? "该会话没有可用 GPU 频率数据" : "该会话未记录硬件频率时间序列"));
        }

        List<(string Name, string Unit, IReadOnlyList<SessionChartPoint>? Points)> powerSeries =
        [
            ("CPU Package", "W", cpu?.CpuPower)
        ];
        for (int index = 0; index < gpus.Count; index++)
        {
            powerSeries.Add(($"GPU · {gpus[index].DeviceName}", "W", gpus[index].GpuPower));
        }
        charts.Add(BuildMultiSeriesChart(
            "estimated-power",
            "估算功率",
            "来自会话硬件时间线中的 CPU Package / GPU Board 功率传感器",
            durationSeconds,
            [],
            timelinePresent ? "没有可用功率传感器数据" : "该会话未记录硬件时间序列",
            powerSeries.ToArray()));
        charts.Add(BuildMultiSeriesChart(
            "cpu-temperature",
            "CPU 温度",
            "会话时间线中的代表性 CPU Package 温度",
            durationSeconds,
            cpuIntervals,
            timelinePresent ? "没有可用 CPU 温度数据" : "该会话未记录硬件时间序列",
            ("CPU 温度", "℃", cpu?.CpuTemperature)));
        for (int index = 0; index < gpus.Count; index++)
        {
            TimelineDeviceData gpu = gpus[index];
            charts.Add(BuildMultiSeriesChart(
                "gpu-temperature:" + gpu.DeviceId,
                "GPU 温度 · " + gpu.DeviceName,
                "Core 与 Hot Spot 温度分开显示",
                durationSeconds,
                BuildIntervals(events, PerformanceLimitProcessorType.Gpu, gpu.DeviceName, gpus.Count, startedAt, gpu),
                "没有可用 GPU 温度数据",
                ("Core", "℃", gpu.GpuTemperature),
                ("Hot Spot", "℃", gpu.GpuHotSpotTemperature)));
        }
        return charts;
    }

    private static double GpuDisplayPriority(TimelineDeviceData gpu)
    {
        double maximumLoad = -1d;
        for (int index = 0; index < gpu.GpuLoad.Count; index++) maximumLoad = Math.Max(maximumLoad, gpu.GpuLoad[index].Value);
        double discreteFallback = gpu.DeviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
            || gpu.DeviceName.Contains("GeForce", StringComparison.OrdinalIgnoreCase)
            || gpu.DeviceName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
            || gpu.DeviceName.Contains("AMD", StringComparison.OrdinalIgnoreCase)
                ? 0.1d
                : 0d;
        return maximumLoad >= 0d ? maximumLoad + 100d : discreteFallback;
    }

    private static void AddFrameChart(
        List<SessionChartModel> charts,
        string key,
        string title,
        string subtitle,
        string unit,
        IReadOnlyList<SessionChartPoint> points,
        double durationSeconds)
    {
        charts.Add(BuildMultiSeriesChart(key, title, subtitle, durationSeconds, [], "--", (title, unit, points)));
    }

    private static SessionChartModel BuildMultiSeriesChart(
        string key,
        string title,
        string subtitle,
        double durationSeconds,
        IReadOnlyList<SessionLimitInterval> intervals,
        string emptyText,
        params (string Name, string Unit, IReadOnlyList<SessionChartPoint>? Points)[] sourceSeries)
    {
        List<SessionChartSeries> series = [];
        for (int index = 0; index < sourceSeries.Length; index++)
        {
            IReadOnlyList<SessionChartPoint>? points = sourceSeries[index].Points;
            if (points is null || points.Count == 0) continue;
            series.Add(CreateSeries(sourceSeries[index].Name, sourceSeries[index].Unit, points, intervals));
        }
        return new SessionChartModel
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            Series = series,
            LimitIntervals = intervals,
            DurationSeconds = durationSeconds,
            EmptyText = emptyText,
            ThrottleStatistics = SessionThrottleStatisticsCalculator.CalculateRaw(
                sourceSeries.FirstOrDefault().Points ?? [],
                intervals,
                durationSeconds)
        };
    }

    private static SessionChartSeries CreateSeries(
        string name,
        string unit,
        IReadOnlyList<SessionChartPoint> points,
        IReadOnlyList<SessionLimitInterval> intervals)
    {
        double sum = 0d;
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        for (int index = 0; index < points.Count; index++)
        {
            sum += points[index].Value;
            minimum = Math.Min(minimum, points[index].Value);
            maximum = Math.Max(maximum, points[index].Value);
        }
        return new SessionChartSeries
        {
            Name = name,
            Unit = unit,
            Points = SessionChartDownsampler.Downsample(points, intervals),
            Average = points.Count > 0 ? sum / points.Count : null,
            Minimum = points.Count > 0 ? minimum : null,
            Maximum = points.Count > 0 ? maximum : null
        };
    }

    private static IReadOnlyList<SessionLimitInterval> BuildIntervals(
        IReadOnlyList<GamePerformanceLimitEvent> events,
        PerformanceLimitProcessorType processorType,
        string? deviceName,
        int deviceCount,
        DateTimeOffset startedAt,
        TimelineDeviceData? timelineDevice)
    {
        List<SessionLimitInterval> result = [];
        for (int index = 0; index < events.Count; index++)
        {
            GamePerformanceLimitEvent item = events[index];
            if (item.ProcessorType != processorType
                || !EventApplies(item, timelineDevice?.DeviceId, deviceName, deviceCount)) continue;
            double start = Math.Max(0d, (item.StartedAt - startedAt).TotalSeconds);
            double end = Math.Max(start, start + item.Duration.TotalSeconds);
            string details = BuildIntervalDetails(item, start, end, timelineDevice);
            result.Add(new SessionLimitInterval
            {
                EventId = item.EventId,
                ProcessorType = item.ProcessorType,
                StartSeconds = start,
                EndSeconds = end,
                Reasons = item.Reasons,
                RawReasons = item.RawReasonNames,
                RawIdentifiers = item.RawIdentifiers,
                TriggerCount = item.TriggerCount,
                ToolTip = details
            });
        }
        return result;
    }

    private static string BuildIntervalDetails(
        GamePerformanceLimitEvent item,
        double start,
        double end,
        TimelineDeviceData? device)
    {
        StringBuilder builder = new();
        builder.Append(item.ProcessorText).Append(' ').Append(FormatElapsed(start)).Append('–').Append(FormatElapsed(end))
            .Append(" · ").Append(item.Duration.TotalSeconds.ToString("0.##", CultureInfo.CurrentCulture)).Append(" s")
            .AppendLine().Append("原因：").Append(item.ReasonText)
            .AppendLine().Append("原始原因：").Append(item.RawReasonText)
            .AppendLine().Append("RawIdentifier：").Append(item.RawIdentifiers.Count == 0 ? "--" : string.Join(" / ", item.RawIdentifiers));
        if (device is null) return builder.ToString();
        if (item.ProcessorType == PerformanceLimitProcessorType.Cpu)
        {
            AppendIntervalMetric(builder, "平均核心频率", device.CpuAverageClock, start, end, "MHz");
            AppendIntervalMetric(builder, "有效频率", device.CpuEffectiveClock, start, end, "MHz");
            AppendIntervalMetric(builder, "温度", device.CpuTemperature, start, end, "℃");
            AppendIntervalMetric(builder, "功耗", device.CpuPower, start, end, "W");
            AppendIntervalMetric(builder, "负载", device.CpuLoad, start, end, "%");
        }
        else
        {
            AppendIntervalMetric(builder, "核心频率", device.GpuCoreClock, start, end, "MHz");
            AppendIntervalMetric(builder, "显存频率", device.GpuMemoryClock, start, end, "MHz");
            AppendIntervalMetric(builder, "温度", device.GpuTemperature, start, end, "℃");
            AppendIntervalMetric(builder, "Hot Spot", device.GpuHotSpotTemperature, start, end, "℃");
            AppendIntervalMetric(builder, "功耗", device.GpuPower, start, end, "W");
            AppendIntervalMetric(builder, "负载", device.GpuLoad, start, end, "%");
        }
        return builder.ToString();
    }

    private static void AppendIntervalMetric(
        StringBuilder builder,
        string name,
        IReadOnlyList<SessionChartPoint> points,
        double start,
        double end,
        string unit)
    {
        double sum = 0d;
        int count = 0;
        for (int index = 0; index < points.Count; index++)
        {
            if (points[index].ElapsedSeconds < start || points[index].ElapsedSeconds > end) continue;
            sum += points[index].Value;
            count++;
        }
        builder.AppendLine().Append(name).Append("：")
            .Append(count == 0 ? "--" : (sum / count).ToString("0.##", CultureInfo.CurrentCulture) + " " + unit);
    }

    private static bool EventApplies(
        GamePerformanceLimitEvent item,
        string? deviceId,
        string? deviceName,
        int deviceCount)
    {
        if (item.ProcessorType == PerformanceLimitProcessorType.Cpu) return true;
        if (!string.IsNullOrWhiteSpace(item.DeviceId))
            return string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
        if (deviceCount <= 1) return true;
        if (item.Scopes.Count == 0) return false;
        for (int index = 0; index < item.Scopes.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(deviceId)
                && item.Scopes[index].Contains(deviceId, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(deviceName)
                && (item.Scopes[index].Contains(deviceName, StringComparison.OrdinalIgnoreCase)
                    || deviceName.Contains(item.Scopes[index], StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private static string FormatElapsed(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");

    private static string? ResolveRelatedPath(
        string frameCsvPath,
        string? recordPath,
        string? summaryFileName,
        SessionFileKind kind,
        Func<string, string> derive,
        List<string> warnings)
    {
        string? directory = Path.GetDirectoryName(frameCsvPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            warnings.Add("会话文件目录无效");
            return null;
        }

        if (!string.IsNullOrWhiteSpace(recordPath))
        {
            if (SessionFilePathResolver.TryValidatePath(directory, recordPath, kind, out string? validated, out string? warning))
            {
                return validated;
            }

            if (!string.IsNullOrWhiteSpace(warning)) warnings.Add(warning);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(summaryFileName))
        {
            if (SessionFilePathResolver.TryResolve(directory, summaryFileName, kind, out string? resolved, out string? warning))
            {
                return resolved;
            }

            if (!string.IsNullOrWhiteSpace(warning)) warnings.Add(warning);
            return null;
        }

        string derived = derive(frameCsvPath);
        if (!SessionFilePathResolver.TryValidatePath(directory, derived, kind, out string? safeDerived, out string? derivedWarning))
        {
            if (!string.IsNullOrWhiteSpace(derivedWarning)) warnings.Add(derivedWarning);
            return null;
        }

        return File.Exists(safeDerived) ? safeDerived : null;
    }

    private static string CreateCacheKey(GameSessionRecordInfo record)
    {
        return string.Join('|',
            record.CsvPath,
            LastWrite(record.CsvPath),
            record.SummaryPath,
            LastWrite(record.SummaryPath),
            LastWrite(record.PerformanceLimitCsvPath),
            LastWrite(record.HardwareTimelineCsvPath));
    }

    private static long LastWrite(string? path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return 0L; }
    }

    private sealed class FrameReadResult
    {
        private readonly int bucketSize;
        private readonly DateTimeOffset captureStartedAt;
        private readonly double maximumDurationSeconds;
        private readonly List<string> warnings;
        private readonly FrameBucket bucket = new();
        private readonly FrameTimeHistogram frameTimeHistogram = new();
        private double elapsedSeconds;
        private double accumulatedFrameSeconds;
        private double frameTimeSum;
        private double cpuBusySum;
        private double gpuTimeSum;
        private double displayLatencySum;
        private long cpuBusyCount;
        private long gpuTimeCount;
        private long displayLatencyCount;
        private int rowsInBucket;
        private bool warnedNonMonotonic;
        private bool warnedOutOfRange;
        private bool completed;

        public FrameReadResult(
            int bucketSize,
            DateTimeOffset captureStartedAt,
            double maximumDurationSeconds,
            List<string> warnings)
        {
            this.bucketSize = bucketSize;
            this.captureStartedAt = captureStartedAt;
            this.maximumDurationSeconds = Math.Max(0d, maximumDurationSeconds);
            this.warnings = warnings;
        }

        public static FrameReadResult Empty { get; } = new(1, DateTimeOffset.MinValue, 0d, []);
        public List<SessionChartPoint> Fps { get; } = [];
        public List<SessionChartPoint> FrameTime { get; } = [];
        public List<SessionChartPoint> CpuBusy { get; } = [];
        public List<SessionChartPoint> GpuTime { get; } = [];
        public List<SessionChartPoint> DisplayLatency { get; } = [];
        public long RawRowCount { get; private set; }
        public long ParsedFrameCount { get; private set; }
        public long FrameCount { get; private set; }
        public double? MinimumFps { get; private set; }
        public double? RawMaximumFps { get; private set; }
        public double? SustainedMaximumFps { get; private set; }
        public double? AverageFps { get; private set; }
        public double? OnePercentLowFps { get; private set; }
        public double? ZeroPointOnePercentLowFps { get; private set; }
        public double? AverageFrameTimeMs { get; private set; }
        public double? AverageCpuBusyMs { get; private set; }
        public double? AverageGpuTimeMs { get; private set; }
        public double? AverageDisplayLatencyMs { get; private set; }
        public double? LastFps { get; private set; }
        public double DurationSeconds => elapsedSeconds;
        public bool IsPartial { get; private set; }
        public long? FailureRow { get; private set; }
        public FrameTimeAxisSource TimeAxisSource { get; private set; }
        public GameFrameQualityDiagnostics Diagnostics { get; private set; } = new();

        public void RecordRawRow() => RawRowCount++;

        public void RecordParsedFrame() => ParsedFrameCount++;

        public DateTimeOffset ResolveObservedAt(GameFrameSample sample)
        {
            if (sample.HasExplicitTimestamp) return sample.Timestamp;
            if (sample.CaptureElapsedSeconds is >= 0d && double.IsFinite(sample.CaptureElapsedSeconds.Value))
            {
                return captureStartedAt.AddSeconds(sample.CaptureElapsedSeconds.Value);
            }
            return captureStartedAt.AddTicks(Math.Max(1L, RawRowCount));
        }

        public void Add(GameFrameSample sample)
        {
            if (!sample.FrameTimeMs.HasValue || sample.FrameTimeMs.Value <= 0d) return;
            accumulatedFrameSeconds += sample.FrameTimeMs.Value / 1000d;
            double candidate;
            FrameTimeAxisSource source;
            if (sample.CaptureElapsedSeconds is >= 0d && double.IsFinite(sample.CaptureElapsedSeconds.Value))
            {
                candidate = sample.CaptureElapsedSeconds.Value;
                source = FrameTimeAxisSource.NativeTimestamp;
            }
            else if (sample.HasExplicitTimestamp)
            {
                candidate = (sample.Timestamp - captureStartedAt).TotalSeconds;
                source = FrameTimeAxisSource.WallClockTimestamp;
            }
            else
            {
                candidate = accumulatedFrameSeconds;
                source = FrameTimeAxisSource.AccumulatedFrameTimeFallback;
            }

            if (!double.IsFinite(candidate)) candidate = accumulatedFrameSeconds;
            double tolerance = Math.Max(2d, maximumDurationSeconds * 0.02d);
            if (candidate < -tolerance
                || maximumDurationSeconds > 0d && candidate > maximumDurationSeconds + tolerance)
            {
                if (!warnedOutOfRange)
                {
                    warnings.Add("帧时间戳超出会话边界，已钳制到有效范围");
                    warnedOutOfRange = true;
                }

                candidate = Math.Clamp(candidate, 0d, Math.Max(elapsedSeconds, maximumDurationSeconds));
            }

            if (candidate < elapsedSeconds)
            {
                if (!warnedNonMonotonic)
                {
                    warnings.Add("帧时间戳非递增，已保持横轴单调");
                    warnedNonMonotonic = true;
                }

                candidate = elapsedSeconds;
            }

            elapsedSeconds = Math.Max(0d, candidate);
            if (source < TimeAxisSource || TimeAxisSource == FrameTimeAxisSource.None)
            {
                TimeAxisSource = source;
            }
            FrameCount++;
            double fps = 1000d / sample.FrameTimeMs.Value;
            MinimumFps = !MinimumFps.HasValue ? fps : Math.Min(MinimumFps.Value, fps);
            LastFps = fps;
            frameTimeSum += sample.FrameTimeMs.Value;
            frameTimeHistogram.Add(sample.FrameTimeMs.Value);
            Accumulate(sample.CpuBusyMs, ref cpuBusySum, ref cpuBusyCount);
            Accumulate(sample.GpuTimeMs, ref gpuTimeSum, ref gpuTimeCount);
            Accumulate(sample.DisplayLatencyMs, ref displayLatencySum, ref displayLatencyCount);
            bucket.Fps.Add(elapsedSeconds, fps);
            bucket.FrameTime.Add(elapsedSeconds, sample.FrameTimeMs.Value);
            bucket.CpuBusy.Add(elapsedSeconds, sample.CpuBusyMs);
            bucket.GpuTime.Add(elapsedSeconds, sample.GpuTimeMs);
            bucket.DisplayLatency.Add(elapsedSeconds, sample.DisplayLatencyMs);
            if (++rowsInBucket >= bucketSize) Flush();
        }

        public void MarkPartial(Exception exception)
        {
            IsPartial = true;
            FailureRow = RawRowCount + 2L;
        }

        public void Complete(GameFrameQualityDiagnostics? diagnostics = null)
        {
            if (completed) return;
            completed = true;
            if (rowsInBucket > 0) Flush();
            Trim(Fps); Trim(FrameTime); Trim(CpuBusy); Trim(GpuTime); Trim(DisplayLatency);
            if (diagnostics is not null)
            {
                Diagnostics = diagnostics;
                RawMaximumFps = diagnostics.RawMaximumFps;
                SustainedMaximumFps = diagnostics.SustainedMaximumFps;
                if (diagnostics.UsedCompatibilityFallback)
                {
                    warnings.Add("历史逐帧文件缺少可靠时间戳或 SwapChain，已使用兼容降级校验");
                }
                if (diagnostics.FrameTimeOutlierSampleCount > 0)
                {
                    warnings.Add($"历史逐帧校验已过滤 {diagnostics.FrameTimeOutlierSampleCount} 个 FrameTime 离群样本");
                }
                if (diagnostics.InvalidTimestampSampleCount > 0)
                {
                    warnings.Add($"历史逐帧校验已过滤 {diagnostics.InvalidTimestampSampleCount} 个重复或倒退时间戳样本");
                }
            }
            AverageFrameTimeMs = Average(frameTimeSum, FrameCount);
            AverageFps = FrameCount > 0 && AverageFrameTimeMs is > 0d
                ? 1000d / AverageFrameTimeMs.Value
                : null;
            AverageCpuBusyMs = Average(cpuBusySum, cpuBusyCount);
            AverageGpuTimeMs = Average(gpuTimeSum, gpuTimeCount);
            AverageDisplayLatencyMs = Average(displayLatencySum, displayLatencyCount);
            OnePercentLowFps = frameTimeHistogram.SlowestMeanToFps(0.01d, 100L);
            ZeroPointOnePercentLowFps = frameTimeHistogram.SlowestMeanToFps(0.001d, 1000L);
        }

        private void Flush()
        {
            bucket.Fps.FlushTo(Fps);
            bucket.FrameTime.FlushTo(FrameTime);
            bucket.CpuBusy.FlushTo(CpuBusy);
            bucket.GpuTime.FlushTo(GpuTime);
            bucket.DisplayLatency.FlushTo(DisplayLatency);
            rowsInBucket = 0;
        }

        private static void Trim(List<SessionChartPoint> points)
        {
            if (points.Count <= SessionChartDownsampler.DefaultMaximumPoints) return;
            IReadOnlyList<SessionChartPoint> sampled = SessionChartDownsampler.Downsample(points, []);
            points.Clear();
            points.AddRange(sampled);
        }

        private static void Accumulate(double? value, ref double sum, ref long count)
        {
            if (!value.HasValue || !double.IsFinite(value.Value) || value.Value < 0d) return;
            sum += value.Value;
            count++;
        }

        private static double? Average(double sum, long count) => count > 0 ? sum / count : null;
    }

    private sealed class FrameTimeHistogram
    {
        private const int BinCount = 2048;
        private const double MinimumFrameTimeMs = 0.0001d;
        private const double MaximumFrameTimeMs = 60_000d;
        private static readonly double LogMinimum = Math.Log(MinimumFrameTimeMs);
        private static readonly double LogRange = Math.Log(MaximumFrameTimeMs) - LogMinimum;
        private readonly long[] counts = new long[BinCount];
        private readonly double[] sums = new double[BinCount];
        private long totalCount;

        public void Add(double frameTimeMs)
        {
            double clamped = Math.Clamp(frameTimeMs, MinimumFrameTimeMs, MaximumFrameTimeMs);
            int index = (int)((Math.Log(clamped) - LogMinimum) / LogRange * (BinCount - 1));
            index = Math.Clamp(index, 0, BinCount - 1);
            counts[index]++;
            sums[index] += frameTimeMs;
            totalCount++;
        }

        public double? SlowestMeanToFps(double fraction, long minimumSamples)
        {
            if (totalCount < minimumSamples) return null;
            long target = Math.Max(1L, (long)Math.Ceiling(totalCount * fraction));
            long selected = 0L;
            double selectedSum = 0d;
            for (int index = BinCount - 1; index >= 0 && selected < target; index--)
            {
                long available = counts[index];
                if (available == 0L) continue;
                long take = Math.Min(available, target - selected);
                selectedSum += sums[index] / available * take;
                selected += take;
            }
            return selected > 0L && selectedSum > 0d
                ? 1000d / (selectedSum / selected)
                : null;
        }
    }

    private sealed class FrameBucket
    {
        public MetricBucket Fps { get; } = new();
        public MetricBucket FrameTime { get; } = new();
        public MetricBucket CpuBusy { get; } = new();
        public MetricBucket GpuTime { get; } = new();
        public MetricBucket DisplayLatency { get; } = new();
    }

    private sealed class MetricBucket
    {
        private SessionChartPoint minimum;
        private SessionChartPoint maximum;
        private double elapsedSum;
        private double valueSum;
        private int count;

        public void Add(double elapsed, double? value)
        {
            if (!value.HasValue || !double.IsFinite(value.Value)) return;
            SessionChartPoint point = new(elapsed, value.Value);
            if (count == 0 || point.Value < minimum.Value) minimum = point;
            if (count == 0 || point.Value > maximum.Value) maximum = point;
            elapsedSum += elapsed;
            valueSum += point.Value;
            count++;
        }

        public void FlushTo(List<SessionChartPoint> target)
        {
            if (count == 0) return;
            target.Add(minimum);
            if (!maximum.Equals(minimum)) target.Add(maximum);
            if (count > 2) target.Add(new SessionChartPoint(elapsedSum / count, valueSum / count));
            count = 0;
            elapsedSum = valueSum = 0d;
        }
    }

    private sealed class TimelineReadResult(bool isPresent, bool isValid)
    {
        private readonly Dictionary<string, TimelineDeviceData> devices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TimelineDeviceData> order = [];
        public static TimelineReadResult NotPresent { get; } = new(false, true);
        public bool IsPresent { get; } = isPresent;
        public bool IsValid { get; set; } = isValid;
        public long RowCount { get; private set; }
        public double DurationSeconds { get; private set; }

        public void Add(GameHardwareTimelineSample sample)
        {
            if (!devices.TryGetValue(sample.DeviceId, out TimelineDeviceData? device))
            {
                device = new TimelineDeviceData(sample.DeviceId, sample.DeviceName, sample.DeviceType);
                devices.Add(sample.DeviceId, device);
                order.Add(device);
            }
            device.Add(sample);
            RowCount++;
            DurationSeconds = Math.Max(DurationSeconds, sample.ElapsedSeconds);
        }

        public TimelineDeviceData? FindFirst(GameTimelineDeviceType type)
        {
            for (int index = 0; index < order.Count; index++) if (order[index].DeviceType == type) return order[index];
            return null;
        }

        public List<TimelineDeviceData> FindAll(GameTimelineDeviceType type)
        {
            List<TimelineDeviceData> result = [];
            for (int index = 0; index < order.Count; index++) if (order[index].DeviceType == type) result.Add(order[index]);
            return result;
        }
    }

    private sealed class TimelineDeviceData(string deviceId, string deviceName, GameTimelineDeviceType deviceType)
    {
        public string DeviceId { get; } = deviceId;
        public string DeviceName { get; } = deviceName;
        public GameTimelineDeviceType DeviceType { get; } = deviceType;
        public List<SessionChartPoint> CpuAverageClock { get; } = [];
        public List<SessionChartPoint> CpuEffectiveClock { get; } = [];
        public List<SessionChartPoint> CpuMaximumClock { get; } = [];
        public List<SessionChartPoint> CpuTemperature { get; } = [];
        public List<SessionChartPoint> CpuPower { get; } = [];
        public List<SessionChartPoint> CpuLoad { get; } = [];
        public List<SessionChartPoint> GpuCoreClock { get; } = [];
        public List<SessionChartPoint> GpuMemoryClock { get; } = [];
        public List<SessionChartPoint> GpuTemperature { get; } = [];
        public List<SessionChartPoint> GpuHotSpotTemperature { get; } = [];
        public List<SessionChartPoint> GpuPower { get; } = [];
        public List<SessionChartPoint> GpuLoad { get; } = [];

        public void Add(GameHardwareTimelineSample sample)
        {
            Add(CpuAverageClock, sample.ElapsedSeconds, sample.CpuAverageCoreClockMHz);
            Add(CpuEffectiveClock, sample.ElapsedSeconds, sample.CpuEffectiveClockMHz);
            Add(CpuMaximumClock, sample.ElapsedSeconds, sample.CpuMaximumCoreClockMHz);
            Add(CpuTemperature, sample.ElapsedSeconds, sample.CpuTemperatureCelsius);
            Add(CpuPower, sample.ElapsedSeconds, sample.CpuPackagePowerWatts);
            Add(CpuLoad, sample.ElapsedSeconds, sample.CpuLoadPercent);
            Add(GpuCoreClock, sample.ElapsedSeconds, sample.GpuCoreClockMHz);
            Add(GpuMemoryClock, sample.ElapsedSeconds, sample.GpuMemoryClockMHz);
            Add(GpuTemperature, sample.ElapsedSeconds, sample.GpuTemperatureCelsius);
            Add(GpuHotSpotTemperature, sample.ElapsedSeconds, sample.GpuHotSpotTemperatureCelsius);
            Add(GpuPower, sample.ElapsedSeconds, sample.GpuBoardPowerWatts);
            Add(GpuLoad, sample.ElapsedSeconds, sample.GpuLoadPercent);
        }

        private static void Add(List<SessionChartPoint> target, double elapsed, double? value)
        {
            if (value.HasValue && double.IsFinite(value.Value)) target.Add(new SessionChartPoint(elapsed, value.Value));
        }
    }
}
