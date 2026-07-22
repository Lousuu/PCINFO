using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SharedGpuHistoryTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Shared GPU history 01 polling records CPU and GPU", TestSupport.Run(PollingRecordsCpuAndGpuAsync)),
        ("Shared GPU history 02 GPU history grows without dashboard refresh", TestSupport.Run(GpuHistoryGrowsWithoutDashboardRefreshAsync)),
        ("Shared GPU history 03 multi GPU history is isolated by stable id", TestSupport.Run(MultiGpuHistoryIsIsolatedByStableIdAsync)),
        ("Shared GPU history 04 missing GPU does not pollute other device", TestSupport.Run(MissingGpuDoesNotPolluteOtherDeviceAsync)),
        ("Shared GPU history 05 dispose unsubscribes from polling", TestSupport.Run(DisposeUnsubscribesFromPollingAsync))
    ];

    private static async Task PollingRecordsCpuAndGpuAsync()
    {
        SequenceSensorService sensor = new(
            [
                [
                    CpuReading(SensorType.Load, "CPU Total", 25d, "/cpu/0/load/0"),
                    GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 66d, "/gpu/0/load/0")
                ]
            ]);
        AppSettings settings = new();
        using PollingService polling = new(sensor, settings);
        using SensorHistoryService history = new(polling);

        await polling.PollNowAsync();

        TestSupport.Equal(1, history.GetSnapshot(SensorHistoryMetric.CpuLoad).Count, "CPU history count");
        TestSupport.Equal(1, history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/0").Count, "GPU history count");
        TestSupport.Nearly(66d, history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/0").Single(), "GPU load value");
    }

    private static async Task GpuHistoryGrowsWithoutDashboardRefreshAsync()
    {
        SequenceSensorService sensor = new(
            [
                [GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 41d, "/gpu/0/load/0")],
                [GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 42d, "/gpu/0/load/0")],
                [GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 43d, "/gpu/0/load/0")]
            ]);
        using PollingService polling = new(sensor, new AppSettings());
        using SensorHistoryService history = new(polling);

        await polling.PollNowAsync();
        await polling.PollNowAsync();
        await polling.PollNowAsync();

        IReadOnlyList<double> snapshot = history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/0");
        TestSupport.Equal(3, snapshot.Count, "GPU history grows from polling only");
        TestSupport.True(snapshot.SequenceEqual([41d, 42d, 43d]), "GPU history order");
    }

    private static async Task MultiGpuHistoryIsIsolatedByStableIdAsync()
    {
        SequenceSensorService sensor = new(
            [
                [
                    GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 72d, "/gpu/0/load/0"),
                    GpuReading("Synthetic Intel GPU", SensorType.Load, "GPU Core Load", 18d, "/gpu/1/load/0")
                ],
                [
                    GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 74d, "/gpu/0/load/0"),
                    GpuReading("Synthetic Intel GPU", SensorType.Load, "GPU Core Load", 19d, "/gpu/1/load/0")
                ]
            ]);
        using PollingService polling = new(sensor, new AppSettings());
        using SensorHistoryService history = new(polling);

        await polling.PollNowAsync();
        await polling.PollNowAsync();

        TestSupport.True(history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/0").SequenceEqual([72d, 74d]), "discrete GPU bucket");
        TestSupport.True(history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/1").SequenceEqual([18d, 19d]), "integrated GPU bucket");
    }

    private static async Task MissingGpuDoesNotPolluteOtherDeviceAsync()
    {
        SequenceSensorService sensor = new(
            [
                [
                    GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 61d, "/gpu/0/load/0"),
                    GpuReading("Synthetic Intel GPU", SensorType.Load, "GPU Core Load", 17d, "/gpu/1/load/0")
                ],
                [GpuReading("Synthetic Intel GPU", SensorType.Load, "GPU Core Load", 18d, "/gpu/1/load/0")]
            ]);
        using PollingService polling = new(sensor, new AppSettings());
        using SensorHistoryService history = new(polling);

        await polling.PollNowAsync();
        await polling.PollNowAsync();

        TestSupport.True(history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/0").SequenceEqual([61d]), "missing GPU bucket unchanged");
        TestSupport.True(history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/1").SequenceEqual([17d, 18d]), "remaining GPU bucket grows");
    }

    private static async Task DisposeUnsubscribesFromPollingAsync()
    {
        SequenceSensorService sensor = new(
            [
                [GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 33d, "/gpu/0/load/0")],
                [GpuReading("Synthetic NVIDIA GPU", SensorType.Load, "GPU Core Load", 34d, "/gpu/0/load/0")]
            ]);
        using PollingService polling = new(sensor, new AppSettings());
        SensorHistoryService history = new(polling);

        await polling.PollNowAsync();
        history.Dispose();
        await polling.PollNowAsync();

        TestSupport.Equal(0, history.GetSnapshot(SensorHistoryMetric.GpuLoad, "/gpu/0").Count, "disposed history is empty");
    }

    private static SensorReading CpuReading(SensorType type, string sensorName, double value, string rawIdentifier) => new()
    {
        DeviceName = "Synthetic CPU",
        SensorName = sensorName,
        Category = SensorCategory.Cpu,
        Type = type,
        Value = value,
        Unit = "%",
        IsAvailable = true,
        Availability = SensorAvailability.Available,
        Source = "Synthetic",
        RawIdentifier = rawIdentifier
    };

    private static SensorReading GpuReading(string deviceName, SensorType type, string sensorName, double value, string rawIdentifier) => new()
    {
        DeviceName = deviceName,
        SensorName = sensorName,
        Category = SensorCategory.Gpu,
        Type = type,
        Value = value,
        Unit = type == SensorType.Load ? "%" : string.Empty,
        IsAvailable = true,
        Availability = SensorAvailability.Available,
        Source = "Synthetic",
        RawIdentifier = rawIdentifier
    };

    private sealed class SequenceSensorService(IReadOnlyList<IReadOnlyList<SensorReading>> batches) : ISensorService
    {
        private int nextIndex;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(Interlocked.Increment(ref nextIndex) - 1, batches.Count - 1);
            return Task.FromResult(batches[index]);
        }

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
        }
    }
}
