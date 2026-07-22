using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class NavigationTransitionService : INavigationTransitionService, IDisposable
{
    private readonly INavigationTransitionClock clock;
    private readonly object sync = new();
    private NavigationTransitionSnapshot current = NavigationTransitionSnapshot.Idle();
    private CancellationTokenSource? activeCancellation;
    private Task? activeTask;
    private string? activeTargetPage;
    private long nextVersion;
    private bool isDisposed;

    public NavigationTransitionService(INavigationTransitionClock? clock = null)
    {
        this.clock = clock ?? new SystemNavigationTransitionClock();
    }

    public event EventHandler<NavigationTransitionChangedEventArgs>? TransitionChanged;

    public NavigationTransitionSnapshot CurrentSnapshot
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
                return activeTask is { IsCompleted: false };
            }
        }
    }

    internal Task? ActiveTask
    {
        get
        {
            lock (sync)
            {
                return activeTask;
            }
        }
    }

    public Task NavigateAsync(
        NavigationTransitionIntent intent,
        Func<CancellationToken, Task> commitAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(commitAsync);
        ThrowIfDisposed();

        lock (sync)
        {
            if (activeTask is { IsCompleted: false }
                && string.Equals(activeTargetPage, intent.Target.PageKey, StringComparison.Ordinal))
            {
                return activeTask;
            }

            activeCancellation?.Cancel();
            long version = ++nextVersion;
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeCancellation = cancellation;
            activeTargetPage = intent.Target.PageKey;
            Task task = RunTransitionAsync(intent, commitAsync, version, cancellation);
            activeTask = task;
            return task;
        }
    }

    public void Cancel()
    {
        lock (sync)
        {
            activeCancellation?.Cancel();
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
            activeCancellation?.Dispose();
            activeCancellation = null;
            activeTask = null;
            activeTargetPage = null;
        }

        Publish(NavigationTransitionSnapshot.Idle(version: Interlocked.Increment(ref nextVersion)));
    }

    private async Task RunTransitionAsync(
        NavigationTransitionIntent intent,
        Func<CancellationToken, Task> commitAsync,
        long version,
        CancellationTokenSource cancellation)
    {
        await Task.Yield();
        NavigationTransitionPlan plan = NavigationTransitionPlan.Create(intent.MotionProfile);
        bool committed = false;
        try
        {
            if (!plan.UsesClock || intent.Direction == NavigationTransitionDirection.None)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await commitAsync(cancellation.Token).ConfigureAwait(false);
                committed = true;
                PublishIdle(version, intent.Target);
                return;
            }

            Publish(CreateSnapshot(version, NavigationTransitionPhase.Route, intent, plan, committed));
            await clock.DelayAsync(plan.RouteDuration, cancellation.Token).ConfigureAwait(false);

            Publish(CreateSnapshot(version, NavigationTransitionPhase.Shift, intent, plan, committed));
            await clock.DelayAsync(plan.ShiftDuration, cancellation.Token).ConfigureAwait(false);

            Publish(CreateSnapshot(version, NavigationTransitionPhase.Relay, intent, plan, committed));
            cancellation.Token.ThrowIfCancellationRequested();
            await commitAsync(cancellation.Token).ConfigureAwait(false);
            committed = true;
            Publish(CreateSnapshot(version, NavigationTransitionPhase.Relay, intent, plan, committed));

            Publish(CreateSnapshot(version, NavigationTransitionPhase.Settle, intent, plan, committed));
            await clock.DelayAsync(plan.SettleDuration, cancellation.Token).ConfigureAwait(false);
            PublishIdle(version, intent.Target);
        }
        catch (OperationCanceledException)
        {
            PublishIdle(version, committed ? intent.Target : intent.Origin);
        }
        catch
        {
            PublishIdle(version, committed ? intent.Target : intent.Origin);
            throw;
        }
        finally
        {
            lock (sync)
            {
                if (activeTask is not null && activeTargetPage == intent.Target.PageKey && current.Version <= version)
                {
                    activeCancellation?.Dispose();
                    activeCancellation = null;
                    activeTask = null;
                    activeTargetPage = null;
                }
            }
        }
    }

    private static NavigationTransitionSnapshot CreateSnapshot(
        long version,
        NavigationTransitionPhase phase,
        NavigationTransitionIntent intent,
        NavigationTransitionPlan plan,
        bool committed) => new(
            version,
            true,
            phase,
            intent.Direction,
            intent.Origin.PageKey,
            intent.Target.PageKey,
            intent.Origin.Code,
            intent.Target.Code,
            intent.Origin.Title,
            intent.Target.Title,
            intent.Origin.Subtitle,
            intent.Target.Subtitle,
            plan,
            committed);

    private void PublishIdle(long version, NavigationRouteDescriptor currentRoute) =>
        Publish(NavigationTransitionSnapshot.Idle(currentRoute, version));

    private void Publish(NavigationTransitionSnapshot snapshot)
    {
        NavigationTransitionChangedEventArgs? args;
        lock (sync)
        {
            if (snapshot.Version < current.Version)
            {
                return;
            }

            NavigationTransitionSnapshot previous = current;
            current = snapshot;
            args = new NavigationTransitionChangedEventArgs(previous, snapshot);
        }

        Delegate[] subscribers = TransitionChanged?.GetInvocationList() ?? [];
        foreach (Delegate subscriber in subscribers)
        {
            try
            {
                ((EventHandler<NavigationTransitionChangedEventArgs>)subscriber)(this, args);
            }
            catch (Exception exception)
            {
                AppLogger.LogError(
                    "FLOW RELAY transition subscriber failed.",
                    exception,
                    $"flow-relay-subscriber:{subscriber.Method.DeclaringType?.FullName}:{subscriber.Method.Name}",
                    TimeSpan.FromMinutes(5));
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(NavigationTransitionService));
        }
    }
}
