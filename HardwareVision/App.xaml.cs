using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision;

public partial class App : System.Windows.Application
{
    private bool servicesDisposed;
    private int unhandledShutdownStarted;

    public ISettingsService SettingsService { get; private set; } = null!;

    public IThemeService ThemeService { get; private set; } = null!;

    public IMotionService MotionService { get; private set; } = null!;

    public IThemeTransitionService ThemeTransitionService { get; private set; } = null!;

    public INavigationTransitionService NavigationTransitionService { get; private set; } = null!;

    public IStartupSequenceService StartupSequenceService { get; private set; } = null!;

    private SystemMotionEnvironment? motionEnvironment;

    public IStartupService StartupService { get; private set; } = null!;

    public IHardwareInfoService HardwareInfoService { get; private set; } = null!;

    public ISensorService SensorService { get; private set; } = null!;

    public SensorDiagnosticService SensorDiagnosticService { get; private set; } = null!;

    public PollingService PollingService { get; private set; } = null!;

    public IHardwareRefreshService HardwareRefreshService { get; private set; } = null!;

    public ISensorHistoryService SensorHistoryService { get; private set; } = null!;

    public IGameEnergyTracker GameEnergyTracker { get; private set; } = null!;

    public IGamePerformanceLimitTracker GamePerformanceLimitTracker { get; private set; } = null!;

    public IGameSessionRecorder GameSessionRecorder { get; private set; } = null!;

    public IForegroundProcessTracker ForegroundProcessTracker { get; private set; } = EmptyForegroundProcessTracker.Instance;

    public ITrayService TrayService { get; private set; } = null!;

    public AppSettings Settings { get; private set; } = new();

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            bool recoverable = ApplicationExceptionPolicy.IsRecoverableUi(args.Exception);
            AppLogger.LogError(
                recoverable ? "Recoverable dispatcher exception." : "Unhandled dispatcher exception; controlled shutdown requested.",
                args.Exception,
                $"dispatcher-unhandled:{args.Exception.GetType().FullName}:{args.Exception.Message}",
                TimeSpan.FromMinutes(1));
            if (recoverable)
            {
                args.Handled = true;
                return;
            }

