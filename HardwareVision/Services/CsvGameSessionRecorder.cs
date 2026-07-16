using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
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
    private readonly Func<GameSessionFrameStorageMode> frameStorageModeProvider;
    private readonly IGameEnergyTracker? energyTracker;
    private readonly IGamePerformanceLimitTracker? performanceLimitTracker;
    private readonly GameHardwareTimelineRecorder? hardwareTimelineRecorder;
    private readonly GameSessionCatalog catalog;
    private readonly SessionDirectorySizeCache directorySizeCache;
    private ActiveSession? activeSession;
    private Task? recoveryTask;
    private SessionFinalizationResult? lastFinalizationResult;
    private SessionFinalizationState finalizationState = SessionFinalizationState.Idle;
    private bool isDisposed;

    public CsvGameSessionRecorder(
        string? rootDirectory = null,
        IGameEnergyTracker? energyTracker = null,
        IGamePerformanceLimitTracker? performanceLimitTracker = null,
        PollingService? pollingService = null,
        Func<GameSessionFrameStorageMode>? frameStorageModeProvider = null)
        : this(
            rootDirectory,
            DefaultChannelCapacity,
            energyTracker,
            performanceLimitTracker,
            pollingService is null ? null : new GameHardwareTimelineRecorder(pollingService, performanceLimitTracker),
            frameStorageModeProvider ?? (() => GameSessionFrameStorageMode.CompressedCsv))
    {
    }

    internal CsvGameSessionRecorder(string? rootDirectory, int channelCapacity)
        : this(rootDirectory, channelCapacity, energyTracker: null, performanceLimitTracker: null, hardwareTimelineRecorder: null,
            frameStorageModeProvider: () => GameSessionFrameStorageMode.PlainCsv)
    {
    }

    internal CsvGameSessionRecorder(string? rootDirectory, int channelCapacity, IGameEnergyTracker? energyTracker)
        : this(rootDirectory, channelCapacity, energyTracker, performanceLimitTracker: null, hardwareTimelineRecorder: null,
            frameStorageModeProvider: () => GameSessionFrameStorageMode.PlainCsv)
    {
    }

    internal CsvGameSessionRecorder(
        string? rootDirectory,
        int channelCapacity,
        IGameEnergyTracker? energyTracker,
        IGamePerformanceLimitTracker? performanceLimitTracker,
        GameHardwareTimelineRecorder? hardwareTimelineRecorder = null,
        Func<GameSessionFrameStorageMode>? frameStorageModeProvider = null)
    {
        RootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HardwareVision", "GameSessions")
            : rootDirectory);
        this.channelCapacity = Math.Max(1, channelCapacity);
        this.energyTracker = energyTracker;
        this.performanceLimitTracker = performanceLimitTracker;
        this.hardwareTimelineRecorder = hardwareTimelineRecorder;
        this.frameStorageModeProvider = frameStorageModeProvider ?? (() => GameSessionFrameStorageMode.PlainCsv);
        catalog = new GameSessionCatalog(RootDirectory);
        directorySizeCache = new SessionDirectorySizeCache(RootDirectory);
        directorySizeCache.StartInitialScan();
    }

    public event EventHandler<GameSessionRecorderStateChangedEventArgs>? StateChanged;

    public string RootDirectory { get; }

    public bool IsRecording
    {
        get
        {
            lock (stateLock)
            {
                return activeSession?.FinalizationState == SessionFinalizationState.Recording;
            }
        }
    }

    public SessionFinalizationState FinalizationState
    {
        get { lock (stateLock) return finalizationState; }
    }

    public SessionFinalizationResult? LastFinalizationResult
    {
        get { lock (stateLock) return lastFinalizationResult; }
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
            partialPaths = Directory.GetFiles(RootDirectory, "*.csv.partial", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(RootDirectory, "*.csv.gz.partial", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
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

        bool hasActiveSession;
        lock (stateLock)
        {
            hasActiveSession = activeSession is not null;
        }

        if (hasActiveSession)
        {
            await CompleteAsync(GameSessionEndReason.RecorderFailed, completedNormally: false, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string monthDirectory = Path.Combine(RootDirectory, startInfo.CaptureStartedAt.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(monthDirectory);
        string gameName = GameSessionFileNaming.Sanitize(startInfo.ProcessName);
        string baseName = $"{gameName}-{startInfo.CaptureStartedAt:yyyyMMdd-HHmmss}-{startInfo.ProcessId}";
        GameSessionFrameStorageMode storageMode = frameStorageModeProvider();
        if (!Enum.IsDefined(storageMode)) storageMode = GameSessionFrameStorageMode.CompressedCsv;
        string extension = storageMode == GameSessionFrameStorageMode.CompressedCsv ? ".csv.gz" : ".csv";
        string finalPath = GameSessionFileNaming.CreateUniquePath(monthDirectory, baseName, extension);
        Channel<GameFrameSample> channel = Channel.CreateBounded<GameFrameSample>(new BoundedChannelOptions(channelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        ActiveSession session = new(startInfo, channel, finalPath, storageMode);
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
            finalizationState = SessionFinalizationState.Recording;
            lastFinalizationResult = null;
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

    public void SetFrameQualityDiagnostics(
        Guid captureSessionId,
        int generation,
        GameFrameQualityDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        lock (stateLock)
        {
            if (activeSession is { } session
                && session.StartInfo.CaptureSessionId == captureSessionId
                && session.StartInfo.Generation == generation)
            {
                session.FrameQualityDiagnostics = diagnostics;
            }
        }
    }

    public async Task<GameSessionRecordInfo?> CompleteAsync(
        GameSessionEndReason reason,
        bool completedNormally,
        CancellationToken cancellationToken = default)
    {
        ActiveSession? session;
        Task<GameSessionRecordInfo?> completionTask;
        lock (stateLock)
        {
            session = activeSession;
            if (session is null) return null;
            if (session.CompletionTask is null)
            {
                session.FinalizationState = SessionFinalizationState.Finalizing;
                finalizationState = SessionFinalizationState.Finalizing;
                session.CompletionTask = FinalizeSessionAsync(session, reason, completedNormally);
            }

            completionTask = session.CompletionTask;
        }

        return cancellationToken.CanBeCanceled
            ? await completionTask.WaitAsync(cancellationToken).ConfigureAwait(false)
            : await completionTask.ConfigureAwait(false);
    }

    private async Task<GameSessionRecordInfo?> FinalizeSessionAsync(
        ActiveSession session,
        GameSessionEndReason reason,
        bool completedNormally)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        GameSessionRecordInfo? record = null;
        SessionFinalizationState state = SessionFinalizationState.Failed;
        try
        {
            record = await FinalizeSessionCoreAsync(session, reason, completedNormally, timeout.Token)
                .ConfigureAwait(false);
            state = session.Failure is null
                ? SessionFinalizationState.Completed
                : SessionFinalizationState.Failed;
            return record;
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            session.Failure ??= exception;
            AddStep(session, "finalizer", false, exception.Message);
            EnsureRecoverablePartial(session);
            AppLogger.LogError(
                $"Game session finalization failed; recoverable data was preserved | path={session.PartialPath}",
                exception,
                $"game-recorder-finalize:{session.StartInfo.CaptureSessionId:N}",
                TimeSpan.FromMinutes(1));
            record = CreateRecord(
                session,
                DateTimeOffset.Now,
                GameSessionEndReason.RecorderFailed,
                summary: null,
                timelineResult: null,
                finalized: false);
            return record;
        }
        finally
        {
            session.Channel.Writer.TryComplete();
            session.WriterCancellation.Cancel();
            session.WriterCancellation.Dispose();
            session.FinalizationState = state;
            SessionFinalizationResult result = new()
            {
                State = state,
                Record = record,
                Steps = session.FinalizationSteps.ToArray()
            };
            lock (stateLock)
            {
                if (ReferenceEquals(activeSession, session)) activeSession = null;
                finalizationState = state;
                lastFinalizationResult = result;
            }
        }
    }

    private async Task<GameSessionRecordInfo?> FinalizeSessionCoreAsync(
        ActiveSession session,
        GameSessionEndReason reason,
        bool completedNormally,
        CancellationToken cancellationToken)
    {
        session.Channel.Writer.TryComplete();
        try
        {
            await session.WriterTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (session.Failure is not null) throw session.Failure;
            AddStep(session, "frame-csv-writer", true);
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            session.Failure ??= exception;
            AddStep(session, "frame-csv-writer", false, exception.Message);
        }

        DateTimeOffset endedAt = DateTimeOffset.Now;
        GameHardwareTimelineResult? timelineResult = null;
        try
        {
            timelineResult = hardwareTimelineRecorder is null
                ? null
                : await hardwareTimelineRecorder.CompleteSessionAsync(
                    session.StartInfo.CaptureSessionId,
                    session.StartInfo.Generation,
                    cancellationToken).ConfigureAwait(false);
            bool timelineSucceeded = timelineResult is null || timelineResult.CompletedSuccessfully;
            AddStep(
                session,
                "hardware-timeline",
                timelineSucceeded,
                timelineSucceeded ? null : "timeline finalizer reported failure");
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            AddStep(session, "hardware-timeline", false, exception.Message);
        }

        if (Interlocked.Read(ref session.WrittenSamples) == 0L)
        {
            TryDelete(session.PartialPath);
            if (!string.IsNullOrWhiteSpace(timelineResult?.FilePath)) TryDelete(timelineResult.FilePath);
            AddStep(session, "frame-csv", true, "not required: no frame samples");
            AddStep(session, "performance-limit-csv", true, "not required: no frame samples");
            AddStep(session, "summary-json", true, "not required: no frame samples");
            RaiseStateChanged(new GameSessionRecorderStateChangedEventArgs(
                "会话结束，未产生可记录帧",
                isRecording: false,
                currentPath: null,
                Interlocked.Read(ref session.DroppedSamples)));
            return null;
        }

        if (session.Failure is not null)
        {
            EnsureRecoverablePartial(session);
            return CreateRecord(
                session,
                endedAt,
                GameSessionEndReason.RecorderFailed,
                summary: null,
                timelineResult,
                finalized: false);
        }

        SafeMoveNoOverwrite(session.PartialPath, session.FinalPath);
        AddStep(session, "frame-csv", true);
        GameSessionSummary? summary = null;
        string? summaryPath = null;
        try
        {
            // The summary is written last. Record the intended step before serialization so
            // a successfully written summary contains the complete four-file outcome.
            AddStep(session, "summary-json", true);
            summary = await CreateSummaryAsync(
                session,
                session.FinalPath,
                endedAt,
                reason,
                completedNormally,
                timelineResult,
                cancellationToken).ConfigureAwait(false);
            summaryPath = GameSessionFileNaming.GetRelatedPath(session.FinalPath, ".summary.json");
            await WriteSummaryAtomicallyAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsFatalException(exception))
        {
            session.Failure ??= exception;
            AddStep(session, "summary-json", false, exception.Message);
            EnsureRecoverablePartial(session);
            summaryPath = null;
            reason = GameSessionEndReason.RecorderFailed;
        }

        bool finalized = session.Failure is null && summaryPath is not null;
        GameSessionRecordInfo record = CreateRecord(
            session,
            endedAt,
            reason,
            summary,
            timelineResult,
            finalized,
            summaryPath);
        if (finalized)
        {
            try
            {
                await catalog.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
                directorySizeCache.AddBytes(CalculateRecordFileBytes(record));
                AddStep(session, "session-index", true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                AddStep(session, "session-index", false, exception.Message);
                AppLogger.LogError("Completed session could not be appended to the index.", exception,
                    $"session-index-append:{session.StartInfo.CaptureSessionId:N}", TimeSpan.FromMinutes(1));
            }
        }
        RaiseStateChanged(new GameSessionRecorderStateChangedEventArgs(
            finalized ? "游戏会话记录已保存" : "记录失败，已保留部分数据",
            isRecording: false,
            record.CsvPath,
            Interlocked.Read(ref session.DroppedSamples),
            record));
        return record;
    }

    public async Task<IReadOnlyList<GameSessionRecordInfo>> GetRecentRecordsAsync(
        int maximumCount = 10,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) return [];
        GameSessionRecordPage page = await GetRecordsPageAsync(
            0,
            maximumCount,
            snapshotToken: null,
            cancellationToken).ConfigureAwait(false);
        return page.Records;
    }

    public async Task<GameSessionRecordPage> GetRecordsPageAsync(
        int offset,
        int pageSize,
        string? snapshotToken = null,
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

        int normalizedOffset = Math.Max(0, offset);
        int normalizedPageSize = Math.Max(1, pageSize);
        if (!Directory.Exists(RootDirectory))
        {
            return new GameSessionRecordPage
            {
                Offset = normalizedOffset,
                PageSize = normalizedPageSize
            };
        }

        CatalogPageReadResult result = await catalog.ReadPageAsync(
            normalizedOffset,
            normalizedPageSize,
            snapshotToken,
            cancellationToken).ConfigureAwait(false);
        return result.Page;
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

        return await directorySizeCache.GetExactSizeAsync(force: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<GameSessionDirectorySizeInfo> GetDirectorySizeInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(directorySizeCache.GetInfo());
    }

    public Task<long> RecalculateDirectorySizeAsync(CancellationToken cancellationToken = default) =>
        directorySizeCache.GetExactSizeAsync(force: true, cancellationToken);

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
        Stopwatch writerStopwatch = Stopwatch.StartNew();
        StreamWriter? writer = null;
        try
        {
            await foreach (GameFrameSample sample in session.Channel.Reader
                .ReadAllAsync(session.WriterCancellation.Token).ConfigureAwait(false))
            {
                writer ??= CreateWriter(session.PartialPath, session.StorageMode);
                string line = GameCsvFormatting.FormatSample(sample);
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                Interlocked.Add(ref session.UncompressedFrameBytes, Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length);
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
            writerStopwatch.Stop();
            long storedBytes = File.Exists(session.PartialPath) ? new FileInfo(session.PartialPath).Length : 0L;
            RuntimePerformanceDiagnostics.RecordCompressionWriter(
                writerStopwatch.Elapsed,
                Interlocked.Read(ref session.UncompressedFrameBytes),
                session.StorageMode == GameSessionFrameStorageMode.CompressedCsv ? storedBytes : 0L,
                Interlocked.Read(ref session.DroppedSamples));
        }
    }

    private static StreamWriter CreateWriter(string partialPath, GameSessionFrameStorageMode storageMode)
    {
        FileStream file = new(
            partialPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        Stream output = storageMode == GameSessionFrameStorageMode.CompressedCsv
            ? new GZipStream(file, CompressionLevel.Fastest, leaveOpen: false)
            : file;
        StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024);
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

        bool performanceExpected = limitsMatch;
        bool performanceSucceeded = !performanceExpected || performanceLimitCsvFileName is not null;
        AddStep(
            session,
            "performance-limit-csv",
            performanceSucceeded,
            performanceSucceeded ? null : "performance-limit CSV could not be finalized");

        bool timelineMatches = timelineResult is not null
            && timelineResult.CaptureSessionId == session.StartInfo.CaptureSessionId
            && timelineResult.CaptureGeneration == session.StartInfo.Generation;
        return new GameSessionSummary
        {
            SessionSchemaVersion = 4,
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
            WarmupCandidateSampleCount = session.FrameQualityDiagnostics?.WarmupCandidateSampleCount,
            WarmupDiscardedSampleCount = session.FrameQualityDiagnostics?.WarmupDiscardedSampleCount,
            NonPrimarySwapChainSampleCount = session.FrameQualityDiagnostics?.NonPrimarySwapChainSampleCount,
            InvalidFrameTimeSampleCount = session.FrameQualityDiagnostics?.InvalidFrameTimeSampleCount,
            InvalidTimestampSampleCount = session.FrameQualityDiagnostics?.InvalidTimestampSampleCount,
            SanitizedMetricFieldCount = session.FrameQualityDiagnostics?.SanitizedMetricFieldCount,
            FrameTimeOutlierSampleCount = session.FrameQualityDiagnostics?.FrameTimeOutlierSampleCount,
            DuplicateCaptureElapsedSampleCount = session.FrameQualityDiagnostics?.DuplicateCaptureElapsedSampleCount,
            RegressedCaptureElapsedSampleCount = session.FrameQualityDiagnostics?.RegressedCaptureElapsedSampleCount,
            DuplicateExplicitTimestampSampleCount = session.FrameQualityDiagnostics?.DuplicateExplicitTimestampSampleCount,
            RegressedExplicitTimestampSampleCount = session.FrameQualityDiagnostics?.RegressedExplicitTimestampSampleCount,
            MissingTimestampSampleCount = session.FrameQualityDiagnostics?.MissingTimestampSampleCount,
            CompatibilityFallbackSampleCount = session.FrameQualityDiagnostics?.CompatibilityFallbackSampleCount,
            StableLevelTransitionCandidateSampleCount = session.FrameQualityDiagnostics?.StableLevelTransitionCandidateSampleCount,
            StableLevelTransitionConfirmedCount = session.FrameQualityDiagnostics?.StableLevelTransitionConfirmedCount,
            InvalidAuxiliaryMetricFieldCount = session.FrameQualityDiagnostics?.InvalidAuxiliaryMetricFieldCount,
            AuxiliaryMetricOutlierFieldCount = session.FrameQualityDiagnostics?.AuxiliaryMetricOutlierFieldCount,
            CpuBusySanitizedCount = session.FrameQualityDiagnostics?.CpuBusySanitizedCount,
            CpuWaitSanitizedCount = session.FrameQualityDiagnostics?.CpuWaitSanitizedCount,
            GpuLatencySanitizedCount = session.FrameQualityDiagnostics?.GpuLatencySanitizedCount,
            GpuTimeSanitizedCount = session.FrameQualityDiagnostics?.GpuTimeSanitizedCount,
            GpuBusySanitizedCount = session.FrameQualityDiagnostics?.GpuBusySanitizedCount,
            GpuWaitSanitizedCount = session.FrameQualityDiagnostics?.GpuWaitSanitizedCount,
            RenderLatencySanitizedCount = session.FrameQualityDiagnostics?.RenderLatencySanitizedCount,
            DisplayLatencySanitizedCount = session.FrameQualityDiagnostics?.DisplayLatencySanitizedCount,
            DisplayedTimeSanitizedCount = session.FrameQualityDiagnostics?.DisplayedTimeSanitizedCount,
            ClickToPhotonLatencySanitizedCount = session.FrameQualityDiagnostics?.ClickToPhotonLatencySanitizedCount,
            PrimarySwapChainAddress = session.FrameQualityDiagnostics?.PrimarySwapChainAddress,
            SwapChainSwitchCount = session.FrameQualityDiagnostics?.SwapChainSwitchCount,
            CaptureWarmupDurationSeconds = session.FrameQualityDiagnostics?.CaptureWarmupDurationSeconds,
            UsedCompatibilityFallback = session.FrameQualityDiagnostics?.UsedCompatibilityFallback,
            PrimarySwapChainSelectionUncertain = session.FrameQualityDiagnostics?.PrimarySwapChainSelectionUncertain,
            RawMaximumFps = session.FrameQualityDiagnostics?.RawMaximumFps,
            SustainedMaximumFps = session.FrameQualityDiagnostics?.SustainedMaximumFps,
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
            CsvFileSize = file.Length,
            FrameStorageFormat = session.StorageMode.ToString(),
            FrameCompression = session.StorageMode == GameSessionFrameStorageMode.CompressedCsv ? "GZip/Fastest" : "None",
            UncompressedFrameBytes = session.StorageMode == GameSessionFrameStorageMode.CompressedCsv
                ? Interlocked.Read(ref session.UncompressedFrameBytes)
                : file.Length,
            CompressedFrameBytes = session.StorageMode == GameSessionFrameStorageMode.CompressedCsv ? file.Length : null,
            CompressionRatioPercent = session.StorageMode == GameSessionFrameStorageMode.CompressedCsv
                && Interlocked.Read(ref session.UncompressedFrameBytes) > 0
                    ? 100d * file.Length / Interlocked.Read(ref session.UncompressedFrameBytes)
                    : null,
            FinalizationSteps = session.FinalizationSteps.ToArray()
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
        using StreamReader reader = GameSessionFrameStreamFactory.Shared.OpenTextReader(csvPath);
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
            using StreamReader reader = GameSessionFrameStreamFactory.Shared.OpenTextReader(path, 4096);
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

    private bool PerformanceLimitsMatch(ActiveSession session)
    {
        GamePerformanceLimitSnapshot snapshot = performanceLimitTracker?.CurrentSnapshot
            ?? GamePerformanceLimitSnapshot.Empty;
        return snapshot.CaptureSessionId == session.StartInfo.CaptureSessionId
            && snapshot.Generation == session.StartInfo.Generation;
    }

    private static GameSessionRecordInfo CreateRecord(
        ActiveSession session,
        DateTimeOffset endedAt,
        GameSessionEndReason reason,
        GameSessionSummary? summary,
        GameHardwareTimelineResult? timelineResult,
        bool finalized,
        string? summaryPath = null)
    {
        string csvPath = finalized ? session.FinalPath : session.PartialPath;
        if (!File.Exists(csvPath) && File.Exists(session.FinalPath)) csvPath = session.FinalPath;
        string directory = Path.GetDirectoryName(csvPath)!;
        string? limitPath = ResolveSummaryFile(
            directory,
            summary?.PerformanceLimitCsvFileName,
            SessionFileKind.PerformanceLimitsCsv);
        string? timelinePath = ResolveSummaryFile(
            directory,
            summary?.HardwareTimelineCsvFileName,
            SessionFileKind.HardwareTimelineCsv)
            ?? timelineResult?.FilePath;
        FileInfo file = new(csvPath);
        return new GameSessionRecordInfo
        {
            GameName = session.StartInfo.ProcessName,
            StartedAt = session.StartInfo.CaptureStartedAt,
            Duration = endedAt - session.StartInfo.CaptureStartedAt,
            FileSize = file.Exists ? file.Length : 0L,
            IsComplete = finalized,
            CsvPath = csvPath,
            SummaryPath = summaryPath,
            PerformanceLimitCsvPath = limitPath,
            HardwareTimelineCsvPath = timelinePath,
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
    }

    private static long CalculateRecordFileBytes(GameSessionRecordInfo record)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        paths.Add(record.CsvPath);
        if (!string.IsNullOrWhiteSpace(record.SummaryPath)) paths.Add(record.SummaryPath);
        if (!string.IsNullOrWhiteSpace(record.PerformanceLimitCsvPath)) paths.Add(record.PerformanceLimitCsvPath);
        if (!string.IsNullOrWhiteSpace(record.HardwareTimelineCsvPath)) paths.Add(record.HardwareTimelineCsvPath);
        long total = 0L;
        foreach (string path in paths)
        {
            try { if (File.Exists(path)) total += new FileInfo(path).Length; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    private static async Task<GameSessionRecordInfo?> ReadSummaryRecordAsync(
        string summaryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(summaryPath);
            GameSessionSummary? summary = await JsonSerializer
                .DeserializeAsync<GameSessionSummary>(stream, SummaryJsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (summary is null) return null;

            string summaryDirectory = Path.GetDirectoryName(summaryPath)!;
            if (!SessionFilePathResolver.TryResolve(
                    summaryDirectory,
                    summary.CsvFileName,
                    SessionFileKind.FrameCsv,
                    out string? csvPath,
                    out _)
                || csvPath is null)
            {
                return null;
            }

            string? limitPath = ResolveSummaryFile(
                summaryDirectory,
                summary.PerformanceLimitCsvFileName,
                SessionFileKind.PerformanceLimitsCsv);
            string? timelinePath = ResolveSummaryFile(
                summaryDirectory,
                summary.HardwareTimelineCsvFileName,
                SessionFileKind.HardwareTimelineCsv);
            return new GameSessionRecordInfo
            {
                GameName = summary.ProcessName,
                StartedAt = summary.CaptureStartedAt,
                Duration = summary.Duration,
                FileSize = File.Exists(csvPath) ? new FileInfo(csvPath).Length : summary.CsvFileSize,
                IsComplete = true,
                CsvPath = csvPath,
                SummaryPath = summaryPath,
                PerformanceLimitCsvPath = limitPath,
                HardwareTimelineCsvPath = timelinePath,
                EndReason = summary.EndReason,
                EstimatedEnergyWh = summary.EstimatedEnergyWh,
                AverageEstimatedPowerWatts = summary.AverageEstimatedPowerWatts,
                EnergyCoveragePercent = summary.EnergyCoveragePercent,
                EnergyIncludedComponents = summary.EnergyIncludedComponents,
                CpuPerformanceLimitEventCount = summary.CpuPerformanceLimitEventCount,
                GpuPerformanceLimitEventCount = summary.GpuPerformanceLimitEventCount,
                CpuPerformanceLimitSupportStatus = summary.CpuPerformanceLimitSupportStatus,
                GpuPerformanceLimitSupportStatus = summary.GpuPerformanceLimitSupportStatus
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.LogError("Game session summary could not be read.", exception,
                $"game-summary-read:{Path.GetFileName(summaryPath)}", TimeSpan.FromMinutes(5));
            return null;
        }
    }

    private static void AddStep(ActiveSession session, string name, bool succeeded, string? error = null)
    {
        SessionFinalizationStepInfo step = new()
        {
            Name = name,
            Succeeded = succeeded,
            Error = error
        };
        int existingIndex = session.FinalizationSteps.FindIndex(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            session.FinalizationSteps[existingIndex] = step;
        }
        else
        {
            session.FinalizationSteps.Add(step);
        }
    }

    private static void SafeMoveNoOverwrite(string source, string destination)
    {
        if (!File.Exists(source))
        {
            if (File.Exists(destination)) return;
            throw new FileNotFoundException("Session source file is missing.", source);
        }

        if (File.Exists(destination))
        {
            throw new IOException($"Session destination already exists: {destination}");
        }

        File.Move(source, destination);
    }

    private static void EnsureRecoverablePartial(ActiveSession session)
    {
        if (File.Exists(session.PartialPath) || !File.Exists(session.FinalPath)) return;
        try
        {
            File.Move(session.FinalPath, session.PartialPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError(
                $"Finalized frame CSV could not be rolled back to a recoverable partial | path={session.FinalPath}",
                exception,
                $"game-recorder-rollback:{session.StartInfo.CaptureSessionId:N}",
                TimeSpan.FromMinutes(1));
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException;

    private static string ParseGameName(string fileName)
    {
        int timestampSeparator = fileName.IndexOf('-', StringComparison.Ordinal);
        return timestampSeparator > 0 ? fileName[..timestampSeparator] : "Game";
    }

    private void AppendIncompleteRecords(List<GameSessionRecordInfo> records, CancellationToken cancellationToken)
    {
        IEnumerable<string> incompletePaths = Directory.EnumerateFiles(
                RootDirectory,
                "*.csv.incomplete",
                SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                RootDirectory,
                "*.csv.gz.incomplete",
                SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string incompletePath in incompletePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
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
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? ResolveSummaryFile(string directory, string? fileName, SessionFileKind kind)
    {
        return SessionFilePathResolver.TryResolve(directory, fileName, kind, out string? path, out _)
            ? path
            : null;
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
        public ActiveSession(
            GameSessionStartInfo startInfo,
            Channel<GameFrameSample> channel,
            string finalPath,
            GameSessionFrameStorageMode storageMode)
        {
            StartInfo = startInfo;
            Channel = channel;
            FinalPath = finalPath;
            PartialPath = finalPath + ".partial";
            StorageMode = storageMode;
            UncompressedFrameBytes = Encoding.UTF8.GetPreamble().Length
                + Encoding.UTF8.GetByteCount(GameCsvFormatting.Header)
                + Environment.NewLine.Length;
        }

        public GameSessionStartInfo StartInfo { get; }
        public Channel<GameFrameSample> Channel { get; }
        public string FinalPath { get; }
        public string PartialPath { get; }
        public GameSessionFrameStorageMode StorageMode { get; }
        public GameFrameQualityDiagnostics? FrameQualityDiagnostics { get; set; }
        public CancellationTokenSource WriterCancellation { get; } = new();
        public Task WriterTask { get; set; } = Task.CompletedTask;
        public Task<GameSessionRecordInfo?>? CompletionTask { get; set; }
        public SessionFinalizationState FinalizationState { get; set; } = SessionFinalizationState.Recording;
        public List<SessionFinalizationStepInfo> FinalizationSteps { get; } = [];
        public Exception? Failure { get; set; }
        public long ReceivedSamples;
        public long WrittenSamples;
        public long DroppedSamples;
        public long LastDropLogTick;
        public long UncompressedFrameBytes;
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
