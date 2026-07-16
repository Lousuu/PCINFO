using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

internal sealed class GameHardwareTimelineRecorder : IDisposable, IAsyncDisposable
{
    internal const int DefaultChannelCapacity = 512;
    internal static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(1);

    private readonly object stateLock = new();
    private readonly PollingService? pollingService;
    private readonly IGamePerformanceLimitTracker? performanceLimitTracker;
    private readonly Func<long> getTimestamp;
    private readonly double timestampFrequency;
    private readonly long sampleIntervalTicks;
    private readonly int channelCapacity;
    private ActiveTimeline? activeTimeline;
    private bool isDisposed;

    public GameHardwareTimelineRecorder(
        PollingService pollingService,
        IGamePerformanceLimitTracker? performanceLimitTracker)
        : this(
            pollingService,
            performanceLimitTracker,
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency,
            DefaultSampleInterval,
            DefaultChannelCapacity)
    {
    }

    internal GameHardwareTimelineRecorder(
        PollingService? pollingService,
        IGamePerformanceLimitTracker? performanceLimitTracker,
        Func<long> getTimestamp,
        long timestampFrequency,
        TimeSpan sampleInterval,
        int channelCapacity)
    {
        this.pollingService = pollingService;
        this.performanceLimitTracker = performanceLimitTracker;
        this.getTimestamp = getTimestamp;
        this.timestampFrequency = Math.Max(1d, timestampFrequency);
        sampleIntervalTicks = Math.Max(1L, (long)Math.Ceiling(sampleInterval.TotalSeconds * this.timestampFrequency));
        this.channelCapacity = Math.Max(1, channelCapacity);
        if (pollingService is not null) pollingService.ReadingsUpdated += OnReadingsUpdated;
    }

