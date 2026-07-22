namespace HardwareVision.Tests;

internal static class LifecycleAndCancellationReviewTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Lifecycle review 01 page ViewModels remain lazy", LazyPages),
        ("Lifecycle review 02 SetPageActive remains central", () => MainContains("private static void SetPageActive")),
        ("Lifecycle review 03 advanced page unsubscribes", AdvancedUnsubscribes),
        ("Lifecycle review 04 advanced cancellation is observed", AdvancedCancellation),
        ("Lifecycle review 05 report dispose cancels load", ReportCancellation),
        ("Lifecycle review 06 settings dispose unsubscribes", SettingsUnsubscribes),
        ("Lifecycle review 07 polling owns one task", PollingSingleTask),
        ("Lifecycle review 08 sensor history unsubscribes", HistoryUnsubscribes),
        ("Lifecycle review 09 navigation tasks are observed", NavigationObserved),
        ("Lifecycle review 10 window disposes DataContext", WindowDisposes)
    ];

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
    private static string Main => Read("HardwareVision", "ViewModels", "MainViewModel.cs");
    private static void MainContains(string value) => TestSupport.True(Main.Contains(value, StringComparison.Ordinal), value);
    private static void LazyPages() { MainContains("advancedSensors ??="); MainContains("gpu ??="); MainContains("settingsViewModel ??="); }
    private static void AdvancedUnsubscribes() { string source = Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs"); TestSupport.True(TraceworkPilotSource.Count(source, "dashboard.PropertyChanged -= OnDashboardPropertyChanged") >= 2, "unsubscribe"); }
    private static void AdvancedCancellation() { string source = Read("HardwareVision", "ViewModels", "AdvancedSensorsViewModel.cs"); TestSupport.True(source.Contains("catch (OperationCanceledException)", StringComparison.Ordinal), "observed cancellation"); TestSupport.True(source.Contains("owner.Dispose()", StringComparison.Ordinal), "CTS dispose"); }
    private static void ReportCancellation() { string source = Read("HardwareVision", "ViewModels", "GameSessionReportViewModel.cs"); TestSupport.True(source.Contains("Interlocked.Exchange(ref loadCancellation, null)", StringComparison.Ordinal), "exchange"); TestSupport.True(source.Contains("cancellation?.Cancel()", StringComparison.Ordinal), "cancel"); }
    private static void SettingsUnsubscribes() { string source = Read("HardwareVision", "ViewModels", "SettingsViewModel.cs"); foreach (string value in new[] { "motionService.MotionChanged -=", "themeTransitionService.TransitionChanged -=", "hardwareRefreshService.StatusChanged -=" }) TestSupport.True(source.Contains(value, StringComparison.Ordinal), value); }
    private static void PollingSingleTask() { string source = Read("HardwareVision", "Services", "PollingService.cs"); TestSupport.True(source.Contains("pollingTask = Task.Run", StringComparison.Ordinal), "owned task"); TestSupport.True(source.Contains("pollExecutionLock", StringComparison.Ordinal), "single flight"); }
    private static void HistoryUnsubscribes() { string source = Read("HardwareVision", "Services", "SensorHistoryService.cs"); TestSupport.True(source.Contains("pollingService.ReadingsUpdated -= OnReadingsUpdated", StringComparison.Ordinal), "history unsubscribe"); }
    private static void NavigationObserved() { MainContains("ObserveNavigationTaskAsync"); MainContains("catch (OperationCanceledException)"); }
    private static void WindowDisposes() { string source = Read("HardwareVision", "MainWindow.xaml.cs"); TestSupport.True(source.Contains("(DataContext as IDisposable)?.Dispose()", StringComparison.Ordinal), "DataContext dispose"); }
}
