using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

internal sealed class GameFrameSampleStore
{
    private const int MaximumStatisticsCacheEntries = 8;
    private sealed record CachedStatistics(
        long SampleVersion,
        DateTimeOffset LowFpsCalculatedAt,
        GamePerformanceSnapshot Snapshot);

    private readonly object syncRoot = new();
    private readonly GameFrameSample?[] samples;
    private readonly Dictionary<long, CachedStatistics> statisticsCache = new();
    private Guid captureSessionId;
    private int startIndex;
    private int sampleCount;
    private long sampleVersion;

    public GameFrameSampleStore(int maximumSampleCount)
    {
        samples = new GameFrameSample[Math.Max(1, maximumSampleCount)];
    }

    public long SampleVersion
    {
        get
        {
            lock (syncRoot)
            {
                return sampleVersion;
            }
        }
    }

    internal TimeSpan LastCalculationLockDuration { get; private set; }

    public void StartSession(Guid sessionId)
    {
        lock (syncRoot)
        {
            captureSessionId = sessionId;
            Array.Clear(samples);
            startIndex = 0;
            sampleCount = 0;
            sampleVersion++;
            statisticsCache.Clear();
        }
    }

    public bool TryAdd(GameFrameSample sample)
    {
        lock (syncRoot)
        {
            if (sample.CaptureSessionId == Guid.Empty || sample.CaptureSessionId != captureSessionId)
            {
                return false;
            }

            if (sampleCount < samples.Length)
            {
                int writeIndex = (startIndex + sampleCount) % samples.Length;
                samples[writeIndex] = sample;
                sampleCount++;
            }
            else
            {
                samples[startIndex] = sample;
                startIndex = (startIndex + 1) % samples.Length;
            }

            sampleVersion++;
            return true;
        }
    }

    public IReadOnlyList<GameFrameSample> Snapshot()
    {
        return Snapshot(window: null);
    }

    public IReadOnlyList<GameFrameSample> Snapshot(TimeSpan? window)
    {
        lock (syncRoot)
        {
            return CreateSnapshotLocked(window);
        }
    }

    public GamePerformanceSnapshot Calculate(TimeSpan window)
    {
        Stopwatch totalClock = Stopwatch.StartNew();
        Stopwatch lockClock = Stopwatch.StartNew();
        GameFrameSample[] snapshot;
        Guid sessionId;
        long version;
        CachedStatistics? cached;
        lock (syncRoot)
        {
            snapshot = CreateSnapshotLocked(window);
            sessionId = captureSessionId;
            version = sampleVersion;
            statisticsCache.TryGetValue(window.Ticks, out cached);
        }

        lockClock.Stop();
        LastCalculationLockDuration = lockClock.Elapsed;
        try
        {
            if (cached is not null && cached.SampleVersion == version)
            {
                return cached.Snapshot;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool calculateLowFps = cached is null
                || now - cached.LowFpsCalculatedAt >= TimeSpan.FromSeconds(1);
            GamePerformanceSnapshot result = GameFrameStatisticsCalculator.Calculate(
                snapshot,
                window,
                sessionId,
                calculateLowFps ? null : cached?.Snapshot);
            CachedStatistics nextCache = new(
                version,
                calculateLowFps ? now : cached!.LowFpsCalculatedAt,
                result);
            lock (syncRoot)
            {
                if (!statisticsCache.ContainsKey(window.Ticks)
                    && statisticsCache.Count >= MaximumStatisticsCacheEntries)
                {
                    long oldestKey = 0;
                    foreach (long cachedWindow in statisticsCache.Keys)
                    {
                        oldestKey = cachedWindow;
                        break;
                    }

                    statisticsCache.Remove(oldestKey);
                }

                statisticsCache[window.Ticks] = nextCache;
            }

            return result;
        }
        finally
        {
            totalClock.Stop();
            RuntimePerformanceDiagnostics.RecordGameStatistics(totalClock.Elapsed, lockClock.Elapsed);
        }
    }

    private GameFrameSample[] CreateSnapshotLocked(TimeSpan? window)
    {
        if (sampleCount == 0)
        {
            return Array.Empty<GameFrameSample>();
        }

        DateTimeOffset cutoff = window.HasValue ? DateTimeOffset.Now - window.Value : DateTimeOffset.MinValue;
        int first = 0;
        if (window.HasValue)
        {
            int low = 0;
            int high = sampleCount;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (samples[(startIndex + middle) % samples.Length]!.Timestamp < cutoff)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            first = low;
        }

        int count = sampleCount - first;
        GameFrameSample[] snapshot = new GameFrameSample[count];
        for (int index = 0; index < count; index++)
        {
            snapshot[index] = samples[(startIndex + first + index) % samples.Length]!;
        }

        return snapshot;
    }
}
