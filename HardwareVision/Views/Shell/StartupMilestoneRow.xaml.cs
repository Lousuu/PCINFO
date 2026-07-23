using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Views.Shell;

public partial class StartupMilestoneRow : System.Windows.Controls.UserControl
{
    private bool routeArrivalPlayed;
    private StartupMilestoneState? terminalLockPlayedState;

    public StartupMilestoneRow()
    {
        InitializeComponent();
    }

    internal FrameworkElement RouteOutputAnchorElement => RouteOutputAnchor;

    internal void ConfigureSegments(bool isFirst, bool isLast)
    {
        UpperRouteSegment.Visibility = isFirst ? Visibility.Hidden : Visibility.Visible;
        LowerRouteSegment.Visibility = isLast ? Visibility.Hidden : Visibility.Visible;
    }

    internal void PrepareForRoute(MotionLevel level)
    {
        ClearRouteAnimations();
        TerminalLockFrame.BeginAnimation(OpacityProperty, null);
        TerminalLockFrame.Opacity = 0d;
        routeArrivalPlayed = false;
        terminalLockPlayedState = null;

        if (level == MotionLevel.Off)
        {
            SetRouteFinalState();
            return;
        }

        if (level == MotionLevel.Reduced)
        {
            SetRouteFinalState();
            return;
        }

        PrepareSegment(UpperRouteSegment);
        PrepareSegment(LowerRouteSegment);
        if (level == MotionLevel.Standard)
        {
            RowRoot.Opacity = 0d;
            MilestoneNode.Opacity = 1d;
            MilestoneName.Opacity = 1d;
            MilestoneStatus.Opacity = 1d;
            MilestoneDetail.Opacity = 1d;
            SetNameTranslation(0d);
            return;
        }

        RowRoot.Opacity = 1d;
        MilestoneNode.Opacity = 0d;
        MilestoneName.Opacity = 0d;
        MilestoneStatus.Opacity = 0d;
        MilestoneDetail.Opacity = 0d;
        SetNameTranslation(6d);
    }

