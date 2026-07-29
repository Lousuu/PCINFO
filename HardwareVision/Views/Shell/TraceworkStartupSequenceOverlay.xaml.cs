using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Utilities;
using Color = System.Windows.Media.Color;
using StartupRenderSource = System.Windows.Media.CompositionTarget;
using Point = System.Windows.Point;

namespace HardwareVision.Views.Shell;

public partial class TraceworkStartupSequenceOverlay : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(StartupSequenceSnapshot),
        typeof(TraceworkStartupSequenceOverlay),
        new PropertyMetadata(null, OnSnapshotChanged));

    private bool bottomRailReady;
    private bool commitPlayed;
    private bool commitMinimumPresentationReached;
    private bool commitPendingForProjection;
    private bool commitEvaluationScheduled;
    private bool commitRevealCompensationPending;
    private bool environmentLedgerPlayed;
    private bool identityLedgerPlayed;
    private bool indexPlayed;
    private bool indexPrepared;
    private bool indexRevealRetryScheduled;
    private bool pendingIndexReplayScheduled;
    private bool projectionLedgerPlayed;
    private bool projectionLedgerReady;
    private bool projectionPulseActive;
    private bool projectionPulseAnimationCompleted;
    private bool projectionPulseMinimumVisibleReached;
    private bool projectionPulsePending;
    private bool projectionPulseVisibleFrameCommitted;
    private bool projectionCompositionObserved;
    private bool projectionVisualGateArmed;
    private bool projectionDormantRetryScheduled;
    private bool projectionRetryScheduled;
    private bool projectionValueTransitionActive;
    private bool projectionValueTransitionPending;
    private bool revealVisualStateEntered;
    private bool routePlayed;
    private bool bottomPhaseTransitionActive;
    private int displayedProjectionResolvedCount;
    private int lastProjectionResolvedCount = -1;
    private int lastPresentedResolvedCount;
    private int latestPendingResolvedCount;
    private int pendingProjectionResolvedCount;
    private ProjectionRoute? lastProjectionRoute;
    private readonly Queue<StartupSequencePhase> pendingBottomPhases = new();
    private long displayedProjectionPollingVersion = -1;
    private long latestVersion = -1;
    private long pendingProjectionPollingVersion = -1;
    private long preparedIndexVersion = -1;
    private MotionLevel preparedMotionLevel = MotionLevel.Full;
    private long projectionPulseGeneration;
    private long projectionRequestGeneration;
    private long projectionVisualGateGeneration;
    private EventHandler? projectionLayoutUpdatedHandler;
    private EventHandler? projectionRenderingHandler;
    private MotionLevel projectionPulseMotionLevel = MotionLevel.Off;
    private int projectionPostStartRenderCount;
    private TimeSpan? projectionCompositionRenderingTime;
    private TimeSpan? projectionLastRenderingTime;
    private TimeSpan? projectionPulseFirstRenderingTime;
    private TimeSpan? projectionPulseSecondRenderingTime;
    private DateTimeOffset projectionRequestTimestamp;
    private bool pendingProjectionHasPostDataLayout;
    private long projectionRetryPollingVersion = -1;
    private int projectionRetryResolvedCount = -1;
    private long projectionValueGeneration;
    private long commitPresentationGeneration;
    private long commitEvaluationGeneration;
    private long indexRevealGeneration;
    private long revealHoldGeneration;
    private long revealSnapshotVersion = -1;
    private StartupSequencePhase? lastAnimatedBottomPhase;
    private StartupSequenceSnapshot? previousSnapshot;
    private StartupSequenceSnapshot? pendingIndexSnapshot;
    private string currentProjectionText = string.Empty;
    private string previousProjectionText = string.Empty;
    private DateTimeOffset? commitVisualStartedAt;
    private DateTimeOffset? projectionPulseAnimationCompletedAt;
    private DateTimeOffset? projectionPulseFirstRenderAt;
    private DateTimeOffset? projectionPulseMinimumVisibleReachedAt;
    private DateTimeOffset? projectionPulseStartedAt;
    private StartupMilestoneRow[] milestoneRows = [];
    private int configuredMilestoneBreakpoint = -1;
    private int configuredProjectionSourceIndex = -1;
    private bool milestoneInitialLayoutCommitted;
    private readonly MilestoneRowPresentation[] milestonePresentations;
    private readonly System.Diagnostics.Stopwatch runtimeDiagnosticClock =
        System.Diagnostics.Stopwatch.StartNew();
    private ProjectionRequestState projectionRequestState;

    internal bool IsProjectionLedgerReady => projectionLedgerReady;
    internal bool IsProjectionPulseActive => projectionPulseActive;
    internal bool IsProjectionPulsePending => projectionPulsePending;
    internal bool IsProjectionPulseVisibleFrameCommitted =>
        projectionPulseVisibleFrameCommitted;
    internal bool IsBottomRailReady => bottomRailReady;
    internal bool IsProjectionValueTransitionActive => projectionValueTransitionActive;
    internal bool IsProjectionValueTransitionPending => projectionValueTransitionPending;
    internal bool IsRevealVisualStateEntered => revealVisualStateEntered;
    internal bool IsCommitPendingForProjection => commitPendingForProjection;
    internal bool IsBottomPhaseTransitionActive => bottomPhaseTransitionActive;
    internal bool IsCommitMinimumPresentationReached => commitMinimumPresentationReached;
    internal bool IsCommitRevealCompensationPending => commitRevealCompensationPending;
    internal bool IsIndexRevealRetryScheduled => indexRevealRetryScheduled;
    internal bool IsIndexPendingForFirstFrameGate => pendingIndexSnapshot is not null;
    internal bool IsIndexPlayed => indexPlayed;
    internal DateTimeOffset? CommitVisualStartedAt => commitVisualStartedAt;
    internal DateTimeOffset? ProjectionPulseCompletedAt { get; private set; }
    internal DateTimeOffset? ProjectionPulseVisibleFrameCommittedAt { get; private set; }
    internal DateTimeOffset? ProjectionPulseAnimationCompletedAt =>
        projectionPulseAnimationCompletedAt;
    internal DateTimeOffset? ProjectionPulseFirstRenderAt =>
        projectionPulseFirstRenderAt;
    internal DateTimeOffset? ProjectionPulseMinimumVisibleReachedAt =>
        projectionPulseMinimumVisibleReachedAt;
    internal DateTimeOffset? ProjectionPulseStartedAt =>
        projectionPulseStartedAt;
    internal bool IsProjectionCompositionObserved =>
        projectionCompositionObserved;
    internal bool IsProjectionPulseAnimationCompleted =>
        projectionPulseAnimationCompleted;
    internal bool IsProjectionPulseMinimumVisibleReached =>
        projectionPulseMinimumVisibleReached;
    internal bool IsProjectionRenderingHandlerAttached =>
        projectionRenderingHandler is not null;
    internal int ProjectionPostStartRenderCount =>
        projectionPostStartRenderCount;
    internal TimeSpan? ProjectionCompositionRenderingTime =>
        projectionCompositionRenderingTime;
    internal TimeSpan? ProjectionPulseFirstRenderingTime =>
        projectionPulseFirstRenderingTime;
    internal TimeSpan? ProjectionPulseSecondRenderingTime =>
        projectionPulseSecondRenderingTime;
    internal int DisplayedProjectionResolvedCount => displayedProjectionResolvedCount;
    internal int PendingBottomPhaseCount => pendingBottomPhases.Count;
    internal long ProjectionPulseGeneration => projectionPulseGeneration;
    internal long ProjectionValueGeneration => projectionValueGeneration;
    internal ProjectionRequestState CurrentProjectionRequestState =>
        projectionRequestState;
    internal int LatestPendingResolvedCount => latestPendingResolvedCount;
    internal int LastPresentedResolvedCount => lastPresentedResolvedCount;
    internal ProjectionRoute? LastProjectionRoute => lastProjectionRoute;

    internal event Action<long>? RevealVisualExitCompleted;

    public TraceworkStartupSequenceOverlay()
    {
        InitializeComponent();
        milestonePresentations = Enum.GetValues<StartupMilestoneId>()
            .Select(id => new MilestoneRowPresentation(
                StartupMilestoneSnapshot.Waiting(id)))
            .ToArray();
        RouteMatrixItems.ItemsSource = milestonePresentations;
        Loaded += (_, _) =>
        {
            EnsureInitialMilestoneLayout();
            ConfigureMilestoneRows();
            PrepareRowsIfNeeded();
            SchedulePendingIndexReplay();
            ContinueProjectionAnchorWaitAfterLifecycleEvent();
        };
        RouteMatrixItems.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (RouteMatrixItems.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                milestoneRows = [];
                ConfigureMilestoneRows();
                PrepareRowsIfNeeded();
                ContinueProjectionAnchorWaitAfterLifecycleEvent();
            }
        };
        SizeChanged += (_, _) =>
        {
            ApplyResponsiveMargins(ActualWidth);
            ConfigureMilestoneRows();
            ContinueProjectionAnchorWaitAfterLifecycleEvent();
        };
        Unloaded += (_, _) =>
        {
            milestoneRows = [];
            configuredMilestoneBreakpoint = -1;
            configuredProjectionSourceIndex = -1;
            milestoneInitialLayoutCommitted = false;
            RestoreFinalState();
        };
    }

    public StartupSequenceSnapshot? Snapshot
    {
        get => (StartupSequenceSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public void PrepareFirstFrame(AppTheme theme, MotionLevel motionLevel)
    {
        if (theme != AppTheme.Tracework || motionLevel == MotionLevel.Off)
        {
            RestoreFinalState();
            return;
        }

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;
        StartupBackgroundLayer.Opacity = 1d;
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
        BottomRailContent.Opacity = 0d;
        ApplyResponsiveMargins(ActualWidth);
    }

    internal void PrepareIndexInitialState(MotionLevel level)
    {
        if (indexPrepared && preparedIndexVersion >= 0)
        {
            return;
        }

        ClearChoreographyClocks();
        indexPrepared = true;
        preparedIndexVersion = Snapshot?.Version ?? latestVersion;
        preparedMotionLevel = level;
        if (Snapshot is { } initialSnapshot
            && displayedProjectionPollingVersion < 0)
        {
            displayedProjectionPollingVersion =
                initialSnapshot.InitialProjection.PollingVersion;
            pendingProjectionPollingVersion = displayedProjectionPollingVersion;
            displayedProjectionResolvedCount = 0;
            pendingProjectionResolvedCount = 0;
        }

        StartupContentLayer.Opacity = 1d;
        SystemIndexText.Opacity = 1d;
        TraceworkTitleText.Opacity = 0d;
        StartupSubtitleText.Opacity = 0d;
        SystemRouteLabel.Opacity = 0d;
        LedgerIdentityGroup.Opacity = 0d;
        LedgerEnvironmentGroup.Opacity = 0d;
        LedgerProjectionGroup.Opacity = 0d;
        ProjectionInputPort.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
        BottomRailContent.Opacity = 1d;
        CommitGroup.Opacity = 1d;
        CommitGroup.Visibility = Visibility.Collapsed;
        CommitExitRoot.Opacity = 1d;
        CommitGraphicLayer.Opacity = 0.82d;
        CommitLock.Opacity = 1d;
        CommitText.Opacity = 1d;
        RouteMatrixItems.Opacity = level == MotionLevel.Reduced ? 0d : 1d;

        SetTranslation(TraceworkTitleText, level == MotionLevel.Full ? 4d : 0d, 0d);
        SetTranslation(StartupSubtitleText, level == MotionLevel.Full ? 4d : 0d, 0d);
        SetTranslation(LedgerEnvironmentGroup, level == MotionLevel.Full ? 5d : 0d, 0d);
        SetTranslation(LedgerProjectionGroup, level == MotionLevel.Full ? 5d : 0d, 0d);
        if (Snapshot is { } snapshot)
        {
            currentProjectionText = FormatProjection(
                snapshot.InitialProjection,
                lastPresentedResolvedCount);
            ProjectionCurrentValue.Text = currentProjectionText;
        }
        PrepareRowsIfNeeded();
        ApplyResponsiveMilestoneLayout(ActualWidth);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PrepareRowsIfNeeded));
    }

    public void RestoreFinalState()
    {
        ClearChoreographyClocks();
        foreach (StartupMilestoneRow row in GetMilestoneRows())
        {
            row.ClearTransientState();
        }

        Opacity = 0d;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        StartupBackgroundLayer.Opacity = 0d;
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
        BottomRailContent.Opacity = 0d;
        RouteMatrixItems.Opacity = 1d;
        CommitExitRoot.Opacity = 1d;
        CommitGraphicLayer.Opacity = 0.82d;
        CommitLock.Opacity = 1d;
        CommitText.Opacity = 1d;
        CommitGroup.Opacity = 1d;
        CommitGroup.Visibility = Visibility.Collapsed;
        ResetCommitPresentationState();
        pendingIndexSnapshot = null;
        pendingIndexReplayScheduled = false;
        CleanupBottomRail();
        projectionValueGeneration++;
        projectionValueTransitionActive = false;
        projectionValueTransitionPending = false;
        CleanupProjectionTransition();
        CleanupProjectionPulse();
    }

    internal static Thickness ResolveContentMargin(double width) => width switch
    {
        >= TraceworkResponsiveGrid.StandardBreakpoint => new Thickness(32d, 28d, 32d, 0d),
        >= TraceworkResponsiveGrid.NarrowBreakpoint => new Thickness(24d, 22d, 24d, 0d),
        _ => new Thickness(18d, 18d, 18d, 0d)
    };

    internal static Thickness ResolveBottomRailMargin(double width) => width switch
    {
        >= TraceworkResponsiveGrid.StandardBreakpoint => new Thickness(32d, 0d, 32d, 24d),
        >= TraceworkResponsiveGrid.NarrowBreakpoint => new Thickness(24d, 0d, 24d, 20d),
        _ => new Thickness(18d, 0d, 18d, 16d)
    };

    private static void OnSnapshotChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TraceworkStartupSequenceOverlay overlay)
        {
            overlay.ApplySnapshot(e.NewValue as StartupSequenceSnapshot);
        }
    }

    private void ApplySnapshot(StartupSequenceSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Version < latestVersion)
        {
            return;
        }

        StartupSequenceSnapshot? prior = previousSnapshot;
        previousSnapshot = snapshot;
        latestVersion = snapshot.Version;
        OverlayRoot.DataContext = snapshot;
        UpdateMilestonePresentations(snapshot);
        currentProjectionText = FormatProjection(snapshot.InitialProjection);

        if (snapshot.HasCompleted
            || snapshot.CurrentTheme != AppTheme.Tracework
            || snapshot.MotionLevel == MotionLevel.Off)
        {
            currentProjectionText = FormatProjection(snapshot.InitialProjection);
            RestoreFinalState();
            return;
        }

        if (revealVisualStateEntered)
        {
            FinalizeProjectionValues(snapshot);
            return;
        }

        if (!snapshot.IsActive || snapshot.Phase == StartupSequencePhase.Dormant)
        {
            ResetPlaybackState(snapshot);
            PrepareFirstFrame(snapshot.CurrentTheme, snapshot.MotionLevel);
            return;
        }

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;
        if (PhaseIndex(snapshot.Phase) < PhaseIndex(StartupSequencePhase.Reveal))
        {
            StartupBackgroundLayer.Opacity = 1d;
            StartupContentLayer.Opacity = 1d;
        }

        if (snapshot.Phase == StartupSequencePhase.Reveal)
        {
            RequestRevealVisualState(snapshot);
            return;
        }

        if (snapshot.Phase == StartupSequencePhase.Index && !indexPrepared)
        {
            PrepareIndexInitialState(snapshot.MotionLevel);
        }

        PrepareRowsIfNeeded();
        ApplyProjectionPortState(snapshot.Phase);
        UpdateBottomRail(snapshot);
        ApplyLedgerPhase(snapshot);
        LatchProjectionPulse(prior, snapshot);

        bool enteringRoute = snapshot.Phase == StartupSequencePhase.Route && !routePlayed;
        if (!enteringRoute
            && (routePlayed || PhaseIndex(snapshot.Phase) >= PhaseIndex(StartupSequencePhase.Bind)))
        {
            ApplyMilestoneTransitions(prior, snapshot);
        }

        if (snapshot.Phase == StartupSequencePhase.Lock)
        {
            FinalizeProjectionValues(snapshot);
        }
        else
        {
            ApplyProjectionTransition(snapshot);
        }
        TryStartLatchedProjectionPulse();
        ApplyCommitState(prior, snapshot);

        if (snapshot.Phase == StartupSequencePhase.Index && !indexPlayed)
        {
            RequestIndexReveal(snapshot);
        }
        else if (enteringRoute)
        {
            routePlayed = true;
            PlayRoute(snapshot);
        }

    }

    private void RequestIndexReveal(StartupSequenceSnapshot snapshot)
    {
        if (indexPlayed
            || snapshot.HasCompleted
            || !snapshot.IsActive
            || snapshot.Phase != StartupSequencePhase.Index)
        {
            return;
        }

        bool surfaceMeasured = snapshot.SurfaceMeasured || snapshot.VisualReady;
        bool gateReleased = snapshot.FirstFrameGateReleased
            || snapshot.VisualReady && !snapshot.SurfaceMeasured;
        pendingIndexSnapshot = snapshot;
        if (surfaceMeasured && gateReleased)
        {
            SchedulePendingIndexReplay();
        }
    }

    private void SchedulePendingIndexReplay()
    {
        if (pendingIndexSnapshot is null || pendingIndexReplayScheduled || !IsLoaded)
        {
            return;
        }

        pendingIndexReplayScheduled = true;
        long generation = ++indexRevealGeneration;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                pendingIndexReplayScheduled = false;
                StartupSequenceSnapshot? pending = pendingIndexSnapshot;
                if (generation != indexRevealGeneration
                    || pending is null
                    || indexPlayed
                    || revealVisualStateEntered
                    || Snapshot is not { IsActive: true, HasCompleted: false } current
                    || current.Version < pending.Version
                    || PhaseIndex(current.Phase) < PhaseIndex(StartupSequencePhase.Index)
                    || PhaseIndex(current.Phase) >= PhaseIndex(StartupSequencePhase.Reveal))
                {
                    return;
                }

                bool surfaceMeasured = current.SurfaceMeasured || current.VisualReady;
                bool gateReleased = current.FirstFrameGateReleased
                    || current.VisualReady && !current.SurfaceMeasured;
                Window? hostWindow = Window.GetWindow(this);
                if (!surfaceMeasured
                    || !gateReleased
                    || !IsLoaded
                    || Visibility != Visibility.Visible
                    || Opacity <= 0d
                    || hostWindow is not { IsVisible: true }
                    || hostWindow.Opacity <= 0d)
                {
                    pendingIndexSnapshot = current;
                    return;
                }

                pendingIndexSnapshot = null;
                indexPlayed = true;
                PlayIndexReveal(pending.MotionLevel);
            }));
    }

    private void RequestRevealVisualState(StartupSequenceSnapshot snapshot)
    {
        if (revealVisualStateEntered)
        {
            return;
        }

        EnterRevealVisualState(snapshot);
    }

    private bool TryResolveCommitRevealCompensation(
        StartupSequenceSnapshot snapshot,
        out TimeSpan compensation)
    {
        compensation = TimeSpan.Zero;
        if (!commitVisualStartedAt.HasValue
            || !string.IsNullOrWhiteSpace(snapshot.FailureMessage))
        {
            return false;
        }

        TimeSpan minimum =
            ResolveCommitMinimumPresentationDuration(snapshot.MotionLevel);
        TimeSpan elapsed = DateTimeOffset.UtcNow - commitVisualStartedAt.Value;
        if (elapsed >= minimum)
        {
            commitMinimumPresentationReached = true;
            return false;
        }

        TimeSpan remaining = minimum - elapsed;
        TimeSpan cap = ResolveCommitRevealCompensationCap(snapshot.MotionLevel);
        compensation = remaining <= cap ? remaining : cap;
        return compensation > TimeSpan.Zero;
    }

    internal static TimeSpan ResolveCommitMinimumPresentationDuration(
        MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level switch
            {
                MotionLevel.Full => 660d,
                MotionLevel.Standard => 540d,
                MotionLevel.Reduced => 270d,
                _ => 0d
            });

    internal static TimeSpan ResolveCommitStableHoldDuration(MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level switch
            {
                MotionLevel.Full => 480d,
                MotionLevel.Standard => 360d,
                MotionLevel.Reduced => 180d,
                _ => 0d
            });

    internal static TimeSpan ResolveCommitRevealCompensationCap(MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level switch
            {
                MotionLevel.Full => 660d,
                MotionLevel.Standard => 540d,
                MotionLevel.Reduced => 270d,
                _ => 0d
            });

    internal static TimeSpan ResolveCommitBuildDuration(MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level switch
            {
                MotionLevel.Full or MotionLevel.Standard => 180d,
                MotionLevel.Reduced => 90d,
                _ => 0d
            });

    internal static TimeSpan ResolveCommitExitDuration(MotionLevel level) =>
        level == MotionLevel.Off
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(90d);

    internal static TimeSpan ResolveRevealHoldDuration(MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level switch
            {
                MotionLevel.Full => 120d,
                MotionLevel.Standard => 100d,
                MotionLevel.Reduced => 60d,
                _ => 0d
            });

    internal static TimeSpan ResolveRevealExitDuration(MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level switch
            {
                MotionLevel.Full => 180d,
                MotionLevel.Standard => 150d,
                MotionLevel.Reduced => 100d,
                _ => 0d
            });

    private void EnterRevealVisualState(StartupSequenceSnapshot snapshot)
    {
        if (revealVisualStateEntered)
        {
            return;
        }

        revealVisualStateEntered = true;
        revealSnapshotVersion = snapshot.Version;
        LogStartupDiagnostic("StartupVisibleFrameCommitted", "RevealEntered");
        FinalizeProjectionValues(snapshot);
        StopProjectionPulseForReveal();
        pendingBottomPhases.Clear();
        bottomPhaseTransitionActive = false;
        CommitRevealBottomRail(snapshot);

        TimeSpan holdDuration = string.IsNullOrWhiteSpace(snapshot.FailureMessage)
            ? ResolveRevealHoldDuration(snapshot.MotionLevel)
            : TimeSpan.Zero;
        if (TryResolveCommitRevealCompensation(snapshot, out TimeSpan compensation)
            && compensation > holdDuration)
        {
            holdDuration = compensation;
            commitRevealCompensationPending = true;
        }

        long generation = ++revealHoldGeneration;
        if (holdDuration <= TimeSpan.Zero)
        {
            StartRevealExit(snapshot.MotionLevel, generation);
            return;
        }

        DoubleAnimation hold = new(0d, 0d, holdDuration)
        {
            FillBehavior = FillBehavior.Stop
        };
        hold.Completed += (_, _) =>
        {
            RevealPresentationHold.BeginAnimation(OpacityProperty, null);
            if (generation != revealHoldGeneration)
            {
                return;
            }

            commitRevealCompensationPending = false;
            if (commitVisualStartedAt.HasValue)
            {
                commitMinimumPresentationReached =
                    DateTimeOffset.UtcNow - commitVisualStartedAt.Value
                    >= ResolveCommitMinimumPresentationDuration(snapshot.MotionLevel);
            }
            StartRevealExit(snapshot.MotionLevel, generation);
        };
        RevealPresentationHold.BeginAnimation(
            OpacityProperty,
            hold,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyCommitState(
        StartupSequenceSnapshot? prior,
        StartupSequenceSnapshot snapshot)
    {
        bool canShowCommit = snapshot.Phase == StartupSequencePhase.Lock && snapshot.CanCommit
            && snapshot.MotionLevel != MotionLevel.Off
            && string.IsNullOrWhiteSpace(snapshot.FailureMessage);
        bool isLeavingCommit = prior is { Phase: StartupSequencePhase.Lock, CanCommit: true }
            && snapshot.Phase == StartupSequencePhase.Reveal;
        bool deferForProjection = canShowCommit
            && IsProjectionVisualBlockingCommit;
        commitPendingForProjection = deferForProjection;
        if (commitPlayed)
        {
            commitPendingForProjection = false;
            CommitGroup.Visibility = Visibility.Visible;
            return;
        }

        CommitGroup.Visibility = Visibility.Collapsed;
        if (!canShowCommit || deferForProjection && !isLeavingCommit)
        {
            CommitExitRoot.Opacity = 1d;
            CommitGraphicLayer.Opacity = 0.82d;
            CommitLock.Opacity = 1d;
            CommitText.Opacity = 1d;
        }

        if (canShowCommit && !deferForProjection && !commitPlayed)
        {
            ScheduleCommitEvaluation();
        }
        else if (deferForProjection)
        {
            ArmProjectionVisualGate(snapshot);
            LogProjectionDiagnostic(
                "CommitDeferredForProjection",
                pendingProjectionPollingVersion,
                $"active={projectionPulseActive}; pending={projectionPulsePending}");
        }
    }

    private void ResetPlaybackState(StartupSequenceSnapshot snapshot)
    {
        ClearChoreographyClocks();
        indexPrepared = false;
        preparedIndexVersion = -1;
        indexPlayed = false;
        pendingIndexSnapshot = null;
        pendingIndexReplayScheduled = false;
        routePlayed = false;
        identityLedgerPlayed = false;
        environmentLedgerPlayed = false;
        projectionLedgerPlayed = false;
        commitPlayed = false;
        ResetCommitPresentationState();
        revealVisualStateEntered = false;
        revealHoldGeneration++;
        indexRevealGeneration++;
        indexRevealRetryScheduled = false;
        commitPendingForProjection = false;
        bottomRailReady = false;
        bottomPhaseTransitionActive = false;
        pendingBottomPhases.Clear();
        lastAnimatedBottomPhase = null;
        lastProjectionResolvedCount = snapshot.InitialProjection.ResolvedVisibleSlotCount;
        lastPresentedResolvedCount = 0;
        latestPendingResolvedCount = 0;
        displayedProjectionResolvedCount = 0;
        pendingProjectionResolvedCount = 0;
        displayedProjectionPollingVersion = snapshot.InitialProjection.PollingVersion;
        pendingProjectionPollingVersion = snapshot.InitialProjection.PollingVersion;
        projectionValueTransitionActive = false;
        projectionValueTransitionPending = false;
        projectionValueGeneration++;
        projectionLedgerReady = false;
        projectionPulseActive = false;
        projectionPulseAnimationCompleted = false;
        projectionPulseMinimumVisibleReached = false;
        projectionPulsePending = false;
        projectionPulseVisibleFrameCommitted = false;
        projectionCompositionObserved = false;
        projectionPostStartRenderCount = 0;
        projectionCompositionRenderingTime = null;
        projectionLastRenderingTime = null;
        projectionPulseFirstRenderingTime = null;
        projectionPulseSecondRenderingTime = null;
        projectionPulseStartedAt = null;
        projectionPulseFirstRenderAt = null;
        projectionPulseMinimumVisibleReachedAt = null;
        projectionPulseAnimationCompletedAt = null;
        ProjectionPulseVisibleFrameCommittedAt = null;
        ProjectionPulseCompletedAt = null;
        projectionRequestGeneration = 0;
        projectionRequestTimestamp = default;
        pendingProjectionHasPostDataLayout = false;
        projectionRequestState = ProjectionRequestState.None;
        projectionVisualGateGeneration++;
        projectionDormantRetryScheduled = false;
        currentProjectionText = FormatProjection(
            snapshot.InitialProjection,
            lastPresentedResolvedCount);
        previousProjectionText = string.Empty;
        ProjectionPreviousValue.Text = string.Empty;
        ProjectionCurrentValue.Text = currentProjectionText;
        CleanupBottomRail();
        CleanupProjectionPulse();
        foreach (StartupMilestoneRow row in GetMilestoneRows())
        {
            row.ClearTransientState();
        }
    }

    private void ConfigureMilestoneRows()
    {
        StartupMilestoneRow[] rows = GetMilestoneRows();
        int breakpoint = ActualWidth switch
        {
            >= TraceworkResponsiveGrid.StandardBreakpoint => 2,
            >= TraceworkResponsiveGrid.NarrowBreakpoint => 1,
            _ => 0
        };
        int projectionSourceIndex = Snapshot?.Milestones
            .Select((milestone, index) => (milestone, index))
            .FirstOrDefault(item => item.milestone.Id == StartupMilestoneId.SensorBus)
            .index ?? -1;
        if (rows.Length == 0
            || configuredMilestoneBreakpoint == breakpoint
                && configuredProjectionSourceIndex == projectionSourceIndex)
        {
            return;
        }

        configuredMilestoneBreakpoint = breakpoint;
        configuredProjectionSourceIndex = projectionSourceIndex;
        for (int index = 0; index < rows.Length; index++)
        {
            bool isProjectionSource = index == projectionSourceIndex;
            rows[index].ConfigureSegments(
                index == 0,
                index == rows.Length - 1,
                isProjectionSource);
            rows[index].ApplyResponsiveDetailWidth(ActualWidth);
            if (isProjectionSource && Snapshot is { } snapshot)
            {
                rows[index].SetProjectionPortPhase(
                    snapshot.Phase,
                    snapshot.MotionLevel);
            }
        }
    }

    private void ApplyProjectionPortState(StartupSequencePhase phase)
    {
        ConfigureMilestoneRows();
        if (phase is StartupSequencePhase.Dormant
            or StartupSequencePhase.Index
            or StartupSequencePhase.Route
            || phase == StartupSequencePhase.Bind && !projectionLedgerReady)
        {
            ProjectionInputPort.Opacity = 0d;
            HideProjectionDormantChannel();
        }
        StartupMilestoneRow[] rows = GetMilestoneRows();
        for (int index = 0; index < rows.Length && index < (Snapshot?.Milestones.Count ?? 0); index++)
        {
            if (Snapshot!.Milestones[index].Id == StartupMilestoneId.SensorBus)
            {
                rows[index].SetProjectionPortPhase(phase, Snapshot.MotionLevel);
            }
        }
    }

    private void PrepareRowsIfNeeded()
    {
        if (!indexPrepared || routePlayed)
        {
            return;
        }

        StartupMilestoneRow[] rows = GetMilestoneRows();
        foreach (StartupMilestoneRow row in rows)
        {
            row.PrepareForRoute(preparedMotionLevel);
        }
    }

    private void ApplyMilestoneTransitions(
        StartupSequenceSnapshot? prior,
        StartupSequenceSnapshot snapshot)
    {
        if (prior is null)
        {
            return;
        }

        Dictionary<StartupMilestoneId, StartupMilestoneState> previousStates =
            prior.Milestones.ToDictionary(item => item.Id, item => item.State);
        StartupMilestoneRow[] rows = GetMilestoneRows();
        for (int index = 0; index < rows.Length && index < snapshot.Milestones.Count; index++)
        {
            StartupMilestoneSnapshot current = snapshot.Milestones[index];
            if (previousStates.TryGetValue(current.Id, out StartupMilestoneState previous))
            {
                rows[index].PlayStateTransition(previous, current.State, snapshot.MotionLevel);
            }
        }
    }

    private void ApplyLedgerPhase(StartupSequenceSnapshot snapshot)
    {
        if (snapshot.Phase == StartupSequencePhase.Index && !identityLedgerPlayed)
        {
            identityLedgerPlayed = true;
        }

        if (snapshot.Phase == StartupSequencePhase.Route && !environmentLedgerPlayed)
        {
            environmentLedgerPlayed = true;
            PlayLedgerGroup(
                LedgerEnvironmentGroup,
                snapshot.MotionLevel,
                snapshot.MotionLevel == MotionLevel.Full ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero);
        }

        if (snapshot.Phase == StartupSequencePhase.Bind && !projectionLedgerPlayed)
        {
            projectionLedgerPlayed = true;
            PlayProjectionLedgerGroup(snapshot.MotionLevel);
        }
    }

    private void PlayLedgerGroup(
        FrameworkElement group,
        MotionLevel level,
        TimeSpan delay)
    {
        if (level == MotionLevel.Off)
        {
            group.Opacity = 1d;
            SetTranslation(group, 0d, 0d);
            return;
        }

        TimeSpan duration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromMilliseconds(80);
        AnimateOpacity(group, delay, duration);
        if (level == MotionLevel.Full)
        {
            AnimateTranslationX(group, 5d, 0d, delay, duration);
        }
        else
        {
            SetTranslation(group, 0d, 0d);
        }
    }

    private void PlayProjectionLedgerGroup(MotionLevel level)
    {
        projectionLedgerReady = false;
        if (level is MotionLevel.Off or MotionLevel.Reduced)
        {
            LedgerProjectionGroup.Opacity = 1d;
            SetTranslation(LedgerProjectionGroup, 0d, 0d);
            SetProjectionLedgerReady();
            return;
        }

        TimeSpan delay = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(40)
            : TimeSpan.Zero;
        TimeSpan duration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromMilliseconds(80);
        long generation = projectionPulseGeneration;
        DoubleAnimationUsingKeyFrames opacity =
            BuildDoubleAnimation(0d, 1d, delay, duration);
        LedgerProjectionGroup.Opacity = 1d;
        LedgerProjectionGroup.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);
        if (level == MotionLevel.Full)
        {
            TranslateTransform transform =
                EnsureTranslateTransform(LedgerProjectionGroup);
            transform.X = 0d;
            DoubleAnimationUsingKeyFrames translation =
                BuildDoubleAnimation(5d, 0d, delay, duration);
            translation.Completed += (_, _) =>
                QueueProjectionLedgerReady(generation);
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                translation,
                HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            SetTranslation(LedgerProjectionGroup, 0d, 0d);
            opacity.Completed += (_, _) =>
                QueueProjectionLedgerReady(generation);
        }
    }

    private void QueueProjectionLedgerReady(long generation)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (generation == projectionPulseGeneration
                    && IsLoaded
                    && Snapshot is
                    {
                        IsActive: true,
                        HasCompleted: false,
                        Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
                    })
                {
                    SetProjectionLedgerReady();
                }
            }));
    }

    private void SetProjectionLedgerReady()
    {
        projectionLedgerReady = true;
        ProjectionInputPort.Opacity = 1d;
        if (Snapshot is { MotionLevel: not MotionLevel.Off } readySnapshot)
        {
            ProjectionInputPort.BeginAnimation(
                OpacityProperty,
                BuildDoubleAnimation(
                    0d,
                    1d,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(
                        readySnapshot.MotionLevel == MotionLevel.Reduced ? 80d : 80d)),
                HandoffBehavior.SnapshotAndReplace);
        }
        if (Snapshot is { } dormantSnapshot)
        {
            ShowProjectionDormantChannel(
                dormantSnapshot.MotionLevel,
                allowLayoutRetry: true);
        }
        TryStartLatchedProjectionPulse();
    }

    private void ShowProjectionDormantChannel(
        MotionLevel level,
        bool allowLayoutRetry)
    {
        double opacity = ResolveProjectionDormantOpacity(level);
        if (opacity <= 0d
            || !projectionLedgerReady
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
            })
        {
            HideProjectionDormantChannel();
            return;
        }

        ProjectionRouteResolution resolution =
            TryResolveProjectionRoute(out ProjectionRoute route);
        if (resolution == ProjectionRouteResolution.LayoutPending
            && allowLayoutRetry
            && !projectionDormantRetryScheduled)
        {
            projectionDormantRetryScheduled = true;
            long generation = projectionPulseGeneration;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() =>
                {
                    projectionDormantRetryScheduled = false;
                    if (generation == projectionPulseGeneration
                        && !revealVisualStateEntered)
                    {
                        ShowProjectionDormantChannel(level, allowLayoutRetry: false);
                    }
                }));
            return;
        }

        if (resolution != ProjectionRouteResolution.Success)
        {
            HideProjectionDormantChannel();
            return;
        }

        projectionDormantRetryScheduled = false;
        ProjectionPulseCanvas.Width = Math.Max(1d, OverlayRoot.ActualWidth);
        ProjectionPulseCanvas.Height = Math.Max(1d, OverlayRoot.ActualHeight);
        ProjectionPulseCanvas.Opacity = 1d;
        ConfigureProjectionGeometry(
            ProjectionDormantSourceSegment,
            ProjectionDormantVerticalSegment,
            ProjectionDormantTargetSegment,
            route,
            opacity);
        foreach (FrameworkElement segment in ProjectionDormantSegments())
        {
            if (segment.Visibility == Visibility.Visible)
            {
                AnimateOpacity(
                    segment,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(100),
                    0d,
                    opacity);
            }
        }
    }

    internal static double ResolveProjectionDormantOpacity(MotionLevel level) =>
        level switch
        {
            MotionLevel.Full or MotionLevel.Standard => 0.12d,
            MotionLevel.Reduced => 0.08d,
            _ => 0d
        };

    private FrameworkElement[] ProjectionDormantSegments() =>
    [
        ProjectionDormantSourceSegment,
        ProjectionDormantVerticalSegment,
        ProjectionDormantTargetSegment
    ];

    private void HideProjectionDormantChannel()
    {
        projectionDormantRetryScheduled = false;
        foreach (FrameworkElement segment in ProjectionDormantSegments())
        {
            segment.BeginAnimation(OpacityProperty, null);
            HideProjectionSegment(segment);
        }

        if (!projectionPulseActive)
        {
            ProjectionPulseCanvas.Opacity = 0d;
        }
    }

    private void ApplyProjectionTransition(StartupSequenceSnapshot snapshot)
    {
        int current = snapshot.InitialProjection.ResolvedVisibleSlotCount;
        long pollingVersion = snapshot.InitialProjection.PollingVersion;
        if (lastProjectionResolvedCount < 0)
        {
            lastProjectionResolvedCount = current;
        }
        lastProjectionResolvedCount = current;

        if (snapshot.InitialProjection.TotalVisibleSlotCount == 0
            || PhaseIndex(snapshot.Phase) < PhaseIndex(StartupSequencePhase.Bind))
        {
            return;
        }

        if (pollingVersion < displayedProjectionPollingVersion
            || pollingVersion < pendingProjectionPollingVersion)
        {
            return;
        }

        if (pollingVersion > displayedProjectionPollingVersion)
        {
            ResetProjectionValueBaseline(snapshot);
        }

        lastPresentedResolvedCount = Math.Max(lastPresentedResolvedCount, current);
        if (projectionValueTransitionActive)
        {
            if (pollingVersion > pendingProjectionPollingVersion
                || pollingVersion == pendingProjectionPollingVersion
                    && current > pendingProjectionResolvedCount)
            {
                pendingProjectionPollingVersion = pollingVersion;
                pendingProjectionResolvedCount = current;
                projectionValueTransitionPending =
                    current != displayedProjectionResolvedCount;
            }
            return;
        }

        if (current == displayedProjectionResolvedCount)
        {
            return;
        }

        StartProjectionValueTransition(snapshot, current, pollingVersion);
    }

    private void ResetProjectionValueBaseline(StartupSequenceSnapshot snapshot)
    {
        projectionValueGeneration++;
        projectionValueTransitionActive = false;
        projectionValueTransitionPending = false;
        displayedProjectionPollingVersion = snapshot.InitialProjection.PollingVersion;
        pendingProjectionPollingVersion = displayedProjectionPollingVersion;
        displayedProjectionResolvedCount = 0;
        pendingProjectionResolvedCount = 0;
        lastPresentedResolvedCount = snapshot.InitialProjection.ResolvedVisibleSlotCount;
        ClearProjectionValueClocks();
        previousProjectionText = string.Empty;
        currentProjectionText = FormatProjection(snapshot.InitialProjection, 0);
        ProjectionPreviousValue.Text = string.Empty;
        ProjectionPreviousValue.Opacity = 0d;
        ProjectionCurrentValue.Text = currentProjectionText;
        ProjectionCurrentValue.Opacity = 1d;
    }

    private void StartProjectionValueTransition(
        StartupSequenceSnapshot snapshot,
        int targetCount,
        long pollingVersion)
    {
        projectionValueTransitionActive = true;
        projectionValueTransitionPending = false;
        pendingProjectionPollingVersion = pollingVersion;
        pendingProjectionResolvedCount = targetCount;
        previousProjectionText = FormatProjection(
            snapshot.InitialProjection,
            displayedProjectionResolvedCount);
        currentProjectionText = FormatProjection(snapshot.InitialProjection, targetCount);
        ProjectionPreviousValue.Text = previousProjectionText;
        ProjectionCurrentValue.Text = currentProjectionText;
        long generation = ++projectionValueGeneration;
        PlayProjectionValueTransition(
            snapshot.MotionLevel,
            generation,
            pollingVersion,
            targetCount);
    }

    private void PlayProjectionValueTransition(
        MotionLevel level,
        long generation,
        long pollingVersion,
        int targetCount)
    {
        ClearProjectionValueClocks();
        if (level == MotionLevel.Off)
        {
            CompleteProjectionValueTransition(generation, pollingVersion, targetCount);
            return;
        }

        ProjectionPreviousValue.Opacity = 0d;
        ProjectionCurrentValue.Opacity = 1d;
        TimeSpan duration = level switch
        {
            MotionLevel.Full => TimeSpan.FromMilliseconds(160),
            MotionLevel.Standard => TimeSpan.FromMilliseconds(130),
            _ => TimeSpan.FromMilliseconds(100)
        };
        AnimateOpacity(ProjectionPreviousValue, TimeSpan.Zero, duration, 1d, 0d);
        DoubleAnimationUsingKeyFrames currentOpacity = BuildDoubleAnimation(0d, 1d, TimeSpan.Zero, duration);
        currentOpacity.Completed += (_, _) =>
        {
            CompleteProjectionValueTransition(generation, pollingVersion, targetCount);
        };
        ProjectionCurrentValue.BeginAnimation(
            OpacityProperty,
            currentOpacity,
            HandoffBehavior.SnapshotAndReplace);

        if (level == MotionLevel.Full)
        {
            AnimateTranslationY(ProjectionPreviousValue, 0d, -8d, TimeSpan.Zero, duration);
            AnimateTranslationY(ProjectionCurrentValue, 8d, 0d, TimeSpan.Zero, duration);
        }
        else if (level == MotionLevel.Standard)
        {
            PlayVerticalClip(ProjectionValueClipHost, ProjectionCurrentValue, duration);
        }
    }

    private void CompleteProjectionValueTransition(
        long generation,
        long pollingVersion,
        int targetCount)
    {
        if (generation != projectionValueGeneration
            || pollingVersion != displayedProjectionPollingVersion)
        {
            return;
        }

        displayedProjectionResolvedCount = targetCount;
        projectionValueTransitionActive = false;
        CleanupProjectionTransition();
        if (!projectionValueTransitionPending
            || pendingProjectionPollingVersion != displayedProjectionPollingVersion
            || pendingProjectionResolvedCount == displayedProjectionResolvedCount
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind
            } snapshot)
        {
            projectionValueTransitionPending = false;
            return;
        }

        int pending = pendingProjectionResolvedCount;
        projectionValueTransitionPending = false;
        StartProjectionValueTransition(snapshot, pending, pendingProjectionPollingVersion);
    }

    private void FinalizeProjectionValues(StartupSequenceSnapshot snapshot)
    {
        if (snapshot.InitialProjection.PollingVersion < displayedProjectionPollingVersion)
        {
            return;
        }

        projectionValueGeneration++;
        projectionValueTransitionActive = false;
        projectionValueTransitionPending = false;
        displayedProjectionPollingVersion = snapshot.InitialProjection.PollingVersion;
        pendingProjectionPollingVersion = displayedProjectionPollingVersion;
        displayedProjectionResolvedCount =
            snapshot.InitialProjection.ResolvedVisibleSlotCount;
        pendingProjectionResolvedCount = displayedProjectionResolvedCount;
        lastPresentedResolvedCount = displayedProjectionResolvedCount;
        currentProjectionText = FormatProjection(snapshot.InitialProjection);
        CleanupProjectionTransition();
    }

    private void PlayRoute(StartupSequenceSnapshot snapshot)
    {
        MotionLevel level = snapshot.MotionLevel;
        if (level == MotionLevel.Reduced)
        {
            RouteMatrixItems.Opacity = 1d;
            AnimateOpacity(RouteMatrixItems, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));
            StartupMilestoneRow[] reducedRows = GetMilestoneRows();
            for (int index = 0; index < reducedRows.Length && index < snapshot.Milestones.Count; index++)
            {
                reducedRows[index].PlayRouteArrivalState(
                    snapshot.Milestones[index].State,
                    level,
                    TimeSpan.Zero);
            }
            return;
        }

        StartupMilestoneRow[] rows = GetMilestoneRows();
        if (rows.Length == 0)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    ConfigureMilestoneRows();
                    PrepareRowsIfNeeded();
                    PlayRoute(snapshot);
                }));
            return;
        }

        int interval = level == MotionLevel.Full
            ? StartupMilestoneRow.FullRouteRowIntervalMilliseconds
            : StartupMilestoneRow.StandardRouteRowIntervalMilliseconds;
        for (int index = 0; index < rows.Length && index < snapshot.Milestones.Count; index++)
        {
            TimeSpan routeDelay = TimeSpan.FromMilliseconds(index * interval);
            rows[index].PlayRouteReveal(level, routeDelay);
            rows[index].PlayRouteArrivalState(snapshot.Milestones[index].State, level, routeDelay);
        }
    }

    private void PlayIndexReveal(MotionLevel level) =>
        PlayIndexReveal(level, allowLayoutRetry: true);

    private void PlayIndexReveal(MotionLevel level, bool allowLayoutRetry)
    {
        if (level == MotionLevel.Off)
        {
            SetIndexFinalState();
            return;
        }

        if (level == MotionLevel.Reduced)
        {
            SystemIndexText.Opacity = 1d;
            TraceworkTitleText.Opacity = 1d;
            StartupSubtitleText.Opacity = 1d;
            StartupTitleGroup.Opacity = 0d;
            AnimateOpacity(StartupTitleGroup, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));
            AnimateOpacity(LedgerIdentityGroup, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            PlayBottomRailEntry(level, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            AnimateOpacity(SystemRouteLabel, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            return;
        }

        if (!TryGetStableIndexLayout(out double width, out double height))
        {
            if (allowLayoutRetry && !indexRevealRetryScheduled)
            {
                indexRevealRetryScheduled = true;
                long generation = ++indexRevealGeneration;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() =>
                    {
                        indexRevealRetryScheduled = false;
                        if (generation != indexRevealGeneration
                            || revealVisualStateEntered
                            || Snapshot is not { IsActive: true, HasCompleted: false } activeSnapshot)
                        {
                            return;
                        }

                        if (PhaseIndex(activeSnapshot.Phase)
                                >= PhaseIndex(StartupSequencePhase.Reveal))
                        {
                            SetIndexFinalState();
                            return;
                        }

                        PlayIndexReveal(level, allowLayoutRetry: false);
                    }));
                return;
            }

            SetIndexFinalState();
            return;
        }

        indexRevealRetryScheduled = false;
        RectangleGeometry clip = new();
        SystemIndexClipHost.Clip = clip;
        TimeSpan indexDuration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(180)
            : TimeSpan.FromMilliseconds(120);
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, 0d, height),
            new Rect(0d, 0d, width, height),
            TimeSpan.Zero,
            indexDuration);

        if (level == MotionLevel.Full)
        {
            AnimateOpacity(TraceworkTitleText, TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(100));
            AnimateTranslationX(TraceworkTitleText, 4d, 0d, TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(100));
            AnimateOpacity(StartupSubtitleText, TimeSpan.FromMilliseconds(75), TimeSpan.FromMilliseconds(90));
            AnimateTranslationX(StartupSubtitleText, 4d, 0d, TimeSpan.FromMilliseconds(75), TimeSpan.FromMilliseconds(90));
            AnimateOpacity(LedgerIdentityGroup, TimeSpan.FromMilliseconds(110), TimeSpan.FromMilliseconds(90));
            PlayBottomRailEntry(level, TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(100));
            AnimateOpacity(SystemRouteLabel, TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(80));
            return;
        }

        AnimateOpacity(TraceworkTitleText, TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(90));
        AnimateOpacity(StartupSubtitleText, TimeSpan.FromMilliseconds(55), TimeSpan.FromMilliseconds(80));
        AnimateOpacity(LedgerIdentityGroup, TimeSpan.FromMilliseconds(85), TimeSpan.FromMilliseconds(80));
        PlayBottomRailEntry(level, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(80));
        AnimateOpacity(SystemRouteLabel, TimeSpan.FromMilliseconds(110), TimeSpan.FromMilliseconds(70));
    }

    private bool TryGetStableIndexLayout(out double width, out double height)
    {
        width = SystemIndexText.ActualWidth;
        height = SystemIndexText.ActualHeight;
        double desiredWidth = SystemIndexText.DesiredSize.Width;
        return SystemIndexText.IsMeasureValid
            && SystemIndexText.IsArrangeValid
            && double.IsFinite(width)
            && double.IsFinite(height)
            && double.IsFinite(desiredWidth)
            && width > 1d
            && height > 1d
            && desiredWidth > 1d
            && Math.Abs(width - desiredWidth) <= 1d;
    }

    private void PlayBottomRailEntry(MotionLevel level, TimeSpan delay, TimeSpan duration)
    {
        if (level is MotionLevel.Reduced or MotionLevel.Off)
        {
            StartupBottomRailLayer.Opacity = 1d;
            SetBottomRailReady();
            if (level == MotionLevel.Reduced)
            {
                AnimateOpacity(StartupBottomRailLayer, delay, duration);
            }
            return;
        }

        DoubleAnimationUsingKeyFrames opacity =
            BuildDoubleAnimation(0d, 1d, delay, duration);
        opacity.Completed += (_, _) =>
        {
            if (IsLoaded
                && Snapshot is { IsActive: true, HasCompleted: false })
            {
                SetBottomRailReady();
            }
        };
        StartupBottomRailLayer.Opacity = 1d;
        StartupBottomRailLayer.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);

        double width = Math.Max(
            1d,
            StartupBottomRailLayer.ActualWidth > 0d
                ? StartupBottomRailLayer.ActualWidth
                : OverlayRoot.ActualWidth
                    - StartupBottomRailLayer.Margin.Left
                    - StartupBottomRailLayer.Margin.Right);
        double height = Math.Max(1d, StartupBottomRailLayer.ActualHeight);
        RectangleGeometry clip = new();
        StartupBottomRailLayer.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, 0d, height),
            new Rect(0d, 0d, width, height),
            delay,
            duration);
    }

    private void SetIndexFinalState()
    {
        indexRevealGeneration++;
        indexRevealRetryScheduled = false;
        ClearGeometry(SystemIndexClipHost);
        SystemIndexText.Opacity = 1d;
        StartupTitleGroup.Opacity = 1d;
        TraceworkTitleText.Opacity = 1d;
        StartupSubtitleText.Opacity = 1d;
        SystemRouteLabel.Opacity = 1d;
        LedgerIdentityGroup.Opacity = 1d;
        StartupBottomRailLayer.Opacity = 1d;
        SetBottomRailReady();
        SetTranslation(TraceworkTitleText, 0d, 0d);
        SetTranslation(StartupSubtitleText, 0d, 0d);
    }

    private void UpdateBottomRail(StartupSequenceSnapshot snapshot)
    {
        EnqueueBottomPhase(snapshot.Phase);
        if (!bottomRailReady)
        {
            return;
        }

        PlayNextBottomPhase();
    }

    private void SetBottomRailReady()
    {
        if (bottomRailReady)
        {
            return;
        }

        bottomRailReady = true;
        PlayNextBottomPhase();
    }

    private void EnqueueBottomPhase(StartupSequencePhase phase)
    {
        int index = PhaseIndex(phase);
        if (index < 0
            || lastAnimatedBottomPhase.HasValue
                && index <= PhaseIndex(lastAnimatedBottomPhase.Value)
            || pendingBottomPhases.Contains(phase))
        {
            return;
        }

        StartupSequencePhase? lastQueued =
            pendingBottomPhases.Count == 0 ? null : pendingBottomPhases.Last();
        if (lastQueued.HasValue && index <= PhaseIndex(lastQueued.Value))
        {
            return;
        }

        pendingBottomPhases.Enqueue(phase);
    }

    private void PlayNextBottomPhase()
    {
        if (!bottomRailReady
            || bottomPhaseTransitionActive
            || pendingBottomPhases.Count == 0
            || Snapshot is null
            || revealVisualStateEntered)
        {
            return;
        }

        StartupSequencePhase phase = pendingBottomPhases.Dequeue();
        StartupPhasePresentation? presentation = StartupPhasePresentation.Create(
            phase,
            Snapshot.FailureMessage);
        if (presentation is null)
        {
            BottomRailContent.Opacity = 0d;
            return;
        }

        UpdateReadyBottomRail(Snapshot.MotionLevel, phase, presentation);
        bottomPhaseTransitionActive = true;
        TimeSpan minimumVisible = ResolveBottomPhaseMinimumVisibleDuration(
            Snapshot.MotionLevel,
            phase);
        if (minimumVisible == TimeSpan.Zero)
        {
            bottomPhaseTransitionActive = false;
            PlayNextBottomPhase();
            return;
        }

        DoubleAnimation hold = new(1d, 1d, minimumVisible)
        {
            FillBehavior = FillBehavior.Stop
        };
        hold.Completed += (_, _) =>
        {
            BottomPhaseAnimationHold.BeginAnimation(OpacityProperty, null);
            bottomPhaseTransitionActive = false;
            PlayNextBottomPhase();
        };
        BottomPhaseAnimationHold.BeginAnimation(
            OpacityProperty,
            hold,
            HandoffBehavior.SnapshotAndReplace);
    }

    internal static TimeSpan ResolveBottomPhaseMinimumVisibleDuration(
        MotionLevel level,
        StartupSequencePhase phase) =>
        phase == StartupSequencePhase.Index
            ? level switch
            {
                MotionLevel.Full => TimeSpan.FromMilliseconds(120),
                MotionLevel.Standard => TimeSpan.FromMilliseconds(160),
                _ => TimeSpan.Zero
            }
            : level switch
            {
                MotionLevel.Full => TimeSpan.FromMilliseconds(120),
                MotionLevel.Standard => TimeSpan.FromMilliseconds(100),
                _ => TimeSpan.Zero
            };

    private void UpdateReadyBottomRail(
        MotionLevel level,
        StartupSequencePhase phase,
        StartupPhasePresentation presentation)
    {
        BottomRailContent.Opacity = 1d;
        BottomPreviousPhaseText.Text = BottomCurrentPhaseText.Text;
        BottomPreviousPhaseText.Foreground = BottomCurrentPhaseText.Foreground;
        BottomPreviousPhaseCode.Text = BottomCurrentPhaseCode.Text;
        BottomPreviousPhaseCode.Foreground = BottomCurrentPhaseCode.Foreground;
        BottomCurrentPhaseText.Text = presentation.DisplayText;
        BottomCurrentPhaseCode.Text = presentation.PhaseCode;
        ApplyPhasePresentationBrushes(presentation);

        bool movesForward = lastAnimatedBottomPhase is null
            || PhaseIndex(phase) > PhaseIndex(lastAnimatedBottomPhase.Value);
        StartupSequencePhase? previousPhase = lastAnimatedBottomPhase;
        Color? previousSegmentColor = previousPhase.HasValue
            && PhaseIndex(previousPhase.Value) >= 0
            && PhaseSegments()[PhaseIndex(previousPhase.Value)].Background is SolidColorBrush previousBrush
                ? previousBrush.Color
                : null;
        if (!movesForward)
        {
            return;
        }

        lastAnimatedBottomPhase = phase;
        PlayBottomPhaseTransition(
            level,
            presentation,
            previousPhase,
            previousSegmentColor,
            phase);
    }

    private void CommitRevealBottomRail(StartupSequenceSnapshot snapshot)
    {
        StartupPhasePresentation? presentation = StartupPhasePresentation.Create(
            StartupSequencePhase.Reveal,
            snapshot.FailureMessage);
        if (presentation is null)
        {
            return;
        }

        ClearBottomTextClocks();
        pendingBottomPhases.Clear();
        bottomPhaseTransitionActive = false;
        bottomRailReady = true;
        lastAnimatedBottomPhase = StartupSequencePhase.Reveal;
        BottomRailContent.Opacity = 1d;
        StartupBottomRailLayer.BeginAnimation(OpacityProperty, null);
        ClearGeometry(StartupBottomRailLayer);
        StartupBottomRailLayer.Opacity = 1d;
        BottomPreviousPhaseText.Text = string.Empty;
        BottomPreviousPhaseCode.Text = string.Empty;
        BottomPreviousPhaseText.Opacity = 0d;
        BottomPreviousPhaseCode.Opacity = 0d;
        BottomCurrentPhaseText.Text = presentation.DisplayText;
        BottomCurrentPhaseCode.Text = presentation.PhaseCode;
        ApplyPhasePresentationBrushes(presentation);
        BottomCurrentPhaseText.Opacity = 1d;
        BottomCurrentPhaseCode.Opacity = 1d;
        SetTranslation(BottomPreviousPhaseText, 0d, 0d);
        SetTranslation(BottomPreviousPhaseCode, 0d, 0d);
        SetTranslation(BottomCurrentPhaseText, 0d, 0d);
        SetTranslation(BottomCurrentPhaseCode, 0d, 0d);
        ApplyPhaseSegmentStates(presentation);
        foreach (Border segment in PhaseSegments())
        {
            ClearGeometry(segment);
            segment.BeginAnimation(OpacityProperty, null);
            segment.Opacity = 1d;
        }
    }

    private void PlayBottomPhaseTransition(
        MotionLevel level,
        StartupPhasePresentation presentation,
        StartupSequencePhase? previousPhase,
        Color? previousSegmentColor,
        StartupSequencePhase currentPhase)
    {
        ClearBottomTextClocks();
        bool hasPreviousText = !string.IsNullOrEmpty(BottomPreviousPhaseText.Text);
        bool hasPreviousCode = !string.IsNullOrEmpty(BottomPreviousPhaseCode.Text);
        BottomPreviousPhaseText.Opacity = hasPreviousText ? 1d : 0d;
        BottomPreviousPhaseCode.Opacity = hasPreviousCode ? 1d : 0d;
        BottomCurrentPhaseText.Opacity = level == MotionLevel.Off ? 1d : 0d;
        BottomCurrentPhaseCode.Opacity = level == MotionLevel.Off ? 1d : 0d;
        SetTranslation(BottomPreviousPhaseText, 0d, 0d);
        SetTranslation(BottomPreviousPhaseCode, 0d, 0d);
        SetTranslation(BottomCurrentPhaseText, 0d, 0d);
        SetTranslation(BottomCurrentPhaseCode, 0d, 0d);
        ApplyPhaseSegmentStates(presentation);
        if (level == MotionLevel.Off)
        {
            BottomPreviousPhaseText.Opacity = 0d;
            BottomPreviousPhaseCode.Opacity = 0d;
            return;
        }

        if (level == MotionLevel.Reduced)
        {
            AnimateOpacity(BottomPreviousPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 1d, 0d);
            AnimateOpacity(BottomPreviousPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 1d, 0d);
            AnimateOpacity(BottomCurrentPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateOpacity(BottomCurrentPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            return;
        }

        if (level == MotionLevel.Full)
        {
            AnimateOpacity(BottomPreviousPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(60), 1d, 0d);
            AnimateTranslationY(BottomPreviousPhaseText, 0d, -8d, TimeSpan.Zero, TimeSpan.FromMilliseconds(60));
            AnimateOpacity(BottomPreviousPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(60), 1d, 0d);
            AnimateTranslationY(BottomPreviousPhaseCode, 0d, -8d, TimeSpan.Zero, TimeSpan.FromMilliseconds(60));
            AnimateOpacity(BottomCurrentPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateTranslationY(BottomCurrentPhaseText, 8d, 0d, TimeSpan.Zero, TimeSpan.FromMilliseconds(90));
            AnimateOpacity(BottomCurrentPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateTranslationY(BottomCurrentPhaseCode, 8d, 0d, TimeSpan.Zero, TimeSpan.FromMilliseconds(90));
        }
        else
        {
            PlayVerticalClip(BottomPhaseTextClipHost, BottomCurrentPhaseText, TimeSpan.FromMilliseconds(90));
            PlayVerticalClip(BottomPhaseCodeClipHost, BottomCurrentPhaseCode, TimeSpan.FromMilliseconds(90));
            AnimateOpacity(BottomPreviousPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 1d, 0d);
            AnimateOpacity(BottomPreviousPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 1d, 0d);
            AnimateOpacity(BottomCurrentPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateOpacity(BottomCurrentPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
        }

        Border current = PhaseSegments()[PhaseIndex(currentPhase)];
        AnimateTrackReveal(
            current,
            level == MotionLevel.Full ? TimeSpan.FromMilliseconds(120) : TimeSpan.FromMilliseconds(100));
        if (previousPhase.HasValue && PhaseIndex(previousPhase.Value) >= 0)
        {
            AnimateSegmentCompletion(
                PhaseSegments()[PhaseIndex(previousPhase.Value)],
                previousSegmentColor);
        }
    }

    private void ApplyPhaseSegmentStates(StartupPhasePresentation presentation)
    {
        Border[] segments = PhaseSegments();
        for (int index = 0; index < segments.Length; index++)
        {
            string resourceKey = index < presentation.CompletedStepCount
                ? "SuccessBrush"
                : index == presentation.CurrentStepIndex
                    ? presentation.IsFailure
                        ? "CriticalBrush"
                        : index == 2
                            ? "TraceworkTelemetryBrush"
                            : "TraceworkIdentityBrush"
                    : "TraceworkTraceGreyBrush";
            segments[index].SetResourceReference(Border.BackgroundProperty, resourceKey);
        }
    }

    private void ApplyPhasePresentationBrushes(StartupPhasePresentation presentation)
    {
        BottomCurrentPhaseText.SetResourceReference(
            TextBlock.ForegroundProperty,
            presentation.IsFailure ? "CriticalBrush" : "TraceworkPaperBrush");
        BottomCurrentPhaseCode.SetResourceReference(
            TextBlock.ForegroundProperty,
            presentation.IsFailure
                ? "CriticalBrush"
                : presentation.CurrentStepIndex == 2
                    ? "TraceworkTelemetryBrush"
                    : "TraceworkIdentityBrush");
    }

    private void AnimateTrackReveal(Border segment, TimeSpan duration)
    {
        double width = Math.Max(segment.MinWidth, segment.ActualWidth);
        RectangleGeometry clip = new();
        segment.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, 0d, 1d),
            new Rect(0d, 0d, width, 1d),
            TimeSpan.Zero,
            duration);
    }

    private void AnimateSegmentCompletion(Border segment, Color? fromColor)
    {
        if (!fromColor.HasValue
            || TryFindResource("SuccessBrush") is not SolidColorBrush success)
        {
            return;
        }

        SolidColorBrush animated = new(success.Color);
        segment.Background = animated;
        animated.BeginAnimation(
            SolidColorBrush.ColorProperty,
            new ColorAnimation(fromColor.Value, success.Color, TimeSpan.FromMilliseconds(80))
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void LatchProjectionPulse(
        StartupSequenceSnapshot? prior,
        StartupSequenceSnapshot snapshot)
    {
        StartupInitialProjectionSnapshot projection = snapshot.InitialProjection;
        StartupInitialProjectionSnapshot? previousProjection =
            prior?.InitialProjection;
        if (previousProjection is not null
            && projection.PollingVersion < previousProjection.PollingVersion)
        {
            return;
        }
        bool hasInitialProjectionSignal =
            projection.PollingVersion > 0
            && (projection.DispatcherApplied
                || projection.ResolvedVisibleSlotCount > 0
                || projection.PostDataLayoutObserved);
        if (!hasInitialProjectionSignal)
        {
            return;
        }
        bool pollingVersionAdvanced = previousProjection is null
            ? hasInitialProjectionSignal
            : projection.PollingVersion > previousProjection.PollingVersion;
        bool resolvedCountAdvanced = previousProjection is null
            ? projection.ResolvedVisibleSlotCount > 0
            : projection.ResolvedVisibleSlotCount
                > previousProjection.ResolvedVisibleSlotCount;
        bool postDataLayoutObserved =
            projection.PostDataLayoutObserved
            && previousProjection?.PostDataLayoutObserved != true;
        if (!pollingVersionAdvanced
            && !resolvedCountAdvanced
            && !postDataLayoutObserved)
        {
            return;
        }

        LogProjectionDiagnostic(
            "ProjectionDataReceived",
            projection.PollingVersion,
            $"resolved={projection.ResolvedVisibleSlotCount}; postLayout={projection.PostDataLayoutObserved}");
        if (!snapshot.IsActive
            || snapshot.HasCompleted
            || PhaseIndex(snapshot.Phase) >= PhaseIndex(StartupSequencePhase.Reveal)
            || snapshot.MotionLevel is MotionLevel.Reduced or MotionLevel.Off)
        {
            return;
        }

        if (projectionPulseActive
            && projection.PollingVersion > pendingProjectionPollingVersion
            && projection.ResolvedVisibleSlotCount
                <= latestPendingResolvedCount)
        {
            latestPendingResolvedCount =
                projection.ResolvedVisibleSlotCount;
            pendingProjectionPollingVersion = projection.PollingVersion;
            pendingProjectionHasPostDataLayout =
                projection.PostDataLayoutObserved;
            projectionRequestTimestamp = DateTimeOffset.UtcNow;
            projectionRequestGeneration++;
            projectionPulsePending = true;
            LogProjectionDiagnostic(
                "ProjectionRequestUpdated",
                projection.PollingVersion,
                $"requestGeneration={projectionRequestGeneration}; resolved={latestPendingResolvedCount}; postLayout={pendingProjectionHasPostDataLayout}");
            LogProjectionDiagnostic(
                "ProjectionRequestCoalesced",
                projection.PollingVersion,
                $"activeGeneration={projectionPulseGeneration}; queuedReplay=True; resolved={projection.ResolvedVisibleSlotCount}");
            return;
        }

        if (projectionPulseActive
            && projection.PollingVersion == pendingProjectionPollingVersion
            && projection.ResolvedVisibleSlotCount
                <= latestPendingResolvedCount)
        {
            pendingProjectionPollingVersion = projection.PollingVersion;
            pendingProjectionHasPostDataLayout =
                projection.PostDataLayoutObserved;
            LogProjectionDiagnostic(
                "ProjectionRequestCoalesced",
                projection.PollingVersion,
                $"activeGeneration={projectionPulseGeneration}; resolved={projection.ResolvedVisibleSlotCount}");
            return;
        }

        bool supersedesActivePulse = projectionPulseActive;
        latestPendingResolvedCount = projection.ResolvedVisibleSlotCount;
        pendingProjectionPollingVersion = projection.PollingVersion;
        pendingProjectionHasPostDataLayout = projection.PostDataLayoutObserved;
        projectionRequestTimestamp = DateTimeOffset.UtcNow;
        bool requestUpdated = projectionRequestGeneration > 0;
        projectionRequestGeneration++;
        DetachProjectionLayoutUpdatedHandler();
        if (supersedesActivePulse)
        {
            LogProjectionDiagnostic(
                "ProjectionPulseCancelled",
                projection.PollingVersion,
                $"reason=Superseded; generation={projectionPulseGeneration}");
            projectionPulseGeneration++;
            DetachProjectionRenderingHandler();
            projectionPulseActive = false;
            ClearProjectionPulseVisuals();
        }
        projectionPulsePending = true;
        projectionRequestState = projection.PostDataLayoutObserved
            ? ProjectionRequestState.Latched
            : ProjectionRequestState.WaitingForDataLayout;
        LogProjectionDiagnostic(
            requestUpdated
                ? "ProjectionRequestUpdated"
                : "ProjectionRequestLatched",
            projection.PollingVersion,
            $"requestGeneration={projectionRequestGeneration}; resolved={latestPendingResolvedCount}; postLayout={pendingProjectionHasPostDataLayout}");
        LogProjectionDiagnostic(
            snapshot.Phase == StartupSequencePhase.Lock
                ? "ProjectionRequestReceivedInLock"
                : "ProjectionRequestReceivedInBind",
            projection.PollingVersion,
            $"requestGeneration={projectionRequestGeneration}");
    }

    private void TryStartLatchedProjectionPulse()
    {
        if (!projectionPulsePending
            || projectionPulseActive
            || projectionRetryScheduled
            || !projectionLedgerReady
            || !pendingProjectionHasPostDataLayout
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock,
                MotionLevel: MotionLevel.Full or MotionLevel.Standard
            } snapshot)
        {
            return;
        }

        StartProjectionPulse(
            snapshot.MotionLevel,
            latestPendingResolvedCount,
            pendingProjectionPollingVersion,
            retryCount: 0);
    }

    private void StartProjectionPulse(
        MotionLevel level,
        int resolvedCount,
        long pollingVersion,
        int retryCount)
    {
        if (level is MotionLevel.Reduced or MotionLevel.Off
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
            }
            || !projectionLedgerReady)
        {
            return;
        }

        ProjectionRouteResolution resolution =
            TryResolveProjectionRoute(out ProjectionRoute route);
        if (resolution != ProjectionRouteResolution.Success)
        {
            projectionRequestState = ProjectionRequestState.WaitingForAnchorLayout;
            if (!projectionRetryScheduled)
            {
                LogProjectionDiagnostic(
                    "ProjectionLayoutWaitStarted",
                    pollingVersion,
                    $"resolved={resolvedCount}; reason={resolution}; boundedByRequest=700ms");
            }
            BeginProjectionLayoutWait(
                level,
                resolvedCount,
                pollingVersion,
                resolution.ToString());
            return;
        }

        DetachProjectionLayoutUpdatedHandler();
        projectionRetryScheduled = false;
        projectionPulsePending = false;
        latestPendingResolvedCount = resolvedCount;
        projectionPulseActive = true;
        projectionPulseAnimationCompleted = false;
        projectionPulseMinimumVisibleReached = false;
        projectionPulseVisibleFrameCommitted = false;
        projectionCompositionObserved = false;
        projectionPostStartRenderCount = 0;
        projectionCompositionRenderingTime = null;
        projectionLastRenderingTime = null;
        projectionPulseFirstRenderingTime = null;
        projectionPulseSecondRenderingTime = null;
        projectionPulseStartedAt = null;
        projectionPulseFirstRenderAt = null;
        projectionPulseMinimumVisibleReachedAt = null;
        projectionPulseAnimationCompletedAt = null;
        ProjectionPulseCompletedAt = null;
        ProjectionPulseVisibleFrameCommittedAt = null;
        projectionPulseMotionLevel = level;
        projectionRequestState = ProjectionRequestState.GeometryReady;
        long generation = ++projectionPulseGeneration;
        lastProjectionRoute = route;
        _ = retryCount;
        LogProjectionDiagnostic(
            "ProjectionGeometryReady",
            pollingVersion,
            $"length={route.TotalRouteLength:0.##}; segments={(route.UsesThreeSegments ? 3 : 1)}");
        PrepareProjectionGeometry(route);
        TryAttachProjectionRenderingHandler(
            generation,
            pollingVersion,
            level,
            resolvedCount);
    }

    private void PrepareProjectionGeometry(ProjectionRoute route)
    {
        ConfigureProjectionGeometry(route);
        ProjectionSourceHorizontalSegment.Opacity = 0d;
        ProjectionSourceHorizontalSegment.Clip = new RectangleGeometry(
            new Rect(
                0d,
                0d,
                Math.Min(1d, route.SourceHorizontalLength),
                1d));
        if (route.UsesThreeSegments)
        {
            ProjectionVerticalBridgeSegment.Opacity = 0d;
            ProjectionVerticalBridgeSegment.Clip = new RectangleGeometry(
                new Rect(
                    0d,
                    0d,
                    1d,
                    Math.Min(1d, route.VerticalBridgeLength)));
            ProjectionTargetHorizontalSegment.Opacity = 0d;
            ProjectionTargetHorizontalSegment.Clip = new RectangleGeometry(
                new Rect(
                    0d,
                    0d,
                    Math.Min(1d, route.TargetHorizontalLength),
                    1d));
        }

        ProjectionPulseHead.Opacity = 0d;
    }

    private void TryAttachProjectionRenderingHandler(
        long generation,
        long pollingVersion,
        MotionLevel level,
        int resolvedCount)
    {
        if (generation != projectionPulseGeneration
            || !projectionPulseActive
            || lastProjectionRoute is not { TotalRouteLength: > 24d } route)
        {
            return;
        }

        if (!CanObserveProjectionComposition(route))
        {
            projectionRequestState = ProjectionRequestState.WaitingForComposition;
            BeginProjectionLayoutWait(
                level,
                resolvedCount,
                pollingVersion,
                ResolveProjectionCompositionWaitReason(route));
            return;
        }

        DetachProjectionLayoutUpdatedHandler();
        if (projectionRenderingHandler is not null)
        {
            return;
        }

        EventHandler handler = (_, args) =>
            OnProjectionRendering(
                generation,
                pollingVersion,
                args);
        projectionRenderingHandler = handler;
        StartupRenderSource.Rendering += handler;
        projectionRequestState = ProjectionRequestState.WaitingForComposition;
        LogProjectionDiagnostic(
            "ProjectionCompositionWaitStarted",
            pollingVersion,
            $"generation={generation}; resolved={resolvedCount}");
    }

    private bool CanObserveProjectionComposition(ProjectionRoute route)
    {
        Window? hostWindow = Window.GetWindow(this);
        return route.TotalRouteLength > 24d
            && IsLoaded
            && Visibility == Visibility.Visible
            && Opacity > 0d
            && hostWindow is { IsVisible: true }
            && hostWindow.Opacity > 0d
            && PresentationSource.FromVisual(OverlayRoot) is not null
            && ProjectionPulseCanvas.IsLoaded
            && ProjectionPulseCanvas.Visibility == Visibility.Visible;
    }

    private string ResolveProjectionCompositionWaitReason(ProjectionRoute route)
    {
        Window? hostWindow = Window.GetWindow(this);
        if (route.TotalRouteLength <= 24d)
        {
            return "InvalidRoute";
        }
        if (PresentationSource.FromVisual(OverlayRoot) is null)
        {
            return "NoPresentationSource";
        }
        if (hostWindow is not { IsVisible: true } || hostWindow.Opacity <= 0d)
        {
            return "HostNotVisible";
        }

        return "LayoutPending";
    }

    private void BeginProjectionLayoutWait(
        MotionLevel level,
        int resolvedCount,
        long pollingVersion,
        string reason)
    {
        projectionRetryScheduled = true;
        projectionRetryPollingVersion = pollingVersion;
        projectionRetryResolvedCount = resolvedCount;
        if (projectionLayoutUpdatedHandler is not null)
        {
            return;
        }

        long requestGeneration = projectionRequestGeneration;
        EventHandler handler = (_, _) =>
            ContinueProjectionReadiness(
                requestGeneration,
                level,
                resolvedCount,
                pollingVersion);
        projectionLayoutUpdatedHandler = handler;
        LayoutUpdated += handler;
        LogProjectionDiagnostic(
            "ProjectionPulseLayoutRetry",
            pollingVersion,
            $"reason={reason}; awaiting=LayoutUpdated");
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
                ContinueProjectionReadiness(
                    requestGeneration,
                    level,
                    resolvedCount,
                    pollingVersion)));
    }

    private void ContinueProjectionReadiness(
        long requestGeneration,
        MotionLevel level,
        int resolvedCount,
        long pollingVersion)
    {
        if (requestGeneration != projectionRequestGeneration
            || revealVisualStateEntered
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
            }
            || pendingProjectionPollingVersion != pollingVersion
            || latestPendingResolvedCount != resolvedCount)
        {
            DetachProjectionLayoutUpdatedHandler();
            return;
        }

        if (projectionPulseActive
            && lastProjectionRoute is not null)
        {
            TryAttachProjectionRenderingHandler(
                projectionPulseGeneration,
                pollingVersion,
                level,
                resolvedCount);
            return;
        }

        StartProjectionPulse(
            level,
            resolvedCount,
            pollingVersion,
            retryCount: 0);
    }

    private void OnProjectionRendering(
        long generation,
        long pollingVersion,
        EventArgs args)
    {
        if (generation != projectionPulseGeneration
            || !projectionPulseActive
            || revealVisualStateEntered
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
            }
            || args is not RenderingEventArgs rendering)
        {
            return;
        }

        if (projectionLastRenderingTime == rendering.RenderingTime)
        {
            return;
        }
        projectionLastRenderingTime = rendering.RenderingTime;

        if (lastProjectionRoute is not { } route
            || !CanObserveProjectionComposition(route))
        {
            return;
        }

        if (!projectionCompositionObserved)
        {
            projectionCompositionObserved = true;
            projectionCompositionRenderingTime = rendering.RenderingTime;
            LogProjectionDiagnostic(
                "ProjectionCompositionObserved",
                pollingVersion,
                $"generation={generation}; renderingTime={rendering.RenderingTime.TotalMilliseconds:0.###}ms");
            StartProjectionPresentation(
                route,
                projectionPulseMotionLevel,
                generation,
                pollingVersion);
            return;
        }

        projectionPostStartRenderCount++;
        if (projectionPostStartRenderCount == 1)
        {
            projectionPulseFirstRenderingTime = rendering.RenderingTime;
            projectionPulseFirstRenderAt = DateTimeOffset.UtcNow;
            LogProjectionDiagnostic(
                "ProjectionPulseFirstRender",
                pollingVersion,
                $"generation={generation}; renderingTime={rendering.RenderingTime.TotalMilliseconds:0.###}ms");
        }
        else if (projectionPostStartRenderCount == 2)
        {
            projectionPulseSecondRenderingTime = rendering.RenderingTime;
            LogProjectionDiagnostic(
                "ProjectionPulseSecondRender",
                pollingVersion,
                $"generation={generation}; renderingTime={rendering.RenderingTime.TotalMilliseconds:0.###}ms");
        }

        if (!projectionPulseVisibleFrameCommitted
            && projectionPostStartRenderCount >= 2
            && IsProjectionPulseVisuallyPresent())
        {
            projectionPulseVisibleFrameCommitted = true;
            ProjectionPulseVisibleFrameCommittedAt = DateTimeOffset.UtcNow;
            projectionRequestState =
                ProjectionRequestState.VisibleFrameCommitted;
            if (commitPendingForProjection
                && Snapshot is
                {
                    Phase: StartupSequencePhase.Lock,
                    CanCommit: true
                } lockSnapshot)
            {
                DisarmProjectionVisualGate();
                ArmProjectionVisualGate(lockSnapshot);
            }
            LogProjectionDiagnostic(
                "ProjectionPulseVisibleFrameCommitted",
                pollingVersion,
                $"generation={generation}; renderingTime={rendering.RenderingTime.TotalMilliseconds:0.###}ms");
        }

        if (!projectionPulseMinimumVisibleReached
            && projectionPulseFirstRenderAt.HasValue
            && DateTimeOffset.UtcNow - projectionPulseFirstRenderAt.Value
                >= ResolveProjectionMinimumVisibleDuration(
                    projectionPulseMotionLevel))
        {
            projectionPulseMinimumVisibleReached = true;
            projectionPulseMinimumVisibleReachedAt = DateTimeOffset.UtcNow;
            LogProjectionDiagnostic(
                "ProjectionPulseMinimumVisibleReached",
                pollingVersion,
                $"generation={generation}; minimum={ResolveProjectionMinimumVisibleDuration(projectionPulseMotionLevel).TotalMilliseconds:0}ms");
        }

        TryCompleteProjectionPresentation(generation, pollingVersion);
    }

    private void StartProjectionPresentation(
        ProjectionRoute route,
        MotionLevel level,
        long generation,
        long pollingVersion)
    {
        ProjectionPulseTiming timing = ProjectionPulseTiming.Create(route, level);
        projectionPulseStartedAt = DateTimeOffset.UtcNow;
        projectionRequestState = ProjectionRequestState.Playing;
        LogProjectionDiagnostic(
            "ProjectionPulseStarted",
            pollingVersion,
            $"generation={generation}; resolved={latestPendingResolvedCount}; renderingTime={projectionCompositionRenderingTime?.TotalMilliseconds:0.###}ms");
        AnimateProjectionSegments(route, timing);
        AnimateProjectionCanvas(timing, generation);
        if (level == MotionLevel.Full)
        {
            AnimatePulseHead(route, timing);
        }
    }

    private void ContinueProjectionAnchorWaitAfterLifecycleEvent()
    {
        if (!projectionRetryScheduled
            || projectionRetryPollingVersion < 0
            || projectionRetryResolvedCount < 0
            || Snapshot is not
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
            } snapshot)
        {
            return;
        }

        ContinueProjectionReadiness(
            projectionRequestGeneration,
            snapshot.MotionLevel,
            projectionRetryResolvedCount,
            projectionRetryPollingVersion);
    }

    private bool IsProjectionPulseVisuallyPresent()
    {
        Window? hostWindow = Window.GetWindow(this);
        bool overlayVisible = IsLoaded
            && Visibility == Visibility.Visible
            && Opacity > 0d
            && hostWindow is { IsVisible: true }
            && hostWindow.Opacity > 0d
            && PresentationSource.FromVisual(OverlayRoot) is not null;
        return overlayVisible
            && ProjectionPulseCanvas.IsLoaded
            && ProjectionPulseCanvas.Visibility == Visibility.Visible
            && ProjectionPulseCanvas.Opacity > 0d
            && lastProjectionRoute is { TotalRouteLength: > 24d }
            && new[]
            {
                ProjectionSourceHorizontalSegment,
                ProjectionVerticalBridgeSegment,
                ProjectionTargetHorizontalSegment
            }.Any(segment =>
                segment.Visibility == Visibility.Visible
                && segment.Opacity > 0d
                && segment.ActualWidth > 0d
                && segment.ActualHeight > 0d
                && segment.Clip is RectangleGeometry geometry
                && ((geometry.Rect.Width > 0.1d
                        && geometry.Rect.Width
                            <= Math.Max(segment.ActualWidth, segment.Width) + 0.1d)
                    || (geometry.Rect.Height > 0.1d
                        && geometry.Rect.Height
                            <= Math.Max(segment.ActualHeight, segment.Height) + 0.1d)))
            && (Snapshot?.MotionLevel != MotionLevel.Full
                || ProjectionPulseHead.Opacity > 0d
                || ProjectionPulseHead.RenderTransform
                    is TranslateTransform pulseTransform
                    && (Math.Abs(pulseTransform.X) > 0.1d
                        || Math.Abs(pulseTransform.Y) > 0.1d));
    }

    private void DetachProjectionRenderingHandler()
    {
        if (projectionRenderingHandler is not null)
        {
            StartupRenderSource.Rendering -= projectionRenderingHandler;
            projectionRenderingHandler = null;
        }
    }

    private void DetachProjectionLayoutUpdatedHandler()
    {
        if (projectionLayoutUpdatedHandler is not null)
        {
            LayoutUpdated -= projectionLayoutUpdatedHandler;
            projectionLayoutUpdatedHandler = null;
        }
        projectionRetryScheduled = false;
    }

    private ProjectionRouteResolution TryResolveProjectionRoute(out ProjectionRoute route)
    {
        route = default;
        StartupMilestoneRow[] rows = GetMilestoneRows();
        int sensorIndex = Snapshot?.Milestones
            .Select((milestone, index) => (milestone, index))
            .FirstOrDefault(item => item.milestone.Id == StartupMilestoneId.SensorBus)
            .index ?? -1;
        if (sensorIndex < 0
            || sensorIndex >= rows.Length
            || !IsLoaded
            || !IsArrangeValid
            || OverlayRoot.ActualWidth <= 0d
            || OverlayRoot.ActualHeight <= 0d
            || !rows[sensorIndex].IsLoaded
            || !ProjectionInputAnchor.IsLoaded)
        {
            return ProjectionRouteResolution.LayoutPending;
        }

        FrameworkElement sourceAnchor = rows[sensorIndex].RouteOutputAnchorElement;
        if (sourceAnchor.ActualWidth <= 0d
            || sourceAnchor.ActualHeight <= 0d
            || ProjectionInputAnchor.ActualWidth <= 0d
            || ProjectionInputAnchor.ActualHeight <= 0d
            || !sourceAnchor.IsArrangeValid
            || !ProjectionInputAnchor.IsArrangeValid
            || PresentationSource.FromVisual(OverlayRoot) is null
            || PresentationSource.FromVisual(sourceAnchor) is null
            || PresentationSource.FromVisual(ProjectionInputAnchor) is null)
        {
            return ProjectionRouteResolution.LayoutPending;
        }

        try
        {
            Point source = sourceAnchor.TranslatePoint(
                new Point(sourceAnchor.ActualWidth / 2d, sourceAnchor.ActualHeight / 2d),
                OverlayRoot);
            Point target = ProjectionInputAnchor.TranslatePoint(
                new Point(
                    ProjectionInputAnchor.ActualWidth / 2d,
                    ProjectionInputAnchor.ActualHeight / 2d),
                OverlayRoot);
            if (!double.IsFinite(source.X)
                || !double.IsFinite(source.Y)
                || !double.IsFinite(target.X)
                || !double.IsFinite(target.Y))
            {
                return ProjectionRouteResolution.Invalid;
            }

            return TryCreateProjectionRoute(source, target, out route)
                ? ProjectionRouteResolution.Success
                : ProjectionRouteResolution.Invalid;
        }
        catch (InvalidOperationException)
        {
            return ProjectionRouteResolution.LayoutPending;
        }
    }

    internal static bool TryCreateProjectionRoute(
        Point source,
        Point target,
        out ProjectionRoute route)
    {
        route = default;
        if (!double.IsFinite(source.X)
            || !double.IsFinite(source.Y)
            || !double.IsFinite(target.X)
            || !double.IsFinite(target.Y)
            || target.X <= source.X)
        {
            return false;
        }

        double horizontalDistance = target.X - source.X;
        double verticalDistance = target.Y - source.Y;
        double absoluteVerticalDistance = Math.Abs(verticalDistance);
        if (horizontalDistance < 24d)
        {
            return false;
        }

        if (absoluteVerticalDistance <= 1d)
        {
            route = new ProjectionRoute(
                source,
                target,
                target.X,
                horizontalDistance,
                0d,
                0d,
                horizontalDistance,
                false);
            return true;
        }

        if (horizontalDistance < 36d)
        {
            return false;
        }

        double minimumSegmentLength = horizontalDistance >= 72d ? 24d : 12d;
        double corridorX = AlignToHalfDip(Math.Clamp(
            source.X + (horizontalDistance * 0.5d),
            source.X + minimumSegmentLength,
            target.X - minimumSegmentLength));
        double sourceHorizontalLength = corridorX - source.X;
        double verticalBridgeLength = Math.Abs(verticalDistance);
        double targetHorizontalLength = target.X - corridorX;
        if (sourceHorizontalLength < minimumSegmentLength
            || targetHorizontalLength < minimumSegmentLength)
        {
            return false;
        }

        route = new ProjectionRoute(
            source,
            target,
            corridorX,
            sourceHorizontalLength,
            verticalBridgeLength,
            targetHorizontalLength,
            sourceHorizontalLength + verticalBridgeLength + targetHorizontalLength,
            true);
        return true;
    }

    private void ConfigureProjectionGeometry(ProjectionRoute route)
    {
        ProjectionPulseCanvas.Width = Math.Max(1d, OverlayRoot.ActualWidth);
        ProjectionPulseCanvas.Height = Math.Max(1d, OverlayRoot.ActualHeight);
        ProjectionPulseCanvas.Opacity = 1d;

        ConfigureProjectionGeometry(
            ProjectionDormantSourceSegment,
            ProjectionDormantVerticalSegment,
            ProjectionDormantTargetSegment,
            route,
            ResolveProjectionDormantOpacity(Snapshot?.MotionLevel ?? MotionLevel.Off));
        ConfigureProjectionGeometry(
            ProjectionSourceHorizontalSegment,
            ProjectionVerticalBridgeSegment,
            ProjectionTargetHorizontalSegment,
            route,
            1d);

        ProjectionPulseHead.Opacity = 0d;
        Canvas.SetLeft(
            ProjectionPulseHead,
            AlignToHalfDip(route.Source.X - 2.5d));
        Canvas.SetTop(
            ProjectionPulseHead,
            AlignToHalfDip(route.Source.Y - 2.5d));
    }

    private static void ConfigureProjectionGeometry(
        FrameworkElement sourceSegment,
        FrameworkElement verticalSegment,
        FrameworkElement targetSegment,
        ProjectionRoute route,
        double opacity)
    {
        ConfigureHorizontalSegment(
            sourceSegment,
            route.Source.X,
            route.Source.Y,
            route.SourceHorizontalLength,
            opacity);
        if (!route.UsesThreeSegments)
        {
            HideProjectionSegment(verticalSegment);
            HideProjectionSegment(targetSegment);
        }
        else
        {
            verticalSegment.Visibility = Visibility.Visible;
            verticalSegment.Opacity = opacity;
            verticalSegment.Height =
                AlignToHalfDip(route.VerticalBridgeLength);
            Canvas.SetLeft(
                verticalSegment,
                AlignToHalfDip(route.CorridorX - 0.5d));
            Canvas.SetTop(
                verticalSegment,
                AlignToHalfDip(Math.Min(route.Source.Y, route.Target.Y)));
            ConfigureHorizontalSegment(
                targetSegment,
                route.CorridorX,
                route.Target.Y,
                route.TargetHorizontalLength,
                opacity);
        }
    }

    private static void ConfigureHorizontalSegment(
        FrameworkElement segment,
        double left,
        double centerY,
        double length,
        double opacity)
    {
        segment.Visibility = Visibility.Visible;
        segment.Opacity = opacity;
        segment.Width = AlignToHalfDip(length);
        Canvas.SetLeft(segment, AlignToHalfDip(left));
        Canvas.SetTop(segment, AlignToHalfDip(centerY - 0.5d));
    }

    private static double AlignToHalfDip(double value) =>
        Math.Round(value * 2d, MidpointRounding.AwayFromZero) / 2d;

    private static void HideProjectionSegment(FrameworkElement segment)
    {
        segment.Visibility = Visibility.Collapsed;
        segment.Opacity = 0d;
        segment.Clip = null;
    }

    private void AnimateProjectionSegments(
        ProjectionRoute route,
        ProjectionPulseTiming timing)
    {
        AnimateHorizontalProjectionSegment(
            ProjectionSourceHorizontalSegment,
            route.SourceHorizontalLength,
            TimeSpan.Zero,
            timing.SourceDuration);
        if (!route.UsesThreeSegments)
        {
            return;
        }

        AnimateVerticalProjectionSegment(
            ProjectionVerticalBridgeSegment,
            route.VerticalBridgeLength,
            route.Target.Y >= route.Source.Y,
            timing.VerticalStart,
            timing.VerticalDuration);
        AnimateHorizontalProjectionSegment(
            ProjectionTargetHorizontalSegment,
            route.TargetHorizontalLength,
            timing.TargetStart,
            timing.TargetDuration);
    }

    private static void AnimateHorizontalProjectionSegment(
        FrameworkElement segment,
        double length,
        TimeSpan delay,
        TimeSpan duration)
    {
        double visibleStart = Math.Min(1d, length);
        RectangleGeometry clip = new(new Rect(0d, 0d, visibleStart, 1d));
        segment.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, visibleStart, 1d),
            new Rect(0d, 0d, length, 1d),
            delay,
            duration);
    }

    private static void AnimateVerticalProjectionSegment(
        FrameworkElement segment,
        double length,
        bool topToBottom,
        TimeSpan delay,
        TimeSpan duration)
    {
        double visibleStart = Math.Min(1d, length);
        Rect initial = topToBottom
            ? new Rect(0d, 0d, 1d, visibleStart)
            : new Rect(0d, Math.Max(0d, length - visibleStart), 1d, visibleStart);
        Rect final = new(0d, 0d, 1d, length);
        RectangleGeometry clip = new(initial);
        segment.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            initial,
            final,
            delay,
            duration);
    }

    private void AnimateProjectionCanvas(
        ProjectionPulseTiming timing,
        long generation)
    {
        DoubleAnimationUsingKeyFrames opacity = BuildProjectionPulseOpacity(timing);
        opacity.Completed += (_, _) => OnProjectionPulseCompleted(generation);
        ProjectionSourceHorizontalSegment.Opacity = 0d;
        ProjectionSourceHorizontalSegment.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);
        foreach (FrameworkElement segment in new[]
                 {
                     ProjectionVerticalBridgeSegment,
                     ProjectionTargetHorizontalSegment
                 })
        {
            if (segment.Visibility == Visibility.Visible)
            {
                segment.Opacity = 0d;
                segment.BeginAnimation(
                    OpacityProperty,
                    BuildProjectionPulseOpacity(timing),
                    HandoffBehavior.SnapshotAndReplace);
            }
        }
    }

    private static DoubleAnimationUsingKeyFrames BuildProjectionPulseOpacity(
        ProjectionPulseTiming timing)
    {
        TimeSpan holdEnd = timing.BuildDuration + timing.HoldDuration;
        TimeSpan fadeEnd = holdEnd + timing.FadeDuration;
        DoubleAnimationUsingKeyFrames opacity = new() { FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(
            1d,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
            1d,
            KeyTime.FromTimeSpan(holdEnd)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
            0d,
            KeyTime.FromTimeSpan(fadeEnd)));
        return opacity;
    }

    private void AnimatePulseHead(
        ProjectionRoute route,
        ProjectionPulseTiming timing)
    {
        TranslateTransform transform = EnsureTranslateTransform(ProjectionPulseHead);
        transform.X = 0d;
        transform.Y = 0d;
        TimeSpan holdEnd = timing.BuildDuration + timing.HoldDuration;
        TimeSpan fadeEnd = holdEnd + timing.FadeDuration;
        DoubleAnimationUsingKeyFrames opacity = new() { FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(
            0d,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
            0.9d,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(30))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
            0.9d,
            KeyTime.FromTimeSpan(holdEnd)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(
            0d,
            KeyTime.FromTimeSpan(fadeEnd)));
        ProjectionPulseHead.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);

        DoubleAnimationUsingKeyFrames x = new() { FillBehavior = FillBehavior.Stop };
        DoubleAnimationUsingKeyFrames y = new() { FillBehavior = FillBehavior.Stop };
        x.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        y.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (route.UsesThreeSegments)
        {
            TimeSpan sourceEnd = timing.SourceDuration;
            TimeSpan verticalEnd = timing.VerticalStart + timing.VerticalDuration;
            x.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.SourceHorizontalLength,
                KeyTime.FromTimeSpan(sourceEnd)));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(
                0d,
                KeyTime.FromTimeSpan(sourceEnd)));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.SourceHorizontalLength,
                KeyTime.FromTimeSpan(verticalEnd)));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.Target.Y - route.Source.Y,
                KeyTime.FromTimeSpan(verticalEnd)));
            x.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.SourceHorizontalLength + route.TargetHorizontalLength,
                KeyTime.FromTimeSpan(timing.BuildDuration)));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.Target.Y - route.Source.Y,
                KeyTime.FromTimeSpan(timing.BuildDuration)));
        }
        else
        {
            x.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.SourceHorizontalLength,
                KeyTime.FromTimeSpan(timing.BuildDuration)));
            y.KeyFrames.Add(new LinearDoubleKeyFrame(
                route.Target.Y - route.Source.Y,
                KeyTime.FromTimeSpan(timing.BuildDuration)));
        }

        transform.BeginAnimation(
            TranslateTransform.XProperty,
            x,
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            y,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnProjectionPulseCompleted(long generation)
    {
        if (generation != projectionPulseGeneration)
        {
            return;
        }

        projectionPulseAnimationCompleted = true;
        projectionPulseAnimationCompletedAt = DateTimeOffset.UtcNow;
        LogProjectionDiagnostic(
            "ProjectionPulseAnimationCompleted",
            Snapshot?.InitialProjection.PollingVersion ?? -1,
            $"generation={generation}");
        if (!projectionPulseMinimumVisibleReached
            || !projectionPulseVisibleFrameCommitted)
        {
            projectionRequestState = ProjectionRequestState.HoldingVisible;
            HoldCompletedProjectionRouteVisible();
        }

        TryCompleteProjectionPresentation(
            generation,
            Snapshot?.InitialProjection.PollingVersion ?? -1);
    }

    private void HoldCompletedProjectionRouteVisible()
    {
        if (lastProjectionRoute is not { } route)
        {
            return;
        }

        foreach (FrameworkElement segment in new FrameworkElement[]
                 {
                     ProjectionSourceHorizontalSegment,
                     ProjectionVerticalBridgeSegment,
                     ProjectionTargetHorizontalSegment
                 })
        {
            segment.BeginAnimation(OpacityProperty, null);
            if (segment.Visibility == Visibility.Visible)
            {
                segment.Opacity = 1d;
            }
        }

        if (ProjectionSourceHorizontalSegment.Clip
            is RectangleGeometry sourceClip)
        {
            sourceClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            sourceClip.Rect = new Rect(
                0d,
                0d,
                route.SourceHorizontalLength,
                1d);
        }
        if (route.UsesThreeSegments
            && ProjectionVerticalBridgeSegment.Clip
                is RectangleGeometry verticalClip)
        {
            verticalClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            verticalClip.Rect = new Rect(
                0d,
                0d,
                1d,
                route.VerticalBridgeLength);
        }
        if (route.UsesThreeSegments
            && ProjectionTargetHorizontalSegment.Clip
                is RectangleGeometry targetClip)
        {
            targetClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            targetClip.Rect = new Rect(
                0d,
                0d,
                route.TargetHorizontalLength,
                1d);
        }

        ProjectionPulseHead.BeginAnimation(OpacityProperty, null);
        ClearTranslation(ProjectionPulseHead);
        ProjectionPulseHead.Opacity = 0d;
    }

    private void TryCompleteProjectionPresentation(
        long generation,
        long pollingVersion)
    {
        if (generation != projectionPulseGeneration
            || !projectionPulseActive
            || !projectionPulseAnimationCompleted
            || !projectionPulseMinimumVisibleReached
            || !projectionPulseVisibleFrameCommitted)
        {
            return;
        }

        ProjectionPulseCompletedAt = DateTimeOffset.UtcNow;
        projectionRequestState = ProjectionRequestState.Completed;
        LogProjectionDiagnostic(
            "ProjectionPulseCompleted",
            pollingVersion,
            $"generation={generation}");
        DetachProjectionRenderingHandler();
        DetachProjectionLayoutUpdatedHandler();
        ClearProjectionPulseVisuals();
        projectionPulseActive = false;
        DisarmProjectionVisualGate();

        if (projectionPulsePending
            && IsLoaded
            && Snapshot is
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Bind or StartupSequencePhase.Lock
            })
        {
            TryStartLatchedProjectionPulse();
            if (projectionPulseActive)
            {
                return;
            }
        }

        if (commitPendingForProjection
            && !revealVisualStateEntered
            && Snapshot is
            {
                IsActive: true,
                HasCompleted: false,
                Phase: StartupSequencePhase.Lock,
                CanCommit: true
            } lockSnapshot)
        {
            commitPendingForProjection = false;
            LogProjectionDiagnostic(
                "CommitReleasedAfterProjection",
                lockSnapshot.InitialProjection.PollingVersion,
                $"generation={generation}");
            ScheduleCommitEvaluation();
            return;
        }

        projectionPulsePending = false;
    }

    internal static TimeSpan ResolveProjectionMinimumVisibleDuration(
        MotionLevel level) =>
        TimeSpan.FromMilliseconds(
            level == MotionLevel.Full ? 180d : 140d);

    private void ArmProjectionVisualGate(StartupSequenceSnapshot snapshot)
    {
        if (projectionVisualGateArmed)
        {
            return;
        }

        projectionVisualGateArmed = true;
        long generation = ++projectionVisualGateGeneration;
        _ = WaitForProjectionVisualGateTimeoutAsync(generation);
    }

    private async Task WaitForProjectionVisualGateTimeoutAsync(long generation)
    {
        try
        {
            bool completionGuard =
                projectionPulseVisibleFrameCommitted;
            TimeSpan timeout;
            if (completionGuard)
            {
                timeout = TimeSpan.FromMilliseconds(1500);
            }
            else
            {
                TimeSpan visibleFrameTimeout =
                    TimeSpan.FromMilliseconds(700);
                TimeSpan requestAge = projectionRequestTimestamp == default
                    ? TimeSpan.Zero
                    : DateTimeOffset.UtcNow - projectionRequestTimestamp;
                timeout = TimeSpan.FromMilliseconds(
                    Math.Max(
                        0d,
                        visibleFrameTimeout.TotalMilliseconds
                            - requestAge.TotalMilliseconds));
            }
            await Task.Delay(timeout).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != projectionVisualGateGeneration)
                {
                    return;
                }

                projectionVisualGateArmed = false;
                projectionVisualGateGeneration++;
                if (!commitPendingForProjection
                    || Snapshot is not
                    {
                        IsActive: true,
                        HasCompleted: false,
                        Phase: StartupSequencePhase.Lock,
                        CanCommit: true
                    } lockSnapshot)
                {
                    return;
                }

                string failOpenReason = ResolveProjectionFailOpenReason(
                    completionGuard);
                LogProjectionDiagnostic(
                    completionGuard
                        ? "ProjectionPulseCompletionGuard"
                        : "ProjectionPulseCompositionTimeout",
                    pendingProjectionPollingVersion,
                    $"requestGeneration={projectionRequestGeneration}; age={(DateTimeOffset.UtcNow - projectionRequestTimestamp).TotalMilliseconds:0}ms; reason={failOpenReason}");
                projectionPulseGeneration++;
                DetachProjectionRenderingHandler();
                DetachProjectionLayoutUpdatedHandler();
                projectionPulseActive = false;
                projectionPulsePending = false;
                projectionRequestState = ProjectionRequestState.TimedOut;
                projectionRetryScheduled = false;
                ClearProjectionPulseVisuals();
                commitPendingForProjection = false;
                LogProjectionDiagnostic(
                    "CommitReleasedAfterProjection",
                    lockSnapshot.InitialProjection.PollingVersion,
                    $"failOpen={failOpenReason}");
                ScheduleCommitEvaluation();
            }, DispatcherPriority.Render);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private string ResolveProjectionFailOpenReason(bool completionGuard)
    {
        if (completionGuard)
        {
            return "CompletionGuard";
        }
        if (lastProjectionRoute is null)
        {
            return projectionRequestState
                == ProjectionRequestState.WaitingForAnchorLayout
                    ? "LayoutPending"
                    : "InvalidRoute";
        }
        if (PresentationSource.FromVisual(OverlayRoot) is null)
        {
            return "NoPresentationSource";
        }

        Window? hostWindow = Window.GetWindow(this);
        if (hostWindow is not { IsVisible: true } || hostWindow.Opacity <= 0d)
        {
            return "HostNotVisible";
        }
        if (!projectionCompositionObserved)
        {
            return "NoCompositionRender";
        }
        if (!projectionPulseVisibleFrameCommitted)
        {
            return "PulseNeverBecameVisible";
        }

        return "CompletionPending";
    }

    private bool IsProjectionVisualBlockingCommit =>
        projectionPulsePending
        || projectionPulseActive
        || projectionRetryScheduled
        || projectionRequestState is ProjectionRequestState.Latched
            or ProjectionRequestState.WaitingForDataLayout
            or ProjectionRequestState.WaitingForAnchorLayout
            or ProjectionRequestState.WaitingForComposition
            or ProjectionRequestState.GeometryReady
            or ProjectionRequestState.Playing
            or ProjectionRequestState.HoldingVisible
            or ProjectionRequestState.WaitingForVisibleFrame
            or ProjectionRequestState.VisibleFrameCommitted;

    private void ScheduleCommitEvaluation()
    {
        if (commitEvaluationScheduled || commitPlayed)
        {
            return;
        }

        commitEvaluationScheduled = true;
        long generation = ++commitEvaluationGeneration;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                commitEvaluationScheduled = false;
                if (generation != commitEvaluationGeneration
                    || commitPlayed
                    || revealVisualStateEntered
                    || Snapshot is not
                    {
                        IsActive: true,
                        HasCompleted: false,
                        Phase: StartupSequencePhase.Lock,
                        CanCommit: true,
                        MotionLevel: not MotionLevel.Off
                    } lockSnapshot)
                {
                    return;
                }

                if (IsProjectionVisualBlockingCommit)
                {
                    commitPendingForProjection = true;
                    ArmProjectionVisualGate(lockSnapshot);
                    return;
                }

                DisarmProjectionVisualGate();
                commitPendingForProjection = false;
                commitPlayed = true;
                PlayCommit(lockSnapshot.MotionLevel);
            }));
    }

    private void DisarmProjectionVisualGate()
    {
        projectionVisualGateGeneration++;
        projectionVisualGateArmed = false;
    }

    private void LogProjectionDiagnostic(
        string eventName,
        long pollingVersion,
        string reason)
    {
        ProjectionRoute? route = lastProjectionRoute;
        double sourceClip = ProjectionSourceHorizontalSegment.Clip
            is RectangleGeometry sourceGeometry
                ? sourceGeometry.Rect.Width
                : 0d;
        Window? hostWindow = Window.GetWindow(this);
        AppLogger.LogKeyEvent(
            $"ProjectionPulseRuntime | event={eventName}; t={runtimeDiagnosticClock.Elapsed.TotalMilliseconds:0}ms; " +
            $"startup={Snapshot?.StartedAt?.UtcTicks ?? -1}; snapshot={latestVersion}; " +
            $"phase={Snapshot?.Phase}; polling={pollingVersion}; resolved={latestPendingResolvedCount}; " +
            $"requestGeneration={projectionRequestGeneration}; pulseGeneration={projectionPulseGeneration}; " +
            $"state={projectionRequestState}; renderingTime={projectionLastRenderingTime?.TotalMilliseconds:0.###}; " +
            $"renderCount={projectionPostStartRenderCount}; " +
            $"postLayout={pendingProjectionHasPostDataLayout}; pending={projectionPulsePending}; " +
            $"active={projectionPulseActive}; visibleFrame={projectionPulseVisibleFrameCommitted}; " +
            $"animationCompleted={projectionPulseAnimationCompleted}; " +
            $"minimumVisibleReached={projectionPulseMinimumVisibleReached}; " +
            $"completed={ProjectionPulseCompletedAt.HasValue}; commitPending={commitPendingForProjection}; " +
            $"commitStarted={commitVisualStartedAt.HasValue}; loaded={IsLoaded}; " +
            $"visibility={Visibility}; opacity={Opacity:0.###}; canvasVisibility={ProjectionPulseCanvas.Visibility}; " +
            $"canvasOpacity={ProjectionPulseCanvas.Opacity:0.###}; sourceLoaded={ProjectionSourceHorizontalSegment.IsLoaded}; " +
            $"targetLoaded={ProjectionInputAnchor.IsLoaded}; sourceArrange={ProjectionSourceHorizontalSegment.IsArrangeValid}; " +
            $"targetArrange={ProjectionInputAnchor.IsArrangeValid}; routeLength={(route?.TotalRouteLength ?? 0d):0.##}; " +
            $"clipWidth={sourceClip:0.##}; segmentOpacity={ProjectionSourceHorizontalSegment.Opacity:0.###}; " +
            $"headOpacity={ProjectionPulseHead.Opacity:0.###}; presentationSource={PresentationSource.FromVisual(OverlayRoot) is not null}; " +
            $"hostVisible={hostWindow is { IsVisible: true } && hostWindow.Opacity > 0d}; dispatcher=Render; reason={reason}");
    }

    private void StartRevealExit(MotionLevel level, long generation)
    {
        if (generation != revealHoldGeneration)
        {
            return;
        }

        commitRevealCompensationPending = false;
        LogStartupDiagnostic("OverlayExitStarted", level.ToString());
        if (level == MotionLevel.Off)
        {
            CompleteRevealExit(generation);
            return;
        }

        TimeSpan duration = ResolveRevealExitDuration(level);
        AnimateExitOpacity(this, TimeSpan.Zero, duration, 0d);
        if (commitPlayed && CommitGroup.Visibility == Visibility.Visible)
        {
            PlayCommitExit();
        }
        else
        {
            CleanupCommitVisualState();
        }

        if (level == MotionLevel.Full)
        {
            TranslateTransform transform = EnsureTranslateTransform(StartupContentLayer);
            transform.X = -4d;
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                BuildDoubleAnimation(0d, -4d, TimeSpan.Zero, duration),
                HandoffBehavior.SnapshotAndReplace);
        }

        DoubleAnimation exitHold = new(0d, 0d, duration)
        {
            FillBehavior = FillBehavior.Stop
        };
        exitHold.Completed += (_, _) =>
        {
            RevealPresentationHold.BeginAnimation(OpacityProperty, null);
            CompleteRevealExit(generation);
        };
        RevealPresentationHold.BeginAnimation(
            OpacityProperty,
            exitHold,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void PlayCommit(MotionLevel level)
    {
        if (level == MotionLevel.Off)
        {
            CleanupCommitVisualState();
            return;
        }

        commitPresentationGeneration++;
        CommitPresentationHold.BeginAnimation(OpacityProperty, null);
        commitVisualStartedAt = DateTimeOffset.UtcNow;
        LogProjectionDiagnostic(
            "CommitStarted",
            Snapshot?.InitialProjection.PollingVersion ?? -1,
            $"requestState={projectionRequestState}");
        commitMinimumPresentationReached = false;
        commitRevealCompensationPending = false;
        ScheduleCommitMinimumPresentation(level, commitPresentationGeneration);
        ScheduleCommitStableHold(level, commitPresentationGeneration);
        CommitGroup.Visibility = Visibility.Visible;
        CommitGroup.Opacity = 1d;
        CommitExitRoot.Opacity = 1d;
        CommitGraphicLayer.Opacity = 0.82d;
        CommitLock.Opacity = 1d;
        CommitText.Opacity = 1d;

        TimeSpan buildDuration = ResolveCommitBuildDuration(level);
        AnimateOpacity(
            CommitGraphicLayer,
            TimeSpan.Zero,
            buildDuration,
            0d,
            0.82d);
        AnimateOpacity(
            CommitText,
            TimeSpan.Zero,
            buildDuration,
            0d,
            1d);
        if (level == MotionLevel.Reduced)
        {
            ClearGeometry(CommitCenterClipHost);
            return;
        }

        RectangleGeometry centerClip = new();
        CommitCenterClipHost.Clip = centerClip;
        AnimateRectWithCommittedFinalState(
            centerClip,
            new Rect(0d, 0d, 0d, 6d),
            new Rect(0d, 0d, 6d, 6d),
            TimeSpan.Zero,
            buildDuration);
    }

    private void UpdateMilestonePresentations(StartupSequenceSnapshot snapshot)
    {
        for (int index = 0;
             index < milestonePresentations.Length
                && index < snapshot.Milestones.Count;
             index++)
        {
            milestonePresentations[index].Update(snapshot.Milestones[index]);
        }
    }

    private void EnsureInitialMilestoneLayout()
    {
        if (milestoneInitialLayoutCommitted
            || RouteMatrixItems.Items.Count == 0
            || RouteMatrixItems.ItemContainerGenerator.ContainerFromIndex(0) is not null)
        {
            return;
        }

        milestoneInitialLayoutCommitted = true;
        RouteMatrixItems.UpdateLayout();
        milestoneRows = [];
    }

    private void PlayCommitExit()
    {
        if (!commitPlayed || CommitGroup.Visibility != Visibility.Visible)
        {
            CleanupCommitVisualState();
            return;
        }

        CommitGraphicLayer.BeginAnimation(OpacityProperty, null);
        CommitText.BeginAnimation(OpacityProperty, null);
        CommitGraphicLayer.Opacity = 0.82d;
        CommitLock.Opacity = 1d;
        CommitText.Opacity = 1d;
        CommitExitRoot.Opacity = 0d;
        DoubleAnimation exit = new(1d, 0d, ResolveCommitExitDuration(MotionLevel.Full))
        {
            FillBehavior = FillBehavior.Stop
        };
        exit.Completed += (_, _) => CleanupCommitVisualState();
        CommitExitRoot.BeginAnimation(
            OpacityProperty,
            exit,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CleanupCommitVisualState()
    {
        CommitGroup.BeginAnimation(OpacityProperty, null);
        CommitExitRoot.BeginAnimation(OpacityProperty, null);
        CommitGraphicLayer.BeginAnimation(OpacityProperty, null);
        CommitLock.BeginAnimation(OpacityProperty, null);
        CommitText.BeginAnimation(OpacityProperty, null);
        CommitGroup.Opacity = 1d;
        CommitGroup.Visibility = Visibility.Collapsed;
        CommitExitRoot.Opacity = 1d;
        CommitGraphicLayer.Opacity = 0.82d;
        CommitLock.Opacity = 1d;
        CommitText.Opacity = 1d;
        ClearGeometry(CommitCenterClipHost);
    }

    private void CompleteRevealExit(long generation)
    {
        if (generation != revealHoldGeneration)
        {
            return;
        }

        Opacity = 0d;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        LogStartupDiagnostic("OverlayCollapsed", "RevealExitCompleted");
        StartupBackgroundLayer.Opacity = 0d;
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
        BottomRailContent.Opacity = 0d;
        RevealVisualExitCompleted?.Invoke(revealSnapshotVersion);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (generation != revealHoldGeneration)
                {
                    return;
                }

                LogStartupDiagnostic("StartupCleanupStarted", "ContextIdle");
                ClearChoreographyClocks();
                CleanupCommitVisualState();
                LogStartupDiagnostic("StartupCleanupCompleted", "ContextIdle");
            }));
    }

    private void LogStartupDiagnostic(string eventName, string reason)
    {
        AppLogger.LogKeyEvent(
            $"StartupMotionRuntime | event={eventName}; t={runtimeDiagnosticClock.Elapsed.TotalMilliseconds:0}ms; " +
            $"nav=-1; startup={latestVersion}; " +
            $"phase={Snapshot?.Phase}; reason={reason}; " +
            $"root={StartupContentLayer.Opacity:0.###}; primary=1; secondary=1; translate=(0,0); " +
            $"host=({ActualWidth:0.##},{ActualHeight:0.##})");
    }

    private void ScheduleCommitMinimumPresentation(
        MotionLevel level,
        long generation)
    {
        TimeSpan minimum = ResolveCommitMinimumPresentationDuration(level);
        if (minimum == TimeSpan.Zero)
        {
            commitMinimumPresentationReached = true;
            return;
        }

        DoubleAnimation hold = new(0d, 0d, minimum)
        {
            FillBehavior = FillBehavior.Stop
        };
        hold.Completed += (_, _) =>
        {
            CommitMinimumPresentationHold.BeginAnimation(OpacityProperty, null);
            if (generation == commitPresentationGeneration
                && commitVisualStartedAt.HasValue)
            {
                commitMinimumPresentationReached = true;
            }
        };
        CommitMinimumPresentationHold.BeginAnimation(
            OpacityProperty,
            hold,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ScheduleCommitStableHold(
        MotionLevel level,
        long generation)
    {
        TimeSpan build = ResolveCommitBuildDuration(level);
        TimeSpan stableHold = ResolveCommitStableHoldDuration(level);
        if (stableHold <= TimeSpan.Zero)
        {
            return;
        }

        DoubleAnimation hold = new(0d, 0d, stableHold)
        {
            BeginTime = build,
            FillBehavior = FillBehavior.Stop
        };
        hold.Completed += (_, _) =>
        {
            CommitPresentationHold.BeginAnimation(OpacityProperty, null);
            if (generation != commitPresentationGeneration)
            {
                return;
            }

            CommitGraphicLayer.BeginAnimation(OpacityProperty, null);
            CommitText.BeginAnimation(OpacityProperty, null);
            CommitExitRoot.Opacity = 1d;
            CommitGraphicLayer.Opacity = 0.82d;
            CommitLock.Opacity = 1d;
            CommitText.Opacity = 1d;
        };
        CommitPresentationHold.BeginAnimation(
            OpacityProperty,
            hold,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void ResetCommitPresentationState()
    {
        commitPresentationGeneration++;
        CommitPresentationHold.BeginAnimation(OpacityProperty, null);
        CommitMinimumPresentationHold.BeginAnimation(OpacityProperty, null);
        commitVisualStartedAt = null;
        commitMinimumPresentationReached = false;
        commitRevealCompensationPending = false;
    }

    private void ApplyResponsiveMargins(double width)
    {
        StartupContentLayer.Margin = ResolveContentMargin(width);
        StartupBottomRailLayer.Margin = ResolveBottomRailMargin(width);
        ApplyResponsiveMilestoneLayout(width);
    }

    private void ApplyResponsiveMilestoneLayout(double width)
    {
        foreach (StartupMilestoneRow row in GetMilestoneRows())
        {
            row.ApplyResponsiveDetailWidth(width);
        }
    }

    private StartupMilestoneRow[] GetMilestoneRows()
    {
        if (milestoneRows.Length > 0
            && milestoneRows.Length == RouteMatrixItems.Items.Count)
        {
            return milestoneRows;
        }

        List<StartupMilestoneRow> rows = [];
        for (int index = 0; index < RouteMatrixItems.Items.Count; index++)
        {
            if (RouteMatrixItems.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container
                && FindDescendant<StartupMilestoneRow>(container) is { } row)
            {
                rows.Add(row);
            }
        }

        milestoneRows = rows.ToArray();
        configuredMilestoneBreakpoint = -1;
        configuredProjectionSourceIndex = -1;
        return milestoneRows;
    }

    private Border[] PhaseSegments() =>
    [
        PhaseSegmentIndex,
        PhaseSegmentRoute,
        PhaseSegmentBind,
        PhaseSegmentLock,
        PhaseSegmentReveal
    ];

    private static int PhaseIndex(StartupSequencePhase phase) => phase switch
    {
        StartupSequencePhase.Index => 0,
        StartupSequencePhase.Route => 1,
        StartupSequencePhase.Bind => 2,
        StartupSequencePhase.Lock => 3,
        StartupSequencePhase.Reveal => 4,
        _ => -1
    };

    private static string FormatProjection(StartupInitialProjectionSnapshot projection) =>
        FormatProjection(projection, projection.ResolvedVisibleSlotCount);

    private static string FormatProjection(
        StartupInitialProjectionSnapshot projection,
        int resolvedCount) =>
        $"{resolvedCount} / {projection.TotalVisibleSlotCount} RESOLVED";

    private static T? FindDescendant<T>(DependencyObject source) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(source);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void ClearChoreographyClocks()
    {
        indexRevealGeneration++;
        indexRevealRetryScheduled = false;
        revealHoldGeneration++;
        foreach (UIElement element in new UIElement[]
                 {
                     this, StartupBackgroundLayer, StartupContentLayer, StartupBottomRailLayer,
                     BottomRailContent, SystemIndexText, TraceworkTitleText, StartupSubtitleText,
                     StartupTitleGroup, SystemRouteLabel, RouteMatrixItems, LedgerIdentityGroup,
                     LedgerEnvironmentGroup, LedgerProjectionGroup, ProjectionPulseCanvas,
                     ProjectionDormantSourceSegment, ProjectionDormantVerticalSegment,
                     ProjectionDormantTargetSegment,
                     ProjectionSourceHorizontalSegment, ProjectionVerticalBridgeSegment,
                     ProjectionTargetHorizontalSegment, ProjectionPulseHead,
                     CommitGroup, CommitExitRoot, CommitGraphicLayer, CommitLock,
                     CommitText, CommitPresentationHold,
                     RevealPresentationHold,
                     CommitMinimumPresentationHold
                 })
        {
            ClearAnimation(element, OpacityProperty);
        }

        ClearBottomTextClocks();
        ClearProjectionValueClocks();
        ClearTranslation(TraceworkTitleText);
        ClearTranslation(StartupSubtitleText);
        ClearTranslation(LedgerEnvironmentGroup);
        ClearTranslation(LedgerProjectionGroup);
        ClearTranslation(StartupContentLayer);
        ClearGeometry(SystemIndexClipHost);
        ClearGeometry(StartupBottomRailLayer);
        ClearGeometry(StartupContentLayer);
        ClearGeometry(CommitCenterClipHost);
        ClearGeometry(BottomPhaseTextClipHost);
        ClearGeometry(BottomPhaseCodeClipHost);
        ClearGeometry(ProjectionValueClipHost);
        foreach (Border segment in PhaseSegments())
        {
            ClearGeometry(segment);
            segment.BeginAnimation(OpacityProperty, null);
            if (segment.Background is SolidColorBrush { IsFrozen: false } brush)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            }
        }

        CleanupProjectionPulse();
    }

    private void ClearBottomTextClocks()
    {
        foreach (UIElement element in new UIElement[]
                 {
                     BottomPreviousPhaseText, BottomCurrentPhaseText,
                     BottomPreviousPhaseCode, BottomCurrentPhaseCode
                 })
        {
            element.BeginAnimation(OpacityProperty, null);
            ClearTranslation(element);
        }

        BottomPhaseAnimationHold.BeginAnimation(OpacityProperty, null);
        ClearGeometry(BottomPhaseTextClipHost);
        ClearGeometry(BottomPhaseCodeClipHost);
    }

    private void ClearProjectionValueClocks()
    {
        ProjectionPreviousValue.BeginAnimation(OpacityProperty, null);
        ProjectionCurrentValue.BeginAnimation(OpacityProperty, null);
        ClearTranslation(ProjectionPreviousValue);
        ClearTranslation(ProjectionCurrentValue);
        ClearGeometry(ProjectionValueClipHost);
    }

    private void CleanupBottomRail()
    {
        ClearBottomTextClocks();
        pendingBottomPhases.Clear();
        bottomPhaseTransitionActive = false;
        BottomPreviousPhaseText.Opacity = 0d;
        BottomCurrentPhaseText.Opacity = 1d;
        BottomPreviousPhaseCode.Opacity = 0d;
        BottomCurrentPhaseCode.Opacity = 1d;
        SetTranslation(BottomPreviousPhaseText, 0d, 0d);
        SetTranslation(BottomCurrentPhaseText, 0d, 0d);
        SetTranslation(BottomPreviousPhaseCode, 0d, 0d);
        SetTranslation(BottomCurrentPhaseCode, 0d, 0d);
        BottomPreviousPhaseText.Text = string.Empty;
        BottomPreviousPhaseCode.Text = string.Empty;
        BottomCurrentPhaseText.Text = string.Empty;
        BottomCurrentPhaseCode.Text = string.Empty;
        foreach (Border segment in PhaseSegments())
        {
            ClearGeometry(segment);
            segment.BeginAnimation(OpacityProperty, null);
        }
    }

    private void CleanupProjectionTransition()
    {
        ClearProjectionValueClocks();
        previousProjectionText = string.Empty;
        ProjectionPreviousValue.Text = string.Empty;
        ProjectionPreviousValue.Opacity = 0d;
        ProjectionCurrentValue.Text = currentProjectionText;
        ProjectionCurrentValue.Opacity = 1d;
        SetTranslation(ProjectionPreviousValue, 0d, 0d);
        SetTranslation(ProjectionCurrentValue, 0d, 0d);
    }

    private void CleanupProjectionPulse()
    {
        LogProjectionCancellationIfNeeded("Cleanup");
        projectionPulseGeneration++;
        projectionPulseActive = false;
        projectionPulseAnimationCompleted = false;
        projectionPulseMinimumVisibleReached = false;
        projectionPulsePending = false;
        projectionPulseVisibleFrameCommitted = false;
        projectionCompositionObserved = false;
        DetachProjectionRenderingHandler();
        DetachProjectionLayoutUpdatedHandler();
        projectionRetryScheduled = false;
        projectionDormantRetryScheduled = false;
        commitPendingForProjection = false;
        projectionLedgerReady = false;
        pendingProjectionHasPostDataLayout = false;
        projectionRequestState = ProjectionRequestState.Cancelled;
        DisarmProjectionVisualGate();
        lastProjectionRoute = null;
        ClearProjectionPulseVisuals();
        HideProjectionDormantChannel();
    }

    private void StopProjectionPulseForReveal()
    {
        LogProjectionCancellationIfNeeded("Reveal");
        projectionPulseGeneration++;
        projectionPulsePending = false;
        projectionPulseActive = false;
        projectionPulseAnimationCompleted = false;
        projectionPulseMinimumVisibleReached = false;
        projectionPulseVisibleFrameCommitted = false;
        projectionCompositionObserved = false;
        DetachProjectionRenderingHandler();
        DetachProjectionLayoutUpdatedHandler();
        projectionRetryScheduled = false;
        projectionDormantRetryScheduled = false;
        commitPendingForProjection = false;
        lastProjectionRoute = null;
        pendingProjectionHasPostDataLayout = false;
        projectionRequestState = ProjectionRequestState.Cancelled;
        DisarmProjectionVisualGate();
        ClearProjectionPulseVisuals();
        HideProjectionDormantChannel();
        ProjectionPulseCanvas.BeginAnimation(OpacityProperty, null);
        ProjectionPulseCanvas.Opacity = 0d;
    }

    private void LogProjectionCancellationIfNeeded(string reason)
    {
        if (projectionRequestState is ProjectionRequestState.None
            or ProjectionRequestState.Completed
            or ProjectionRequestState.TimedOut
            or ProjectionRequestState.Cancelled)
        {
            return;
        }

        LogProjectionDiagnostic(
            "ProjectionPulseCancelled",
            pendingProjectionPollingVersion,
            reason);
    }

    private void ClearProjectionPulseVisuals()
    {
        foreach (FrameworkElement segment in new FrameworkElement[]
                 {
                     ProjectionSourceHorizontalSegment,
                     ProjectionVerticalBridgeSegment,
                     ProjectionTargetHorizontalSegment
                 })
        {
            segment.BeginAnimation(OpacityProperty, null);
            ClearGeometry(segment);
            segment.Opacity = 0d;
        }

        ProjectionPulseHead.BeginAnimation(OpacityProperty, null);
        ClearTranslation(ProjectionPulseHead);
        ProjectionPulseHead.Opacity = 0d;
    }

    private static void PlayVerticalClip(
        FrameworkElement host,
        FrameworkElement content,
        TimeSpan duration)
    {
        double width = Math.Max(1d, content.ActualWidth);
        double height = Math.Max(1d, content.ActualHeight);
        RectangleGeometry clip = new();
        host.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, width, 0d),
            new Rect(0d, 0d, width, height),
            TimeSpan.Zero,
            duration);
    }

    private static void AnimateOpacity(
        UIElement target,
        TimeSpan delay,
        TimeSpan duration,
        double from = 0d,
        double to = 1d)
    {
        target.Opacity = to;
        target.BeginAnimation(
            OpacityProperty,
            BuildDoubleAnimation(from, to, delay, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateExitOpacity(
        UIElement target,
        TimeSpan delay,
        TimeSpan duration,
        double finalOpacity)
    {
        target.Opacity = finalOpacity;
        target.BeginAnimation(
            OpacityProperty,
            BuildDoubleAnimation(1d, finalOpacity, delay, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateTranslationX(
        UIElement target,
        double from,
        double to,
        TimeSpan delay,
        TimeSpan duration)
    {
        TranslateTransform transform = EnsureTranslateTransform(target);
        transform.X = to;
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            BuildDoubleAnimation(from, to, delay, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateTranslationY(
        UIElement target,
        double from,
        double to,
        TimeSpan delay,
        TimeSpan duration)
    {
        TranslateTransform transform = EnsureTranslateTransform(target);
        transform.Y = to;
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            BuildDoubleAnimation(from, to, delay, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames BuildDoubleAnimation(
        double from,
        double to,
        TimeSpan delay,
        TimeSpan duration)
    {
        DoubleAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(to, KeyTime.FromTimeSpan(delay + duration)));
        return animation;
    }

    private static void AnimateRectWithCommittedFinalState(
        RectangleGeometry geometry,
        Rect initial,
        Rect final,
        TimeSpan delay,
        TimeSpan duration)
    {
        geometry.Rect = initial;
        RectAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteRectKeyFrame(
            initial,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteRectKeyFrame(
            initial,
            KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new LinearRectKeyFrame(
            final,
            KeyTime.FromTimeSpan(delay + duration)));
        animation.Completed += (_, _) =>
        {
            geometry.Rect = final;
            geometry.BeginAnimation(RectangleGeometry.RectProperty, null);
        };
        geometry.BeginAnimation(
            RectangleGeometry.RectProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static TranslateTransform EnsureTranslateTransform(UIElement target)
    {
        if (target.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        target.RenderTransform = transform;
        return transform;
    }

    private static void SetTranslation(UIElement target, double x, double y)
    {
        TranslateTransform transform = EnsureTranslateTransform(target);
        transform.X = x;
        transform.Y = y;
    }

    private static void ClearTranslation(UIElement target)
    {
        if (target.RenderTransform is not TranslateTransform transform)
        {
            target.RenderTransform = new TranslateTransform();
            return;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0d;
        transform.Y = 0d;
    }

    private static void ClearAnimation(UIElement target, DependencyProperty property) =>
        target.BeginAnimation(property, null);

    private static void ClearGeometry(FrameworkElement target)
    {
        if (target.Clip is RectangleGeometry clip)
        {
            clip.BeginAnimation(RectangleGeometry.RectProperty, null);
        }

        target.Clip = null;
    }

    internal readonly record struct ProjectionRoute(
        Point Source,
        Point Target,
        double CorridorX,
        double SourceHorizontalLength,
        double VerticalBridgeLength,
        double TargetHorizontalLength,
        double TotalRouteLength,
        bool UsesThreeSegments);

    private enum ProjectionRouteResolution
    {
        Success,
        Invalid,
        LayoutPending
    }

    private readonly record struct ProjectionPulseTiming(
        TimeSpan SourceDuration,
        TimeSpan VerticalDuration,
        TimeSpan TargetDuration,
        TimeSpan VerticalStart,
        TimeSpan TargetStart,
        TimeSpan BuildDuration,
        TimeSpan HoldDuration,
        TimeSpan FadeDuration)
    {
        public static ProjectionPulseTiming Create(
            ProjectionRoute route,
            MotionLevel level)
        {
            double routeDurationMs = ResolveProjectionRouteDurationMilliseconds(
                route.TotalRouteLength,
                level);
            TimeSpan hold = TimeSpan.FromMilliseconds(
                level == MotionLevel.Full ? 50d : 30d);
            TimeSpan fade = TimeSpan.FromMilliseconds(
                level == MotionLevel.Full ? 90d : 70d);
            if (!route.UsesThreeSegments)
            {
                TimeSpan duration = TimeSpan.FromMilliseconds(routeDurationMs);
                return new(
                    duration,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    duration,
                    duration,
                    duration,
                    hold,
                    fade);
            }

            double sourceMinimum = level == MotionLevel.Full ? 80d : 60d;
            double verticalMinimum = level == MotionLevel.Full ? 90d : 70d;
            double targetMinimum = level == MotionLevel.Full ? 120d : 90d;
            double distributable = Math.Max(
                0d,
                routeDurationMs + 30d
                    - sourceMinimum
                    - verticalMinimum
                    - targetMinimum);
            double sourceMs = sourceMinimum
                + (distributable * route.SourceHorizontalLength / route.TotalRouteLength);
            double verticalMs = verticalMinimum
                + (distributable * route.VerticalBridgeLength / route.TotalRouteLength);
            double targetMs = targetMinimum
                + (distributable * route.TargetHorizontalLength / route.TotalRouteLength);
            TimeSpan source = TimeSpan.FromMilliseconds(sourceMs);
            TimeSpan verticalStart = source - TimeSpan.FromMilliseconds(15);
            TimeSpan vertical = TimeSpan.FromMilliseconds(verticalMs);
            TimeSpan targetStart =
                verticalStart + vertical - TimeSpan.FromMilliseconds(15);
            TimeSpan target = TimeSpan.FromMilliseconds(targetMs);
            TimeSpan build = targetStart + target;
            return new(
                source,
                vertical,
                target,
                verticalStart,
                targetStart,
                build,
                hold,
                fade);
        }
    }

    internal static double ResolveProjectionRouteDurationMilliseconds(
        double totalRouteLength,
        MotionLevel level)
    {
        double speed = level == MotionLevel.Full ? 600d : 800d;
        double minimum = level == MotionLevel.Full ? 360d : 260d;
        double maximum = level == MotionLevel.Full ? 520d : 380d;
        return Math.Clamp((totalRouteLength / speed) * 1000d, minimum, maximum);
    }

    internal enum ProjectionRequestState
    {
        None,
        Latched,
        WaitingForDataLayout,
        WaitingForAnchorLayout,
        GeometryReady,
        WaitingForComposition,
        Playing,
        HoldingVisible,
        WaitingForVisibleFrame,
        VisibleFrameCommitted,
        Completed,
        TimedOut,
        Cancelled
    }

    private sealed class MilestoneRowPresentation(
        StartupMilestoneSnapshot snapshot) : INotifyPropertyChanged
    {
        private StartupMilestoneSnapshot current = snapshot;

        public StartupMilestoneId Id => current.Id;
        public string Name => current.Name;
        public StartupMilestoneState State => current.State;
        public string StatusText => current.StatusText;
        public string Detail => current.Detail;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Update(StartupMilestoneSnapshot next)
        {
            if (current == next)
            {
                return;
            }

            StartupMilestoneSnapshot previous = current;
            current = next;
            if (previous.Name != next.Name)
            {
                PropertyChanged?.Invoke(this, new(nameof(Name)));
            }
            if (previous.State != next.State)
            {
                PropertyChanged?.Invoke(this, new(nameof(State)));
            }
            if (previous.StatusText != next.StatusText)
            {
                PropertyChanged?.Invoke(this, new(nameof(StatusText)));
            }
            if (previous.Detail != next.Detail)
            {
                PropertyChanged?.Invoke(this, new(nameof(Detail)));
            }
        }
    }
}
