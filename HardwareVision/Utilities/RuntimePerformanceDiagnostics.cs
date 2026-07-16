using System.Diagnostics;
using System.Globalization;

namespace HardwareVision.Utilities;

public readonly record struct RuntimePerformanceSnapshot(
    long PollingCycles,
    long LibreHardwareMonitorUpdates,
    long WmiCpuClockQueries,
    long WmiCpuClockCacheHits,
    long WmiCpuClockFailures,
    long DiskRefreshes,
    long DiskRefreshSkips,
    long NetworkRefreshes,
    long NetworkRefreshSkips,
    long DashboardRefreshes,
    long GameStatisticsCalculations,
    long PresentMonRows,
    long PresentMonSamples,
    long PresentMonFilteredRows,
    long PresentMonParses,
    long EnergyTrackerInputs,
    long EnergyTrackerSnapshots,
    long PerformanceLimitTrackerInputs,
    long PerformanceLimitTrackerSnapshots,
    long WarmupCandidateSamples,
    long WarmupDiscardedSamples,
    long InvalidFrameSamples,
    long NonPrimarySwapChainSamples,
    long DeviceChangeNotifications,
    long HardwareRefreshRequests,
    long HardwareRefreshCoalesced,
    long HardwareRefreshSuccesses,
    long HardwareRefreshFailures,
    long ProviderRefreshFailures,
    long FrameUncompressedBytes,
    long FrameCompressedBytes,
    long CompressionWriterQueueDrops);

public static class RuntimePerformanceDiagnostics
{
    private static readonly object SummaryLock = new();
    private static readonly long StartedTimestamp = Stopwatch.GetTimestamp();
    private static long lastSummaryTimestamp = StartedTimestamp;
    private static TimeSpan lastProcessorTime = GetProcessorTime();
    private static long lastAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
    private static int lastGen0 = GC.CollectionCount(0);
    private static int lastGen1 = GC.CollectionCount(1);
    private static int lastGen2 = GC.CollectionCount(2);

    private static long pollingCycles;
    private static long pollingDurationTicks;
    private static long lhmUpdates;
    private static long lhmDurationTicks;
    private static long wmiQueries;
    private static long wmiCacheHits;
    private static long wmiFailures;
    private static long wmiDurationTicks;
    private static long diskRefreshes;
    private static long diskRefreshSkips;
    private static long diskDurationTicks;
    private static long networkRefreshes;
    private static long networkRefreshSkips;
    private static long networkDurationTicks;
    private static long dashboardRefreshes;
    private static long gameStatisticsCalculations;
    private static long gameStatisticsDurationTicks;
    private static long gameStatisticsLockDurationTicks;
    private static long presentMonRows;
    private static long presentMonSamples;
    private static long presentMonFilteredRows;
    private static long presentMonParses;
    private static long presentMonParseDurationTicks;
    private static long energyTrackerInputs;
    private static long energyTrackerSnapshots;
    private static long performanceLimitTrackerInputs;
    private static long performanceLimitTrackerSnapshots;
    private static long warmupCandidateSamples;
    private static long warmupDiscardedSamples;
    private static long invalidFrameSamples;
    private static long nonPrimarySwapChainSamples;
    private static long deviceChangeNotifications;
    private static long hardwareRefreshRequests;
    private static long hardwareRefreshCoalesced;
    private static long hardwareRefreshSuccesses;
    private static long hardwareRefreshFailures;
    private static long providerRefreshFailures;
    private static long hardwareRefreshDurationTicks;
    private static long frameUncompressedBytes;
    private static long frameCompressedBytes;
    private static long compressionWriterQueueDrops;
    private static long compressionWriterDurationTicks;
    private static long compressionWriterSessions;

