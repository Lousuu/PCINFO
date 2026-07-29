using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class StartupDashboardReadinessTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return ("Startup Dashboard readiness 01 initial slots remain pending", InitialSlotsRemainPending);
        yield return ("Startup Dashboard readiness 02 sensors do not resolve network or system", SensorsDoNotResolveNetworkOrSystem);
        yield return ("Startup Dashboard readiness 03 hardware completes system source", HardwareCompletesSystemSource);
        yield return ("Startup Dashboard readiness 04 network completes independently", NetworkCompletesIndependently);
        yield return ("Startup Dashboard readiness 05 network failure is terminal", NetworkFailureIsTerminal);
        yield return ("Startup Dashboard readiness 06 hardware failure is terminal", HardwareFailureIsTerminal);
        yield return ("Startup Dashboard readiness 07 completed empty source is unavailable", CompletedEmptySourceIsUnavailable);
        yield return ("Startup Dashboard readiness 08 unsupported requires explicit availability", UnsupportedRequiresExplicitAvailability);
        yield return ("Startup Dashboard readiness 09 source states are monotonic", SourceStatesAreMonotonic);
        yield return ("Startup Dashboard readiness 10 one batch may move zero to six", OneBatchMayMoveZeroToSix);
        yield return ("Startup Dashboard readiness 11 pending projection blocks normal commit", PendingProjectionBlocksNormalCommit);
        yield return ("Startup Dashboard readiness 12 hard cutoff converts only pending slots", TestSupport.Run(HardCutoffConvertsOnlyPendingSlotsAsync));
        yield return ("Startup Dashboard readiness 13 real ViewModel keeps system pending after sensors", ViewModelKeepsSystemPendingAfterSensors);
        yield return ("Startup Dashboard readiness 14 hardware snapshot republishes system", ViewModelRepublishesAfterHardwareSnapshot);
        yield return ("Startup Dashboard readiness 15 hardware failure republishes failed system", ViewModelRepublishesHardwareFailure);
        yield return ("Startup Dashboard readiness 16 older projection version cannot overwrite terminal state", OlderProjectionVersionCannotOverwriteTerminalState);
        yield return ("Startup Dashboard readiness 17 completed startup ignores later Dashboard projection", TestSupport.Run(CompletedStartupIgnoresLaterDashboardProjectionAsync));
        yield return ("Startup Dashboard readiness 18 not reported remains pending until source completion", NotReportedRemainsPendingUntilSourceCompletion);
        yield return ("Startup Dashboard readiness 19 polling failure resolves only sensor slots", PollingFailureResolvesOnlySensorSlots);
        yield return ("Startup Dashboard readiness 20 sensors-only never marks network or system unavailable", SensorsOnlyNeverMarksNetworkOrSystemUnavailable);
    }

    private static void InitialSlotsRemainPending()
    {
        StartupDashboardReadinessTracker tracker = new();
        TestSupport.Equal(6, tracker.Slots.Count, "slot count");
        TestSupport.True(tracker.Slots.All(slot => slot.State == StartupProjectionState.Pending), "all slots pending");
        TestSupport.False(tracker.TryCreateSnapshot(1, out _), "unchanged pending state is not republished");
    }

    private static void SensorsDoNotResolveNetworkOrSystem()
    {
        StartupDashboardReadinessTracker tracker = new();
        tracker.TryComplete(HardwareOverviewKind.Cpu, StartupProjectionState.Value, "sensor");
        tracker.TryComplete(HardwareOverviewKind.Gpu, StartupProjectionState.Unavailable, "sensor");
        tracker.TryComplete(HardwareOverviewKind.Memory, StartupProjectionState.Value, "sensor");
        StartupInitialProjectionSnapshot snapshot = Published(tracker);
        TestSupport.Equal(3, snapshot.ResolvedVisibleSlotCount, "sensor resolved count");
        AssertState(snapshot, HardwareOverviewKind.Network, StartupProjectionState.Pending);
        AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Pending);
    }

    private static void HardwareCompletesSystemSource()
    {
        StartupDashboardReadinessTracker tracker = new();
        tracker.TryComplete(HardwareOverviewKind.System, StartupProjectionState.Value, "hardware snapshot");
        StartupInitialProjectionSnapshot snapshot = Published(tracker);
        AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Value);
        TestSupport.Equal(1, snapshot.ResolvedVisibleSlotCount, "hardware-only resolved count");
    }

    private static void NetworkCompletesIndependently()
    {
        StartupDashboardReadinessTracker tracker = new();
        tracker.TryComplete(HardwareOverviewKind.Network, StartupProjectionState.Unavailable, "adapter refresh empty");
        StartupInitialProjectionSnapshot snapshot = Published(tracker);
        AssertState(snapshot, HardwareOverviewKind.Network, StartupProjectionState.Unavailable);
        AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Pending);
    }

    private static void NetworkFailureIsTerminal()
    {
        StartupDashboardReadinessTracker tracker = new();
        tracker.TryComplete(HardwareOverviewKind.Network, StartupProjectionState.Failed, "adapter refresh failed");
        AssertState(Published(tracker), HardwareOverviewKind.Network, StartupProjectionState.Failed);
    }

    private static void HardwareFailureIsTerminal()
    {
        StartupDashboardReadinessTracker tracker = new();
        tracker.TryComplete(HardwareOverviewKind.System, StartupProjectionState.Failed, "snapshot failed");
        AssertState(Published(tracker), HardwareOverviewKind.System, StartupProjectionState.Failed);
    }

    private static void CompletedEmptySourceIsUnavailable()
    {
        StartupProjectionState state = StartupDashboardReadinessTracker.ResolveCompletedSourceState(
            [MetricAvailability.NotReported, MetricAvailability.Loading]);
        TestSupport.Equal(StartupProjectionState.Unavailable, state, "completed empty source");
    }

    private static void UnsupportedRequiresExplicitAvailability()
    {
        StartupProjectionState unsupported = StartupDashboardReadinessTracker.ResolveCompletedSourceState(
            [MetricAvailability.NotReported, MetricAvailability.Unsupported]);
        StartupProjectionState notReported = StartupDashboardReadinessTracker.ResolveCompletedSourceState(
            [MetricAvailability.NotReported]);
        TestSupport.Equal(StartupProjectionState.Unsupported, unsupported, "explicit unsupported");
        TestSupport.Equal(StartupProjectionState.Unavailable, notReported, "not reported is not unsupported");
    }

    private static void SourceStatesAreMonotonic()
    {
        StartupDashboardReadinessTracker tracker = new();
        TestSupport.True(
            tracker.TryComplete(HardwareOverviewKind.Cpu, StartupProjectionState.Value, "first"),
            "first terminal transition");
        StartupInitialProjectionSnapshot first = Published(tracker);
        TestSupport.False(
            tracker.TryComplete(HardwareOverviewKind.Cpu, StartupProjectionState.Failed, "late"),
            "late terminal transition ignored");
        TestSupport.False(tracker.TryCreateSnapshot(2, out _), "ignored transition not republished");
        AssertState(first, HardwareOverviewKind.Cpu, StartupProjectionState.Value);
    }

    private static void OneBatchMayMoveZeroToSix()
    {
        StartupDashboardReadinessTracker tracker = new();
        foreach (HardwareOverviewKind kind in StartupInitialProjectionSnapshot.Pending.Slots.Select(slot => slot.Region))
        {
            tracker.TryComplete(kind, StartupProjectionState.Value, "same batch");
        }
        StartupInitialProjectionSnapshot snapshot = Published(tracker);
        TestSupport.Equal(6, snapshot.ResolvedVisibleSlotCount, "same-batch resolved count");
        TestSupport.Equal(1L, snapshot.PollingVersion, "one publication polling version");
        TestSupport.True(tracker.IsReady, "tracker ready after all six terminal states");
    }

    private static void PendingProjectionBlocksNormalCommit()
    {
        using StartupSequenceService service = ReadyService(new ImmediateClock());
        StartupInitialProjectionSnapshot partial = ProjectionWithPending(HardwareOverviewKind.Network);
        service.ReportInitialProjection(partial);
        service.ReportPostDataLayout(partial.PollingVersion);
        TestSupport.False(service.CurrentSnapshot.CanCommit, "pending slot blocks commit");

        StartupInitialProjectionSnapshot complete = partial with
        {
            PollingVersion = partial.PollingVersion + 1,
            Slots = partial.Slots
                .Select(slot => slot.Region == HardwareOverviewKind.Network
                    ? slot with { State = StartupProjectionState.Unavailable, Detail = "completed empty" }
                    : slot)
                .ToArray()
        };
        service.ReportInitialProjection(complete);
        service.ReportPostDataLayout(complete.PollingVersion);
        TestSupport.True(service.CurrentSnapshot.CanCommit, "all terminal slots permit commit");
    }

    private static async Task HardCutoffConvertsOnlyPendingSlotsAsync()
    {
        using StartupSequenceService service = ReadyService(new ImmediateClock());
        StartupInitialProjectionSnapshot partial = ProjectionWithPending(HardwareOverviewKind.Network);
        service.ReportInitialProjection(partial);
        service.ReportPostDataLayout(partial.PollingVersion);
        await service.StartAsync();
        StartupInitialProjectionSnapshot resolved = service.CurrentSnapshot.InitialProjection;
        AssertState(resolved, HardwareOverviewKind.Network, StartupProjectionState.TimedOut);
        AssertState(resolved, HardwareOverviewKind.Cpu, StartupProjectionState.Value);
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "hard cutoff remains fail-open");
    }

    private static void ViewModelKeepsSystemPendingAfterSensors() =>
        WithDashboardScope(async scope =>
        {
            TaskCompletionSource<StartupInitialProjectionSnapshot> projection = NewProjectionSignal();
            scope.Dashboard.InitialProjectionApplied += Capture;
            await scope.Polling.PollNowAsync();
            StartupInitialProjectionSnapshot snapshot =
                await projection.Task.WaitAsync(TimeSpan.FromSeconds(10));
            TestSupport.True(snapshot.ResolvedVisibleSlotCount < 6, "sensors-only is not 6/6");
            AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Pending);
            scope.Dashboard.InitialProjectionApplied -= Capture;

            void Capture(object? sender, StartupInitialProjectionSnapshot value) =>
                projection.TrySetResult(value);
        });

    private static void ViewModelRepublishesAfterHardwareSnapshot() =>
        WithDashboardScope(async scope =>
        {
            await scope.Polling.PollNowAsync();
            await WaitUntilAsync(
                () => scope.Projections.Any(),
                "initial sensor projection");
            long pollingVersion = scope.Projections.Last().PollingVersion;
            await scope.Dashboard.RefreshHardwareInfoAsync();
            await WaitUntilAsync(
                () => scope.Projections.Any(snapshot =>
                    snapshot.PollingVersion >= pollingVersion
                    && Slot(snapshot, HardwareOverviewKind.System).IsResolved),
                "hardware projection");
            StartupInitialProjectionSnapshot snapshot = scope.Projections.Last();
            TestSupport.True(snapshot.PollingVersion >= pollingVersion, "hardware keeps a non-stale polling version");
            AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Value);
        });

    private static void ViewModelRepublishesHardwareFailure() =>
        WithDashboardScope(async scope =>
        {
            scope.Hardware.Fail = true;
            await scope.Dashboard.RefreshHardwareInfoAsync();
            await WaitUntilAsync(
                () => scope.Projections.Any(snapshot =>
                    Slot(snapshot, HardwareOverviewKind.System).State == StartupProjectionState.Failed),
                "hardware failure projection");
            AssertState(scope.Projections.Last(), HardwareOverviewKind.System, StartupProjectionState.Failed);
        });

    private static void OlderProjectionVersionCannotOverwriteTerminalState()
    {
        using StartupSequenceService service = ReadyService(new ImmediateClock());
        StartupInitialProjectionSnapshot newer = CompleteProjection(2);
        service.ReportInitialProjection(newer);
        StartupInitialProjectionSnapshot older = newer with
        {
            PollingVersion = 1,
            Slots = newer.Slots
                .Select(slot => slot.Region == HardwareOverviewKind.System
                    ? slot with { State = StartupProjectionState.Failed, Detail = "stale failure" }
                    : slot)
                .ToArray()
        };
        TestSupport.False(service.ReportInitialProjection(older), "older projection rejected");
        AssertState(service.CurrentSnapshot.InitialProjection, HardwareOverviewKind.System, StartupProjectionState.Value);
    }

    private static async Task CompletedStartupIgnoresLaterDashboardProjectionAsync()
    {
        using StartupSequenceService service = ReadyService(new ImmediateClock());
        StartupInitialProjectionSnapshot complete = CompleteProjection(1);
        service.ReportInitialProjection(complete);
        service.ReportPostDataLayout(complete.PollingVersion);
        await service.StartAsync();
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "startup completed");

        StartupInitialProjectionSnapshot later = CompleteProjection(2) with
        {
            Slots = CompleteProjection(2).Slots
                .Select(slot => slot with { State = StartupProjectionState.Failed, Detail = "ordinary refresh" })
                .ToArray()
        };
        TestSupport.False(service.ReportInitialProjection(later), "completed startup ignores later projection");
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "startup remains completed");
        AssertState(service.CurrentSnapshot.InitialProjection, HardwareOverviewKind.System, StartupProjectionState.Value);
    }

    private static void NotReportedRemainsPendingUntilSourceCompletion()
    {
        StartupDashboardReadinessTracker tracker = new();
        TestSupport.Equal(
            StartupProjectionState.Pending,
            tracker.Slots.Single(slot => slot.Region == HardwareOverviewKind.Network).State,
            "unfinished source remains pending");
        StartupProjectionState completed = StartupDashboardReadinessTracker.ResolveCompletedSourceState(
            [MetricAvailability.NotReported]);
        tracker.TryComplete(HardwareOverviewKind.Network, completed, "source completed without data");
        AssertState(Published(tracker), HardwareOverviewKind.Network, StartupProjectionState.Unavailable);
    }

    private static void PollingFailureResolvesOnlySensorSlots() =>
        WithDashboardScope(
            async scope =>
            {
                TaskCompletionSource<StartupInitialProjectionSnapshot> projection = NewProjectionSignal();
                scope.Dashboard.InitialProjectionApplied += Capture;
                await scope.Polling.PollNowAsync();
                StartupInitialProjectionSnapshot snapshot =
                    await projection.Task.WaitAsync(TimeSpan.FromSeconds(10));
                AssertState(snapshot, HardwareOverviewKind.Cpu, StartupProjectionState.Failed);
                AssertState(snapshot, HardwareOverviewKind.Gpu, StartupProjectionState.Failed);
                AssertState(snapshot, HardwareOverviewKind.Memory, StartupProjectionState.Failed);
                AssertState(snapshot, HardwareOverviewKind.Disk, StartupProjectionState.Pending);
                AssertState(snapshot, HardwareOverviewKind.Network, StartupProjectionState.Pending);
                AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Pending);
                scope.Dashboard.InitialProjectionApplied -= Capture;

                void Capture(object? sender, StartupInitialProjectionSnapshot value) =>
                    projection.TrySetResult(value);
            },
            new FailingReadinessSensorService());

    private static void SensorsOnlyNeverMarksNetworkOrSystemUnavailable()
    {
        StartupDashboardReadinessTracker tracker = new();
        tracker.TryComplete(HardwareOverviewKind.Cpu, StartupProjectionState.Value, "sensor");
        tracker.TryComplete(HardwareOverviewKind.Gpu, StartupProjectionState.Unavailable, "sensor");
        tracker.TryComplete(HardwareOverviewKind.Memory, StartupProjectionState.Value, "sensor");
        StartupInitialProjectionSnapshot snapshot = Published(tracker);
        AssertState(snapshot, HardwareOverviewKind.Network, StartupProjectionState.Pending);
        AssertState(snapshot, HardwareOverviewKind.System, StartupProjectionState.Pending);
        TestSupport.False(
            snapshot.Slots
                .Where(slot => slot.Region is HardwareOverviewKind.Network or HardwareOverviewKind.System)
                .Any(slot => slot.State == StartupProjectionState.Unavailable),
            "sensors-only batch does not manufacture unavailable");
    }

    private static StartupInitialProjectionSnapshot Published(
        StartupDashboardReadinessTracker tracker)
    {
        TestSupport.True(
            tracker.TryCreateSnapshot(1, out StartupInitialProjectionSnapshot snapshot),
            "projection published");
        return snapshot;
    }

    private static void AssertState(
        StartupInitialProjectionSnapshot snapshot,
        HardwareOverviewKind kind,
        StartupProjectionState expected) =>
        TestSupport.Equal(expected, Slot(snapshot, kind).State, $"{kind} state");

    private static StartupProjectionSlotSnapshot Slot(
        StartupInitialProjectionSnapshot snapshot,
        HardwareOverviewKind kind) =>
        snapshot.Slots.Single(slot => slot.Region == kind);

    private static StartupInitialProjectionSnapshot ProjectionWithPending(
        HardwareOverviewKind pending) =>
        new(
            1,
            StartupInitialProjectionSnapshot.Pending.Slots
                .Select(slot => slot.Region == pending
                    ? slot
                    : slot with { State = StartupProjectionState.Value, Detail = "value" })
                .ToArray(),
            DispatcherApplied: true,
            PostDataLayoutObserved: false);

    private static StartupInitialProjectionSnapshot CompleteProjection(long pollingVersion) =>
        new(
            pollingVersion,
            StartupInitialProjectionSnapshot.Pending.Slots
                .Select(slot => slot with { State = StartupProjectionState.Value, Detail = "value" })
                .ToArray(),
            DispatcherApplied: true,
            PostDataLayoutObserved: false);

    private static StartupSequenceService ReadyService(IStartupSequenceClock clock)
    {
        StartupSequenceService service = new(
            AppTheme.Tracework,
            MotionLevel.Standard,
            clock,
            clock,
            clock);
        foreach (StartupMilestoneId id in Enum.GetValues<StartupMilestoneId>()
                     .Where(id => id != StartupMilestoneId.ShellSurface))
        {
            service.ReportMilestone(id, StartupMilestoneState.Ready, "ready");
        }
        service.ReportSurfaceReady(1120, 720, "test");
        service.ReportFirstFrameGateReleased("test");
        return service;
    }

    private static void WithDashboardScope(
        Func<DashboardScope, Task> test,
        ISensorService? sensorService = null)
    {
        TestSupport.InTemporaryDirectory(
            (Func<string, Task>)(directory =>
                RunOnDispatcherAsync(async dispatcher =>
                {
                    using DashboardScope scope = new(directory, dispatcher, sensorService);
                    await test(scope);
                }))).GetAwaiter().GetResult();
    }

    private static Task RunOnDispatcherAsync(Func<Dispatcher, Task> test)
    {
        TaskCompletionSource completion = NewSignal();
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await test(dispatcher);
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "StartupDashboardReadiness.Dispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string label)
    {
        Stopwatch timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException(label);
            }
            await Task.Delay(10);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<StartupInitialProjectionSnapshot> NewProjectionSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ImmediateClock : IStartupSequenceClock
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class DashboardScope : IDisposable
    {
        public DashboardScope(
            string directory,
            Dispatcher dispatcher,
            ISensorService? sensorService)
        {
            Hardware = new MutableHardwareInfoService();
            Polling = new PollingService(sensorService ?? new ReadinessSensorService(), new AppSettings());
            History = new SensorHistoryService();
            Dashboard = new DashboardViewModel(
                new AppSettings(),
                Hardware,
                Polling,
                new SettingsService(directory),
                dispatcher,
                History);
            Dashboard.InitialProjectionApplied += OnProjection;
        }

        public MutableHardwareInfoService Hardware { get; }
        public PollingService Polling { get; }
        public SensorHistoryService History { get; }
        public DashboardViewModel Dashboard { get; }
        public ConcurrentQueue<StartupInitialProjectionSnapshot> Projections { get; } = new();

        public void Dispose()
        {
            Dashboard.InitialProjectionApplied -= OnProjection;
            Dashboard.Dispose();
            Polling.DisposeAsync().AsTask().GetAwaiter().GetResult();
            History.Dispose();
        }

        private void OnProjection(object? sender, StartupInitialProjectionSnapshot snapshot) =>
            Projections.Enqueue(snapshot);
    }

    private sealed class ReadinessSensorService : ISensorService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorReading>>(
            [
                new SensorReading
                {
                    DeviceName = "Test CPU",
                    SensorName = "CPU Total",
                    Category = SensorCategory.Cpu,
                    Type = SensorType.Load,
                    Value = 42,
                    Unit = "%",
                    IsAvailable = true,
                    Availability = SensorAvailability.Available,
                    Status = HardwareStatus.Normal,
                    Source = "Test",
                    RawIdentifier = "cpu/load/total"
                }
            ]);

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
            CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class FailingReadinessSensorService : ISensorService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<SensorReading>>(
                new InvalidOperationException("expected sensor failure"));

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
            CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class MutableHardwareInfoService : IHardwareInfoService
    {
        public bool Fail { get; set; }

        public void InvalidateCaches()
        {
        }

        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                return Task.FromException<HardwareSnapshot>(
                    new InvalidOperationException("expected hardware failure"));
            }

            return Task.FromResult(new HardwareSnapshot
            {
                Timestamp = DateTimeOffset.Now,
                ComputerName = "Test Computer",
                MotherboardName = "Test Board",
                OperatingSystem = "Test OS",
                Devices =
                [
                    new HardwareDevice
                    {
                        Id = "system",
                        Name = "Test Computer",
                        Model = "Test Model",
                        Category = SensorCategory.Unknown,
                        Properties = new Dictionary<string, string?>
                        {
                            ["HardwareSource"] = "Win32_ComputerSystem",
                            ["Model"] = "Test Model"
                        }
                    }
                ]
            });
        }

        public async Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(
            CancellationToken cancellationToken = default) =>
            (await GetHardwareSnapshotAsync(cancellationToken)).Devices;

        public async Task<HardwareSummary> GetHardwareSummaryAsync(
            CancellationToken cancellationToken = default)
        {
            HardwareSnapshot snapshot = await GetHardwareSnapshotAsync(cancellationToken);
            return new HardwareSummary(
                snapshot.ComputerName!,
                snapshot.OperatingSystem!,
                snapshot.CpuName,
                snapshot.GpuName,
                snapshot.MotherboardName,
                null);
        }
    }
}
