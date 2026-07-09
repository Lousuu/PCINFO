using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IGamePerformanceService : IDisposable
{
    event EventHandler<GameFrameSample>? FrameReceived;

    event EventHandler<string>? StatusChanged;

    bool IsCaptureAvailable { get; }

    string StatusText { get; }

    string? CaptureToolPath { get; }

    IReadOnlyList<GameFrameSample> RecentSamples { get; }

    Task<IReadOnlyList<GameProcessInfo>> GetCandidateProcessesAsync(CancellationToken cancellationToken = default);

    Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default);

    Task StopCaptureAsync(CancellationToken cancellationToken = default);

    Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default);
}
