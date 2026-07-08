using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision;

public partial class App : System.Windows.Application
{
    private bool servicesDisposed;

    public ISettingsService SettingsService { get; private set; } = null!;

    public IStartupService StartupService { get; private set; } = null!;

    public IHardwareInfoService HardwareInfoService { get; private set; } = null!;

    public ISensorService SensorService { get; private set; } = null!;

    public SensorDiagnosticService SensorDiagnosticService { get; private set; } = null!;

    public PollingService PollingService { get; private set; } = null!;

    public ITrayService TrayService { get; private set; } = null!;

    public AppSettings Settings { get; private set; } = new();

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.LogError(
                "Unhandled dispatcher exception.",
                args.Exception,
                $"dispatcher-unhandled:{args.Exception.GetType().FullName}",
                TimeSpan.Zero);
            args.Handled = true;
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
        try
        {
            base.OnStartup(e);
            AppLogger.LogKeyEvent("HardwareVision starting.");

            if (e.Args.Any(arg => string.Equals(arg, "--export-official-comparison", StringComparison.OrdinalIgnoreCase)))
            {
                SensorDiagnosticService diagnostics = new();
                await diagnostics.ExportOfficialComparisonAsync();
                Shutdown();
                return;
            }

            SettingsService = new SettingsService();
            StartupService = new StartupTaskService();
            HardwareInfoService = new HardwareInfoService();
            SensorService = new SensorAggregatorService(
            [
                new LibreHardwareMonitorProvider(),
                new WmiCpuClockSensorProvider()
            ]);
            SensorDiagnosticService = new SensorDiagnosticService();
            AppLogger.LogKeyEvent("Startup services created.");

            Settings = await SettingsService.LoadAsync();
            if (Math.Abs(Settings.RefreshIntervalSeconds - 0.5d) > double.Epsilon)
            {
                Settings.RefreshIntervalSeconds = 0.5d;
                await SettingsService.SaveAsync(Settings);
            }

            AppLogger.LogKeyEvent("Settings loaded.");

            bool startupEnabled = StartupService.IsEnabled();
            AppLogger.LogKeyEvent("Startup state checked.");
            if (Settings.AutoStartEnabled != startupEnabled)
            {
                Settings.AutoStartEnabled = startupEnabled;
                await SettingsService.SaveAsync(Settings);
            }

            PollingService = new PollingService(SensorService, Settings);

            MainWindow mainWindow = new(
                Settings,
                HardwareInfoService,
                PollingService,
                SettingsService,
                StartupService,
                SensorDiagnosticService);
            AppLogger.LogKeyEvent("Main window constructed.");

            MainWindow = mainWindow;
            TrayService = new TrayService();
            TrayService.Initialize(mainWindow);
            TrayService.OpenRequested += (_, _) => mainWindow.ShowFromTray();
            TrayService.RefreshHardwareInfoRequested += (_, _) => ObserveTask(mainWindow.RefreshHardwareInfoAsync(), "tray-refresh-hardware-info");
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

            mainWindow.Show();
            ObserveTask(PollingService.StartAsync(), "polling-start");
            AppLogger.LogKeyEvent("Main window shown.");
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

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        AppLogger.LogKeyEvent("HardwareVision exiting.");
        if (!servicesDisposed)
        {
            ShutdownServicesAsync().GetAwaiter().GetResult();
        }
        base.OnExit(e);
    }

    public async Task RestartAsAdministratorAsync()
    {
        string? executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            AppLogger.LogError(
                "Cannot restart as administrator because the executable path is unavailable.",
                null,
                "restart-admin-missing-exe",
                TimeSpan.FromMinutes(5));
            return;
        }

        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.RequestExit();
        }

        await ShutdownServicesAsync();

        ProcessStartInfo startInfo = new(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        Process.Start(startInfo);
        Shutdown();
    }

    private async Task ShutdownServicesAsync()
    {
        if (servicesDisposed)
        {
            return;
        }

        servicesDisposed = true;

        await DisposeServiceAsync(PollingService, "polling-service");
        await DisposeServiceAsync(SensorService, "sensor-service");

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
