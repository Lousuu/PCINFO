using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class SensorHistoryService : ISensorHistoryService
{
    public const int MaximumPoints = 240;

    private sealed class SensorHistorySeries
    {
        private readonly object syncRoot = new();
        private readonly double[] values = new double[MaximumPoints];
        private int writeIndex;
        private int count;

        public void Append(double? value)
        {
            if (!value.HasValue || !double.IsFinite(value.Value))
            {
                return;
            }

            lock (syncRoot)
            {
                values[writeIndex] = value.Value;
                writeIndex = (writeIndex + 1) % values.Length;
                count = Math.Min(count + 1, values.Length);
            }
        }

        public IReadOnlyList<double> Snapshot(int maximumPoints)
        {
            lock (syncRoot)
            {
                int snapshotCount = Math.Min(count, Math.Clamp(maximumPoints, 1, values.Length));
                if (snapshotCount == 0)
                {
                    return Array.Empty<double>();
                }

                double[] snapshot = new double[snapshotCount];
                int start = (writeIndex - snapshotCount + values.Length) % values.Length;
                for (int index = 0; index < snapshotCount; index++)
                {
                    snapshot[index] = values[(start + index) % values.Length];
                }

                return snapshot;
            }
        }
    }

    private readonly PollingService? pollingService;
    private readonly Dictionary<SensorHistoryMetric, SensorHistorySeries> series;
    private readonly Dictionary<string, Dictionary<SensorHistoryMetric, SensorHistorySeries>> gpuSeriesByDeviceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly GpuDeviceService gpuDeviceService = new();
    private bool isDisposed;

    public SensorHistoryService()
        : this(pollingService: null)
    {
    }

    public SensorHistoryService(PollingService? pollingService)
    {
        this.pollingService = pollingService;
        series = Enum.GetValues<SensorHistoryMetric>()
            .ToDictionary(static metric => metric, static _ => new SensorHistorySeries());
        if (pollingService is not null)
        {
            pollingService.ReadingsUpdated += OnReadingsUpdated;
        }
    }

    public void RecordGpu(GpuDevice? gpu)
    {
        if (isDisposed || gpu is null)
        {
            return;
        }

        AppendGpu(gpu, updateDefaultSeries: true);
    }

    public void RecordDisk(DiskDevice? disk)
    {
        if (isDisposed || disk is null)
        {
            return;
        }

        Append(SensorHistoryMetric.DiskRead, disk.ReadSpeed);
        Append(SensorHistoryMetric.DiskWrite, disk.WriteSpeed);
    }

    public void RecordNetwork(NetworkAdapterDevice? adapter)
    {
        if (isDisposed || adapter is null)
        {
            return;
        }

        Append(SensorHistoryMetric.NetworkUpload, adapter.UploadSpeed);
        Append(SensorHistoryMetric.NetworkDownload, adapter.DownloadSpeed);
    }

    public IReadOnlyList<double> GetSnapshot(SensorHistoryMetric metric, int maximumPoints = MaximumPoints)
    {
        return isDisposed ? Array.Empty<double>() : series[metric].Snapshot(maximumPoints);
    }

    public IReadOnlyList<double> GetSnapshot(SensorHistoryMetric metric, string? deviceId, int maximumPoints = MaximumPoints)
    {
        if (isDisposed || string.IsNullOrWhiteSpace(deviceId))
        {
            return GetSnapshot(metric, maximumPoints);
        }

        lock (gpuSeriesByDeviceId)
        {
            return gpuSeriesByDeviceId.TryGetValue(deviceId, out Dictionary<SensorHistoryMetric, SensorHistorySeries>? deviceSeries)
                && deviceSeries.TryGetValue(metric, out SensorHistorySeries? metricSeries)
                    ? metricSeries.Snapshot(maximumPoints)
                    : Array.Empty<double>();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        if (pollingService is not null)
        {
            pollingService.ReadingsUpdated -= OnReadingsUpdated;
        }
    }

    internal void RecordSensorReadings(IReadOnlyList<SensorReading> readings)
    {
        if (isDisposed)
        {
            return;
        }

        Append(SensorHistoryMetric.CpuLoad, HardwareDetail(readings, SensorType.Load, "Total")?.Value);
        Append(SensorHistoryMetric.CpuTemperature, HardwareDetail(readings, SensorType.Temperature, "Package", "CPU")?.Value);
        Append(SensorHistoryMetric.CpuPower, HardwareDetail(readings, SensorType.Power, "Package", "CPU")?.Value);
        Append(SensorHistoryMetric.CpuClock, AverageCpuClock(readings));
        RecordGpuReadings(readings);
    }

    private void OnReadingsUpdated(object? sender, SensorReadingsUpdatedEventArgs e)
    {
        RecordSensorReadings(e.Readings);
    }

    private void Append(SensorHistoryMetric metric, double? value)
    {
        series[metric].Append(value);
    }

    private void RecordGpuReadings(IReadOnlyList<SensorReading> readings)
    {
        IReadOnlyList<GpuDevice> devices = gpuDeviceService.BuildGpuDevices(null, readings, null);
        for (int index = 0; index < devices.Count; index++)
        {
            AppendGpu(devices[index], updateDefaultSeries: index == 0);
        }
    }

    private void AppendGpu(GpuDevice gpu, bool updateDefaultSeries)
    {
        AppendGpuMetric(gpu, SensorHistoryMetric.GpuLoad, gpu.CoreLoad?.Value, updateDefaultSeries);
        AppendGpuMetric(gpu, SensorHistoryMetric.GpuTemperature, gpu.TemperatureCore?.Value, updateDefaultSeries);
        AppendGpuMetric(gpu, SensorHistoryMetric.GpuPower, gpu.PowerPackage?.Value, updateDefaultSeries);
        AppendGpuMetric(gpu, SensorHistoryMetric.GpuClock, gpu.CoreClock?.Value, updateDefaultSeries);
        AppendGpuMetric(gpu, SensorHistoryMetric.GpuMemoryUsed, gpu.MemoryUsed?.Value, updateDefaultSeries);
    }

    private void AppendGpuMetric(GpuDevice gpu, SensorHistoryMetric metric, double? value, bool updateDefaultSeries)
    {
        if (updateDefaultSeries)
        {
            Append(metric, value);
        }

        if (string.IsNullOrWhiteSpace(gpu.Id) || !value.HasValue || !double.IsFinite(value.Value))
        {
            return;
        }

        SensorHistorySeries? metricSeries;
        lock (gpuSeriesByDeviceId)
        {
            if (!gpuSeriesByDeviceId.TryGetValue(gpu.Id, out Dictionary<SensorHistoryMetric, SensorHistorySeries>? deviceSeries))
            {
                deviceSeries = new Dictionary<SensorHistoryMetric, SensorHistorySeries>();
                gpuSeriesByDeviceId[gpu.Id] = deviceSeries;
            }

            if (!deviceSeries.TryGetValue(metric, out metricSeries))
            {
                metricSeries = new SensorHistorySeries();
                deviceSeries[metric] = metricSeries;
            }
        }

        metricSeries.Append(value);
    }

    private static SensorReading? HardwareDetail(
        IReadOnlyList<SensorReading> readings,
        SensorType type,
        params string[] preferredTerms)
    {
        SensorReading? fallback = null;
        for (int index = 0; index < readings.Count; index++)
        {
            SensorReading reading = readings[index];
            if (reading.Category != SensorCategory.Cpu || reading.Type != type || !reading.IsAvailable)
            {
                continue;
            }

            fallback ??= reading;
            for (int termIndex = 0; termIndex < preferredTerms.Length; termIndex++)
            {
                if (reading.SensorName.Contains(preferredTerms[termIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return reading;
                }
            }
        }

        return fallback;
    }

    private static double? AverageCpuClock(IReadOnlyList<SensorReading> readings)
    {
        double sum = 0d;
        int count = 0;
        for (int index = 0; index < readings.Count; index++)
        {
            SensorReading reading = readings[index];
            if (reading.Category != SensorCategory.Cpu
                || reading.Type != SensorType.Clock
                || !reading.IsAvailable
                || reading.Value is not > 0d
                || IsBusClock(reading))
            {
                continue;
            }

            sum += reading.Value.Value;
            count++;
        }

        return count == 0 ? null : sum / count;
    }

    private static bool IsBusClock(SensorReading reading)
    {
        string text = $"{reading.SensorName} {reading.RawIdentifier}";
        return text.Contains("Bus", StringComparison.OrdinalIgnoreCase)
            || text.Contains("BCLK", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Base Clock", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Reference Clock", StringComparison.OrdinalIgnoreCase);
    }
}
