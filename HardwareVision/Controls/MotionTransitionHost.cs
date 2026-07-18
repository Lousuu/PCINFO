using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;
using HardwareVision.Themes;

namespace HardwareVision.Controls;

public sealed class MotionTransitionHost : ContentControl
{
    public static readonly DependencyProperty IsTransitionEnabledProperty =
        DependencyProperty.Register(
            nameof(IsTransitionEnabled),
            typeof(bool),
            typeof(MotionTransitionHost),
            new PropertyMetadata(false, OnTransitionGateChanged));

    public static readonly DependencyProperty AnimateInitialContentProperty =
        DependencyProperty.Register(
            nameof(AnimateInitialContent),
            typeof(bool),
            typeof(MotionTransitionHost),
            new PropertyMetadata(false));

    public static readonly DependencyProperty TransitionDirectionProperty =
        DependencyProperty.Register(
            nameof(TransitionDirection),
            typeof(MotionTransitionDirection),
            typeof(MotionTransitionHost),
            new PropertyMetadata(MotionTransitionDirection.FromRight));

    private FrameworkElement? motionSurface;
    private TranslateTransform? translateTransform;
    private bool hasSeenContent;

    static MotionTransitionHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MotionTransitionHost),
            new FrameworkPropertyMetadata(typeof(MotionTransitionHost)));
    }

    public bool IsTransitionEnabled
    {
        get => (bool)GetValue(IsTransitionEnabledProperty);
        set => SetValue(IsTransitionEnabledProperty, value);
    }

    public bool AnimateInitialContent
    {
        get => (bool)GetValue(AnimateInitialContentProperty);
        set => SetValue(AnimateInitialContentProperty, value);
    }

    public MotionTransitionDirection TransitionDirection
    {
        get => (MotionTransitionDirection)GetValue(TransitionDirectionProperty);
        set => SetValue(TransitionDirectionProperty, value);
    }

    internal MotionTransitionPlan? LastTransitionPlan { get; private set; }

    public override void OnApplyTemplate()
    {
        CancelTransition();
        base.OnApplyTemplate();
        motionSurface = GetTemplateChild("MotionSurface") as FrameworkElement;
        translateTransform = GetTemplateChild("MotionTranslateTransform") as TranslateTransform;
        RestoreFinalState();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        CancelTransition();

        if (ReferenceEquals(oldContent, newContent))
        {
            return;
        }

        bool isInitialContent = !hasSeenContent;
        hasSeenContent = true;
        if (isInitialContent && !AnimateInitialContent)
        {
            LastTransitionPlan = MotionTransitionPlanFactory.Create(
                MotionContext.GetCurrentProfile(this),
                isTransitionEnabled: false,
                IsLoaded,
                IsVisible,
                IsHostWindowVisible(),
                TransitionDirection);
            return;
        }

        BeginCurrentTransition();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == MotionContext.CurrentProfileProperty
            || e.Property == MotionContext.IsAnimationEnabledProperty
            || e.Property == MotionContext.EffectiveLevelProperty)
        {
            MotionProfile profile = MotionContext.GetCurrentProfile(this);
            if (!profile.IsAnimationEnabled || profile.EffectiveLevel == MotionLevel.Off)
            {
                CancelTransition();
            }
        }
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        RestoreFinalState();
    }

    private static void OnTransitionGateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MotionTransitionHost host && e.NewValue is false)
        {
            host.CancelTransition();
        }
    }

    private void BeginCurrentTransition()
    {
        RestoreFinalState();
        MotionProfile profile = MotionContext.GetCurrentProfile(this);
        MotionTransitionPlan plan = MotionTransitionPlanFactory.Create(
            profile,
            IsTransitionEnabled,
            IsLoaded,
            IsVisible,
            IsHostWindowVisible(),
            TransitionDirection);
        LastTransitionPlan = plan;

        if (!plan.ShouldAnimate || motionSurface is null)
        {
            RestoreFinalState();
            return;
        }

        if (plan.AnimatesOpacity)
        {
            DoubleAnimation opacityAnimation = new()
            {
                From = plan.StartOpacity,
                To = 1d,
                Duration = new Duration(plan.Duration),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            motionSurface.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        }

        if (plan.AnimatesTranslation && translateTransform is not null)
        {
            DependencyProperty property = plan.Direction == MotionTransitionDirection.FromBottom
                ? TranslateTransform.YProperty
                : TranslateTransform.XProperty;
            DoubleAnimation offsetAnimation = new()
            {
                From = plan.Offset,
                To = 0d,
                Duration = new Duration(plan.Duration),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            translateTransform.BeginAnimation(property, offsetAnimation, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void CancelTransition()
    {
        if (motionSurface is not null)
        {
            motionSurface.BeginAnimation(OpacityProperty, null);
        }

        if (translateTransform is not null)
        {
            translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
        }

        RestoreFinalState();
    }

    private void RestoreFinalState()
    {
        if (motionSurface is not null)
        {
            motionSurface.Opacity = 1d;
        }

        if (translateTransform is not null)
        {
            translateTransform.X = 0d;
            translateTransform.Y = 0d;
        }
    }

    private bool IsHostWindowVisible()
    {
        Window? window = Window.GetWindow(this);
        return window is null || window.IsVisible;
    }
}