            if (ApplicationExceptionPolicy.IsFatal(args.Exception))
            {
                args.Handled = false;
                return;
            }

#if DEBUG
            args.Handled = false;
#else
            args.Handled = true;
            _ = ControlledShutdownAfterUnhandledExceptionAsync(args.Exception);
#endif
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.LogError(
                    "Unhandled application exception.",
                    exception,
                    $"appdomain-unhandled:{exception.GetType().FullName}",
                    TimeSpan.Zero);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.LogError(
                "Unobserved task exception.",
                args.Exception,
                $"task-unobserved:{args.Exception.GetType().FullName}",
                TimeSpan.Zero);
            args.SetObserved();
        };
    }

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        Stopwatch startupClock = Stopwatch.StartNew();
        try
        {
            base.OnStartup(e);
            AppLogger.LogKeyEvent("HardwareVision starting.");
            AppLogger.LogStartupStage("App startup start", startupClock);

            if (e.Args.Any(arg => string.Equals(arg, "--export-official-comparison", StringComparison.OrdinalIgnoreCase)))
            {
                SensorDiagnosticService diagnostics = new();
                await diagnostics.ExportOfficialComparisonAsync();
                Shutdown();
                return;
            }

            Stopwatch phaseClock = Stopwatch.StartNew();
            SettingsService = new SettingsService();
            AppLogger.LogStartupStage("SettingsService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            StartupService = new StartupTaskService();
            AppLogger.LogStartupStage("StartupTaskService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            HardwareInfoService = new HardwareInfoService();
            AppLogger.LogStartupStage("HardwareInfoService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            LibreHardwareMonitorProvider libreHardwareMonitorProvider = new();
            WmiCpuClockSensorProvider wmiCpuClockSensorProvider = new();
            WindowsCpuPerformanceLimitSensorProvider windowsCpuPerformanceLimitSensorProvider = new();
            NvidiaPerformanceLimitSensorProvider nvidiaPerformanceLimitSensorProvider = new();
            SensorAggregatorService sensorAggregatorService = new(
            [
                libreHardwareMonitorProvider,
                wmiCpuClockSensorProvider,
                windowsCpuPerformanceLimitSensorProvider,
                nvidiaPerformanceLimitSensorProvider
            ]);
            SensorService = sensorAggregatorService;
            AppLogger.LogStartupStage("SensorAggregatorService and providers created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            SensorDiagnosticService = new SensorDiagnosticService();
            AppLogger.LogStartupStage("SensorDiagnosticService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            Settings = await SettingsService.LoadAsync();
            AppLogger.LogStartupStage("SettingsService.LoadAsync completed", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            ThemeService = new ThemeService(this);
            AppTheme startupTheme = AppThemeParser.Parse(Settings.Theme);
            if (!ThemeService.ApplyTheme(startupTheme) && startupTheme != AppTheme.Classic)
            {
                ThemeService.ApplyTheme(AppTheme.Classic);
            }
            Settings.Theme = AppThemeParser.ToStorageValue(ThemeService.CurrentTheme);
            if (ThemeService.CurrentTheme != startupTheme)
            {
                _ = await SettingsService.TrySaveAsync(Settings);
            }
            AppLogger.LogStartupStage($"ThemeService applied {ThemeService.CurrentTheme}", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            motionEnvironment = new SystemMotionEnvironment();
            MotionLevel requestedMotionLevel = MotionLevelParser.Parse(Settings.Motion);
            MotionService = new MotionService(motionEnvironment, requestedMotionLevel, Dispatcher);
            string normalizedMotion = MotionLevelParser.ToStorageValue(MotionService.RequestedLevel);
            if (!string.Equals(Settings.Motion, normalizedMotion, StringComparison.Ordinal))
            {
                Settings.Motion = normalizedMotion;
                _ = await SettingsService.TrySaveAsync(Settings);
            }
            AppLogger.LogStartupStage($"MotionService applied {MotionService.CurrentProfile.EffectiveLevel}", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            StartupSequenceService = new StartupSequenceService(
                ThemeService.CurrentTheme,
                MotionService.CurrentProfile.EffectiveLevel);
            StartupSequenceService.ReportMilestone(
                StartupMilestoneId.ThemeResources,
                StartupMilestoneState.Ready,
                $"{ThemeService.CurrentTheme} resources applied");
            AppLogger.LogStartupStage("StartupSequenceService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            ThemeTransitionService = new ThemeTransitionService(ThemeService, MotionService, Dispatcher);
            AppLogger.LogStartupStage("ThemeTransitionService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            NavigationTransitionService = new NavigationTransitionService();
            AppLogger.LogStartupStage("NavigationTransitionService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            PollingService = new PollingService(SensorService, Settings);
            StartupSequenceService.ReportMilestone(
                StartupMilestoneId.SensorBus,
                StartupMilestoneState.Pending,
                "Awaiting first shared polling sample");
            AppLogger.LogStartupStage("PollingService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            HardwareRefreshService = new HardwareRefreshService(
                HardwareInfoService,
                sensorAggregatorService,
                PollingService);
            AppLogger.LogStartupStage("HardwareRefreshService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            GameEnergyTracker = new GameEnergyTracker(PollingService, Settings);
            AppLogger.LogStartupStage("GameEnergyTracker created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            GamePerformanceLimitTracker = new GamePerformanceLimitTracker(
                PollingService,
                [windowsCpuPerformanceLimitSensorProvider, nvidiaPerformanceLimitSensorProvider]);
            AppLogger.LogStartupStage("GamePerformanceLimitTracker created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            GameSessionRecorder = new CsvGameSessionRecorder(
                energyTracker: GameEnergyTracker,
                performanceLimitTracker: GamePerformanceLimitTracker,
                pollingService: PollingService,
                frameStorageModeProvider: () => Settings.GameSessionFrameStorageMode);
            Stopwatch gameSessionRecoveryClock = Stopwatch.StartNew();
            Task gameSessionRecoveryTask = GameSessionRecorder.RecoverIncompleteSessionsAsync();
            AppLogger.LogStartupStage("GameSessionRecorder recovery started", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            SensorHistoryService = new SensorHistoryService(PollingService);
            StartupSequenceService.ReportMilestone(
                StartupMilestoneId.HistoryBuffer,
                StartupMilestoneState.Ready,
                "Subscribed to shared polling source");
            AppLogger.LogStartupStage("SensorHistoryService created", startupClock, phaseClock.Elapsed);

            phaseClock.Restart();
            ForegroundProcessTracker = new ForegroundProcessTracker();
            AppLogger.LogStartupStage("ForegroundProcessTracker created", startupClock, phaseClock.Elapsed);

            StartupSequenceService.ReportMilestone(
                StartupMilestoneId.ServiceGraph,
                StartupMilestoneState.Ready,
                "Single App-owned service graph established");

            phaseClock.Restart();
            MainWindow mainWindow = new(
                Settings,
                HardwareInfoService,
                PollingService,
                SettingsService,
                ThemeService,
                MotionService,
                ThemeTransitionService,
                NavigationTransitionService,
                StartupSequenceService,
                StartupService,
                SensorDiagnosticService,
                ForegroundProcessTracker,
                SensorHistoryService,
                GameSessionRecorder,
                GameEnergyTracker,
                GamePerformanceLimitTracker,
                HardwareRefreshService);
            AppLogger.LogStartupStage("MainWindow constructed", startupClock, phaseClock.Elapsed);

            MainWindow = mainWindow;
            RegisterFirstDashboardDataLog(mainWindow, startupClock);
            TrayService = new TrayService();
            TrayService.Initialize(mainWindow);
            TrayService.OpenRequested += (_, _) => mainWindow.ShowFromTray();
            TrayService.RefreshHardwareInfoRequested += (_, _) => ObserveTask(
                mainWindow.RefreshHardwareInfoAsync(HardwareRefreshReason.Tray),
                "tray-refresh-hardware-info");
            TrayService.SettingsRequested += (_, _) =>
            {
                mainWindow.ShowFromTray();
                mainWindow.ShowSettingsPage();
            };
            TrayService.ExitRequested += async (_, _) =>
            {
                try
                {
                    mainWindow.RequestExit();
                    await ShutdownServicesAsync();
                    Shutdown();
                }
                catch (Exception exception)
                {
                    AppLogger.LogError(
                        "Tray exit failed.",
                        exception,
                        $"tray-exit:{exception.GetType().FullName}",
                        TimeSpan.FromMinutes(5));
                    Shutdown(-1);
                }
            };

            phaseClock.Restart();
            mainWindow.Show();
            AppLogger.LogStartupStage("MainWindow.Show completed", startupClock, phaseClock.Elapsed);
            AppLogger.LogMemoryCheckpoint("main window just shown");

            ObserveTask(LogTaskCompletionAsync(
                gameSessionRecoveryTask,
                "GameSessionRecorder recovery completed",
                startupClock,
                gameSessionRecoveryClock), "game-session-recovery");
            ObserveTask(SyncStartupStateAsync(mainWindow, startupClock), "startup-state-sync");
            ObserveTask(LogTaskCompletionAsync(
                mainWindow.RefreshHardwareInfoAsync(HardwareRefreshReason.Startup),
                "Initial hardware snapshot completed",
                startupClock,
                Stopwatch.StartNew()), "initial-hardware-info");
            RegisterFirstPollingDataLog(startupClock);
            ObserveTask(LogTaskCompletionAsync(PollingService.StartAsync(), "PollingService.StartAsync returned", startupClock, Stopwatch.StartNew()), "polling-start");
            ScheduleMemoryCheckpoints();
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "HardwareVision startup failed.",
                exception,
                $"startup-failed:{exception.GetType().FullName}",
                TimeSpan.Zero);
            Shutdown(-1);
        }
    }

    private async Task SyncStartupStateAsync(MainWindow mainWindow, Stopwatch startupClock)
    {
        Stopwatch phaseClock = Stopwatch.StartNew();
        bool startupEnabled = await Task.Run(() => StartupService.IsEnabled());
        AppLogger.LogStartupStage("StartupTaskService.IsEnabled completed", startupClock, phaseClock.Elapsed);

        if (Settings.AutoStartEnabled != startupEnabled)
        {
            Settings.AutoStartEnabled = startupEnabled;
            await SettingsService.SaveAsync(Settings);
        }

        await Dispatcher.InvokeAsync(() => mainWindow.ApplyStartupState(startupEnabled));
    }

    private void RegisterFirstPollingDataLog(Stopwatch startupClock)
    {
        Stopwatch firstPollingClock = Stopwatch.StartNew();
        EventHandler<SensorReadingsUpdatedEventArgs>? handler = null;
        EventHandler<Exception>? failureHandler = null;
        void Detach()
        {
            PollingService.ReadingsUpdated -= handler;
            PollingService.PollingFailed -= failureHandler;
        }
        handler = (_, args) =>
        {
            Detach();
            StartupSequenceService.ReportMilestone(
                StartupMilestoneId.SensorBus,
                args.Readings.Count > 0 ? StartupMilestoneState.Ready : StartupMilestoneState.Partial,
                args.Readings.Count > 0
                    ? $"{args.Readings.Count} readings received"
                    : "Shared polling completed without visible readings");
            AppLogger.LogStartupStage($"PollingService first readings received ({args.Readings.Count} readings)", startupClock, firstPollingClock.Elapsed);
            AppLogger.LogMemoryCheckpoint("first data refresh completed");
        };

        failureHandler = (_, exception) =>
        {
            Detach();
            StartupSequenceService.ReportMilestone(
                StartupMilestoneId.SensorBus,
                StartupMilestoneState.Failed,
                exception.Message);
        };

        PollingService.ReadingsUpdated += handler;
        PollingService.PollingFailed += failureHandler;
    }

    private static void RegisterFirstDashboardDataLog(MainWindow mainWindow, Stopwatch startupClock)
    {
        if (mainWindow.DataContext is not ViewModels.MainViewModel viewModel)
        {
            return;
        }

        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName != nameof(ViewModels.DashboardViewModel.CurrentSensorReadings)
                || viewModel.Dashboard.CurrentSensorReadings.Count == 0)
            {
                return;
            }

            viewModel.Dashboard.PropertyChanged -= handler;
            AppLogger.LogStartupStage("Dashboard first data displayed", startupClock);
            AppLogger.LogMemoryCheckpoint("dashboard first data displayed");
        };

        viewModel.Dashboard.PropertyChanged += handler;
    }

    private static async Task LogTaskCompletionAsync(Task task, string stage, Stopwatch startupClock, Stopwatch phaseClock)
    {
        await task.ConfigureAwait(false);
        AppLogger.LogStartupStage(stage, startupClock, phaseClock.Elapsed);
    }

    private static void ScheduleMemoryCheckpoints()
    {
        ObserveTask(LogDelayedMemoryCheckpointAsync("idle 5 minutes", TimeSpan.FromMinutes(5)), "memory-checkpoint-5m");
        ObserveTask(LogDelayedMemoryCheckpointAsync("idle 10 minutes", TimeSpan.FromMinutes(10)), "memory-checkpoint-10m");
    }

    private static async Task LogDelayedMemoryCheckpointAsync(string checkpoint, TimeSpan delay)
    {
        await Task.Delay(delay);
        AppLogger.LogMemoryCheckpoint(checkpoint);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppLogger.LogKeyEvent("HardwareVision exiting.");
        if (!servicesDisposed)
        {
            ShutdownServicesAsync().GetAwaiter().GetResult();
        }
        base.OnExit(e);
    }

    private async Task ShutdownServicesAsync()
    {
        if (servicesDisposed)
        {
            return;
        }

        servicesDisposed = true;

        await DisposeServiceAsync(GameSessionRecorder, "game-session-recorder");
        await DisposeServiceAsync(GameEnergyTracker, "game-energy-tracker");
        await DisposeServiceAsync(GamePerformanceLimitTracker, "game-performance-limit-tracker");
        await DisposeServiceAsync(SensorHistoryService, "sensor-history-service");
        await DisposeServiceAsync(PollingService, "polling-service");
        await DisposeServiceAsync(SensorService, "sensor-service");
        await DisposeServiceAsync(ForegroundProcessTracker as IDisposable, "foreground-process-tracker");
        await DisposeServiceAsync(NavigationTransitionService as IDisposable, "navigation-transition-service");
        await DisposeServiceAsync(ThemeTransitionService as IDisposable, "theme-transition-service");
        await DisposeServiceAsync(StartupSequenceService, "startup-sequence-service");
        await DisposeServiceAsync(MotionService as IDisposable, "motion-service");
        await DisposeServiceAsync(motionEnvironment, "motion-environment");

        try
        {
            TrayService?.Dispose();
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "Tray service dispose failed.",
                exception,
                $"tray-dispose:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }

    private async Task ControlledShutdownAfterUnhandledExceptionAsync(Exception exception)
    {
        if (Interlocked.Exchange(ref unhandledShutdownStarted, 1) != 0) return;
        try
        {
            if (GameSessionRecorder is not null)
            {
                await GameSessionRecorder.CompleteAsync(
                    GameSessionEndReason.CaptureFailed,
                    completedNormally: false).ConfigureAwait(false);
            }
        }
        catch (Exception finalizationException) when (!ApplicationExceptionPolicy.IsFatal(finalizationException))
        {
            AppLogger.LogError("Session finalization during controlled shutdown failed.", finalizationException,
                "unhandled-session-finalization", TimeSpan.Zero);
        }

        try
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await ShutdownServicesAsync();
                Shutdown(-1);
            }).Task.Unwrap();
        }
        catch (Exception shutdownException) when (!ApplicationExceptionPolicy.IsFatal(shutdownException))
        {
            AppLogger.LogError("Controlled shutdown after an unhandled exception failed.", shutdownException,
                "unhandled-controlled-shutdown", TimeSpan.Zero);
            Shutdown(-1);
        }
    }

    private static void ObserveTask(Task task, string operation)
    {
        _ = ObserveTaskAsync(task, operation);
    }

    private static async Task ObserveTaskAsync(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                $"Background task failed: {operation}.",
                exception,
                $"background-task:{operation}:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }

    private static async Task DisposeServiceAsync(IDisposable? service, string serviceName)
    {
        if (service is null)
        {
            return;
        }

        try
        {
            if (service is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
                return;
            }

            service.Dispose();
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                $"Service dispose failed: {serviceName}.",
                exception,
                $"service-dispose:{serviceName}:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }
}
