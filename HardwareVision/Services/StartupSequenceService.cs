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
    private readonly IStartupSequenceClock visualClock;
    private readonly Dictionary<StartupMilestoneId, StartupMilestoneSnapshot> milestones;
    private StartupSequenceSnapshot current;
    private CancellationTokenSource? activeCancellation;
    private Task? activeTask;
    private TaskCompletionSource readinessChanged = CreateSignal();
    private TaskCompletionSource<bool>? revealVisualCompletion;
    private long revealVisualVersion = -1;
    private long nextVersion;
    private bool hasStarted;
    private bool surfaceMeasured;
    private bool firstFrameGateReleased;
    private string firstFrameGateReleaseReason = string.Empty;
    private bool commitAuthorized;
    private StartupInitialProjectionSnapshot initialProjection = StartupInitialProjectionSnapshot.Pending;
    private bool isDisposed;

    internal bool WasRevealVisualCompletionReported { get; private set; }

    internal DateTimeOffset? RevealVisualCompletedAt { get; private set; }

    internal DateTimeOffset? LogicalSequenceCompletedAt { get; private set; }

    public StartupSequenceService(
        AppTheme theme,
        MotionLevel motionLevel,
        IStartupSequenceClock? clock = null,
        IStartupSequenceClock? readinessClock = null,
        IStartupSequenceClock? visualClock = null)
    {
        this.clock = clock ?? new SystemStartupSequenceClock();
        this.readinessClock = readinessClock ?? new SystemStartupSequenceClock();
        this.visualClock = visualClock
            ?? readinessClock
            ?? clock
            ?? new SystemStartupSequenceClock();
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

        string normalizedDetail = string.IsNullOrWhiteSpace(detail)
            ? $"Measured {width:0} × {height:0}"
            : detail.Trim();
        StartupSequenceChangedEventArgs? args;
        TaskCompletionSource signal;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted || surfaceMeasured)
            {
                return false;
            }

            StartupMilestoneSnapshot previous = milestones[StartupMilestoneId.ShellSurface];
            if (previous.State is not (StartupMilestoneState.Wait or StartupMilestoneState.Pending))
            {
                return false;
            }

            milestones[StartupMilestoneId.ShellSurface] = previous with
            {
                State = StartupMilestoneState.Ready,
                StatusText = StartupMilestoneSnapshot.GetStatusText(StartupMilestoneState.Ready),
                Detail = normalizedDetail
            };
            surfaceMeasured = true;
            signal = readinessChanged;
            readinessChanged = CreateSignal();
            args = CreateSnapshotLocked(current.Phase, current.IsActive, current.HasCompleted,
                current.Announcement, ResolveFailureMessageLocked());
        }

        signal.TrySetResult();
        Raise(args);
        LogStartupDiagnostic("SurfaceMeasured", normalizedDetail);
        return true;
    }

    public bool ReportFirstFrameGateReleased(string reason)
    {
        StartupSequenceChangedEventArgs? args;
        TaskCompletionSource signal;
        lock (sync)
        {
            if (isDisposed || current.HasCompleted || firstFrameGateReleased)
            {
                return false;
            }

            firstFrameGateReleased = true;
            firstFrameGateReleaseReason = string.IsNullOrWhiteSpace(reason)
                ? "CompositorReady"
                : reason.Trim();
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
        LogStartupDiagnostic("FirstFrameGateReleased", firstFrameGateReleaseReason);
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

    public bool ReportRevealVisualCompleted(long startupVersion)
    {
        TaskCompletionSource<bool>? completion;
        lock (sync)
        {
            if (isDisposed
                || current.HasCompleted
                || current.Phase != StartupSequencePhase.Reveal
                || startupVersion != revealVisualVersion)
            {
                return false;
            }

            completion = revealVisualCompletion;
            if (completion is null)
            {
                return false;
            }
        }

        bool reported = completion.TrySetResult(true);
        if (reported)
        {
            WasRevealVisualCompletionReported = true;
            RevealVisualCompletedAt = DateTimeOffset.UtcNow;
            LogStartupDiagnostic(
                "RevealVisualCompleted",
                $"VisualFrameCommitted; revealVersion={startupVersion}");
        }
        return reported;
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
            revealVisualCompletion?.TrySetCanceled();
            revealVisualCompletion = null;
            revealVisualVersion = -1;
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
            revealVisualCompletion?.TrySetCanceled();
            revealVisualCompletion = null;
            revealVisualVersion = -1;
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
            LogStartupDiagnostic("StartupIndexRequested", "MeasuredAndGateReleased");
            PublishPhase(StartupSequencePhase.Index, "迹构正在启动");
            LogStartupDiagnostic("StartupIndexStarted", "Index");
            await DelayAsync(plan.IndexDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);
            LogStartupDiagnostic("StartupIndexCompleted", "Index");

            PublishPhase(StartupSequencePhase.Route, string.Empty);
            await DelayAsync(plan.RouteDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);

            PublishPhase(StartupSequencePhase.Bind, string.Empty);
            await DelayAsync(plan.BindDuration, plan.UsesClock, cancellationToken).ConfigureAwait(false);

            await WaitForCommitAsync(plan, elapsed, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CurrentSnapshot.CanCommit)
            {
                await WaitForReadinessSettleAsync(
                    plan.ReadinessSettleDuration,
                    plan.UsesClock,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
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

            long revealVersion = PublishRevealAndArmVisualGate();
            LogStartupDiagnostic(
                "RevealLogicalStarted",
                $"Reveal; revealVersion={revealVersion}");
            await WaitForRevealVisualCompletionAsync(
                revealVersion,
                plan.RevealVisualTimeout,
                cancellationToken).ConfigureAwait(false);
            LogStartupDiagnostic("StartupVisible", "RevealVisualCompleted");
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

        LogStartupDiagnostic(
            "StartupIndexDeferred",
            CurrentSnapshot.SurfaceMeasured
                ? "FirstFrameGatePending"
                : "SurfaceMeasurementPending");

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

    private async Task WaitForReadinessSettleAsync(
        TimeSpan duration,
        bool usesClock,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero || CurrentSnapshot.CanCommit)
        {
            return;
        }

        Task settle = usesClock
            ? clock.DelayAsync(duration, cancellationToken)
            : Task.Delay(duration, cancellationToken);
        while (!CurrentSnapshot.CanCommit && !settle.IsCompleted)
        {
            Task signal;
            lock (sync)
            {
                signal = readinessChanged.Task;
            }

            await Task.WhenAny(signal, settle).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

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

    private long PublishRevealAndArmVisualGate()
    {
        StartupSequenceChangedEventArgs? args;
        long version;
        lock (sync)
        {
            if (current.HasCompleted || StartupSequencePhase.Reveal <= current.Phase)
            {
                return current.Version;
            }

            args = CreateSnapshotLocked(
                StartupSequencePhase.Reveal,
                isActive: true,
                hasCompleted: false,
                current.Announcement,
                ResolveFailureMessageLocked());
            version = current.Version;
            revealVisualVersion = version;
            WasRevealVisualCompletionReported = false;
            RevealVisualCompletedAt = null;
            revealVisualCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Raise(args);
        return version;
    }

    private async Task WaitForRevealVisualCompletionAsync(
        long version,
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken)
    {
        if (timeoutDuration <= TimeSpan.Zero)
        {
            ReportRevealVisualCompleted(version);
            return;
        }

        TaskCompletionSource<bool>? completion;
        lock (sync)
        {
            completion = revealVisualVersion == version
                ? revealVisualCompletion
                : null;
        }
        if (completion is null)
        {
            return;
        }

        using CancellationTokenSource timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timeout = visualClock.DelayAsync(
            timeoutDuration,
            timeoutCancellation.Token);
        Task winner = await Task.WhenAny(completion.Task, timeout).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (winner == completion.Task && completion.Task.IsCompletedSuccessfully)
        {
            timeoutCancellation.Cancel();
        }
        else
        {
            LogStartupDiagnostic(
                "RevealVisualCompletionTimeout",
                $"timeout={timeoutDuration.TotalMilliseconds:0}ms; revealVersion={version}");
        }

        lock (sync)
        {
            if (revealVisualVersion == version)
            {
                revealVisualCompletion = null;
                revealVisualVersion = -1;
            }
        }
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

            revealVisualCompletion?.TrySetResult(false);
            revealVisualCompletion = null;
            revealVisualVersion = -1;
            LogicalSequenceCompletedAt = DateTimeOffset.UtcNow;
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
        bool readinessNow =
            coreReady
            && sensorTerminal
            && surfaceMeasured
            && firstFrameGateReleased
            && initialProjection.IsReady;
        if (phase >= StartupSequencePhase.Lock
            && (previous.CanCommit || readinessNow))
        {
            commitAuthorized = true;
        }
        bool canCommit = commitAuthorized || readinessNow;
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
            surfaceMeasured,
            firstFrameGateReleased,
            firstFrameGateReleaseReason,
            surfaceMeasured && firstFrameGateReleased,
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

    private void LogStartupDiagnostic(string eventName, string reason)
    {
        StartupSequenceSnapshot snapshot = CurrentSnapshot;
        double relative = snapshot.StartedAt.HasValue
            ? (DateTimeOffset.UtcNow - snapshot.StartedAt.Value).TotalMilliseconds
            : 0d;
        AppLogger.LogKeyEvent(
            $"{eventName} | relative={relative:0} ms; version={snapshot.Version}"
            + $"; gate={snapshot.FirstFrameGateReleased}; measured={snapshot.SurfaceMeasured}"
            + $"; startup={snapshot.Phase}; motion={snapshot.MotionLevel}; reason={reason}");
    }

    private readonly record struct StartupSequencePlan(
        bool UsesClock,
        TimeSpan IndexDuration,
        TimeSpan RouteDuration,
        TimeSpan BindDuration,
        TimeSpan LockDuration,
        TimeSpan RevealDuration,
        TimeSpan RevealVisualTimeout,
        TimeSpan HardCutoff,
        TimeSpan ReadinessSettleDuration)
    {
        public static StartupSequencePlan Create(AppTheme theme, MotionLevel motionLevel)
        {
            if (motionLevel == MotionLevel.Off)
            {
                return new(false, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromMilliseconds(1500),
                    TimeSpan.Zero);
            }

            if (theme == AppTheme.Classic || motionLevel == MotionLevel.Reduced)
            {
                return new(
                    true,
                    TimeSpan.FromMilliseconds(20),
                    TimeSpan.FromMilliseconds(10),
                    TimeSpan.FromMilliseconds(10),
                    ResolveStartupLockDuration(motionLevel),
                    TimeSpan.FromMilliseconds(180),
                    TimeSpan.FromMilliseconds(450),
                    TimeSpan.FromMilliseconds(1320),
                    TimeSpan.FromMilliseconds(80));
            }

            if (motionLevel == MotionLevel.Standard)
            {
                return new(
                    true,
                    TimeSpan.FromMilliseconds(300),
                    ResolveTraceworkRouteDuration(motionLevel),
                    TimeSpan.FromMilliseconds(220),
                    ResolveStartupLockDuration(motionLevel),
                    TimeSpan.FromMilliseconds(330),
                    TimeSpan.FromMilliseconds(750),
                    ResolveTraceworkHardCutoff(motionLevel),
                    ResolveReadinessSettleDuration(motionLevel));
            }

            return new(
                true,
                TimeSpan.FromMilliseconds(360),
                ResolveTraceworkRouteDuration(motionLevel),
                TimeSpan.FromMilliseconds(360),
                ResolveStartupLockDuration(motionLevel),
                TimeSpan.FromMilliseconds(390),
                TimeSpan.FromMilliseconds(900),
                ResolveTraceworkHardCutoff(motionLevel),
                ResolveReadinessSettleDuration(motionLevel));
        }
    }

    internal static TimeSpan ResolveTraceworkRouteDuration(MotionLevel motionLevel) =>
        TimeSpan.FromMilliseconds(
            motionLevel == MotionLevel.Full ? 1220d : 720d);

    internal static TimeSpan ResolveStartupLockDuration(MotionLevel motionLevel) =>
        TimeSpan.FromMilliseconds(
            motionLevel switch
            {
                MotionLevel.Full => 1250d,
                MotionLevel.Standard => 950d,
                MotionLevel.Reduced => 360d,
                _ => 0d
            });

    internal static TimeSpan ResolveTraceworkHardCutoff(MotionLevel motionLevel) =>
        TimeSpan.FromMilliseconds(
            motionLevel == MotionLevel.Full ? 4500d : 3620d);

    internal static TimeSpan ResolveReadinessSettleDuration(MotionLevel motionLevel) =>
        TimeSpan.FromMilliseconds(
            motionLevel switch
            {
                MotionLevel.Full => 180d,
                MotionLevel.Standard => 150d,
                MotionLevel.Reduced => 80d,
                _ => 0d
            });
}
