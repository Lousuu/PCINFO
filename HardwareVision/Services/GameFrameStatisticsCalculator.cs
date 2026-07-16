using HardwareVision.Models;

namespace HardwareVision.Services;

public static class GameFrameStatisticsCalculator
{
    private const double CurrentFpsWindowFrameTimeMs = 1_000d;
    private const int MinimumCurrentFpsSamples = 10;
    private const int MinimumAverageFpsSamples = 2;

    public static GamePerformanceSnapshot Calculate(IEnumerable<GameFrameSample> samples, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(samples);
        IReadOnlyList<GameFrameSample> sampleList = samples as IReadOnlyList<GameFrameSample> ?? samples.ToArray();
        return Calculate(sampleList, window);
    }

    public static GamePerformanceSnapshot Calculate(
        IReadOnlyList<GameFrameSample> samples,
        TimeSpan window,
        Guid? captureSessionId = null)
    {
        return Calculate(samples, window, captureSessionId, cachedLowFps: null);
    }

    internal static GamePerformanceSnapshot Calculate(
        IReadOnlyList<GameFrameSample> samples,
        TimeSpan window,
        Guid? captureSessionId,
        GamePerformanceSnapshot? cachedLowFps)
    {
        ArgumentNullException.ThrowIfNull(samples);
        DateTimeOffset cutoff = DateTimeOffset.Now - window;
        int frameCount = 0;
        double frameTimeSum = 0d;
        int cpuBusyCount = 0;
        double cpuBusySum = 0d;
        int gpuTimeCount = 0;
        double gpuTimeSum = 0d;
        int displayLatencyCount = 0;
        double displayLatencySum = 0d;

        for (int index = 0; index < samples.Count; index++)
        {
            GameFrameSample sample = samples[index];
            if (sample.Timestamp < cutoff
                || captureSessionId.HasValue && sample.CaptureSessionId != captureSessionId.Value
                || !IsValidPositive(sample.FrameTimeMs))
            {
                continue;
            }

            frameCount++;
            frameTimeSum += sample.FrameTimeMs!.Value;
            Accumulate(sample.CpuBusyMs, ref cpuBusySum, ref cpuBusyCount);
            Accumulate(sample.GpuTimeMs, ref gpuTimeSum, ref gpuTimeCount);
            Accumulate(sample.DisplayLatencyMs, ref displayLatencySum, ref displayLatencyCount);
        }

        double? averageFrameTime = Average(frameTimeSum, frameCount);
        (double? onePercentLow, double? zeroPointOnePercentLow) = cachedLowFps is null
            ? CalculateLowFpsValues(samples, cutoff, captureSessionId, frameCount)
            : (cachedLowFps.OnePercentLowFps, cachedLowFps.ZeroPointOnePercentLowFps);
        return new GamePerformanceSnapshot
        {
            SampleCount = frameCount,
            CurrentFps = CalculateCurrentFps(samples, cutoff, captureSessionId),
            AverageFps = frameCount >= MinimumAverageFpsSamples ? FrameTimeToFps(averageFrameTime) : null,
            OnePercentLowFps = onePercentLow,
            ZeroPointOnePercentLowFps = zeroPointOnePercentLow,
            AverageFrameTimeMs = averageFrameTime,
            AverageCpuBusyMs = Average(cpuBusySum, cpuBusyCount),
            AverageGpuTimeMs = Average(gpuTimeSum, gpuTimeCount),
            AverageDisplayLatencyMs = Average(displayLatencySum, displayLatencyCount)
        };
    }

