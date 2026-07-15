using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IGameSessionReportService
{
    Task<GameSessionReport> LoadAsync(GameSessionRecordInfo record, CancellationToken cancellationToken = default);
}
