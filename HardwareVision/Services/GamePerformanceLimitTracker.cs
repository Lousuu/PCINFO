using System.Diagnostics;
using System.Text;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class GamePerformanceLimitTracker : IGamePerformanceLimitTracker
{
    internal const int StartConfirmationSamples = 2;
    internal const int EndConfirmationSamples = 3;
    internal static readonly TimeSpan StartConfirmationDuration = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan EndConfirmationDuration = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ActiveDurationPublishInterval = TimeSpan.FromSeconds(1);
    private const int DefaultCapacity = 200;
    private const int MaximumReasonCacheEntries = 512;
    private static readonly string[] ExcludedReasonTokens =
    [
        "utilization / idle",
        "low utilization",
        "idle p-state",
        "application clock setting",
        "sync boost",
        "display clock setting",
        "nvml flag",
        "performance limit flag"
    ];
    private static readonly string[] ExplicitReasonTokens =
    [
        "thermal thrott",
        "thermal slowdown",
        "thermal event",
        "temperature event",
        "cpu thermal",
        "software power cap",
        "hardware slowdown",
        "hardware power brake",
        "power limit",
        "performance limit - power",
        "performance limit - thermal",
        "perfcap",
        "electrical design point",
        "edp throttl",
        "current limit",
        "reliability voltage"
    ];
    private readonly object stateLock = new();
    private readonly PollingService? pollingService;
    private readonly IReadOnlyList<IGameSessionSensorProvider> controlledProviders;
    private readonly Func<long> getTimestamp;
    private readonly double timestampFrequency;
    private readonly int capacity;
    private readonly Dictionary<string, CachedReasonClassification> reasonCache = new(StringComparer.Ordinal);
    private SessionState? currentSession;
    private GamePerformanceLimitSnapshot currentSnapshot = GamePerformanceLimitSnapshot.Empty;
    private volatile bool isDisposed;

    public GamePerformanceLimitTracker(PollingService pollingService)
        : this(pollingService, Stopwatch.GetTimestamp, Stopwatch.Frequency, DefaultCapacity, [])
    {
    }

    internal GamePerformanceLimitTracker(
        PollingService pollingService,
        IReadOnlyList<IGameSessionSensorProvider> controlledProviders)
        : this(pollingService, Stopwatch.GetTimestamp, Stopwatch.Frequency, DefaultCapacity, controlledProviders)
    {
    }

    internal GamePerformanceLimitTracker(
        PollingService? pollingService,
        Func<long> getTimestamp,
        long timestampFrequency,
        int capacity = DefaultCapacity,
        IReadOnlyList<IGameSessionSensorProvider>? controlledProviders = null)
    {
        this.pollingService = pollingService;
        this.getTimestamp = getTimestamp;
        this.timestampFrequency = Math.Max(1d, timestampFrequency);
        this.capacity = Math.Max(1, capacity);
        this.controlledProviders = controlledProviders ?? [];
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

        SetProvidersActive(true);
        Publish(snapshot);
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
            FinalizeActiveEvent(session, session.Cpu, timestamp, observedAt);
            FinalizeActiveEvent(session, session.Gpu, timestamp, observedAt);
            session.IsTracking = false;
            snapshot = currentSnapshot = CreateSnapshot(session, isTracking: false);
        }

        SetProvidersActive(false);
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

        SetProvidersActive(false);
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

            RuntimePerformanceDiagnostics.RecordPerformanceLimitTrackerInput();
            BuildObservation(readings, session);
            bool changed = ApplyProcessorState(
                session,
                session.Cpu,
                session.CpuReasons,
                session.CpuRawReasons,
                session.CpuScopes,
                session.CpuRawIdentifiers,
                ResolveSupportStatus(session.CpuSensorObserved, session.CpuTemporarilyUnavailable, session.CpuReasons.Count),
                timestamp,
                observedAt);
            changed |= ApplyProcessorState(
                session,
                session.Gpu,
                session.GpuReasons,
                session.GpuRawReasons,
                session.GpuScopes,
                session.GpuRawIdentifiers,
                ResolveSupportStatus(session.GpuSensorObserved, session.GpuTemporarilyUnavailable, session.GpuReasons.Count),
                timestamp,
                observedAt);
            if (changed)
            {
                snapshot = currentSnapshot = CreateSnapshot(session, isTracking: true);
            }
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    internal static IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> SelectActiveReasons(
        IEnumerable<SensorReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        List<string>? cpu = null;
        List<string>? gpu = null;
        foreach (SensorReading reading in readings)
        {
            ReasonCandidate? candidate = CreateReasonCandidate(reading);
            if (!candidate.HasValue)
            {
                continue;
            }

            List<string> target = candidate.Value.ProcessorType == PerformanceLimitProcessorType.Cpu
                ? cpu ??= []
                : gpu ??= [];
            AddUnique(target, candidate.Value.Reason);
        }

        Dictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> result = new();
        if (cpu is not null) result[PerformanceLimitProcessorType.Cpu] = cpu.ToArray();
        if (gpu is not null) result[PerformanceLimitProcessorType.Gpu] = gpu.ToArray();
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

    private void BuildObservation(IReadOnlyList<SensorReading> readings, SessionState session)
    {
        session.CpuReasons.Clear();
        session.GpuReasons.Clear();
        session.CpuRawReasons.Clear();
        session.GpuRawReasons.Clear();
        session.CpuScopes.Clear();
        session.GpuScopes.Clear();
        session.CpuRawIdentifiers.Clear();
        session.GpuRawIdentifiers.Clear();
        session.CpuSensorObserved = false;
        session.GpuSensorObserved = false;
        session.CpuTemporarilyUnavailable = false;
        session.GpuTemporarilyUnavailable = false;
        for (int index = 0; index < readings.Count; index++)
        {
            SensorReading reading = readings[index];
            if (reading.Category is not (SensorCategory.Cpu or SensorCategory.Gpu))
            {
                continue;
            }

            bool diagnostic = IsPerformanceLimitDiagnostic(reading);
            if (diagnostic)
            {
                bool temporary = reading.Availability is SensorAvailability.Error or SensorAvailability.Unavailable;
                if (reading.Category == SensorCategory.Cpu)
                {
                    session.CpuSensorObserved = true;
                    session.CpuTemporarilyUnavailable |= temporary;
                }
                else
                {
                    session.GpuSensorObserved = true;
                    session.GpuTemporarilyUnavailable |= temporary;
                }
            }

            ReasonCandidate? candidate = GetReasonCandidate(reading);
            if (!candidate.HasValue)
            {
                continue;
            }

            if (candidate.Value.ProcessorType == PerformanceLimitProcessorType.Cpu)
            {
                session.CpuSensorObserved = true;
                AddUnique(session.CpuReasons, candidate.Value.Reason);
                AddUnique(session.CpuRawReasons, candidate.Value.RawReasonName);
                AddUnique(session.CpuScopes, candidate.Value.Scope);
                AddUnique(session.CpuRawIdentifiers, candidate.Value.RawIdentifier);
            }
            else
            {
                session.GpuSensorObserved = true;
                AddUnique(session.GpuReasons, candidate.Value.Reason);
                AddUnique(session.GpuRawReasons, candidate.Value.RawReasonName);
                AddUnique(session.GpuScopes, candidate.Value.Scope);
                AddUnique(session.GpuRawIdentifiers, candidate.Value.RawIdentifier);
            }
        }
    }

    private ReasonCandidate? GetReasonCandidate(SensorReading reading)
    {
        string rawIdentifier = reading.RawIdentifier ?? string.Empty;
        if (rawIdentifier.Length > 0
            && reasonCache.TryGetValue(rawIdentifier, out CachedReasonClassification cached))
        {
            return cached.IsReason && IsActiveStateReading(reading)
                ? new ReasonCandidate(
                    cached.ProcessorType,
                    cached.Reason,
                    cached.RawReasonName,
                    cached.Scope,
                    rawIdentifier)
                : null;
        }

        ReasonCandidate? candidate = CreateReasonCandidate(reading);
        if (rawIdentifier.Length > 0 && reasonCache.Count < MaximumReasonCacheEntries)
        {
            reasonCache.TryAdd(
                rawIdentifier,
                candidate.HasValue
                    ? new CachedReasonClassification(
                        true,
                        candidate.Value.ProcessorType,
                        candidate.Value.Reason,
                        candidate.Value.RawReasonName,
                        candidate.Value.Scope)
                    : CachedReasonClassification.NotReason);
        }

        return candidate;
    }

    private bool ApplyProcessorState(
        SessionState session,
        ProcessorState processor,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> rawReasons,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> rawIdentifiers,
        PerformanceLimitSupportStatus supportStatus,
        long timestamp,
        DateTimeOffset observedAt)
    {
        bool changed = processor.SupportStatus != supportStatus;
        processor.SupportStatus = supportStatus;
        if (supportStatus is PerformanceLimitSupportStatus.Unsupported
            or PerformanceLimitSupportStatus.TemporarilyUnavailable)
        {
            processor.Pending = null;
            processor.ClearCount = 0;
            processor.ClearStartedTimestamp = null;
            return changed;
        }

        if (reasons.Count == 0)
        {
            processor.Pending = null;
            if (processor.Active is null)
            {
                processor.ClearCount = 0;
                processor.ClearStartedTimestamp = null;
                return changed;
            }

            if (!processor.ClearStartedTimestamp.HasValue)
            {
                processor.ClearStartedTimestamp = timestamp;
                processor.ClearCount = 1;
            }
            else
            {
                processor.ClearCount++;
            }

            if (processor.ClearCount >= EndConfirmationSamples
                || Elapsed(processor.ClearStartedTimestamp.Value, timestamp) >= EndConfirmationDuration)
            {
                changed |= FinalizeActiveEvent(session, processor, timestamp, observedAt);
                processor.ClearCount = 0;
                processor.ClearStartedTimestamp = null;
            }

            return changed;
        }

        processor.ClearCount = 0;
        processor.ClearStartedTimestamp = null;
        if (processor.Active is not null && ReasonsEqual(processor.Active.Reasons, reasons))
        {
            processor.Pending = null;
            processor.Active.TriggerCount++;
            MergeUnique(processor.Active.RawReasonNames, rawReasons);
            MergeUnique(processor.Active.Scopes, scopes);
            MergeUnique(processor.Active.RawIdentifiers, rawIdentifiers);
            if (Elapsed(processor.Active.LastPublishedTimestamp, timestamp) >= ActiveDurationPublishInterval)
            {
                processor.Active.LastPublishedTimestamp = timestamp;
                UpdateEvent(session, processor.Active, timestamp, isActive: true);
                return true;
            }

            return changed;
        }

        if (processor.Pending is null || !ReasonsEqual(processor.Pending.Reasons, reasons))
        {
            processor.Pending = new PendingEventState(
                timestamp,
                observedAt,
                CopyReasons(reasons),
                CopyReasons(rawReasons),
                CopyReasons(scopes),
                CopyReasons(rawIdentifiers),
                sampleCount: 1);
        }
        else
        {
            processor.Pending.SampleCount++;
            MergeUnique(processor.Pending.RawReasonNames, rawReasons);
            MergeUnique(processor.Pending.Scopes, scopes);
            MergeUnique(processor.Pending.RawIdentifiers, rawIdentifiers);
        }

        PendingEventState pending = processor.Pending;
        if (pending.SampleCount < StartConfirmationSamples
            && Elapsed(pending.FirstTimestamp, timestamp) < StartConfirmationDuration)
        {
            return changed;
        }

        if (processor.Active is not null)
        {
            FinalizeActiveEvent(session, processor, pending.FirstTimestamp, pending.FirstObservedAt);
        }

        StartOrMergeEvent(session, processor, pending, timestamp);
        processor.Pending = null;
        return true;
    }

    private void StartOrMergeEvent(
        SessionState session,
        ProcessorState processor,
        PendingEventState pending,
        long timestamp)
    {
        CompletedEventState? completed = processor.LastCompleted;
        ActiveEventState active;
        if (completed is not null
            && ReasonsEqual(completed.Reasons, pending.Reasons)
            && Elapsed(completed.EndTimestamp, pending.FirstTimestamp) <= MergeWindow
            && FindEventIndex(session, completed.EventId) >= 0)
        {
            active = new ActiveEventState(
                completed.EventId,
                processor.ProcessorType,
                completed.StartTimestamp,
                completed.StartedAt,
                completed.Reasons,
                MergeLists(completed.RawReasonNames, pending.RawReasonNames),
                MergeLists(completed.Scopes, pending.Scopes),
                MergeLists(completed.RawIdentifiers, pending.RawIdentifiers),
                completed.TriggerCount + pending.SampleCount,
                wasMerged: true,
                timestamp);
            processor.LastCompleted = null;
            processor.Active = active;
            UpdateEvent(session, active, timestamp, isActive: true);
            return;
        }

        long eventId = ++session.NextEventId;
        active = new ActiveEventState(
            eventId,
            processor.ProcessorType,
            pending.FirstTimestamp,
            pending.FirstObservedAt,
            pending.Reasons,
            pending.RawReasonNames,
            pending.Scopes,
            pending.RawIdentifiers,
            pending.SampleCount,
            wasMerged: false,
            timestamp);
        processor.Active = active;
        session.Events.Add(CreateEvent(session, active, timestamp, isActive: true));
        while (session.Events.Count > capacity)
        {
            session.EventsTruncated = true;
            long removedEventId = session.Events[0].EventId;
            session.Events.RemoveAt(0);
            if (session.Cpu.LastCompleted?.EventId == removedEventId) session.Cpu.LastCompleted = null;
            if (session.Gpu.LastCompleted?.EventId == removedEventId) session.Gpu.LastCompleted = null;
        }
    }

    private bool FinalizeActiveEvent(
        SessionState session,
        ProcessorState processor,
        long timestamp,
        DateTimeOffset observedAt)
    {
        ActiveEventState? active = processor.Active;
        if (active is null)
        {
            return false;
        }

        UpdateEvent(session, active, timestamp, isActive: false);
        processor.LastCompleted = new CompletedEventState(
            active.EventId,
            active.StartTimestamp,
            active.StartedAt,
            timestamp,
            observedAt,
            active.Reasons,
            active.RawReasonNames,
            active.Scopes,
            active.RawIdentifiers,
            active.TriggerCount,
            active.WasMerged);
        processor.Active = null;
        return true;
    }

    private void UpdateEvent(SessionState session, ActiveEventState active, long timestamp, bool isActive)
    {
        int index = FindEventIndex(session, active.EventId);
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
            DeviceId = active.ProcessorType == PerformanceLimitProcessorType.Gpu
                ? GpuDeviceIdentity.TryExtractCommonStableKey(active.RawIdentifiers)
                : null,
            StartedAt = active.StartedAt,
            Duration = TimeSpan.FromSeconds(seconds),
            IsActive = isActive,
            Reasons = CopyReasons(active.Reasons),
            RawReasonNames = CopyReasons(active.RawReasonNames),
            Scopes = CopyReasons(active.Scopes),
            RawIdentifiers = CopyReasons(active.RawIdentifiers),
            TriggerCount = active.TriggerCount,
            WasMerged = active.WasMerged
        };
    }

    private static string[] MergeLists(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        List<string> merged = new(left.Count + right.Count);
        MergeUnique(merged, left);
        MergeUnique(merged, right);
        return merged.ToArray();
    }

    private static int FindEventIndex(SessionState session, long eventId)
    {
        for (int index = session.Events.Count - 1; index >= 0; index--)
        {
            if (session.Events[index].EventId == eventId)
            {
                return index;
            }
        }

        return -1;
    }

    private static GamePerformanceLimitSnapshot CreateSnapshot(SessionState session, bool isTracking)
    {
        GamePerformanceLimitEvent[] events = new GamePerformanceLimitEvent[session.Events.Count];
        for (int index = 0; index < session.Events.Count; index++)
        {
            events[index] = session.Events[session.Events.Count - index - 1];
        }

        return new GamePerformanceLimitSnapshot
        {
            CaptureSessionId = session.CaptureSessionId,
            Generation = session.Generation,
            IsTracking = isTracking,
            CpuSupportStatus = session.Cpu.SupportStatus,
            GpuSupportStatus = session.Gpu.SupportStatus,
            EventsTruncated = session.EventsTruncated,
            Events = events
        };
    }

    private static ReasonCandidate? CreateReasonCandidate(SensorReading reading)
    {
        if (!IsActiveStateReading(reading))
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
            reason,
            CleanDiagnosticText(reading.SensorName, reason),
            CleanDiagnosticText(
                reading.DeviceName,
                reading.Category == SensorCategory.Cpu ? "CPU" : "GPU"),
            reading.RawIdentifier?.Trim() ?? string.Empty);
    }

    private static string CleanDiagnosticText(string? value, string fallback)
    {
        string cleaned = value?.Trim() ?? string.Empty;
        return cleaned.Length == 0 ? fallback : cleaned.Length <= 160 ? cleaned : cleaned[..160];
    }

    private static bool IsActiveStateReading(SensorReading reading)
    {
        return reading.Category is SensorCategory.Cpu or SensorCategory.Gpu
            && reading.Type == SensorType.State
            && reading.IsAvailable
            && reading.Value is double value
            && double.IsFinite(value)
            && value > 0.5d
            && IsStateUnit(reading.Unit);
    }

    private static bool IsExplicitLimitReason(string name)
    {
        if (name.Length == 0
            || ContainsAny(name, ExcludedReasonTokens))
        {
            return false;
        }

        return ContainsAny(name, ExplicitReasonTokens);
    }

    private static bool IsPerformanceLimitDiagnostic(SensorReading reading)
    {
        if (reading.Type != SensorType.State)
        {
            return false;
        }

        string raw = reading.RawIdentifier ?? string.Empty;
        return raw.StartsWith("/nvml/", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("Processor Information.PerformanceLimit", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("/performance-limit-status/", StringComparison.OrdinalIgnoreCase);
    }

    private static PerformanceLimitSupportStatus ResolveSupportStatus(
        bool sensorObserved,
        bool temporarilyUnavailable,
        int reasonCount)
    {
        if (temporarilyUnavailable) return PerformanceLimitSupportStatus.TemporarilyUnavailable;
        if (!sensorObserved) return PerformanceLimitSupportStatus.Unsupported;
        return reasonCount > 0
            ? PerformanceLimitSupportStatus.ActiveLimit
            : PerformanceLimitSupportStatus.SupportedNormal;
    }

    private static bool IsStateUnit(string? unit)
    {
        ReadOnlySpan<char> normalized = unit.AsSpan().Trim();
        return normalized.IsEmpty
            || normalized.Equals("%".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("bool".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("boolean".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("state".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanReasonName(string? name)
    {
        ReadOnlySpan<char> value = name.AsSpan().Trim();
        if (value.IsEmpty)
        {
            return string.Empty;
        }

        bool needsCleanup = value.Length > 96;
        bool previousWhitespace = false;
        for (int index = 0; index < value.Length && !needsCleanup; index++)
        {
            bool whitespace = char.IsWhiteSpace(value[index]);
            needsCleanup = whitespace && previousWhitespace;
            previousWhitespace = whitespace;
        }

        if (!needsCleanup)
        {
            return value.Length == name?.Length ? name : value.ToString();
        }

        StringBuilder builder = new(Math.Min(96, value.Length));
        previousWhitespace = false;
        for (int index = 0; index < value.Length && builder.Length < 96; index++)
        {
            char character = value[index];
            bool whitespace = char.IsWhiteSpace(character);
            if (whitespace && previousWhitespace) continue;
            builder.Append(whitespace ? ' ' : character);
            previousWhitespace = whitespace;
        }

        if (builder.Length > 96)
        {
            builder.Length = 93;
            builder.Append("...");
        }

        return builder.ToString();
    }

    private static bool ReasonsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count) return false;
        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] CopyReasons(IReadOnlyList<string> reasons)
    {
        string[] result = new string[reasons.Count];
        for (int index = 0; index < reasons.Count; index++) result[index] = reasons[index];
        return result;
    }

    private static void MergeUnique(List<string> target, IReadOnlyList<string> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (values[index].Length > 0) AddUnique(target, values[index]);
        }
    }

    private static void AddUnique(List<string> reasons, string reason)
    {
        for (int index = 0; index < reasons.Count; index++)
        {
            if (string.Equals(reasons[index], reason, StringComparison.OrdinalIgnoreCase)) return;
        }

        reasons.Add(reason);
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> tokens)
    {
        for (int index = 0; index < tokens.Count; index++)
        {
            if (value.Contains(tokens[index], StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private TimeSpan Elapsed(long start, long end) =>
        TimeSpan.FromSeconds(Math.Max(0d, (end - start) / timestampFrequency));

    private void SetProvidersActive(bool active)
    {
        for (int index = 0; index < controlledProviders.Count; index++)
        {
            controlledProviders[index].SetSessionActive(active);
        }
    }

    private void Publish(GamePerformanceLimitSnapshot snapshot)
    {
        if (isDisposed) return;
        RuntimePerformanceDiagnostics.RecordPerformanceLimitTrackerSnapshot();
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private readonly record struct ReasonCandidate(
        PerformanceLimitProcessorType ProcessorType,
        string Reason,
        string RawReasonName,
        string Scope,
        string RawIdentifier);

    private readonly record struct CachedReasonClassification(
        bool IsReason,
        PerformanceLimitProcessorType ProcessorType,
        string Reason,
        string RawReasonName,
        string Scope)
    {
        public static CachedReasonClassification NotReason { get; } =
            new(false, PerformanceLimitProcessorType.Cpu, string.Empty, string.Empty, string.Empty);
    }

    private sealed class SessionState(Guid captureSessionId, int generation)
    {
        public Guid CaptureSessionId { get; } = captureSessionId;

        public int Generation { get; } = generation;

        public bool IsTracking { get; set; } = true;

        public long NextEventId { get; set; }

        public bool EventsTruncated { get; set; }

        public List<GamePerformanceLimitEvent> Events { get; } = [];

        public ProcessorState Cpu { get; } = new(PerformanceLimitProcessorType.Cpu);

        public ProcessorState Gpu { get; } = new(PerformanceLimitProcessorType.Gpu);

        public List<string> CpuReasons { get; } = [];

        public List<string> GpuReasons { get; } = [];

        public List<string> CpuRawReasons { get; } = [];

        public List<string> GpuRawReasons { get; } = [];

        public List<string> CpuScopes { get; } = [];

        public List<string> GpuScopes { get; } = [];

        public List<string> CpuRawIdentifiers { get; } = [];

        public List<string> GpuRawIdentifiers { get; } = [];

        public bool CpuSensorObserved { get; set; }

        public bool GpuSensorObserved { get; set; }

        public bool CpuTemporarilyUnavailable { get; set; }

        public bool GpuTemporarilyUnavailable { get; set; }
    }

    private sealed class ProcessorState(PerformanceLimitProcessorType processorType)
    {
        public PerformanceLimitProcessorType ProcessorType { get; } = processorType;

        public PerformanceLimitSupportStatus SupportStatus { get; set; } = PerformanceLimitSupportStatus.NotStarted;

        public ActiveEventState? Active { get; set; }

        public PendingEventState? Pending { get; set; }

        public CompletedEventState? LastCompleted { get; set; }

        public int ClearCount { get; set; }

        public long? ClearStartedTimestamp { get; set; }
    }

    private sealed class PendingEventState(
        long firstTimestamp,
        DateTimeOffset firstObservedAt,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> rawReasonNames,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> rawIdentifiers,
        int sampleCount)
    {
        public long FirstTimestamp { get; } = firstTimestamp;

        public DateTimeOffset FirstObservedAt { get; } = firstObservedAt;

        public IReadOnlyList<string> Reasons { get; } = reasons;

        public List<string> RawReasonNames { get; } = new(rawReasonNames);

        public List<string> Scopes { get; } = new(scopes);

        public List<string> RawIdentifiers { get; } = new(rawIdentifiers);

        public int SampleCount { get; set; } = sampleCount;
    }

    private sealed class ActiveEventState(
        long eventId,
        PerformanceLimitProcessorType processorType,
        long startTimestamp,
        DateTimeOffset startedAt,
        IReadOnlyList<string> reasons,
        IReadOnlyList<string> rawReasonNames,
        IReadOnlyList<string> scopes,
        IReadOnlyList<string> rawIdentifiers,
        int triggerCount,
        bool wasMerged,
        long lastPublishedTimestamp)
    {
        public long EventId { get; } = eventId;

        public PerformanceLimitProcessorType ProcessorType { get; } = processorType;

        public long StartTimestamp { get; } = startTimestamp;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public IReadOnlyList<string> Reasons { get; } = reasons;

        public List<string> RawReasonNames { get; } = new(rawReasonNames);

        public List<string> Scopes { get; } = new(scopes);

        public List<string> RawIdentifiers { get; } = new(rawIdentifiers);

        public int TriggerCount { get; set; } = triggerCount;

        public bool WasMerged { get; } = wasMerged;

        public long LastPublishedTimestamp { get; set; } = lastPublishedTimestamp;
    }

    private sealed record CompletedEventState(
        long EventId,
        long StartTimestamp,
        DateTimeOffset StartedAt,
        long EndTimestamp,
        DateTimeOffset EndedAt,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<string> RawReasonNames,
        IReadOnlyList<string> Scopes,
        IReadOnlyList<string> RawIdentifiers,
        int TriggerCount,
        bool WasMerged);
}