    public void StartSession(GameSessionStartInfo startInfo, string frameCsvPath)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(frameCsvPath);
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (activeTimeline is not null) throw new InvalidOperationException("A hardware timeline session is already active.");
            Channel<GameHardwareTimelineSample> channel = Channel.CreateBounded<GameHardwareTimelineSample>(
                new BoundedChannelOptions(channelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait
                });
            ActiveTimeline timeline = new(startInfo, channel, GameHardwareTimelineCsv.GetPath(frameCsvPath), getTimestamp());
            timeline.WriterTask = WriteAsync(timeline);
            activeTimeline = timeline;
        }
    }

    public async Task<GameHardwareTimelineResult?> CompleteSessionAsync(
        Guid captureSessionId,
        int generation,
        CancellationToken cancellationToken = default)
    {
        ActiveTimeline? timeline;
        lock (stateLock)
        {
            timeline = activeTimeline;
            if (timeline is null
                || timeline.StartInfo.CaptureSessionId != captureSessionId
                || timeline.StartInfo.Generation != generation)
            {
                return null;
            }

            activeTimeline = null;
        }

        timeline.Channel.Writer.TryComplete();
        try
        {
            await timeline.WriterTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            File.Move(timeline.PartialPath, timeline.FinalPath);
            return CreateResult(timeline, timeline.FinalPath, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            timeline.Failure ??= exception;
            AppLogger.LogError(
                $"Hardware timeline could not be finalized; partial data was preserved | path={timeline.PartialPath}",
                timeline.Failure,
                $"game-hardware-timeline:{captureSessionId:N}",
                TimeSpan.FromMinutes(1));
            return CreateResult(timeline, File.Exists(timeline.PartialPath) ? timeline.PartialPath : null, false);
        }
    }

    internal bool RecordReadings(
        Guid captureSessionId,
        int generation,
        IReadOnlyList<SensorReading> readings,
        GamePerformanceLimitSnapshot limits,
        long timestamp,
        DateTimeOffset observedAt)
    {
        ActiveTimeline? timeline;
        lock (stateLock)
        {
            timeline = activeTimeline;
            if (isDisposed
                || timeline is null
                || timeline.StartInfo.CaptureSessionId != captureSessionId
                || timeline.StartInfo.Generation != generation)
            {
                return false;
            }

            if (timeline.LastSampleTimestamp.HasValue
                && timestamp - timeline.LastSampleTimestamp.Value < sampleIntervalTicks)
            {
                return false;
            }

            timeline.LastSampleTimestamp = timestamp;
        }

        // All persisted session files share the capture wall-clock origin. The monotonic
        // timestamp above is only used for the one-second throttle; it is not a second time base.
        double elapsed = Math.Max(0d, (observedAt - timeline.StartInfo.CaptureStartedAt).TotalSeconds);
        IReadOnlyList<GameHardwareTimelineSample> samples = GameHardwareTimelineSampler.CreateSamples(
            timeline.StartInfo,
            readings,
            limits,
            observedAt,
            elapsed);
        bool accepted = false;
        for (int index = 0; index < samples.Count; index++)
        {
            if (timeline.Channel.Writer.TryWrite(samples[index]))
            {
                accepted = true;
            }
            else
            {
                Interlocked.Increment(ref timeline.DroppedSamples);
            }
        }

        return accepted;
    }

    public static async Task RecoverIncompleteAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory)) return;
        string[] partialPaths;
        try
        {
            partialPaths = Directory.GetFiles(
                rootDirectory,
                "*.hardware-timeline.partial.csv",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLogger.LogError(
                "Incomplete hardware timeline scan failed; existing files were left in place.",
                exception,
                $"hardware-timeline-recovery-scan:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
            return;
        }

        foreach (string partialPath in partialPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool hasData;
                using (StreamReader reader = new(partialPath, Encoding.UTF8, true, 4096))
                {
                    _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    hasData = !string.IsNullOrWhiteSpace(await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false));
                }
                if (!hasData)
                {
                    File.Delete(partialPath);
                    continue;
                }

                string incompletePath = partialPath.Replace(
                    ".hardware-timeline.partial.csv",
                    ".hardware-timeline.incomplete.csv",
                    StringComparison.OrdinalIgnoreCase);
                incompletePath = AllocatePath(incompletePath);
                File.Move(partialPath, incompletePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.LogError(
                    $"Incomplete hardware timeline was left in place | path={partialPath}",
                    exception,
                    $"hardware-timeline-recovery:{Path.GetFileName(partialPath)}",
                    TimeSpan.FromMinutes(5));
            }
        }
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        if (pollingService is not null) pollingService.ReadingsUpdated -= OnReadingsUpdated;
        ActiveTimeline? timeline;
        lock (stateLock)
        {
            timeline = activeTimeline;
        }
        if (timeline is not null)
        {
            CompleteSessionAsync(timeline.StartInfo.CaptureSessionId, timeline.StartInfo.Generation)
                .GetAwaiter().GetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed) return;
        isDisposed = true;
        if (pollingService is not null) pollingService.ReadingsUpdated -= OnReadingsUpdated;
        ActiveTimeline? timeline;
        lock (stateLock)
        {
            timeline = activeTimeline;
        }
        if (timeline is not null)
        {
            await CompleteSessionAsync(timeline.StartInfo.CaptureSessionId, timeline.StartInfo.Generation).ConfigureAwait(false);
        }
    }

    private void OnReadingsUpdated(object? sender, SensorReadingsUpdatedEventArgs e)
    {
        ActiveTimeline? timeline;
        lock (stateLock)
        {
            timeline = activeTimeline;
        }
        if (timeline is null || isDisposed) return;
        RecordReadings(
            timeline.StartInfo.CaptureSessionId,
            timeline.StartInfo.Generation,
            e.Readings,
            performanceLimitTracker?.CurrentSnapshot ?? GamePerformanceLimitSnapshot.Empty,
            getTimestamp(),
            e.Timestamp);
    }

    private static async Task WriteAsync(ActiveTimeline timeline)
    {
        try
        {
            await using FileStream stream = new(
                timeline.PartialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using StreamWriter writer = new(stream, new UTF8Encoding(true), 32 * 1024);
            await writer.WriteLineAsync(GameHardwareTimelineCsv.Header).ConfigureAwait(false);
            await foreach (GameHardwareTimelineSample sample in timeline.Channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await writer.WriteLineAsync(GameHardwareTimelineCsv.Format(sample)).ConfigureAwait(false);
                Interlocked.Increment(ref timeline.WrittenSamples);
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            timeline.Failure = exception;
            timeline.Channel.Writer.TryComplete(exception);
            throw;
        }
    }

    private static GameHardwareTimelineResult CreateResult(ActiveTimeline timeline, string? path, bool success) => new()
    {
        CaptureSessionId = timeline.StartInfo.CaptureSessionId,
        CaptureGeneration = timeline.StartInfo.Generation,
        FilePath = path,
        WrittenSampleCount = Interlocked.Read(ref timeline.WrittenSamples),
        DroppedSampleCount = Interlocked.Read(ref timeline.DroppedSamples),
        CompletedSuccessfully = success
    };

    private static string AllocatePath(string path)
    {
        if (!File.Exists(path)) return path;
        string directory = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = Path.Combine(directory, $"{name}-{suffix}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException("Unable to allocate an incomplete hardware timeline path.");
    }

    private sealed class ActiveTimeline(
        GameSessionStartInfo startInfo,
        Channel<GameHardwareTimelineSample> channel,
        string finalPath,
        long startTimestamp)
    {
        public GameSessionStartInfo StartInfo { get; } = startInfo;
        public Channel<GameHardwareTimelineSample> Channel { get; } = channel;
        public string FinalPath { get; } = finalPath;
        public string PartialPath { get; } = GameHardwareTimelineCsv.GetPartialPath(finalPath);
        public long StartTimestamp { get; } = startTimestamp;
        public long? LastSampleTimestamp { get; set; }
        public Task WriterTask { get; set; } = Task.CompletedTask;
        public Exception? Failure { get; set; }
        public long WrittenSamples;
        public long DroppedSamples;
    }
}
