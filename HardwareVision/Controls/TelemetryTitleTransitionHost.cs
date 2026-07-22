using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Controls;

[TemplatePart(Name = CodePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = TitlePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = SubtitlePartName, Type = typeof(TextBlock))]
[TemplatePart(Name = CodeTranslatePartName, Type = typeof(TranslateTransform))]
[TemplatePart(Name = TitleTranslatePartName, Type = typeof(TranslateTransform))]
public sealed class TelemetryTitleTransitionHost : System.Windows.Controls.Control
{
    private const string CodePartName = "PART_Code";
    private const string TitlePartName = "PART_Title";
    private const string SubtitlePartName = "PART_Subtitle";
    private const string CodeTranslatePartName = "PART_CodeTranslate";
    private const string TitleTranslatePartName = "PART_TitleTranslate";

    public static readonly DependencyProperty SnapshotProperty = Register(
        nameof(Snapshot), typeof(NavigationTransitionSnapshot), NavigationTransitionSnapshot.Idle(), OnValueChanged);
    public static readonly DependencyProperty CurrentCodeProperty = Register(
        nameof(CurrentCode), typeof(string), string.Empty, OnValueChanged);
    public static readonly DependencyProperty CurrentTitleProperty = Register(
        nameof(CurrentTitle), typeof(string), string.Empty, OnValueChanged);
    public static readonly DependencyProperty CurrentSubtitleProperty = Register(
        nameof(CurrentSubtitle), typeof(string), string.Empty, OnValueChanged);

    private TextBlock? code;
    private TextBlock? title;
    private TextBlock? subtitle;
    private TranslateTransform? codeTranslate;
    private TranslateTransform? titleTranslate;
    private long renderedVersion = -1;
    private NavigationTransitionPhase renderedPhase = NavigationTransitionPhase.Idle;

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
        code = GetTemplateChild(CodePartName) as TextBlock;
        title = GetTemplateChild(TitlePartName) as TextBlock;
        subtitle = GetTemplateChild(SubtitlePartName) as TextBlock;
        codeTranslate = GetTemplateChild(CodeTranslatePartName) as TranslateTransform;
        titleTranslate = GetTemplateChild(TitleTranslatePartName) as TranslateTransform;
        ApplyState();
    }

    public void CancelTransition() => RestoreFinalState();

    public void RestoreFinalState()
    {
        ClearAnimations(code, codeTranslate);
        ClearAnimations(title, titleTranslate);
        ClearAnimations(subtitle, null);
        if (code is not null) code.Text = CurrentCode;
        if (title is not null) title.Text = CurrentTitle;
        if (subtitle is not null) subtitle.Text = CurrentSubtitle;
        renderedVersion = -1;
        renderedPhase = NavigationTransitionPhase.Idle;
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
            renderedVersion = snapshot.Version;
            renderedPhase = NavigationTransitionPhase.Idle;
            if (code is not null) code.Text = $"{snapshot.OriginCode} /";
            if (title is not null) title.Text = snapshot.OriginTitle;
            if (subtitle is not null) subtitle.Text = snapshot.OriginSubtitle;
        }

        if (snapshot.Phase == renderedPhase)
        {
            return;
        }
        renderedPhase = snapshot.Phase;

        switch (snapshot.Phase)
        {
            case NavigationTransitionPhase.Route:
                Fade(subtitle, 1d, 0d, snapshot.Plan.RouteDuration, TimeSpan.Zero, holdEnd: true);
                break;
            case NavigationTransitionPhase.Shift:
                PlayShift(snapshot);
                break;
            case NavigationTransitionPhase.Relay when snapshot.HasCommitted:
                if (code is not null) code.Text = $"{snapshot.TargetCode} /";
                if (title is not null) title.Text = snapshot.TargetTitle;
                break;
            case NavigationTransitionPhase.Settle:
                if (subtitle is not null) subtitle.Text = snapshot.TargetSubtitle;
                TimeSpan fadeDuration = TimeSpan.FromMilliseconds(Math.Min(70d, snapshot.Plan.SettleDuration.TotalMilliseconds));
                TimeSpan delay = snapshot.Plan.SettleDuration - fadeDuration;
                Fade(subtitle, 0d, 1d, fadeDuration, delay);
                break;
        }
    }

    private void PlayShift(NavigationTransitionSnapshot snapshot)
    {
        double startOpacity = snapshot.Plan.EffectiveLevel == MotionLevel.Full ? 0.58d : 0.68d;
        if (title is not null) title.Text = snapshot.TargetTitle;
        if (code is not null) code.Text = $"{snapshot.TargetCode} /";
        Fade(title, startOpacity, 1d, snapshot.Plan.ShiftDuration, TimeSpan.Zero);
        Fade(code, startOpacity, 1d, snapshot.Plan.ShiftDuration, TimeSpan.Zero);
        if (!snapshot.Plan.AllowsTelemetryTranslation)
        {
            return;
        }

        double titleOffset = snapshot.Plan.EffectiveLevel == MotionLevel.Full ? 6d : 4d;
        double codeOffset = snapshot.Plan.EffectiveLevel == MotionLevel.Full ? 4d : 3d;
        double sign = snapshot.Direction == NavigationTransitionDirection.FromLeft ? -1d : 1d;
        Translate(titleTranslate, sign * titleOffset, snapshot.Plan.ShiftDuration);
        Translate(codeTranslate, sign * codeOffset, snapshot.Plan.ShiftDuration);
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
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Translate(TranslateTransform? transform, double from, TimeSpan duration)
    {
        transform?.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            From = from,
            To = 0d,
            Duration = new Duration(duration),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static void ClearAnimations(UIElement? element, TranslateTransform? transform)
    {
        if (element is not null)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1d;
        }
        if (transform is not null)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.X = 0d;
            transform.Y = 0d;
        }
    }
}
