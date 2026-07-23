using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Views.Shell;

public partial class StartupMilestoneRow : System.Windows.Controls.UserControl
{
    internal const int FullRouteRowIntervalMilliseconds = 205;
    internal const int StandardRouteRowIntervalMilliseconds = 120;
    internal const int FullRouteFinalRowEndMilliseconds = 180;
    internal const int StandardRouteFinalRowEndMilliseconds = 70;

    private bool routeArrivalPlayed;
    private StartupSequencePhase? projectionPortPhase;
    private StartupMilestoneState? terminalLockPlayedState;

    public StartupMilestoneRow()
    {
        InitializeComponent();
    }

    internal FrameworkElement RouteOutputAnchorElement => RouteOutputAnchor;

    internal void ConfigureSegments(bool isFirst, bool isLast, bool isProjectionSource)
    {
        UpperRouteSegment.Visibility = isFirst ? Visibility.Hidden : Visibility.Visible;
        LowerRouteSegment.Visibility = isLast ? Visibility.Hidden : Visibility.Visible;
        bool projectionSourceChanged = RouteOutputPort.Tag is not bool prior
            || prior != isProjectionSource;
        RouteOutputPort.Tag = isProjectionSource;
        if (projectionSourceChanged)
        {
            RouteOutputPort.Visibility = Visibility.Collapsed;
            RouteOutputPort.Opacity = 0d;
            projectionPortPhase = null;
        }
    }

    internal void SetProjectionPortPhase(StartupSequencePhase phase, MotionLevel level)
    {
        bool isProjectionSource = RouteOutputPort.Tag is true;
        if (isProjectionSource && projectionPortPhase == phase)
        {
            return;
        }

        projectionPortPhase = phase;
        RouteOutputPort.BeginAnimation(OpacityProperty, null);
        if (!isProjectionSource
            || phase is StartupSequencePhase.Dormant or StartupSequencePhase.Index)
        {
            RouteOutputPort.Visibility = Visibility.Collapsed;
            RouteOutputPort.Opacity = 0d;
            return;
        }

        RouteOutputPort.Visibility = Visibility.Visible;
        if (phase == StartupSequencePhase.Route)
        {
            RouteOutputPort.Opacity = 0d;
            return;
        }

        if (phase == StartupSequencePhase.Bind)
        {
            if (level == MotionLevel.Reduced)
            {
                RouteOutputPort.Opacity = 1d;
                return;
            }

            AnimateOpacityWithCommittedFinalState(
                RouteOutputPort,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(80d),
                0.35d,
                1d);
            return;
        }

        RouteOutputPort.Opacity = 1d;
    }

