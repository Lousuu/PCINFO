namespace HardwareVision.Models;

public sealed class GameFrameSample
{
    public Guid CaptureSessionId { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public double? FrameTimeMs { get; init; }

    public double? Fps { get; init; }

    public double? CpuBusyMs { get; init; }

    public double? CpuWaitMs { get; init; }

    public double? GpuTimeMs { get; init; }

    public double? GpuBusyMs { get; init; }

    public double? GpuWaitMs { get; init; }

    public double? GpuLatencyMs { get; init; }

    public double? RenderLatencyMs { get; init; }

    public double? DisplayLatencyMs { get; init; }

    public double? ClickToPhotonLatencyMs { get; init; }

    public double? DisplayedTimeMs { get; init; }

    public string? SwapChainAddress { get; init; }

    public string? FrameType { get; init; }

    public string? Runtime { get; init; }

    public string? PresentMode { get; init; }
}
