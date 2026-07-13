using System.Windows;
using System.ComponentModel;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;
using HardwareVision.ViewModels;

namespace HardwareVision;

public partial class MainWindow : Window
{
    private readonly AppSettings settings;
    private readonly PollingService pollingService;
    private bool isExitRequested;

    public MainWindow()
        : this(
            new AppSettings(),
            new HardwareInfoService(),
            new PollingService(new EmptySensorService(), new AppSettings()),
            new SettingsService(),
            new StartupTaskService(),
            new SensorDiagnosticService(),
            EmptyForegroundProcessTracker.Instance,
            new SensorHistoryService(),
            new CsvGameSessionRecorder())
    {
    }

    public MainWindow(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IStartupService startupService,
        SensorDiagnosticService sensorDiagnosticService,
        IForegroundProcessTracker foregroundProcessTracker,
        ISensorHistoryService sensorHistoryService,
        IGameSessionRecorder gameSessionRecorder)
    {
        this.settings = settings;
        this.pollingService = pollingService;

        AppLogger.LogKeyEvent("MainWindow InitializeComponent starting.");
        InitializeComponent();
        AppLogger.LogKeyEvent("MainWindow InitializeComponent completed.");
        AppLogger.LogKeyEvent("MainViewModel construction starting.");
        DataContext = new MainViewModel(
            settings,
            hardwareInfoService,
            pollingService,
            settingsService,
            startupService,
            Dispatcher,
            sensorDiagnosticService,
            foregroundProcessTracker,
            sensorHistoryService,
            gameSessionRecorder);
        AppLogger.LogKeyEvent("MainViewModel construction completed.");
        IsVisibleChanged += (_, _) =>
        {
            pollingService.SetBackgroundMode(!IsVisible);
            (DataContext as MainViewModel)?.SetWindowVisible(IsVisible);
        };
        Closing += OnClosing;
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    public void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        pollingService.SetBackgroundMode(false);
    }

    public void HideToTray()
    {
        Hide();
        pollingService.SetBackgroundMode(true);
    }

    public Task RefreshHardwareInfoAsync()
    {
        return DataContext is MainViewModel viewModel
            ? viewModel.RefreshHardwareInfoAsync()
            : Task.CompletedTask;
    }

    public void ShowSettingsPage()
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ShowSettingsPage();
        }
    }

    public void ApplyStartupState(bool enabled)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ApplyStartupState(enabled);
        }
    }

    public void RequestExit()
    {
        isExitRequested = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (isExitRequested || !settings.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private sealed class EmptySensorService : ISensorService
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SensorReading>>(Array.Empty<SensorReading>());
        }

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken = default)
        {
            return GetCurrentReadingsAsync(cancellationToken);
        }

        public void Dispose()
        {
        }
    }
}
