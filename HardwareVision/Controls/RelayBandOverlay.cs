using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Controls;

[TemplatePart(Name = BandPartName, Type = typeof(FrameworkElement))]
[TemplatePart(Name = TranslatePartName, Type = typeof(TranslateTransform))]
[TemplatePart(Name = CodePartName, Type = typeof(TextBlock))]
public sealed class RelayBandOverlay : System.Windows.Controls.Control
{
    private const string BandPartName = "PART_Band";
    private const string TranslatePartName = "PART_Translate";
    private const string CodePartName = "PART_Code";

    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(NavigationTransitionSnapshot),
        typeof(RelayBandOverlay),
        new PropertyMetadata(NavigationTransitionSnapshot.Idle(), OnSnapshotChanged));

    private FrameworkElement? band;
    private TranslateTransform? translate;
    private TextBlock? code;
    private long activeVersion = -1;

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
        SizeChanged += (_, _) =>
        {
            if (Snapshot.IsActive && Snapshot.Version == activeVersion)
            {
                StartTransition(Snapshot);
            }
        };
        Unloaded += (_, _) => RestoreFinalState();
    }

    public NavigationTransitionSnapshot Snapshot
    {
        get => (NavigationTransitionSnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    internal double BandWidth => band?.Width ?? 0d;

    internal double BandHeight => band?.Height ?? 0d;

    public override void OnApplyTemplate()
    {
        RestoreFinalState();
        base.OnApplyTemplate();
        band = GetTemplateChild(BandPartName) as FrameworkElement;
        translate = GetTemplateChild(TranslatePartName) as TranslateTransform;
        code = GetTemplateChild(CodePartName) as TextBlock;
        ApplySnapshot(Snapshot);
    }

    public void CancelTransition() => RestoreFinalState();

    public void RestoreFinalState()
    {
        if (band is not null)
        {
            band.BeginAnimation(OpacityProperty, null);
            band.Opacity = 0d;
        }
        if (translate is not null)
        {
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            translate.X = 0d;
            translate.Y = 0d;
        }
        activeVersion = -1;
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

        if (code is not null)
        {
            code.Text = $"{snapshot.OriginCode} → {snapshot.TargetCode}";
        }

        if (activeVersion == snapshot.Version)
        {
            return;
        }

        activeVersion = snapshot.Version;
        StartTransition(snapshot);
    }

    private void StartTransition(NavigationTransitionSnapshot snapshot)
    {
        if (band is null || translate is null || ActualWidth <= 0d || ActualHeight <= 0d)
        {
            return;
        }

        bool horizontal = snapshot.Direction is NavigationTransitionDirection.FromLeft
            or NavigationTransitionDirection.FromRight;
        band.Width = horizontal ? Math.Clamp(ActualWidth * 0.22d, 160d, 300d) : ActualWidth;
        band.Height = horizontal ? ActualHeight : Math.Clamp(ActualHeight * 0.22d, 120d, 220d);
        band.HorizontalAlignment = horizontal
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Stretch;
        band.VerticalAlignment = horizontal
            ? System.Windows.VerticalAlignment.Stretch
            : System.Windows.VerticalAlignment.Top;

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;
        band.Opacity = 1d;

        if (!snapshot.Plan.AllowsRelayTranslation)
        {
            band.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(snapshot.Plan.CommitTime)),
                    new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(snapshot.Plan.TotalDuration))
                },
                FillBehavior = FillBehavior.Stop
            });
            return;
        }

        DependencyProperty property;
        double start;
        double center;
        double end;
        if (horizontal)
        {
            property = TranslateTransform.XProperty;
            center = (ActualWidth - band.Width) / 2d;
            bool fromRight = snapshot.Direction == NavigationTransitionDirection.FromRight;
            start = fromRight ? ActualWidth : -band.Width;
            end = fromRight ? -band.Width : ActualWidth;
        }
        else
        {
            property = TranslateTransform.YProperty;
            center = (ActualHeight - band.Height) / 2d;
            (start, end) = snapshot.Direction switch
            {
                NavigationTransitionDirection.FromBottom => (ActualHeight, -band.Height),
                NavigationTransitionDirection.FromTop => (-band.Height, ActualHeight),
                _ => (0d, 0d)
            };
        }

        DoubleAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(center, KeyTime.FromTimeSpan(snapshot.Plan.CommitTime)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(end, KeyTime.FromTimeSpan(snapshot.Plan.TotalDuration)));
        animation.Completed += (_, _) => RestoreFinalState();
        translate.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
