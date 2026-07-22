using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Controls;

[TemplatePart(Name = TrackViewportPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = SourceLayerPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TargetLayerPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = SourceCodePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = SourceTitlePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = SourceSubtitlePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = TargetCodePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = TargetTitlePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = TargetSubtitlePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = SourceTranslatePartName, Type = typeof(TranslateTransform))]
[TemplatePart(Name = TargetTranslatePartName, Type = typeof(TranslateTransform))]
public sealed class TelemetryTitleTransitionHost : System.Windows.Controls.Control
{
    private const string TrackViewportPartName = "PART_TrackViewport";
    private const string SourceLayerPartName = "PART_SourceLayer";
    private const string TargetLayerPartName = "PART_TargetLayer";
    private const string SourceCodePartName = "PART_SourceCode";
    private const string SourceTitlePartName = "PART_SourceTitle";
    private const string SourceSubtitlePartName = "PART_SourceSubtitle";
    private const string TargetCodePartName = "PART_TargetCode";
    private const string TargetTitlePartName = "PART_TargetTitle";
    private const string TargetSubtitlePartName = "PART_TargetSubtitle";
    private const string SourceTranslatePartName = "PART_SourceTranslate";
    private const string TargetTranslatePartName = "PART_TargetTranslate";
    private const string SourceCodeTranslatePartName = "PART_SourceCodeTranslate";
    private const string TargetCodeTranslatePartName = "PART_TargetCodeTranslate";

    public static readonly DependencyProperty SnapshotProperty = Register(
        nameof(Snapshot), typeof(NavigationTransitionSnapshot), NavigationTransitionSnapshot.Idle(), OnValueChanged);
    public static readonly DependencyProperty CurrentCodeProperty = Register(
        nameof(CurrentCode), typeof(string), string.Empty, OnValueChanged);
    public static readonly DependencyProperty CurrentTitleProperty = Register(
        nameof(CurrentTitle), typeof(string), string.Empty, OnValueChanged);
    public static readonly DependencyProperty CurrentSubtitleProperty = Register(
        nameof(CurrentSubtitle), typeof(string), string.Empty, OnValueChanged);

    private FrameworkElement? trackViewport;
    private FrameworkElement? sourceLayer;
    private FrameworkElement? targetLayer;
    private TextBlock? sourceCode;
    private TextBlock? sourceTitle;
    private TextBlock? sourceSubtitle;
    private TextBlock? targetCode;
    private TextBlock? targetTitle;
    private TextBlock? targetSubtitle;
    private TranslateTransform? sourceTranslate;
    private TranslateTransform? targetTranslate;
    private TranslateTransform? sourceCodeTranslate;
    private TranslateTransform? targetCodeTranslate;
    private long renderedVersion = -1;
    private NavigationTransitionPhase renderedPhase = NavigationTransitionPhase.Idle;
    private bool renderedCommitted;

    static TelemetryTitleTransitionHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(TelemetryTitleTransitionHost),
            new FrameworkPropertyMetadata(typeof(TelemetryTitleTransitionHost)));
    }

    public TelemetryTitleTransitionHost()
    {
        Focusable = false;
        IsTabStop = false;
        IsHitTestVisible = false;
        Unloaded += (_, _) => RestoreFinalState();
    }

    public NavigationTransitionSnapshot Snapshot
    {
        get => (NavigationTransitionSnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public string CurrentCode
    {
        get => (string)GetValue(CurrentCodeProperty);
        set => SetValue(CurrentCodeProperty, value);
    }

    public string CurrentTitle
    {
        get => (string)GetValue(CurrentTitleProperty);
        set => SetValue(CurrentTitleProperty, value);
    }

    public string CurrentSubtitle
    {
        get => (string)GetValue(CurrentSubtitleProperty);
        set => SetValue(CurrentSubtitleProperty, value);
    }

    public override void OnApplyTemplate()
    {
        RestoreFinalState();
        base.OnApplyTemplate();
        trackViewport = GetTemplateChild(TrackViewportPartName) as FrameworkElement;
        sourceLayer = GetTemplateChild(SourceLayerPartName) as FrameworkElement;
        targetLayer = GetTemplateChild(TargetLayerPartName) as FrameworkElement;
        sourceCode = GetTemplateChild(SourceCodePartName) as TextBlock;
        sourceTitle = GetTemplateChild(SourceTitlePartName) as TextBlock;
        sourceSubtitle = GetTemplateChild(SourceSubtitlePartName) as TextBlock;
        targetCode = GetTemplateChild(TargetCodePartName) as TextBlock;
        targetTitle = GetTemplateChild(TargetTitlePartName) as TextBlock;
        targetSubtitle = GetTemplateChild(TargetSubtitlePartName) as TextBlock;
        sourceTranslate = GetTemplateChild(SourceTranslatePartName) as TranslateTransform;
        targetTranslate = GetTemplateChild(TargetTranslatePartName) as TranslateTransform;
        sourceCodeTranslate = GetTemplateChild(SourceCodeTranslatePartName) as TranslateTransform;
        targetCodeTranslate = GetTemplateChild(TargetCodeTranslatePartName) as TranslateTransform;
        if (trackViewport is not null)
        {
            trackViewport.ClipToBounds = true;
        }
        ApplyState();
    }

    public void CancelTransition() => RestoreFinalState();

    public void RestoreFinalState()
    {
        ClearLayer(sourceLayer, sourceTranslate, sourceCodeTranslate, visible: true);
        ClearLayer(targetLayer, targetTranslate, targetCodeTranslate, visible: false);
        ClearOpacity(sourceSubtitle, 1d);
        ClearOpacity(targetSubtitle, 0d);
        SetText(sourceCode, CurrentCode);
        SetText(sourceTitle, CurrentTitle);
        SetText(sourceSubtitle, CurrentSubtitle);
        SetText(targetCode, string.Empty);
        SetText(targetTitle, string.Empty);
        SetText(targetSubtitle, string.Empty);
        ConfigureAutomation(committed: false, currentTitle: CurrentTitle);
        renderedVersion = -1;
        renderedPhase = NavigationTransitionPhase.Idle;
        renderedCommitted = false;
    }

    private static DependencyProperty Register(
        string name,
        Type type,
        object defaultValue,
        PropertyChangedCallback callback) =>
        DependencyProperty.Register(name, type, typeof(TelemetryTitleTransitionHost), new PropertyMetadata(defaultValue, callback));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        ((TelemetryTitleTransitionHost)d).ApplyState();
    }

    private void ApplyState()
    {
        NavigationTransitionSnapshot snapshot = Snapshot;
        if (!snapshot.IsActive || !snapshot.Plan.UsesClock)
        {
            RestoreFinalState();
            return;
        }

        if (renderedVersion != snapshot.Version)
        {
            BeginVersion(snapshot);
        }

        if (snapshot.Phase == renderedPhase && snapshot.HasCommitted == renderedCommitted)
        {
            return;
        }
        renderedPhase = snapshot.Phase;
        renderedCommitted = snapshot.HasCommitted;

        switch (snapshot.Phase)
        {
            case NavigationTransitionPhase.Route:
                Fade(sourceSubtitle, 1d, 0d, snapshot.Plan.RouteDuration, TimeSpan.Zero, holdEnd: true);
                break;
            case NavigationTransitionPhase.Shift:
                PlayShift(snapshot);
                break;
            case NavigationTransitionPhase.Relay when snapshot.HasCommitted:
                CommitTarget(snapshot);
                break;
            case NavigationTransitionPhase.Settle:
                PlaySettle(snapshot);
                break;
        }
    }

    private void BeginVersion(NavigationTransitionSnapshot snapshot)
    {
        RestoreFinalState();
        renderedVersion = snapshot.Version;
        renderedPhase = NavigationTransitionPhase.Idle;
        renderedCommitted = false;
        SetText(sourceCode, $"{snapshot.OriginCode} /");
        SetText(sourceTitle, snapshot.OriginTitle);
        SetText(sourceSubtitle, snapshot.OriginSubtitle);
        SetText(targetCode, $"{snapshot.TargetCode} /");
        SetText(targetTitle, snapshot.TargetTitle);
        SetText(targetSubtitle, snapshot.TargetSubtitle);
        if (sourceLayer is not null)
        {
            sourceLayer.Visibility = Visibility.Visible;
            sourceLayer.Opacity = 1d;
        }
        if (targetLayer is not null)
        {
            targetLayer.Visibility = Visibility.Visible;
            targetLayer.Opacity = 0d;
        }
        if (targetSubtitle is not null)
        {
            targetSubtitle.Opacity = 0d;
        }
        ConfigureAutomation(committed: false, currentTitle: snapshot.OriginTitle);
    }

    private void PlayShift(NavigationTransitionSnapshot snapshot)
    {
        bool reduced = !snapshot.Plan.AllowsTelemetryTranslation;
        Fade(sourceLayer, 1d, 0.28d, snapshot.Plan.ShiftDuration, TimeSpan.Zero, holdEnd: true);
        Fade(targetLayer, 0.18d, 1d, snapshot.Plan.ShiftDuration, TimeSpan.Zero, holdEnd: true);
        if (reduced)
        {
            return;
        }

        (Vector sourceOffset, Vector targetOffset) = ResolveOffsets(snapshot);
        AnimateTranslate(sourceTranslate, sourceOffset, new Vector(0d, 0d), snapshot.Plan.ShiftDuration, reverse: true);
        AnimateTranslate(targetTranslate, targetOffset, new Vector(0d, 0d), snapshot.Plan.ShiftDuration, reverse: false);
        AnimateTranslate(sourceCodeTranslate, sourceOffset * -0.4d, new Vector(0d, 0d), snapshot.Plan.ShiftDuration, reverse: true);
        AnimateTranslate(targetCodeTranslate, targetOffset * -0.4d, new Vector(0d, 0d), snapshot.Plan.ShiftDuration, reverse: false);
    }

    private void CommitTarget(NavigationTransitionSnapshot snapshot)
    {
        if (renderedVersion != snapshot.Version)
        {
            return;
        }
        ConfigureAutomation(committed: true, currentTitle: snapshot.TargetTitle);
        Fade(sourceLayer, 0.28d, 0d, TimeSpan.FromMilliseconds(
            snapshot.Plan.EffectiveLevel == MotionLevel.Full ? 64d : snapshot.Plan.EffectiveLevel == MotionLevel.Standard ? 46d : 28d),
            TimeSpan.Zero,
            holdEnd: true);
    }

    private void PlaySettle(NavigationTransitionSnapshot snapshot)
    {
        if (sourceLayer is not null)
        {
            sourceLayer.Visibility = Visibility.Collapsed;
        }
        if (targetLayer is not null)
        {
            targetLayer.Visibility = Visibility.Visible;
            targetLayer.Opacity = 1d;
        }
        TimeSpan fadeDuration = TimeSpan.FromMilliseconds(Math.Min(70d, snapshot.Plan.SettleDuration.TotalMilliseconds));
        TimeSpan delay = snapshot.Plan.SettleDuration - fadeDuration;
        Fade(targetSubtitle, 0d, 1d, fadeDuration, delay, holdEnd: true);
    }

    private static (Vector Source, Vector Target) ResolveOffsets(NavigationTransitionSnapshot snapshot)
    {
        bool full = snapshot.Plan.EffectiveLevel == MotionLevel.Full;
        double sourceHorizontal = full ? 8d : 6d;
        double targetHorizontal = full ? 10d : 7d;
        double sourceVertical = full ? 6d : 4d;
        double targetVertical = full ? 8d : 5d;
        return snapshot.Direction switch
        {
            NavigationTransitionDirection.FromRight => (new Vector(-sourceHorizontal, 0d), new Vector(targetHorizontal, 0d)),
            NavigationTransitionDirection.FromLeft => (new Vector(sourceHorizontal, 0d), new Vector(-targetHorizontal, 0d)),
            NavigationTransitionDirection.FromBottom => (new Vector(0d, -sourceVertical), new Vector(0d, targetVertical)),
            NavigationTransitionDirection.FromTop => (new Vector(0d, sourceVertical), new Vector(0d, -targetVertical)),
            _ => (new Vector(), new Vector())
        };
    }

    private static void AnimateTranslate(
        TranslateTransform? transform,
        Vector offset,
        Vector target,
        TimeSpan duration,
        bool reverse)
    {
        if (transform is null)
        {
            return;
        }
        Vector from = reverse ? target : offset;
        Vector to = reverse ? offset : target;
        CubicEase easing = new() { EasingMode = EasingMode.EaseInOut };
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X, to.X, new Duration(duration))
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = easing
        }, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y, to.Y, new Duration(duration))
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void ConfigureAutomation(bool committed, string currentTitle)
    {
        if (sourceTitle is not null)
        {
            AutomationProperties.SetLiveSetting(sourceTitle, AutomationLiveSetting.Off);
            AutomationProperties.SetName(sourceTitle, committed ? string.Empty : currentTitle);
        }
        if (targetTitle is not null)
        {
            AutomationProperties.SetLiveSetting(
                targetTitle,
                committed ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
            AutomationProperties.SetName(targetTitle, committed ? currentTitle : string.Empty);
        }
    }

    private static void Fade(
        UIElement? element,
        double from,
        double to,
        TimeSpan duration,
        TimeSpan delay,
        bool holdEnd = false)
    {
        element?.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = delay,
            Duration = new Duration(duration),
            FillBehavior = holdEnd ? FillBehavior.HoldEnd : FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void ClearLayer(
        FrameworkElement? layer,
        TranslateTransform? transform,
        TranslateTransform? codeTransform,
        bool visible)
    {
        ClearOpacity(layer, visible ? 1d : 0d);
        ClearTransform(transform);
        ClearTransform(codeTransform);
        if (layer is not null)
        {
            layer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void ClearOpacity(UIElement? element, double opacity)
    {
        if (element is null)
        {
            return;
        }
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = opacity;
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

    private static void SetText(TextBlock? block, string text)
    {
        if (block is not null)
        {
            block.Text = text;
        }
    }
}