    public static RuntimePerformanceSnapshot Snapshot => new(
        Interlocked.Read(ref pollingCycles),
        Interlocked.Read(ref lhmUpdates),
        Interlocked.Read(ref wmiQueries),
        Interlocked.Read(ref wmiCacheHits),
        Interlocked.Read(ref wmiFailures),
        Interlocked.Read(ref diskRefreshes),
        Interlocked.Read(ref diskRefreshSkips),
        Interlocked.Read(ref networkRefreshes),
        Interlocked.Read(ref networkRefreshSkips),
        Interlocked.Read(ref dashboardRefreshes),
        Interlocked.Read(ref gameStatisticsCalculations),
        Interlocked.Read(ref presentMonRows),
        Interlocked.Read(ref presentMonSamples),
        Interlocked.Read(ref presentMonFilteredRows),
        Interlocked.Read(ref presentMonParses),
        Interlocked.Read(ref energyTrackerInputs),
        Interlocked.Read(ref energyTrackerSnapshots),
        Interlocked.Read(ref performanceLimitTrackerInputs),
        Interlocked.Read(ref performanceLimitTrackerSnapshots),
        Interlocked.Read(ref warmupCandidateSamples),
        Interlocked.Read(ref warmupDiscardedSamples),
        Interlocked.Read(ref invalidFrameSamples),
        Interlocked.Read(ref nonPrimarySwapChainSamples),
        Interlocked.Read(ref deviceChangeNotifications),
        Interlocked.Read(ref hardwareRefreshRequests),
        Interlocked.Read(ref hardwareRefreshCoalesced),
        Interlocked.Read(ref hardwareRefreshSuccesses),
        Interlocked.Read(ref hardwareRefreshFailures),
        Interlocked.Read(ref providerRefreshFailures),
        Interlocked.Read(ref frameUncompressedBytes),
        Interlocked.Read(ref frameCompressedBytes),
        Interlocked.Read(ref compressionWriterQueueDrops));

    public static void RecordPolling(TimeSpan duration)
    {
        Interlocked.Increment(ref pollingCycles);
        Interlocked.Add(ref pollingDurationTicks, duration.Ticks);
    }

    public static void RecordLibreHardwareMonitorUpdate(TimeSpan duration)
    {
        Interlocked.Increment(ref lhmUpdates);
        Interlocked.Add(ref lhmDurationTicks, duration.Ticks);
    }

    public static void RecordWmiCpuClockQuery(TimeSpan duration)
    {
        Interlocked.Increment(ref wmiQueries);
        Interlocked.Add(ref wmiDurationTicks, duration.Ticks);
    }

    public static void RecordWmiCpuClockCacheHit() => Interlocked.Increment(ref wmiCacheHits);

    public static void RecordWmiCpuClockFailure() => Interlocked.Increment(ref wmiFailures);

    public static void RecordDiskRefresh(TimeSpan duration)
    {
        Interlocked.Increment(ref diskRefreshes);
        Interlocked.Add(ref diskDurationTicks, duration.Ticks);
    }

    public static void RecordDiskRefreshSkip() => Interlocked.Increment(ref diskRefreshSkips);

    public static void RecordNetworkRefresh(TimeSpan duration)
    {
        Interlocked.Increment(ref networkRefreshes);
        Interlocked.Add(ref networkDurationTicks, duration.Ticks);
    }

    public static void RecordNetworkRefreshSkip() => Interlocked.Increment(ref networkRefreshSkips);

    public static void RecordDashboardRefresh() => Interlocked.Increment(ref dashboardRefreshes);

    public static void RecordGameStatistics(TimeSpan duration, TimeSpan lockDuration = default)
    {
        Interlocked.Increment(ref gameStatisticsCalculations);
        Interlocked.Add(ref gameStatisticsDurationTicks, duration.Ticks);
        Interlocked.Add(ref gameStatisticsLockDurationTicks, lockDuration.Ticks);
    }

    public static void RecordPresentMonRow() => Interlocked.Increment(ref presentMonRows);

    public static void RecordPresentMonSample() => Interlocked.Increment(ref presentMonSamples);

    public static void RecordPresentMonFilteredRow() => Interlocked.Increment(ref presentMonFilteredRows);

    public static void RecordPresentMonParse(TimeSpan duration)
    {
        Interlocked.Increment(ref presentMonParses);
        Interlocked.Add(ref presentMonParseDurationTicks, duration.Ticks);
    }

    public static void RecordEnergyTrackerInput() => Interlocked.Increment(ref energyTrackerInputs);

    public static void RecordEnergyTrackerSnapshot() => Interlocked.Increment(ref energyTrackerSnapshots);

    public static void RecordPerformanceLimitTrackerInput() => Interlocked.Increment(ref performanceLimitTrackerInputs);

