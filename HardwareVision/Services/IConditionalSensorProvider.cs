using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IConditionalSensorProvider : ISensorProvider
{
    Task<IReadOnlyList<SensorReading>> GetReadingsAsync(
        IReadOnlyList<SensorReading> higherPriorityReadings,
        CancellationToken cancellationToken = default);
}
