using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class NavigationTransitionServiceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Flow service 01 phase order", TestSupport.Run(PhaseOrderAsync)),
        ("Flow service 02 commit occurs at Relay", TestSupport.Run(CommitOccursAtRelayAsync)),
        ("Flow service 03 commit executes once", TestSupport.Run(CommitExecutesOnceAsync)),
        ("Flow service 04 same target reuses task", TestSupport.Run(SameTargetReusesTaskAsync)),
        ("Flow service 05 different target cancels before commit", TestSupport.Run(DifferentTargetCancelsBeforeCommitAsync)),
        ("Flow service 06 canceled target does not commit", TestSupport.Run(CanceledTargetDoesNotCommitAsync)),
        ("Flow service 07 cancel after commit returns Idle", TestSupport.Run(CancelAfterCommitReturnsIdleAsync)),
        ("Flow service 08 rapid twenty requests keep latest", TestSupport.Run(RapidTwentyRequestsKeepLatestAsync)),
        ("Flow service 09 versions are monotonic", TestSupport.Run(VersionsAreMonotonicAsync)),
        ("Flow service 10 clock failure returns Idle", TestSupport.Run(ClockFailureReturnsIdleAsync)),
        ("Flow service 11 commit failure returns Idle", TestSupport.Run(CommitFailureReturnsIdleAsync)),
        ("Flow service 12 subscriber failure is isolated", TestSupport.Run(SubscriberFailureIsIsolatedAsync)),
        ("Flow service 13 external cancellation returns Idle", TestSupport.Run(ExternalCancellationReturnsIdleAsync)),
        ("Flow service 14 dispose returns Idle", TestSupport.Run(DisposeReturnsIdleAsync)),
        ("Flow service 15 active task is cleared", TestSupport.Run(ActiveTaskIsClearedAsync)),
        ("Flow service 16 Off uses no clock", TestSupport.Run(OffUsesNoClockAsync)),
        ("Flow service 17 stale snapshots do not replace latest", TestSupport.Run(StaleSnapshotsDoNotReplaceLatestAsync))
    ];

    private static NavigationTransitionIntent Intent(string target, MotionLevel level = MotionLevel.Standard, string origin = "Dashboard") =>
        new(Route(origin), Route(target), MotionProfile.Create(level, level, string.Empty));

    private static NavigationRouteDescriptor Route(string key)
    {
        _ = NavigationRouteDescriptor.TryCreate(key, key, key, key, out NavigationRouteDescriptor? route);
        return TestSupport.NotNull(route, key);
    }

    private static async Task PhaseOrderAsync()
    {
        NavigationTransitionService service = new(new ImmediateClock());
        List<NavigationTransitionPhase> phases = [];
        service.TransitionChanged += (_, e) => phases.Add(e.CurrentSnapshot.Phase);
        await service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        string expected = string.Join(',', new[]
        {
            NavigationTransitionPhase.Route,
            NavigationTransitionPhase.Shift,
            NavigationTransitionPhase.Relay,
            NavigationTransitionPhase.Relay,
            NavigationTransitionPhase.Settle,
            NavigationTransitionPhase.Idle
        });
        TestSupport.Equal(expected, string.Join(',', phases), "phase order");
    }

    private static async Task CommitOccursAtRelayAsync()
    {
        NavigationTransitionService service = new(new ImmediateClock());
        NavigationTransitionPhase phaseAtCommit = NavigationTransitionPhase.Idle;
        await service.NavigateAsync(Intent("Gpu"), _ =>
        {
            phaseAtCommit = service.CurrentSnapshot.Phase;
            return Task.CompletedTask;
        });
        TestSupport.Equal(NavigationTransitionPhase.Relay, phaseAtCommit, "commit phase");
    }

    private static async Task CommitExecutesOnceAsync()
    {
        NavigationTransitionService service = new(new ImmediateClock());
        int commits = 0;
        await service.NavigateAsync(Intent("Gpu"), _ => { commits++; return Task.CompletedTask; });
        TestSupport.Equal(1, commits, "commit count");
    }

    private static async Task SameTargetReusesTaskAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        Task first = service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        await clock.Entered.Task;
        Task second = service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        TestSupport.True(ReferenceEquals(first, second), "same target task");
        clock.Release();
        await first;
    }

    private static async Task DifferentTargetCancelsBeforeCommitAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        int firstCommits = 0;
        int secondCommits = 0;
        Task first = service.NavigateAsync(Intent("Cpu"), _ => { firstCommits++; return Task.CompletedTask; });
        await clock.Entered.Task;
        Task second = service.NavigateAsync(Intent("Gpu"), _ => { secondCommits++; return Task.CompletedTask; });
        clock.Release();
        await Task.WhenAll(first, second);
        TestSupport.Equal(0, firstCommits, "first canceled commit");
        TestSupport.Equal(1, secondCommits, "second commit");
    }

    private static Task CanceledTargetDoesNotCommitAsync() => DifferentTargetCancelsBeforeCommitAsync();

    private static async Task CancelAfterCommitReturnsIdleAsync()
    {
        ThirdGateClock clock = new();
        NavigationTransitionService service = new(clock);
        int commits = 0;
        Task task = service.NavigateAsync(Intent("Gpu"), _ => { commits++; return Task.CompletedTask; });
        await clock.ThirdEntered.Task;
        service.Cancel();
        await task;
        TestSupport.Equal(1, commits, "committed before settle cancel");
        TestSupport.Equal(NavigationTransitionPhase.Idle, service.CurrentSnapshot.Phase, "idle after settle cancel");
    }

    private static async Task RapidTwentyRequestsKeepLatestAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        List<string> commits = [];
        List<Task> tasks = [];
        string[] targets = ["Cpu", "Gpu", "Memory", "Disk", "Network", "Motherboard", "AdvancedSensors", "GamePerformance", "Settings", "MetricVisibility"];
        for (int index = 0; index < 20; index++)
        {
            string target = targets[index % targets.Length];
            tasks.Add(service.NavigateAsync(Intent(target), _ => { commits.Add(target); return Task.CompletedTask; }));
            await Task.Yield();
        }
        string latest = targets[19 % targets.Length];
        clock.Release();
        await Task.WhenAll(tasks);
        TestSupport.Equal(1, commits.Count, "rapid commit count");
        TestSupport.Equal(latest, commits[0], "rapid latest target");
    }

    private static async Task VersionsAreMonotonicAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        List<long> versions = [];
        service.TransitionChanged += (_, e) => versions.Add(e.CurrentSnapshot.Version);
        Task first = service.NavigateAsync(Intent("Cpu"), _ => Task.CompletedTask);
        await clock.Entered.Task;
        Task second = service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        clock.Release();
        await Task.WhenAll(first, second);
        TestSupport.True(versions.Zip(versions.Skip(1)).All(pair => pair.First <= pair.Second), "monotonic versions");
    }

    private static async Task ClockFailureReturnsIdleAsync()
    {
        NavigationTransitionService service = new(new ThrowClock());
        try { await service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask); }
        catch (InvalidOperationException) { }
        TestSupport.Equal(NavigationTransitionPhase.Idle, service.CurrentSnapshot.Phase, "clock failure idle");
    }

    private static async Task CommitFailureReturnsIdleAsync()
    {
        NavigationTransitionService service = new(new ImmediateClock());
        try { await service.NavigateAsync(Intent("Gpu"), _ => throw new InvalidOperationException("commit")); }
        catch (InvalidOperationException) { }
        TestSupport.Equal(NavigationTransitionPhase.Idle, service.CurrentSnapshot.Phase, "commit failure idle");
    }

    private static async Task SubscriberFailureIsIsolatedAsync()
    {
        NavigationTransitionService service = new(new ImmediateClock());
        int successfulCalls = 0;
        service.TransitionChanged += (_, _) => throw new InvalidOperationException("subscriber");
        service.TransitionChanged += (_, _) => successfulCalls++;
        await service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        TestSupport.True(successfulCalls >= 6, "other subscriber calls");
    }

    private static async Task ExternalCancellationReturnsIdleAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        using CancellationTokenSource cancellation = new();
        Task task = service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask, cancellation.Token);
        await clock.Entered.Task;
        cancellation.Cancel();
        await task;
        TestSupport.Equal(NavigationTransitionPhase.Idle, service.CurrentSnapshot.Phase, "external cancel idle");
    }

    private static async Task DisposeReturnsIdleAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        Task task = service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        await clock.Entered.Task;
        service.Dispose();
        await task;
        TestSupport.Equal(NavigationTransitionPhase.Idle, service.CurrentSnapshot.Phase, "dispose idle");
        TestSupport.False(service.IsTransitioning, "dispose active task");
    }

    private static async Task ActiveTaskIsClearedAsync()
    {
        NavigationTransitionService service = new(new ImmediateClock());
        await service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        TestSupport.True(service.ActiveTask is null, "active task cleared");
    }

    private static async Task OffUsesNoClockAsync()
    {
        CountingClock clock = new();
        NavigationTransitionService service = new(clock);
        int commits = 0;
        await service.NavigateAsync(Intent("Gpu", MotionLevel.Off), _ => { commits++; return Task.CompletedTask; });
        TestSupport.Equal(0, clock.Count, "Off clock calls");
        TestSupport.Equal(1, commits, "Off commit");
    }

    private static async Task StaleSnapshotsDoNotReplaceLatestAsync()
    {
        GateClock clock = new();
        NavigationTransitionService service = new(clock);
        Task first = service.NavigateAsync(Intent("Cpu"), _ => Task.CompletedTask);
        await clock.Entered.Task;
        Task second = service.NavigateAsync(Intent("Gpu"), _ => Task.CompletedTask);
        clock.Release();
        await Task.WhenAll(first, second);
        TestSupport.Equal("Gpu", service.CurrentSnapshot.TargetPage, "latest idle target");
        TestSupport.True(service.CurrentSnapshot.Version >= 2, "latest version");
    }

    private sealed class ImmediateClock : INavigationTransitionClock
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            _ = duration;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class CountingClock : INavigationTransitionClock
    {
        public int Count { get; private set; }
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            _ = duration;
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class GateClock : INavigationTransitionClock
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            _ = duration;
            Entered.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }
        public void Release() => released.TrySetResult();
    }

    private sealed class ThirdGateClock : INavigationTransitionClock
    {
        private int calls;
        public TaskCompletionSource ThirdEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            _ = duration;
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref calls) < 3) return Task.CompletedTask;
            ThirdEntered.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class ThrowClock : INavigationTransitionClock
    {
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("clock");
    }
}
