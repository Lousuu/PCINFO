using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IGameEnergyTracker : IDisposable
{
    event EventHandler<GameEnergySnapshot>? SnapshotChanged;

    GameEnergySnapshot CurrentSnapshot { get; }

    void StartSession(GameSessionStartInfo startInfo);

    GameEnergySnapshot? CompleteSession(Guid captureSessionId, int generation);
}
