using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;

namespace HardwareVision.Views.Shell;

public partial class TraceworkStartupSequenceOverlay : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(StartupSequenceSnapshot),
        typeof(TraceworkStartupSequenceOverlay),
        new PropertyMetadata(null, OnSnapshotChanged));

    private bool commitPlayed;
    private bool indexPlayed;
    private bool revealExitPlayed;
    private bool routePlayed;
    private int lastProjectionResolvedCount = -1;
    private long latestVersion = -1;
    private StartupSequenceSnapshot? previousSnapshot;

    public TraceworkStartupSequenceOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => ConfigureMilestoneRows();
        RouteMatrixItems.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (RouteMatrixItems.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                ConfigureMilestoneRows();
            }
        };
        SizeChanged += (_, _) => ApplyResponsiveMargins(ActualWidth);
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
        ApplyResponsiveMargins(ActualWidth);
    }

    public void RestoreFinalState()
    {
        ClearAnimation(this, OpacityProperty);
        ClearAnimation(StartupBackgroundLayer, OpacityProperty);
        ClearAnimation(StartupContentLayer, OpacityProperty);
        ClearAnimation(StartupBottomRailLayer, OpacityProperty);
        ClearAnimation(SystemIndexText, OpacityProperty);
        ClearAnimation(RouteMatrixItems, OpacityProperty);
        ClearAnimation(ProjectionValue, OpacityProperty);
        ClearAnimation(InternalPulse, OpacityProperty);
        ClearAnimation(CommitGroup, OpacityProperty);
        ClearAnimation(CommitLock, OpacityProperty);
        ClearAnimation(CommitText, OpacityProperty);
        InternalPulseTransform.BeginAnimation(TranslateTransform.XProperty, null);
        if (StartupContentLayer.RenderTransform is TranslateTransform contentTransform)
        {
            contentTransform.BeginAnimation(TranslateTransform.XProperty, null);
            contentTransform.X = 0d;
        }

        ClearGeometry(SystemIndexClipHost);
        ClearGeometry(ProjectionValueClipHost);
        ClearGeometry(StartupContentLayer);
        ClearGeometry(CommitCenterClipHost);
        foreach (StartupMilestoneRow row in GetMilestoneRows())
        {
            row.ClearTransientState();
        }

        Opacity = 0d;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        InternalPulse.Opacity = 0d;
        RouteMatrixItems.Opacity = 1d;
        InternalPulseTransform.X = 0d;
        CommitLock.Opacity = 0d;
        CommitText.Opacity = 1d;
        CommitGroup.Opacity = 0d;
        CommitGroup.Visibility = Visibility.Collapsed;
        StartupBackgroundLayer.Opacity = 0d;
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
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

        if (snapshot.HasCompleted
            || snapshot.CurrentTheme != AppTheme.Tracework
            || snapshot.MotionLevel == MotionLevel.Off)
        {
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
        StartupBottomRailLayer.Opacity = 1d;
        ApplyMilestoneTransitions(prior, snapshot);
        ApplyProjectionTransition(snapshot);

        bool canShowCommit = snapshot.Phase == StartupSequencePhase.Lock && snapshot.CanCommit;
        CommitGroup.Visibility = canShowCommit ? Visibility.Visible : Visibility.Collapsed;
        if (!canShowCommit)
        {
            CommitGroup.Opacity = 0d;
            CommitLock.Opacity = 0d;
        }

        if (snapshot.Phase == StartupSequencePhase.Index && !indexPlayed)
        {
            indexPlayed = true;
            PlayIndexReveal(snapshot.MotionLevel);
        }
        else if (snapshot.Phase == StartupSequencePhase.Route && !routePlayed)
        {
            routePlayed = true;
            PlayRoute(snapshot.MotionLevel);
        }

        if (canShowCommit && !commitPlayed)
        {
            commitPlayed = true;
            PlayCommit(snapshot.MotionLevel);
        }

        if (snapshot.Phase == StartupSequencePhase.Reveal && !revealExitPlayed)
        {
            revealExitPlayed = true;
            PlayConcurrentExit(snapshot.MotionLevel);
        }
    }

    private void ResetPlaybackState(StartupSequenceSnapshot snapshot)
    {
        indexPlayed = false;
        routePlayed = false;
        commitPlayed = false;
        revealExitPlayed = false;
        lastProjectionResolvedCount = snapshot.InitialProjection.ResolvedVisibleSlotCount;
    }

    private void ConfigureMilestoneRows()
    {
        StartupMilestoneRow[] rows = GetMilestoneRows();
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index].ConfigureSegments(index == 0, index == rows.Length - 1);
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

    private void ApplyProjectionTransition(StartupSequenceSnapshot snapshot)
    {
        int current = snapshot.InitialProjection.ResolvedVisibleSlotCount;
        if (lastProjectionResolvedCount < 0)
        {
            lastProjectionResolvedCount = current;
            return;
        }

        int previous = lastProjectionResolvedCount;
        lastProjectionResolvedCount = current;
        if (current <= previous)
        {
            return;
        }

        if (snapshot.MotionLevel == MotionLevel.Reduced)
        {
            AnimateOpacity(ProjectionValue, TimeSpan.Zero, TimeSpan.FromMilliseconds(90));
            return;
        }

        PlayVerticalClip(ProjectionValueClipHost, ProjectionValue, TimeSpan.FromMilliseconds(90));
        if (snapshot.MotionLevel == MotionLevel.Full)
        {
            PlayProjectionPulse();
        }
    }

    private void PlayRoute(MotionLevel level)
    {
        if (level == MotionLevel.Reduced)
        {
            AnimateOpacity(RouteMatrixItems, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));
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
                    PlayRoute(level);
                }));
            return;
        }

        int interval = level == MotionLevel.Full
            ? 45
            : level == MotionLevel.Standard
                ? 28
                : 0;
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index].PlayRouteReveal(level, TimeSpan.FromMilliseconds(index * interval));
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

        TranslateTransform transform = StartupContentLayer.RenderTransform as TranslateTransform ?? new TranslateTransform();
        StartupContentLayer.RenderTransform = transform;
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

    private void PlayIndexReveal(MotionLevel level)
    {
        if (level == MotionLevel.Reduced)
        {
            AnimateOpacity(SystemIndexText, TimeSpan.Zero, TimeSpan.FromMilliseconds(80));
            return;
        }

        double width = Math.Max(1d, SystemIndexText.ActualWidth);
        double height = Math.Max(1d, SystemIndexText.ActualHeight);
        RectangleGeometry clip = new(new Rect(0d, 0d, width, height));
        SystemIndexClipHost.Clip = clip;
        TimeSpan duration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(180)
            : TimeSpan.FromMilliseconds(120);
        clip.BeginAnimation(
            RectangleGeometry.RectProperty,
            new RectAnimation(
                new Rect(0d, 0d, 0d, height),
                new Rect(0d, 0d, width, height),
                duration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void PlayProjectionPulse()
    {
        InternalPulse.Opacity = 0d;
        InternalPulseTransform.X = 0d;
        DoubleAnimationUsingKeyFrames pulse = new() { FillBehavior = FillBehavior.Stop };
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.85d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(65))));
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        InternalPulse.BeginAnimation(OpacityProperty, pulse, HandoffBehavior.SnapshotAndReplace);
        InternalPulseTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(0d, 180d, TimeSpan.FromMilliseconds(180))
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
