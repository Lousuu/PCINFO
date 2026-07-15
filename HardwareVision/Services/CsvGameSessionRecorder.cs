using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class CsvGameSessionRecorder : IGameSessionRecorder
{
    private const int DefaultChannelCapacity = 8_192;
    private static readonly JsonSerializerOptions SummaryJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object stateLock = new();
    private readonly int channelCapacity;
    private readonly IGameEnergyTracker? energyTracker;
    private readonly IGamePerformanceLimitTracker? performanceLimitTracker;
    private readonly GameHardwareTimelineRecorder? hardwareTimelineRecorder;
    private ActiveSession? activeSession;
    private Task? recoveryTask;
    private bool isDisposed;

    public CsvGameSessionRecorder(
        string? rootDirectory = null,
        IGameEnergyTracker? energyTracker = null,
        IGamePerformanceLimitTracker? performanceLimitTracker = null,
        PollingService? pollingService = null)
        : this(
            rootDirectory,
            DefaultChannelCapacity,
            energyTracker,
            performanceLimitTracker,
            pollingService is null ? null : new GameHardwareTimelineRecorder(pollingService, performanceLimitTracker))
    {
    }

    internal CsvGameSessionRecorder(string? rootDirectory, int channelCapacity)
        : this(rootDirectory, channelCapacity, energyTracker: null, performanceLimitTracker: null, hardwareTimelineRecorder: null)
    {
    }

    internal CsvGameSessionRecorder(string? rootDirectory, int channelCapacity, IGameEnergyTracker? energyTracker)
        : this(rootDirectory, channelCapacity, energyTracker, performanceLimitTracker: null, hardwareTimelineRecorder: null)
    {
    }

    internal CsvGameSessionRecorder(
        string? rootDirectory,
        int channelCapacity,
        IGameEnergyTracker? energyTracker,
        IGamePerformanceLimitTracker? performanceLimitTracker,
        GameHardwareTimelineRecorder? hardwareTimelineRecorder = null)
    {
        RootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HardwareVision", "GameSessions")
            : rootDirectory);
        this.channelCapacity = Math.Max(1, channelCapacity);
        this.energyTracker = energyTracker;
        this.performanceLimitTracker = performanceLimitTracker;
        this.hardwareTimelineRecorder = hardwareTimelineRecorder;
    }

    public event EventHandler<GameSessionRecorderStateChangedEventArgs>? StateChanged;

    public string RootDirectory { get; }

    public bool IsRecording
    {
        get
        {
            lock (stateLock)
            {
                return activeSession is not null;
            }
        }
    }

    public string RecordingStatusText
    {
        get
        {
            lock (stateLock)
            {
                return activeSession is null ? "等待游戏会话" : "正在自动记录";
            }
        }
    }

    public string? CurrentFilePath
    {
        get
        {
            lock (stateLock)
            {
                return activeSession?.PartialPath;
            }
        }
    }

    public long DroppedSampleCount
    {
        get
        {
            lock (stateLock)
            {
                return activeSession is null ? 0L : Interlocked.Read(ref activeSession.DroppedSamples);
            }
        }
    }

    public Task RecoverIncompleteSessionsAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            task = recoveryTask ??= Task.Run(RecoverIncompleteSessionsCoreAsync);
        }

        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    private async Task RecoverIncompleteSessionsCoreAsync()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return;
        }

        await GameHardwareTimelineRecorder.RecoverIncompleteAsync(RootDirectory).ConfigureAwait(false);

        string[] partialPaths;
        try
        {
            partialPaths = Directory.GetFiles(RootDirectory, "*.csv.partial", SearchOption.AllDirectories);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Incomplete game session scan failed.", exception,
                $"game-recorder-recovery-scan:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
            return;
        }

        foreach (string partialPath in partialPaths)
        {
            try
            {
                bool hasData = await HasDataRowAsync(partialPath, CancellationToken.None).ConfigureAwait(false);
                if (!hasData)
                {
                    TryDelete(partialPath);
                    continue;
                }

                string incompletePath = partialPath[..^".partial".Length] + ".incomplete";
                incompletePath = AllocateRecoveryPath(incompletePath);
                File.Move(partialPath, incompletePath);
                AppLogger.LogKeyEvent($"Recovered incomplete game session | path={incompletePath}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.LogError(
                    $"Incomplete game session could not be recovered; data was left in place | path={partialPath}",
                    exception,
                    $"game-recorder-recovery:{Path.GetFileName(partialPath)}",
                    TimeSpan.FromMinutes(5));
            }
        }
    }

    public async Task StartAsync(GameSessionStartInfo startInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ObjectDisposedException.ThrowIf(isDisposed, this);
        Task? recovery;
        lock (stateLock)
        {
            recovery = recoveryTask;
        }

        if (recovery is not null)
        {
            await recovery.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (IsRecording)
        {
            await CompleteAsync(GameSessionEndReason.RecorderFailed, completedNormally: false, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string monthDirectory = Path.Combine(RootDirectory, startInfo.CaptureStartedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(monthDirectory);
        string gameName = GameSessionFileNaming.Sanitize(startInfo.ProcessName);
        string baseName = $"{gameName}-{startInfo.CaptureStartedAt:yyyyMMdd-HHmmss}-{startInfo.ProcessId}";
        string finalPath = GameSessionFileNaming.CreateUniquePath(monthDirectory, baseName, ".csv");
        Channel<GameFrameSample> channel = Channel.CreateBounded<GameFrameSample>(new BoundedChannelOptions(channelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        ActiveSession session = new(startInfo, channel, finalPath);
        session.WriterTask = WriteSessionAsync(session);
        try
        {
            hardwareTimelineRecorder?.StartSession(startInfo, finalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLogger.LogError(
                $"Hardware timeline could not start | session={startInfo.CaptureSessionId:N}",
                exception,
                $"hardware-timeline-start:{startInfo.CaptureSessionId:N}",
                TimeSpan.FromMinutes(1));
        }

        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            activeSession = session;
        }

        RaiseStateChanged(new GameSessionRecorderStateChangedEventArgs(
            "正在自动记录游戏会话",
            isRecording: true,
            session.PartialPath,
            droppedSamples: 0));
    }

    public bool TryRecord(GameFrameSample sample, Guid captureSessionId, int generation)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ActiveSession? session;
        lock (stateLock)
        {
            session = activeSession;
            if (session is null
                || session.StartInfo.CaptureSessionId != captureSessionId
                || session.StartInfo.Generation != generation)
            {
                return false;
            }
        }

        Interlocked.Increment(ref session.ReceivedSamples);
        if (session.Channel.Writer.TryWrite(sample))
        {
            return true;
        }

        long dropped = Interlocked.Increment(ref session.DroppedSamples);
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref session.LastDropLogTick);
        if ((previous == 0L || now - previous >= 60_000L)
            && Interlocked.CompareExchange(ref session.LastDropLogTick, now, previous) == previous)
        {
            AppLogger.LogError(
                $"Game session recorder queue full | session={captureSessionId:N}; dropped={dropped}",
                null,
                $"game-recorder-drop:{captureSessionId:N}",
                TimeSpan.FromMinutes(1));
        }

        return false;
    }

    public async Task<GameSessionRecordInfo?> CompleteAsync(
        GameSessionEndReason reason,
        bool completedNormally,
        CancellationToken cancellationToken = default)
    {
        ActiveSession? session;
        lock (stateLock)
        {
            session = activeSession;
            activeSession = null;
        }

        if (session is null)
        {
            return null;
        }

        session.Channel.Writer.TryComplete();
        try
        {
            await session.WriterTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            session.Failure = exception;
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset endedAt = DateTimeOffset.Now;
        GameHardwareTimelineResult? timelineResult = hardwareTimelineRecorder is null
            ? null
            : await hardwareTimelineRecorder.CompleteSessionAsync(
                session.StartInfo.CaptureSessionId,
                session.StartInfo.Generation,
                cancellationToken).ConfigureAwait(false);
        long written = Interlocked.Read(ref session.WrittenSamples);
        if (written == 0L)
        {
            TryDelete(session.PartialPath);
            if (!string.IsNullOrWhiteSpace(timelineResult?.FilePath)) TryDelete(timelineResult.FilePath);
            RaiseStateChanged(new GameSessionRecorderStateChangedEventArgs(
                "会话结束，未产生可记录帧",
                isRecording: false,
                currentPath: null,
                Interlocked.Read(ref session.DroppedSamples)));
            return null;
        }

        bool finalized = session.Failure is null;
        string csvPath = session.PartialPath;
        string? summaryPath = null;
        GameSessionSummary? summary = null;
        if (finalized)
        {
            File.Move(session.PartialPath, session.FinalPath);
            csvPath = session.FinalPath;
            summary = await CreateSummaryAsync(
                session,
                csvPath,
                endedAt,
                reason,
                completedNormally,
                timelineResult,
                cancellationToken).ConfigureAwait(false);
            summaryPath = Path.ChangeExtension(csvPath, ".summary.json");
            await WriteSummaryAtomicallyAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            reason = GameSessionEndReason.RecorderFailed;
            completedNormally = false;
            AppLogger.LogError(
                $"Game session recording failed; partial data preserved | path={session.PartialPath}",
                session.Failure,
                $"game-recorder-write:{session.StartInfo.CaptureSessionId:N}",
                TimeSpan.FromMinutes(1));
        }

        FileInfo file = new(csvPath);
        GameSessionRecordInfo record = new()
        {
            GameName = session.StartInfo.ProcessName,
            StartedAt = session.StartInfo.CaptureStartedAt,
            Duration = endedAt - session.StartInfo.CaptureStartedAt,
            FileSize = file.Exists ? file.Length : 0L,
            IsComplete = finalized,
            CsvPath = csvPath,
            SummaryPath = summaryPath,
            PerformanceLimitCsvPath = summary?.PerformanceLimitCsvFileName is string limitFileName
                ? Path.Combine(Path.GetDirectoryName(csvPath)!, limitFileName)
                : null,
            HardwareTimelineCsvPath = summary?.HardwareTimelineCsvFileName is string timelineFileName
                ? Path.Combine(Path.GetDirectoryName(csvPath)!, timelineFileName)
                : null,
            EndReason = reason,
            EstimatedEnergyWh = summary?.EstimatedEnergyWh,
            AverageEstimatedPowerWatts = summary?.AverageEstimatedPowerWatts,
            EnergyCoveragePercent = summary?.EnergyCoveragePercent,
            EnergyIncludedComponents = summary?.EnergyIncludedComponents,
            CpuPerformanceLimitEventCount = summary?.CpuPerformanceLimitEventCount,
            GpuPerformanceLimitEventCount = summary?.GpuPerformanceLimitEventCount,
            CpuPerformanceLimitSupportStatus = summary?.CpuPerformanceLimitSupportStatus,
            GpuPerformanceLimitSupportStatus = summary?.GpuPerformanceLimitSupportStatus
        };
        RaiseStateChanged(new GameSessionRecorderStateChangedEventArgs(
            finalized ? "游戏会话记录已保存" : "记录失败，已保留部分数据",
            isRecording: false,
            csvPath,
            Interlocked.Read(ref session.DroppedSamples),
            record));
        return record;
    }

    public async Task<IReadOnlyList<GameSessionRecordInfo>> GetRecentRecordsAsync(
        int maximumCount = 10,
        CancellationToken cancellationToken = default)
    {
        Task? recovery;
        lock (stateLock)
        {
            recovery = recoveryTask;
        }

        if (recovery is not null)
        {
            await recovery.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (maximumCount <= 0 || !Directory.Exists(RootDirectory))
        {
            return Array.Empty<GameSessionRecordInfo>();
        }

        List<GameSessionRecordInfo> records = new();
        foreach (string summaryPath in Directory.EnumerateFiles(RootDirectory, "*.summary.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = File.OpenRead(summaryPath);
                GameSessionSummary? summary = await JsonSerializer
                    .DeserializeAsync<GameSessionSummary>(stream, SummaryJsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (summary is null)
                {
                    continue;
                }

                string csvPath = Path.Combine(Path.GetDirectoryName(summaryPath)!, summary.CsvFileName);
                records.Add(new GameSessionRecordInfo
                {
                    GameName = summary.ProcessName,
                    StartedAt = summary.CaptureStartedAt,
                    Duration = summary.Duration,
                    FileSize = File.Exists(csvPath) ? new FileInfo(csvPath).Length : summary.CsvFileSize,
                    IsComplete = true,
                    CsvPath = csvPath,
                    SummaryPath = summaryPath,
                    PerformanceLimitCsvPath = string.IsNullOrWhiteSpace(summary.PerformanceLimitCsvFileName)
                        ? null
                        : Path.Combine(Path.GetDirectoryName(summaryPath)!, summary.PerformanceLimitCsvFileName),
                    HardwareTimelineCsvPath = string.IsNullOrWhiteSpace(summary.HardwareTimelineCsvFileName)
                        ? null
                        : Path.Combine(Path.GetDirectoryName(summaryPath)!, summary.HardwareTimelineCsvFileName),
                    EndReason = summary.EndReason,
                    EstimatedEnergyWh = summary.EstimatedEnergyWh,
                    AverageEstimatedPowerWatts = summary.AverageEstimatedPowerWatts,
                    EnergyCoveragePercent = summary.EnergyCoveragePercent,
                    EnergyIncludedComponents = summary.EnergyIncludedComponents,
                    CpuPerformanceLimitEventCount = summary.CpuPerformanceLimitEventCount,
                    GpuPerformanceLimitEventCount = summary.GpuPerformanceLimitEventCount,
                    CpuPerformanceLimitSupportStatus = summary.CpuPerformanceLimitSupportStatus,
                    GpuPerformanceLimitSupportStatus = summary.GpuPerformanceLimitSupportStatus
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLogger.LogError("Game session summary could not be read.", exception,
                    $"game-summary-read:{Path.GetFileName(summaryPath)}", TimeSpan.FromMinutes(5));
            }
        }

        foreach (string incompletePath in Directory.EnumerateFiles(RootDirectory, "*.csv.incomplete", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo file = new(incompletePath);
            records.Add(new GameSessionRecordInfo
            {
                GameName = ParseGameName(file.Name),
                StartedAt = file.CreationTime,
                Duration = TimeSpan.Zero,
                FileSize = file.Length,
                IsComplete = false,
                CsvPath = incompletePath,
                EndReason = GameSessionEndReason.ApplicationShutdown
            });
        }

        return records
            .OrderByDescending(record => record.StartedAt)
            .Take(maximumCount)
            .ToArray();
    }

    public async Task<long> GetDirectorySizeAsync(CancellationToken cancellationToken = default)
    {
        Task? recovery;
        lock (stateLock)
        {
            recovery = recoveryTask;
        }

        if (recovery is not null)
        {
            await recovery.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await Task.Run(() =>
        {
            if (!Directory.Exists(RootDirectory))
            {
                return 0L;
            }

            long size = 0L;
            foreach (string path in Directory.EnumerateFiles(RootDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    size += new FileInfo(path).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            return size;
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Task? recovery;
        lock (stateLock)
        {
            recovery = recoveryTask;
        }
        recovery?.GetAwaiter().GetResult();
        CompleteAsync(GameSessionEndReason.ApplicationShutdown, completedNormally: false)
            .GetAwaiter()
            .GetResult();
        hardwareTimelineRecorder?.Dispose();
        isDisposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        Task? recovery;
        lock (stateLock)
        {
            recovery = recoveryTask;
        }
        if (recovery is not null)
        {
            await recovery.ConfigureAwait(false);
        }
        await CompleteAsync(GameSessionEndReason.ApplicationShutdown, completedNormally: false).ConfigureAwait(false);
        if (hardwareTimelineRecorder is not null)
        {
            await hardwareTimelineRecorder.DisposeAsync().ConfigureAwait(false);
        }
        isDisposed = true;
    }

    private static async Task WriteSessionAsync(ActiveSession session)
    {
        StreamWriter? writer = null;
        try
        {
            await foreach (GameFrameSample sample in session.Channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                writer ??= CreateWriter(session.PartialPath);
                await writer.WriteLineAsync(GameCsvFormatting.FormatSample(sample)).ConfigureAwait(false);
                long written = Interlocked.Increment(ref session.WrittenSamples);
                Accumulate(session, sample);
                if (written % 256L == 0L)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }

            if (writer is not null)
            {
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            session.Failure = exception;
            session.Channel.Writer.TryComplete(exception);
        }
        finally
        {
            if (writer is not null)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static StreamWriter CreateWriter(string partialPath)
    {
        FileStream stream = new(
            partialPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024);
        writer.WriteLine(GameCsvFormatting.Header);
        return writer;
    }

    private static void Accumulate(ActiveSession session, GameFrameSample sample)
    {
        Add(sample.Fps, ref session.FpsSum, ref session.FpsCount);
        Add(sample.FrameTimeMs, ref session.FrameTimeSum, ref session.FrameTimeCount);
        Add(sample.CpuBusyMs, ref session.CpuBusySum, ref session.CpuBusyCount);
        Add(sample.GpuTimeMs, ref session.GpuTimeSum, ref session.GpuTimeCount);
        Add(sample.DisplayLatencyMs, ref session.DisplayLatencySum, ref session.DisplayLatencyCount);
    }

    private static void Add(double? value, ref double sum, ref long count)
    {
        if (value.HasValue && double.IsFinite(value.Value))
        {
            sum += value.Value;
            count++;
        }
    }

    private async Task<GameSessionSummary> CreateSummaryAsync(
        ActiveSession session,
        string csvPath,
        DateTimeOffset endedAt,
        GameSessionEndReason reason,
        bool completedNormally,
        GameHardwareTimelineResult? timelineResult,
        CancellationToken cancellationToken)
    {
        (double? onePercentLow, double? zeroPointOnePercentLow) = await CalculateLowFpsAsync(
            csvPath,
            session.FrameTimeCount,
            cancellationToken).ConfigureAwait(false);
        FileInfo file = new(csvPath);
        GameEnergySnapshot energy = energyTracker?.CurrentSnapshot ?? GameEnergySnapshot.Empty;
        bool energyMatches = energy.CaptureSessionId == session.StartInfo.CaptureSessionId
            && energy.Generation == session.StartInfo.Generation;
        GamePerformanceLimitSnapshot limits = performanceLimitTracker?.CurrentSnapshot
            ?? GamePerformanceLimitSnapshot.Empty;
        bool limitsMatch = limits.CaptureSessionId == session.StartInfo.CaptureSessionId
            && limits.Generation == session.StartInfo.Generation;
        int cpuLimitCount = 0;
        int gpuLimitCount = 0;
        double cpuLimitDuration = 0d;
        double gpuLimitDuration = 0d;
        if (limitsMatch)
        {
            for (int index = 0; index < limits.Events.Count; index++)
            {
                GamePerformanceLimitEvent item = limits.Events[index];
                if (item.ProcessorType == PerformanceLimitProcessorType.Cpu)
                {
                    cpuLimitCount++;
                    cpuLimitDuration += item.Duration.TotalSeconds;
                }
                else
                {
                    gpuLimitCount++;
                    gpuLimitDuration += item.Duration.TotalSeconds;
                }
            }
        }
        string? performanceLimitCsvFileName = null;
        if (limitsMatch)
        {
            string limitPath = GamePerformanceLimitCsv.GetPath(csvPath);
            try
            {
                await GamePerformanceLimitCsv.WriteAsync(
                    limitPath,
                    limits,
                    session.StartInfo.CaptureStartedAt,
                    cancellationToken).ConfigureAwait(false);
                performanceLimitCsvFileName = Path.GetFileName(limitPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.LogError(
                    $"Performance-limit CSV could not be written | path={limitPath}",
                    exception,
                    $"performance-limit-csv:{session.StartInfo.CaptureSessionId:N}",
                    TimeSpan.FromMinutes(1));
            }
        }

        bool timelineMatches = timelineResult is not null
            && timelineResult.CaptureSessionId == session.StartInfo.CaptureSessionId
            && timelineResult.CaptureGeneration == session.StartInfo.Generation;
        return new GameSessionSummary
        {
            HardwareVisionVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            PresentMonVersion = "2.5.1",
            CaptureSessionId = session.StartInfo.CaptureSessionId,
            CaptureGeneration = session.StartInfo.Generation,
            ProcessId = session.StartInfo.ProcessId,
            ProcessName = session.StartInfo.ProcessName,
            WindowTitle = session.StartInfo.WindowTitle,
            ExecutablePath = session.StartInfo.ExecutablePath,
            CaptureStartedAt = session.StartInfo.CaptureStartedAt,
            CaptureEndedAt = endedAt,
            Duration = endedAt - session.StartInfo.CaptureStartedAt,
            ReceivedSampleCount = Interlocked.Read(ref session.ReceivedSamples),
            WrittenSampleCount = Interlocked.Read(ref session.WrittenSamples),
            DroppedRecordSampleCount = Interlocked.Read(ref session.DroppedSamples),
            AverageFps = FpsFromFrameTime(session.FrameTimeSum, session.FrameTimeCount),
            OnePercentLowFps = onePercentLow,
            ZeroPointOnePercentLowFps = zeroPointOnePercentLow,
            AverageFrameTimeMs = Average(session.FrameTimeSum, session.FrameTimeCount),
            AverageCpuBusyMs = Average(session.CpuBusySum, session.CpuBusyCount),
            AverageGpuTimeMs = Average(session.GpuTimeSum, session.GpuTimeCount),
            AverageDisplayLatencyMs = Average(session.DisplayLatencySum, session.DisplayLatencyCount),
            EstimatedEnergyWh = energyMatches ? energy.EstimatedEnergyWh : null,
            CpuEstimatedEnergyWh = energyMatches ? energy.CpuEstimatedEnergyWh : null,
            GpuEstimatedEnergyWh = energyMatches ? energy.GpuEstimatedEnergyWh : null,
            AverageEstimatedPowerWatts = energyMatches ? energy.AverageEstimatedPowerWatts : null,
            CpuAverageEstimatedPowerWatts = energyMatches ? energy.CpuAverageEstimatedPowerWatts : null,
            GpuAverageEstimatedPowerWatts = energyMatches ? energy.GpuAverageEstimatedPowerWatts : null,
            EnergyCoveragePercent = energyMatches ? energy.CoveragePercent : null,
            EnergyValidIntegrationDuration = energyMatches ? energy.ValidIntegrationDuration : null,
            EnergyIncludedComponents = energyMatches ? energy.IncludedComponents : null,
            AverageCpuLoadPercent = energyMatches ? energy.AverageCpuLoadPercent : null,
            AverageCpuTemperatureCelsius = energyMatches ? energy.AverageCpuTemperatureCelsius : null,
            AverageGpuLoadPercent = energyMatches ? energy.AverageGpuLoadPercent : null,
            AverageGpuTemperatureCelsius = energyMatches ? energy.AverageGpuTemperatureCelsius : null,
            AverageMemoryLoadPercent = energyMatches ? energy.AverageMemoryLoadPercent : null,
            HardwareMetadata = session.StartInfo.HardwareMetadata,
            CpuPerformanceLimitEventCount = limitsMatch ? cpuLimitCount : null,
            GpuPerformanceLimitEventCount = limitsMatch ? gpuLimitCount : null,
            CpuPerformanceLimitDurationSeconds = limitsMatch ? cpuLimitDuration : null,
            GpuPerformanceLimitDurationSeconds = limitsMatch ? gpuLimitDuration : null,
            PerformanceLimitEvents = limitsMatch ? limits.Events : null,
            PerformanceLimitEventsTruncated = limitsMatch ? limits.EventsTruncated : null,
            CpuPerformanceLimitSupportStatus = limitsMatch ? limits.CpuSupportStatus : null,
            GpuPerformanceLimitSupportStatus = limitsMatch ? limits.GpuSupportStatus : null,
            CompletedNormally = completedNormally,
            EndReason = reason,
            CsvFileName = file.Name,
            PerformanceLimitCsvFileName = performanceLimitCsvFileName,
            HardwareTimelineCsvFileName = timelineMatches && timelineResult!.CompletedSuccessfully
                ? Path.GetFileName(timelineResult.FilePath)
                : null,
            TimelineWrittenSampleCount = timelineMatches ? timelineResult!.WrittenSampleCount : null,
            TimelineDroppedSampleCount = timelineMatches ? timelineResult!.DroppedSampleCount : null,
            CsvFileSize = file.Length
        };
    }

    private static async Task<(double? OnePercentLow, double? ZeroPointOnePercentLow)> CalculateLowFpsAsync(
        string csvPath,
        long frameTimeCount,
        CancellationToken cancellationToken)
    {
        if (frameTimeCount < 100L)
        {
            return (null, null);
        }

        int onePercentCount = Math.Max(1, (int)Math.Ceiling(frameTimeCount * 0.01d));
        PriorityQueue<double, double> slowest = new(onePercentCount + 1);
        using StreamReader reader = new(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 64 * 1024);
        _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (!GameCsvFormatting.TryGetDoubleField(line, 6, out double frameTime) || frameTime <= 0d)
            {
                continue;
            }

            slowest.Enqueue(frameTime, frameTime);
            if (slowest.Count > onePercentCount)
            {
                slowest.Dequeue();
            }
        }

        double[] values = slowest.UnorderedItems.Select(item => item.Element).ToArray();
        double? oneLow = FpsFromMeanFrameTime(values);
        if (frameTimeCount < 1_000L)
        {
            return (oneLow, null);
        }

        int zeroPointOneCount = Math.Max(1, (int)Math.Ceiling(frameTimeCount * 0.001d));
        double[] zeroValues = values.OrderByDescending(value => value).Take(zeroPointOneCount).ToArray();
        return (oneLow, FpsFromMeanFrameTime(zeroValues));
    }

    private static double? FpsFromMeanFrameTime(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        double mean = values.Sum() / values.Count;
        return mean > 0d ? 1000d / mean : null;
    }

    private static double? Average(double sum, long count) => count > 0L ? sum / count : null;

    private static double? FpsFromFrameTime(double sum, long count)
    {
        double? mean = Average(sum, count);
        return mean is > 0d ? 1000d / mean.Value : null;
    }

    private static async Task WriteSummaryAtomicallyAsync(
        string summaryPath,
        GameSessionSummary summary,
        CancellationToken cancellationToken)
    {
        string temporaryPath = summaryPath + ".tmp";
        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, summary, SummaryJsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, summaryPath);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task<bool> HasDataRowAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using StreamReader reader = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 4096);
            _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new FileInfo(path).Length > 0L;
        }

        return false;
    }

    private static string AllocateRecoveryPath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = Path.Combine(directory, $"{fileName}-{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to allocate an incomplete session file name.");
    }

    private static string ParseGameName(string fileName)
    {
        int timestampSeparator = fileName.IndexOf('-', StringComparison.Ordinal);
        return timestampSeparator > 0 ? fileName[..timestampSeparator] : "Game";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void RaiseStateChanged(GameSessionRecorderStateChangedEventArgs args)
    {
        StateChanged?.Invoke(this, args);
    }

    private sealed class ActiveSession
    {
        public ActiveSession(GameSessionStartInfo startInfo, Channel<GameFrameSample> channel, string finalPath)
        {
            StartInfo = startInfo;
            Channel = channel;
            FinalPath = finalPath;
            PartialPath = finalPath + ".partial";
        }

        public GameSessionStartInfo StartInfo { get; }
        public Channel<GameFrameSample> Channel { get; }
        public string FinalPath { get; }
        public string PartialPath { get; }
        public Task WriterTask { get; set; } = Task.CompletedTask;
        public Exception? Failure { get; set; }
        public long ReceivedSamples;
        public long WrittenSamples;
        public long DroppedSamples;
        public long LastDropLogTick;
        public double FpsSum;
        public long FpsCount;
        public double FrameTimeSum;
        public long FrameTimeCount;
        public double CpuBusySum;
        public long CpuBusyCount;
        public double GpuTimeSum;
        public long GpuTimeCount;
        public double DisplayLatencySum;
        public long DisplayLatencyCount;
    }
}
