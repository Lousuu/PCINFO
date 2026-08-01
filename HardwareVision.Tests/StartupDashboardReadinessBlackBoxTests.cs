using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class StartupDashboardReadinessBlackBoxTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return (
            "Startup Dashboard readiness black box sensors-only keeps System pending",
            SensorsOnlyKeepsSystemPending);
    }

    private static void SensorsOnlyKeepsSystemPending()
    {
        TestSupport.InTemporaryDirectory(
            (Func<string, Task>)(directory =>
                RunOnDispatcherAsync(async dispatcher =>
                {
                    using SensorHistoryService history = new();
                    await using PollingService polling = new(
                        new SingleReadingSensorService(),
                        new AppSettings());
                    using DashboardViewModel dashboard = new(
                        new AppSettings(),
                        new UnusedHardwareInfoService(),
                        polling,
                        new SettingsService(directory),
                        dispatcher,
                        history);
                    TaskCompletionSource<StartupInitialProjectionSnapshot> projection = new(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    dashboard.InitialProjectionApplied += Capture;
                    await polling.PollNowAsync();
                    StartupInitialProjectionSnapshot snapshot =
                        await projection.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    StartupProjectionSlotSnapshot system = snapshot.Slots.Single(
                        slot => slot.Region == HardwareOverviewKind.System);
                    TestSupport.Equal(
                        StartupProjectionState.Pending,
                        system.State,
                        "sensors-only System state");
                    dashboard.InitialProjectionApplied -= Capture;

                    void Capture(object? sender, StartupInitialProjectionSnapshot value) =>
                        projection.TrySetResult(value);
                }))).GetAwaiter().GetResult();
    }

    private static Task RunOnDispatcherAsync(Func<Dispatcher, Task> test)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            Name = "StartupDashboardReadiness.BlackBox"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class SingleReadingSensorService : ISensorService
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

    private sealed class UnusedHardwareInfoService : IHardwareInfoService
    {
        public void InvalidateCaches()
        {
        }

        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot());

        public async Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(
            CancellationToken cancellationToken = default) =>
            (await GetHardwareSnapshotAsync(cancellationToken)).Devices;

        public Task<HardwareSummary> GetHardwareSummaryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("--", "--", null, null, null, null));
    }
}
