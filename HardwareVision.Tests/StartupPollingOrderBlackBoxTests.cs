namespace HardwareVision.Tests;

internal static class StartupPollingOrderBlackBoxTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return (
            "Startup coordination black box App starts polling before hardware refresh",
            AppStartsPollingBeforeHardwareRefresh);
    }

    private static void AppStartsPollingBeforeHardwareRefresh()
    {
        string source = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "HardwareVision", "App.xaml.cs"));
        int startupTail = source.IndexOf(
            "ObserveTask(SyncStartupStateAsync(mainWindow, startupClock)",
            StringComparison.Ordinal);
        int scheduleMemory = source.IndexOf(
            "ScheduleMemoryCheckpoints();",
            startupTail,
            StringComparison.Ordinal);
        TestSupport.True(startupTail >= 0 && scheduleMemory > startupTail, "startup tail");

        string startup = source[startupTail..scheduleMemory];
        int handler = startup.IndexOf("RegisterFirstPollingDataLog(startupClock);", StringComparison.Ordinal);
        int pollingStart = startup.IndexOf("PollingService.StartAsync()", StringComparison.Ordinal);
        int gatedRefresh = startup.IndexOf(
            "WaitForFirstPollingThenRefreshHardwareAsync(",
            StringComparison.Ordinal);

        TestSupport.True(handler >= 0, "first polling handler registration");
        TestSupport.True(pollingStart > handler, "polling starts after handler registration");
        TestSupport.True(gatedRefresh > pollingStart, "startup refresh is gated after polling start");
        TestSupport.False(
            startup.Contains(
                "mainWindow.RefreshHardwareInfoAsync(HardwareRefreshReason.Startup)",
                StringComparison.Ordinal),
            "startup tail still starts hardware refresh directly");
    }
}