    internal void PlayRouteReveal(MotionLevel level, TimeSpan delay)
    {
        if (level == MotionLevel.Off)
        {
            SetRouteFinalState();
            return;
        }

        if (level == MotionLevel.Reduced)
        {
            return;
        }

        AnimateSegment(UpperRouteSegment, delay, TimeSpan.FromMilliseconds(70));
        AnimateSegment(LowerRouteSegment, delay, TimeSpan.FromMilliseconds(70));
        if (level == MotionLevel.Standard)
        {
            AnimateOpacity(RowRoot, delay, TimeSpan.FromMilliseconds(80));
            return;
        }

        AnimateOpacity(MilestoneNode, delay + TimeSpan.FromMilliseconds(70), TimeSpan.FromMilliseconds(50));
        AnimateOpacity(MilestoneName, delay, TimeSpan.FromMilliseconds(90));
        AnimateOpacity(MilestoneStatus, delay + TimeSpan.FromMilliseconds(70), TimeSpan.FromMilliseconds(80));
        AnimateOpacity(MilestoneDetail, delay + TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100));
        if (MilestoneName.RenderTransform is TranslateTransform transform)
        {
            transform.X = 0d;
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                BuildDoubleAnimation(6d, 0d, delay, TimeSpan.FromMilliseconds(90)),
                HandoffBehavior.SnapshotAndReplace);
        }
    }

    internal void PlayRouteArrivalState(
        StartupMilestoneState current,
        MotionLevel level,
        TimeSpan routeDelay)
    {
        if (routeArrivalPlayed)
        {
            return;
        }

        routeArrivalPlayed = true;
        if (current == StartupMilestoneState.Wait || level == MotionLevel.Off)
        {
            return;
        }

        if (current == StartupMilestoneState.Pending)
        {
            TimeSpan pendingDelay = level == MotionLevel.Reduced
                ? TimeSpan.Zero
                : routeDelay + TimeSpan.FromMilliseconds(70);
            AnimateOpacity(MilestoneNode, pendingDelay, TimeSpan.FromMilliseconds(80), 0.45d, 1d);
            AnimateOpacity(MilestoneStatus, pendingDelay, TimeSpan.FromMilliseconds(80), 0.45d, 1d);
            return;
        }

        if (current is StartupMilestoneState.Ready
            or StartupMilestoneState.Partial
            or StartupMilestoneState.Failed)
        {
            PlayTerminalLock(
                current,
                level,
                level == MotionLevel.Reduced
                    ? TimeSpan.Zero
                    : routeDelay + TimeSpan.FromMilliseconds(100));
        }
    }

    internal void PlayStateTransition(
        StartupMilestoneState previous,
        StartupMilestoneState current,
        MotionLevel level)
    {
        if (previous == current || level == MotionLevel.Off)
        {
            return;
        }

        if (current == StartupMilestoneState.Pending)
        {
            AnimateOpacity(MilestoneNode, TimeSpan.Zero, TimeSpan.FromMilliseconds(80), 0.45d, 1d);
            AnimateOpacity(MilestoneStatus, TimeSpan.Zero, TimeSpan.FromMilliseconds(80), 0.45d, 1d);
            return;
        }

        if (current is StartupMilestoneState.Ready
            or StartupMilestoneState.Partial
            or StartupMilestoneState.Failed)
        {
            PlayTerminalLock(current, level, TimeSpan.Zero);
        }
    }

    internal void ClearTransientState()
    {
        ClearRouteAnimations();
        TerminalLockFrame.BeginAnimation(OpacityProperty, null);
        TerminalLockFrame.Opacity = 0d;
        routeArrivalPlayed = false;
        terminalLockPlayedState = null;
        SetRouteFinalState();
    }

    private void PlayTerminalLock(
        StartupMilestoneState current,
        MotionLevel level,
        TimeSpan delay)
    {
        if (terminalLockPlayedState == current)
        {
            return;
        }

        terminalLockPlayedState = current;
        TimeSpan peak = level == MotionLevel.Reduced
            ? delay + TimeSpan.FromMilliseconds(35)
            : delay + TimeSpan.FromMilliseconds(50);
        TimeSpan end = level == MotionLevel.Reduced
            ? delay + TimeSpan.FromMilliseconds(80)
            : delay + TimeSpan.FromMilliseconds(140);
        DoubleAnimationUsingKeyFrames frame = new() { FillBehavior = FillBehavior.Stop };
        frame.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frame.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(delay)));
        frame.KeyFrames.Add(new LinearDoubleKeyFrame(
            level == MotionLevel.Reduced ? 0.65d : 0.9d,
            KeyTime.FromTimeSpan(peak)));
        frame.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(end)));
        TerminalLockFrame.Opacity = 0d;
        TerminalLockFrame.BeginAnimation(OpacityProperty, frame, HandoffBehavior.SnapshotAndReplace);
    }

    private void SetRouteFinalState()
    {
        RowRoot.Opacity = 1d;
        MilestoneNode.Opacity = 1d;
        MilestoneName.Opacity = 1d;
        MilestoneStatus.Opacity = 1d;
        MilestoneDetail.Opacity = 1d;
        UpperRouteSegment.Clip = null;
        LowerRouteSegment.Clip = null;
        SetNameTranslation(0d);
    }

    private void ClearRouteAnimations()
    {
        RowRoot.BeginAnimation(OpacityProperty, null);
        MilestoneNode.BeginAnimation(OpacityProperty, null);
        MilestoneName.BeginAnimation(OpacityProperty, null);
        MilestoneStatus.BeginAnimation(OpacityProperty, null);
        MilestoneDetail.BeginAnimation(OpacityProperty, null);
        if (MilestoneName.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        ClearSegmentAnimation(UpperRouteSegment);
        ClearSegmentAnimation(LowerRouteSegment);
    }

    private static void PrepareSegment(FrameworkElement segment)
    {
        segment.Clip = new RectangleGeometry(new Rect(0d, 0d, 1d, 0d));
    }

    private static void AnimateSegment(FrameworkElement segment, TimeSpan delay, TimeSpan duration)
    {
        if (segment.Visibility != Visibility.Visible)
        {
            return;
        }

        RectangleGeometry clip = new(new Rect(0d, 0d, 1d, 17d));
        segment.Clip = clip;
        RectAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteRectKeyFrame(new Rect(0d, 0d, 1d, 0d), KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new LinearRectKeyFrame(new Rect(0d, 0d, 1d, 17d), KeyTime.FromTimeSpan(delay + duration)));
        clip.BeginAnimation(RectangleGeometry.RectProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void ClearSegmentAnimation(FrameworkElement segment)
    {
        if (segment.Clip is RectangleGeometry clip)
        {
            clip.BeginAnimation(RectangleGeometry.RectProperty, null);
        }

        segment.Clip = null;
    }

    private void SetNameTranslation(double value)
    {
        if (MilestoneName.RenderTransform is TranslateTransform transform)
        {
            transform.X = value;
        }
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
}
