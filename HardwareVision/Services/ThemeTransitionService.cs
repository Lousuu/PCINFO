using System.Windows.Threading;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class ThemeTransitionService : IThemeTransitionService, IDisposable
{
    private readonly IThemeService themeService;
    private readonly IMotionService motionService;
    private readonly Dispatcher dispatcher;
    private readonly IThemeTransitionClock clock;
    private readonly object sync = new();
    private ThemeTransitionSnapshot current;
    private CancellationTokenSource? activeTransitionCancellation;
    private Task<ThemeTransitionResult>? activeTransitionTask;
    private AppTheme activeTargetTheme;
    private long nextVersion;
    private bool isDisposed;

    public ThemeTransitionService(
        IThemeService themeService,
        IMotionService motionService,
        Dispatcher dispatcher,
        IThemeTransitionClock? clock = null)
    {
        this.themeService = themeService;
        this.motionService = motionService;
        this.dispatcher = dispatcher;
        this.clock = clock ?? new SystemThemeTransitionClock();
        current = ThemeTransitionSnapshot.Idle(themeService.CurrentTheme);
    }

    public event EventHandler<ThemeTransitionChangedEventArgs>? TransitionChanged;

    public ThemeTransitionSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public bool IsTransitioning
    {
        get
        {
            lock (sync)
            {
                return activeTransitionTask is { IsCompleted: false };
            }
        }
    }

    public Task<ThemeTransitionResult> ApplyThemeAsync(AppTheme targetTheme, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Task<ThemeTransitionResult>? previousTask = null;
        CancellationTokenSource transitionCancellation;
        long version;
        AppTheme sourceTheme = themeService.CurrentTheme;

        lock (sync)
        {
            if (activeTransitionTask is { IsCompleted: false })
            {
                if (activeTargetTheme == targetTheme)
                {
                    return activeTransitionTask;
                }

                previousTask = activeTransitionTask;
                activeTransitionCancellation?.Cancel();
            }

            if (sourceTheme == targetTheme && previousTask is null)
            {
                return Task.FromResult(ThemeTransitionResult.AlreadyCurrent(targetTheme));
            }

            version = ++nextVersion;
            activeTargetTheme = targetTheme;
            activeTransitionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            transitionCancellation = activeTransitionCancellation;
            activeTransitionTask = RunTransitionAsync(
                sourceTheme,
                targetTheme,
                version,
                previousTask,
                transitionCancellation.Token);
            return activeTransitionTask;
        }
    }

    public void Cancel()
    {
        lock (sync)
        {
            activeTransitionCancellation?.Cancel();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Cancel();
        lock (sync)
        {
            activeTransitionCancellation?.Dispose();
            activeTransitionCancellation = null;
        }
    }

    private async Task<ThemeTransitionResult> RunTransitionAsync(
        AppTheme sourceTheme,
        AppTheme targetTheme,
        long version,
        Task<ThemeTransitionResult>? previousTask,
        CancellationToken cancellationToken)
    {
        if (previousTask is not null)
        {
            try
            {
                await previousTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        sourceTheme = themeService.CurrentTheme;
        if (sourceTheme == targetTheme)
        {
            PublishIdle(version, targetTheme);
            return ThemeTransitionResult.AlreadyCurrent(targetTheme);
        }

        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(motionService.CurrentProfile);
        if (!plan.IsOverlayEnabled || !plan.UsesClock)
        {
            bool applied = InvokeOnDispatcher(() => themeService.ApplyTheme(targetTheme));
            PublishIdle(version, themeService.CurrentTheme);
            return applied
                ? ThemeTransitionResult.Applied(sourceTheme, targetTheme)
                : ThemeTransitionResult.Failed(sourceTheme, targetTheme, "Theme service rejected target theme.");
        }

        bool committed = false;
        try
        {
            Publish(version, ThemeTransitionPhase.Trace, sourceTheme, targetTheme, plan, committed, null, null);
            await clock.DelayAsync(plan.TraceDuration, cancellationToken).ConfigureAwait(false);

            Publish(version, ThemeTransitionPhase.Latch, sourceTheme, targetTheme, plan, committed, null, null);
            bool applied = InvokeOnDispatcher(() => themeService.ApplyTheme(targetTheme));
            if (!applied)
            {
                Publish(
                    version,
                    ThemeTransitionPhase.Failed,
                    sourceTheme,
                    targetTheme,
                    plan,
                    committed,
                    ThemeTransitionStatus.Failed,
                    "Theme service rejected target theme.");
                PublishIdle(version, themeService.CurrentTheme);
                return ThemeTransitionResult.Failed(sourceTheme, targetTheme, "Theme service rejected target theme.");
            }

            committed = true;
            lock (sync)
            {
                if (activeTransitionTask is not null && activeTargetTheme == targetTheme)
                {
                }
            }

            await clock.DelayAsync(plan.LatchDuration, cancellationToken).ConfigureAwait(false);
            Publish(version, ThemeTransitionPhase.Splice, sourceTheme, targetTheme, plan, committed, null, null);
            await clock.DelayAsync(plan.SpliceDuration, cancellationToken).ConfigureAwait(false);
            PublishIdle(version, targetTheme);
            return ThemeTransitionResult.Applied(sourceTheme, targetTheme);
        }
        catch (OperationCanceledException) when (!isDisposed)
        {
            PublishIdle(version, themeService.CurrentTheme);
            return cancellationToken.IsCancellationRequested
                ? ThemeTransitionResult.Superseded(sourceTheme, targetTheme, committed)
                : ThemeTransitionResult.Cancelled(sourceTheme, targetTheme, committed);
        }
        finally
        {
            lock (sync)
            {
                if (activeTransitionTask is not null
                    && activeTargetTheme == targetTheme
                    && current.Version <= version)
                {
                    activeTransitionCancellation?.Dispose();
                    activeTransitionCancellation = null;
                }
            }
        }
    }

    private void Publish(
        long version,
        ThemeTransitionPhase phase,
        AppTheme sourceTheme,
        AppTheme targetTheme,
        ThemeTransitionPlan plan,
        bool committed,
        ThemeTransitionStatus? terminalStatus,
        string? failureMessage)
    {
        ThemeTransitionSnapshot snapshot = new(
            version,
            phase,
            sourceTheme,
            targetTheme,
            plan,
            IsActive: phase != ThemeTransitionPhase.Idle,
            IsInteractionBlocked: plan.BlocksInteraction && phase != ThemeTransitionPhase.Idle,
            WasThemeCommitted: committed,
            TerminalStatus: terminalStatus,
            FailureMessage: failureMessage);
        Publish(snapshot);
    }

    private void PublishIdle(long version, AppTheme currentTheme)
    {
        ThemeTransitionSnapshot idle = ThemeTransitionSnapshot.Idle(currentTheme) with { Version = version };
        Publish(idle);
    }

    private void Publish(ThemeTransitionSnapshot snapshot)
    {
        void PublishCore()
        {
            ThemeTransitionChangedEventArgs? args = null;
            lock (sync)
            {
                if (snapshot.Version < current.Version)
                {
                    return;
                }

                ThemeTransitionSnapshot previous = current;
                current = snapshot;
                args = new ThemeTransitionChangedEventArgs(previous, snapshot);
            }

            TransitionChanged?.Invoke(this, args);
        }

        if (dispatcher.CheckAccess())
        {
            PublishCore();
        }
        else
        {
            _ = dispatcher.BeginInvoke((Action)PublishCore);
        }
    }

    private T InvokeOnDispatcher<T>(Func<T> action)
    {
        if (dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.Invoke(action);
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(ThemeTransitionService));
        }
    }
}