    public static void RecordPerformanceLimitTrackerSnapshot() => Interlocked.Increment(ref performanceLimitTrackerSnapshots);

    public static void RecordWarmup(HardwareVision.Models.GameFrameQualityDiagnostics diagnostics)
    {
        Interlocked.Add(ref warmupCandidateSamples, diagnostics.WarmupCandidateSampleCount);
        Interlocked.Add(ref warmupDiscardedSamples, diagnostics.WarmupDiscardedSampleCount);
        Interlocked.Add(ref invalidFrameSamples, diagnostics.InvalidFrameTimeSampleCount + diagnostics.InvalidTimestampSampleCount);
        Interlocked.Add(ref nonPrimarySwapChainSamples, diagnostics.NonPrimarySwapChainSampleCount);
    }

    public static void RecordDeviceChangeNotification() => Interlocked.Increment(ref deviceChangeNotifications);

    public static void RecordHardwareRefreshRequest(bool coalesced)
    {
        Interlocked.Increment(ref hardwareRefreshRequests);
        if (coalesced) Interlocked.Increment(ref hardwareRefreshCoalesced);
    }

    public static void RecordHardwareRefresh(TimeSpan duration, bool succeeded, int failedProviderCount)
    {
        if (succeeded) Interlocked.Increment(ref hardwareRefreshSuccesses);
        else Interlocked.Increment(ref hardwareRefreshFailures);
        Interlocked.Add(ref providerRefreshFailures, failedProviderCount);
        Interlocked.Add(ref hardwareRefreshDurationTicks, duration.Ticks);
    }

    public static void RecordCompressionWriter(TimeSpan duration, long uncompressedBytes, long compressedBytes, long queueDrops)
    {
        Interlocked.Increment(ref compressionWriterSessions);
        Interlocked.Add(ref compressionWriterDurationTicks, duration.Ticks);
        Interlocked.Add(ref frameUncompressedBytes, uncompressedBytes);
        Interlocked.Add(ref frameCompressedBytes, compressedBytes);
        Interlocked.Add(ref compressionWriterQueueDrops, queueDrops);
    }

    public static void TryLogSummary(bool isBackgroundMode)
    {
        long now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(Volatile.Read(ref lastSummaryTimestamp), now) < TimeSpan.FromMinutes(1))
        {
            return;
        }

