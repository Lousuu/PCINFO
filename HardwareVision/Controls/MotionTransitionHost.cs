using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Themes;
using HardwareVision.Utilities;
using FlowRelayDirection = HardwareVision.Models.NavigationTransitionDirection;
using FlowRelayPlan = HardwareVision.Models.NavigationTransitionPlan;

namespace HardwareVision.Controls;

internal enum MotionTransitionLifecycleState
{
    Idle,
    Prepared,
    Exiting,
    Committed,
    Entering,
    Finalizing,
    Cancelled
}

internal sealed record MotionRuntimeDiagnostic(
    string EventName,
    TimeSpan RelativeTime,
    long NavigationVersion,
    long StartupGeneration,
    MotionTransitionLifecycleState State,
    string Reason,
    double RootOpacity,
    double PrimaryOpacity,
    double SecondaryOpacity,
    double RootTranslateX,
    double RootTranslateY,
    double HostWidth,
    double HostHeight);

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
    private CachedPagePresenter? pagePresenter;
    private TranslateTransform? translateTransform;
    private bool hasSeenContent;
    private bool isUnloaded;
    private bool replayScheduled;
    private object? pendingContent;
    private readonly List<FrameworkElement> animatedModules = [];
    private bool explicitSettleActive;
    private FrameworkElement? cachedPrimary;
    private FrameworkElement? cachedSecondary;
    private object? cachedRoleContent;
    private FrameworkElement? outgoingRoot;
    private FrameworkElement? outgoingPrimary;
    private FrameworkElement? outgoingSecondary;
    private FlowRelayPlan? explicitPlan;
    private FlowRelayDirection explicitDirection;
    private long explicitNavigationVersion = -1;
    private bool explicitContentCommitted;
    private long startupRevealGeneration;
    private long contentGeneration;
    private bool navigationCompletionRequested;
    private bool navigationEnterCompleted;
    private bool navigationExitCompleted;
    private bool navigationEnterStarted;
    private int pendingEnterAnimations;
    private long roleResolutionScheduledVersion = -1;
    private long finalizedNavigationVersion = -1;
    private bool startupRevealPrepared;
    private bool startupRevealStarted;
    private int pendingStartupRevealAnimations;
    private MotionLevel startupRevealLevel = MotionLevel.Off;
    private MotionTransitionLifecycleState lifecycleState =
        MotionTransitionLifecycleState.Idle;
    private readonly System.Diagnostics.Stopwatch diagnosticClock =
        System.Diagnostics.Stopwatch.StartNew();
    private readonly List<MotionRuntimeDiagnostic> diagnostics = [];

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
        SizeChanged += OnHostSizeChanged;
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

    internal MotionTransitionLifecycleState LifecycleState => lifecycleState;

    internal long ActiveNavigationVersion => explicitNavigationVersion;

    internal long ContentGeneration => contentGeneration;

    internal long StartupRevealGeneration => startupRevealGeneration;

    internal IReadOnlyList<MotionRuntimeDiagnostic> Diagnostics => diagnostics;

    internal FrameworkElement? ActiveRoot =>
        explicitNavigationVersion >= 0
            ? pagePresenter?.PresentedRoot ?? motionSurface
            : motionSurface;

    internal FrameworkElement? ActivePrimary => cachedPrimary;

    internal FrameworkElement? ActiveSecondary => cachedSecondary;

    internal bool IsStartupRevealPrepared => startupRevealPrepared;

    internal event EventHandler? StartupRevealVisualCompleted;

    internal bool IsNavigationExitCompleted => navigationExitCompleted;

    internal bool IsNavigationContentCommitted => explicitContentCommitted;

    internal bool IsNavigationEnterStarted => navigationEnterStarted;

    internal bool IsNavigationEnterCompleted => navigationEnterCompleted;

    public override void OnApplyTemplate()
    {
        CancelAllMotion("TemplateReplaced");
        if (pagePresenter is not null)
        {
            pagePresenter.ContentPresented -= OnPageContentPresented;
        }
        base.OnApplyTemplate();
        motionSurface = GetTemplateChild("MotionSurface") as FrameworkElement;
        pagePresenter =
            GetTemplateChild("PagePresenter") as CachedPagePresenter;
        if (pagePresenter is not null)
        {
            pagePresenter.ContentPresented += OnPageContentPresented;
        }
        translateTransform = GetTemplateChild("MotionTranslateTransform") as TranslateTransform;
        RestoreFinalState();
        SchedulePendingReplay();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        contentGeneration++;
        if (explicitNavigationVersion >= 0)
        {
            hasSeenContent = true;
            pendingContent = null;
            explicitContentCommitted = true;
            cachedRoleContent = null;
            lifecycleState = MotionTransitionLifecycleState.Committed;
            EmitDiagnostic("PageContentCommitted", "NextRender");
            ScheduleCommittedTemplateDiagnostics(explicitNavigationVersion);
            LastSkipReason = "ExplicitPageCommit";
            return;
        }

        CancelNavigationTransition("AutoContentChanged");

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
                CancelAllMotion("MotionOff");
            }
        }
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualParent is null)
        {
            CancelAllMotion("VisualParentDetached");
        }
    }

    private static void OnTransitionGateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MotionTransitionHost host && e.NewValue is false)
        {
            host.pendingContent = null;
            host.LastSkipReason = "TransitionDisabled";
            host.CancelNavigationTransition("TransitionDisabled");
        }
    }

    private static void OnAutoTransitionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MotionTransitionHost host && e.NewValue is false)
        {
            host.pendingContent = null;
            host.LastSkipReason = "AutoTransitionDisabled";
            host.CancelNavigationTransition("AutoTransitionDisabled");
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

    public void PrepareNavigation(
        FlowRelayPlan plan,
        FlowRelayDirection direction,
        long version)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (version < explicitNavigationVersion)
        {
            return;
        }

        CancelNavigationTransition("Superseded");
        explicitPlan = plan;
        explicitDirection = direction;
        explicitNavigationVersion = version;
        explicitContentCommitted = false;
        navigationCompletionRequested = false;
        navigationEnterCompleted = false;
        navigationExitCompleted = false;
        navigationEnterStarted = false;
        pendingEnterAnimations = 0;
        finalizedNavigationVersion = -1;
        lifecycleState = MotionTransitionLifecycleState.Prepared;
        pagePresenter?.BeginTransition(version);
        ResolveRoleCache();
        outgoingRoot = pagePresenter?.OutgoingRoot;
        outgoingPrimary = cachedPrimary;
        outgoingSecondary = cachedSecondary;
        _ = EnsureModuleTranslate(outgoingRoot);
        _ = EnsureModuleTranslate(outgoingPrimary);
        _ = EnsureModuleTranslate(outgoingSecondary);
        IsHitTestVisible = false;
        EmitDiagnostic("NavigationPrepared", "Route");
    }

    public void PlayExit(
        FlowRelayPlan plan,
        FlowRelayDirection direction,
        long version)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (version != explicitNavigationVersion
            || outgoingRoot is null)
        {
            return;
        }

        explicitPlan = plan;
        explicitDirection = direction;
        lifecycleState = MotionTransitionLifecycleState.Exiting;
        EmitDiagnostic("PageExitStarted", "Shift");
        IsHitTestVisible = false;
        FrameworkElement exitingRoot = outgoingRoot;
        TrackAnimatedElement(exitingRoot);
        AnimateExitElement(
            exitingRoot,
            EnsureModuleTranslate(exitingRoot),
            TimeSpan.Zero,
            plan.PageExitDuration,
            plan.PageExitOpacity,
            plan.PageExitOffset,
            direction,
            () => OnOutgoingExitCompleted(version, exitingRoot));
        if (plan.AllowsRoleStagger)
        {
            TrackAnimatedElement(outgoingSecondary);
            AnimateExitElement(
                outgoingSecondary,
                EnsureModuleTranslate(outgoingSecondary),
                plan.SecondaryExitDelay,
                plan.SecondaryExitDuration,
                plan.SecondaryCommitOpacity,
                plan.SecondaryExitOffset,
                direction);
            TrackAnimatedElement(outgoingPrimary);
            AnimateExitElement(
                outgoingPrimary,
                EnsureModuleTranslate(outgoingPrimary),
                plan.PrimaryExitDelay,
                plan.PrimaryExitDuration,
                plan.PrimaryCommitOpacity,
                plan.PrimaryExitOffset,
                direction);
        }
        QueueDiagnostic(
            "PageExitFirstRender",
            "RenderPriority",
            TimeSpan.Zero,
            version);
        QueueDiagnostic(
            "PageExitSampled",
            "ShiftSample",
            TimeSpan.FromMilliseconds(Math.Max(1d, plan.PageExitDuration.TotalMilliseconds / 2d)),
            version);
    }

    public void PrepareCommittedContent(
        FlowRelayPlan plan,
        FlowRelayDirection direction,
        long version)
    {
        if (version != explicitNavigationVersion)
        {
            return;
        }

        explicitPlan = plan;
        explicitDirection = direction;
        EmitDiagnostic("DecorativeRelayCommitted", "Relay");
    }

    public void PlayEnter(
        FlowRelayPlan plan,
        FlowRelayDirection direction,
        long version)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (version != explicitNavigationVersion)
        {
            return;
        }

        if (navigationEnterStarted)
        {
            return;
        }

        LastTransitionPlan = null;
        if (!plan.UsesClock || plan.EffectiveLevel == MotionLevel.Off)
        {
            LastSkipReason = "MotionOff";
            CompleteNavigation(version);
            return;
        }

        lifecycleState = MotionTransitionLifecycleState.Entering;
        _ = Dispatcher.BeginInvoke(
            new Action(() => PlayExplicitSettle(plan, direction, version)),
            DispatcherPriority.Loaded);
    }

    public void PlaySettle(FlowRelayPlan plan, FlowRelayDirection direction)
    {
        long version = explicitNavigationVersion >= 0
            ? explicitNavigationVersion
            : 0;
        if (explicitNavigationVersion < 0)
        {
            PrepareNavigation(plan, direction, version);
            explicitContentCommitted = true;
        }
        PlayEnter(plan, direction, version);
    }

    public void CompleteNavigation(long version)
    {
        if (version != explicitNavigationVersion)
        {
            return;
        }

        navigationCompletionRequested = true;
        if (!navigationEnterCompleted && pendingEnterAnimations > 0)
        {
            return;
        }

        FinalizeNavigation(version);
    }

    private void FinalizeNavigation(long version)
    {
        if (version != explicitNavigationVersion
            || finalizedNavigationVersion == version)
        {
            return;
        }

        finalizedNavigationVersion = version;
        lifecycleState = MotionTransitionLifecycleState.Finalizing;
        pagePresenter?.EndTransition(version);
        EmitDiagnostic("NavigationFinalized", "AllEnterClocksCompleted");
        RestoreVisualFinalState();
        IsHitTestVisible = true;
        EmitDiagnostic("DeferredWorkStarted", "ContextIdleCleanup");
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (version != explicitNavigationVersion)
                {
                    return;
                }

                explicitPlan = null;
                explicitNavigationVersion = -1;
                explicitContentCommitted = false;
                cachedRoleContent = null;
                cachedPrimary = null;
                cachedSecondary = null;
                outgoingRoot = null;
                outgoingPrimary = null;
                outgoingSecondary = null;
                lifecycleState = MotionTransitionLifecycleState.Idle;
                EmitDiagnostic("DeferredWorkCompleted", "ContextIdleCleanup");
            }));
    }

    public void CancelNavigationTransitionForStartup() =>
        CancelNavigationTransition("StartupTakeover");

    public void CancelNavigationTransitionForTheme() =>
        CancelNavigationTransition("ThemeTakeover");

    public void CancelAllMotionForUnload() =>
        CancelAllMotion("Unload");

    public void CancelTransition() => CancelAllMotion("ExplicitCancel");

    public void CancelNavigationTransition(string reason)
    {
        bool hadNavigation = explicitNavigationVersion >= 0
            || lifecycleState is MotionTransitionLifecycleState.Prepared
                or MotionTransitionLifecycleState.Exiting
                or MotionTransitionLifecycleState.Committed
                or MotionTransitionLifecycleState.Entering
                or MotionTransitionLifecycleState.Finalizing;
        if (hadNavigation)
        {
            lifecycleState = MotionTransitionLifecycleState.Cancelled;
            EmitDiagnostic("NavigationCancelReason", reason);
            EmitDiagnostic("NavigationCancelled", reason);
        }

        explicitSettleActive = false;
        pendingEnterAnimations = 0;
        navigationCompletionRequested = false;
        navigationEnterCompleted = false;
        navigationExitCompleted = false;
        navigationEnterStarted = false;
        if (motionSurface is not null)
        {
            motionSurface.BeginAnimation(OpacityProperty, null);
            motionSurface.Clip = null;
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

        pagePresenter?.EndTransition(explicitNavigationVersion);
        RestoreFinalState();
        explicitPlan = null;
        explicitNavigationVersion = -1;
        explicitContentCommitted = false;
        cachedRoleContent = null;
        cachedPrimary = null;
        cachedSecondary = null;
        outgoingRoot = null;
        outgoingPrimary = null;
        outgoingSecondary = null;
        if (!startupRevealPrepared)
        {
            lifecycleState = MotionTransitionLifecycleState.Idle;
        }
    }

    public void CancelStartupReveal(string reason)
    {
        if (startupRevealPrepared || startupRevealStarted)
        {
            EmitDiagnostic("StartupCleanupStarted", reason);
        }
        startupRevealGeneration++;
        startupRevealPrepared = false;
        startupRevealStarted = false;
        startupRevealLevel = MotionLevel.Off;
        RestoreVisualFinalState();
        IsHitTestVisible = true;
        EmitDiagnostic("StartupCleanupCompleted", reason);
    }

    private void CancelAllMotion(string reason)
    {
        CancelNavigationTransition(reason);
        CancelStartupReveal(reason);
    }

    public void RestoreFinalState()
    {
        RestoreVisualFinalState();
    }

    private void RestoreVisualFinalState()
    {
        explicitSettleActive = false;
        if (motionSurface is not null)
        {
            motionSurface.BeginAnimation(OpacityProperty, null);
            motionSurface.Opacity = 1d;
            motionSurface.Clip = null;
        }

        if (translateTransform is not null)
        {
            translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
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
        FlowRelayDirection direction,
        long version)
    {
        FrameworkElement? incomingRoot = pagePresenter?.PresentedRoot;
        if (version != explicitNavigationVersion
            || navigationEnterStarted
            || incomingRoot is null
            || !IsLoaded
            || !IsVisible)
        {
            LastSkipReason = "HostNotReady";
            return;
        }

        ResolveRoleCache();
        ApplyCommittedBaseState();
        LastSkipReason = null;
        TransitionExecutionCount++;
        explicitSettleActive = true;
        navigationEnterStarted = true;
        navigationEnterCompleted = false;
        pendingEnterAnimations = 1
            + (plan.AllowsRoleStagger && cachedPrimary is not null ? 1 : 0)
            + (plan.AllowsRoleStagger && cachedSecondary is not null ? 1 : 0);
        lifecycleState = MotionTransitionLifecycleState.Entering;
        EmitDiagnostic("PageEnterStarted", "Settle");
        TimeSpan duration = plan.PageEnterDuration;
        TranslateTransform? incomingTransform = EnsureModuleTranslate(incomingRoot);
        TrackAnimatedElement(incomingRoot);
        incomingRoot.Opacity = 1d;
        if (incomingTransform is not null)
        {
            incomingTransform.X = 0d;
            incomingTransform.Y = 0d;
        }
        DoubleAnimation opacityAnimation = new()
        {
            From = plan.PageStartOpacity,
            To = 1d,
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        opacityAnimation.Completed += (_, _) => OnEnterAnimationCompleted(version);
        incomingRoot.BeginAnimation(OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);

        if (plan.AllowsPageTranslation && plan.PageSettleOffset > 0d && incomingTransform is not null)
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
            incomingTransform.BeginAnimation(property, translation, HandoffBehavior.SnapshotAndReplace);
        }

        if (plan.AllowsRoleStagger)
        {
            PlayModuleSettle(plan, direction, version);
        }

        QueueDiagnostic(
            "PageEnterSampled",
            "SettleSample",
            TimeSpan.FromMilliseconds(Math.Max(1d, duration.TotalMilliseconds / 2d)),
            version);
        _ = RestoreInteractionAfterThresholdAsync(
            version,
            plan.PageStartOpacity,
            duration);
    }

    private void PlayModuleSettle(
        FlowRelayPlan plan,
        FlowRelayDirection direction,
        long version)
    {
        AnimateModule(
            cachedPrimary,
            plan.PrimaryEnterDelay,
            plan.PrimaryEnterDuration,
            plan.PrimaryModuleStartOpacity,
            plan.PrimaryModuleOffset,
            direction,
            version);
        AnimateModule(
            cachedSecondary,
            plan.SecondaryEnterDelay,
            plan.SecondaryEnterDuration,
            plan.SecondaryModuleStartOpacity,
            plan.SecondaryModuleOffset,
            direction,
            version);
    }

    private void AnimateModule(
        FrameworkElement? module,
        TimeSpan delay,
        TimeSpan enterDuration,
        double startOpacity,
        double offset,
        FlowRelayDirection direction,
        long version)
    {
        if (module is null)
        {
            return;
        }

        TranslateTransform transform = module.RenderTransform as TranslateTransform ?? new TranslateTransform();
        module.RenderTransform = transform;
        TrackAnimatedElement(module);
        module.Opacity = 1d;
        transform.X = 0d;
        transform.Y = 0d;
        DoubleAnimationUsingKeyFrames opacityAnimation = BuildDelayedAnimation(
            startOpacity,
            1d,
            delay,
            enterDuration,
            EasingMode.EaseOut);
        opacityAnimation.Completed += (_, _) => OnEnterAnimationCompleted(version);
        module.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);

        (DependencyProperty property, double signedOffset) = ResolveTranslation(direction, offset);
        transform.BeginAnimation(
            property,
            BuildDelayedAnimation(
                signedOffset,
                0d,
                delay,
                enterDuration,
                EasingMode.EaseOut),
            HandoffBehavior.SnapshotAndReplace);
    }

    public void PrepareStartupReveal(MotionLevel level)
    {
        if (startupRevealPrepared && startupRevealLevel == level)
        {
            EmitDiagnostic("StartupRevealStatePreserved", "ActiveSnapshot");
            return;
        }

        startupRevealGeneration++;
        startupRevealPrepared = true;
        startupRevealStarted = false;
        startupRevealLevel = level;
        ResolveRoleCache();
        if (motionSurface is null || level == MotionLevel.Off)
        {
            RestoreVisualFinalState();
            IsHitTestVisible = true;
            EmitDiagnostic("StartupRevealPrepared", "MotionOff");
            return;
        }

        (double rootOpacity, double primaryOpacity, double secondaryOpacity,
            double rootOffset, double primaryOffset, double secondaryOffset) = level switch
        {
            MotionLevel.Full => (0.32d, 0.42d, 0.24d, 8d, 6d, 10d),
            MotionLevel.Standard => (0.38d, 0.46d, 0.30d, 6d, 4d, 7d),
            _ => (0d, 1d, 1d, 0d, 0d, 0d)
        };
        SetElementBase(motionSurface, translateTransform, rootOpacity, rootOffset);
        SetElementBase(
            cachedPrimary,
            EnsureModuleTranslate(cachedPrimary),
            primaryOpacity,
            primaryOffset);
        SetElementBase(
            cachedSecondary,
            EnsureModuleTranslate(cachedSecondary),
            secondaryOpacity,
            secondaryOffset);
        IsHitTestVisible = false;
        EmitDiagnostic("StartupRevealPrepared", level.ToString());
    }

    public void PlayStartupReveal(MotionLevel level)
    {
        if (!startupRevealPrepared || startupRevealLevel != level)
        {
            PrepareStartupReveal(level);
        }

        if (startupRevealStarted)
        {
            EmitDiagnostic("StartupRevealStatePreserved", "RevealAlreadyStarted");
            return;
        }

        startupRevealStarted = true;
        long generation = startupRevealGeneration;
        pendingStartupRevealAnimations = 0;
        ResolveRoleCache();
        if (motionSurface is null || level == MotionLevel.Off)
        {
            CompleteStartupReveal();
            return;
        }

        EmitDiagnostic("StartupRevealStarted", level.ToString());
        EmitDiagnostic("DashboardRootEnterStarted", level.ToString());
        if (level == MotionLevel.Reduced)
        {
            BeginStartupAnimation(
                motionSurface,
                translateTransform,
                0d,
                0d,
                TimeSpan.FromMilliseconds(60),
                TimeSpan.FromMilliseconds(120),
                generation);
            return;
        }

        bool full = level == MotionLevel.Full;
        BeginStartupAnimation(
            motionSurface,
            translateTransform,
            full ? 0.32d : 0.38d,
            full ? 8d : 6d,
            TimeSpan.FromMilliseconds(full ? 120d : 100d),
            TimeSpan.FromMilliseconds(full ? 220d : 180d),
            generation);
        if (cachedPrimary is not null)
        {
            EmitDiagnostic("DashboardPrimaryEnterStarted", level.ToString());
        }
        BeginStartupAnimation(
            cachedPrimary,
            EnsureModuleTranslate(cachedPrimary),
            full ? 0.42d : 0.46d,
            full ? 6d : 4d,
            TimeSpan.FromMilliseconds(full ? 140d : 114d),
            TimeSpan.FromMilliseconds(full ? 190d : 166d),
            generation);
        if (cachedSecondary is not null)
        {
            EmitDiagnostic("DashboardSecondaryEnterStarted", level.ToString());
        }
        BeginStartupAnimation(
            cachedSecondary,
            EnsureModuleTranslate(cachedSecondary),
            full ? 0.24d : 0.30d,
            full ? 10d : 7d,
            TimeSpan.FromMilliseconds(full ? 185d : 144d),
            TimeSpan.FromMilliseconds(full ? 190d : 166d),
            generation);
        _ = RestoreStartupInteractionAsync(
            generation,
            TimeSpan.FromMilliseconds(full ? 260d : 220d));
        if (pendingStartupRevealAnimations == 0)
        {
            CompleteStartupVisualFrame(generation);
        }
    }

    public void RestoreStartupReveal()
    {
        CompleteStartupReveal();
    }

    public void CompleteStartupReveal()
    {
        if (!startupRevealPrepared && !startupRevealStarted)
        {
            RestoreVisualFinalState();
            IsHitTestVisible = true;
            return;
        }

        EmitDiagnostic("StartupCleanupStarted", "SequenceCompleted");
        RestoreVisualFinalState();
        IsHitTestVisible = true;
        startupRevealPrepared = false;
        startupRevealStarted = false;
        pendingStartupRevealAnimations = 0;
        startupRevealLevel = MotionLevel.Off;
        EmitDiagnostic("StartupRevealCompleted", "SequenceCompleted");
        EmitDiagnostic("StartupCleanupCompleted", "SequenceCompleted");
    }

    private static void AnimateExitElement(
        FrameworkElement? element,
        TranslateTransform? transform,
        TimeSpan delay,
        TimeSpan duration,
        double commitOpacity,
        double offset,
        FlowRelayDirection direction,
        Action? completed = null)
    {
        if (element is null)
        {
            return;
        }

        element.Opacity = commitOpacity;
        DoubleAnimationUsingKeyFrames opacity = BuildDelayedAnimation(
            1d,
            commitOpacity,
            delay,
            duration,
            EasingMode.EaseIn);
        opacity.FillBehavior = FillBehavior.HoldEnd;
        if (completed is not null)
        {
            opacity.Completed += (_, _) => completed();
        }
        element.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);

        if (transform is null || offset <= 0d)
        {
            return;
        }

        (DependencyProperty property, double enterOffset) =
            ResolveTranslation(direction, offset);
        double exitOffset = -enterOffset;
        if (property == TranslateTransform.XProperty)
        {
            transform.X = exitOffset;
        }
        else
        {
            transform.Y = exitOffset;
        }
        DoubleAnimationUsingKeyFrames translation = BuildDelayedAnimation(
            0d,
            exitOffset,
            delay,
            duration,
            EasingMode.EaseIn);
        translation.FillBehavior = FillBehavior.HoldEnd;
        transform.BeginAnimation(
            property,
            translation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnOutgoingExitCompleted(long version, FrameworkElement exitingRoot)
    {
        if (version != explicitNavigationVersion
            || !ReferenceEquals(outgoingRoot, exitingRoot))
        {
            return;
        }

        RestoreTrackedElement(outgoingSecondary);
        RestoreTrackedElement(outgoingPrimary);
        RestoreTrackedElement(exitingRoot);
        pagePresenter?.CompleteOutgoing(version, exitingRoot);
        navigationExitCompleted = true;
        EmitDiagnostic("PageExitCompleted", "AnimationCompleted");
        outgoingRoot = null;
        outgoingPrimary = null;
        outgoingSecondary = null;
    }

    private void TrackAnimatedElement(FrameworkElement? element)
    {
        if (element is not null && !animatedModules.Contains(element))
        {
            animatedModules.Add(element);
        }
    }

    private void RestoreTrackedElement(FrameworkElement? element)
    {
        if (element is null)
        {
            return;
        }

        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = 1d;
        if (element.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = 0d;
            transform.Y = 0d;
        }
        animatedModules.Remove(element);
    }

    private void ApplyCommittedBaseState()
    {
        FrameworkElement? incomingRoot = pagePresenter?.PresentedRoot;
        if (incomingRoot is null || explicitPlan is null)
        {
            return;
        }

        FlowRelayPlan plan = explicitPlan;
        TranslateTransform? incomingTransform = EnsureModuleTranslate(incomingRoot);
        TrackAnimatedElement(incomingRoot);
        incomingRoot.BeginAnimation(OpacityProperty, null);
        incomingTransform?.BeginAnimation(TranslateTransform.XProperty, null);
        incomingTransform?.BeginAnimation(TranslateTransform.YProperty, null);
        SetElementBase(
            incomingRoot,
            incomingTransform,
            plan.PageStartOpacity,
            plan.AllowsPageTranslation ? plan.PageSettleOffset : 0d,
            explicitDirection);
        if (plan.AllowsRoleStagger)
        {
            TrackAnimatedElement(cachedPrimary);
            SetElementBase(
                cachedPrimary,
                EnsureModuleTranslate(cachedPrimary),
                plan.PrimaryModuleStartOpacity,
                plan.PrimaryModuleOffset,
                explicitDirection);
            TrackAnimatedElement(cachedSecondary);
            SetElementBase(
                cachedSecondary,
                EnsureModuleTranslate(cachedSecondary),
                plan.SecondaryModuleStartOpacity,
                plan.SecondaryModuleOffset,
                explicitDirection);
        }
        IsHitTestVisible = false;
    }

    private void OnPageContentPresented(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        cachedRoleContent = null;
        cachedPrimary = null;
        cachedSecondary = null;
        ResolveRoleCache();
        if (explicitNavigationVersion >= 0 && explicitContentCommitted)
        {
            ApplyCommittedBaseState();
            if (explicitPlan is not null)
            {
                PlayEnter(explicitPlan, explicitDirection, explicitNavigationVersion);
            }
        }
    }

    private void ResolveRoleCache()
    {
        if (motionSurface is null)
        {
            return;
        }

        if (ReferenceEquals(cachedRoleContent, Content)
            && (cachedPrimary is not null || cachedSecondary is not null))
        {
            return;
        }

        System.Diagnostics.Stopwatch resolutionClock =
            System.Diagnostics.Stopwatch.StartNew();
        cachedRoleContent = Content;
        FrameworkElement? roleRoot =
            ReferenceEquals(pagePresenter?.PresentedContent, Content)
                ? pagePresenter.PresentedRoot
                : null;
        if (roleRoot is null)
        {
            cachedPrimary = null;
            cachedSecondary = null;
            resolutionClock.Stop();
            AppLogger.LogKeyEvent(
                "MotionRuntime | event=RoleTreeDeferred; "
                + $"page={Content?.GetType().Name ?? "null"}; "
                + $"elapsed={resolutionClock.Elapsed.TotalMilliseconds:0.###}ms");
            return;
        }

        cachedPrimary = FindRoleElement(
            roleRoot,
            NavigationMotionRole.Primary);
        cachedSecondary = FindRoleElement(
            roleRoot,
            NavigationMotionRole.Secondary);
        resolutionClock.Stop();
        AppLogger.LogKeyEvent(
            "MotionRuntime | event=RoleTreeResolved; "
            + $"page={Content?.GetType().Name ?? "null"}; "
            + $"primary={cachedPrimary?.Name ?? "none"}; "
            + $"secondary={cachedSecondary?.Name ?? "none"}; "
            + $"elapsed={resolutionClock.Elapsed.TotalMilliseconds:0.###}ms");
    }

    private void ScheduleCommittedTemplateDiagnostics(long version)
    {
        if (roleResolutionScheduledVersion == version)
        {
            return;
        }

        roleResolutionScheduledVersion = version;
        long scheduledTimestamp =
            System.Diagnostics.Stopwatch.GetTimestamp();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (version != explicitNavigationVersion)
                {
                    return;
                }

                TimeSpan dispatcherGap =
                    System.Diagnostics.Stopwatch.GetElapsedTime(
                        scheduledTimestamp);
                EmitDiagnostic(
                    "ContentTemplateApplied",
                    $"Loaded; dispatcherGapMs={dispatcherGap.TotalMilliseconds:0.###}");
                if (cachedPrimary is null && cachedSecondary is null)
                {
                    cachedRoleContent = null;
                    ResolveRoleCache();
                    ApplyCommittedBaseState();
                }

                EmitDiagnostic("TargetLayoutCompleted", "Loaded");
                _ = Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() =>
                    {
                        if (version == explicitNavigationVersion)
                        {
                            EmitDiagnostic(
                                "PageEnterFirstRender",
                                "RenderPriority");
                        }
                    }));
            }));
    }

    private static TranslateTransform? EnsureModuleTranslate(FrameworkElement? module)
    {
        if (module is null)
        {
            return null;
        }

        if (module.RenderTransform is TranslateTransform transform)
        {
            return transform;
        }

        transform = new TranslateTransform();
        module.RenderTransform = transform;
        return transform;
    }

    private static void SetElementBase(
        FrameworkElement? element,
        TranslateTransform? transform,
        double opacity,
        double offset,
        FlowRelayDirection direction = FlowRelayDirection.FromBottom)
    {
        if (element is null)
        {
            return;
        }

        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = opacity;
        if (transform is null)
        {
            return;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0d;
        transform.Y = 0d;
        (DependencyProperty property, double signedOffset) =
            ResolveTranslation(direction, offset);
        if (property == TranslateTransform.XProperty)
        {
            transform.X = signedOffset;
        }
        else
        {
            transform.Y = signedOffset;
        }
    }

    private void BeginStartupAnimation(
        FrameworkElement? element,
        TranslateTransform? transform,
        double startOpacity,
        double offset,
        TimeSpan delay,
        TimeSpan duration,
        long generation)
    {
        if (element is null)
        {
            return;
        }

        pendingStartupRevealAnimations++;
        element.Opacity = 1d;
        DoubleAnimationUsingKeyFrames opacity = BuildDelayedAnimation(
            startOpacity,
            1d,
            delay,
            duration,
            EasingMode.EaseOut);
        opacity.Completed += (_, _) => OnStartupAnimationCompleted(generation);
        element.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);
        if (transform is null || offset <= 0d)
        {
            return;
        }

        transform.Y = 0d;
        transform.BeginAnimation(
            TranslateTransform.YProperty,
            BuildDelayedAnimation(
                offset,
                0d,
                delay,
                duration,
                EasingMode.EaseOut),
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnStartupAnimationCompleted(long generation)
    {
        if (generation != startupRevealGeneration
            || !startupRevealStarted
            || pendingStartupRevealAnimations <= 0)
        {
            return;
        }

        pendingStartupRevealAnimations--;
        if (pendingStartupRevealAnimations == 0)
        {
            CompleteStartupVisualFrame(generation);
        }
    }

    private void CompleteStartupVisualFrame(long generation)
    {
        if (generation != startupRevealGeneration || !startupRevealStarted)
        {
            return;
        }

        RestoreVisualFinalState();
        IsHitTestVisible = true;
        EmitDiagnostic("RevealVisualFrameCommitted", "AllDashboardClocksCompleted");
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (generation == startupRevealGeneration && startupRevealStarted)
                {
                    StartupRevealVisualCompleted?.Invoke(this, EventArgs.Empty);
                }
            }));
    }

    private static DoubleAnimationUsingKeyFrames BuildDelayedAnimation(
        double from,
        double to,
        TimeSpan delay,
        TimeSpan duration,
        EasingMode easingMode)
    {
        DoubleAnimationUsingKeyFrames animation = new()
        {
            FillBehavior = FillBehavior.Stop
        };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
            from,
            KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > TimeSpan.Zero)
        {
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                from,
                KeyTime.FromTimeSpan(delay)));
        }
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(
            to,
            KeyTime.FromTimeSpan(delay + duration),
            new CubicEase { EasingMode = easingMode }));
        return animation;
    }

    private void OnEnterAnimationCompleted(long version)
    {
        if (version != explicitNavigationVersion || pendingEnterAnimations <= 0)
        {
            return;
        }

        pendingEnterAnimations--;
        if (pendingEnterAnimations > 0)
        {
            return;
        }

        navigationEnterCompleted = true;
        explicitSettleActive = false;
        lifecycleState = MotionTransitionLifecycleState.Finalizing;
        EmitDiagnostic("PageEnterCompleted", "AllEnterClocksCompleted");
        if (navigationCompletionRequested)
        {
            FinalizeNavigation(version);
        }
    }

    private async Task RestoreInteractionAfterThresholdAsync(
        long version,
        double startOpacity,
        TimeSpan duration)
    {
        double fraction = startOpacity >= 0.70d
            ? 0d
            : Math.Clamp((0.70d - startOpacity) / (1d - startOpacity), 0d, 1d);
        await Task.Delay(TimeSpan.FromMilliseconds(
            duration.TotalMilliseconds * fraction)).ConfigureAwait(false);
        try
        {
            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (version == explicitNavigationVersion)
                    {
                        IsHitTestVisible = true;
                    }
                },
                DispatcherPriority.Input);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task RestoreStartupInteractionAsync(
        long generation,
        TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        try
        {
            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (generation == startupRevealGeneration)
                    {
                        IsHitTestVisible = true;
                    }
                },
                DispatcherPriority.Input);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
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

    internal FrameworkElement? FindRoleElement(NavigationMotionRole role)
    {
        ResolveRoleCache();
        return role == NavigationMotionRole.Primary
            ? cachedPrimary
            : role == NavigationMotionRole.Secondary
                ? cachedSecondary
                : null;
    }

    internal static FrameworkElement? FindRoleElement(
        DependencyObject? root,
        NavigationMotionRole role)
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
        CancelAllMotionForUnload();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            SchedulePendingReplay();
        }
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        if (explicitSettleActive || startupRevealStarted)
        {
            string reason = e.NewSize.Width <= 0d
                || e.NewSize.Height <= 0d
                || !double.IsFinite(e.NewSize.Width)
                || !double.IsFinite(e.NewSize.Height)
                    ? "InvalidDimensions"
                    : "Continue";
            EmitDiagnostic("HostSizeChangedDuringMotion", reason);
            if (reason == "InvalidDimensions")
            {
                CancelAllMotion(reason);
            }
        }
    }

    private void QueueDiagnostic(
        string eventName,
        string reason,
        TimeSpan delay,
        long version)
    {
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(async () =>
            {
                await Task.Delay(delay).ConfigureAwait(true);
                if (version == explicitNavigationVersion)
                {
                    EmitDiagnostic(eventName, reason);
                }
            }));
    }

    private void EmitDiagnostic(string eventName, string reason)
    {
        FrameworkElement? activeRoot = ActiveRoot;
        double rootOpacity = activeRoot?.Opacity ?? 1d;
        double primaryOpacity = cachedPrimary?.Opacity ?? 1d;
        double secondaryOpacity = cachedSecondary?.Opacity ?? 1d;
        TranslateTransform? activeTransform = activeRoot?.RenderTransform as TranslateTransform;
        double translateX = activeTransform?.X ?? translateTransform?.X ?? 0d;
        double translateY = activeTransform?.Y ?? translateTransform?.Y ?? 0d;
        MotionRuntimeDiagnostic diagnostic = new(
            eventName,
            diagnosticClock.Elapsed,
            explicitNavigationVersion,
            startupRevealGeneration,
            lifecycleState,
            reason,
            rootOpacity,
            primaryOpacity,
            secondaryOpacity,
            translateX,
            translateY,
            ActualWidth,
            ActualHeight);
        diagnostics.Add(diagnostic);
        if (diagnostics.Count > 256)
        {
            diagnostics.RemoveAt(0);
        }

        AppLogger.LogKeyEvent(
            $"MotionRuntime | event={eventName}; t={diagnostic.RelativeTime.TotalMilliseconds:0}ms; " +
            $"nav={diagnostic.NavigationVersion}; startup={diagnostic.StartupGeneration}; " +
            $"state={diagnostic.State}; phase={diagnostic.State}; reason={reason}; " +
            $"root={rootOpacity:0.###}; primary={primaryOpacity:0.###}; secondary={secondaryOpacity:0.###}; " +
            $"translate=({translateX:0.###},{translateY:0.###}); " +
            $"host=({ActualWidth:0.##},{ActualHeight:0.##})");
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
