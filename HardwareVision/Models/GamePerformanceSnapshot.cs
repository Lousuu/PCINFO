namespace HardwareVision.Models;

public sealed class GamePerformanceSnapshot
{
    public int SampleCount { get; init; }

    public double? CurrentFps { get; init; }

    public double? AverageFps { get; init; }

    public double? OnePercentLowFps { get; init; }

    public double? ZeroPointOnePercentLowFps { get; init; }

    public double? AverageFrameTimeMs { get; init; }

    public double? AverageCpuBusyMs { get; init; }

    public double? AverageGpuTimeMs { get; init; }

    public double? AverageDisplayLatencyMs { get; init; }
}
