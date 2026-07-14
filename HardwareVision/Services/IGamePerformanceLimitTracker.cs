using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IGamePerformanceLimitTracker : IDisposable
{
    event EventHandler<GamePerformanceLimitSnapshot>? SnapshotChanged;

    GamePerformanceLimitSnapshot CurrentSnapshot { get; }

    void StartSession(GameSessionStartInfo startInfo);

    GamePerformanceLimitSnapshot? CompleteSession(Guid captureSessionId, int generation);
}
