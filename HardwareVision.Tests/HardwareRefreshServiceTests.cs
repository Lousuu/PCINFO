using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class HardwareRefreshServiceTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return ("Hardware refresh 01 device messages debounce", TestSupport.Run(DeviceMessagesDebounceAsync));
        yield return ("Hardware refresh 02 auto refresh disabled", AutoRefreshDisabled);
        yield return ("Hardware refresh 03 monitor dispose stops messages", MonitorDisposeStopsMessages);
        yield return ("Hardware refresh 04 concurrent manual refresh is single-flight", TestSupport.Run(ManualRefreshIsSingleFlightAsync));
        yield return ("Hardware refresh 05 failed provider preserves snapshot", TestSupport.Run(FailedProviderDoesNotBlockSnapshotAsync));
        yield return ("Hardware refresh 06 refresh triggers immediate poll", TestSupport.Run(RefreshTriggersImmediatePollAsync));
        yield return ("Hardware refresh 07 canceled debounce owns token lifetime", TestSupport.Run(CanceledDebounceOwnsTokenLifetimeAsync));
    }

    private static async Task DeviceMessagesDebounceAsync()
    {
        CountingRefreshService service = new();
        using HardwareChangeMonitor monitor = new(service, () => true, TimeSpan.FromMilliseconds(15), TimeSpan.FromMilliseconds(20));
        for (int index = 0; index < 20; index++)
        {
            monitor.NotifyDeviceChange(HardwareChangeMonitor.DbtDeviceArrival);
        }
        await Task.Delay(100);
        TestSupport.Equal(1, service.Count, "debounced refresh count");
    }

    private static void AutoRefreshDisabled()
    {
        CountingRefreshService service = new();
        using HardwareChangeMonitor monitor = new(service, () => false, TimeSpan.Zero, TimeSpan.Zero);
        TestSupport.False(monitor.NotifyDeviceChange(HardwareChangeMonitor.DbtDeviceArrival), "disabled notification");
        TestSupport.Equal(0, service.Count, "no refresh");
    }

    private static void MonitorDisposeStopsMessages()
    {
        CountingRefreshService service = new();
        HardwareChangeMonitor monitor = new(service, () => true, TimeSpan.Zero, TimeSpan.Zero);
        monitor.Dispose();
        TestSupport.False(monitor.NotifyDeviceChange(HardwareChangeMonitor.DbtDeviceRemoveComplete), "disposed notification");
    }

    private static async Task ManualRefreshIsSingleFlightAsync()
    {
        BlockingHardwareInfo hardware = new();
        await using TestRefreshEnvironment environment = new(hardware, new RefreshProvider());
        Task<HardwareRefreshResult> first = environment.Service.RefreshAsync(HardwareRefreshReason.ManualSettings);
        Task<HardwareRefreshResult> second = environment.Service.RefreshAsync(HardwareRefreshReason.ManualSettings);
        hardware.Release();
        await Task.WhenAll(first, second);
        TestSupport.Equal(1, hardware.SnapshotCount, "single snapshot build");
    }

    private static async Task FailedProviderDoesNotBlockSnapshotAsync()
    {
        BlockingHardwareInfo hardware = new(released: true);
        await using TestRefreshEnvironment environment = new(hardware, new RefreshProvider(failRefresh: true));
        HardwareRefreshResult result = await environment.Service.RefreshAsync(HardwareRefreshReason.Diagnostic);
        TestSupport.Equal(HardwareRefreshState.PartiallyFailed, result.State, "partial state");
        TestSupport.NotNull(result.Snapshot, "good snapshot retained");
        TestSupport.Equal(1, result.FailedProviders.Count, "failed provider reported");
    }

    private static async Task RefreshTriggersImmediatePollAsync()
    {
        BlockingHardwareInfo hardware = new(released: true);
        RefreshProvider provider = new();
        await using TestRefreshEnvironment environment = new(hardware, provider);
        await environment.Service.RefreshAsync(HardwareRefreshReason.Diagnostic);
        TestSupport.True(provider.ReadCount >= 1, "immediate poll read");
    }

    private static async Task CanceledDebounceOwnsTokenLifetimeAsync()
    {
        TokenLifetimeRefreshService service = new();
        using HardwareChangeMonitor monitor = new(service, () => true, TimeSpan.Zero, TimeSpan.Zero);
        TestSupport.True(monitor.NotifyDeviceChange(HardwareChangeMonitor.DbtDeviceArrival), "first notification");
        await service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        TestSupport.True(monitor.NotifyDeviceChange(HardwareChangeMonitor.DbtDeviceNodesChanged), "replacement notification");
        service.ReleaseFirst.TrySetResult();
        await service.FirstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        TestSupport.True(service.FirstTokenWasCanceled, "superseded token canceled");
        TestSupport.False(service.FirstTokenWasDisposed, "superseded token disposed before owner completed");
    }

    private sealed class TestRefreshEnvironment : IAsyncDisposable
    {
        public TestRefreshEnvironment(IHardwareInfoService hardware, RefreshProvider provider)
        {
            Aggregator = new SensorAggregatorService([provider]);
            Polling = new PollingService(Aggregator, new AppSettings());
            Service = new HardwareRefreshService(hardware, Aggregator, Polling);
        }
        public SensorAggregatorService Aggregator { get; }
        public PollingService Polling { get; }
        public HardwareRefreshService Service { get; }
        public async ValueTask DisposeAsync()
        {
            await Polling.DisposeAsync();
            await Aggregator.DisposeAsync();
        }
    }

    private sealed class RefreshProvider(bool failRefresh = false) : ISensorProvider, IRefreshableSensorProvider
    {
        public string Name => "test-provider";
        public bool IsAvailable => true;
        public int Priority => 1;
        public int ReadCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<SensorReading>>([]);
        }
        public Task RefreshDevicesAsync(CancellationToken cancellationToken = default) => failRefresh
            ? Task.FromException(new IOException("provider failed"))
            : Task.CompletedTask;
    }

    private sealed class BlockingHardwareInfo(bool released = false) : IHardwareInfoService
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SnapshotCount { get; private set; }
        public void Release() => release.TrySetResult();
        public void InvalidateCaches() { }
        public async Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotCount++;
            if (!released) await release.Task.WaitAsync(cancellationToken);
            return new HardwareSnapshot { Timestamp = DateTimeOffset.Now };
        }
        public async Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default) =>
            (await GetHardwareSnapshotAsync(cancellationToken)).Devices;
        public async Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default)
        {
            await GetHardwareSnapshotAsync(cancellationToken);
            return new HardwareSummary("test", "test", null, null, null, null);
        }
    }

    private sealed class CountingRefreshService : IHardwareRefreshService
    {
        public event EventHandler<HardwareRefreshStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<HardwareSnapshot>? SnapshotRefreshed;
        public int Count { get; private set; }
        public bool IsRefreshing => false;
        public HardwareRefreshResult? LastResult { get; private set; }
        public Task<HardwareRefreshResult> RefreshAsync(HardwareRefreshReason reason, CancellationToken cancellationToken = default)
        {
            Count++;
            LastResult = new HardwareRefreshResult { Reason = reason, State = HardwareRefreshState.Completed, CompletedAt = DateTimeOffset.Now };
            SnapshotRefreshed?.Invoke(this, new HardwareSnapshot { Timestamp = DateTimeOffset.Now });
            StatusChanged?.Invoke(this, new HardwareRefreshStatusChangedEventArgs { Reason = reason, State = HardwareRefreshState.Completed, Result = LastResult });
            return Task.FromResult(LastResult);
        }
    }

    private sealed class TokenLifetimeRefreshService : IHardwareRefreshService
    {
        private int count;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FirstTokenWasCanceled { get; private set; }
        public bool FirstTokenWasDisposed { get; private set; }
        public event EventHandler<HardwareRefreshStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<HardwareSnapshot>? SnapshotRefreshed
        {
            add { }
            remove { }
        }
        public bool IsRefreshing => false;
        public HardwareRefreshResult? LastResult => null;

        public async Task<HardwareRefreshResult> RefreshAsync(
            HardwareRefreshReason reason,
            CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref count);
            if (call == 1)
            {
                FirstStarted.TrySetResult();
                await ReleaseFirst.Task;
                FirstTokenWasCanceled = cancellationToken.IsCancellationRequested;
                try
                {
                    _ = cancellationToken.WaitHandle;
                }
                catch (ObjectDisposedException)
                {
                    FirstTokenWasDisposed = true;
                }
                finally
                {
                    FirstCompleted.TrySetResult();
                }
            }

            return new HardwareRefreshResult
            {
                Reason = reason,
                State = HardwareRefreshState.Completed,
                CompletedAt = DateTimeOffset.Now
            };
        }
    }
}
