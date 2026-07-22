using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Themes;
using FlowRelayDirection = HardwareVision.Models.NavigationTransitionDirection;
using FlowRelayPlan = HardwareVision.Models.NavigationTransitionPlan;

namespace HardwareVision.Controls;

public sealed class MotionTransitionHost : ContentControl
{
    public static readonly DependencyProperty IsAutoTransitionEnabledProperty =
        DependencyProperty.Register(
            nameof(IsAutoTransitionEnabled),
            typeof(bool),
            typeof(MotionTransitionHost),
            new PropertyMetadata(true, OnAutoTransitionChanged));

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
    private bool isUnloaded;
    private bool replayScheduled;
    private object? pendingContent;
    private readonly List<FrameworkElement> animatedModules = [];

    static MotionTransitionHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MotionTransitionHost),
            new FrameworkPropertyMetadata(typeof(MotionTransitionHost)));
    }

    public MotionTransitionHost()
    {
        ClipToBounds = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public bool IsTransitionEnabled
    {
        get => (bool)GetValue(IsTransitionEnabledProperty);
        set => SetValue(IsTransitionEnabledProperty, value);
    }

    public bool IsAutoTransitionEnabled
    {
        get => (bool)GetValue(IsAutoTransitionEnabledProperty);
        set => SetValue(IsAutoTransitionEnabledProperty, value);
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

    internal int TransitionExecutionCount { get; private set; }

    internal bool PendingTransition => pendingContent is not null;

    internal string? LastSkipReason { get; private set; }

    public override void OnApplyTemplate()
    {
        CancelTransition();
        base.OnApplyTemplate();
        motionSurface = GetTemplateChild("MotionSurface") as FrameworkElement;
        translateTransform = GetTemplateChild("MotionTranslateTransform") as TranslateTransform;
        RestoreFinalState();
        SchedulePendingReplay();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        CancelTransition();

        if (ReferenceEquals(oldContent, newContent))
        {
            LastSkipReason = "SameContent";
            return;
        }

        if (!IsAutoTransitionEnabled)
        {
            hasSeenContent = true;
            pendingContent = null;
            LastSkipReason = "AutoTransitionDisabled";
            return;
        }

        bool isInitialContent = !hasSeenContent;
        hasSeenContent = true;
        if (isInitialContent && !AnimateInitialContent)
        {
            LastSkipReason = "InitialContent";
            LastTransitionPlan = MotionTransitionPlanFactory.Create(
                MotionContext.GetCurrentProfile(this),
                isTransitionEnabled: false,
                IsLoaded,
                IsVisible,
                IsHostWindowVisible(),
                TransitionDirection);
            return;
        }

        BeginCurrentTransition(newContent, scheduleIfNotReady: true);
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
                pendingContent = null;
                LastSkipReason = "MotionOff";
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
            host.pendingContent = null;
            host.LastSkipReason = "TransitionDisabled";
            host.CancelTransition();
        }
    }

    private static void OnAutoTransitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MotionTransitionHost host && e.NewValue is false)
        {
            host.pendingContent = null;
            host.LastSkipReason = "AutoTransitionDisabled";
            host.CancelTransition();
        }
    }

    private void BeginCurrentTransition(object? content, bool scheduleIfNotReady)
    {
        RestoreFinalState();
        MotionProfile profile = MotionContext.GetCurrentProfile(this);
        if (!IsTransitionEnabled)
        {
            pendingContent = null;
            LastSkipReason = "TransitionDisabled";
            LastTransitionPlan = MotionTransitionPlanFactory.Create(
                profile,
                isTransitionEnabled: false,
                IsLoaded,
                IsVisible,
                IsHostWindowVisible(),
                TransitionDirection);
            return;
        }

        if (!profile.IsAnimationEnabled || profile.EffectiveLevel == MotionLevel.Off)
        {
            pendingContent = null;
            LastSkipReason = "MotionOff";
            LastTransitionPlan = MotionTransitionPlanFactory.Create(
                profile,
                isTransitionEnabled: true,
                IsLoaded,
                IsVisible,
                IsHostWindowVisible(),
                TransitionDirection);
            return;
        }

        if (isUnloaded)
        {
            pendingContent = null;
            LastSkipReason = "Unloaded";
            return;
        }

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
            pendingContent = content;
            LastSkipReason = motionSurface is null ? "TemplateNotReady" : "HostNotReady";
            RestoreFinalState();
            if (scheduleIfNotReady)
            {
                SchedulePendingReplay();
            }

            return;
        }

        pendingContent = null;
        LastSkipReason = null;
        TransitionExecutionCount++;
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
            opacityAnimation.Completed += (_, _) => RestoreFinalState();
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
            offsetAnimation.Completed += (_, _) => RestoreFinalState();
            translateTransform.BeginAnimation(property, offsetAnimation, HandoffBehavior.SnapshotAndReplace);
        }
    }

    public void PlaySettle(FlowRelayPlan plan, FlowRelayDirection direction)
    {
        ArgumentNullException.ThrowIfNull(plan);
        CancelTransition();
        LastTransitionPlan = null;
        if (!plan.UsesClock || plan.EffectiveLevel == MotionLevel.Off)
        {
            LastSkipReason = "MotionOff";
            return;
        }

        _ = Dispatcher.BeginInvoke(
            new Action(() => PlayExplicitSettle(plan, direction)),
            DispatcherPriority.Loaded);
    }

    public void CancelTransition()
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

        foreach (FrameworkElement module in animatedModules)
        {
            module.BeginAnimation(OpacityProperty, null);
            if (module.RenderTransform is TranslateTransform moduleTranslate)
            {
                moduleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                moduleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                moduleTranslate.X = 0d;
                moduleTranslate.Y = 0d;
            }
            module.Opacity = 1d;
        }
        animatedModules.Clear();

        RestoreFinalState();
    }

    public void RestoreFinalState()
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

        foreach (FrameworkElement module in animatedModules)
        {
            module.BeginAnimation(OpacityProperty, null);
            module.Opacity = 1d;
            if (module.RenderTransform is TranslateTransform moduleTranslate)
            {
                moduleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                moduleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                moduleTranslate.X = 0d;
                moduleTranslate.Y = 0d;
            }
        }
        animatedModules.Clear();

    }

    private void PlayExplicitSettle(
        FlowRelayPlan plan,
        FlowRelayDirection direction)
    {
        RestoreFinalState();
        if (motionSurface is null || !IsLoaded || !IsVisible)
        {
            LastSkipReason = "HostNotReady";
            return;
        }

        LastSkipReason = null;
        TransitionExecutionCount++;
        TimeSpan duration = plan.SettleDuration;
        DoubleAnimation opacityAnimation = new()
        {
            From = plan.PageStartOpacity,
            To = 1d,
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        opacityAnimation.Completed += (_, _) => RestoreFinalState();
        motionSurface.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);

        if (plan.AllowsPageTranslation && plan.PageSettleOffset > 0d && translateTransform is not null)
        {
            (DependencyProperty property, double offset) = ResolveTranslation(direction, plan.PageSettleOffset);
            DoubleAnimation translation = new()
            {
                From = offset,
                To = 0d,
                Duration = new Duration(duration),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            translation.Completed += (_, _) => RestoreFinalState();
            translateTransform.BeginAnimation(property, translation, HandoffBehavior.SnapshotAndReplace);
        }

        if (plan.AllowsModuleStagger)
        {
            PlayModuleSettle(plan, direction);
        }
    }

    private void PlayModuleSettle(FlowRelayPlan plan, FlowRelayDirection direction)
    {
        FrameworkElement? primary = FindRoleElement(motionSurface, NavigationMotionRole.Primary);
        FrameworkElement? secondary = FindRoleElement(motionSurface, NavigationMotionRole.Secondary);
        double startOpacity = plan.EffectiveLevel == MotionLevel.Full ? 0.78d : 0.84d;
        double offset = plan.EffectiveLevel == MotionLevel.Full ? 4d : 3d;
        AnimateModule(primary, plan.PrimaryModuleDelay, startOpacity, offset, plan, direction);
        AnimateModule(secondary, plan.SecondaryModuleDelay, startOpacity, offset, plan, direction);
    }

    private void AnimateModule(
        FrameworkElement? module,
        TimeSpan delay,
        double startOpacity,
        double offset,
        FlowRelayPlan plan,
        FlowRelayDirection direction)
    {
        if (module is null)
        {
            return;
        }

        TranslateTransform transform = module.RenderTransform as TranslateTransform ?? new TranslateTransform();
        module.RenderTransform = transform;
        animatedModules.Add(module);
        Duration duration = new(plan.SettleDuration - delay > TimeSpan.Zero
            ? plan.SettleDuration - delay
            : TimeSpan.Zero);
        module.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = startOpacity,
            To = 1d,
            BeginTime = delay,
            Duration = duration,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);

        (DependencyProperty property, double signedOffset) = ResolveTranslation(direction, offset);
        transform.BeginAnimation(property, new DoubleAnimation
        {
            From = signedOffset,
            To = 0d,
            BeginTime = delay,
            Duration = duration,
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static (DependencyProperty Property, double Offset) ResolveTranslation(
        FlowRelayDirection direction,
        double offset) => direction switch
        {
            FlowRelayDirection.FromTop => (TranslateTransform.YProperty, -offset),
            FlowRelayDirection.FromBottom => (TranslateTransform.YProperty, offset),
            FlowRelayDirection.FromLeft => (TranslateTransform.XProperty, -offset),
            _ => (TranslateTransform.XProperty, offset)
        };

    private static FrameworkElement? FindRoleElement(DependencyObject? root, NavigationMotionRole role)
    {
        if (root is null)
        {
            return null;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && NavigationMotion.GetRole(element) == role)
            {
                return element;
            }

            FrameworkElement? nested = FindRoleElement(child, role);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private bool IsHostWindowVisible()
    {
        Window? window = Window.GetWindow(this);
        return window is null || window.IsVisible;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        isUnloaded = false;
        SchedulePendingReplay();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        isUnloaded = true;
        pendingContent = null;
        replayScheduled = false;
        LastSkipReason = "Unloaded";
        CancelTransition();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            SchedulePendingReplay();
        }
    }

    private void SchedulePendingReplay()
    {
        if (pendingContent is null || replayScheduled || isUnloaded)
        {
            return;
        }

        replayScheduled = true;
        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                replayScheduled = false;
                TryReplayPendingTransition();
            }),
            DispatcherPriority.Loaded);
    }

    private void TryReplayPendingTransition()
    {
        if (pendingContent is null || isUnloaded)
        {
            return;
        }

        object content = pendingContent;
        BeginCurrentTransition(content, scheduleIfNotReady: false);
    }
}
