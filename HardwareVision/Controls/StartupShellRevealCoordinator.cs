using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Controls;

public sealed class StartupShellRevealCoordinator
{
    private readonly IReadOnlyList<FrameworkElement> targets;
    private bool prepared;
    private bool revealPlayed;

    public StartupShellRevealCoordinator(params FrameworkElement[] targets)
    {
        this.targets = targets;
    }

    public void Apply(StartupSequenceSnapshot snapshot)
    {
        if (snapshot.HasCompleted || !snapshot.IsActive)
        {
            RestoreFinalState();
            return;
        }

        if (!prepared)
        {
            prepared = true;
            foreach (FrameworkElement target in targets)
            {
                ClearAnimations(target);
                target.Opacity = snapshot.MotionLevel == MotionLevel.Off ? 1d : 0d;
                target.IsHitTestVisible = false;
            }
        }

        if (snapshot.Phase != StartupSequencePhase.Reveal || revealPlayed)
        {
            return;
        }

        revealPlayed = true;
        PlayReveal(snapshot);
    }

    public void RestoreFinalState()
    {
        foreach (FrameworkElement target in targets)
        {
            ClearAnimations(target);
            target.Opacity = 1d;
            target.Clip = null;
            target.IsHitTestVisible = true;
        }

        prepared = false;
        revealPlayed = false;
    }

    private void PlayReveal(StartupSequenceSnapshot snapshot)
    {
        if (snapshot.MotionLevel == MotionLevel.Off)
        {
            RestoreFinalState();
            return;
        }

        if (snapshot.CurrentTheme == AppTheme.Classic)
        {
            foreach (FrameworkElement target in targets)
            {
                AnimateOpacity(target, TimeSpan.Zero, TimeSpan.FromMilliseconds(120));
            }
            return;
        }

        if (snapshot.MotionLevel == MotionLevel.Reduced)
        {
            foreach (FrameworkElement target in targets)
            {
                AnimateOpacity(target, TimeSpan.Zero, TimeSpan.FromMilliseconds(150));
            }
            return;
        }

        TimeSpan[] delays;
        TimeSpan[] durations;
        if (snapshot.MotionLevel == MotionLevel.Full)
        {
            delays =
            [
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(45),
                TimeSpan.FromMilliseconds(90),
                TimeSpan.FromMilliseconds(210)
            ];
            durations =
            [
                TimeSpan.FromMilliseconds(70),
                TimeSpan.FromMilliseconds(70),
                TimeSpan.FromMilliseconds(190),
                TimeSpan.FromMilliseconds(70)
            ];
        }
        else
        {
            delays =
            [
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(30),
                TimeSpan.FromMilliseconds(58),
                TimeSpan.FromMilliseconds(150)
            ];
            durations =
            [
                TimeSpan.FromMilliseconds(55),
                TimeSpan.FromMilliseconds(55),
                TimeSpan.FromMilliseconds(140),
                TimeSpan.FromMilliseconds(55)
            ];
        }

        for (int index = 0; index < targets.Count; index++)
        {
            TimeSpan delay = delays[Math.Min(index, delays.Length - 1)];
            TimeSpan duration = durations[Math.Min(index, durations.Length - 1)];
            AnimateOpacity(targets[index], delay, duration);
            AnimateClip(targets[index], delay, duration);
        }
    }

    private static void AnimateOpacity(FrameworkElement target, TimeSpan delay, TimeSpan duration)
    {
        DoubleAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(
            1d,
            KeyTime.FromTimeSpan(delay + duration),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        target.Opacity = 1d;
        target.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateClip(FrameworkElement target, TimeSpan delay, TimeSpan duration)
    {
        if (target.ActualWidth <= 0d || target.ActualHeight <= 0d)
        {
            return;
        }

        RectangleGeometry clip = new(new Rect(0d, 0d, target.ActualWidth, target.ActualHeight));
        target.Clip = clip;
        RectAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteRectKeyFrame(
            new Rect(0d, 0d, 0d, target.ActualHeight),
            KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new EasingRectKeyFrame(
            new Rect(0d, 0d, target.ActualWidth, target.ActualHeight),
            KeyTime.FromTimeSpan(delay + duration),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        clip.Rect = new Rect(0d, 0d, target.ActualWidth, target.ActualHeight);
        clip.BeginAnimation(RectangleGeometry.RectProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void ClearAnimations(FrameworkElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        if (target.Clip is RectangleGeometry clip)
        {
            clip.BeginAnimation(RectangleGeometry.RectProperty, null);
        }
    }
}
