using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class StartupSequenceService : IStartupSequenceService
{
    private static readonly StartupMilestoneId[] CoreCommitMilestones =
    [
        StartupMilestoneId.ThemeResources,
        StartupMilestoneId.ServiceGraph,
        StartupMilestoneId.PageRouter,
        StartupMilestoneId.HistoryBuffer,
        StartupMilestoneId.ShellSurface
    ];

    private readonly object sync = new();
    private readonly IStartupSequenceClock clock;
    private readonly IStartupSequenceClock readinessClock;
    private readonly Dictionary<StartupMilestoneId, StartupMilestoneSnapshot> milestones;
    private StartupSequenceSnapshot current;
    private CancellationTokenSource? activeCancellation;
    private Task? activeTask;
    private TaskCompletionSource readinessChanged = CreateSignal();
    private long nextVersion;
    private bool hasStarted;
    private bool visualReady;
    private StartupInitialProjectionSnapshot initialProjection = StartupInitialProjectionSnapshot.Pending;
    private bool isDisposed;

    public StartupSequenceService(
        AppTheme theme,
        MotionLevel motionLevel,
        IStartupSequenceClock? clock = null,
        IStartupSequenceClock? readinessClock = null)
    {
        this.clock = clock ?? new SystemStartupSequenceClock();
        this.readinessClock = readinessClock ?? new SystemStartupSequenceClock();
        current = StartupSequenceSnapshot.Dormant(theme, motionLevel);
        milestones = current.Milestones.ToDictionary(item => item.Id);
    }

    public event EventHandler<StartupSequenceChangedEventArgs>? SnapshotChanged;

    public StartupSequenceSnapshot CurrentSnapshot
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (hasStarted)
            {
                return activeTask ?? Task.CompletedTask;
            }

            hasStarted = true;
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeTask = RunAsync(activeCancellation.Token);
            return activeTask;
        }
    }

    public bool ReportMilestone(
        StartupMilestoneId id,
        StartupMilestoneState state,
        string detail = "")
    {
        StartupSequenceChangedEventArgs? args;
        TaskCompletionSource signal;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted && state != StartupMilestoneState.Failed)
            {
                return false;
            }

            StartupMilestoneSnapshot previous = milestones[id];
            if (previous.State == state
                && previous.State is StartupMilestoneState.Ready
                    or StartupMilestoneState.Partial
                    or StartupMilestoneState.Failed)
            {
                return false;
            }
            if (!IsAllowedTransition(previous.State, state))
            {
                return false;
            }

            string normalizedDetail = detail?.Trim() ?? string.Empty;
            StartupMilestoneSnapshot next = new(
                id,
                previous.Name,
                state,
                StartupMilestoneSnapshot.GetStatusText(state),
                normalizedDetail);
            if (previous == next)
            {
                return false;
            }

            milestones[id] = next;
            signal = readinessChanged;
            readinessChanged = CreateSignal();
            args = CreateSnapshotLocked(
                current.Phase,
                current.IsActive,
                current.HasCompleted,
                current.Announcement,
                ResolveFailureMessageLocked());
        }

        signal.TrySetResult();
        Raise(args);
        return true;
    }

    public bool ReportSurfaceReady(double width, double height, string detail = "")
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
        {
            return false;
        }

        StartupSequenceChangedEventArgs? args;
        TaskCompletionSource signal;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted || visualReady)
            {
                return false;
            }

            StartupMilestoneSnapshot previous = milestones[StartupMilestoneId.ShellSurface];
            if (previous.State is not (StartupMilestoneState.Wait or StartupMilestoneState.Pending))
            {
                return false;
            }

            string normalizedDetail = string.IsNullOrWhiteSpace(detail)
                ? $"Measured {width:0} × {height:0}"
                : detail.Trim();
            milestones[StartupMilestoneId.ShellSurface] = previous with
            {
                State = StartupMilestoneState.Ready,
                StatusText = StartupMilestoneSnapshot.GetStatusText(StartupMilestoneState.Ready),
                Detail = normalizedDetail
            };
            visualReady = true;
            signal = readinessChanged;
            readinessChanged = CreateSignal();
            args = CreateSnapshotLocked(current.Phase, current.IsActive, current.HasCompleted,
                current.Announcement, ResolveFailureMessageLocked());
        }

        signal.TrySetResult();
        Raise(args);
        return true;
    }

    public bool ReportInitialProjection(StartupInitialProjectionSnapshot projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        StartupSequenceChangedEventArgs? args;
        TaskCompletionSource signal;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted || projection.PollingVersion < initialProjection.PollingVersion)
            {
                return false;
            }

            initialProjection = projection with
            {
                PostDataLayoutObserved = initialProjection.PostDataLayoutObserved
                    && projection.PollingVersion == initialProjection.PollingVersion
            };
            signal = readinessChanged;
            readinessChanged = CreateSignal();
            args = CreateSnapshotLocked(current.Phase, current.IsActive, current.HasCompleted,
                current.Announcement, ResolveFailureMessageLocked());
        }

        signal.TrySetResult();
        Raise(args);
        return true;
    }

    public bool ReportPostDataLayout(long pollingVersion)
    {
        StartupSequenceChangedEventArgs? args;
        TaskCompletionSource signal;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted || !initialProjection.DispatcherApplied
                || pollingVersion < initialProjection.PollingVersion || initialProjection.PostDataLayoutObserved)
            {
                return false;
            }

            initialProjection = initialProjection with { PostDataLayoutObserved = true };
            signal = readinessChanged;
            readinessChanged = CreateSignal();
            args = CreateSnapshotLocked(current.Phase, current.IsActive, current.HasCompleted,
                current.Announcement, ResolveFailureMessageLocked());
        }

        signal.TrySetResult();
        Raise(args);
        return true;
    }

    public void CompleteForHiddenWindow()
    {
        CancellationTokenSource? cancellation;
        StartupSequenceChangedEventArgs? args = null;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted)
            {
                return;
            }

            cancellation = activeCancellation;
            args = CreateSnapshotLocked(
                StartupSequencePhase.Complete,
                isActive: false,
                hasCompleted: true,
                "系统界面已就绪",
                current.FailureMessage);
        }

        cancellation?.Cancel();
        Raise(args);
    }

    public void CompleteForVisualReadinessFailure(string detail)
    {
        string failure = string.IsNullOrWhiteSpace(detail)
            ? "INITIAL TRACE visual surface readiness timeout."
            : detail.Trim();
        AppLogger.LogError(
            failure,
            new TimeoutException(failure),
            "startup-sequence:visual-surface-readiness-timeout",
            TimeSpan.Zero);
        PublishComplete(failure);
    }

    public void Cancel() => CompleteForHiddenWindow();

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (sync)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            cancellation = activeCancellation;
            activeCancellation = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        Stopwatch elapsed = Stopwatch.StartNew();
        StartupSequencePlan plan = StartupSequencePlan.Create(
            CurrentSnapshot.CurrentTheme,
            CurrentSnapshot.MotionLevel);
        try
        {
            if (!await WaitForVisualReadyAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
            elapsed.Restart();
            PublishPhase(StartupSequencePhase.Index, "迹构正在启动");
            await DelayAsync(plan.IndexDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);

            PublishPhase(StartupSequencePhase.Route, string.Empty);
            await DelayAsync(plan.RouteDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);

            PublishPhase(StartupSequencePhase.Bind, string.Empty);
            await DelayAsync(plan.BindDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);

            await WaitForCommitAsync(plan, elapsed, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CurrentSnapshot.CanCommit)
            {
                ResolveReadinessAtCutoff();
                if (!CurrentSnapshot.CanCommit)
                {
                    PublishComplete("Startup core readiness did not complete before the hard cutoff.");
                    return;
                }
            }

            PublishPhase(StartupSequencePhase.Lock, string.Empty);
            await DelayAsync(plan.LockDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);

            PublishPhase(StartupSequencePhase.Reveal, string.Empty);
            await DelayAsync(plan.RevealDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);
            PublishComplete(CurrentSnapshot.FailureMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!CurrentSnapshot.HasCompleted)
            {
                PublishComplete(CurrentSnapshot.FailureMessage);
            }
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "INITIAL TRACE startup sequence failed.",
                exception,
                $"startup-sequence:{exception.GetType().FullName}",
                TimeSpan.Zero);
            PublishComplete(exception.Message);
            throw;
        }
        finally
        {
            lock (sync)
            {
                activeCancellation?.Dispose();
                activeCancellation = null;
                activeTask = null;
            }
        }
    }

    private async Task<bool> WaitForVisualReadyAsync(CancellationToken cancellationToken)
    {
        if (CurrentSnapshot.VisualReady)
        {
            return true;
        }

        TimeSpan timeoutDuration = TimeSpan.FromMilliseconds(2500);
        using CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timeout = readinessClock.DelayAsync(timeoutDuration, timeoutCancellation.Token);
        while (!CurrentSnapshot.VisualReady && !timeout.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task signal;
            lock (sync)
            {
                signal = readinessChanged.Task;
            }
            await Task.WhenAny(signal, timeout).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (CurrentSnapshot.VisualReady)
        {
            timeoutCancellation.Cancel();
            return true;
        }

        CompleteForVisualReadinessFailure(
            $"INITIAL TRACE visual surface readiness timeout after {timeoutDuration.TotalMilliseconds:0} ms.");
        return false;
    }

    private async Task WaitForCommitAsync(
        StartupSequencePlan plan,
        Stopwatch elapsed,
        CancellationToken cancellationToken)
    {
        if (CurrentSnapshot.CanCommit)
        {
            return;
        }

        if (!plan.UsesClock)
        {
            Task readinessCutoff = Task.Delay(plan.HardCutoff, cancellationToken);
            while (!CurrentSnapshot.CanCommit && !readinessCutoff.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task signal;
                lock (sync)
                {
                    signal = readinessChanged.Task;
                }
                await Task.WhenAny(signal, readinessCutoff).ConfigureAwait(false);
            }
            return;
        }

        TimeSpan remaining = plan.HardCutoff - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        Task cutoff = clock.DelayAsync(remaining, cancellationToken);
        while (!CurrentSnapshot.CanCommit && !cutoff.IsCompleted)
        {
            Task signal;
            lock (sync)
            {
                signal = readinessChanged.Task;
            }
            await Task.WhenAny(signal, cutoff).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private Task DelayAsync(TimeSpan delay, bool usesClock, CancellationToken cancellationToken) =>
        usesClock ? clock.DelayAsync(delay, cancellationToken) : Task.CompletedTask;

    private void ResolveReadinessAtCutoff()
    {
        StartupSequenceChangedEventArgs? args;
        lock (sync)
        {
            if (!initialProjection.IsReady)
            {
                StartupProjectionSlotSnapshot[] slots = initialProjection.Slots
                    .Select(slot => slot.IsResolved ? slot : slot with
                    {
                        State = StartupProjectionState.TimedOut,
                        Detail = "Initial projection timed out"
                    })
                    .ToArray();
                initialProjection = initialProjection with
                {
                    Slots = slots,
                    DispatcherApplied = true,
                    PostDataLayoutObserved = true
                };
            }

            if (milestones[StartupMilestoneId.SensorBus].State is StartupMilestoneState.Wait or StartupMilestoneState.Pending)
            {
                StartupMilestoneSnapshot previous = milestones[StartupMilestoneId.SensorBus];
                milestones[StartupMilestoneId.SensorBus] = previous with
                {
                    State = StartupMilestoneState.Partial,
                    StatusText = StartupMilestoneSnapshot.GetStatusText(StartupMilestoneState.Partial),
                    Detail = "Initial sensor sample timed out"
                };
            }

            args = CreateSnapshotLocked(current.Phase, current.IsActive, current.HasCompleted,
                current.Announcement, ResolveFailureMessageLocked());
        }
        Raise(args);
    }

    private void PublishPhase(StartupSequencePhase phase, string announcement)
    {
        StartupSequenceChangedEventArgs? args;
        lock (sync)
        {
            if (current.HasCompleted || phase <= current.Phase)
            {
                return;
            }

            args = CreateSnapshotLocked(
                phase,
                isActive: true,
                hasCompleted: false,
                string.IsNullOrEmpty(announcement) ? current.Announcement : announcement,
                ResolveFailureMessageLocked());
        }
        Raise(args);
    }

    private void PublishComplete(string? failureMessage)
    {
        StartupSequenceChangedEventArgs? args;
        lock (sync)
        {
            if (current.HasCompleted)
            {
                return;
            }

            args = CreateSnapshotLocked(
                StartupSequencePhase.Complete,
                isActive: false,
                hasCompleted: true,
                "系统界面已就绪",
                failureMessage);
        }
        Raise(args);
    }

    private StartupSequenceChangedEventArgs CreateSnapshotLocked(
        StartupSequencePhase phase,
        bool isActive,
        bool hasCompleted,
        string announcement,
        string? failureMessage)
    {
        StartupSequenceSnapshot previous = current;
        StartupMilestoneSnapshot[] ordered = Enum.GetValues<StartupMilestoneId>()
            .Select(id => milestones[id])
            .ToArray();
        bool shellReady = milestones[StartupMilestoneId.ShellSurface].State == StartupMilestoneState.Ready;
        bool coreReady = CoreCommitMilestones.All(id => milestones[id].State == StartupMilestoneState.Ready);
        bool sensorTerminal = milestones[StartupMilestoneId.SensorBus].State is StartupMilestoneState.Ready
            or StartupMilestoneState.Partial or StartupMilestoneState.Failed;
        bool canCommit = coreReady && sensorTerminal && visualReady && initialProjection.IsReady;
        current = new StartupSequenceSnapshot(
            ++nextVersion,
            phase,
            isActive,
            hasCompleted,
            previous.StartedAt ?? DateTimeOffset.UtcNow,
            previous.CurrentTheme,
            previous.MotionLevel,
            previous.LaunchKind,
            ordered,
            shellReady,
            visualReady,
            initialProjection,
            canCommit,
            failureMessage,
            announcement);
        return new StartupSequenceChangedEventArgs(previous, current);
    }

    private string? ResolveFailureMessageLocked()
    {
        StartupMilestoneSnapshot? failed = milestones.Values.FirstOrDefault(item =>
            item.State == StartupMilestoneState.Failed);
        return failed is null
            ? current.FailureMessage
            : string.IsNullOrWhiteSpace(failed.Detail)
                ? $"{failed.Name} failed."
                : $"{failed.Name}: {failed.Detail}";
    }

    private void Raise(StartupSequenceChangedEventArgs? args)
    {
        if (args is null)
        {
            return;
        }

        foreach (EventHandler<StartupSequenceChangedEventArgs> handler in
                 SnapshotChanged?.GetInvocationList().Cast<EventHandler<StartupSequenceChangedEventArgs>>() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                AppLogger.LogError(
                    "INITIAL TRACE subscriber failed.",
                    exception,
                    $"startup-sequence-subscriber:{handler.Method.DeclaringType?.FullName}:{handler.Method.Name}",
                    TimeSpan.FromMinutes(5));
            }
        }
    }

    private static bool IsAllowedTransition(
        StartupMilestoneState previous,
        StartupMilestoneState next)
    {
        if (previous == next)
        {
            return true;
        }

        return previous switch
        {
            StartupMilestoneState.Wait => next is StartupMilestoneState.Pending
                or StartupMilestoneState.Ready
                or StartupMilestoneState.Partial
                or StartupMilestoneState.Failed,
            StartupMilestoneState.Pending => next is StartupMilestoneState.Ready
                or StartupMilestoneState.Partial
                or StartupMilestoneState.Failed,
            _ => false
        };
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct StartupSequencePlan(
        bool UsesClock,
        TimeSpan IndexDuration,
        TimeSpan RouteDuration,
        TimeSpan BindDuration,
        TimeSpan LockDuration,
        TimeSpan RevealDuration,
        TimeSpan HardCutoff)
    {
        public static StartupSequencePlan Create(AppTheme theme, MotionLevel motionLevel)
        {
            if (motionLevel == MotionLevel.Off)
            {
                return new(false, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(1500));
            }

            if (theme == AppTheme.Classic || motionLevel == MotionLevel.Reduced)
            {
                return new(
                    true,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(30),
                    TimeSpan.FromMilliseconds(150),
                    TimeSpan.FromMilliseconds(1320));
            }

            if (motionLevel == MotionLevel.Standard)
            {
                return new(
                    true,
                    TimeSpan.FromMilliseconds(190),
                    ResolveTraceworkRouteDuration(motionLevel),
                    TimeSpan.FromMilliseconds(220),
                    TimeSpan.FromMilliseconds(180),
                    TimeSpan.FromMilliseconds(270),
                    ResolveTraceworkHardCutoff(motionLevel));
            }

            return new(
                true,
                TimeSpan.FromMilliseconds(240),
                ResolveTraceworkRouteDuration(motionLevel),
                TimeSpan.FromMilliseconds(360),
                TimeSpan.FromMilliseconds(180),
                TimeSpan.FromMilliseconds(360),
                ResolveTraceworkHardCutoff(motionLevel));
        }
    }

    internal static TimeSpan ResolveTraceworkRouteDuration(MotionLevel motionLevel) =>
        TimeSpan.FromMilliseconds(
            motionLevel == MotionLevel.Full ? 1050d : 680d);

    internal static TimeSpan ResolveTraceworkHardCutoff(MotionLevel motionLevel) =>
        TimeSpan.FromMilliseconds(
            motionLevel == MotionLevel.Full ? 4210d : 3460d);
}
