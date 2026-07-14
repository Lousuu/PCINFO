using System.Diagnostics;
using System.Text.RegularExpressions;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class GamePerformanceLimitTracker : IGamePerformanceLimitTracker
{
    private const int DefaultCapacity = 200;
    private readonly object stateLock = new();
    private readonly PollingService? pollingService;
    private readonly Func<long> getTimestamp;
    private readonly double timestampFrequency;
    private readonly int capacity;
    private SessionState? currentSession;
    private GamePerformanceLimitSnapshot currentSnapshot = GamePerformanceLimitSnapshot.Empty;
    private bool isDisposed;

    public GamePerformanceLimitTracker(PollingService pollingService)
        : this(pollingService, Stopwatch.GetTimestamp, Stopwatch.Frequency, DefaultCapacity)
    {
    }

    internal GamePerformanceLimitTracker(
        PollingService? pollingService,
        Func<long> getTimestamp,
        long timestampFrequency,
        int capacity = DefaultCapacity)
    {
        this.pollingService = pollingService;
        this.getTimestamp = getTimestamp;
        this.timestampFrequency = Math.Max(1d, timestampFrequency);
        this.capacity = Math.Max(1, capacity);
        if (pollingService is not null)
        {
            pollingService.ReadingsUpdated += OnReadingsUpdated;
        }
    }

    public event EventHandler<GamePerformanceLimitSnapshot>? SnapshotChanged;

    public GamePerformanceLimitSnapshot CurrentSnapshot
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
        GamePerformanceLimitSnapshot snapshot;
        lock (stateLock)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            currentSession = new SessionState(startInfo.CaptureSessionId, startInfo.Generation);
            snapshot = currentSnapshot = CreateSnapshot(currentSession, isTracking: true);
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    public GamePerformanceLimitSnapshot? CompleteSession(Guid captureSessionId, int generation)
    {
        GamePerformanceLimitSnapshot snapshot;
        lock (stateLock)
        {
            SessionState? session = currentSession;
            if (session is null
                || session.CaptureSessionId != captureSessionId
                || session.Generation != generation)
            {
                return null;
            }

            if (!session.IsTracking)
            {
                return currentSnapshot;
            }

            long timestamp = getTimestamp();
            DateTimeOffset observedAt = DateTimeOffset.Now;
            FinalizeActiveEvent(session, PerformanceLimitProcessorType.Cpu, timestamp, observedAt);
            FinalizeActiveEvent(session, PerformanceLimitProcessorType.Gpu, timestamp, observedAt);
            session.IsTracking = false;
            snapshot = currentSnapshot = CreateSnapshot(session, isTracking: false);
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
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(readings);
        IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> reasons = SelectActiveReasons(readings);
        GamePerformanceLimitSnapshot? snapshot = null;
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

            bool changed = ApplyProcessorState(
                session,
                PerformanceLimitProcessorType.Cpu,
                reasons.GetValueOrDefault(PerformanceLimitProcessorType.Cpu) ?? [],
                timestamp,
                observedAt);
            changed |= ApplyProcessorState(
                session,
                PerformanceLimitProcessorType.Gpu,
                reasons.GetValueOrDefault(PerformanceLimitProcessorType.Gpu) ?? [],
                timestamp,
                observedAt);
            if (changed)
            {
                snapshot = currentSnapshot = CreateSnapshot(session, isTracking: true);
            }
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    internal static IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> SelectActiveReasons(
        IEnumerable<SensorReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        Dictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> result = new();
        foreach (IGrouping<PerformanceLimitProcessorType, string> group in readings
            .Select(CreateReasonCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .GroupBy(candidate => candidate.ProcessorType, candidate => candidate.Reason))
        {
            result[group.Key] = group
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return result;
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

        RecordReadings(sessionId, generation, e.Readings, getTimestamp(), e.Timestamp);
    }

    private bool ApplyProcessorState(
        SessionState session,
        PerformanceLimitProcessorType processorType,
        IReadOnlyList<string> reasons,
        long timestamp,
        DateTimeOffset observedAt)
    {
        ActiveEventState? active = session.ActiveEvents.GetValueOrDefault(processorType);
        if (reasons.Count == 0)
        {
            return active is not null && FinalizeActiveEvent(session, processorType, timestamp, observedAt);
        }

        if (active is not null && ReasonsEqual(active.Reasons, reasons))
        {
            UpdateEvent(session, active, timestamp, isActive: true);
            return true;
        }

        if (active is not null)
        {
            FinalizeActiveEvent(session, processorType, timestamp, observedAt);
        }

        long eventId = ++session.NextEventId;
        string[] copiedReasons = reasons.ToArray();
        ActiveEventState newEvent = new(eventId, processorType, timestamp, observedAt, copiedReasons);
        session.ActiveEvents[processorType] = newEvent;
        session.Events.Add(CreateEvent(session, newEvent, timestamp, isActive: true));
        while (session.Events.Count > capacity)
        {
            session.Events.RemoveAt(0);
        }

        return true;
    }

    private bool FinalizeActiveEvent(
        SessionState session,
        PerformanceLimitProcessorType processorType,
        long timestamp,
        DateTimeOffset observedAt)
    {
        if (!session.ActiveEvents.Remove(processorType, out ActiveEventState? active))
        {
            return false;
        }

        UpdateEvent(session, active, timestamp, isActive: false);
        return true;
    }

    private void UpdateEvent(SessionState session, ActiveEventState active, long timestamp, bool isActive)
    {
        int index = session.Events.FindIndex(item => item.EventId == active.EventId);
        if (index >= 0)
        {
            session.Events[index] = CreateEvent(session, active, timestamp, isActive);
        }
    }

    private GamePerformanceLimitEvent CreateEvent(
        SessionState session,
        ActiveEventState active,
        long timestamp,
        bool isActive)
    {
        double seconds = Math.Max(0d, (timestamp - active.StartTimestamp) / timestampFrequency);
        return new GamePerformanceLimitEvent
        {
            EventId = active.EventId,
            CaptureSessionId = session.CaptureSessionId,
            Generation = session.Generation,
            ProcessorType = active.ProcessorType,
            StartedAt = active.StartedAt,
            Duration = TimeSpan.FromSeconds(seconds),
            IsActive = isActive,
            Reasons = active.Reasons
        };
    }

    private static GamePerformanceLimitSnapshot CreateSnapshot(SessionState session, bool isTracking)
    {
        return new GamePerformanceLimitSnapshot
        {
            CaptureSessionId = session.CaptureSessionId,
            Generation = session.Generation,
            IsTracking = isTracking,
            Events = session.Events.AsEnumerable().Reverse().ToArray()
        };
    }

    private static ReasonCandidate? CreateReasonCandidate(SensorReading reading)
    {
        if (reading.Category is not (SensorCategory.Cpu or SensorCategory.Gpu)
            || !reading.IsAvailable
            || reading.Value is not double value
            || !double.IsFinite(value)
            || value <= 0.5d
            || !IsStateUnit(reading.Unit))
        {
            return null;
        }

        string reason = CleanReasonName(reading.SensorName);
        if (!IsExplicitLimitReason(reason))
        {
            return null;
        }

        return new ReasonCandidate(
            reading.Category == SensorCategory.Cpu
                ? PerformanceLimitProcessorType.Cpu
                : PerformanceLimitProcessorType.Gpu,
            reason);
    }

    private static bool IsExplicitLimitReason(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        return ContainsAny(
            name,
            "throttl",
            "thermal event",
            "temperature event",
            "performance limit",
            "perf limit",
            "perfcap",
            "limit reason",
            "limit -",
            "limit:",
            "limit exceeded",
            "electrical design point",
            "edp throttl",
            "current limit",
            "reliability voltage",
            "utilization limit",
            "max turbo limit");
    }

    private static bool IsStateUnit(string? unit)
    {
        string normalized = unit?.Trim() ?? string.Empty;
        return normalized.Length == 0
            || normalized.Equals("%", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("bool", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("boolean", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("state", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanReasonName(string? name)
    {
        string cleaned = Regex.Replace(name?.Trim() ?? string.Empty, @"\s+", " ");
        return cleaned.Length <= 96 ? cleaned : cleaned[..93] + "...";
    }

    private static bool ReasonsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Count == right.Count
            && left.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(right.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private readonly record struct ReasonCandidate(
        PerformanceLimitProcessorType ProcessorType,
        string Reason);

    private sealed class SessionState(Guid captureSessionId, int generation)
    {
        public Guid CaptureSessionId { get; } = captureSessionId;

        public int Generation { get; } = generation;

        public bool IsTracking { get; set; } = true;

        public long NextEventId { get; set; }

        public List<GamePerformanceLimitEvent> Events { get; } = [];

        public Dictionary<PerformanceLimitProcessorType, ActiveEventState> ActiveEvents { get; } = [];
    }

    private sealed record ActiveEventState(
        long EventId,
        PerformanceLimitProcessorType ProcessorType,
        long StartTimestamp,
        DateTimeOffset StartedAt,
        IReadOnlyList<string> Reasons);
}