        lock (SummaryLock)
        {
            long previousTimestamp = lastSummaryTimestamp;
            if (Stopwatch.GetElapsedTime(previousTimestamp, now) < TimeSpan.FromMinutes(1))
            {
                return;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(previousTimestamp, now);
            TimeSpan processorTime = GetProcessorTime();
            double cpuPercent = elapsed.TotalSeconds <= 0d
                ? 0d
                : Math.Max(0d, (processorTime - lastProcessorTime).TotalSeconds)
                    / elapsed.TotalSeconds
                    / Math.Max(1, Environment.ProcessorCount)
                    * 100d;
            long allocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            using Process process = Process.GetCurrentProcess();

            AppLogger.LogKeyEvent(
                "Performance summary"
                + $" | mode={(isBackgroundMode ? "background" : "foreground")}"
                + $"; uptimeSec={Stopwatch.GetElapsedTime(StartedTimestamp, now).TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}"
                + $"; cpuPercent={cpuPercent.ToString("0.###", CultureInfo.InvariantCulture)}"
                + $"; workingSetBytes={process.WorkingSet64.ToString(CultureInfo.InvariantCulture)}"
                + $"; allocatedBytes={(allocatedBytes - lastAllocatedBytes).ToString(CultureInfo.InvariantCulture)}"
                + $"; gen0={(gen0 - lastGen0).ToString(CultureInfo.InvariantCulture)}"
                + $"; gen1={(gen1 - lastGen1).ToString(CultureInfo.InvariantCulture)}"
                + $"; gen2={(gen2 - lastGen2).ToString(CultureInfo.InvariantCulture)}"
                + $"; polling={Interlocked.Read(ref pollingCycles)}"
                + $"; pollingAvgMs={AverageMilliseconds(pollingDurationTicks, pollingCycles)}"
                + $"; lhmUpdates={Interlocked.Read(ref lhmUpdates)}"
                + $"; lhmAvgMs={AverageMilliseconds(lhmDurationTicks, lhmUpdates)}"
                + $"; wmiQueries={Interlocked.Read(ref wmiQueries)}"
                + $"; wmiCacheHits={Interlocked.Read(ref wmiCacheHits)}"
                + $"; wmiFailures={Interlocked.Read(ref wmiFailures)}"
                + $"; wmiAvgMs={AverageMilliseconds(wmiDurationTicks, wmiQueries)}"
                + $"; diskRefreshes={Interlocked.Read(ref diskRefreshes)}"
                + $"; diskSkips={Interlocked.Read(ref diskRefreshSkips)}"
                + $"; diskAvgMs={AverageMilliseconds(diskDurationTicks, diskRefreshes)}"
                + $"; networkRefreshes={Interlocked.Read(ref networkRefreshes)}"
                + $"; networkSkips={Interlocked.Read(ref networkRefreshSkips)}"
                + $"; networkAvgMs={AverageMilliseconds(networkDurationTicks, networkRefreshes)}"
                + $"; dashboardRefreshes={Interlocked.Read(ref dashboardRefreshes)}"
                + $"; gameStats={Interlocked.Read(ref gameStatisticsCalculations)}"
                + $"; gameStatsAvgMs={AverageMilliseconds(gameStatisticsDurationTicks, gameStatisticsCalculations)}"
                + $"; gameStatsLockAvgMs={AverageMilliseconds(gameStatisticsLockDurationTicks, gameStatisticsCalculations)}"
                + $"; presentMonRows={Interlocked.Read(ref presentMonRows)}"
                + $"; presentMonSamples={Interlocked.Read(ref presentMonSamples)}"
                + $"; presentMonFilteredRows={Interlocked.Read(ref presentMonFilteredRows)}"
                + $"; presentMonParses={Interlocked.Read(ref presentMonParses)}"
                + $"; presentMonParseAvgMs={AverageMilliseconds(presentMonParseDurationTicks, presentMonParses)}"
                + $"; energyTrackerInputs={Interlocked.Read(ref energyTrackerInputs)}"
                + $"; energyTrackerSnapshots={Interlocked.Read(ref energyTrackerSnapshots)}"
                + $"; limitTrackerInputs={Interlocked.Read(ref performanceLimitTrackerInputs)}"
                + $"; limitTrackerSnapshots={Interlocked.Read(ref performanceLimitTrackerSnapshots)}"
                + $"; warmupCandidates={Interlocked.Read(ref warmupCandidateSamples)}"
                + $"; warmupDiscarded={Interlocked.Read(ref warmupDiscardedSamples)}"
                + $"; invalidFrames={Interlocked.Read(ref invalidFrameSamples)}"
                + $"; nonPrimarySwapChain={Interlocked.Read(ref nonPrimarySwapChainSamples)}"
                + $"; deviceChanges={Interlocked.Read(ref deviceChangeNotifications)}"
                + $"; hardwareRefreshes={Interlocked.Read(ref hardwareRefreshSuccesses)}"
                + $"; hardwareRefreshFailures={Interlocked.Read(ref hardwareRefreshFailures)}"
                + $"; hardwareRefreshAvgMs={AverageMilliseconds(hardwareRefreshDurationTicks, hardwareRefreshSuccesses)}"
                + $"; frameUncompressedBytes={Interlocked.Read(ref frameUncompressedBytes)}"
                + $"; frameCompressedBytes={Interlocked.Read(ref frameCompressedBytes)}"
                + $"; compressionWriterAvgMs={AverageMilliseconds(compressionWriterDurationTicks, compressionWriterSessions)}"
                + $"; compressionQueueDrops={Interlocked.Read(ref compressionWriterQueueDrops)}");

            lastSummaryTimestamp = now;
            lastProcessorTime = processorTime;
            lastAllocatedBytes = allocatedBytes;
            lastGen0 = gen0;
            lastGen1 = gen1;
            lastGen2 = gen2;
        }
    }

    private static string AverageMilliseconds(long durationTicks, long count)
    {
        long currentCount = Interlocked.Read(ref count);
        if (currentCount <= 0)
        {
            return "0";
        }

        return (TimeSpan.FromTicks(Interlocked.Read(ref durationTicks)).TotalMilliseconds / currentCount)
            .ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static TimeSpan GetProcessorTime()
    {
        using Process process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }
}
