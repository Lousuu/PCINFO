using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;
using HardwareVision.ViewModels;
using WpfButton = System.Windows.Controls.Button;
using WpfBinding = System.Windows.Data.Binding;
using WpfPoint = System.Windows.Point;

namespace HardwareVision.Views.Shell;

public partial class TraceworkSignalRail : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(NavigationTransitionSnapshot),
        typeof(TraceworkSignalRail),
        new PropertyMetadata(NavigationTransitionSnapshot.Idle(), OnSnapshotChanged));

    private long activeVersion = -1;
    private long committedVersion = -1;

    public TraceworkSignalRail()
    {
        InitializeComponent();
        SetBinding(SnapshotProperty, new WpfBinding("NavigationTransition"));
        Focusable = false;
        Unloaded += (_, _) => RestoreRouteVisuals();
    }

    public NavigationTransitionSnapshot Snapshot
    {
        get => (NavigationTransitionSnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TraceworkSignalRail rail && e.NewValue is NavigationTransitionSnapshot snapshot)
        {
            rail.ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive || !snapshot.Plan.ShowsSignalRailCursor)
        {
            RestoreRouteVisuals();
            return;
        }

        if (snapshot.Phase == NavigationTransitionPhase.Relay && snapshot.HasCommitted)
        {
            PlayArrivalLock(snapshot);
            return;
        }

        if (activeVersion == snapshot.Version)
        {
            return;
        }

        activeVersion = snapshot.Version;
        _ = Dispatcher.BeginInvoke(new Action(() => StartRoute(snapshot)));
    }

    public void CancelTransition() => RestoreRouteVisuals();

    private void StartRoute(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive || snapshot.Version != activeVersion)
        {
            return;
        }

        UpdateLayout();
        WpfButton? origin = FindButton(snapshot.OriginPage, requireSelected: true);
        WpfButton? target = FindButton(snapshot.TargetPage, requireSelected: false);
        if (origin is null || target is null)
        {
            RestoreRouteVisuals();
            return;
        }

        try
        {
            double start = CenterY(origin);
            double end = CenterY(target);
            double segmentTop = Math.Min(start, end) + 1d;
            RouteSegment.Height = Math.Max(1d, Math.Abs(end - start));
            RouteSegmentTranslate.Y = segmentTop;
            RouteSegment.Visibility = Visibility.Visible;
            RouteSegment.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0d, RouteOpacity(snapshot), new Duration(snapshot.Plan.RouteDuration))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                },
                HandoffBehavior.SnapshotAndReplace);

            RoutePulse.Visibility = Visibility.Visible;
            RoutePulse.Opacity = 1d;
            RoutePulseTranslate.Y = start;
            RoutePulseTranslate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(start, end, new Duration(snapshot.Plan.CommitTime))
                {
                    FillBehavior = FillBehavior.HoldEnd,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                },
                HandoffBehavior.SnapshotAndReplace);

            if (snapshot.Plan.EffectiveLevel == MotionLevel.Full)
            {
                PulseTrail.Visibility = Visibility.Visible;
                PulseTrail.Opacity = 0.65d;
                PulseTrailTranslate.Y = start;
                PulseTrailTranslate.BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(start, end, new Duration(snapshot.Plan.CommitTime))
                    {
                        FillBehavior = FillBehavior.HoldEnd,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    },
                    HandoffBehavior.SnapshotAndReplace);
                PulseTrail.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0.65d, 0d, new Duration(snapshot.Plan.CommitTime))
                    {
                        FillBehavior = FillBehavior.HoldEnd,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    },
                    HandoffBehavior.SnapshotAndReplace);
            }
        }
        catch (InvalidOperationException)
        {
            RestoreRouteVisuals();
        }
    }

    private void PlayArrivalLock(NavigationTransitionSnapshot snapshot)
    {
        if (activeVersion != snapshot.Version || committedVersion == snapshot.Version)
        {
            return;
        }
        committedVersion = snapshot.Version;

        WpfButton? target = FindButton(snapshot.TargetPage, requireSelected: true);
        if (target is null)
        {
            RestoreRouteVisuals();
            return;
        }

        double targetCenter = CenterY(target);
        RoutePulse.Visibility = Visibility.Collapsed;
        RoutePulse.Opacity = 0d;
        PulseTrail.Visibility = Visibility.Collapsed;
        PulseTrail.Opacity = 0d;
        RouteSegment.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(RouteOpacity(snapshot), 0d, new Duration(LockDuration(snapshot)))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);

        ArrivalLockTranslate.Y = targetCenter;
        ArrivalLock.Visibility = Visibility.Visible;
        ArrivalLock.Opacity = 0d;
        TimeSpan duration = LockDuration(snapshot);
        DoubleAnimationUsingKeyFrames fade = new() { FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromPercent(0.38d)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromPercent(0.62d)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(duration)));
        fade.Completed += (_, _) => RestoreRouteVisuals();
        ArrivalLock.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    private static double RouteOpacity(NavigationTransitionSnapshot snapshot) =>
        snapshot.Plan.EffectiveLevel == MotionLevel.Full ? 0.72d : 0.52d;

    private static TimeSpan LockDuration(NavigationTransitionSnapshot snapshot) =>
        snapshot.Plan.EffectiveLevel == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(52)
            : TimeSpan.FromMilliseconds(38);

    private static void ClearVisual(Border visual, TranslateTransform transform)
    {
        visual.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        visual.Opacity = 0d;
        transform.X = 0d;
        transform.Y = 0d;
        visual.Visibility = Visibility.Collapsed;
        visual.IsHitTestVisible = false;
    }

    private void RestoreRouteVisuals()
    {
        ClearVisual(RouteSegment, RouteSegmentTranslate);
        ClearVisual(RoutePulse, RoutePulseTranslate);
        ClearVisual(PulseTrail, PulseTrailTranslate);
        ClearVisual(ArrivalLock, ArrivalLockTranslate);
        RouteSegment.Height = 0d;
        activeVersion = -1;
        committedVersion = -1;
    }

    private WpfButton? FindButton(string pageKey, bool requireSelected)
    {
        return FindVisualDescendants<WpfButton>(NavigationItemsControl).FirstOrDefault(button =>
            button.DataContext is NavigationItemViewModel item
            && item.Key == pageKey
            && (!requireSelected || item.IsSelected));
    }

    private double CenterY(FrameworkElement element)
    {
        WpfPoint origin = element.TransformToAncestor(this).Transform(new WpfPoint(0d, 0d));
        return origin.Y + (element.ActualHeight / 2d) - (RoutePulse.Height / 2d);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindVisualDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
