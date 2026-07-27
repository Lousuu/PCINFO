using System.Windows;
using System.Windows.Media.Animation;
using HardwareVision.Models;

namespace HardwareVision.Controls;

public sealed class StartupShellRevealCoordinator
{
    private readonly IReadOnlyList<FrameworkElement> targets;
    private bool prepared;
    private bool revealPlayed;
    private bool pageVisualCompleted;
    private bool visualCompleted;
    private int pendingShellAnimations;
    private long revealVersion = -1;

    public StartupShellRevealCoordinator(params FrameworkElement[] targets)
    {
        this.targets = targets;
        foreach (MotionTransitionHost pageHost in targets.OfType<MotionTransitionHost>())
        {
            pageHost.StartupRevealVisualCompleted += OnPageVisualCompleted;
        }
    }

    public event EventHandler<StartupRevealVisualCompletedEventArgs>? VisualCompleted;

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
                target.Opacity = target is MotionTransitionHost
                    ? 1d
                    : snapshot.MotionLevel == MotionLevel.Off ? 1d : 0d;
                target.IsHitTestVisible = false;
                if (target is MotionTransitionHost pageHost)
                {
                    pageHost.PrepareStartupReveal(snapshot.MotionLevel);
                }
            }
        }
        else
        {
            foreach (MotionTransitionHost pageHost in targets.OfType<MotionTransitionHost>())
            {
                pageHost.PrepareStartupReveal(snapshot.MotionLevel);
            }
        }

        if (snapshot.Phase != StartupSequencePhase.Reveal || revealPlayed)
        {
            return;
        }

        revealPlayed = true;
        revealVersion = snapshot.Version;
        pageVisualCompleted = false;
        visualCompleted = false;
        pendingShellAnimations = 0;
        PlayReveal(snapshot);
    }

    public void RestoreFinalState()
    {
        foreach (FrameworkElement target in targets)
        {
            ClearAnimations(target);
            target.Opacity = 1d;
            target.IsHitTestVisible = true;
            if (target is MotionTransitionHost pageHost)
            {
                pageHost.CompleteStartupReveal();
            }
        }

        prepared = false;
        revealPlayed = false;
        pageVisualCompleted = false;
        visualCompleted = false;
        pendingShellAnimations = 0;
        revealVersion = -1;
    }

    private void PlayReveal(StartupSequenceSnapshot snapshot)
    {
        if (snapshot.MotionLevel == MotionLevel.Off)
        {
            RestoreFinalState();
            VisualCompleted?.Invoke(
                this,
                new StartupRevealVisualCompletedEventArgs(snapshot.Version));
            return;
        }

        if (snapshot.CurrentTheme == AppTheme.Classic)
        {
            pageVisualCompleted = true;
            foreach (FrameworkElement target in targets)
            {
                BeginShellOpacity(
                    target,
                    TimeSpan.Zero,
                    TimeSpan.FromMilliseconds(120));
            }
            return;
        }

        if (snapshot.MotionLevel == MotionLevel.Reduced)
        {
            foreach (FrameworkElement target in targets)
            {
                if (target is MotionTransitionHost pageHost)
                {
                    pageHost.PlayStartupReveal(snapshot.MotionLevel);
                }
                else
                {
                    BeginShellOpacity(
                        target,
                        TimeSpan.FromMilliseconds(60),
                        TimeSpan.FromMilliseconds(120));
                }
            }
            return;
        }

        for (int index = 0; index < targets.Count; index++)
        {
            if (targets[index] is MotionTransitionHost pageHost)
            {
                pageHost.PlayStartupReveal(snapshot.MotionLevel);
                continue;
            }

            (TimeSpan delay, TimeSpan duration) =
                ResolveTraceworkTiming(snapshot.MotionLevel, index);
            BeginShellOpacity(targets[index], delay, duration);
        }
    }

    private void BeginShellOpacity(
        FrameworkElement target,
        TimeSpan delay,
        TimeSpan duration)
    {
        pendingShellAnimations++;
        AnimateOpacity(
            target,
            delay,
            duration,
            () =>
            {
                pendingShellAnimations--;
                TryCompleteVisual();
            });
    }

    private void OnPageVisualCompleted(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        pageVisualCompleted = true;
        TryCompleteVisual();
    }

    private void TryCompleteVisual()
    {
        bool hasPageHost = targets.OfType<MotionTransitionHost>().Any();
        if (!revealPlayed
            || visualCompleted
            || pendingShellAnimations != 0
            || hasPageHost && !pageVisualCompleted)
        {
            return;
        }

        visualCompleted = true;
        long completedVersion = revealVersion;
        VisualCompleted?.Invoke(
            this,
            new StartupRevealVisualCompletedEventArgs(completedVersion));
    }

    internal static (TimeSpan Delay, TimeSpan Duration) ResolveTraceworkTiming(
        MotionLevel level,
        int targetIndex)
    {
        int index = Math.Clamp(targetIndex, 0, 3);
        if (level == MotionLevel.Full)
        {
            TimeSpan[] delays =
            [
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(140),
                TimeSpan.FromMilliseconds(120),
                TimeSpan.FromMilliseconds(190)
            ];
            TimeSpan[] durations =
            [
                TimeSpan.FromMilliseconds(220),
                TimeSpan.FromMilliseconds(190),
                TimeSpan.FromMilliseconds(220),
                TimeSpan.FromMilliseconds(185)
            ];
            return (delays[index], durations[index]);
        }

        TimeSpan[] standardDelays =
        [
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(114),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(144)
        ];
        TimeSpan[] standardDurations =
        [
            TimeSpan.FromMilliseconds(180),
            TimeSpan.FromMilliseconds(166),
            TimeSpan.FromMilliseconds(180),
            TimeSpan.FromMilliseconds(166)
        ];
        return (standardDelays[index], standardDurations[index]);
    }

    private static void AnimateOpacity(
        FrameworkElement target,
        TimeSpan delay,
        TimeSpan duration,
        Action completed)
    {
        DoubleAnimationUsingKeyFrames animation = new() { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(delay)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(
            1d,
            KeyTime.FromTimeSpan(delay + duration),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        animation.Completed += (_, _) => completed();
        target.Opacity = 1d;
        target.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void ClearAnimations(FrameworkElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
    }
}

public sealed class StartupRevealVisualCompletedEventArgs(long startupVersion) : EventArgs
{
    public long StartupVersion { get; } = startupVersion;
}
