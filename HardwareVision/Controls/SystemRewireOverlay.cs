using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Controls;

public sealed class SystemRewireOverlay : System.Windows.Controls.Control
{
    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(
            nameof(Snapshot),
            typeof(ThemeTransitionSnapshot),
            typeof(SystemRewireOverlay),
            new PropertyMetadata(null, OnSnapshotChanged));

    private FrameworkElement? overlayRoot;
    private FrameworkElement? rewireLabel;
    private FrameworkElement? sourceNode;
    private FrameworkElement? targetNode;
    private FrameworkElement? spliceSegment;
    private TranslateTransform? sourceTranslateTransform;
    private TranslateTransform? targetTranslateTransform;

    static SystemRewireOverlay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SystemRewireOverlay),
            new FrameworkPropertyMetadata(typeof(SystemRewireOverlay)));
    }

    public SystemRewireOverlay()
    {
        Focusable = false;
        IsTabStop = false;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        Opacity = 0d;
    }

    public ThemeTransitionSnapshot? Snapshot
    {
        get => (ThemeTransitionSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    internal ThemeTransitionPlan? LastOverlayPlan { get; private set; }

    internal ThemeTransitionPhase LastPhase { get; private set; } = ThemeTransitionPhase.Idle;

    internal bool LastUsedSpatialMotion { get; private set; }

    public override void OnApplyTemplate()
    {
        StopAnimations();
        base.OnApplyTemplate();
        overlayRoot = GetTemplateChild("PART_OverlayRoot") as FrameworkElement;
        rewireLabel = GetTemplateChild("PART_RewireLabel") as FrameworkElement;
        sourceNode = GetTemplateChild("PART_SourceNode") as FrameworkElement;
        targetNode = GetTemplateChild("PART_TargetNode") as FrameworkElement;
        spliceSegment = GetTemplateChild("PART_SpliceSegment") as FrameworkElement;
        sourceTranslateTransform = GetTemplateChild("PART_SourceTranslateTransform") as TranslateTransform;
        targetTranslateTransform = GetTemplateChild("PART_TargetTranslateTransform") as TranslateTransform;
        ApplySnapshot(Snapshot, animate: false);
    }

    private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SystemRewireOverlay overlay)
        {
            overlay.ApplySnapshot(e.NewValue as ThemeTransitionSnapshot, animate: true);
        }
    }

    private void ApplySnapshot(ThemeTransitionSnapshot? snapshot, bool animate)
    {
        LastPhase = snapshot?.Phase ?? ThemeTransitionPhase.Idle;
        LastOverlayPlan = snapshot?.Plan;
        LastUsedSpatialMotion = snapshot?.Plan.AllowsTraceTranslation == true;

        bool active = snapshot?.IsActive == true && snapshot.Plan.IsOverlayEnabled;
        IsHitTestVisible = snapshot?.IsInteractionBlocked == true;
        Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active)
        {
            StopAnimations();
            Opacity = 0d;
            return;
        }

        if (rewireLabel is not null)
        {
            rewireLabel.Visibility = snapshot!.Plan.ShowsSystemRewireLabel
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        double targetOpacity = snapshot!.Plan.BackdropTargetOpacity;
        Opacity = targetOpacity;
        if (animate)
        {
            BeginOpacityAnimation(targetOpacity, snapshot.Phase, snapshot.Plan);
            BeginTraceAnimation(snapshot.Phase, snapshot.Plan);
        }
        else
        {
            SetNodeOffsets(0d, 0d);
        }
    }

    private void BeginOpacityAnimation(double targetOpacity, ThemeTransitionPhase phase, ThemeTransitionPlan plan)
    {
        if (plan.EffectiveLevel == MotionLevel.Off)
        {
            return;
        }

        TimeSpan duration = phase switch
        {
            ThemeTransitionPhase.Trace => plan.TraceDuration,
            ThemeTransitionPhase.Latch => plan.LatchDuration,
            ThemeTransitionPhase.Splice => plan.SpliceDuration,
            _ => TimeSpan.FromMilliseconds(40)
        };

        DoubleAnimation animation = new()
        {
            From = phase == ThemeTransitionPhase.Trace ? 0d : null,
            To = targetOpacity,
            Duration = new Duration(duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : duration),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void BeginTraceAnimation(ThemeTransitionPhase phase, ThemeTransitionPlan plan)
    {
        if (!plan.AllowsTraceTranslation)
        {
            SetNodeOffsets(0d, 0d);
            return;
        }

        double sourceFrom = phase == ThemeTransitionPhase.Trace ? -plan.TraceOffset : 0d;
        double targetFrom = phase == ThemeTransitionPhase.Trace ? plan.TraceOffset : 0d;
        TimeSpan duration = phase == ThemeTransitionPhase.Splice
            ? plan.SpliceDuration
            : plan.TraceDuration;

        AnimateTranslate(sourceTranslateTransform, sourceFrom, 0d, duration);
        AnimateTranslate(targetTranslateTransform, targetFrom, 0d, duration);

        if (spliceSegment is not null)
        {
            double segmentOpacity = phase == ThemeTransitionPhase.Splice ? 1d : 0.62d;
            spliceSegment.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation
                {
                    To = segmentOpacity,
                    Duration = new Duration(duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : duration),
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                },
                HandoffBehavior.SnapshotAndReplace);
            spliceSegment.Opacity = segmentOpacity;
        }
    }

    private static void AnimateTranslate(TranslateTransform? transform, double from, double to, TimeSpan duration)
    {
        if (transform is null)
        {
            return;
        }

        DoubleAnimation animation = new()
        {
            From = from,
            To = to,
            Duration = new Duration(duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : duration),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
        transform.X = to;
    }

    private void SetNodeOffsets(double sourceOffset, double targetOffset)
    {
        if (sourceTranslateTransform is not null)
        {
            sourceTranslateTransform.X = sourceOffset;
        }

        if (targetTranslateTransform is not null)
        {
            targetTranslateTransform.X = targetOffset;
        }
    }

    private void StopAnimations()
    {
        BeginAnimation(OpacityProperty, null);
        if (overlayRoot is not null)
        {
            overlayRoot.BeginAnimation(OpacityProperty, null);
        }

        if (sourceTranslateTransform is not null)
        {
            sourceTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        if (targetTranslateTransform is not null)
        {
            targetTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        if (spliceSegment is not null)
        {
            spliceSegment.BeginAnimation(OpacityProperty, null);
        }
    }
}
