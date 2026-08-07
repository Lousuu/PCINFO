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
        ("Lifecycle review 10 window disposes DataContext", WindowDisposes),
        ("Lifecycle review 11 app exit drains queued diagnostics", TestSupport.Run(AppExitDrainsLoggerAsync)),
        ("Lifecycle review 12 theme transitions avoid synchronous dispatcher calls", ThemeDispatchIsAsync),
        ("Lifecycle review 13 startup task I/O stays off the UI thread", StartupTaskIoIsAsync),
        ("Lifecycle review 14 game session path failures stay bounded", GameSessionPathsFailBoundedly),
        ("Lifecycle review 15 ViewModel dispatch never blocks producers", ViewModelDispatchIsNonBlocking)
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
    private static void ThemeDispatchIsAsync()
    {
        string theme = Read("HardwareVision", "Services", "ThemeService.cs");
        string transition = Read("HardwareVision", "Services", "ThemeTransitionService.cs");
        TestSupport.False(theme.Contains("Dispatcher.Invoke(", StringComparison.Ordinal), "theme service sync dispatcher");
        TestSupport.False(transition.Contains("dispatcher.Invoke(", StringComparison.Ordinal), "theme transition sync dispatcher");
        TestSupport.True(transition.Contains("await dispatcher.InvokeAsync(", StringComparison.Ordinal), "theme transition async dispatcher");
    }
    private static void StartupTaskIoIsAsync()
    {
        string app = Read("HardwareVision", "App.xaml.cs");
        string settings = Read("HardwareVision", "ViewModels", "SettingsViewModel.cs");
        string startup = Read("HardwareVision", "Services", "StartupTaskService.cs");
        TestSupport.False(settings.Contains("startupService.SetEnabled(", StringComparison.Ordinal), "settings sync startup call");
        TestSupport.True(settings.Contains("SetStartupEnabledAsync(", StringComparison.Ordinal), "settings async startup call");
        TestSupport.True(settings.Contains("CancelAutoStartChange();", StringComparison.Ordinal), "settings disposal cancellation");
        TestSupport.True(startup.Contains("operationGate.WaitAsync(", StringComparison.Ordinal), "startup single flight");
        TestSupport.True(startup.Contains("ReadToEndAsync(cancellationToken)", StringComparison.Ordinal), "startup async pipe read");
        TestSupport.True(startup.Contains("WaitForExitAsync(cancellationToken)", StringComparison.Ordinal), "startup async process wait");
        TestSupport.False(startup.Contains("StandardOutput.ReadToEnd()", StringComparison.Ordinal), "startup sync pipe deadlock");
        TestSupport.False(app.Contains("Task.Run(() => StartupService.IsEnabled())", StringComparison.Ordinal), "app sync startup query wrapper");
        TestSupport.True(app.Contains("StartupService.IsStartupEnabledAsync()", StringComparison.Ordinal), "app async startup query");
    }
    private static void GameSessionPathsFailBoundedly()
    {
        string game = Read("HardwareVision", "ViewModels", "GamePerformanceViewModel.cs");
        string report = Read("HardwareVision", "ViewModels", "GameSessionReportViewModel.cs");
        string gameMethod = game[game.IndexOf("private void OpenPath", StringComparison.Ordinal)..game.IndexOf("private void ResetCharts", StringComparison.Ordinal)];
        string reportMethod = report[report.IndexOf("private void OpenDirectory", StringComparison.Ordinal)..report.IndexOf("private async Task ExportPlainCsvAsync", StringComparison.Ordinal)];
        TestSupport.True(gameMethod.IndexOf("try", StringComparison.Ordinal) < gameMethod.IndexOf("File.Exists", StringComparison.Ordinal), "game path preparation is guarded");
        TestSupport.True(reportMethod.IndexOf("try", StringComparison.Ordinal) < reportMethod.IndexOf("Path.GetDirectoryName", StringComparison.Ordinal), "report path preparation is guarded");
        foreach (string exception in new[] { "IOException", "UnauthorizedAccessException", "SecurityException", "ArgumentException", "NotSupportedException" })
        {
            TestSupport.True(gameMethod.Contains(exception, StringComparison.Ordinal), $"game path catches {exception}");
            TestSupport.True(reportMethod.Contains(exception, StringComparison.Ordinal), $"report path catches {exception}");
        }
        TestSupport.True(gameMethod.Contains("StatusText =", StringComparison.Ordinal), "game path reports failure");
        TestSupport.True(reportMethod.Contains("StatusText =", StringComparison.Ordinal), "report path reports failure");
    }
    private static void ViewModelDispatchIsNonBlocking()
    {
        string helpers = Read("HardwareVision", "ViewModels", "ViewModelHelpers.cs");
        TestSupport.False(helpers.Contains("dispatcher.Invoke(", StringComparison.Ordinal), "ViewModel sync dispatcher");
        TestSupport.True(helpers.Contains("dispatcher.BeginInvoke(", StringComparison.Ordinal), "ViewModel async dispatcher");
        TestSupport.True(helpers.Contains("DispatcherPriority.DataBind", StringComparison.Ordinal), "ViewModel update priority");
        TestSupport.True(helpers.Contains("dispatcher.HasShutdownStarted", StringComparison.Ordinal), "ViewModel shutdown guard");
        TestSupport.True(helpers.Contains("dispatcher.HasShutdownFinished", StringComparison.Ordinal), "ViewModel shutdown completion guard");

        System.Windows.Threading.Dispatcher dispatcher =
            System.Windows.Threading.Dispatcher.CurrentDispatcher;
        using ManualResetEventSlim producerReturned = new();
        int applied = 0;
        Thread producer = new(() =>
        {
            HardwareVision.ViewModels.ViewModelHelpers.Dispatch(
                dispatcher,
                () => Interlocked.Exchange(ref applied, 1));
            producerReturned.Set();
        })
        {
            IsBackground = true
        };
        producer.Start();
        bool returnedWithoutUiPump = producerReturned.Wait(
            TimeSpan.FromSeconds(1));
        TestSupport.Equal(0, Volatile.Read(ref applied), "queued ViewModel update waits for UI dispatcher");

        System.Windows.Threading.DispatcherFrame frame = new();
        System.Windows.Threading.DispatcherTimer timer = new(
            TimeSpan.FromMilliseconds(20),
            System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            dispatcher);
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Stop();
        producer.Join(TimeSpan.FromSeconds(1));

        TestSupport.True(returnedWithoutUiPump, "background producer is never blocked by ViewModel dispatch");
        TestSupport.Equal(1, Volatile.Read(ref applied), "queued ViewModel update runs on UI dispatcher");
    }
    private static async Task AppExitDrainsLoggerAsync()
    {
        string app = Read("HardwareVision", "App.xaml.cs");
        string logger = Read("HardwareVision", "Utilities", "AppLogger.cs");
        int shutdown = app.IndexOf("ShutdownServicesAsync().GetAwaiter().GetResult()", StringComparison.Ordinal);
        int flush = app.IndexOf("AppLogger.FlushAsync().GetAwaiter().GetResult()", StringComparison.Ordinal);
        TestSupport.True(shutdown >= 0 && flush > shutdown, "logger drains after service shutdown");
        TestSupport.True(logger.Contains("TaskCompletionSource completion", StringComparison.Ordinal), "flush uses completion barrier");
        TestSupport.True(logger.Contains("request.FlushCompletion.TrySetResult()", StringComparison.Ordinal), "writer completes FIFO barrier");
        await HardwareVision.Utilities.AppLogger.FlushAsync();
    }
}
