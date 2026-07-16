using HardwareVision.Models;

namespace HardwareVision.Services;

public interface ISensorHistoryService : IDisposable
{
    void RecordGpu(GpuDevice? gpu);

    void RecordDisk(DiskDevice? disk);

    void RecordNetwork(NetworkAdapterDevice? adapter);

    IReadOnlyList<double> GetSnapshot(SensorHistoryMetric metric, int maximumPoints = 240);
}
