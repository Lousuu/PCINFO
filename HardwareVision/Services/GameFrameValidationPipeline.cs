using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class GameCaptureWarmupOptions
{
    public int MinimumCandidateFrames { get; init; } = 12;

    public TimeSpan MinimumStableDuration { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaximumWarmupDuration { get; init; } = TimeSpan.FromSeconds(3);

    public int StabilityWindowSize { get; init; } = 12;

    public int MinimumStableValues { get; init; } = 8;

    public double StabilityRatio { get; init; } = 4d;

    public double StartupOutlierRatio { get; init; } = 20d;

    public int MaximumTrackedSwapChains { get; init; } = 16;

    public TimeSpan PrimarySwapChainInactivity { get; init; } = TimeSpan.FromMilliseconds(750);

    public TimeSpan SwapChainSwitchConfirmation { get; init; } = TimeSpan.FromSeconds(1);

    public int MinimumSwitchCandidateFrames { get; init; } = 8;

    public double MaximumFrameTimeMs { get; init; } = 60_000d;

    public double MaximumAuxiliaryMetricMs { get; init; } = 60_000d;
}

public readonly record struct GameFrameValidationResult(
    GameFrameSampleQuality Quality,
    GameFrameSample? Sample,
    bool IsAccepted,
    bool StateChanged);

public sealed class GameFrameValidationPipeline
{
    private const string SingleStreamKey = "<single-stream>";
    private readonly GameCaptureWarmupOptions options;
    private readonly Dictionary<string, SwapChainCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<double> recentFrameTimes;
    private DateTimeOffset? warmupStartedAt;
    private DateTimeOffset? stableAt;
    private DateTimeOffset? lastExplicitTimestamp;
    private double? lastCaptureElapsedSeconds;
    private string? primarySwapChain;
    private string? pendingSwitch;
    private DateTimeOffset? pendingSwitchStartedAt;
    private long warmupCandidates;
    private long warmupDiscarded;
    private long nonPrimary;
    private long invalidFrameTime;
    private long invalidTimestamp;
    private long sanitizedFields;
    private int swapChainSwitches;
    private bool compatibilityFallback;

    public GameFrameValidationPipeline(GameCaptureWarmupOptions? options = null)
    {
        this.options = options ?? new GameCaptureWarmupOptions();
        ValidateOptions(this.options);
        recentFrameTimes = new Queue<double>(this.options.StabilityWindowSize);
    }

    public GameCaptureWarmupState State { get; private set; } = GameCaptureWarmupState.WaitingForHeader;

    public void AcceptHeader()
    {
        if (State == GameCaptureWarmupState.WaitingForHeader)
        {
            State = GameCaptureWarmupState.WaitingForStableSwapChain;
        }
    }

    public GameFrameValidationResult Process(GameFrameSample sample, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(sample);
        GameCaptureWarmupState previousState = State;
        if (State == GameCaptureWarmupState.WaitingForHeader)
        {
            return Reject(GameFrameSampleQuality.SchemaSuspect, previousState);
        }

        if (!IsValidFrameTime(sample.FrameTimeMs))
        {
            invalidFrameTime++;
            return Reject(GameFrameSampleQuality.InvalidFrameTime, previousState);
        }

        if (!IsTimestampValid(sample))
        {
            invalidTimestamp++;
            return Reject(GameFrameSampleQuality.NonMonotonicTimestamp, previousState);
        }

        GameFrameSample sanitized = SanitizeMetrics(sample);
        string key = NormalizeSwapChain(sample.SwapChainAddress);
        compatibilityFallback |= key == SingleStreamKey;
        SwapChainCandidate? candidate = GetOrCreateCandidate(key, observedAt);
        if (candidate is null)
        {
            nonPrimary++;
            return Reject(GameFrameSampleQuality.NonPrimarySwapChain, previousState);
        }

        candidate.Observe(observedAt, sample.FrameTimeMs!.Value);
        if (candidate.FrameCount == 1)
        {
            warmupCandidates++;
            warmupDiscarded++;
            warmupStartedAt ??= observedAt;
            State = GameCaptureWarmupState.WarmingUp;
            return Reject(GameFrameSampleQuality.FirstSwapChainFrame, previousState);
        }

        if (State is GameCaptureWarmupState.WaitingForStableSwapChain or GameCaptureWarmupState.WarmingUp or GameCaptureWarmupState.Resetting)
        {
            warmupStartedAt ??= observedAt;
            warmupCandidates++;
            warmupDiscarded++;
            AddRecentFrameTime(sample.FrameTimeMs.Value);
            SwapChainCandidate selected = SelectDominantCandidate();
            bool maximumWaitReached = observedAt - warmupStartedAt.Value >= options.MaximumWarmupDuration;
            bool ready = selected.FrameCount >= options.MinimumCandidateFrames
                && observedAt - warmupStartedAt.Value >= options.MinimumStableDuration
                && HasStableWindow();
            if (!ready && !maximumWaitReached)
            {
                State = GameCaptureWarmupState.WarmingUp;
                GameFrameSampleQuality quality = IsIsolatedStartupOutlier(sample.FrameTimeMs.Value)
                    ? GameFrameSampleQuality.ImplausibleStartupOutlier
                    : GameFrameSampleQuality.WarmupDiscarded;
                return Reject(quality, previousState);
            }

            primarySwapChain = selected.Key;
            selected.LastAcceptedAt = observedAt;
            stableAt ??= observedAt;
            State = GameCaptureWarmupState.Stable;
            return Reject(GameFrameSampleQuality.WarmupDiscarded, previousState);
        }

        if (!string.Equals(key, primarySwapChain, StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldSwitch(candidate, observedAt))
            {
                primarySwapChain = key;
                pendingSwitch = null;
                pendingSwitchStartedAt = null;
                swapChainSwitches++;
                warmupStartedAt = observedAt;
                recentFrameTimes.Clear();
                State = GameCaptureWarmupState.Resetting;
                warmupCandidates++;
                warmupDiscarded++;
                return Reject(GameFrameSampleQuality.WarmupDiscarded, previousState);
            }

            nonPrimary++;
            return Reject(GameFrameSampleQuality.NonPrimarySwapChain, previousState);
        }

        candidate.LastAcceptedAt = observedAt;
        AddRecentFrameTime(sample.FrameTimeMs.Value);
        return new GameFrameValidationResult(
            sanitizedFields > 0 && HasAnySanitizedDifference(sample, sanitized)
                ? GameFrameSampleQuality.SanitizedMetricField
                : GameFrameSampleQuality.Accepted,
            sanitized,
            IsAccepted: true,
            StateChanged: previousState != State);
    }

    public void Complete() => State = GameCaptureWarmupState.Completed;

    public GameFrameQualityDiagnostics GetDiagnostics(DateTimeOffset now)
    {
        double duration = warmupStartedAt.HasValue
            ? Math.Max(0d, ((stableAt ?? now) - warmupStartedAt.Value).TotalSeconds)
            : 0d;
        return new GameFrameQualityDiagnostics
        {
            WarmupCandidateSampleCount = warmupCandidates,
            WarmupDiscardedSampleCount = warmupDiscarded,
            NonPrimarySwapChainSampleCount = nonPrimary,
            InvalidFrameTimeSampleCount = invalidFrameTime,
            InvalidTimestampSampleCount = invalidTimestamp,
            SanitizedMetricFieldCount = sanitizedFields,
            PrimarySwapChainAddress = primarySwapChain == SingleStreamKey ? null : primarySwapChain,
            SwapChainSwitchCount = swapChainSwitches,
            CaptureWarmupDurationSeconds = duration,
            UsedCompatibilityFallback = compatibilityFallback
        };
    }

    private bool ShouldSwitch(SwapChainCandidate candidate, DateTimeOffset observedAt)
    {
        if (primarySwapChain is null || !candidates.TryGetValue(primarySwapChain, out SwapChainCandidate? primary))
        {
            return true;
        }

        if (observedAt - primary.LastSeenAt < options.PrimarySwapChainInactivity
            || candidate.FrameCount < options.MinimumSwitchCandidateFrames)
        {
            pendingSwitch = null;
            pendingSwitchStartedAt = null;
            return false;
        }

        if (!string.Equals(pendingSwitch, candidate.Key, StringComparison.OrdinalIgnoreCase))
        {
            pendingSwitch = candidate.Key;
            pendingSwitchStartedAt = observedAt;
            return false;
        }

        return pendingSwitchStartedAt.HasValue
            && observedAt - pendingSwitchStartedAt.Value >= options.SwapChainSwitchConfirmation;
    }

    private SwapChainCandidate? GetOrCreateCandidate(string key, DateTimeOffset observedAt)
    {
        if (candidates.TryGetValue(key, out SwapChainCandidate? candidate))
        {
            return candidate;
        }

        if (candidates.Count >= options.MaximumTrackedSwapChains)
        {
            return null;
        }

        candidate = new SwapChainCandidate(key, observedAt);
        candidates.Add(key, candidate);
        return candidate;
    }

    private SwapChainCandidate SelectDominantCandidate() => candidates.Values
        .OrderByDescending(static item => item.FrameCount)
        .ThenByDescending(static item => item.LastSeenAt)
        .First();

    private void AddRecentFrameTime(double value)
    {
        recentFrameTimes.Enqueue(value);
        while (recentFrameTimes.Count > options.StabilityWindowSize)
        {
            recentFrameTimes.Dequeue();
        }
    }

    private bool HasStableWindow()
    {
        if (recentFrameTimes.Count < options.MinimumStableValues)
        {
            return false;
        }

        double[] values = recentFrameTimes.OrderBy(static value => value).ToArray();
        double median = values[values.Length / 2];
        int stable = values.Count(value => value >= median / options.StabilityRatio
            && value <= median * options.StabilityRatio);
        return stable >= options.MinimumStableValues;
    }

    private bool IsIsolatedStartupOutlier(double value)
    {
        if (recentFrameTimes.Count < options.MinimumStableValues)
        {
            return false;
        }

        double[] values = recentFrameTimes.OrderBy(static item => item).ToArray();
        double median = values[values.Length / 2];
        return value < median / options.StartupOutlierRatio;
    }

    private bool IsTimestampValid(GameFrameSample sample)
    {
        if (sample.CaptureElapsedSeconds is < 0d
            || lastCaptureElapsedSeconds.HasValue
            && sample.CaptureElapsedSeconds.HasValue
            && sample.CaptureElapsedSeconds.Value < lastCaptureElapsedSeconds.Value)
        {
            return false;
        }

        if (sample.HasExplicitTimestamp && lastExplicitTimestamp.HasValue && sample.Timestamp < lastExplicitTimestamp.Value)
        {
            return false;
        }

        if (sample.CaptureElapsedSeconds.HasValue)
        {
            lastCaptureElapsedSeconds = sample.CaptureElapsedSeconds;
        }

        if (sample.HasExplicitTimestamp)
        {
            lastExplicitTimestamp = sample.Timestamp;
        }

        return true;
    }

    private GameFrameSample SanitizeMetrics(GameFrameSample sample)
    {
        double? cpuBusy = SanitizeAuxiliary(sample.CpuBusyMs);
        double? cpuWait = SanitizeAuxiliary(sample.CpuWaitMs);
        double? gpuLatency = SanitizeAuxiliary(sample.GpuLatencyMs);
        double? gpuTime = SanitizeAuxiliary(sample.GpuTimeMs);
        double? gpuBusy = SanitizeAuxiliary(sample.GpuBusyMs);
        double? gpuWait = SanitizeAuxiliary(sample.GpuWaitMs);
        double? renderLatency = SanitizeAuxiliary(sample.RenderLatencyMs);
        double? displayLatency = SanitizeAuxiliary(sample.DisplayLatencyMs);
        double? displayedTime = SanitizeAuxiliary(sample.DisplayedTimeMs);
        double? clickLatency = SanitizeAuxiliary(sample.ClickToPhotonLatencyMs);
        return new GameFrameSample
        {
            CaptureSessionId = sample.CaptureSessionId,
            Timestamp = sample.Timestamp,
            HasExplicitTimestamp = sample.HasExplicitTimestamp,
            CaptureElapsedSeconds = sample.CaptureElapsedSeconds,
            ProcessId = sample.ProcessId,
            ProcessName = sample.ProcessName,
            FrameTimeMs = sample.FrameTimeMs,
            Fps = sample.Fps,
            CpuBusyMs = cpuBusy,
            CpuWaitMs = cpuWait,
            GpuLatencyMs = gpuLatency,
            GpuTimeMs = gpuTime,
            GpuBusyMs = gpuBusy,
            GpuWaitMs = gpuWait,
            RenderLatencyMs = renderLatency,
            DisplayLatencyMs = displayLatency,
            DisplayedTimeMs = displayedTime,
            ClickToPhotonLatencyMs = clickLatency,
            Runtime = sample.Runtime,
            PresentMode = sample.PresentMode,
            SwapChainAddress = sample.SwapChainAddress,
            FrameType = sample.FrameType
        };
    }

    private double? SanitizeAuxiliary(double? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        if (!double.IsFinite(value.Value) || value.Value < 0d || value.Value > options.MaximumAuxiliaryMetricMs)
        {
            sanitizedFields++;
            return null;
        }

        return value;
    }

    private static bool HasAnySanitizedDifference(GameFrameSample original, GameFrameSample sanitized) =>
        original.CpuBusyMs != sanitized.CpuBusyMs
        || original.CpuWaitMs != sanitized.CpuWaitMs
        || original.GpuLatencyMs != sanitized.GpuLatencyMs
        || original.GpuTimeMs != sanitized.GpuTimeMs
        || original.GpuBusyMs != sanitized.GpuBusyMs
        || original.GpuWaitMs != sanitized.GpuWaitMs
        || original.RenderLatencyMs != sanitized.RenderLatencyMs
        || original.DisplayLatencyMs != sanitized.DisplayLatencyMs
        || original.DisplayedTimeMs != sanitized.DisplayedTimeMs
        || original.ClickToPhotonLatencyMs != sanitized.ClickToPhotonLatencyMs;

    private bool IsValidFrameTime(double? value) => value.HasValue
        && double.IsFinite(value.Value)
        && value.Value > 0d
        && value.Value <= options.MaximumFrameTimeMs;

    private GameFrameValidationResult Reject(GameFrameSampleQuality quality, GameCaptureWarmupState previousState) =>
        new(quality, null, IsAccepted: false, StateChanged: previousState != State);

    private static string NormalizeSwapChain(string? value) =>
        string.IsNullOrWhiteSpace(value) ? SingleStreamKey : value.Trim();

    private static void ValidateOptions(GameCaptureWarmupOptions options)
    {
        if (options.MinimumCandidateFrames < 2
            || options.StabilityWindowSize < options.MinimumStableValues
            || options.MinimumStableValues < 2
            || options.MaximumTrackedSwapChains < 1
            || options.MinimumStableDuration < TimeSpan.Zero
            || options.MaximumWarmupDuration < options.MinimumStableDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private sealed class SwapChainCandidate
    {
        public SwapChainCandidate(string key, DateTimeOffset observedAt)
        {
            Key = key;
            FirstSeenAt = observedAt;
            LastSeenAt = observedAt;
        }

        public string Key { get; }
        public DateTimeOffset FirstSeenAt { get; }
        public DateTimeOffset LastSeenAt { get; private set; }
        public DateTimeOffset? LastAcceptedAt { get; set; }
        public long FrameCount { get; private set; }

        public void Observe(DateTimeOffset observedAt, double frameTime)
        {
            _ = frameTime;
            FrameCount++;
            LastSeenAt = observedAt;
        }
    }
}
