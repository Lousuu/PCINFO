using HardwareVision.Models;

namespace HardwareVision.Services;

public static class GameFrameStatisticsCalculator
{
    public static GamePerformanceSnapshot Calculate(IEnumerable<GameFrameSample> samples, TimeSpan window)
    {
        DateTimeOffset cutoff = DateTimeOffset.Now - window;
        GameFrameSample[] windowSamples = samples
            .Where(sample => sample.Timestamp >= cutoff)
            .ToArray();

        double[] frameTimes = windowSamples
            .Select(sample => sample.FrameTimeMs)
            .Where(IsValidPositive)
            .Select(value => value!.Value)
            .ToArray();

        return new GamePerformanceSnapshot
        {
            SampleCount = frameTimes.Length,
            CurrentFps = windowSamples.LastOrDefault(sample => IsValidPositive(sample.Fps))?.Fps,
            AverageFps = FrameTimeToFps(Average(frameTimes)),
            OnePercentLowFps = CalculateLowFps(frameTimes, 0.01d, 100),
            ZeroPointOnePercentLowFps = CalculateLowFps(frameTimes, 0.001d, 1000),
            AverageFrameTimeMs = Average(frameTimes),
            AverageCpuBusyMs = Average(windowSamples.Select(sample => sample.CpuBusyMs).Where(IsValidPositive).Select(value => value!.Value)),
            AverageGpuTimeMs = Average(windowSamples.Select(sample => sample.GpuTimeMs).Where(IsValidPositive).Select(value => value!.Value)),
            AverageLatencyMs = Average(windowSamples.Select(ResolveLatency).Where(IsValidPositive).Select(value => value!.Value))
        };
    }

    private static double? CalculateLowFps(IReadOnlyCollection<double> frameTimes, double percentile, int minimumSamples)
    {
        if (frameTimes.Count < minimumSamples)
        {
            return null;
        }

        int lowSampleCount = Math.Max(1, (int)Math.Ceiling(frameTimes.Count * percentile));

        // Lower FPS is derived from the slowest frame times, sorted slow-to-fast and averaged before converting to FPS.
        double averageSlowFrameTime = frameTimes
            .OrderByDescending(value => value)
            .Take(lowSampleCount)
            .Average();

        return FrameTimeToFps(averageSlowFrameTime);
    }

    private static double? ResolveLatency(GameFrameSample sample)
    {
        return FirstValid(sample.ClickToPhotonLatencyMs, sample.DisplayLatencyMs, sample.RenderLatencyMs);
    }

    private static double? FirstValid(params double?[] values)
    {
        foreach (double? value in values)
        {
            if (IsValidPositive(value))
            {
                return value;
            }
        }

        return null;
    }

    private static double? Average(IEnumerable<double> values)
    {
        double[] array = values.ToArray();
        return array.Length == 0 ? null : array.Average();
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
}
