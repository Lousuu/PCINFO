using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using Color = System.Windows.Media.Color;
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
    private bool commitExitPlayed;
    private bool commitPendingForProjection;
    private bool environmentLedgerPlayed;
    private bool identityLedgerPlayed;
    private bool indexPlayed;
    private bool indexPrepared;
    private bool projectionLedgerPlayed;
    private bool projectionLedgerReady;
    private bool projectionPulseActive;
    private bool projectionPulsePending;
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
    private long projectionRetryPollingVersion = -1;
    private int projectionRetryResolvedCount = -1;
    private long projectionValueGeneration;
    private StartupSequencePhase? lastAnimatedBottomPhase;
    private StartupSequenceSnapshot? previousSnapshot;
    private string currentProjectionText = string.Empty;
    private string previousProjectionText = string.Empty;

    internal bool IsProjectionLedgerReady => projectionLedgerReady;
    internal bool IsProjectionPulseActive => projectionPulseActive;
    internal bool IsProjectionPulsePending => projectionPulsePending;
    internal bool IsBottomRailReady => bottomRailReady;
    internal bool IsProjectionValueTransitionActive => projectionValueTransitionActive;
    internal bool IsProjectionValueTransitionPending => projectionValueTransitionPending;
    internal bool IsRevealVisualStateEntered => revealVisualStateEntered;
    internal bool IsCommitPendingForProjection => commitPendingForProjection;
    internal int DisplayedProjectionResolvedCount => displayedProjectionResolvedCount;
    internal int PendingBottomPhaseCount => pendingBottomPhases.Count;
    internal long ProjectionPulseGeneration => projectionPulseGeneration;
    internal long ProjectionValueGeneration => projectionValueGeneration;
    internal int LatestPendingResolvedCount => latestPendingResolvedCount;
    internal int LastPresentedResolvedCount => lastPresentedResolvedCount;
    internal ProjectionRoute? LastProjectionRoute => lastProjectionRoute;

    public TraceworkStartupSequenceOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ConfigureMilestoneRows();
            PrepareRowsIfNeeded();
        };
        RouteMatrixItems.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (RouteMatrixItems.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                ConfigureMilestoneRows();
                PrepareRowsIfNeeded();
            }
        };
        SizeChanged += (_, _) =>
        {
            ApplyResponsiveMargins(ActualWidth);
        };
        Unloaded += (_, _) => RestoreFinalState();
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
        CommitGroup.Opacity = 0d;
        CommitGroup.Visibility = Visibility.Collapsed;
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
        CommitLock.Opacity = 0d;
        CommitText.Opacity = 1d;
        CommitGroup.Opacity = 0d;
        CommitGroup.Visibility = Visibility.Collapsed;
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
        RouteMatrixItems.UpdateLayout();
        ConfigureMilestoneRows();
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
            UpdateBottomRail(snapshot);
            EnterRevealVisualState(snapshot);
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

        bool enteringRoute = snapshot.Phase == StartupSequencePhase.Route && !routePlayed;
        if (!enteringRoute
            && (routePlayed || PhaseIndex(snapshot.Phase) >= PhaseIndex(StartupSequencePhase.Bind)))
        {
            ApplyMilestoneTransitions(prior, snapshot);
        }

        if (snapshot.Phase == StartupSequencePhase.Lock)
        {
            projectionPulsePending = false;
            FinalizeProjectionValues(snapshot);
        }
        else
        {
            ApplyProjectionTransition(snapshot);
        }
        ApplyCommitState(prior, snapshot);

        if (snapshot.Phase == StartupSequencePhase.Index && !indexPlayed)
        {
            indexPlayed = true;
            PlayIndexReveal(snapshot.MotionLevel);
        }
        else if (enteringRoute)
        {
            routePlayed = true;
            PlayRoute(snapshot);
        }

    }

    private void EnterRevealVisualState(StartupSequenceSnapshot snapshot)
    {
        if (revealVisualStateEntered)
        {
            return;
        }

        FinalizeProjectionValues(snapshot);
        StopProjectionPulseForReveal();
        pendingBottomPhases.Clear();
        bottomPhaseTransitionActive = false;
        if (commitPlayed || CommitGroup.Visibility == Visibility.Visible)
        {
            commitExitPlayed = true;
            PlayCommitExit();
        }
        PlayConcurrentExit(snapshot.MotionLevel);
        revealVisualStateEntered = true;
    }

    private void ApplyCommitState(
        StartupSequenceSnapshot? prior,
        StartupSequenceSnapshot snapshot)
    {
        bool canShowCommit = snapshot.Phase == StartupSequencePhase.Lock && snapshot.CanCommit;
        bool isLeavingCommit = prior is { Phase: StartupSequencePhase.Lock, CanCommit: true }
            && snapshot.Phase == StartupSequencePhase.Reveal;
        bool deferForProjection = canShowCommit && projectionPulseActive;
        commitPendingForProjection = deferForProjection;
        CommitGroup.Visibility = canShowCommit && !deferForProjection || isLeavingCommit
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!canShowCommit || deferForProjection && !isLeavingCommit)
        {
            CommitGroup.Opacity = 0d;
            CommitLock.Opacity = 0d;
        }

        if (canShowCommit && !deferForProjection && !commitPlayed)
        {
            commitPlayed = true;
            PlayCommit(snapshot.MotionLevel);
        }
        else if (isLeavingCommit && !commitExitPlayed)
        {
            commitExitPlayed = true;
            PlayCommitExit();
        }
    }

    private void ResetPlaybackState(StartupSequenceSnapshot snapshot)
    {
        ClearChoreographyClocks();
        indexPrepared = false;
        preparedIndexVersion = -1;
        indexPlayed = false;
        routePlayed = false;
        identityLedgerPlayed = false;
        environmentLedgerPlayed = false;
        projectionLedgerPlayed = false;
        commitPlayed = false;
        commitExitPlayed = false;
        revealVisualStateEntered = false;
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
        projectionPulsePending = false;
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
        for (int index = 0; index < rows.Length; index++)
        {
            bool isProjectionSource = Snapshot?.Milestones.ElementAtOrDefault(index)?.Id
                == StartupMilestoneId.SensorBus;
            rows[index].ConfigureSegments(
                index == 0,
                index == rows.Length - 1,
                isProjectionSource);
        }
    }

    private void ApplyProjectionPortState(StartupSequencePhase phase)
    {
        if (phase is StartupSequencePhase.Dormant
            or StartupSequencePhase.Index
            or StartupSequencePhase.Route
            || phase == StartupSequencePhase.Bind && !projectionLedgerReady)
        {
            ProjectionInputPort.Opacity = 0d;
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
                        Phase: StartupSequencePhase.Bind
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
        if (!projectionPulsePending
            || Snapshot is not { Phase: StartupSequencePhase.Bind } snapshot)
        {
            return;
        }

        projectionPulsePending = false;
        StartProjectionPulse(
            snapshot.MotionLevel,
            latestPendingResolvedCount,
            snapshot.InitialProjection.PollingVersion,
            allowLayoutRetry: true);
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
        RequestProjectionPulse(snapshot.MotionLevel, current, pollingVersion);
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
        projectionPulseGeneration++;
        projectionPulseActive = false;
        projectionPulsePending = false;
        projectionRetryScheduled = false;
        ClearProjectionPulseVisuals();
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

        bool pollingVersionAdvanced =
            snapshot.InitialProjection.PollingVersion > displayedProjectionPollingVersion;
        if (pollingVersionAdvanced && projectionPulseActive)
        {
            projectionPulseGeneration++;
            projectionPulseActive = false;
            projectionPulsePending = false;
            projectionRetryScheduled = false;
            ClearProjectionPulseVisuals();
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

    private void PlayIndexReveal(MotionLevel level)
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

        double width = Math.Max(1d, SystemIndexText.ActualWidth);
        double height = Math.Max(1d, SystemIndexText.ActualHeight);
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

        StartupBottomRailLayer.UpdateLayout();
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
        SystemIndexText.Opacity = 1d;
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
        StartupSequenceSnapshot presentationSnapshot = Snapshot with { Phase = phase };
        UpdateReadyBottomRail(presentationSnapshot);
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
            BottomPhaseText.BeginAnimation(OpacityProperty, null);
            bottomPhaseTransitionActive = false;
            PlayNextBottomPhase();
        };
        BottomPhaseText.BeginAnimation(
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

    private void UpdateReadyBottomRail(StartupSequenceSnapshot snapshot)
    {
        StartupPhasePresentation? presentation = StartupPhasePresentation.Create(
            snapshot.Phase,
            snapshot.FailureMessage);
        if (presentation is null)
        {
            BottomRailContent.Opacity = 0d;
            return;
        }

        BottomRailContent.Opacity = 1d;
        string priorText = BottomCurrentPhaseText.Text;
        BottomPreviousPhaseText.Text = priorText;
        BottomCurrentPhaseText.Text = presentation.DisplayText;
        BottomPhaseText.Text = presentation.DisplayText;
        BottomPhaseCode.Text = presentation.PhaseCode;
        ApplyFailureBrushes(presentation.IsFailure);

        bool movesForward = lastAnimatedBottomPhase is null
            || PhaseIndex(snapshot.Phase) > PhaseIndex(lastAnimatedBottomPhase.Value);
        StartupSequencePhase? previousPhase = lastAnimatedBottomPhase;
        Color? previousSegmentColor = previousPhase.HasValue
            && PhaseIndex(previousPhase.Value) >= 0
            && PhaseSegments()[PhaseIndex(previousPhase.Value)].Background is SolidColorBrush previousBrush
                ? previousBrush.Color
                : null;
        ApplyPhaseSegmentStates(presentation);
        if (!movesForward)
        {
            return;
        }

        lastAnimatedBottomPhase = snapshot.Phase;
        PlayBottomPhaseTransition(
            snapshot.MotionLevel,
            previousPhase,
            previousSegmentColor,
            snapshot.Phase);
    }

    private void PlayBottomPhaseTransition(
        MotionLevel level,
        StartupSequencePhase? previousPhase,
        Color? previousSegmentColor,
        StartupSequencePhase currentPhase)
    {
        ClearBottomTextClocks();
        if (level == MotionLevel.Off)
        {
            return;
        }

        if (level == MotionLevel.Reduced)
        {
            AnimateOpacity(BottomPreviousPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 1d, 0d);
            AnimateOpacity(BottomCurrentPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateOpacity(BottomPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(80), 0.3d, 1d);
            return;
        }

        if (level == MotionLevel.Full)
        {
            AnimateOpacity(BottomPreviousPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(60), 1d, 0d);
            AnimateTranslationY(BottomPreviousPhaseText, 0d, -8d, TimeSpan.Zero, TimeSpan.FromMilliseconds(60));
            AnimateOpacity(BottomCurrentPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateTranslationY(BottomCurrentPhaseText, 8d, 0d, TimeSpan.Zero, TimeSpan.FromMilliseconds(90));
            AnimateOpacity(BottomPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(80), 0.3d, 1d);
            AnimateTranslationX(BottomPhaseCode, 4d, 0d, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));
        }
        else
        {
            PlayVerticalClip(BottomPhaseTextClipHost, BottomCurrentPhaseText, TimeSpan.FromMilliseconds(90));
            AnimateOpacity(BottomPreviousPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 1d, 0d);
            AnimateOpacity(BottomCurrentPhaseText, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 1d);
            AnimateOpacity(BottomPhaseCode, TimeSpan.Zero, TimeSpan.FromMilliseconds(80), 0.3d, 1d);
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

    private void ApplyFailureBrushes(bool isFailure)
    {
        if (isFailure)
        {
            BottomCurrentPhaseText.SetResourceReference(TextBlock.ForegroundProperty, "CriticalBrush");
            BottomPhaseCode.SetResourceReference(TextBlock.ForegroundProperty, "CriticalBrush");
        }
        else
        {
            BottomCurrentPhaseText.ClearValue(TextBlock.ForegroundProperty);
            BottomPhaseCode.ClearValue(TextBlock.ForegroundProperty);
        }
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

    private void RequestProjectionPulse(
        MotionLevel level,
        int resolvedCount,
        long pollingVersion)
    {
        latestPendingResolvedCount = resolvedCount;
        pendingProjectionPollingVersion = pollingVersion;
        if (level is MotionLevel.Reduced or MotionLevel.Off
            || Snapshot is not { Phase: StartupSequencePhase.Bind })
        {
            return;
        }

        if (!projectionLedgerReady || projectionPulseActive)
        {
            projectionPulsePending = true;
            return;
        }

        StartProjectionPulse(level, resolvedCount, pollingVersion, allowLayoutRetry: true);
    }

    private void StartProjectionPulse(
        MotionLevel level,
        int resolvedCount,
        long pollingVersion,
        bool allowLayoutRetry)
    {
        if (level is MotionLevel.Reduced or MotionLevel.Off
            || Snapshot is not { IsActive: true, HasCompleted: false, Phase: StartupSequencePhase.Bind }
            || !projectionLedgerReady)
        {
            return;
        }

        ProjectionRouteResolution resolution =
            TryResolveProjectionRoute(out ProjectionRoute route);
        if (resolution == ProjectionRouteResolution.LayoutPending
            && allowLayoutRetry)
        {
            QueueProjectionRouteRetry(level, resolvedCount, pollingVersion);
            return;
        }
        if (resolution != ProjectionRouteResolution.Success)
        {
            projectionPulsePending = false;
            return;
        }

        projectionRetryScheduled = false;
        projectionPulsePending = false;
        latestPendingResolvedCount = resolvedCount;
        projectionPulseActive = true;
        long generation = ++projectionPulseGeneration;
        lastProjectionRoute = route;
        ProjectionPulseTiming timing = ProjectionPulseTiming.Create(route, level);
        ConfigureProjectionSegments(route);
        AnimateProjectionSegments(route, timing);
        AnimateProjectionCanvas(timing, generation);
        if (level == MotionLevel.Full)
        {
            AnimatePulseHead(route, timing);
        }
    }

    private void QueueProjectionRouteRetry(
        MotionLevel level,
        int resolvedCount,
        long pollingVersion)
    {
        if (projectionRetryScheduled
            && projectionRetryPollingVersion == pollingVersion
            && projectionRetryResolvedCount == resolvedCount)
        {
            return;
        }

        projectionPulsePending = true;
        projectionRetryScheduled = true;
        projectionRetryPollingVersion = pollingVersion;
        projectionRetryResolvedCount = resolvedCount;
        long generation = projectionPulseGeneration;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                projectionRetryScheduled = false;
                if (generation != projectionPulseGeneration
                    || revealVisualStateEntered
                    || Snapshot is not
                    {
                        IsActive: true,
                        HasCompleted: false,
                        Phase: StartupSequencePhase.Bind
                    } snapshot
                    || snapshot.InitialProjection.PollingVersion != pollingVersion
                    || latestPendingResolvedCount != resolvedCount)
                {
                    return;
                }

                StartProjectionPulse(
                    level,
                    resolvedCount,
                    pollingVersion,
                    allowLayoutRetry: false);
            }));
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
            || !rows[sensorIndex].IsLoaded
            || !ProjectionInputAnchor.IsLoaded)
        {
            return ProjectionRouteResolution.LayoutPending;
        }

        FrameworkElement sourceAnchor = rows[sensorIndex].RouteOutputAnchorElement;
        if (sourceAnchor.ActualWidth <= 0d
            || sourceAnchor.ActualHeight <= 0d
            || ProjectionInputAnchor.ActualWidth <= 0d
            || ProjectionInputAnchor.ActualHeight <= 0d)
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
        double corridorX = Math.Clamp(
            source.X + (horizontalDistance * 0.5d),
            source.X + minimumSegmentLength,
            target.X - minimumSegmentLength);
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

    private void ConfigureProjectionSegments(ProjectionRoute route)
    {
        ProjectionPulseCanvas.Width = Math.Max(1d, OverlayRoot.ActualWidth);
        ProjectionPulseCanvas.Height = Math.Max(1d, OverlayRoot.ActualHeight);
        ProjectionPulseCanvas.Opacity = 1d;

        ConfigureHorizontalSegment(
            ProjectionSourceHorizontalSegment,
            route.Source.X,
            route.Source.Y,
            route.SourceHorizontalLength);
        if (!route.UsesThreeSegments)
        {
            HideProjectionSegment(ProjectionVerticalBridgeSegment);
            HideProjectionSegment(ProjectionTargetHorizontalSegment);
        }
        else
        {
            ProjectionVerticalBridgeSegment.Visibility = Visibility.Visible;
            ProjectionVerticalBridgeSegment.Opacity = 1d;
            ProjectionVerticalBridgeSegment.Height =
                AlignToHalfDip(route.VerticalBridgeLength);
            Canvas.SetLeft(
                ProjectionVerticalBridgeSegment,
                AlignToHalfDip(route.CorridorX - 0.5d));
            Canvas.SetTop(
                ProjectionVerticalBridgeSegment,
                AlignToHalfDip(Math.Min(route.Source.Y, route.Target.Y)));
            ConfigureHorizontalSegment(
                ProjectionTargetHorizontalSegment,
                route.CorridorX,
                route.Target.Y,
                route.TargetHorizontalLength);
        }

        ProjectionPulseHead.Opacity = 0d;
        Canvas.SetLeft(
            ProjectionPulseHead,
            AlignToHalfDip(route.Source.X - 2.5d));
        Canvas.SetTop(
            ProjectionPulseHead,
            AlignToHalfDip(route.Source.Y - 2.5d));
    }

    private static void ConfigureHorizontalSegment(
        FrameworkElement segment,
        double left,
        double centerY,
        double length)
    {
        segment.Visibility = Visibility.Visible;
        segment.Opacity = 1d;
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
        RectangleGeometry clip = new(new Rect(0d, 0d, 0d, 1d));
        segment.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, 0d, 1d),
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
        Rect initial = topToBottom
            ? new Rect(0d, 0d, 1d, 0d)
            : new Rect(0d, length, 1d, 0d);
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
        opacity.Completed += (_, _) => OnProjectionPulseCompleted(generation);
        ProjectionPulseCanvas.Opacity = 0d;
        ProjectionPulseCanvas.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);
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

        ClearProjectionPulseVisuals();
        projectionPulseActive = false;
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
            if (!commitPlayed)
            {
                commitPlayed = true;
                PlayCommit(lockSnapshot.MotionLevel);
            }
            return;
        }

        if (!projectionPulsePending
            || !IsLoaded
            || Snapshot is not { IsActive: true, HasCompleted: false, Phase: StartupSequencePhase.Bind } snapshot)
        {
            projectionPulsePending = false;
            return;
        }

        projectionPulsePending = false;
        StartProjectionPulse(
            snapshot.MotionLevel,
            latestPendingResolvedCount,
            snapshot.InitialProjection.PollingVersion,
            allowLayoutRetry: true);
    }

    private void PlayConcurrentExit(MotionLevel level)
    {
        if (level == MotionLevel.Reduced)
        {
            AnimateExitOpacity(StartupContentLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(150), 0d);
            AnimateExitOpacity(StartupBottomRailLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(150), 0d);
            AnimateExitOpacity(StartupBackgroundLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(150), 0d);
            return;
        }

        if (level == MotionLevel.Standard)
        {
            AnimateExitOpacity(StartupContentLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0.15d);
            AnimateExitOpacity(StartupBottomRailLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(120), 0d);
            AnimateExitOpacity(StartupBackgroundLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(200), 0d);
            return;
        }

        AnimateExitOpacity(StartupContentLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(110), 0.15d);
        AnimateExitOpacity(StartupBottomRailLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(120), 0d);
        AnimateExitOpacity(
            StartupBackgroundLayer,
            TimeSpan.FromMilliseconds(35),
            TimeSpan.FromMilliseconds(225),
            0d);

        TranslateTransform transform = EnsureTranslateTransform(StartupContentLayer);
        transform.X = 0d;
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            BuildDoubleAnimation(0d, -8d, TimeSpan.Zero, TimeSpan.FromMilliseconds(110)),
            HandoffBehavior.SnapshotAndReplace);

        double width = Math.Max(1d, StartupContentLayer.ActualWidth);
        double height = Math.Max(1d, StartupContentLayer.ActualHeight);
        RectangleGeometry clip = new();
        StartupContentLayer.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, width, height),
            new Rect(0d, 0d, width * 0.75d, height),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(110));
    }

    private void PlayCommit(MotionLevel level)
    {
        CommitGroup.Visibility = Visibility.Visible;
        if (level == MotionLevel.Reduced)
        {
            CommitGroup.Opacity = 0.35d;
            AnimateOpacity(CommitGroup, TimeSpan.Zero, TimeSpan.FromMilliseconds(90), 0d, 0.35d);
            return;
        }

        DoubleAnimationUsingKeyFrames lockOpacity = new() { FillBehavior = FillBehavior.Stop };
        lockOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        lockOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        lockOpacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.35d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        CommitGroup.Opacity = 0.35d;
        CommitGroup.BeginAnimation(OpacityProperty, lockOpacity, HandoffBehavior.SnapshotAndReplace);
        CommitLock.Opacity = 0.35d;
        CommitLock.BeginAnimation(OpacityProperty, lockOpacity, HandoffBehavior.SnapshotAndReplace);

        RectangleGeometry centerClip = new();
        CommitCenterClipHost.Clip = centerClip;
        AnimateRectWithCommittedFinalState(
            centerClip,
            new Rect(0d, 0d, 0d, 6d),
            new Rect(0d, 0d, 6d, 6d),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(90));
        AnimateOpacity(
            CommitText,
            TimeSpan.FromMilliseconds(45),
            TimeSpan.FromMilliseconds(90));
    }

    private void PlayCommitExit()
    {
        CommitGroup.Visibility = Visibility.Visible;
        CommitGroup.Opacity = 0d;
        DoubleAnimation exit = new(1d, 0d, TimeSpan.FromMilliseconds(90))
        {
            FillBehavior = FillBehavior.Stop
        };
        exit.Completed += (_, _) =>
        {
            CommitGroup.BeginAnimation(OpacityProperty, null);
            CommitGroup.Opacity = 0d;
            if (Snapshot?.Phase != StartupSequencePhase.Lock)
            {
                CommitGroup.Visibility = Visibility.Collapsed;
            }
        };
        CommitGroup.BeginAnimation(OpacityProperty, exit, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyResponsiveMargins(double width)
    {
        StartupContentLayer.Margin = ResolveContentMargin(width);
        StartupBottomRailLayer.Margin = ResolveBottomRailMargin(width);
    }

    private StartupMilestoneRow[] GetMilestoneRows()
    {
        List<StartupMilestoneRow> rows = [];
        for (int index = 0; index < RouteMatrixItems.Items.Count; index++)
        {
            if (RouteMatrixItems.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container
                && FindDescendant<StartupMilestoneRow>(container) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows.ToArray();
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
        foreach (UIElement element in new UIElement[]
                 {
                     this, StartupBackgroundLayer, StartupContentLayer, StartupBottomRailLayer,
                     BottomRailContent, SystemIndexText, TraceworkTitleText, StartupSubtitleText,
                     StartupTitleGroup, SystemRouteLabel, RouteMatrixItems, LedgerIdentityGroup,
                     LedgerEnvironmentGroup, LedgerProjectionGroup, ProjectionPulseCanvas,
                     ProjectionSourceHorizontalSegment, ProjectionVerticalBridgeSegment,
                     ProjectionTargetHorizontalSegment, ProjectionPulseHead,
                     CommitGroup, CommitLock, CommitText
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
                     BottomPreviousPhaseText, BottomCurrentPhaseText, BottomPhaseCode
                 })
        {
            element.BeginAnimation(OpacityProperty, null);
            ClearTranslation(element);
        }

        BottomPhaseText.BeginAnimation(OpacityProperty, null);
        ClearGeometry(BottomPhaseTextClipHost);
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
        BottomPhaseCode.Opacity = 1d;
        SetTranslation(BottomPreviousPhaseText, 0d, 0d);
        SetTranslation(BottomCurrentPhaseText, 0d, 0d);
        SetTranslation(BottomPhaseCode, 0d, 0d);
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
        projectionPulseGeneration++;
        projectionPulseActive = false;
        projectionPulsePending = false;
        projectionRetryScheduled = false;
        commitPendingForProjection = false;
        projectionLedgerReady = false;
        lastProjectionRoute = null;
        ClearProjectionPulseVisuals();
    }

    private void StopProjectionPulseForReveal()
    {
        projectionPulseGeneration++;
        projectionPulsePending = false;
        projectionPulseActive = false;
        projectionRetryScheduled = false;
        commitPendingForProjection = false;
        lastProjectionRoute = null;
        ClearProjectionPulseVisuals();
    }

    private void ClearProjectionPulseVisuals()
    {
        ProjectionPulseCanvas.BeginAnimation(OpacityProperty, null);
        ProjectionPulseCanvas.Opacity = 0d;
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
}
