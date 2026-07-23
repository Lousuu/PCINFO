using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Views.Shell;

public partial class StartupMilestoneRow : System.Windows.Controls.UserControl
{
    public StartupMilestoneRow()
    {
        InitializeComponent();
    }

    internal void ConfigureSegments(bool isFirst, bool isLast)
    {
        UpperRouteSegment.Visibility = isFirst ? Visibility.Hidden : Visibility.Visible;
        LowerRouteSegment.Visibility = isLast ? Visibility.Hidden : Visibility.Visible;
    }

    internal void PlayRouteReveal(MotionLevel level, TimeSpan delay)
    {
        ClearRouteAnimations();
        if (level == MotionLevel.Off)
        {
            SetRouteFinalState();
            return;
        }

        if (level == MotionLevel.Reduced)
        {
            AnimateOpacity(RowRoot, delay, TimeSpan.FromMilliseconds(80));
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
            DoubleAnimationUsingKeyFrames frame = new() { FillBehavior = FillBehavior.Stop };
            frame.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frame.KeyFrames.Add(new LinearDoubleKeyFrame(0.9d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
            frame.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
            TerminalLockFrame.Opacity = 0d;
            TerminalLockFrame.BeginAnimation(OpacityProperty, frame, HandoffBehavior.SnapshotAndReplace);
        }
    }

    internal void ClearTransientState()
    {
        ClearRouteAnimations();
        TerminalLockFrame.BeginAnimation(OpacityProperty, null);
        TerminalLockFrame.Opacity = 0d;
        SetRouteFinalState();
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
        if (MilestoneName.RenderTransform is TranslateTransform transform)
        {
            transform.X = 0d;
        }
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