    private static double? CalculateCurrentFps(
        IReadOnlyList<GameFrameSample> samples,
        DateTimeOffset cutoff,
        Guid? captureSessionId)
    {
        double frameTimeSum = 0d;
        int frameCount = 0;
        for (int index = samples.Count - 1; index >= 0; index--)
        {
            GameFrameSample sample = samples[index];
            if (sample.Timestamp < cutoff)
            {
                break;
            }

            if (captureSessionId.HasValue && sample.CaptureSessionId != captureSessionId.Value
                || !IsValidPositive(sample.FrameTimeMs))
            {
                continue;
            }

            frameTimeSum += sample.FrameTimeMs!.Value;
            frameCount++;
            if (frameTimeSum >= CurrentFpsWindowFrameTimeMs)
            {
                break;
            }
        }

        return frameCount >= MinimumCurrentFpsSamples
            ? FrameTimeToFps(Average(frameTimeSum, frameCount))
            : null;
    }

    private static (double? OnePercentLow, double? ZeroPointOnePercentLow) CalculateLowFpsValues(
        IReadOnlyList<GameFrameSample> samples,
        DateTimeOffset cutoff,
        Guid? captureSessionId,
        int frameCount)
    {
        int onePercentCount = frameCount >= 100
            ? Math.Max(1, (int)Math.Ceiling(frameCount * 0.01d))
            : 0;
        int zeroPointOnePercentCount = frameCount >= 1000
            ? Math.Max(1, (int)Math.Ceiling(frameCount * 0.001d))
            : 0;
        if (onePercentCount == 0 && zeroPointOnePercentCount == 0)
        {
            return (null, null);
        }

        PriorityQueue<double, double>? onePercentSlowest = onePercentCount > 0
            ? new PriorityQueue<double, double>(onePercentCount + 1)
            : null;
        PriorityQueue<double, double>? zeroPointOnePercentSlowest = zeroPointOnePercentCount > 0
            ? new PriorityQueue<double, double>(zeroPointOnePercentCount + 1)
            : null;

        for (int index = 0; index < samples.Count; index++)
        {
            GameFrameSample sample = samples[index];
            if (sample.Timestamp < cutoff
                || captureSessionId.HasValue && sample.CaptureSessionId != captureSessionId.Value
                || !IsValidPositive(sample.FrameTimeMs))
            {
                continue;
            }

            double frameTime = sample.FrameTimeMs!.Value;
            AddSlowFrame(onePercentSlowest, onePercentCount, frameTime);
            AddSlowFrame(zeroPointOnePercentSlowest, zeroPointOnePercentCount, frameTime);
        }

        // Low FPS is the mean of the slowest percentile of frame times, converted back to FPS.
        return (
            SlowFrameMeanToFps(onePercentSlowest),
            SlowFrameMeanToFps(zeroPointOnePercentSlowest));
    }

    private static void AddSlowFrame(PriorityQueue<double, double>? frames, int maximumCount, double frameTime)
    {
        if (frames is null)
        {
            return;
        }

        frames.Enqueue(frameTime, frameTime);
        if (frames.Count > maximumCount)
        {
            frames.Dequeue();
        }
    }

    private static double? SlowFrameMeanToFps(PriorityQueue<double, double>? frames)
    {
        return frames is null || frames.Count == 0
            ? null
            : FrameTimeToFps(frames.UnorderedItems.Average(item => item.Element));
    }

    private static void Accumulate(double? value, ref double sum, ref int count)
    {
        if (IsValidNonNegative(value))
        {
            sum += value!.Value;
            count++;
        }
    }

    private static double? Average(double sum, int count)
    {
        return count == 0 ? null : sum / count;
    }

    private static double? FrameTimeToFps(double? frameTimeMs)
    {
        return IsValidPositive(frameTimeMs) ? 1000d / frameTimeMs.GetValueOrDefault() : null;
    }

    private static bool IsValidPositive(double? value)
    {
        return value.HasValue
            && value.Value > 0d
            && !double.IsNaN(value.Value)
            && !double.IsInfinity(value.Value);
    }

    private static bool IsValidNonNegative(double? value)
    {
        return value.HasValue
            && value.Value >= 0d
            && double.IsFinite(value.Value);
    }
}