    internal void PrepareForRoute(MotionLevel level)
    {
        ClearRouteAnimations();
        TerminalLockFrame.BeginAnimation(OpacityProperty, null);
        TerminalLockFrame.Opacity = 0d;
        PendingFrame.BeginAnimation(OpacityProperty, null);
        PendingFrame.Opacity = 0d;
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
            RevealProjectionPort(level, TimeSpan.Zero);
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

        if (level == MotionLevel.Standard)
        {
            CommitSegmentAt(UpperRouteSegment, delay);
            AnimateOpacity(
                RowRoot,
                delay,
                TimeSpan.FromMilliseconds(70));
            AnimateSegment(
                LowerRouteSegment,
                delay + TimeSpan.FromMilliseconds(70),
                TimeSpan.FromMilliseconds(50));
            RevealProjectionPort(level, delay);
            return;
        }

        CommitSegmentAt(UpperRouteSegment, delay);
        AnimateOpacity(
            MilestoneNode,
            delay,
            TimeSpan.FromMilliseconds(40));
        AnimateOpacity(
            MilestoneName,
            delay + TimeSpan.FromMilliseconds(15),
            TimeSpan.FromMilliseconds(55));
        AnimateOpacity(
            MilestoneStatus,
            delay + TimeSpan.FromMilliseconds(25),
            TimeSpan.FromMilliseconds(55));
        AnimateOpacity(
            MilestoneDetail,
            delay + TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(65));
        AnimateSegment(
            LowerRouteSegment,
            delay + TimeSpan.FromMilliseconds(145),
            TimeSpan.FromMilliseconds(60));
        RevealProjectionPort(level, delay);
        if (MilestoneName.RenderTransform is TranslateTransform transform)
        {
            transform.X = 0d;
            transform.BeginAnimation(
                TranslateTransform.XProperty,
                BuildDoubleAnimation(
                    6d,
                    0d,
                    delay + TimeSpan.FromMilliseconds(15),
                    TimeSpan.FromMilliseconds(55)),
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
                : routeDelay + TimeSpan.FromMilliseconds(
                    level == MotionLevel.Full ? 90d : 0d);
            PlayPendingFeedback(pendingDelay);
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
                    : routeDelay + TimeSpan.FromMilliseconds(
                        level == MotionLevel.Full ? 90d : 0d));
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
            PlayPendingFeedback(TimeSpan.Zero);
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
        PendingFrame.BeginAnimation(OpacityProperty, null);
        PendingFrame.Opacity = 0d;
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
        TimeSpan peak = delay + TimeSpan.FromMilliseconds(35);
        TimeSpan end = delay + TimeSpan.FromMilliseconds(
            level == MotionLevel.Reduced ? 80d : 90d);
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

    private void PlayPendingFeedback(TimeSpan delay)
    {
        DoubleAnimationUsingKeyFrames frame = new() { FillBehavior = FillBehavior.Stop };
        frame.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        frame.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(delay)));
        frame.KeyFrames.Add(new LinearDoubleKeyFrame(
            0.65d,
            KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(45))));
        frame.KeyFrames.Add(new LinearDoubleKeyFrame(
            0d,
            KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(120))));
        PendingFrame.Opacity = 0d;
        PendingFrame.BeginAnimation(
            OpacityProperty,
            frame,
            HandoffBehavior.SnapshotAndReplace);
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
        PendingFrame.BeginAnimation(OpacityProperty, null);
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

        RectangleGeometry clip = new();
        segment.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            new Rect(0d, 0d, 1d, 0d),
            new Rect(0d, 0d, 1d, 17d),
            delay,
            duration);
    }

    private static void CommitSegmentAt(FrameworkElement segment, TimeSpan delay)
    {
        if (segment.Visibility != Visibility.Visible)
        {
            return;
        }

        Rect initial = new(0d, 0d, 1d, 0d);
        Rect final = new(0d, 0d, 1d, 17d);
        RectangleGeometry clip = new(initial);
        segment.Clip = clip;
        AnimateRectWithCommittedFinalState(
            clip,
            initial,
            final,
            delay,
            TimeSpan.FromMilliseconds(1));
    }

    private void RevealProjectionPort(MotionLevel level, TimeSpan delay)
    {
        if (RouteOutputPort.Tag is not true)
        {
            return;
        }

        RouteOutputPort.Visibility = Visibility.Visible;
        if (level == MotionLevel.Reduced)
        {
            RouteOutputPort.Opacity = 0.35d;
            return;
        }

        AnimateOpacityWithCommittedFinalState(
            RouteOutputPort,
            delay,
            TimeSpan.FromMilliseconds(level == MotionLevel.Full ? 80d : 60d),
            0d,
            0.35d);
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

    private static void AnimateOpacityWithCommittedFinalState(
        UIElement target,
        TimeSpan delay,
        TimeSpan duration,
        double from,
        double to)
    {
        target.Opacity = from;
        DoubleAnimationUsingKeyFrames animation =
            BuildDoubleAnimation(from, to, delay, duration);
        animation.Completed += (_, _) =>
        {
            target.Opacity = to;
            target.BeginAnimation(OpacityProperty, null);
        };
        target.BeginAnimation(
            OpacityProperty,
            animation,
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
        animation.KeyFrames.Add(new DiscreteRectKeyFrame(initial, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteRectKeyFrame(initial, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new LinearRectKeyFrame(final, KeyTime.FromTimeSpan(delay + duration)));
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
}
