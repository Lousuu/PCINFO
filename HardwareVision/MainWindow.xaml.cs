using System.Windows;
using System.ComponentModel;
using System.Windows.Interop;
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
    private readonly IStartupSequenceService startupSequenceService;
    private readonly HardwareChangeMonitor? hardwareChangeMonitor;
    private HwndSource? windowSource;
    private bool isExitRequested;
    private bool startupVisualReadyReported;

    public MainWindow(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IThemeService themeService,
        IMotionService motionService,
        IThemeTransitionService themeTransitionService,
        INavigationTransitionService navigationTransitionService,
        IStartupSequenceService startupSequenceService,
        IStartupService startupService,
        SensorDiagnosticService sensorDiagnosticService,
        IForegroundProcessTracker foregroundProcessTracker,
        ISensorHistoryService sensorHistoryService,
        IGameSessionRecorder gameSessionRecorder,
        IGameEnergyTracker? gameEnergyTracker = null,
        IGamePerformanceLimitTracker? gamePerformanceLimitTracker = null,
        IHardwareRefreshService? hardwareRefreshService = null)
    {
        this.settings = settings;
        this.pollingService = pollingService;
        this.startupSequenceService = startupSequenceService;

        AppLogger.LogKeyEvent("MainWindow InitializeComponent starting.");
        InitializeComponent();
        AppLogger.LogKeyEvent("MainWindow InitializeComponent completed.");
        AppLogger.LogKeyEvent("MainViewModel construction starting.");
        MainViewModel viewModel = new(
            settings,
            hardwareInfoService,
            pollingService,
            settingsService,
            themeService,
            motionService,
            themeTransitionService,
            navigationTransitionService,
            startupService,
            Dispatcher,
            sensorDiagnosticService,
            foregroundProcessTracker,
            sensorHistoryService,
            gameSessionRecorder,
            gameEnergyTracker,
            gamePerformanceLimitTracker,
            hardwareRefreshService,
            startupSequenceService);
        DataContext = viewModel;
        startupSequenceService.ReportMilestone(
            StartupMilestoneId.PageRouter,
            viewModel.CurrentPage is not null
                ? StartupMilestoneState.Ready
                : StartupMilestoneState.Failed,
            viewModel.CurrentPage is not null
                ? "MainViewModel and initial CurrentPage established"
                : "Initial CurrentPage was not established");
        if (hardwareRefreshService is not null)
        {
            hardwareChangeMonitor = new HardwareChangeMonitor(
                hardwareRefreshService,
                () => settings.AutoRefreshHardwareOnDeviceChange);
            SourceInitialized += OnSourceInitialized;
        }
        AppLogger.LogKeyEvent("MainViewModel construction completed.");
        ContentRendered += OnContentRendered;
        IsVisibleChanged += (_, _) =>
        {
            pollingService.SetBackgroundMode(!IsVisible);
            (DataContext as MainViewModel)?.SetWindowVisible(IsVisible);
            if (!IsVisible)
            {
                startupSequenceService.CompleteForHiddenWindow();
            }
        };
        StateChanged += (_, _) =>
        {
            bool minimized = WindowState == WindowState.Minimized;
            (DataContext as MainViewModel)?.SetWindowMinimized(minimized);
            if (minimized)
            {
                startupSequenceService.CompleteForHiddenWindow();
            }
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            startupSequenceService.Cancel();
            RemoveWindowHook();
            hardwareChangeMonitor?.Dispose();
            (DataContext as IDisposable)?.Dispose();
        };
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (startupVisualReadyReported)
        {
            return;
        }

        startupVisualReadyReported = true;
        ContentRendered -= OnContentRendered;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            if (!IsVisible || MainShell.ActualWidth <= 0d || MainShell.ActualHeight <= 0d)
            {
                startupSequenceService.CompleteForHiddenWindow();
                return;
            }

            MainShell.UpdateLayout();
            if (!MainShell.ReportStartupVisualSurfaceReady())
            {
                startupSequenceService.CompleteForHiddenWindow();
                return;
            }
            _ = startupSequenceService.StartAsync();
        });
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

    public Task RefreshHardwareInfoAsync(HardwareRefreshReason reason = HardwareRefreshReason.ManualSettings)
    {
        return DataContext is MainViewModel viewModel
            ? viewModel.RefreshHardwareInfoAsync(reason)
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
            startupSequenceService.Cancel();
            (DataContext as MainViewModel)?.SetWindowClosing();
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        windowSource?.AddHook(WindowMessageHook);
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        _ = hwnd;
        _ = lParam;
        if (message == HardwareChangeMonitor.WmDeviceChange)
        {
            hardwareChangeMonitor?.NotifyDeviceChange(unchecked((int)wParam.ToInt64()));
        }
        return nint.Zero;
    }

    private void RemoveWindowHook()
    {
        if (windowSource is not null)
        {
            windowSource.RemoveHook(WindowMessageHook);
            windowSource = null;
        }
    }

}
