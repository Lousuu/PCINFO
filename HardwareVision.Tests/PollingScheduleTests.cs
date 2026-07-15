using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class PollingScheduleTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Polling schedule 01 foreground transition wakes immediately", TestSupport.Run(ForegroundTransitionWakesImmediatelyAsync)),
        ("Polling schedule 02 interval update wakes immediately", TestSupport.Run(IntervalUpdateWakesImmediatelyAsync)),
        ("Polling schedule 03 rapid switching never reenters", TestSupport.Run(RapidSwitchingNeverReentersAsync)),
        ("Polling schedule 04 slow collection skips missed cycles", TestSupport.Run(SlowCollectionSkipsMissedCyclesAsync)),
        ("Polling schedule 05 stop interrupts schedule wait", TestSupport.Run(StopInterruptsScheduleWaitAsync)),
        ("Polling schedule 06 one thousand switches remain coalesced", TestSupport.Run(OneThousandSwitchesRemainCoalescedAsync))
    ];

    private static async Task ForegroundTransitionWakesImmediatelyAsync()
    {
        CountingSensorService sensors = new();
        await using PollingService polling = Create(sensors, foreground: 0.5d, background: 120);
        polling.SetBackgroundMode(true);
        await polling.StartAsync();
        await WaitForPollsAsync(sensors, 1, TimeSpan.FromSeconds(2));
        Stopwatch stopwatch = Stopwatch.StartNew();
        polling.SetBackgroundMode(false);
        await WaitForPollsAsync(sensors, 2, TimeSpan.FromSeconds(2));
        stopwatch.Stop();
        TestSupport.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"foreground wake took {stopwatch.Elapsed}");
    }

    private static async Task IntervalUpdateWakesImmediatelyAsync()
    {
        CountingSensorService sensors = new();
        await using PollingService polling = Create(sensors, foreground: 30d, background: 120);
        await polling.StartAsync();
        await WaitForPollsAsync(sensors, 1, TimeSpan.FromSeconds(2));
        Stopwatch stopwatch = Stopwatch.StartNew();
        polling.UpdateIntervals(0.5d, 120);
        await WaitForPollsAsync(sensors, 2, TimeSpan.FromSeconds(2));
        TestSupport.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "updated foreground interval was not applied immediately");
    }

    private static async Task RapidSwitchingNeverReentersAsync()
    {
        CountingSensorService sensors = new(TimeSpan.FromMilliseconds(80));
        await using PollingService polling = Create(sensors, foreground: 0.5d, background: 120);
        await polling.StartAsync();
        for (int index = 0; index < 100; index++) polling.SetBackgroundMode((index & 1) == 0);
        await Task.Delay(600);
        TestSupport.Equal(1, sensors.MaximumConcurrentCalls, "polling reentered");
    }

    private static async Task SlowCollectionSkipsMissedCyclesAsync()
    {
        CountingSensorService sensors = new(TimeSpan.FromMilliseconds(750));
        await using PollingService polling = Create(sensors, foreground: 0.5d, background: 120);
        await polling.StartAsync();
        await Task.Delay(2400);
        await polling.StopAsync();
        TestSupport.True(sensors.PollCount <= 4, $"slow polling busy-looped: {sensors.PollCount} calls");
        TestSupport.Equal(1, sensors.MaximumConcurrentCalls, "slow polling reentered");
    }

    private static async Task StopInterruptsScheduleWaitAsync()
    {
        CountingSensorService sensors = new();
        await using PollingService polling = Create(sensors, foreground: 30d, background: 120);
        await polling.StartAsync();
        await WaitForPollsAsync(sensors, 1, TimeSpan.FromSeconds(2));
        Stopwatch stopwatch = Stopwatch.StartNew();
        await polling.StopAsync();
        TestSupport.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"stop took {stopwatch.Elapsed}");
    }

    private static async Task OneThousandSwitchesRemainCoalescedAsync()
    {
        CountingSensorService sensors = new(TimeSpan.FromMilliseconds(5));
        await using PollingService polling = Create(sensors, foreground: 0.5d, background: 120);
        await polling.StartAsync();
        Measurement measurement = TestSupport.Measure("polling-1000-switches", () =>
        {
            for (int index = 0; index < 1000; index++) polling.SetBackgroundMode((index & 1) == 0);
        });
        await Task.Delay(200);
        TestSupport.Equal(1, sensors.MaximumConcurrentCalls, "stress switching reentered");
        TestSupport.True(measurement.Elapsed < TimeSpan.FromSeconds(2), "switch signaling was unexpectedly slow");
    }

    private static PollingService Create(CountingSensorService sensors, double foreground, int background) =>
        new(sensors, new AppSettings
        {
            RefreshIntervalSeconds = foreground,
            BackgroundRefreshIntervalSeconds = background
        });

    private static async Task WaitForPollsAsync(
        CountingSensorService sensors,
        int expected,
        TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (sensors.PollCount < expected && stopwatch.Elapsed < timeout) await Task.Delay(10);
        TestSupport.True(sensors.PollCount >= expected, $"expected {expected} polls, got {sensors.PollCount}");
    }
}
