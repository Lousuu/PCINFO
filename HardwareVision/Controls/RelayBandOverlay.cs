using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace HardwareVision.Controls;

[TemplatePart(Name = BandRootPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = BandBodyPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = LeadingEdgePartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TrailingEdgePartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TraceAPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TraceBPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = NodeAPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = NodeBPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = InternalPulsePartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = RouteCodePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = CenterLockPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TranslatePartName, Type = typeof(TranslateTransform))]
public sealed class RelayBandOverlay : System.Windows.Controls.Control
{
    private const string BandRootPartName = "PART_BandRoot";
    private const string BandBodyPartName = "PART_BandBody";
    private const string LeadingEdgePartName = "PART_LeadingEdge";
    private const string TrailingEdgePartName = "PART_TrailingEdge";
    private const string TraceAPartName = "PART_TraceA";
    private const string TraceBPartName = "PART_TraceB";
    private const string NodeAPartName = "PART_NodeA";
    private const string NodeBPartName = "PART_NodeB";
    private const string InternalPulsePartName = "PART_InternalPulse";
    private const string RouteCodePartName = "PART_RouteCode";
    private const string CenterLockPartName = "PART_CenterLock";
    private const string TranslatePartName = "PART_Translate";
    private const string InternalPulseTranslatePartName = "PART_InternalPulseTranslate";

    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(NavigationTransitionSnapshot),
        typeof(RelayBandOverlay),
        new PropertyMetadata(NavigationTransitionSnapshot.Idle(), OnSnapshotChanged));

    private FrameworkElement? bandRoot;
    private FrameworkElement? bandBody;
    private FrameworkElement? leadingEdge;
    private FrameworkElement? trailingEdge;
    private FrameworkElement? traceA;
    private FrameworkElement? traceB;
    private FrameworkElement? nodeA;
    private FrameworkElement? nodeB;
    private FrameworkElement? internalPulse;
    private TextBlock? routeCode;
    private FrameworkElement? centerLock;
    private TranslateTransform? translate;
    private TranslateTransform? internalPulseTranslate;
    private long activeVersion = -1;
    private long committedVersion = -1;

    static RelayBandOverlay()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RelayBandOverlay),
            new FrameworkPropertyMetadata(typeof(RelayBandOverlay)));
    }

    public RelayBandOverlay()
    {
        Focusable = false;
        IsTabStop = false;
        IsHitTestVisible = false;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;
        SizeChanged += (_, _) => SuppressCurrentVersionAfterResize();
        Unloaded += (_, _) => RestoreFinalState();
    }

    public NavigationTransitionSnapshot Snapshot
    {
        get => (NavigationTransitionSnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    internal double BandWidth => bandRoot?.Width ?? 0d;

    internal double BandHeight => bandRoot?.Height ?? 0d;

    public override void OnApplyTemplate()
    {
        RestoreFinalState();
        base.OnApplyTemplate();
        bandRoot = GetTemplateChild(BandRootPartName) as FrameworkElement;
        bandBody = GetTemplateChild(BandBodyPartName) as FrameworkElement;
        leadingEdge = GetTemplateChild(LeadingEdgePartName) as FrameworkElement;
        trailingEdge = GetTemplateChild(TrailingEdgePartName) as FrameworkElement;
        traceA = GetTemplateChild(TraceAPartName) as FrameworkElement;
        traceB = GetTemplateChild(TraceBPartName) as FrameworkElement;
        nodeA = GetTemplateChild(NodeAPartName) as FrameworkElement;
        nodeB = GetTemplateChild(NodeBPartName) as FrameworkElement;
        internalPulse = GetTemplateChild(InternalPulsePartName) as FrameworkElement;
        routeCode = GetTemplateChild(RouteCodePartName) as TextBlock;
        centerLock = GetTemplateChild(CenterLockPartName) as FrameworkElement;
        translate = GetTemplateChild(TranslatePartName) as TranslateTransform;
        internalPulseTranslate = GetTemplateChild(InternalPulseTranslatePartName) as TranslateTransform;
        ApplySnapshot(Snapshot);
    }

    public void CancelTransition() => RestoreFinalState();

    public void RestoreFinalState()
    {
        ClearElement(bandRoot, 0d, Visibility.Collapsed);
        ClearElement(nodeA, 0.35d, Visibility.Visible);
        ClearElement(nodeB, 0.35d, Visibility.Visible);
        ClearElement(internalPulse, 0d, Visibility.Collapsed);
        ClearElement(centerLock, 0d, Visibility.Collapsed);
        ClearTransform(translate);
        ClearTransform(internalPulseTranslate);
        activeVersion = -1;
        committedVersion = -1;
        Opacity = 0d;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RelayBandOverlay overlay && e.NewValue is NavigationTransitionSnapshot snapshot)
        {
            overlay.ApplySnapshot(snapshot);
        }
    }

    private void ApplySnapshot(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive || !snapshot.Plan.ShowsRelayBand)
        {
            RestoreFinalState();
            return;
        }

        if (routeCode is not null)
        {
            routeCode.Text = $"{snapshot.OriginCode} → {snapshot.TargetCode}";
        }

        if (activeVersion != snapshot.Version)
        {
            activeVersion = snapshot.Version;
            committedVersion = -1;
            StartTransition(snapshot);
        }

        if (snapshot.Phase == NavigationTransitionPhase.Relay
            && snapshot.HasCommitted
            && committedVersion != snapshot.Version)
        {
            committedVersion = snapshot.Version;
            PlayCenterLock(snapshot);
        }
    }

    private void StartTransition(NavigationTransitionSnapshot snapshot)
    {
        if (bandRoot is null || translate is null || ActualWidth <= 0d || ActualHeight <= 0d)
        {
            return;
        }

        bool horizontalTravel = snapshot.Direction is NavigationTransitionDirection.FromLeft
            or NavigationTransitionDirection.FromRight;
        ConfigureBand(snapshot.Direction, horizontalTravel);
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;
        bandRoot.Visibility = Visibility.Visible;
        bandRoot.Opacity = 1d;

        if (!snapshot.Plan.AllowsRelayTranslation)
        {
            bandRoot.HorizontalAlignment = WpfHorizontalAlignment.Center;
            bandRoot.VerticalAlignment = WpfVerticalAlignment.Center;
            DoubleAnimationUsingKeyFrames fade = new() { FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(snapshot.Plan.CommitTime)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(snapshot.Plan.TotalDuration)));
            fade.Completed += (_, _) => RestoreFinalState();
            bandRoot.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        PlayInternalSignals(snapshot, horizontalTravel);
        (DependencyProperty property, double start, double center, double end) =
            ResolveTravel(snapshot.Direction, horizontalTravel);
        CubicEase easing = new() { EasingMode = EasingMode.EaseInOut };
        DoubleAnimationUsingKeyFrames travel = new() { FillBehavior = FillBehavior.Stop };
        travel.KeyFrames.Add(new EasingDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero), easing));
        travel.KeyFrames.Add(new EasingDoubleKeyFrame(center, KeyTime.FromTimeSpan(snapshot.Plan.CommitTime),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        travel.KeyFrames.Add(new EasingDoubleKeyFrame(end, KeyTime.FromTimeSpan(snapshot.Plan.TotalDuration),
            new CubicEase { EasingMode = EasingMode.EaseInOut }));
        travel.Completed += (_, _) => RestoreFinalState();
        translate.BeginAnimation(property, travel, HandoffBehavior.SnapshotAndReplace);
    }

    private void ConfigureBand(NavigationTransitionDirection direction, bool horizontalTravel)
    {
        if (bandRoot is null)
        {
            return;
        }

        bandRoot.Width = horizontalTravel ? Math.Clamp(ActualWidth * 0.22d, 160d, 300d) : ActualWidth;
        bandRoot.Height = horizontalTravel ? ActualHeight : Math.Clamp(ActualHeight * 0.22d, 120d, 220d);
        bandRoot.HorizontalAlignment = horizontalTravel ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Stretch;
        bandRoot.VerticalAlignment = horizontalTravel ? WpfVerticalAlignment.Stretch : WpfVerticalAlignment.Top;

        bool leadingAtStart = direction is NavigationTransitionDirection.FromRight
            or NavigationTransitionDirection.FromBottom;
        ConfigureEdge(leadingEdge, horizontalTravel, leadingAtStart, 2d);
        ConfigureEdge(trailingEdge, horizontalTravel, !leadingAtStart, 1d);
        ConfigureTrace(traceA, horizontalTravel, -18d);
        ConfigureTrace(traceB, horizontalTravel, 18d);
        ConfigureNode(nodeA, horizontalTravel, -18d);
        ConfigureNode(nodeB, horizontalTravel, 18d);
    }

    private static void ConfigureEdge(
        FrameworkElement? edge,
        bool horizontalTravel,
        bool atStart,
        double thickness)
    {
        if (edge is null)
        {
            return;
        }
        edge.Width = horizontalTravel ? thickness : double.NaN;
        edge.Height = horizontalTravel ? double.NaN : thickness;
        edge.HorizontalAlignment = horizontalTravel
            ? atStart ? WpfHorizontalAlignment.Left : WpfHorizontalAlignment.Right
            : WpfHorizontalAlignment.Stretch;
        edge.VerticalAlignment = horizontalTravel
            ? WpfVerticalAlignment.Stretch
            : atStart ? WpfVerticalAlignment.Top : WpfVerticalAlignment.Bottom;
    }

    private static void ConfigureTrace(FrameworkElement? trace, bool horizontalTravel, double offset)
    {
        if (trace is null)
        {
            return;
        }
        trace.Width = horizontalTravel ? 1d : double.NaN;
        trace.Height = horizontalTravel ? double.NaN : 1d;
        trace.HorizontalAlignment = horizontalTravel ? WpfHorizontalAlignment.Center : WpfHorizontalAlignment.Stretch;
        trace.VerticalAlignment = horizontalTravel ? WpfVerticalAlignment.Stretch : WpfVerticalAlignment.Center;
        trace.Margin = horizontalTravel
            ? new Thickness(offset, 26d, 0d, 26d)
            : new Thickness(26d, offset, 26d, 0d);
    }

    private static void ConfigureNode(FrameworkElement? node, bool horizontalTravel, double offset)
    {
        if (node is null)
        {
            return;
        }
        node.HorizontalAlignment = WpfHorizontalAlignment.Center;
        node.VerticalAlignment = WpfVerticalAlignment.Center;
        node.Margin = horizontalTravel
            ? new Thickness(offset, offset, 0d, 0d)
            : new Thickness(offset, -offset, 0d, 0d);
    }

    private (DependencyProperty Property, double Start, double Center, double End) ResolveTravel(
        NavigationTransitionDirection direction,
        bool horizontalTravel)
    {
        if (bandRoot is null)
        {
            return (TranslateTransform.XProperty, 0d, 0d, 0d);
        }
        if (horizontalTravel)
        {
            double center = (ActualWidth - bandRoot.Width) / 2d;
            bool fromRight = direction == NavigationTransitionDirection.FromRight;
            return (TranslateTransform.XProperty,
                fromRight ? ActualWidth : -bandRoot.Width,
                center,
                fromRight ? -bandRoot.Width : ActualWidth);
        }
        double verticalCenter = (ActualHeight - bandRoot.Height) / 2d;
        return direction switch
        {
            NavigationTransitionDirection.FromBottom =>
                (TranslateTransform.YProperty, ActualHeight, verticalCenter, -bandRoot.Height),
            NavigationTransitionDirection.FromTop =>
                (TranslateTransform.YProperty, -bandRoot.Height, verticalCenter, ActualHeight),
            _ => (TranslateTransform.YProperty, -bandRoot.Height, verticalCenter, ActualHeight)
        };
    }

    private void PlayInternalSignals(NavigationTransitionSnapshot snapshot, bool horizontalTravel)
    {
        if (snapshot.Plan.EffectiveLevel == MotionLevel.Reduced)
        {
            ClearElement(nodeA, 0d, Visibility.Collapsed);
            ClearElement(nodeB, 0d, Visibility.Collapsed);
            ClearElement(internalPulse, 0d, Visibility.Collapsed);
            return;
        }
        PlayNode(nodeA, TimeSpan.Zero, snapshot.Plan.CommitTime);
        TimeSpan nodeDelay = snapshot.Plan.EffectiveLevel == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(24)
            : TimeSpan.FromMilliseconds(18);
        PlayNode(nodeB, nodeDelay, snapshot.Plan.CommitTime);
        if (snapshot.Plan.EffectiveLevel != MotionLevel.Full
            || internalPulse is null
            || internalPulseTranslate is null
            || bandRoot is null)
        {
            return;
        }

        internalPulse.Visibility = Visibility.Visible;
        internalPulse.Opacity = 0.82d;
        DependencyProperty property = horizontalTravel
            ? TranslateTransform.XProperty
            : TranslateTransform.YProperty;
        double extent = horizontalTravel ? bandRoot.Width : bandRoot.Height;
        internalPulseTranslate.BeginAnimation(property, new DoubleAnimation(-extent * 0.32d, extent * 0.32d,
            new Duration(snapshot.Plan.CommitTime))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void PlayNode(FrameworkElement? node, TimeSpan delay, TimeSpan commitTime)
    {
        if (node is null)
        {
            return;
        }
        TimeSpan peak = delay + TimeSpan.FromMilliseconds(20);
        TimeSpan end = delay + TimeSpan.FromMilliseconds(42);
        if (end > commitTime)
        {
            end = commitTime;
        }
        DoubleAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.35d, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(peak < end ? peak : end)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.35d, KeyTime.FromTimeSpan(end)));
        node.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void PlayCenterLock(NavigationTransitionSnapshot snapshot)
    {
        if (centerLock is null || snapshot.Plan.EffectiveLevel == MotionLevel.Off)
        {
            return;
        }
        Visibility = Visibility.Visible;
        Opacity = 1d;
        centerLock.Visibility = Visibility.Visible;
        centerLock.Opacity = 0d;
        (TimeSpan fadeIn, TimeSpan hold, TimeSpan fadeOut) = snapshot.Plan.EffectiveLevel switch
        {
            MotionLevel.Full => (TimeSpan.FromMilliseconds(18), TimeSpan.FromMilliseconds(18), TimeSpan.FromMilliseconds(28)),
            MotionLevel.Standard => (TimeSpan.FromMilliseconds(14), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(22)),
            _ => (TimeSpan.FromMilliseconds(14), TimeSpan.Zero, TimeSpan.FromMilliseconds(14))
        };
        TimeSpan holdEnd = fadeIn + hold;
        TimeSpan end = holdEnd + fadeOut;
        DoubleAnimationUsingKeyFrames flash = new() { FillBehavior = FillBehavior.Stop };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(fadeIn)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(holdEnd)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(end)));
        flash.Completed += (_, _) =>
        {
            if (centerLock is not null)
            {
                centerLock.BeginAnimation(OpacityProperty, null);
                centerLock.Opacity = 0d;
                centerLock.Visibility = Visibility.Collapsed;
            }
        };
        centerLock.BeginAnimation(OpacityProperty, flash, HandoffBehavior.SnapshotAndReplace);
    }

    private void SuppressCurrentVersionAfterResize()
    {
        if (!Snapshot.IsActive || activeVersion != Snapshot.Version)
        {
            return;
        }
        long version = Snapshot.Version;
        RestoreFinalState();
        activeVersion = version;
        committedVersion = version;
    }

    private static void ClearElement(FrameworkElement? element, double opacity, Visibility visibility)
    {
        if (element is null)
        {
            return;
        }
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = opacity;
        element.Visibility = visibility;
    }

    private static void ClearTransform(TranslateTransform? transform)
    {
        if (transform is null)
        {
            return;
        }
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.X = 0d;
        transform.Y = 0d;
    }
}
