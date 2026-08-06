using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    internal const int FullDetailTransitionMilliseconds = 140;
    internal const int StandardDetailTransitionMilliseconds = 105;
    internal const int ReducedDetailTransitionMilliseconds = 90;

    private long detailTransitionGeneration;
    private bool detailInitialized;
    private bool detailTransitionActive;
    private MotionLevel detailMotionLevel = MotionLevel.Full;
    private string presentedDetailText = string.Empty;
    private string expectedTerminalDetail = string.Empty;
    private long detailContextVersion = -1;
    private long lastDetailCompletionVersion = -1;
    private bool detailContextIsTerminal;
    private bool isFinalSensorDetailPresented;
    private bool projectionPortAuthorized;
    private long projectionPortRevealGeneration;
    private int projectionPortRevealCount;
    private bool routeArrivalPlayed;
    private StartupSequencePhase? projectionPortPhase;
    private StartupMilestoneState? terminalLockPlayedState;

    public StartupMilestoneRow()
    {
        InitializeComponent();
        Unloaded += (_, _) => RestoreDetailFinalState();
    }

    internal bool ApplyResponsiveDetailWidth(double hostWidth)
    {
        double width = ResolveDetailMaxWidth(hostWidth);
        double previousWidth = MilestoneDetail.MaxWidth;
        bool changed = Math.Abs(previousWidth - width) > 0.01d;
        PreviousDetailText.MaxWidth = width;
        MilestoneDetail.MaxWidth = width;
        return changed;
    }

    internal static double ResolveDetailMaxWidth(double hostWidth) => hostWidth switch
    {
        >= 1108d => 420d,
        >= 1000d => 300d,
        _ => 220d
    };

    internal FrameworkElement RouteOutputAnchorElement => RouteOutputAnchor;
    internal FrameworkElement RouteOutputPortElement => RouteOutputPort;
    internal double DetailSlotWidth => DetailColumn.ActualWidth;
    internal string PresentedDetailText => presentedDetailText;
    internal bool IsDetailTransitionActive => detailTransitionActive;
    internal bool IsFinalSensorDetailPresented => isFinalSensorDetailPresented;
    internal long DetailTransitionGeneration => detailTransitionGeneration;
    internal int ProjectionPortRevealCount => projectionPortRevealCount;
    internal TextBlock PreviousDetailElement => PreviousDetailText;
    internal TextBlock CurrentDetailElement => MilestoneDetail;

    internal event Action<StartupMilestoneRow, DetailPresentationCompletion>?
        DetailPresentationCompleted;

    internal void SetMotionLevel(MotionLevel level)
    {
        detailMotionLevel = level;
        if (level == MotionLevel.Off)
        {
            RestoreDetailFinalState();
        }
    }

    internal void ConfigureSegments(bool isFirst, bool isLast, bool isProjectionSource)
    {
        UpperRouteSegment.Visibility = isFirst ? Visibility.Hidden : Visibility.Visible;
        LowerRouteSegment.Visibility = isLast ? Visibility.Hidden : Visibility.Visible;
        bool projectionSourceChanged = RouteOutputPort.Tag is not bool prior
            || prior != isProjectionSource;
        RouteOutputPort.Tag = isProjectionSource;
        if (projectionSourceChanged)
        {
            HideProjectionPort(keepArranged: false);
            projectionPortPhase = null;
        }
    }

    internal void SetProjectionPortPhase(StartupSequencePhase phase, MotionLevel level)
    {
        SetMotionLevel(level);
        bool isProjectionSource = RouteOutputPort.Tag is true;
        if (isProjectionSource && projectionPortPhase == phase)
        {
            return;
        }

        projectionPortPhase = phase;
        if (!isProjectionSource
            || phase is StartupSequencePhase.Dormant or StartupSequencePhase.Index)
        {
            HideProjectionPort(keepArranged: false);
            return;
        }

        RouteOutputPort.Visibility = Visibility.Visible;
        if (!projectionPortAuthorized)
        {
            RouteOutputPort.BeginAnimation(OpacityProperty, null);
            RouteOutputPort.Opacity = 0d;
        }
    }

    internal void SetDetailPresentationContext(
        long snapshotVersion,
        string expectedDetail,
        bool isTerminal)
    {
        detailContextVersion = snapshotVersion;
        expectedTerminalDetail = expectedDetail ?? string.Empty;
        detailContextIsTerminal = isTerminal;
        if (!isTerminal)
        {
            isFinalSensorDetailPresented = false;
            return;
        }

        NotifyDetailPresentationCompletedIfAuthorized();
    }

    internal void RevealProjectionPort(MotionLevel level, Action completed)
    {
        if (RouteOutputPort.Tag is not true || projectionPortAuthorized)
        {
            completed();
            return;
        }

        projectionPortAuthorized = true;
        projectionPortRevealCount++;
        long generation = ++projectionPortRevealGeneration;
        RouteOutputPort.Visibility = Visibility.Visible;
        RouteOutputPort.BeginAnimation(OpacityProperty, null);
        RouteOutputPort.Opacity = 1d;
        if (level is MotionLevel.Off or MotionLevel.Reduced)
        {
            completed();
            return;
        }

        DoubleAnimationUsingKeyFrames opacity = BuildDoubleAnimation(
            0d,
            1d,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(level == MotionLevel.Full ? 80d : 60d));
        opacity.Completed += (_, _) =>
        {
            if (generation != projectionPortRevealGeneration)
            {
                return;
            }

            RouteOutputPort.BeginAnimation(OpacityProperty, null);
            RouteOutputPort.Opacity = 1d;
            completed();
        };
        RouteOutputPort.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);
    }

    internal void HideProjectionPort(bool keepArranged = true)
    {
        projectionPortRevealGeneration++;
        projectionPortAuthorized = false;
        RouteOutputPort.BeginAnimation(OpacityProperty, null);
        RouteOutputPort.Opacity = 0d;
        RouteOutputPort.Visibility = keepArranged
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    internal void PrepareForRoute(MotionLevel level)
    {
        SetMotionLevel(level);
        RestoreDetailFinalState();
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
            MilestoneDetailClipHost.Opacity = 1d;
            SetNameTranslation(0d);
            return;
        }

        RowRoot.Opacity = 1d;
        MilestoneNode.Opacity = 0d;
        MilestoneName.Opacity = 0d;
        MilestoneStatus.Opacity = 0d;
        MilestoneDetailClipHost.Opacity = 0d;
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
            MilestoneDetailClipHost,
            delay + TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(65));
        AnimateSegment(
            LowerRouteSegment,
            delay + TimeSpan.FromMilliseconds(145),
            TimeSpan.FromMilliseconds(60));
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
        RestoreDetailFinalState();
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
        MilestoneDetailClipHost.Opacity = 1d;
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
        MilestoneDetailClipHost.BeginAnimation(OpacityProperty, null);
        PendingFrame.BeginAnimation(OpacityProperty, null);
        if (MilestoneName.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        ClearSegmentAnimation(UpperRouteSegment);
        ClearSegmentAnimation(LowerRouteSegment);
    }

    internal void RestoreDetailFinalState()
    {
        detailTransitionGeneration++;
        detailTransitionActive = false;
        ClearDetailClocks();
        PreviousDetailText.Text = string.Empty;
        PreviousDetailText.Opacity = 0d;
        MilestoneDetail.Opacity = 1d;
        SetDetailTranslation(PreviousDetailText, 0d);
        SetDetailTranslation(MilestoneDetail, 0d);
        presentedDetailText = MilestoneDetail.Text ?? presentedDetailText;
    }

    private void OnDetailTargetUpdated(object sender, DataTransferEventArgs args)
    {
        _ = sender;
        _ = args;
        string target = MilestoneDetail.Text ?? string.Empty;
        if (!detailInitialized)
        {
            detailInitialized = true;
            presentedDetailText = target;
            RestoreDetailFinalState();
            return;
        }

        if (string.Equals(presentedDetailText, target, StringComparison.Ordinal))
        {
            return;
        }

        string previous = presentedDetailText;
        presentedDetailText = target;
        isFinalSensorDetailPresented = false;
        PlayDetailTransition(previous, detailMotionLevel);
    }

    private void PlayDetailTransition(string previous, MotionLevel level)
    {
        long generation = ++detailTransitionGeneration;
        ClearDetailClocks();
        PreviousDetailText.Text = previous;
        PreviousDetailText.Opacity = 0d;
        MilestoneDetail.Opacity = 1d;
        SetDetailTranslation(PreviousDetailText, 0d);
        SetDetailTranslation(MilestoneDetail, 0d);

        if (level == MotionLevel.Off || !IsLoaded || !IsVisible)
        {
            detailTransitionActive = false;
            PreviousDetailText.Text = string.Empty;
            NotifyDetailPresentationCompletedIfAuthorized();
            return;
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(level switch
        {
            MotionLevel.Full => FullDetailTransitionMilliseconds,
            MotionLevel.Standard => StandardDetailTransitionMilliseconds,
            _ => ReducedDetailTransitionMilliseconds
        });
        double offset = level switch
        {
            MotionLevel.Full => 3d,
            MotionLevel.Standard => 2d,
            _ => 0d
        };
        detailTransitionActive = true;
        PreviousDetailText.BeginAnimation(
            OpacityProperty,
            BuildDoubleAnimation(1d, 0d, TimeSpan.Zero, duration),
            HandoffBehavior.SnapshotAndReplace);
        DoubleAnimationUsingKeyFrames currentOpacity =
            BuildDoubleAnimation(0d, 1d, TimeSpan.Zero, duration);
        currentOpacity.Completed += (_, _) => CompleteDetailTransition(generation);
        MilestoneDetail.BeginAnimation(
            OpacityProperty,
            currentOpacity,
            HandoffBehavior.SnapshotAndReplace);
        if (offset > 0d)
        {
            AnimateDetailTranslation(PreviousDetailText, 0d, -offset, duration);
            AnimateDetailTranslation(MilestoneDetail, offset, 0d, duration);
        }
    }

    private void CompleteDetailTransition(long generation)
    {
        if (generation != detailTransitionGeneration)
        {
            return;
        }

        detailTransitionActive = false;
        ClearDetailClocks();
        PreviousDetailText.Text = string.Empty;
        PreviousDetailText.Opacity = 0d;
        MilestoneDetail.Opacity = 1d;
        SetDetailTranslation(PreviousDetailText, 0d);
        SetDetailTranslation(MilestoneDetail, 0d);
        NotifyDetailPresentationCompletedIfAuthorized();
    }

    private void NotifyDetailPresentationCompletedIfAuthorized()
    {
        if (!detailContextIsTerminal
            || detailTransitionActive
            || !detailInitialized
            || detailContextVersion < 0
            || lastDetailCompletionVersion == detailContextVersion
            || !string.Equals(
                presentedDetailText,
                expectedTerminalDetail,
                StringComparison.Ordinal)
            || !string.Equals(
                MilestoneDetail.Text ?? string.Empty,
                expectedTerminalDetail,
                StringComparison.Ordinal))
        {
            return;
        }

        isFinalSensorDetailPresented = true;
        lastDetailCompletionVersion = detailContextVersion;
        DetailPresentationCompleted?.Invoke(
            this,
            new DetailPresentationCompletion(
                detailContextVersion,
                detailTransitionGeneration,
                presentedDetailText));
    }

    private void ClearDetailClocks()
    {
        PreviousDetailText.BeginAnimation(OpacityProperty, null);
        MilestoneDetail.BeginAnimation(OpacityProperty, null);
        ClearDetailTranslation(PreviousDetailText);
        ClearDetailTranslation(MilestoneDetail);
    }

    private static void AnimateDetailTranslation(
        UIElement target,
        double from,
        double to,
        TimeSpan duration)
    {
        TranslateTransform transform = EnsureDetailTransform(target);
        transform.Y = to;
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            BuildDoubleAnimation(from, to, TimeSpan.Zero, duration),
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void SetDetailTranslation(UIElement target, double value)
    {
        TranslateTransform transform = EnsureDetailTransform(target);
        transform.Y = value;
    }

    private static void ClearDetailTranslation(UIElement target)
    {
        TranslateTransform transform = EnsureDetailTransform(target);
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0d;
        transform.Y = 0d;
    }

    private static TranslateTransform EnsureDetailTransform(UIElement target)
    {
        if (target.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        target.RenderTransform = transform;
        return transform;
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

    internal readonly record struct DetailPresentationCompletion(
        long SnapshotVersion,
        long TransitionGeneration,
        string Detail);
}
