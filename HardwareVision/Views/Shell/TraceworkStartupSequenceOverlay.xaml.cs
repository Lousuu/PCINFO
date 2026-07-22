using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Views.Shell;

public partial class TraceworkStartupSequenceOverlay : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot),
        typeof(StartupSequenceSnapshot),
        typeof(TraceworkStartupSequenceOverlay),
        new PropertyMetadata(null, OnSnapshotChanged));

    private bool commitFlashPlayed;
    private bool internalPulsePlayed;
    private bool routeStaggerPlayed;
    private long latestVersion = -1;

    public TraceworkStartupSequenceOverlay()
    {
        InitializeComponent();
        Unloaded += (_, _) => RestoreFinalState();
    }

    public StartupSequenceSnapshot? Snapshot
    {
        get => (StartupSequenceSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public void PrepareFirstFrame(AppTheme theme, MotionLevel motionLevel)
    {
        if (theme != AppTheme.Tracework || motionLevel == MotionLevel.Off)
        {
            RestoreFinalState();
            return;
        }

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;
        StartupBackgroundLayer.Opacity = 1d;
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
    }

    public void RestoreFinalState()
    {
        BeginAnimation(OpacityProperty, null);
        StartupBackgroundLayer.BeginAnimation(OpacityProperty, null);
        StartupContentLayer.BeginAnimation(OpacityProperty, null);
        StartupBottomRailLayer.BeginAnimation(OpacityProperty, null);
        SystemIndexText.BeginAnimation(OpacityProperty, null);
        InternalPulse.BeginAnimation(OpacityProperty, null);
        InternalPulseTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CommitGroup.BeginAnimation(OpacityProperty, null);
        CommitLock.BeginAnimation(OpacityProperty, null);
        foreach (FrameworkElement row in RouteMatrixItems.Items.Cast<object>()
                     .Select(item => RouteMatrixItems.ItemContainerGenerator.ContainerFromItem(item))
                     .OfType<FrameworkElement>())
        {
            row.BeginAnimation(OpacityProperty, null);
            if (row.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.YProperty, null);
                transform.Y = 0d;
            }
        }
        SystemIndexClipHost.Clip = null;
        Opacity = 0d;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        InternalPulse.Opacity = 0d;
        InternalPulseTransform.X = 0d;
        CommitLock.Opacity = 0d;
        CommitGroup.Opacity = 0d;
        CommitGroup.Visibility = Visibility.Collapsed;
        StartupBackgroundLayer.Opacity = 0d;
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
    }

    private static void OnSnapshotChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TraceworkStartupSequenceOverlay overlay)
        {
            overlay.ApplySnapshot(e.NewValue as StartupSequenceSnapshot);
        }
    }

    private void ApplySnapshot(StartupSequenceSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Version < latestVersion)
        {
            return;
        }

        latestVersion = snapshot.Version;
        OverlayRoot.DataContext = snapshot;
        if (snapshot.HasCompleted
            || snapshot.CurrentTheme != AppTheme.Tracework
            || snapshot.MotionLevel == MotionLevel.Off)
        {
            RestoreFinalState();
            return;
        }

        if (!snapshot.IsActive || snapshot.Phase == StartupSequencePhase.Dormant)
        {
            PrepareFirstFrame(snapshot.CurrentTheme, snapshot.MotionLevel);
            return;
        }

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;
        StartupBackgroundLayer.Opacity = 1d;
        StartupContentLayer.Opacity = 1d;
        StartupBottomRailLayer.Opacity = 1d;
        bool commitVisible = snapshot.Phase == StartupSequencePhase.Lock && snapshot.CanCommit;
        CommitGroup.Visibility = commitVisible ? Visibility.Visible : Visibility.Collapsed;
        CommitGroup.Opacity = commitVisible ? 1d : 0d;
        if (!commitVisible)
        {
            CommitLock.BeginAnimation(OpacityProperty, null);
            CommitLock.Opacity = 0d;
        }

        if (snapshot.Phase == StartupSequencePhase.Reveal)
        {
            PlayConcurrentExit(snapshot.MotionLevel);
        }

        if (snapshot.MotionLevel == MotionLevel.Reduced)
        {
            SystemIndexClipHost.Clip = null;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(80))
            {
                FillBehavior = FillBehavior.Stop
            });
        }
        else if (snapshot.Phase == StartupSequencePhase.Index)
        {
            PlayIndexReveal(snapshot.MotionLevel);
        }
        else if (snapshot.Phase == StartupSequencePhase.Route
                 && !routeStaggerPlayed)
        {
            routeStaggerPlayed = true;
            PlayRouteStagger(snapshot.MotionLevel);
        }
        else if (snapshot.Phase == StartupSequencePhase.Bind
                 && snapshot.MotionLevel == MotionLevel.Full
                 && !internalPulsePlayed)
        {
            internalPulsePlayed = true;
            PlayInternalPulse();
        }

        if (snapshot.Phase == StartupSequencePhase.Lock
            && snapshot.CanCommit
            && !commitFlashPlayed)
        {
            commitFlashPlayed = true;
            PlayCommitLock(snapshot.MotionLevel);
        }
    }

    private void PlayRouteStagger(MotionLevel level)
    {
        FrameworkElement[] rows = RouteMatrixItems.Items.Cast<object>()
            .Select(item => RouteMatrixItems.ItemContainerGenerator.ContainerFromItem(item))
            .OfType<FrameworkElement>()
            .ToArray();
        for (int index = 0; index < rows.Length; index++)
        {
            FrameworkElement row = rows[index];
            TimeSpan delay = TimeSpan.FromMilliseconds(index * (level == MotionLevel.Full ? 24 : 18));
            DoubleAnimationUsingKeyFrames opacity = new() { FillBehavior = FillBehavior.Stop };
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(delay)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(100))));
            row.Opacity = 1d;
            row.BeginAnimation(OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
            if (level == MotionLevel.Full)
            {
                TranslateTransform transform = row.RenderTransform as TranslateTransform ?? new TranslateTransform();
                row.RenderTransform = transform;
                transform.Y = 0d;
                transform.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(6d, 0d, TimeSpan.FromMilliseconds(120))
                    {
                        BeginTime = delay,
                        FillBehavior = FillBehavior.Stop,
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    }, HandoffBehavior.SnapshotAndReplace);
            }
        }
    }

    private void PlayConcurrentExit(MotionLevel level)
    {
        TimeSpan duration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(380)
            : level == MotionLevel.Standard
                ? TimeSpan.FromMilliseconds(260)
                : TimeSpan.FromMilliseconds(150);
        StartupContentLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1d, 0d, duration) { FillBehavior = FillBehavior.Stop },
            HandoffBehavior.SnapshotAndReplace);
        StartupBottomRailLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1d, 0d, duration) { FillBehavior = FillBehavior.Stop },
            HandoffBehavior.SnapshotAndReplace);
        StartupBackgroundLayer.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1d, 0d, duration) { FillBehavior = FillBehavior.Stop },
            HandoffBehavior.SnapshotAndReplace);
        StartupContentLayer.Opacity = 0d;
        StartupBottomRailLayer.Opacity = 0d;
        StartupBackgroundLayer.Opacity = 0d;
    }

    private void PlayIndexReveal(MotionLevel level)
    {
        double width = Math.Max(1d, SystemIndexText.ActualWidth);
        double height = Math.Max(1d, SystemIndexText.ActualHeight);
        RectangleGeometry clip = new(new Rect(0d, 0d, width, height));
        SystemIndexClipHost.Clip = clip;
        TimeSpan duration = level == MotionLevel.Full
            ? TimeSpan.FromMilliseconds(180)
            : TimeSpan.FromMilliseconds(120);
        clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(
            new Rect(0d, 0d, 0d, height),
            new Rect(0d, 0d, width, height),
            duration)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void PlayInternalPulse()
    {
        InternalPulse.Opacity = 0d;
        InternalPulseTransform.X = 0d;
        DoubleAnimationUsingKeyFrames pulse = new() { FillBehavior = FillBehavior.Stop };
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.8d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
        InternalPulse.BeginAnimation(OpacityProperty, pulse, HandoffBehavior.SnapshotAndReplace);
        InternalPulseTransform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(0d, 240d, TimeSpan.FromMilliseconds(260))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void PlayCommitLock(MotionLevel level)
    {
        CommitGroup.Visibility = Visibility.Visible;
        CommitGroup.Opacity = 1d;
        CommitLock.Opacity = 0.65d;
        TimeSpan peak = level == MotionLevel.Reduced
            ? TimeSpan.FromMilliseconds(30)
            : TimeSpan.FromMilliseconds(45);
        TimeSpan end = level == MotionLevel.Reduced
            ? TimeSpan.FromMilliseconds(90)
            : TimeSpan.FromMilliseconds(150);
        DoubleAnimationUsingKeyFrames flash = new() { FillBehavior = FillBehavior.Stop };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.65d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(peak)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.65d, KeyTime.FromTimeSpan(end)));
        flash.Completed += (_, _) =>
        {
            CommitLock.BeginAnimation(OpacityProperty, null);
            CommitLock.Opacity = 0.65d;
        };
        CommitLock.BeginAnimation(OpacityProperty, flash, HandoffBehavior.SnapshotAndReplace);
    }
}
