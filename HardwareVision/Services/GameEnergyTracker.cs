using System.Diagnostics;
using System.Text;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class GameEnergyTracker : IGameEnergyTracker
{
    private static readonly string[] ExcludedPowerSensorTokens =
    [
        "limit", "tdp", "ppt", "core", "ccd", "uncore", "dram", "soc"
    ];
    private const double MaximumComponentPowerWatts = 2_000d;
    private const int MaximumMetadataCacheEntries = 512;
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(500);
    private readonly object stateLock = new();
    private readonly PollingService? pollingService;
    private readonly Func<long> getTimestamp;
    private readonly double timestampFrequency;
    private readonly long snapshotIntervalTicks;
    private readonly TimeSpan foregroundMaximumGap;
    private readonly TimeSpan backgroundMaximumGap;
    private readonly Dictionary<string, CandidateMetadata> metadataCache = new(StringComparer.Ordinal);
    private SessionState? currentSession;
    private GameEnergySnapshot currentSnapshot = GameEnergySnapshot.Empty;
    private volatile bool isDisposed;

    public GameEnergyTracker(PollingService pollingService, AppSettings settings)
        : this(
            pollingService,
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency,
            CalculateMaximumGap(settings.RefreshIntervalSeconds, 3d),
            CalculateMaximumGap(settings.BackgroundRefreshIntervalSeconds, 15d))
    {
    }

    internal GameEnergyTracker(
        PollingService? pollingService,
        Func<long> getTimestamp,
        long timestampFrequency,
        TimeSpan foregroundMaximumGap,
        TimeSpan backgroundMaximumGap)
    {
        this.pollingService = pollingService;
        this.getTimestamp = getTimestamp;
        this.timestampFrequency = Math.Max(1d, timestampFrequency);
        snapshotIntervalTicks = Math.Max(1L, (long)Math.Ceiling(this.timestampFrequency * SnapshotInterval.TotalSeconds));
        this.foregroundMaximumGap = foregroundMaximumGap > TimeSpan.Zero
            ? foregroundMaximumGap
            : TimeSpan.FromSeconds(3);
        this.backgroundMaximumGap = backgroundMaximumGap > TimeSpan.Zero
            ? backgroundMaximumGap
            : TimeSpan.FromSeconds(15);
        if (pollingService is not null)
        {
            pollingService.ReadingsUpdated += OnReadingsUpdated;
        }
    }

    public event EventHandler<GameEnergySnapshot>? SnapshotChanged;

    public GameEnergySnapshot CurrentSnapshot
    {
        get
        {
            lock (stateLock)
            {
                return currentSnapshot;
            }
        }
    }

    public void StartSession(GameSessionStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        GameEnergySnapshot snapshot;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            long now = getTimestamp();
            currentSession = new SessionState(startInfo.CaptureSessionId, startInfo.Generation, now);
            snapshot = currentSnapshot = CreateSnapshot(currentSession, now, isTracking: true);
        }

        Publish(snapshot);
    }

    public GameEnergySnapshot? CompleteSession(Guid captureSessionId, int generation)
    {
        GameEnergySnapshot snapshot;
        lock (stateLock)
        {
            if (currentSession is null
                || currentSession.CaptureSessionId != captureSessionId
                || currentSession.Generation != generation)
            {
                return null;
            }

            if (!currentSession.IsTracking)
            {
                return currentSnapshot;
            }

            long now = getTimestamp();
            currentSession.IsTracking = false;
            currentSession.CompletedTimestamp = now;
            snapshot = currentSnapshot = CreateSnapshot(currentSession, now, isTracking: false);
        }

        Publish(snapshot);
        return snapshot;
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            currentSession = null;
        }

        if (pollingService is not null)
        {
            pollingService.ReadingsUpdated -= OnReadingsUpdated;
        }
    }

    internal void RecordReadings(
        Guid captureSessionId,
        int generation,
        IReadOnlyList<SensorReading> readings,
        long timestamp,
        bool isBackgroundMode)
    {
        ArgumentNullException.ThrowIfNull(readings);
        GameEnergySnapshot? snapshot = null;
        lock (stateLock)
        {
            SessionState? session = currentSession;
            if (isDisposed
                || session is null
                || !session.IsTracking
                || session.CaptureSessionId != captureSessionId
                || session.Generation != generation)
            {
                return;
            }

            RuntimePerformanceDiagnostics.RecordEnergyTrackerInput();
            SelectPowerComponents(readings, session);
            ApplyComponents(
                session,
                timestamp,
                isBackgroundMode ? backgroundMaximumGap : foregroundMaximumGap);
            if (!session.LastSnapshotTimestamp.HasValue
                || timestamp - session.LastSnapshotTimestamp.Value >= snapshotIntervalTicks)
            {
                session.LastSnapshotTimestamp = timestamp;
                snapshot = currentSnapshot = CreateSnapshot(session, timestamp, isTracking: true);
            }
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    internal static SelectedPowerSample? SelectPowerSample(IEnumerable<SensorReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        List<SelectedPowerComponent> selected = [];
        Dictionary<string, int> indexes = new(StringComparer.OrdinalIgnoreCase);
        foreach (SensorReading reading in readings)
        {
            Candidate? candidate = CreateCandidate(reading);
            if (!candidate.HasValue)
            {
                continue;
            }

            Candidate value = candidate.Value;
            if (!indexes.TryGetValue(value.DeviceKey, out int selectedIndex))
            {
                indexes.Add(value.DeviceKey, selected.Count);
                selected.Add(value.ToSelected());
            }
            else if (IsBetterCandidate(value, selected[selectedIndex]))
            {
                selected[selectedIndex] = value.ToSelected();
            }
        }

        if (selected.Count == 0)
        {
            return null;
        }

        double total = 0d;
        for (int index = 0; index < selected.Count; index++)
        {
            total += selected[index].PowerWatts;
        }

        return double.IsFinite(total) && total >= 0d
            ? new SelectedPowerSample(total, BuildComponentText(selected))
            : null;
    }

    private void OnReadingsUpdated(object? sender, SensorReadingsUpdatedEventArgs e)
    {
        Guid sessionId;
        int generation;
        lock (stateLock)
        {
            if (currentSession is null || !currentSession.IsTracking || isDisposed)
            {
                return;
            }

            sessionId = currentSession.CaptureSessionId;
            generation = currentSession.Generation;
        }

        RecordReadings(sessionId, generation, e.Readings, getTimestamp(), e.IsBackgroundMode);
    }

    private void SelectPowerComponents(IReadOnlyList<SensorReading> readings, SessionState session)
    {
        session.SelectedComponents.Clear();
        session.SelectionIndexes.Clear();
        session.ResetMetricCandidates();
        for (int index = 0; index < readings.Count; index++)
        {
            SensorReading reading = readings[index];
            ConsiderSessionMetric(session, reading);
            if (!TryCreateCandidate(reading, out Candidate candidate))
            {
                continue;
            }

            if (!session.SelectionIndexes.TryGetValue(candidate.DeviceKey, out int selectedIndex))
            {
                session.SelectionIndexes.Add(candidate.DeviceKey, session.SelectedComponents.Count);
                session.SelectedComponents.Add(candidate.ToSelected());
            }
            else if (IsBetterCandidate(candidate, session.SelectedComponents[selectedIndex]))
            {
                session.SelectedComponents[selectedIndex] = candidate.ToSelected();
            }
        }

        session.CommitMetricCandidates();
    }

    private static void ConsiderSessionMetric(SessionState session, SensorReading reading)
    {
        if (!reading.IsAvailable
            || !reading.Value.HasValue
            || !double.IsFinite(reading.Value.Value))
        {
            return;
        }

        double value = reading.Value.Value;
        if (reading.Type == SensorType.Load && value is >= 0d and <= 100d)
        {
            if (reading.Category == SensorCategory.Cpu)
            {
                session.ConsiderCpuLoad(value, RankMetric(reading.SensorName, "total", "package", "cpu"));
            }
            else if (reading.Category == SensorCategory.Gpu)
            {
                session.ConsiderGpuLoad(value, RankMetric(reading.SensorName, "core", "gpu", "3d"));
            }
            else if (reading.Category == SensorCategory.Memory)
            {
                session.ConsiderMemoryLoad(value, RankMetric(reading.SensorName, "memory", "used", "load"));
            }

            return;
        }

        if (reading.Type != SensorType.Temperature || value is < -50d or > 150d)
        {
            return;
        }

        if (reading.Category == SensorCategory.Cpu)
        {
            session.ConsiderCpuTemperature(value, RankMetric(reading.SensorName, "package", "core max", "tctl"));
        }
        else if (reading.Category == SensorCategory.Gpu)
        {
            session.ConsiderGpuTemperature(value, RankMetric(reading.SensorName, "core", "gpu", "hot spot"));
        }
    }

    private static int RankMetric(string? sensorName, string preferred, string secondary, string fallback)
    {
        string name = sensorName ?? string.Empty;
        if (name.Contains(preferred, StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains(secondary, StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains(fallback, StringComparison.OrdinalIgnoreCase)) return 60;
        return 10;
    }

    private bool TryCreateCandidate(SensorReading reading, out Candidate candidate)
    {
        if (!IsValidPowerReading(reading, out double powerWatts))
        {
            candidate = default;
            return false;
        }

        string rawIdentifier = reading.RawIdentifier ?? string.Empty;
        CandidateMetadata metadata;
        if (rawIdentifier.Length > 0 && metadataCache.TryGetValue(rawIdentifier, out CandidateMetadata? cached))
        {
            metadata = cached;
        }
        else
        {
            metadata = CreateCandidateMetadata(reading);
            if (rawIdentifier.Length > 0 && metadataCache.Count < MaximumMetadataCacheEntries)
            {
                metadataCache.TryAdd(rawIdentifier, metadata);
            }
        }

        if (!metadata.IsValid)
        {
            candidate = default;
            return false;
        }

        candidate = new Candidate(
            metadata.Category,
            metadata.DeviceKey,
            metadata.DeviceName,
            metadata.SensorName,
            powerWatts,
            metadata.Rank);
        return true;
    }

    private void ApplyComponents(SessionState session, long timestamp, TimeSpan maximumGap)
    {
        if (timestamp < session.StartTimestamp)
        {
            return;
        }

        for (int index = 0; index < session.ComponentOrder.Count; index++)
        {
            session.ComponentOrder[index].IsCurrentlyAvailable = false;
        }

        double currentPower = 0d;
        bool componentsChanged = false;
        for (int index = 0; index < session.SelectedComponents.Count; index++)
        {
            SelectedPowerComponent selected = session.SelectedComponents[index];
            if (!session.Components.TryGetValue(selected.ComponentKey, out ComponentState? component))
            {
                component = new ComponentState(
                    selected.ComponentKey,
                    selected.Category,
                    selected.DeviceName);
                session.Components.Add(selected.ComponentKey, component);
                session.ComponentOrder.Add(component);
                componentsChanged = true;
            }

            component.IsCurrentlyAvailable = true;
            component.LastSeenTimestamp = timestamp;
            currentPower += selected.PowerWatts;
            session.HasPowerData = true;
            if (component.PreviousTimestamp is long previousTimestamp
                && component.PreviousPowerWatts is double previousPower
                && timestamp > previousTimestamp)
            {
                double elapsedSeconds = (timestamp - previousTimestamp) / timestampFrequency;
                if (elapsedSeconds <= maximumGap.TotalSeconds)
                {
                    component.EnergyWattSeconds +=
                        (previousPower + selected.PowerWatts) * 0.5d * elapsedSeconds;
                    component.ValidIntegrationSeconds += elapsedSeconds;
                }
            }

            component.PreviousPowerWatts = selected.PowerWatts;
            component.PreviousTimestamp = timestamp;
        }

        for (int index = 0; index < session.ComponentOrder.Count; index++)
        {
            ComponentState component = session.ComponentOrder[index];
            if (!component.IsCurrentlyAvailable)
            {
                component.PreviousPowerWatts = null;
                component.PreviousTimestamp = null;
            }
        }

        session.CurrentPowerWatts = session.SelectedComponents.Count == 0 ? null : currentPower;
        if (componentsChanged || session.IncludedComponentsText is null)
        {
            session.IncludedComponentsText = BuildComponentText(session.ComponentOrder);
        }
    }

    private GameEnergySnapshot CreateSnapshot(SessionState session, long timestamp, bool isTracking)
    {
        long endTimestamp = session.CompletedTimestamp ?? timestamp;
        double sessionSeconds = Math.Max(0d, (endTimestamp - session.StartTimestamp) / timestampFrequency);
        double energyWattSeconds = 0d;
        double cpuEnergyWattSeconds = 0d;
        double gpuEnergyWattSeconds = 0d;
        double effectiveIntegrationSeconds = 0d;
        double cpuIntegrationSeconds = 0d;
        double gpuIntegrationSeconds = 0d;
        for (int index = 0; index < session.ComponentOrder.Count; index++)
        {
            ComponentState component = session.ComponentOrder[index];
            energyWattSeconds += component.EnergyWattSeconds;
            effectiveIntegrationSeconds = Math.Max(
                effectiveIntegrationSeconds,
                component.ValidIntegrationSeconds);
            if (component.Category == SensorCategory.Cpu)
            {
                cpuEnergyWattSeconds += component.EnergyWattSeconds;
                cpuIntegrationSeconds = Math.Max(cpuIntegrationSeconds, component.ValidIntegrationSeconds);
            }
            else if (component.Category == SensorCategory.Gpu)
            {
                gpuEnergyWattSeconds += component.EnergyWattSeconds;
                gpuIntegrationSeconds = Math.Max(gpuIntegrationSeconds, component.ValidIntegrationSeconds);
            }
        }

        double? coverage = sessionSeconds > 0d
            ? Math.Clamp(effectiveIntegrationSeconds / sessionSeconds * 100d, 0d, 100d)
            : null;
        return new GameEnergySnapshot
        {
            CaptureSessionId = session.CaptureSessionId,
            Generation = session.Generation,
            IsTracking = isTracking,
            EstimatedEnergyWh = session.HasPowerData ? energyWattSeconds / 3600d : null,
            CpuEstimatedEnergyWh = cpuIntegrationSeconds > 0d ? cpuEnergyWattSeconds / 3600d : null,
            GpuEstimatedEnergyWh = gpuIntegrationSeconds > 0d ? gpuEnergyWattSeconds / 3600d : null,
            CurrentEstimatedPowerWatts = session.CurrentPowerWatts,
            AverageEstimatedPowerWatts = effectiveIntegrationSeconds > 0d
                ? energyWattSeconds / effectiveIntegrationSeconds
                : null,
            CpuAverageEstimatedPowerWatts = cpuIntegrationSeconds > 0d
                ? cpuEnergyWattSeconds / cpuIntegrationSeconds
                : null,
            GpuAverageEstimatedPowerWatts = gpuIntegrationSeconds > 0d
                ? gpuEnergyWattSeconds / gpuIntegrationSeconds
                : null,
            SessionDuration = TimeSpan.FromSeconds(sessionSeconds),
            ValidIntegrationDuration = TimeSpan.FromSeconds(effectiveIntegrationSeconds),
            CoveragePercent = coverage,
            IncludedComponents = session.IncludedComponentsText,
            AverageCpuLoadPercent = Average(session.CpuLoadSum, session.CpuLoadCount),
            AverageCpuTemperatureCelsius = Average(session.CpuTemperatureSum, session.CpuTemperatureCount),
            AverageGpuLoadPercent = Average(session.GpuLoadSum, session.GpuLoadCount),
            AverageGpuTemperatureCelsius = Average(session.GpuTemperatureSum, session.GpuTemperatureCount),
            AverageMemoryLoadPercent = Average(session.MemoryLoadSum, session.MemoryLoadCount)
        };
    }

    private static double? Average(double sum, long count) => count > 0 ? sum / count : null;

    private void Publish(GameEnergySnapshot snapshot)
    {
        if (isDisposed)
        {
            return;
        }

        RuntimePerformanceDiagnostics.RecordEnergyTrackerSnapshot();
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private static Candidate? CreateCandidate(SensorReading reading)
    {
        if (!IsValidPowerReading(reading, out double powerWatts))
        {
            return null;
        }

        CandidateMetadata metadata = CreateCandidateMetadata(reading);
        return metadata.IsValid
            ? new Candidate(
                metadata.Category,
                metadata.DeviceKey,
                metadata.DeviceName,
                metadata.SensorName,
                powerWatts,
                metadata.Rank)
            : null;
    }

    private static CandidateMetadata CreateCandidateMetadata(SensorReading reading)
    {
        int rank = RankSensor(reading.Category, reading.SensorName);
        if (rank <= 0)
        {
            return CandidateMetadata.Invalid;
        }

        string deviceName = string.IsNullOrWhiteSpace(reading.DeviceName)
            ? (reading.Category == SensorCategory.Cpu ? "CPU" : "GPU")
            : reading.DeviceName.Trim();
        return new CandidateMetadata(
            true,
            reading.Category,
            CreateDeviceKey(reading, deviceName),
            deviceName,
            reading.SensorName,
            rank);
    }

    private static bool IsValidPowerReading(SensorReading reading, out double powerWatts)
    {
        if (reading.Type == SensorType.Power
            && reading.Category is SensorCategory.Cpu or SensorCategory.Gpu
            && reading.IsAvailable
            && reading.Value is double value
            && double.IsFinite(value)
            && value >= 0d
            && value <= MaximumComponentPowerWatts
            && IsWatts(reading.Unit))
        {
            powerWatts = value;
            return true;
        }

        powerWatts = 0d;
        return false;
    }

    private static int RankSensor(SensorCategory category, string? sensorName)
    {
        string name = sensorName?.Trim() ?? string.Empty;
        if (name.Length == 0
            || ContainsAny(name, ExcludedPowerSensorTokens))
        {
            return 0;
        }

        if (category == SensorCategory.Cpu)
        {
            if (name.Contains("package", StringComparison.OrdinalIgnoreCase)) return 100;
            if (name.Contains("total", StringComparison.OrdinalIgnoreCase)) return 90;
            if (name.Contains("cpu", StringComparison.OrdinalIgnoreCase)
                && name.Contains("power", StringComparison.OrdinalIgnoreCase)) return 80;
            return 0;
        }

        if (name.Contains("board", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("total", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("package", StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains("gpu", StringComparison.OrdinalIgnoreCase)
            && name.Contains("power", StringComparison.OrdinalIgnoreCase)) return 70;
        return 0;
    }

    private static string CreateDeviceKey(SensorReading reading, string deviceName)
    {
        string raw = reading.RawIdentifier?.Trim() ?? string.Empty;
        if (raw.Length > 0)
        {
            int powerIndex = raw.IndexOf("/power/", StringComparison.OrdinalIgnoreCase);
            if (powerIndex > 0)
            {
                return raw[..powerIndex];
            }

            ReadOnlySpan<char> span = raw.AsSpan().Trim('/');
            int firstSlash = span.IndexOf('/');
            if (firstSlash > 0)
            {
                int secondSlash = span[(firstSlash + 1)..].IndexOf('/');
                if (secondSlash >= 0)
                {
                    return "/" + span[..(firstSlash + secondSlash + 1)].ToString();
                }
            }
        }

        StringBuilder normalized = new(deviceName.Length);
        foreach (char character in deviceName)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.Length > 0 ? normalized.ToString() : reading.Category.ToString();
    }

    private static bool IsWatts(string? unit)
    {
        ReadOnlySpan<char> normalized = unit.AsSpan().Trim();
        return normalized.IsEmpty
            || normalized.Equals("W".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Watt".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Watts".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> tokens)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            if (value.Contains(tokens[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBetterCandidate(Candidate candidate, SelectedPowerComponent selected)
    {
        return candidate.Rank > selected.Rank
            || candidate.Rank == selected.Rank
            && string.Compare(candidate.SensorName, selected.SensorName, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string BuildComponentText(IReadOnlyList<SelectedPowerComponent> components)
    {
        int cpuCount = 0;
        int gpuCount = 0;
        for (int index = 0; index < components.Count; index++)
        {
            if (components[index].Category == SensorCategory.Cpu) cpuCount++;
            else gpuCount++;
        }

        StringBuilder builder = new(components.Count * 24);
        int cpuIndex = 0;
        int gpuIndex = 0;
        for (int index = 0; index < components.Count; index++)
        {
            SelectedPowerComponent component = components[index];
            if (builder.Length > 0) builder.Append("; ");
            bool cpu = component.Category == SensorCategory.Cpu;
            int componentIndex = cpu ? ++cpuIndex : ++gpuIndex;
            int categoryCount = cpu ? cpuCount : gpuCount;
            builder.Append(cpu ? "CPU" : "GPU");
            if (categoryCount > 1) builder.Append(' ').Append(componentIndex);
            builder.Append(" (").Append(component.DeviceName).Append(')');
        }

        return builder.ToString();
    }

    private static string? BuildComponentText(IReadOnlyList<ComponentState> components)
    {
        if (components.Count == 0)
        {
            return null;
        }

        int cpuCount = 0;
        int gpuCount = 0;
        for (int index = 0; index < components.Count; index++)
        {
            if (components[index].Category == SensorCategory.Cpu) cpuCount++;
            else gpuCount++;
        }

        StringBuilder builder = new(components.Count * 24);
        int cpuIndex = 0;
        int gpuIndex = 0;
        for (int index = 0; index < components.Count; index++)
        {
            ComponentState component = components[index];
            if (builder.Length > 0) builder.Append("; ");
            bool cpu = component.Category == SensorCategory.Cpu;
            int componentIndex = cpu ? ++cpuIndex : ++gpuIndex;
            int categoryCount = cpu ? cpuCount : gpuCount;
            builder.Append(cpu ? "CPU" : "GPU");
            if (categoryCount > 1) builder.Append(' ').Append(componentIndex);
            builder.Append(" (").Append(component.DisplayName).Append(')');
        }

        return builder.ToString();
    }

    private static TimeSpan CalculateMaximumGap(double intervalSeconds, double fallbackSeconds)
    {
        double interval = double.IsFinite(intervalSeconds) && intervalSeconds > 0d
            ? intervalSeconds
            : fallbackSeconds;
        return TimeSpan.FromSeconds(Math.Max(fallbackSeconds, interval * 1.75d + 0.5d));
    }

    internal sealed record SelectedPowerSample(double PowerWatts, string IncludedComponents);

    private readonly record struct Candidate(
        SensorCategory Category,
        string DeviceKey,
        string DeviceName,
        string SensorName,
        double PowerWatts,
        int Rank)
    {
        public SelectedPowerComponent ToSelected() =>
            new(DeviceKey, Category, DeviceName, SensorName, PowerWatts, Rank);
    }

    private sealed record CandidateMetadata(
        bool IsValid,
        SensorCategory Category,
        string DeviceKey,
        string DeviceName,
        string SensorName,
        int Rank)
    {
        public static CandidateMetadata Invalid { get; } =
            new(false, SensorCategory.Unknown, string.Empty, string.Empty, string.Empty, 0);
    }

    private readonly record struct SelectedPowerComponent(
        string ComponentKey,
        SensorCategory Category,
        string DeviceName,
        string SensorName,
        double PowerWatts,
        int Rank);

    private sealed class ComponentState(string key, SensorCategory category, string displayName)
    {
        public string Key { get; } = key;

        public SensorCategory Category { get; } = category;

        public string DisplayName { get; } = displayName;

        public double? PreviousPowerWatts { get; set; }

        public long? PreviousTimestamp { get; set; }

        public double EnergyWattSeconds { get; set; }

        public double ValidIntegrationSeconds { get; set; }

        public long LastSeenTimestamp { get; set; }

        public bool IsCurrentlyAvailable { get; set; }
    }

    private sealed class SessionState(Guid captureSessionId, int generation, long startTimestamp)
    {
        public Guid CaptureSessionId { get; } = captureSessionId;

        public int Generation { get; } = generation;

        public long StartTimestamp { get; } = startTimestamp;

        public long? CompletedTimestamp { get; set; }

        public long? LastSnapshotTimestamp { get; set; }

        public bool IsTracking { get; set; } = true;

        public bool HasPowerData { get; set; }

        public double? CurrentPowerWatts { get; set; }

        public string? IncludedComponentsText { get; set; }

        public Dictionary<string, ComponentState> Components { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ComponentState> ComponentOrder { get; } = [];

        public Dictionary<string, int> SelectionIndexes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<SelectedPowerComponent> SelectedComponents { get; } = [];

        public double CpuLoadSum;
        public long CpuLoadCount;
        public double CpuTemperatureSum;
        public long CpuTemperatureCount;
        public double GpuLoadSum;
        public long GpuLoadCount;
        public double GpuTemperatureSum;
        public long GpuTemperatureCount;
        public double MemoryLoadSum;
        public long MemoryLoadCount;

        private double? cpuLoadCandidate;
        private int cpuLoadRank;
        private double? cpuTemperatureCandidate;
        private int cpuTemperatureRank;
        private double? gpuLoadCandidate;
        private int gpuLoadRank;
        private double? gpuTemperatureCandidate;
        private int gpuTemperatureRank;
        private double? memoryLoadCandidate;
        private int memoryLoadRank;

        public void ResetMetricCandidates()
        {
            cpuLoadCandidate = cpuTemperatureCandidate = gpuLoadCandidate = gpuTemperatureCandidate = memoryLoadCandidate = null;
            cpuLoadRank = cpuTemperatureRank = gpuLoadRank = gpuTemperatureRank = memoryLoadRank = int.MinValue;
        }

        public void ConsiderCpuLoad(double value, int rank) => Consider(value, rank, ref cpuLoadCandidate, ref cpuLoadRank);
        public void ConsiderCpuTemperature(double value, int rank) => Consider(value, rank, ref cpuTemperatureCandidate, ref cpuTemperatureRank);
        public void ConsiderGpuLoad(double value, int rank) => Consider(value, rank, ref gpuLoadCandidate, ref gpuLoadRank);
        public void ConsiderGpuTemperature(double value, int rank) => Consider(value, rank, ref gpuTemperatureCandidate, ref gpuTemperatureRank);
        public void ConsiderMemoryLoad(double value, int rank) => Consider(value, rank, ref memoryLoadCandidate, ref memoryLoadRank);

        public void CommitMetricCandidates()
        {
            Add(cpuLoadCandidate, ref CpuLoadSum, ref CpuLoadCount);
            Add(cpuTemperatureCandidate, ref CpuTemperatureSum, ref CpuTemperatureCount);
            Add(gpuLoadCandidate, ref GpuLoadSum, ref GpuLoadCount);
            Add(gpuTemperatureCandidate, ref GpuTemperatureSum, ref GpuTemperatureCount);
            Add(memoryLoadCandidate, ref MemoryLoadSum, ref MemoryLoadCount);
        }

        private static void Consider(double value, int rank, ref double? candidate, ref int currentRank)
        {
            if (rank > currentRank)
            {
                candidate = value;
                currentRank = rank;
            }
        }

        private static void Add(double? value, ref double sum, ref long count)
        {
            if (!value.HasValue) return;
            sum += value.Value;
            count++;
        }
    }
}
