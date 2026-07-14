using System.Diagnostics;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class GameEnergyTracker : IGameEnergyTracker
{
    private const double MaximumComponentPowerWatts = 2_000d;
    private readonly object stateLock = new();
    private readonly PollingService? pollingService;
    private readonly Func<long> getTimestamp;
    private readonly double timestampFrequency;
    private readonly TimeSpan foregroundMaximumGap;
    private readonly TimeSpan backgroundMaximumGap;
    private SessionState? currentSession;
    private GameEnergySnapshot currentSnapshot = GameEnergySnapshot.Empty;
    private bool isDisposed;

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

        SnapshotChanged?.Invoke(this, snapshot);
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

        SnapshotChanged?.Invoke(this, snapshot);
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
        SelectedPowerSample? selected = SelectPowerSample(readings);
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

            ApplySample(
                session,
                selected,
                timestamp,
                isBackgroundMode ? backgroundMaximumGap : foregroundMaximumGap);
            snapshot = currentSnapshot = CreateSnapshot(session, timestamp, isTracking: true);
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    internal static SelectedPowerSample? SelectPowerSample(IEnumerable<SensorReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        Candidate[] candidates = readings
            .Select(CreateCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        Candidate[] selected = candidates
            .GroupBy(candidate => $"{candidate.Category}:{candidate.DeviceKey}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Rank)
                .ThenBy(candidate => candidate.SensorName, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(candidate => candidate.Category)
            .ThenBy(candidate => candidate.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        double total = selected.Sum(candidate => candidate.PowerWatts);
        if (!double.IsFinite(total) || total < 0d)
        {
            return null;
        }

        Dictionary<SensorCategory, int> categoryCounts = selected
            .GroupBy(candidate => candidate.Category)
            .ToDictionary(group => group.Key, group => group.Count());
        Dictionary<SensorCategory, int> categoryIndexes = new();
        string[] components = selected.Select(candidate =>
        {
            int index = categoryIndexes.GetValueOrDefault(candidate.Category) + 1;
            categoryIndexes[candidate.Category] = index;
            string category = candidate.Category == SensorCategory.Cpu ? "CPU" : "GPU";
            string suffix = categoryCounts[candidate.Category] > 1 ? $" {index}" : string.Empty;
            return $"{category}{suffix} ({candidate.DeviceName})";
        }).ToArray();
        return new SelectedPowerSample(total, string.Join("; ", components));
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

    private void ApplySample(SessionState session, SelectedPowerSample? sample, long timestamp, TimeSpan maximumGap)
    {
        if (timestamp < session.StartTimestamp)
        {
            return;
        }

        if (sample is null)
        {
            session.PreviousPowerWatts = null;
            session.PreviousTimestamp = null;
            session.CurrentPowerWatts = null;
            return;
        }

        session.HasPowerData = true;
        session.CurrentPowerWatts = sample.PowerWatts;
        foreach (string component in sample.IncludedComponents.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            session.IncludedComponents.Add(component);
        }
        if (session.PreviousTimestamp is long previousTimestamp
            && session.PreviousPowerWatts is double previousPower
            && timestamp > previousTimestamp)
        {
            double elapsedSeconds = (timestamp - previousTimestamp) / timestampFrequency;
            if (elapsedSeconds <= maximumGap.TotalSeconds)
            {
                session.EnergyWattSeconds += (previousPower + sample.PowerWatts) * 0.5d * elapsedSeconds;
                session.ValidIntegrationSeconds += elapsedSeconds;
            }
        }

        session.PreviousPowerWatts = sample.PowerWatts;
        session.PreviousTimestamp = timestamp;
    }

    private GameEnergySnapshot CreateSnapshot(SessionState session, long timestamp, bool isTracking)
    {
        long endTimestamp = session.CompletedTimestamp ?? timestamp;
        double sessionSeconds = Math.Max(0d, (endTimestamp - session.StartTimestamp) / timestampFrequency);
        double? coverage = sessionSeconds > 0d
            ? Math.Clamp(session.ValidIntegrationSeconds / sessionSeconds * 100d, 0d, 100d)
            : null;
        return new GameEnergySnapshot
        {
            CaptureSessionId = session.CaptureSessionId,
            Generation = session.Generation,
            IsTracking = isTracking,
            EstimatedEnergyWh = session.HasPowerData ? session.EnergyWattSeconds / 3600d : null,
            CurrentEstimatedPowerWatts = session.CurrentPowerWatts,
            AverageEstimatedPowerWatts = session.ValidIntegrationSeconds > 0d
                ? session.EnergyWattSeconds / session.ValidIntegrationSeconds
                : null,
            SessionDuration = TimeSpan.FromSeconds(sessionSeconds),
            ValidIntegrationDuration = TimeSpan.FromSeconds(session.ValidIntegrationSeconds),
            CoveragePercent = coverage,
            IncludedComponents = session.IncludedComponents.Count > 0
                ? string.Join("; ", session.IncludedComponents)
                : null
        };
    }

    private static Candidate? CreateCandidate(SensorReading reading)
    {
        if (reading.Type != SensorType.Power
            || reading.Category is not (SensorCategory.Cpu or SensorCategory.Gpu)
            || !reading.IsAvailable
            || reading.Value is not double value
            || !double.IsFinite(value)
            || value < 0d
            || value > MaximumComponentPowerWatts
            || !IsWatts(reading.Unit))
        {
            return null;
        }

        int rank = RankSensor(reading.Category, reading.SensorName);
        if (rank <= 0)
        {
            return null;
        }

        string deviceName = string.IsNullOrWhiteSpace(reading.DeviceName)
            ? (reading.Category == SensorCategory.Cpu ? "CPU" : "GPU")
            : reading.DeviceName.Trim();
        return new Candidate(
            reading.Category,
            CreateDeviceKey(reading, deviceName),
            deviceName,
            reading.SensorName,
            value,
            rank);
    }

    private static int RankSensor(SensorCategory category, string? sensorName)
    {
        string name = sensorName?.Trim() ?? string.Empty;
        if (name.Length == 0
            || ContainsAny(name, "limit", "tdp", "ppt", "core", "ccd", "uncore", "dram", "soc"))
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

            string[] parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                return $"/{parts[0]}/{parts[1]}";
            }
        }

        StringBuilder normalized = new(deviceName.Length);
        foreach (char character in deviceName.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(character);
            }
        }

        return normalized.Length > 0 ? normalized.ToString() : reading.Category.ToString();
    }

    private static bool IsWatts(string? unit)
    {
        string normalized = unit?.Trim() ?? string.Empty;
        return normalized.Length == 0
            || normalized.Equals("W", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Watt", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Watts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static TimeSpan CalculateMaximumGap(double intervalSeconds, double fallbackSeconds)
    {
        double interval = double.IsFinite(intervalSeconds) && intervalSeconds > 0d
            ? intervalSeconds
            : fallbackSeconds;
        return TimeSpan.FromSeconds(Math.Max(fallbackSeconds, interval * 1.75d + 0.5d));
    }

    internal sealed record SelectedPowerSample(double PowerWatts, string IncludedComponents);

    private sealed record Candidate(
        SensorCategory Category,
        string DeviceKey,
        string DeviceName,
        string SensorName,
        double PowerWatts,
        int Rank);

    private sealed class SessionState(Guid captureSessionId, int generation, long startTimestamp)
    {
        public Guid CaptureSessionId { get; } = captureSessionId;

        public int Generation { get; } = generation;

        public long StartTimestamp { get; } = startTimestamp;

        public long? CompletedTimestamp { get; set; }

        public bool IsTracking { get; set; } = true;

        public bool HasPowerData { get; set; }

        public double EnergyWattSeconds { get; set; }

        public double ValidIntegrationSeconds { get; set; }

        public long? PreviousTimestamp { get; set; }

        public double? PreviousPowerWatts { get; set; }

        public double? CurrentPowerWatts { get; set; }

        public HashSet<string> IncludedComponents { get; } = new(StringComparer.Ordinal);
    }
}
