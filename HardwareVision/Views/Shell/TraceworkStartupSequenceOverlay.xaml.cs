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

    private bool commitPlayed;
    private bool commitExitPlayed;
    private bool environmentLedgerPlayed;
    private bool identityLedgerPlayed;
    private bool indexPlayed;
    private bool indexPrepared;
    private bool projectionLedgerPlayed;
    private bool revealExitPlayed;
    private bool routePlayed;
    private int lastProjectionResolvedCount = -1;
    private long latestVersion = -1;
    private long preparedIndexVersion = -1;
    private MotionLevel preparedMotionLevel = MotionLevel.Full;
    private Point? projectionPulseSource;
    private Point? projectionPulseTarget;
    private StartupSequencePhase? lastAnimatedBottomPhase;
    private StartupSequenceSnapshot? previousSnapshot;
    private string currentProjectionText = string.Empty;
    private string previousProjectionText = string.Empty;

    public TraceworkStartupSequenceOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ConfigureMilestoneRows();
            PrepareRowsIfNeeded();
            UpdateProjectionAnchorCache();
        };
        RouteMatrixItems.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (RouteMatrixItems.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                ConfigureMilestoneRows();
                PrepareRowsIfNeeded();
                UpdateProjectionAnchorCache();
            }
        };
        SizeChanged += (_, _) =>
        {
            ApplyResponsiveMargins(ActualWidth);
            UpdateProjectionAnchorCache();
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

        StartupContentLayer.Opacity = 1d;
        SystemIndexText.Opacity = 1d;
        TraceworkTitleText.Opacity = 0d;
        StartupSubtitleText.Opacity = 0d;
        SystemRouteLabel.Opacity = 0d;
        LedgerIdentityGroup.Opacity = 0d;
        LedgerEnvironmentGroup.Opacity = 0d;
        LedgerProjectionGroup.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
        BottomRailContent.Opacity = 1d;
        CommitGroup.Opacity = 0d;
        CommitGroup.Visibility = Visibility.Collapsed;
        RouteMatrixItems.Opacity = level == MotionLevel.Reduced ? 0d : 1d;

        SetTranslation(TraceworkTitleText, level == MotionLevel.Full ? 4d : 0d, 0d);
        SetTranslation(StartupSubtitleText, level == MotionLevel.Full ? 4d : 0d, 0d);
        SetTranslation(LedgerEnvironmentGroup, level == MotionLevel.Full ? 5d : 0d, 0d);
        SetTranslation(LedgerProjectionGroup, level == MotionLevel.Full ? 5d : 0d, 0d);
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
        UpdateProjectionAnchorCache();
        OverlayRoot.DataContext = snapshot;
        RouteMatrixItems.UpdateLayout();
        ConfigureMilestoneRows();

        if (snapshot.HasCompleted
            || snapshot.CurrentTheme != AppTheme.Tracework
            || snapshot.MotionLevel == MotionLevel.Off)
        {
            currentProjectionText = FormatProjection(snapshot.InitialProjection);
            RestoreFinalState();
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
        StartupBackgroundLayer.Opacity = 1d;
        StartupContentLayer.Opacity = 1d;

        if (snapshot.Phase == StartupSequencePhase.Index && !indexPrepared)
        {
            PrepareIndexInitialState(snapshot.MotionLevel);
        }

        PrepareRowsIfNeeded();
        UpdateBottomRail(snapshot);
        ApplyLedgerPhase(snapshot);

        bool enteringRoute = snapshot.Phase == StartupSequencePhase.Route && !routePlayed;
        if (!enteringRoute
            && (routePlayed || PhaseIndex(snapshot.Phase) >= PhaseIndex(StartupSequencePhase.Bind)))
        {
            ApplyMilestoneTransitions(prior, snapshot);
        }

        ApplyProjectionTransition(snapshot);
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

        if (snapshot.Phase == StartupSequencePhase.Reveal && !revealExitPlayed)
        {
            revealExitPlayed = true;
            PlayConcurrentExit(snapshot.MotionLevel);
        }
    }

    private void ApplyCommitState(
        StartupSequenceSnapshot? prior,
        StartupSequenceSnapshot snapshot)
    {
        bool canShowCommit = snapshot.Phase == StartupSequencePhase.Lock && snapshot.CanCommit;
        bool isLeavingCommit = prior is { Phase: StartupSequencePhase.Lock, CanCommit: true }
            && snapshot.Phase == StartupSequencePhase.Reveal;
        CommitGroup.Visibility = canShowCommit || isLeavingCommit
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!canShowCommit && !isLeavingCommit)
        {
            CommitGroup.Opacity = 0d;
            CommitLock.Opacity = 0d;
        }

        if (canShowCommit && !commitPlayed)
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
        revealExitPlayed = false;
        lastAnimatedBottomPhase = null;
        lastProjectionResolvedCount = snapshot.InitialProjection.ResolvedVisibleSlotCount;
        currentProjectionText = FormatProjection(snapshot.InitialProjection);
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
            rows[index].ConfigureSegments(index == 0, index == rows.Length - 1);
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
            PlayLedgerGroup(
                LedgerProjectionGroup,
                snapshot.MotionLevel,
                snapshot.MotionLevel == MotionLevel.Full ? TimeSpan.FromMilliseconds(40) : TimeSpan.Zero);
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

    private void ApplyProjectionTransition(StartupSequenceSnapshot snapshot)
    {
        int current = snapshot.InitialProjection.ResolvedVisibleSlotCount;
        string nextText = FormatProjection(snapshot.InitialProjection);
        if (lastProjectionResolvedCount < 0)
        {
            lastProjectionResolvedCount = current;
            currentProjectionText = nextText;
            ProjectionCurrentValue.Text = currentProjectionText;
            return;
        }

        int previous = lastProjectionResolvedCount;
        lastProjectionResolvedCount = current;
        if (current <= previous)
        {
            if (currentProjectionText.Length == 0)
            {
                currentProjectionText = nextText;
                ProjectionCurrentValue.Text = currentProjectionText;
            }
            return;
        }

        previousProjectionText = currentProjectionText.Length == 0
            ? $"{previous} / {snapshot.InitialProjection.TotalVisibleSlotCount} RESOLVED"
            : currentProjectionText;
        currentProjectionText = nextText;
        ProjectionPreviousValue.Text = previousProjectionText;
        ProjectionCurrentValue.Text = currentProjectionText;

        if (PhaseIndex(snapshot.Phase) < PhaseIndex(StartupSequencePhase.Bind))
        {
            CleanupProjectionTransition();
            return;
        }

        PlayProjectionValueTransition(snapshot.MotionLevel);
        PlayProjectionPulse(snapshot.MotionLevel);
    }

    private void PlayProjectionValueTransition(MotionLevel level)
    {
        ClearProjectionValueClocks();
        if (level == MotionLevel.Off)
        {
            CleanupProjectionTransition();
            return;
        }

        ProjectionPreviousValue.Opacity = 0d;
        ProjectionCurrentValue.Opacity = 1d;
        TimeSpan duration = level == MotionLevel.Reduced
            ? TimeSpan.FromMilliseconds(90)
            : TimeSpan.FromMilliseconds(level == MotionLevel.Standard ? 90 : 100);
        AnimateOpacity(ProjectionPreviousValue, TimeSpan.Zero, duration, 1d, 0d);
        DoubleAnimationUsingKeyFrames currentOpacity = BuildDoubleAnimation(0d, 1d, TimeSpan.Zero, duration);
        currentOpacity.Completed += (_, _) =>
        {
            if (ProjectionCurrentValue.Text == currentProjectionText)
            {
                CleanupProjectionTransition();
            }
        };
        ProjectionCurrentValue.BeginAnimation(
            OpacityProperty,
            currentOpacity,
            HandoffBehavior.SnapshotAndReplace);

        if (level == MotionLevel.Full)
        {
            AnimateTranslationY(ProjectionPreviousValue, 0d, -10d, TimeSpan.Zero, duration);
            AnimateTranslationY(ProjectionCurrentValue, 10d, 0d, TimeSpan.Zero, duration);
        }
        else if (level == MotionLevel.Standard)
        {
            PlayVerticalClip(ProjectionValueClipHost, ProjectionCurrentValue, duration);
        }
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

        int interval = level == MotionLevel.Full ? 45 : 28;
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
            AnimateOpacity(StartupTitleGroup, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));
            AnimateOpacity(LedgerIdentityGroup, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            AnimateOpacity(StartupBottomRailLayer, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            AnimateOpacity(SystemRouteLabel, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            return;
        }

        double width = Math.Max(1d, SystemIndexText.ActualWidth);
        double height = Math.Max(1d, SystemIndexText.ActualHeight);
        RectangleGeometry clip = new(new Rect(0d, 0d, width, height));
        SystemIndexClipHost.Clip = clip;
        TimeSpan indexDuration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(180)
            : TimeSpan.FromMilliseconds(120);
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, 0d, height),
                new Rect(0d, 0d, width, height),
                indexDuration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);

        if (level == MotionLevel.Full)
        {
            AnimateOpacity(TraceworkTitleText, TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(100));
            AnimateTranslationX(TraceworkTitleText, 4d, 0d, TimeSpan.FromMilliseconds(35), TimeSpan.FromMilliseconds(100));
            AnimateOpacity(StartupSubtitleText, TimeSpan.FromMilliseconds(75), TimeSpan.FromMilliseconds(90));
            AnimateTranslationX(StartupSubtitleText, 4d, 0d, TimeSpan.FromMilliseconds(75), TimeSpan.FromMilliseconds(90));
            AnimateOpacity(LedgerIdentityGroup, TimeSpan.FromMilliseconds(110), TimeSpan.FromMilliseconds(90));
            PlayBottomRailEntry(level, TimeSpan.FromMilliseconds(140), TimeSpan.FromMilliseconds(100));
            AnimateOpacity(SystemRouteLabel, TimeSpan.FromMilliseconds(160), TimeSpan.FromMilliseconds(80));
            return;
        }

        AnimateOpacity(TraceworkTitleText, TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(90));
        AnimateOpacity(StartupSubtitleText, TimeSpan.FromMilliseconds(55), TimeSpan.FromMilliseconds(80));
        AnimateOpacity(LedgerIdentityGroup, TimeSpan.FromMilliseconds(85), TimeSpan.FromMilliseconds(80));
        PlayBottomRailEntry(level, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(80));
        AnimateOpacity(SystemRouteLabel, TimeSpan.FromMilliseconds(110), TimeSpan.FromMilliseconds(70));
    }

    private void PlayBottomRailEntry(MotionLevel level, TimeSpan delay, TimeSpan duration)
    {
        AnimateOpacity(StartupBottomRailLayer, delay, duration);
        if (level == MotionLevel.Reduced || level == MotionLevel.Off)
        {
            return;
        }

        double width = Math.Max(1d, StartupBottomRailLayer.ActualWidth);
        double height = Math.Max(1d, StartupBottomRailLayer.ActualHeight);
        RectangleGeometry clip = new(new Rect(0d, 0d, width, height));
        StartupBottomRailLayer.Clip = clip;
        TimeSpan clipDuration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(180)
            : TimeSpan.FromMilliseconds(120);
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, 0d, height),
                new Rect(0d, 0d, width, height),
                clipDuration)
            {
                BeginTime = delay,
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void SetIndexFinalState()
    {
        SystemIndexText.Opacity = 1d;
        TraceworkTitleText.Opacity = 1d;
        StartupSubtitleText.Opacity = 1d;
        SystemRouteLabel.Opacity = 1d;
        LedgerIdentityGroup.Opacity = 1d;
        StartupBottomRailLayer.Opacity = 1d;
        SetTranslation(TraceworkTitleText, 0d, 0d);
        SetTranslation(StartupSubtitleText, 0d, 0d);
    }

    private void UpdateBottomRail(StartupSequenceSnapshot snapshot)
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
        RectangleGeometry clip = new(new Rect(0d, 0d, width, 1d));
        segment.Clip = clip;
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, 0d, 1d),
                new Rect(0d, 0d, width, 1d),
                duration)
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
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

    private void PlayProjectionPulse(MotionLevel level)
    {
        CleanupProjectionPulse();
        if (level is MotionLevel.Reduced or MotionLevel.Off)
        {
            return;
        }

        UpdateProjectionAnchorCache();
        if (projectionPulseSource is not Point source
            || projectionPulseTarget is not Point target)
        {
            return;
        }

        double trackWidth = target.X - source.X;
        if (trackWidth < 24d)
        {
            return;
        }

        ProjectionPulseCanvas.Width = Math.Max(1d, OverlayRoot.ActualWidth);
        ProjectionPulseCanvas.Height = Math.Max(1d, OverlayRoot.ActualHeight);
        PathGeometry geometry = BuildProjectionPulsePath(source, target);
        ProjectionPulseTrack.Data = geometry;
        ProjectionPulseCanvas.Opacity = 1d;
        ProjectionPulseTrack.Opacity = 0d;

        double minY = Math.Min(source.Y, target.Y) - 2d;
        double clipHeight = Math.Abs(target.Y - source.Y) + 4d;
        RectangleGeometry clip = new(new Rect(source.X, minY, trackWidth, clipHeight));
        ProjectionPulseTrack.Clip = clip;
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(source.X, minY, 0d, clipHeight),
                new Rect(source.X, minY, trackWidth, clipHeight),
                TimeSpan.FromMilliseconds(120))
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
        AnimatePulseTrackOpacity();

        if (level == MotionLevel.Standard)
        {
            return;
        }

        Canvas.SetLeft(ProjectionPulseHead, source.X - 8d);
        Canvas.SetTop(ProjectionPulseHead, source.Y - 0.5d);
        AnimatePulseHead(source, target);
    }

    private static PathGeometry BuildProjectionPulsePath(Point source, Point target)
    {
        PathFigure figure = new() { StartPoint = source, IsClosed = false, IsFilled = false };
        if (Math.Abs(target.Y - source.Y) <= 2d)
        {
            figure.Segments.Add(new LineSegment(target, true));
        }
        else
        {
            double elbowX = Math.Min(target.X, source.X + 24d);
            figure.Segments.Add(new LineSegment(new Point(elbowX, source.Y), true));
            figure.Segments.Add(new LineSegment(new Point(elbowX, target.Y), true));
            figure.Segments.Add(new LineSegment(target, true));
        }

        return new PathGeometry([figure]);
    }

    private void AnimatePulseTrackOpacity()
    {
        DoubleAnimationUsingKeyFrames opacity = new() { FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(30))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        ProjectionPulseTrack.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimatePulseHead(Point source, Point target)
    {
        ProjectionPulseHead.Opacity = 0d;
        TranslateTransform transform = EnsureTranslateTransform(ProjectionPulseHead);
        transform.X = 0d;
        transform.Y = 0d;
        DoubleAnimationUsingKeyFrames opacity = new() { FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.9d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(55))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        ProjectionPulseHead.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);

        double horizontal = target.X - source.X;
        double vertical = target.Y - source.Y;
        if (Math.Abs(vertical) <= 2d)
        {
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(0d, horizontal, TimeSpan.FromMilliseconds(180))
                {
                    FillBehavior = FillBehavior.Stop
                },
                HandoffBehavior.SnapshotAndReplace);
            return;
        }

        double first = Math.Min(24d, horizontal);
        double total = first + Math.Abs(vertical) + Math.Max(0d, horizontal - first);
        TimeSpan elbowTime = TimeSpan.FromMilliseconds(180d * first / total);
        TimeSpan verticalTime = TimeSpan.FromMilliseconds(180d * (first + Math.Abs(vertical)) / total);
        DoubleAnimationUsingKeyFrames x = new() { FillBehavior = FillBehavior.Stop };
        x.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        x.KeyFrames.Add(new LinearDoubleKeyFrame(first, KeyTime.FromTimeSpan(elbowTime)));
        x.KeyFrames.Add(new LinearDoubleKeyFrame(first, KeyTime.FromTimeSpan(verticalTime)));
        x.KeyFrames.Add(new LinearDoubleKeyFrame(horizontal, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        DoubleAnimationUsingKeyFrames y = new() { FillBehavior = FillBehavior.Stop };
        y.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        y.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(elbowTime)));
        y.KeyFrames.Add(new LinearDoubleKeyFrame(vertical, KeyTime.FromTimeSpan(verticalTime)));
        y.KeyFrames.Add(new LinearDoubleKeyFrame(vertical, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        transform.BeginAnimation(TranslateTransform.XProperty, x, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, y, HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateProjectionAnchorCache()
    {
        StartupMilestoneRow[] rows = GetMilestoneRows();
        int sensorIndex = Snapshot?.Milestones
            .Select((milestone, index) => (milestone, index))
            .FirstOrDefault(item => item.milestone.Id == StartupMilestoneId.SensorBus)
            .index ?? -1;
        if (sensorIndex < 0 || sensorIndex >= rows.Length
            || !rows[sensorIndex].IsLoaded
            || !ProjectionInputAnchor.IsLoaded)
        {
            return;
        }

        try
        {
            FrameworkElement sourceAnchor = rows[sensorIndex].RouteOutputAnchorElement;
            Point source = sourceAnchor.TranslatePoint(
                new Point(sourceAnchor.ActualWidth / 2d, sourceAnchor.ActualHeight / 2d),
                OverlayRoot);
            Point target = ProjectionInputAnchor.TranslatePoint(
                new Point(0d, ProjectionInputAnchor.ActualHeight / 2d),
                OverlayRoot);
            if (double.IsFinite(source.X)
                && double.IsFinite(source.Y)
                && double.IsFinite(target.X)
                && double.IsFinite(target.Y))
            {
                projectionPulseSource = source;
                projectionPulseTarget = target;
            }
        }
        catch (InvalidOperationException)
        {
            // Keep the last valid layout coordinates while ItemsControl replaces containers.
        }
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
        transform.X = -8d;
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            BuildDoubleAnimation(0d, -8d, TimeSpan.Zero, TimeSpan.FromMilliseconds(110)),
            HandoffBehavior.SnapshotAndReplace);

        double width = Math.Max(1d, StartupContentLayer.ActualWidth);
        double height = Math.Max(1d, StartupContentLayer.ActualHeight);
        RectangleGeometry clip = new(new Rect(0d, 0d, width * 0.75d, height));
        StartupContentLayer.Clip = clip;
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, width, height),
                new Rect(0d, 0d, width * 0.75d, height),
                TimeSpan.FromMilliseconds(110))
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
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

        RectangleGeometry centerClip = new(new Rect(0d, 0d, 6d, 6d));
        CommitCenterClipHost.Clip = centerClip;
        centerClip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, 0d, 6d),
                new Rect(0d, 0d, 6d, 6d),
                TimeSpan.FromMilliseconds(90))
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
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
        $"{projection.ResolvedVisibleSlotCount} / {projection.TotalVisibleSlotCount} RESOLVED";

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
                     LedgerEnvironmentGroup, LedgerProjectionGroup, CommitGroup, CommitLock, CommitText
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
        ProjectionPulseTrack.BeginAnimation(OpacityProperty, null);
        ProjectionPulseHead.BeginAnimation(OpacityProperty, null);
        ClearTranslation(ProjectionPulseHead);
        ClearGeometry(ProjectionPulseTrack);
        ProjectionPulseTrack.Data = null;
        ProjectionPulseTrack.Opacity = 0d;
        ProjectionPulseHead.Opacity = 0d;
        ProjectionPulseCanvas.Opacity = 0d;
    }

    private static void PlayVerticalClip(
        FrameworkElement host,
        FrameworkElement content,
        TimeSpan duration)
    {
        double width = Math.Max(1d, content.ActualWidth);
        double height = Math.Max(1d, content.ActualHeight);
        RectangleGeometry clip = new(new Rect(0d, 0d, width, height));
        host.Clip = clip;
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, width, 0d),
                new Rect(0d, 0d, width, height),
                duration)
            {
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
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
}
