using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameHardwareTimelineSampler
{
    public static IReadOnlyList<GameHardwareTimelineSample> CreateSamples(
        GameSessionStartInfo session,
        IReadOnlyList<SensorReading> readings,
        GamePerformanceLimitSnapshot limits,
        DateTimeOffset timestamp,
        double elapsedSeconds)
    {
        CpuAccumulator cpu = new();
        MemoryAccumulator memory = new();
        Dictionary<string, GpuAccumulator> gpuById = new(StringComparer.OrdinalIgnoreCase);
        List<GpuAccumulator> gpuOrder = [];

        for (int index = 0; index < readings.Count; index++)
        {
            SensorReading reading = readings[index];
            if (!reading.IsAvailable || !reading.Value.HasValue || !double.IsFinite(reading.Value.Value)) continue;
            if (reading.Category == SensorCategory.Cpu)
            {
                cpu.Observe(reading);
            }
            else if (reading.Category == SensorCategory.Gpu)
            {
                if (!IsGpuTelemetry(reading)) continue;
                string deviceId = GetDeviceId(reading);
                if (!gpuById.TryGetValue(deviceId, out GpuAccumulator? gpu))
                {
                    gpu = new GpuAccumulator(deviceId, Clean(reading.DeviceName, "GPU"));
                    gpuById.Add(deviceId, gpu);
                    gpuOrder.Add(gpu);
                }

                gpu.Observe(reading);
            }
            else if (reading.Category == SensorCategory.Memory)
            {
                memory.Observe(reading);
            }
        }

        List<GameHardwareTimelineSample> result = new(gpuOrder.Count + 2);
        LimitState cpuLimit = ResolveLimit(limits, PerformanceLimitProcessorType.Cpu, "cpu", null, 1);
        if (cpu.HasData || cpuLimit.HasStatus)
        {
            result.Add(new GameHardwareTimelineSample
            {
                CaptureSessionId = session.CaptureSessionId,
                CaptureGeneration = session.Generation,
                Timestamp = timestamp,
                ElapsedSeconds = elapsedSeconds,
                DeviceType = GameTimelineDeviceType.Cpu,
                DeviceId = "cpu",
                DeviceName = Clean(session.HardwareMetadata?.CpuName, cpu.DeviceName ?? "CPU"),
                CpuAverageCoreClockMHz = cpu.CoreClockCount > 0 ? cpu.CoreClockSum / cpu.CoreClockCount : null,
                CpuEffectiveClockMHz = cpu.EffectiveClockCount > 0 ? cpu.EffectiveClockSum / cpu.EffectiveClockCount : null,
                CpuMaximumCoreClockMHz = cpu.CoreClockCount > 0 ? cpu.MaximumCoreClock : null,
                CpuLoadPercent = cpu.Load,
                CpuTemperatureCelsius = cpu.Temperature,
                CpuPackagePowerWatts = cpu.Power,
                CpuLimitActive = cpuLimit.IsActive,
                CpuLimitReasonCount = cpuLimit.Reasons.Count,
                CpuLimitReasons = cpuLimit.Reasons,
                CpuLimitSupportStatus = cpuLimit.Status
            });
        }

        for (int index = 0; index < gpuOrder.Count; index++)
        {
            GpuAccumulator gpu = gpuOrder[index];
            LimitState gpuLimit = ResolveLimit(
                limits,
                PerformanceLimitProcessorType.Gpu,
                gpu.DeviceId,
                gpu.DeviceName,
                gpuOrder.Count);
            result.Add(new GameHardwareTimelineSample
            {
                CaptureSessionId = session.CaptureSessionId,
                CaptureGeneration = session.Generation,
                Timestamp = timestamp,
                ElapsedSeconds = elapsedSeconds,
                DeviceType = GameTimelineDeviceType.Gpu,
                DeviceId = gpu.DeviceId,
                DeviceName = gpu.DeviceName,
                GpuCoreClockMHz = gpu.CoreClock,
                GpuMemoryClockMHz = gpu.MemoryClock,
                GpuLoadPercent = gpu.Load,
                GpuTemperatureCelsius = gpu.Temperature,
                GpuHotSpotTemperatureCelsius = gpu.HotSpotTemperature,
                GpuBoardPowerWatts = gpu.Power,
                GpuLimitActive = gpuLimit.IsActive,
                GpuLimitReasonCount = gpuLimit.Reasons.Count,
                GpuLimitReasons = gpuLimit.Reasons,
                GpuLimitSupportStatus = gpuLimit.Status
            });
        }

        if (memory.HasData)
        {
            result.Add(new GameHardwareTimelineSample
            {
                CaptureSessionId = session.CaptureSessionId,
                CaptureGeneration = session.Generation,
                Timestamp = timestamp,
                ElapsedSeconds = elapsedSeconds,
                DeviceType = GameTimelineDeviceType.Memory,
                DeviceId = "memory",
                DeviceName = "Memory",
                MemoryUsedBytes = memory.UsedBytes,
                MemoryLoadPercent = memory.Load
            });
        }

        return result;
    }

    private static LimitState ResolveLimit(
        GamePerformanceLimitSnapshot snapshot,
        PerformanceLimitProcessorType processorType,
        string? deviceId,
        string? deviceName,
        int deviceCount)
    {
        PerformanceLimitSupportStatus status = processorType == PerformanceLimitProcessorType.Cpu
            ? snapshot.CpuSupportStatus
            : snapshot.GpuSupportStatus;
        List<string> reasons = [];
        for (int index = 0; index < snapshot.Events.Count; index++)
        {
            GamePerformanceLimitEvent item = snapshot.Events[index];
            if (!item.IsActive
                || item.ProcessorType != processorType
                || !AppliesToDevice(item, deviceId, deviceName, deviceCount)) continue;
            for (int reasonIndex = 0; reasonIndex < item.Reasons.Count; reasonIndex++)
            {
                AddUnique(reasons, item.Reasons[reasonIndex]);
            }
        }

        bool hasStatus = status != PerformanceLimitSupportStatus.NotStarted;
        return new LimitState(
            hasStatus ? reasons.Count > 0 : null,
            reasons,
            hasStatus ? status : null,
            hasStatus);
    }

    private static bool AppliesToDevice(
        GamePerformanceLimitEvent item,
        string? deviceId,
        string? deviceName,
        int deviceCount)
    {
        if (item.ProcessorType == PerformanceLimitProcessorType.Cpu) return true;
        if (!string.IsNullOrWhiteSpace(item.DeviceId))
        {
            return string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
        }

        if (deviceCount <= 1) return true;
        if (item.Scopes.Count == 0) return false;

        for (int index = 0; index < item.Scopes.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(deviceId)
                && item.Scopes[index].Contains(deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(deviceName)
                && (item.Scopes[index].Contains(deviceName, StringComparison.OrdinalIgnoreCase)
                    || deviceName.Contains(item.Scopes[index], StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetDeviceId(SensorReading reading)
    {
        string raw = reading.RawIdentifier ?? string.Empty;
        string? stable = GpuDeviceIdentity.TryExtractStableKey(raw);
        if (!string.IsNullOrWhiteSpace(stable)) return stable;
        if (raw.StartsWith('/'))
        {
            int first = raw.IndexOf('/', 1);
            int second = first < 0 ? -1 : raw.IndexOf('/', first + 1);
            if (second > 0) return raw[..second];
        }

        return "gpu:" + Clean(reading.DeviceName, "unknown").ToLowerInvariant();
    }

    private static bool IsGpuTelemetry(SensorReading reading)
    {
        if (reading.Type is SensorType.Clock or SensorType.Load or SensorType.Temperature or SensorType.Power)
        {
            return true;
        }

        return reading.Type == SensorType.Data
            && (reading.SensorName.Contains("memory", StringComparison.OrdinalIgnoreCase)
                || reading.SensorName.Contains("vram", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ValidPositive(double value) => double.IsFinite(value) && value > 0d;
    private static bool ValidPercent(double value) => double.IsFinite(value) && value is >= 0d and <= 100d;
    private static bool ValidTemperature(double value) => double.IsFinite(value) && value is >= -50d and <= 150d;
    private static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static int Rank(string name, string primary, string secondary, string tertiary = "")
    {
        if (name.Contains(primary, StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains(secondary, StringComparison.OrdinalIgnoreCase)) return 80;
        if (tertiary.Length > 0 && name.Contains(tertiary, StringComparison.OrdinalIgnoreCase)) return 60;
        return 10;
    }

    private static void AddUnique(List<string> values, string value)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.OrdinalIgnoreCase)) return;
        }

        values.Add(value);
    }

    private readonly record struct LimitState(
        bool? IsActive,
        IReadOnlyList<string> Reasons,
        PerformanceLimitSupportStatus? Status,
        bool HasStatus);

    private sealed class CpuAccumulator
    {
        private int loadRank;
        private int temperatureRank;
        private int powerRank;

        public string? DeviceName { get; private set; }
        public double CoreClockSum { get; private set; }
        public int CoreClockCount { get; private set; }
        public double EffectiveClockSum { get; private set; }
        public int EffectiveClockCount { get; private set; }
        public double MaximumCoreClock { get; private set; }
        public double? Load;
        public double? Temperature;
        public double? Power;
        public bool HasData => CoreClockCount > 0 || EffectiveClockCount > 0 || Load.HasValue || Temperature.HasValue || Power.HasValue;

        public void Observe(SensorReading reading)
        {
            DeviceName ??= Clean(reading.DeviceName, "CPU");
            string name = reading.SensorName ?? string.Empty;
            double value = reading.Value!.Value;
            if (reading.Type == SensorType.Clock && ValidPositive(value))
            {
                if (name.Contains("bus", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("bclk", StringComparison.OrdinalIgnoreCase)) return;
                if (name.Contains("effective", StringComparison.OrdinalIgnoreCase))
                {
                    EffectiveClockSum += value;
                    EffectiveClockCount++;
                }
                else if (name.Contains("core", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("memory", StringComparison.OrdinalIgnoreCase))
                {
                    CoreClockSum += value;
                    CoreClockCount++;
                    MaximumCoreClock = Math.Max(MaximumCoreClock, value);
                }

                return;
            }

            if (reading.Type == SensorType.Load && ValidPercent(value))
            {
                Select(value, Rank(name, "total", "package", "cpu"), ref Load, ref loadRank);
            }
            else if (reading.Type == SensorType.Temperature && ValidTemperature(value))
            {
                Select(value, Rank(name, "package", "core max", "tctl"), ref Temperature, ref temperatureRank);
            }
            else if (reading.Type == SensorType.Power && ValidPositive(value))
            {
                Select(value, Rank(name, "package", "total", "cpu"), ref Power, ref powerRank);
            }
        }
    }

    private sealed class GpuAccumulator(string deviceId, string deviceName)
    {
        private int coreClockRank;
        private int memoryClockRank;
        private int loadRank;
        private int temperatureRank;
        private int hotSpotRank;
        private int powerRank;

        public string DeviceId { get; } = deviceId;
        public string DeviceName { get; } = deviceName;
        public double? CoreClock;
        public double? MemoryClock;
        public double? Load;
        public double? Temperature;
        public double? HotSpotTemperature;
        public double? Power;

        public void Observe(SensorReading reading)
        {
            string name = reading.SensorName ?? string.Empty;
            double value = reading.Value!.Value;
            if (reading.Type == SensorType.Clock && ValidPositive(value))
            {
                if (name.Contains("memory", StringComparison.OrdinalIgnoreCase))
                {
                    Select(value, Rank(name, "memory", "vram"), ref MemoryClock, ref memoryClockRank);
                }
                else if (name.Contains("core", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("graphics", StringComparison.OrdinalIgnoreCase))
                {
                    Select(value, Rank(name, "core", "graphics"), ref CoreClock, ref coreClockRank);
                }
            }
            else if (reading.Type == SensorType.Load && ValidPercent(value))
            {
                Select(value, Rank(name, "core", "gpu", "3d"), ref Load, ref loadRank);
            }
            else if (reading.Type == SensorType.Temperature && ValidTemperature(value))
            {
                if (name.Contains("hot", StringComparison.OrdinalIgnoreCase))
                {
                    Select(value, 100, ref HotSpotTemperature, ref hotSpotRank);
                }
                else
                {
                    Select(value, Rank(name, "core", "gpu"), ref Temperature, ref temperatureRank);
                }
            }
            else if (reading.Type == SensorType.Power && ValidPositive(value))
            {
                Select(value, Rank(name, "board", "total", "package"), ref Power, ref powerRank);
            }
        }
    }

    private sealed class MemoryAccumulator
    {
        private int loadRank;
        private int usedRank;

        public double? UsedBytes;
        public double? Load;
        public bool HasData => UsedBytes.HasValue || Load.HasValue;

        public void Observe(SensorReading reading)
        {
            string name = reading.SensorName ?? string.Empty;
            double value = reading.Value!.Value;
            if (reading.Type == SensorType.Load && ValidPercent(value))
            {
                Select(value, Rank(name, "memory", "used", "load"), ref Load, ref loadRank);
            }
            else if (reading.Type == SensorType.Data
                && name.Contains("used", StringComparison.OrdinalIgnoreCase)
                && TryConvertToBytes(value, reading.Unit, out double bytes))
            {
                Select(bytes, Rank(name, "memory used", "used"), ref UsedBytes, ref usedRank);
            }
        }

        private static bool TryConvertToBytes(double value, string? unit, out double bytes)
        {
            bytes = 0d;
            if (!double.IsFinite(value) || value < 0d) return false;
            string normalized = unit?.Trim() ?? string.Empty;
            double multiplier = normalized.ToUpperInvariant() switch
            {
                "B" or "BYTE" or "BYTES" => 1d,
                "KB" => 1_000d,
                "KIB" => 1_024d,
                "MB" => 1_000_000d,
                "MIB" => 1_048_576d,
                "GB" => 1_000_000_000d,
                "GIB" => 1_073_741_824d,
                _ => 0d
            };
            if (multiplier == 0d) return false;
            bytes = value * multiplier;
            return double.IsFinite(bytes);
        }
    }

    private static void Select(double value, int rank, ref double? target, ref int targetRank)
    {
        if (rank <= targetRank) return;
        target = value;
        targetRank = rank;
    }
}
