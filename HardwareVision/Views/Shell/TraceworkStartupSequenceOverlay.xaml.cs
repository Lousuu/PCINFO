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

    public void RestoreFinalState()
    {
        BeginAnimation(OpacityProperty, null);
        SystemIndexText.BeginAnimation(OpacityProperty, null);
        InternalPulse.BeginAnimation(OpacityProperty, null);
        InternalPulseTransform.BeginAnimation(TranslateTransform.XProperty, null);
        CommitLock.BeginAnimation(OpacityProperty, null);
        SystemIndexClipHost.Clip = null;
        Opacity = 0d;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = false;
        InternalPulse.Opacity = 0d;
        InternalPulseTransform.X = 0d;
        CommitLock.Opacity = 0d;
        CommitLock.Visibility = Visibility.Collapsed;
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
        DataContext = snapshot;
        if (!snapshot.IsActive
            || snapshot.HasCompleted
            || snapshot.CurrentTheme != AppTheme.Tracework
            || snapshot.MotionLevel == MotionLevel.Off)
        {
            RestoreFinalState();
            return;
        }

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        Opacity = 1d;

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
        CommitLock.Visibility = Visibility.Visible;
        TimeSpan peak = level == MotionLevel.Reduced
            ? TimeSpan.FromMilliseconds(30)
            : TimeSpan.FromMilliseconds(45);
        TimeSpan end = level == MotionLevel.Reduced
            ? TimeSpan.FromMilliseconds(90)
            : TimeSpan.FromMilliseconds(150);
        DoubleAnimationUsingKeyFrames flash = new() { FillBehavior = FillBehavior.Stop };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(peak)));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(end)));
        flash.Completed += (_, _) =>
        {
            CommitLock.BeginAnimation(OpacityProperty, null);
            CommitLock.Opacity = 0d;
            CommitLock.Visibility = Visibility.Collapsed;
        };
        CommitLock.BeginAnimation(OpacityProperty, flash, HandoffBehavior.SnapshotAndReplace);
    }
}
