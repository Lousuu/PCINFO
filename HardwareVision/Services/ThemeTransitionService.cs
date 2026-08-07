using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Utilities;

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
    private TaskCompletionSource<ThemeVisualReadinessResult>? visualReadinessCompletion;
    private long visualReadinessVersion = -1;
    private AppTheme visualReadinessTarget;
    private AppTheme activeTargetTheme;
    private long nextVersion;
    private bool isDisposed;

    internal ThemeVisualReadinessResult? LastVisualReadinessResult { get; private set; }

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

    public bool ReportVisualReady(
        long version,
        AppTheme targetTheme,
        ThemeVisualReadinessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TaskCompletionSource<ThemeVisualReadinessResult>? completion;
        lock (sync)
        {
            if (isDisposed
                || version != visualReadinessVersion
                || targetTheme != visualReadinessTarget
                || current.Version > version)
            {
                return false;
            }

            completion = visualReadinessCompletion;
        }

        bool reported = completion?.TrySetResult(result) == true;
        if (reported)
        {
            LastVisualReadinessResult = result;
        }
        return reported;
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
            visualReadinessCompletion?.TrySetCanceled();
            visualReadinessCompletion = null;
            visualReadinessVersion = -1;
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
            await PublishIdleAsync(version, targetTheme).ConfigureAwait(false);
            return ThemeTransitionResult.AlreadyCurrent(targetTheme);
        }

        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(motionService.CurrentProfile);
        if (!plan.IsOverlayEnabled || !plan.UsesClock)
        {
            bool applied = await InvokeOnDispatcherAsync(
                () => themeService.ApplyTheme(targetTheme)).ConfigureAwait(false);
            await PublishIdleAsync(version, themeService.CurrentTheme).ConfigureAwait(false);
            return applied
                ? ThemeTransitionResult.Applied(sourceTheme, targetTheme)
                : ThemeTransitionResult.Failed(sourceTheme, targetTheme, "Theme service rejected target theme.");
        }

        bool committed = false;
        try
        {
            await PublishAsync(
                version,
                ThemeTransitionPhase.Trace,
                sourceTheme,
                targetTheme,
                plan,
                committed,
                null,
                null).ConfigureAwait(false);
            await clock.DelayAsync(plan.TraceDuration, cancellationToken).ConfigureAwait(false);

            await PublishAsync(
                version,
                ThemeTransitionPhase.Latch,
                sourceTheme,
                targetTheme,
                plan,
                committed,
                null,
                null).ConfigureAwait(false);
            ArmVisualReadinessGate(version, targetTheme);
            bool applied = await InvokeOnDispatcherAsync(
                () => themeService.ApplyTheme(targetTheme)).ConfigureAwait(false);
            if (!applied)
            {
                await PublishAsync(
                    version,
                    ThemeTransitionPhase.Failed,
                    sourceTheme,
                    targetTheme,
                    plan,
                    committed,
                    ThemeTransitionStatus.Failed,
                    "Theme service rejected target theme.").ConfigureAwait(false);
                await PublishIdleAsync(version, themeService.CurrentTheme).ConfigureAwait(false);
                return ThemeTransitionResult.Failed(sourceTheme, targetTheme, "Theme service rejected target theme.");
            }

            committed = true;
            LogThemeDiagnostic(
                "ThemeResourcesApplied",
                version,
                targetTheme,
                "CurrentTheme updated");
            await WaitForVisualReadinessAsync(
                version,
                targetTheme,
                cancellationToken).ConfigureAwait(false);
            await PublishAsync(
                version,
                ThemeTransitionPhase.Splice,
                sourceTheme,
                targetTheme,
                plan,
                committed,
                null,
                null).ConfigureAwait(false);
            await clock.DelayAsync(plan.SpliceDuration, cancellationToken).ConfigureAwait(false);
            await PublishIdleAsync(version, targetTheme).ConfigureAwait(false);
            return ThemeTransitionResult.Applied(sourceTheme, targetTheme);
        }
        catch (OperationCanceledException) when (!isDisposed)
        {
            QueueIdleAfterCancellation(version, themeService.CurrentTheme);
            return cancellationToken.IsCancellationRequested
                ? ThemeTransitionResult.Superseded(sourceTheme, targetTheme, committed)
                : ThemeTransitionResult.Cancelled(sourceTheme, targetTheme, committed);
        }
        finally
        {
            lock (sync)
            {
                if (visualReadinessVersion == version)
                {
                    visualReadinessCompletion?.TrySetCanceled();
                    visualReadinessCompletion = null;
                    visualReadinessVersion = -1;
                }
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

    private void ArmVisualReadinessGate(long version, AppTheme targetTheme)
    {
        lock (sync)
        {
            visualReadinessCompletion?.TrySetCanceled();
            visualReadinessVersion = version;
            visualReadinessTarget = targetTheme;
            LastVisualReadinessResult = null;
            visualReadinessCompletion =
                new TaskCompletionSource<ThemeVisualReadinessResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
        }
        LogThemeDiagnostic(
            "ThemeVisualGateArmed",
            version,
            targetTheme,
            "Awaiting target layout/render");
    }

    private async Task WaitForVisualReadinessAsync(
        long version,
        AppTheme targetTheme,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<ThemeVisualReadinessResult>? completion;
        lock (sync)
        {
            completion = visualReadinessVersion == version
                ? visualReadinessCompletion
                : null;
        }
        if (completion is null)
        {
            return;
        }

        using CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timeout = clock.DelayAsync(
            TimeSpan.FromMilliseconds(900),
            timeoutCancellation.Token);
        Task winner = await Task.WhenAny(completion.Task, timeout).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (winner == completion.Task && completion.Task.IsCompletedSuccessfully)
        {
            timeoutCancellation.Cancel();
            ThemeVisualReadinessResult result = await completion.Task.ConfigureAwait(false);
            LogThemeDiagnostic(
                result.IsReady ? "ThemeVisualReady" : "ThemeVisualTimeout",
                version,
                targetTheme,
                result.IsReady
                    ? $"renderPasses={result.RenderPassCount}"
                    : result.FailureReason);
        }
        else
        {
            LogThemeDiagnostic(
                "ThemeVisualTimeout",
                version,
                targetTheme,
                "timeout=900ms");
        }

        lock (sync)
        {
            if (visualReadinessVersion == version)
            {
                visualReadinessCompletion = null;
                visualReadinessVersion = -1;
            }
        }
    }

    private static void LogThemeDiagnostic(
        string eventName,
        long version,
        AppTheme targetTheme,
        string reason)
    {
        AppLogger.LogKeyEvent(
            $"{eventName} | version={version}; target={targetTheme}; reason={reason}");
    }

    private Task PublishAsync(
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
        return PublishAsync(snapshot);
    }

    private Task PublishIdleAsync(
        long version,
        AppTheme currentTheme)
    {
        ThemeTransitionSnapshot idle = ThemeTransitionSnapshot.Idle(currentTheme) with { Version = version };
        return PublishAsync(idle);
    }

    private void QueueIdleAfterCancellation(long version, AppTheme currentTheme)
    {
        ThemeTransitionSnapshot idle = ThemeTransitionSnapshot.Idle(currentTheme) with { Version = version };
        if (dispatcher.CheckAccess())
        {
            PublishCore(idle);
            return;
        }

        _ = dispatcher.BeginInvoke(
            (Action)(() => PublishCore(idle)),
            DispatcherPriority.Normal);
    }

    private async Task PublishAsync(ThemeTransitionSnapshot snapshot)
    {
        if (dispatcher.CheckAccess())
        {
            PublishCore(snapshot);
        }
        else
        {
            await dispatcher.InvokeAsync(
                () => PublishCore(snapshot),
                DispatcherPriority.Normal);
        }
    }

    private void PublishCore(ThemeTransitionSnapshot snapshot)
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

    private async Task<T> InvokeOnDispatcherAsync<T>(Func<T> action)
    {
        if (dispatcher.CheckAccess())
        {
            return action();
        }

        return await dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(ThemeTransitionService));
        }
    }
}
