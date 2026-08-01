using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class StartupPollingCoordinationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return ("Startup coordination 01 queued refresh waits for first poll", TestSupport.Run(QueuedRefreshWaitsForFirstPollAsync));
        yield return ("Startup coordination 02 SensorAggregatorService and HardwareRefreshService follow first cycle", TestSupport.Run(RefreshLockFollowsFirstCycleAsync));
        yield return ("Startup coordination 03 successful first poll releases refresh", TestSupport.Run(SuccessfulFirstPollReleasesRefreshAsync));
        yield return ("Startup coordination 04 empty first poll completes cycle", TestSupport.Run(EmptyFirstPollCompletesCycleAsync));
        yield return ("Startup coordination 05 failed first poll releases fallback", TestSupport.Run(FailedFirstPollReleasesFallbackAsync));
        yield return ("Startup coordination 06 waiter cancellation does not hang", TestSupport.Run(WaiterCancellationDoesNotHangAsync));
        yield return ("Startup coordination 07 dispose completes pending cycle", TestSupport.Run(DisposeCompletesPendingCycleAsync));
        yield return ("Startup coordination 08 stop completes pending cycle", TestSupport.Run(StopCompletesPendingCycleAsync));
        yield return ("Startup coordination 09 multiple waiters share first result", TestSupport.Run(MultipleWaitersShareFirstResultAsync));
        yield return ("Startup coordination 10 PollNow remains serialized", TestSupport.Run(PollNowRemainsSerializedAsync));
        yield return ("Startup coordination 11 sensor service initializes once", TestSupport.Run(SensorServiceInitializesOnceAsync));
        yield return ("Startup coordination 12 first result is terminal", TestSupport.Run(FirstResultIsTerminalAsync));
        yield return ("Startup coordination 13 old order blocks first poll", TestSupport.Run(OldOrderBlocksFirstPollAsync));
        yield return ("Startup coordination 14 new order completes poll before refresh", TestSupport.Run(NewOrderCompletesPollBeforeRefreshAsync));
    }

    private static async Task QueuedRefreshWaitsForFirstPollAsync()
    {
        GateSensorService sensors = new(releaseRead: false);
        await using PollingService polling = CreatePolling(sensors);
        bool refreshStarted = false;
        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                _ =>
                {
                    refreshStarted = true;
                    return Task.CompletedTask;
                });

        TestSupport.False(refreshStarted, "refresh started before polling");
        await polling.StartAsync();
        await sensors.ReadEntered.Task.WaitAsync(TestTimeout);
        TestSupport.False(refreshStarted, "refresh started while first read was pending");
        sensors.ReleaseRead.TrySetResult();

        FirstPollingCycleOutcome outcome = await coordination.WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, outcome, "first-cycle outcome");
        TestSupport.True(refreshStarted, "refresh was not released");
    }

    private static async Task RefreshLockFollowsFirstCycleAsync()
    {
        RefreshGateProvider provider = new();
        await using SensorAggregatorService aggregator = new([provider]);
        await using PollingService polling = CreatePolling(aggregator);
        ImmediateHardwareInfo hardware = new();
        HardwareRefreshService refreshService = new(hardware, aggregator, polling);

        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                async _ => await refreshService.RefreshAsync(HardwareRefreshReason.Startup));

        TestSupport.False(provider.RefreshEntered.Task.IsCompleted, "provider refresh entered before polling");
        await polling.StartAsync();
        FirstPollingCycleOutcome firstCycle =
            await polling.WaitForFirstCycleAsync().WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, firstCycle, "first-cycle outcome");
        await provider.RefreshEntered.Task.WaitAsync(TestTimeout);
        TestSupport.True(polling.FirstCycleCompleted.IsCompletedSuccessfully, "refresh acquired before first cycle");
        provider.ReleaseRefresh.TrySetResult();
        await coordination.WaitAsync(TestTimeout);
        TestSupport.Equal(1, provider.RefreshCount, "startup refresh count");
        TestSupport.Equal(1, hardware.SnapshotCount, "hardware snapshot count");
    }

    private static async Task SuccessfulFirstPollReleasesRefreshAsync()
    {
        GateSensorService sensors = new(readings: [Reading()]);
        await using PollingService polling = CreatePolling(sensors);
        FirstPollingCycleOutcome? releasedFor = null;
        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                outcome =>
                {
                    releasedFor = outcome;
                    return Task.CompletedTask;
                });

        await polling.StartAsync();
        FirstPollingCycleOutcome outcome = await coordination.WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, outcome, "successful outcome");
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, releasedFor, "refresh release outcome");
    }

    private static async Task EmptyFirstPollCompletesCycleAsync()
    {
        GateSensorService sensors = new(readings: []);
        await using PollingService polling = CreatePolling(sensors);
        await polling.StartAsync();
        FirstPollingCycleOutcome outcome =
            await polling.WaitForFirstCycleAsync().WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, outcome, "empty poll outcome");
        TestSupport.Equal(0, polling.LatestReadings.Count, "empty readings");
    }

    private static async Task FailedFirstPollReleasesFallbackAsync()
    {
        FailOnceSensorService sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        bool refreshStarted = false;
        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                _ =>
                {
                    refreshStarted = true;
                    return Task.CompletedTask;
                });

        await polling.StartAsync();
        FirstPollingCycleOutcome outcome = await coordination.WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.Failed, outcome, "failed outcome");
        TestSupport.True(refreshStarted, "failure fallback did not release refresh");
    }

    private static async Task WaiterCancellationDoesNotHangAsync()
    {
        await using PollingService polling = CreatePolling(new GateSensorService());
        using CancellationTokenSource cancellation = new();
        bool refreshStarted = false;
        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                _ =>
                {
                    refreshStarted = true;
                    return Task.CompletedTask;
                },
                cancellation.Token);
        cancellation.Cancel();

        try
        {
            await coordination.WaitAsync(TestTimeout);
            throw new InvalidOperationException("cancelled coordination completed normally");
        }
        catch (OperationCanceledException)
        {
        }

        TestSupport.False(refreshStarted, "cancelled waiter released refresh");
    }

    private static async Task DisposeCompletesPendingCycleAsync()
    {
        PollingService polling = CreatePolling(new GateSensorService());
        Task<FirstPollingCycleOutcome> waiter = polling.WaitForFirstCycleAsync();
        await polling.DisposeAsync();
        FirstPollingCycleOutcome outcome = await waiter.WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.Cancelled, outcome, "dispose outcome");
    }

    private static async Task StopCompletesPendingCycleAsync()
    {
        GateSensorService sensors = new(releaseRead: false);
        await using PollingService polling = CreatePolling(sensors);
        await polling.StartAsync();
        await sensors.ReadEntered.Task.WaitAsync(TestTimeout);
        Task<FirstPollingCycleOutcome> waiter = polling.WaitForFirstCycleAsync();
        await polling.StopAsync();
        FirstPollingCycleOutcome outcome = await waiter.WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.Cancelled, outcome, "stop outcome");
    }

    private static async Task MultipleWaitersShareFirstResultAsync()
    {
        GateSensorService sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        Task<FirstPollingCycleOutcome> first = polling.WaitForFirstCycleAsync();
        Task<FirstPollingCycleOutcome> second = polling.WaitForFirstCycleAsync();
        await polling.StartAsync();
        FirstPollingCycleOutcome[] outcomes =
            await Task.WhenAll(first, second).WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, outcomes[0], "first waiter");
        TestSupport.Equal(outcomes[0], outcomes[1], "shared waiter result");
        TestSupport.True(ReferenceEquals(polling.FirstCycleCompleted, first), "shared one-shot task");
    }

    private static async Task PollNowRemainsSerializedAsync()
    {
        SerialSensorService sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        await polling.StartAsync();
        await sensors.FirstReadEntered.Task.WaitAsync(TestTimeout);
        Task pollNow = polling.PollNowAsync();
        TestSupport.Equal(1, sensors.MaximumConcurrentReads, "concurrency while first read held");
        sensors.ReleaseFirstRead.TrySetResult();
        await pollNow.WaitAsync(TestTimeout);
        TestSupport.Equal(1, sensors.MaximumConcurrentReads, "PollNow concurrent reads");
        TestSupport.True(sensors.ReadCount >= 2, "PollNow did not execute");
    }

    private static async Task SensorServiceInitializesOnceAsync()
    {
        GateSensorService sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                _ => Task.CompletedTask);
        await polling.StartAsync();
        await coordination.WaitAsync(TestTimeout);
        TestSupport.Equal(1, sensors.InitializeCount, "sensor service initialization count");
    }

    private static async Task FirstResultIsTerminalAsync()
    {
        FailOnceSensorService sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        await polling.StartAsync();
        FirstPollingCycleOutcome first =
            await polling.WaitForFirstCycleAsync().WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.Failed, first, "initial failure");
        await polling.PollNowAsync();
        FirstPollingCycleOutcome afterSuccess = await polling.FirstCycleCompleted;
        TestSupport.Equal(FirstPollingCycleOutcome.Failed, afterSuccess, "terminal first-cycle result");
    }

    private static async Task OldOrderBlocksFirstPollAsync()
    {
        SharedProviderGate sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        Task oldRefresh = sensors.RunRefreshAsync();
        await sensors.RefreshLockAcquired.Task.WaitAsync(TestTimeout);
        await polling.StartAsync();
        await sensors.InitializeRequested.Task.WaitAsync(TestTimeout);

        TestSupport.False(
            polling.FirstCycleCompleted.IsCompleted,
            "old refresh-first order did not block first polling initialization");

        sensors.ReleaseRefresh.TrySetResult();
        await oldRefresh.WaitAsync(TestTimeout);
        FirstPollingCycleOutcome outcome =
            await polling.WaitForFirstCycleAsync().WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, outcome, "old-order eventual outcome");
    }

    private static async Task NewOrderCompletesPollBeforeRefreshAsync()
    {
        SharedProviderGate sensors = new();
        await using PollingService polling = CreatePolling(sensors);
        Task<FirstPollingCycleOutcome> coordination =
            StartupPollingCoordinator.WaitForFirstCycleThenRefreshAsync(
                polling,
                _ => sensors.RunRefreshAsync());

        await polling.StartAsync();
        FirstPollingCycleOutcome outcome =
            await polling.WaitForFirstCycleAsync().WaitAsync(TestTimeout);
        TestSupport.Equal(FirstPollingCycleOutcome.ReadingsUpdated, outcome, "new-order first cycle");
        await sensors.RefreshLockAcquired.Task.WaitAsync(TestTimeout);
        TestSupport.True(polling.FirstCycleCompleted.IsCompletedSuccessfully, "refresh preceded first cycle");
        sensors.ReleaseRefresh.TrySetResult();
        await coordination.WaitAsync(TestTimeout);
    }

    private static PollingService CreatePolling(ISensorService sensors) =>
        new(sensors, new AppSettings
        {
            RefreshIntervalSeconds = 30,
            BackgroundRefreshIntervalSeconds = 120
        });

    private static SensorReading Reading() => new()
    {
        DeviceName = "CPU",
        SensorName = "Load",
        Category = SensorCategory.Cpu,
        Type = SensorType.Load,
        Value = 10,
        Unit = "%",
        Status = HardwareStatus.Normal,
        Timestamp = DateTimeOffset.Now,
        IsAvailable = true,
        Source = "test",
        Availability = SensorAvailability.Available,
        RawIdentifier = "test/cpu/load",
        LastUpdated = DateTimeOffset.Now
    };

    private sealed class GateSensorService(
        bool releaseRead = true,
        IReadOnlyList<SensorReading>? readings = null) : ISensorService
    {
        public TaskCompletionSource ReadEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int InitializeCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCount++;
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
            CancellationToken cancellationToken = default)
        {
            ReadEntered.TrySetResult();
            if (releaseRead)
            {
                ReleaseRead.TrySetResult();
            }

            await ReleaseRead.Task.WaitAsync(cancellationToken);
            return readings ?? [];
        }

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
            CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class FailOnceSensorService : ISensorService
    {
        private int reads;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                return Task.FromException<IReadOnlyList<SensorReading>>(
                    new IOException("first polling failed"));
            }

            return Task.FromResult<IReadOnlyList<SensorReading>>([Reading()]);
        }

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
            CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class SerialSensorService : ISensorService
    {
        private int activeReads;
        private int maximumConcurrentReads;
        private int readCount;

        public TaskCompletionSource FirstReadEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrentReads => Volatile.Read(ref maximumConcurrentReads);
        public int ReadCount => Volatile.Read(ref readCount);

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
            CancellationToken cancellationToken = default)
        {
            int active = Interlocked.Increment(ref activeReads);
            int observed;
            while (active > (observed = Volatile.Read(ref maximumConcurrentReads)))
            {
                Interlocked.CompareExchange(ref maximumConcurrentReads, active, observed);
            }

            int call = Interlocked.Increment(ref readCount);
            try
            {
                if (call == 1)
                {
                    FirstReadEntered.TrySetResult();
                    await ReleaseFirstRead.Task.WaitAsync(cancellationToken);
                }

                return [];
            }
            finally
            {
                Interlocked.Decrement(ref activeReads);
            }
        }

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
            CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class SharedProviderGate : ISensorService
    {
        private readonly SemaphoreSlim providerLock = new(1, 1);

        public TaskCompletionSource RefreshLockAcquired { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InitializeRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRefresh { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task RunRefreshAsync()
        {
            await providerLock.WaitAsync();
            try
            {
                RefreshLockAcquired.TrySetResult();
                await ReleaseRefresh.Task;
            }
            finally
            {
                providerLock.Release();
            }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeRequested.TrySetResult();
            await providerLock.WaitAsync(cancellationToken);
            providerLock.Release();
        }

        public Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorReading>>([]);

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
            CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
            providerLock.Dispose();
        }
    }

    private sealed class RefreshGateProvider : ISensorProvider, IRefreshableSensorProvider
    {
        public string Name => "refresh-gate";
        public bool IsAvailable => true;
        public int Priority => 1;
        public TaskCompletionSource RefreshEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRefresh { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int RefreshCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SensorReading>> GetReadingsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorReading>>([]);

        public async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            RefreshEntered.TrySetResult();
            await ReleaseRefresh.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ImmediateHardwareInfo : IHardwareInfoService
    {
        public int SnapshotCount { get; private set; }

        public void InvalidateCaches()
        {
        }

        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            SnapshotCount++;
            return Task.FromResult(new HardwareSnapshot { Timestamp = DateTimeOffset.Now });
        }

        public async Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(
            CancellationToken cancellationToken = default) =>
            (await GetHardwareSnapshotAsync(cancellationToken)).Devices;

        public Task<HardwareSummary> GetHardwareSummaryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("test", "test", null, null, null, null));
    }
}
