using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class GameCaptureWarmupOptions
{
    public int MinimumCandidateFrames { get; init; } = 12;

    public TimeSpan MinimumStableDuration { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaximumWarmupDuration { get; init; } = TimeSpan.FromSeconds(3);

    public int StabilityWindowSize { get; init; } = 31;

    public int MinimumStableValues { get; init; } = 8;

    public double StabilityRatio { get; init; } = 4d;

    public double StartupOutlierRatio { get; init; } = 20d;

    public double StableFastOutlierRatio { get; init; } = 3d;

    public int FrameTimeTransitionConfirmationFrames { get; init; } = 6;

    public int AuxiliaryTransitionConfirmationFrames { get; init; } = 4;

    public int MaximumTrackedSwapChains { get; init; } = 16;

    public TimeSpan PrimarySwapChainInactivity { get; init; } = TimeSpan.FromMilliseconds(750);

    public TimeSpan SwapChainSwitchConfirmation { get; init; } = TimeSpan.FromSeconds(1);

    public int MinimumSwitchCandidateFrames { get; init; } = 8;

    public double MaximumFrameTimeMs { get; init; } = 60_000d;

    public bool HistoricalReplayMode { get; init; }
}

public readonly record struct GameFrameValidationResult(
    GameFrameSampleQuality Quality,
    GameFrameSample? Sample,
    bool IsAccepted,
    bool StateChanged,
    bool PrimarySwapChainChanged = false);

public sealed class GameFrameValidationPipeline
{
    private const string SingleStreamKey = "<single-stream>";
    private static readonly AuxiliaryMetricRule[] AuxiliaryRules =
    [
        new(AuxiliaryMetric.CpuBusy, 10_000d, 8d, 50d),
        new(AuxiliaryMetric.CpuWait, 120_000d, 8d, 250d),
        new(AuxiliaryMetric.GpuLatency, 120_000d, 8d, 250d),
        new(AuxiliaryMetric.GpuTime, 10_000d, 8d, 50d),
        new(AuxiliaryMetric.GpuBusy, 10_000d, 8d, 50d),
        new(AuxiliaryMetric.GpuWait, 120_000d, 8d, 250d),
        new(AuxiliaryMetric.RenderLatency, 120_000d, 8d, 250d),
        new(AuxiliaryMetric.DisplayLatency, 120_000d, 8d, 250d),
        new(AuxiliaryMetric.DisplayedTime, 120_000d, 8d, 250d),
        new(AuxiliaryMetric.ClickToPhotonLatency, 120_000d, 8d, 250d)
    ];

    private readonly GameCaptureWarmupOptions options;
    private readonly Dictionary<string, SwapChainCandidate> candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly long[] sanitizedByMetric = new long[(int)AuxiliaryMetric.Count];
    // Sustained maximum FPS is the peak reciprocal of the trailing mean frame time.
    // The window holds at most eight accepted frames and does not emit a value before
    // four frames, so a single tiny FrameTime can never define the displayed maximum.
    private readonly SustainedFpsEstimator sustainedFps = new(8, 4);
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
    private long frameTimeOutliers;
    private long duplicateCaptureElapsed;
    private long regressedCaptureElapsed;
    private long duplicateExplicitTimestamp;
    private long regressedExplicitTimestamp;
    private long missingTimestamp;
    private long compatibilityFallbackSamples;
    private long transitionCandidates;
    private long transitionConfirmed;
    private long sanitizedFields;
    private long invalidAuxiliaryFields;
    private long auxiliaryOutlierFields;
    private int swapChainSwitches;
    private bool compatibilityFallback;
    private bool primarySelectionUncertain;
    private double? rawMaximumFps;

    public GameFrameValidationPipeline(GameCaptureWarmupOptions? options = null)
    {
        this.options = options ?? new GameCaptureWarmupOptions();
        ValidateOptions(this.options);
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

        double fps = 1000d / sample.FrameTimeMs!.Value;
        rawMaximumFps = !rawMaximumFps.HasValue ? fps : Math.Max(rawMaximumFps.Value, fps);
        GameFrameSampleQuality? timestampRejection = ValidateTimestamp(sample, out bool timestampFallback);
        if (timestampRejection.HasValue)
        {
            return Reject(timestampRejection.Value, previousState);
        }

        string key = NormalizeSwapChain(sample.SwapChainAddress);
        compatibilityFallback |= key == SingleStreamKey || timestampFallback;
        SwapChainCandidate? candidate = GetOrCreateCandidate(key, observedAt);
        if (candidate is null)
        {
            nonPrimary++;
            return Reject(GameFrameSampleQuality.NonPrimarySwapChain, previousState);
        }

        candidate.ObserveMetadata(sample, observedAt);
        if (options.HistoricalReplayMode)
        {
            return ProcessHistorical(sample, candidate, key, observedAt, timestampFallback, previousState);
        }

        if (candidate.FrameCount == 1)
        {
            candidate.SeedFrameTime(sample.FrameTimeMs.Value);
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
            bool startupOutlier = candidate.IsFastOutlier(sample.FrameTimeMs.Value, options.StartupOutlierRatio);
            candidate.SeedFrameTime(sample.FrameTimeMs.Value);
            SwapChainCandidate selected = SelectDominantCandidate(observedAt);
            bool maximumWaitReached = observedAt - warmupStartedAt.Value >= options.MaximumWarmupDuration;
            bool ready = selected.FrameCount >= options.MinimumCandidateFrames
                && observedAt - selected.FirstSeenAt >= options.MinimumStableDuration
                && selected.HasStableWindow(options.MinimumStableValues, options.StabilityRatio);
            if (!ready && !maximumWaitReached)
            {
                State = GameCaptureWarmupState.WarmingUp;
                return Reject(
                    startupOutlier
                        ? GameFrameSampleQuality.ImplausibleStartupOutlier
                        : GameFrameSampleQuality.WarmupDiscarded,
                    previousState);
            }

            primarySwapChain = selected.Key;
            selected.LastAcceptedAt = observedAt;
            primarySelectionUncertain = selected.PresentationConfidence == 0;
            stableAt ??= observedAt;
            State = GameCaptureWarmupState.Stable;
            return Reject(GameFrameSampleQuality.WarmupDiscarded, previousState);
        }

        FrameTimeDecision frameTimeDecision = candidate.EvaluateFrameTime(sample.FrameTimeMs.Value);
        if (frameTimeDecision is FrameTimeDecision.Outlier or FrameTimeDecision.TransitionCandidate)
        {
            candidate.LastAnomalyAt = observedAt;
            frameTimeOutliers++;
            if (frameTimeDecision == FrameTimeDecision.TransitionCandidate)
            {
                transitionCandidates++;
            }

            return Reject(
                frameTimeDecision == FrameTimeDecision.Outlier
                    ? GameFrameSampleQuality.FrameTimeOutlier
                    : GameFrameSampleQuality.StableLevelTransitionCandidate,
                previousState);
        }

        bool primaryChanged = false;
        if (!string.Equals(key, primarySwapChain, StringComparison.OrdinalIgnoreCase))
        {
            if (!ShouldSwitch(candidate, observedAt))
            {
                nonPrimary++;
                return Reject(GameFrameSampleQuality.NonPrimarySwapChain, previousState);
            }

            primarySwapChain = key;
            pendingSwitch = null;
            pendingSwitchStartedAt = null;
            swapChainSwitches++;
            primarySelectionUncertain = candidate.PresentationConfidence == 0;
            sustainedFps.Clear();
            candidate.ResetTransitionCandidate();
            primaryChanged = true;
        }

        if (frameTimeDecision == FrameTimeDecision.TransitionConfirmed)
        {
            transitionConfirmed++;
        }

        GameFrameSample sanitized = SanitizeMetrics(sample, candidate);
        RecordAccepted(candidate, sanitized, observedAt);
        GameFrameSampleQuality quality = frameTimeDecision == FrameTimeDecision.TransitionConfirmed
            ? GameFrameSampleQuality.StableLevelTransitionConfirmed
            : timestampFallback
                ? GameFrameSampleQuality.MissingTimestampFallback
                : HasAnySanitizedDifference(sample, sanitized)
                    ? GameFrameSampleQuality.SanitizedMetricField
                    : GameFrameSampleQuality.Accepted;
        return new GameFrameValidationResult(
            quality,
            sanitized,
            IsAccepted: true,
            StateChanged: previousState != State,
            PrimarySwapChainChanged: primaryChanged);
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
            FrameTimeOutlierSampleCount = frameTimeOutliers,
            DuplicateCaptureElapsedSampleCount = duplicateCaptureElapsed,
            RegressedCaptureElapsedSampleCount = regressedCaptureElapsed,
            DuplicateExplicitTimestampSampleCount = duplicateExplicitTimestamp,
            RegressedExplicitTimestampSampleCount = regressedExplicitTimestamp,
            MissingTimestampSampleCount = missingTimestamp,
            CompatibilityFallbackSampleCount = compatibilityFallbackSamples,
            StableLevelTransitionCandidateSampleCount = transitionCandidates,
            StableLevelTransitionConfirmedCount = transitionConfirmed,
            SanitizedMetricFieldCount = sanitizedFields,
            InvalidAuxiliaryMetricFieldCount = invalidAuxiliaryFields,
            AuxiliaryMetricOutlierFieldCount = auxiliaryOutlierFields,
            CpuBusySanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.CpuBusy],
            CpuWaitSanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.CpuWait],
            GpuLatencySanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.GpuLatency],
            GpuTimeSanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.GpuTime],
            GpuBusySanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.GpuBusy],
            GpuWaitSanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.GpuWait],
            RenderLatencySanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.RenderLatency],
            DisplayLatencySanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.DisplayLatency],
            DisplayedTimeSanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.DisplayedTime],
            ClickToPhotonLatencySanitizedCount = sanitizedByMetric[(int)AuxiliaryMetric.ClickToPhotonLatency],
            PrimarySwapChainAddress = primarySwapChain == SingleStreamKey ? null : primarySwapChain,
            SwapChainSwitchCount = swapChainSwitches,
            CaptureWarmupDurationSeconds = duration,
            UsedCompatibilityFallback = compatibilityFallback,
            PrimarySwapChainSelectionUncertain = primarySelectionUncertain,
            RawMaximumFps = rawMaximumFps,
            SustainedMaximumFps = sustainedFps.MaximumFps
        };
    }

    private GameFrameValidationResult ProcessHistorical(
        GameFrameSample sample,
        SwapChainCandidate candidate,
        string key,
        DateTimeOffset observedAt,
        bool timestampFallback,
        GameCaptureWarmupState previousState)
    {
        bool primaryChanged = false;
        if (primarySwapChain is null)
        {
            primarySwapChain = key;
            primarySelectionUncertain = candidate.PresentationConfidence == 0;
            State = GameCaptureWarmupState.Stable;
        }
        else if (!string.Equals(key, primarySwapChain, StringComparison.OrdinalIgnoreCase))
        {
            if (!ShouldSwitch(candidate, observedAt))
            {
                nonPrimary++;
                return Reject(GameFrameSampleQuality.NonPrimarySwapChain, previousState);
            }

            primarySwapChain = key;
            swapChainSwitches++;
            sustainedFps.Clear();
            primaryChanged = true;
        }

        FrameTimeDecision decision = candidate.EvaluateFrameTime(sample.FrameTimeMs!.Value);
        if (decision is FrameTimeDecision.Outlier or FrameTimeDecision.TransitionCandidate)
        {
            frameTimeOutliers++;
            candidate.LastAnomalyAt = observedAt;
            if (decision == FrameTimeDecision.TransitionCandidate)
            {
                transitionCandidates++;
            }

            return Reject(
                decision == FrameTimeDecision.Outlier
                    ? GameFrameSampleQuality.FrameTimeOutlier
                    : GameFrameSampleQuality.StableLevelTransitionCandidate,
                previousState);
        }

        if (decision == FrameTimeDecision.TransitionConfirmed)
        {
            transitionConfirmed++;
        }

        GameFrameSample sanitized = SanitizeMetrics(sample, candidate);
        RecordAccepted(candidate, sanitized, observedAt);
        GameFrameSampleQuality quality = decision == FrameTimeDecision.TransitionConfirmed
            ? GameFrameSampleQuality.StableLevelTransitionConfirmed
            : timestampFallback
                ? GameFrameSampleQuality.MissingTimestampFallback
                : HasAnySanitizedDifference(sample, sanitized)
                    ? GameFrameSampleQuality.SanitizedMetricField
                    : GameFrameSampleQuality.Accepted;
        return new GameFrameValidationResult(
            quality,
            sanitized,
            IsAccepted: true,
            StateChanged: previousState != State,
            PrimarySwapChainChanged: primaryChanged);
    }

    private void RecordAccepted(SwapChainCandidate candidate, GameFrameSample sample, DateTimeOffset observedAt)
    {
        candidate.AcceptedFrameCount++;
        candidate.LastAcceptedAt = observedAt;
        sustainedFps.Add(sample.FrameTimeMs!.Value);
    }

    private bool ShouldSwitch(SwapChainCandidate candidate, DateTimeOffset observedAt)
    {
        if (primarySwapChain is null || !candidates.TryGetValue(primarySwapChain, out SwapChainCandidate? primary))
        {
            return true;
        }

        bool candidateReady = candidate.FrameCount >= options.MinimumSwitchCandidateFrames
            && candidate.ConsecutiveStableFrames >= options.MinimumStableValues
            && candidate.HasStableWindow(options.MinimumStableValues, options.StabilityRatio)
            && candidate.CountRecent(observedAt, TimeSpan.FromSeconds(1)) >= options.MinimumStableValues;
        if (observedAt - primary.LastSeenAt < options.PrimarySwapChainInactivity || !candidateReady)
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

        candidate = new SwapChainCandidate(key, observedAt, options, AuxiliaryRules);
        candidates.Add(key, candidate);
        return candidate;
    }

    private SwapChainCandidate SelectDominantCandidate(DateTimeOffset observedAt)
    {
        SwapChainCandidate? selected = null;
        long selectedScore = long.MinValue;
        foreach (SwapChainCandidate candidate in candidates.Values)
        {
            long score = candidate.SelectionScore(observedAt, primarySwapChain);
            if (selected is null || score > selectedScore)
            {
                selected = candidate;
                selectedScore = score;
            }
        }

        return selected!;
    }

    private GameFrameSampleQuality? ValidateTimestamp(GameFrameSample sample, out bool fallback)
    {
        fallback = !sample.CaptureElapsedSeconds.HasValue && !sample.HasExplicitTimestamp;
        if (sample.CaptureElapsedSeconds.HasValue)
        {
            double current = sample.CaptureElapsedSeconds.Value;
            if (!double.IsFinite(current) || current < 0d
                || lastCaptureElapsedSeconds.HasValue && current < lastCaptureElapsedSeconds.Value)
            {
                regressedCaptureElapsed++;
                return GameFrameSampleQuality.RegressedCaptureElapsed;
            }

            if (lastCaptureElapsedSeconds.HasValue && current == lastCaptureElapsedSeconds.Value)
            {
                duplicateCaptureElapsed++;
                return GameFrameSampleQuality.DuplicateCaptureElapsed;
            }
        }

        if (sample.HasExplicitTimestamp && lastExplicitTimestamp.HasValue)
        {
            if (sample.Timestamp < lastExplicitTimestamp.Value)
            {
                regressedExplicitTimestamp++;
                return GameFrameSampleQuality.RegressedExplicitTimestamp;
            }

            if (sample.Timestamp == lastExplicitTimestamp.Value)
            {
                duplicateExplicitTimestamp++;
                return GameFrameSampleQuality.DuplicateExplicitTimestamp;
            }
        }

        if (fallback)
        {
            missingTimestamp++;
            compatibilityFallbackSamples++;
        }

        if (sample.CaptureElapsedSeconds.HasValue)
        {
            lastCaptureElapsedSeconds = sample.CaptureElapsedSeconds.Value;
        }

        if (sample.HasExplicitTimestamp)
        {
            lastExplicitTimestamp = sample.Timestamp;
        }

        return null;
    }

    private GameFrameSample SanitizeMetrics(GameFrameSample sample, SwapChainCandidate candidate)
    {
        double? cpuBusy = SanitizeAuxiliary(candidate, AuxiliaryMetric.CpuBusy, sample.CpuBusyMs);
        double? cpuWait = SanitizeAuxiliary(candidate, AuxiliaryMetric.CpuWait, sample.CpuWaitMs);
        double? gpuLatency = SanitizeAuxiliary(candidate, AuxiliaryMetric.GpuLatency, sample.GpuLatencyMs);
        double? gpuTime = SanitizeAuxiliary(candidate, AuxiliaryMetric.GpuTime, sample.GpuTimeMs);
        double? gpuBusy = SanitizeAuxiliary(candidate, AuxiliaryMetric.GpuBusy, sample.GpuBusyMs);
        double? gpuWait = SanitizeAuxiliary(candidate, AuxiliaryMetric.GpuWait, sample.GpuWaitMs);
        double? renderLatency = SanitizeAuxiliary(candidate, AuxiliaryMetric.RenderLatency, sample.RenderLatencyMs);
        double? displayLatency = SanitizeAuxiliary(candidate, AuxiliaryMetric.DisplayLatency, sample.DisplayLatencyMs);
        double? displayedTime = SanitizeAuxiliary(candidate, AuxiliaryMetric.DisplayedTime, sample.DisplayedTimeMs);
        double? clickLatency = SanitizeAuxiliary(candidate, AuxiliaryMetric.ClickToPhotonLatency, sample.ClickToPhotonLatencyMs);
        return new GameFrameSample
        {
            CaptureSessionId = sample.CaptureSessionId,
            Timestamp = sample.Timestamp,
            HasExplicitTimestamp = sample.HasExplicitTimestamp,
            CaptureElapsedSeconds = sample.CaptureElapsedSeconds,
            ProcessId = sample.ProcessId,
            ProcessName = sample.ProcessName,
            FrameTimeMs = sample.FrameTimeMs,
            Fps = 1000d / sample.FrameTimeMs!.Value,
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

    private double? SanitizeAuxiliary(SwapChainCandidate candidate, AuxiliaryMetric metric, double? value)
    {
        double? sanitized = candidate.Sanitize(metric, value, out AuxiliarySanitizationReason reason);
        if (reason == AuxiliarySanitizationReason.None)
        {
            return sanitized;
        }

        sanitizedFields++;
        sanitizedByMetric[(int)metric]++;
        if (reason == AuxiliarySanitizationReason.Outlier)
        {
            auxiliaryOutlierFields++;
        }
        else
        {
            invalidAuxiliaryFields++;
        }

        return sanitized;
    }

    private bool IsValidFrameTime(double? value) => value.HasValue
        && double.IsFinite(value.Value)
        && value.Value > 0d
        && value.Value <= options.MaximumFrameTimeMs;

    private GameFrameValidationResult Reject(GameFrameSampleQuality quality, GameCaptureWarmupState previousState) =>
        new(quality, null, IsAccepted: false, StateChanged: previousState != State);

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

    private static string NormalizeSwapChain(string? value) =>
        string.IsNullOrWhiteSpace(value) ? SingleStreamKey : value.Trim();

    private static void ValidateOptions(GameCaptureWarmupOptions options)
    {
        if (options.MinimumCandidateFrames < 2
            || options.StabilityWindowSize < options.MinimumStableValues
            || options.MinimumStableValues < 2
            || options.FrameTimeTransitionConfirmationFrames < 2
            || options.AuxiliaryTransitionConfirmationFrames < 2
            || options.StableFastOutlierRatio <= 1d
            || options.MaximumTrackedSwapChains < 1
            || options.MinimumStableDuration < TimeSpan.Zero
            || options.MaximumWarmupDuration < options.MinimumStableDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private enum AuxiliaryMetric
    {
        CpuBusy,
        CpuWait,
        GpuLatency,
        GpuTime,
        GpuBusy,
        GpuWait,
        RenderLatency,
        DisplayLatency,
        DisplayedTime,
        ClickToPhotonLatency,
        Count
    }

    private enum AuxiliarySanitizationReason
    {
        None,
        NonFinite,
        Negative,
        StructuralLimit,
        Outlier
    }

    private enum FrameTimeDecision
    {
        Accepted,
        Outlier,
        TransitionCandidate,
        TransitionConfirmed
    }

    private readonly record struct AuxiliaryMetricRule(
        AuxiliaryMetric Metric,
        double StructuralMaximum,
        double OutlierRatio,
        double MinimumOutlierDelta);

    private sealed class SwapChainCandidate
    {
        private readonly DateTimeOffset[] recentActivity = new DateTimeOffset[64];
        private readonly RobustFrameTimeFilter frameTimes;
        private readonly AuxiliaryMetricFilter[] auxiliaryFilters;
        private int activityCount;
        private int activityNext;

        public SwapChainCandidate(
            string key,
            DateTimeOffset observedAt,
            GameCaptureWarmupOptions options,
            IReadOnlyList<AuxiliaryMetricRule> rules)
        {
            Key = key;
            FirstSeenAt = observedAt;
            LastSeenAt = observedAt;
            frameTimes = new RobustFrameTimeFilter(options);
            auxiliaryFilters = new AuxiliaryMetricFilter[(int)AuxiliaryMetric.Count];
            for (int index = 0; index < rules.Count; index++)
            {
                AuxiliaryMetricRule rule = rules[index];
                auxiliaryFilters[(int)rule.Metric] = new AuxiliaryMetricFilter(
                    options.StabilityWindowSize,
                    options.MinimumStableValues,
                    options.AuxiliaryTransitionConfirmationFrames,
                    rule);
            }
        }

        public string Key { get; }
        public DateTimeOffset FirstSeenAt { get; }
        public DateTimeOffset LastSeenAt { get; private set; }
        public DateTimeOffset? LastAcceptedAt { get; set; }
        public DateTimeOffset? LastAnomalyAt { get; set; }
        public long FrameCount { get; private set; }
        public long AcceptedFrameCount { get; set; }
        public int ConsecutiveStableFrames { get; private set; }
        public string? PresentMode { get; private set; }
        public string? FrameType { get; private set; }

        public int PresentationConfidence
        {
            get
            {
                int score = 0;
                if (PresentMode?.Contains("Independent Flip", StringComparison.OrdinalIgnoreCase) == true
                    || PresentMode?.Contains("Hardware", StringComparison.OrdinalIgnoreCase) == true)
                {
                    score += 3;
                }
                if (string.Equals(FrameType, "Application", StringComparison.OrdinalIgnoreCase))
                {
                    score += 1;
                }
                return score;
            }
        }

        public void ObserveMetadata(GameFrameSample sample, DateTimeOffset observedAt)
        {
            FrameCount++;
            LastSeenAt = observedAt;
            if (!string.IsNullOrWhiteSpace(sample.PresentMode)) PresentMode = sample.PresentMode;
            if (!string.IsNullOrWhiteSpace(sample.FrameType)) FrameType = sample.FrameType;
            recentActivity[activityNext] = observedAt;
            activityNext = (activityNext + 1) % recentActivity.Length;
            if (activityCount < recentActivity.Length) activityCount++;
        }

        public void SeedFrameTime(double value)
        {
            frameTimes.Seed(value);
            ConsecutiveStableFrames++;
        }

        public FrameTimeDecision EvaluateFrameTime(double value)
        {
            FrameTimeDecision decision = frameTimes.Evaluate(value);
            if (decision is FrameTimeDecision.Accepted or FrameTimeDecision.TransitionConfirmed)
            {
                ConsecutiveStableFrames++;
            }
            else
            {
                ConsecutiveStableFrames = 0;
            }
            return decision;
        }

        public bool IsFastOutlier(double value, double ratio) => frameTimes.IsFastOutlier(value, ratio);

        public bool HasStableWindow(int minimumValues, double ratio) => frameTimes.IsStable(minimumValues, ratio);

        public double? Sanitize(
            AuxiliaryMetric metric,
            double? value,
            out AuxiliarySanitizationReason reason) =>
            auxiliaryFilters[(int)metric].Filter(value, out reason);

        public void ResetTransitionCandidate() => frameTimes.ResetTransitionCandidate();

        public int CountRecent(DateTimeOffset now, TimeSpan window)
        {
            DateTimeOffset cutoff = now - window;
            int count = 0;
            for (int index = 0; index < activityCount; index++)
            {
                if (recentActivity[index] >= cutoff) count++;
            }
            return count;
        }

        public long SelectionScore(DateTimeOffset observedAt, string? currentPrimary)
        {
            long score = CountRecent(observedAt, TimeSpan.FromSeconds(1)) * 8L;
            score += Math.Min(ConsecutiveStableFrames, 64) * 4L;
            score += Math.Min(FrameCount, 64L);
            // Presentation metadata is only a soft preference: it cannot reject a chain,
            // but an application-owned independent-flip chain should withstand a noisy
            // composed overlay producing roughly twice as many rows during selection.
            score += PresentationConfidence * 64L;
            if (string.Equals(Key, currentPrimary, StringComparison.OrdinalIgnoreCase)) score += 256L;
            if (observedAt - LastSeenAt > TimeSpan.FromMilliseconds(250)) score -= 128L;
            return score;
        }
    }

    private sealed class RobustFrameTimeFilter
    {
        private readonly FixedDoubleWindow baseline;
        private readonly FixedDoubleWindow transition;
        private readonly int minimumValues;
        private readonly int confirmationFrames;
        private readonly double fastOutlierRatio;

        public RobustFrameTimeFilter(GameCaptureWarmupOptions options)
        {
            baseline = new FixedDoubleWindow(options.StabilityWindowSize);
            transition = new FixedDoubleWindow(options.FrameTimeTransitionConfirmationFrames);
            minimumValues = options.MinimumStableValues;
            confirmationFrames = options.FrameTimeTransitionConfirmationFrames;
            fastOutlierRatio = options.StableFastOutlierRatio;
        }

        public void Seed(double value) => baseline.Add(value);

        public bool IsFastOutlier(double value, double ratio)
        {
            if (baseline.Count < minimumValues) return false;
            if (value >= baseline.Average / ratio) return false;
            (double median, double mad) = baseline.MedianAndMad();
            double deviation = median - value;
            return value < median / ratio
                && deviation > Math.Max(median * 0.15d, mad * 8d);
        }

        public bool IsStable(int required, double ratio)
        {
            if (baseline.Count < required) return false;
            (double median, _) = baseline.MedianAndMad();
            int stable = baseline.CountWithin(median / ratio, median * ratio);
            return stable >= required;
        }

        public FrameTimeDecision Evaluate(double value)
        {
            if (baseline.Count < minimumValues)
            {
                baseline.Add(value);
                transition.Clear();
                return FrameTimeDecision.Accepted;
            }

            if (!IsFastOutlier(value, fastOutlierRatio))
            {
                baseline.Add(value);
                transition.Clear();
                return FrameTimeDecision.Accepted;
            }

            if (transition.Count > 0)
            {
                (double candidateMedian, _) = transition.MedianAndMad();
                if (value < candidateMedian / 1.5d || value > candidateMedian * 1.5d)
                {
                    transition.Clear();
                }
            }

            transition.Add(value);
            // A much faster level is withheld until consecutive values agree. This
            // rejects an isolated spike while allowing a real 60 -> 240 FPS transition
            // (and stable uncapped high-FPS streams) after confirmation.
            if (transition.Count < confirmationFrames)
            {
                return transition.Count == 1
                    ? FrameTimeDecision.Outlier
                    : FrameTimeDecision.TransitionCandidate;
            }

            baseline.Clear();
            transition.CopyValuesTo(baseline);
            transition.Clear();
            return FrameTimeDecision.TransitionConfirmed;
        }

        public void ResetTransitionCandidate() => transition.Clear();
    }

    private sealed class AuxiliaryMetricFilter
    {
        private readonly FixedDoubleWindow baseline;
        private readonly FixedDoubleWindow transition;
        private readonly int minimumValues;
        private readonly int confirmationFrames;
        private readonly AuxiliaryMetricRule rule;

        public AuxiliaryMetricFilter(
            int windowSize,
            int minimumValues,
            int confirmationFrames,
            AuxiliaryMetricRule rule)
        {
            baseline = new FixedDoubleWindow(windowSize);
            transition = new FixedDoubleWindow(confirmationFrames);
            this.minimumValues = minimumValues;
            this.confirmationFrames = confirmationFrames;
            this.rule = rule;
        }

        public double? Filter(double? value, out AuxiliarySanitizationReason reason)
        {
            reason = AuxiliarySanitizationReason.None;
            if (!value.HasValue) return null;
            if (!double.IsFinite(value.Value))
            {
                reason = AuxiliarySanitizationReason.NonFinite;
                return null;
            }
            if (value.Value < 0d)
            {
                reason = AuxiliarySanitizationReason.Negative;
                return null;
            }
            if (value.Value > rule.StructuralMaximum)
            {
                reason = AuxiliarySanitizationReason.StructuralLimit;
                return null;
            }
            if (baseline.Count < minimumValues)
            {
                baseline.Add(value.Value);
                transition.Clear();
                return value;
            }

            double fastThreshold = Math.Max(
                baseline.Average * rule.OutlierRatio,
                baseline.Average + rule.MinimumOutlierDelta);
            if (value.Value <= fastThreshold)
            {
                baseline.Add(value.Value);
                transition.Clear();
                return value;
            }

            (double median, double mad) = baseline.MedianAndMad();
            double threshold = Math.Max(
                median * rule.OutlierRatio,
                median + Math.Max(rule.MinimumOutlierDelta, mad * 8d));
            if (value.Value <= threshold)
            {
                baseline.Add(value.Value);
                transition.Clear();
                return value;
            }

            if (transition.Count > 0)
            {
                (double candidateMedian, _) = transition.MedianAndMad();
                if (value.Value < candidateMedian / 2d || value.Value > candidateMedian * 2d)
                {
                    transition.Clear();
                }
            }
            transition.Add(value.Value);
            if (transition.Count >= confirmationFrames)
            {
                baseline.Clear();
                transition.CopyValuesTo(baseline);
                transition.Clear();
                return value;
            }

            reason = AuxiliarySanitizationReason.Outlier;
            return null;
        }
    }

    private sealed class FixedDoubleWindow
    {
        private readonly double[] values;
        private readonly double[] scratch;
        private int next;
        private double sum;

        public FixedDoubleWindow(int capacity)
        {
            values = new double[Math.Max(1, capacity)];
            scratch = new double[values.Length];
        }

        public int Count { get; private set; }

        public double Average => Count > 0 ? sum / Count : 0d;

        public void Add(double value)
        {
            if (Count == values.Length)
            {
                sum -= values[next];
            }
            else
            {
                Count++;
            }
            values[next] = value;
            sum += value;
            next = (next + 1) % values.Length;
        }

        public void Clear()
        {
            Count = 0;
            next = 0;
            sum = 0d;
        }

        public (double Median, double Mad) MedianAndMad()
        {
            for (int index = 0; index < Count; index++) scratch[index] = values[index];
            Array.Sort(scratch, 0, Count);
            double median = scratch[Count / 2];
            for (int index = 0; index < Count; index++) scratch[index] = Math.Abs(values[index] - median);
            Array.Sort(scratch, 0, Count);
            return (median, scratch[Count / 2]);
        }

        public int CountWithin(double minimum, double maximum)
        {
            int result = 0;
            for (int index = 0; index < Count; index++)
            {
                if (values[index] >= minimum && values[index] <= maximum) result++;
            }
            return result;
        }

        public void CopyValuesTo(FixedDoubleWindow target)
        {
            for (int index = 0; index < Count; index++) target.Add(values[index]);
        }
    }

    private sealed class SustainedFpsEstimator
    {
        private readonly double[] frameTimes;
        private readonly int minimumSamples;
        private int next;
        private int count;
        private double sum;

        public SustainedFpsEstimator(int windowSize, int minimumSamples)
        {
            frameTimes = new double[windowSize];
            this.minimumSamples = minimumSamples;
        }

        public double? MaximumFps { get; private set; }

        public void Add(double frameTimeMs)
        {
            if (count == frameTimes.Length)
            {
                sum -= frameTimes[next];
            }
            else
            {
                count++;
            }
            frameTimes[next] = frameTimeMs;
            sum += frameTimeMs;
            next = (next + 1) % frameTimes.Length;
            if (count < minimumSamples) return;
            double fps = 1000d / (sum / count);
            MaximumFps = !MaximumFps.HasValue ? fps : Math.Max(MaximumFps.Value, fps);
        }

        public void Clear()
        {
            next = 0;
            count = 0;
            sum = 0d;
        }
    }
}
