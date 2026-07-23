using System.Windows;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;
using HardwareVision.ViewModels;
using Color = System.Windows.Media.Color;

namespace HardwareVision;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private static readonly Color FirstFrameColor = Color.FromRgb(0x0B, 0x0E, 0x11);
    private static readonly TimeSpan FirstFrameFailOpenDelay = TimeSpan.FromMilliseconds(500);
    private readonly AppSettings settings;
    private readonly PollingService pollingService;
    private readonly IStartupSequenceService startupSequenceService;
    private readonly HardwareChangeMonitor? hardwareChangeMonitor;
    private HwndSource? windowSource;
    private bool isExitRequested;
    private bool startupContentRenderedHandled;
    private int firstFrameGateState;
    private int firstFrameReleaseCount;
    private long firstFrameGateGeneration;

    internal bool IsFirstFrameGateArmed => Volatile.Read(ref firstFrameGateState) == 1;
    internal bool IsFirstFrameGateReleased => Volatile.Read(ref firstFrameGateState) >= 2;
    internal int FirstFrameReleaseCount => Volatile.Read(ref firstFrameReleaseCount);
    internal static TimeSpan FirstFrameFailOpenTimeout => FirstFrameFailOpenDelay;
    internal static bool TryArmFirstFrameGate(ref int state) =>
        Interlocked.CompareExchange(ref state, 1, 0) == 0;
    internal static bool TryReleaseFirstFrameGateState(ref int state) =>
        Interlocked.CompareExchange(ref state, 2, 1) == 1;

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
            InvalidateFirstFrameGate();
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
        if (startupContentRenderedHandled)
        {
            return;
        }

        startupContentRenderedHandled = true;
        ContentRendered -= OnContentRendered;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            try
            {
                MainShell.TryReportStartupSurfaceReady(
                    "MainWindow.ContentRendered / DispatcherPriority.Render");
            }
            finally
            {
                TryReleaseFirstFrameGate();
            }
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

    public void PrepareFirstFrame()
    {
        MainShell.PrepareFirstFrame();
        if (!TryArmFirstFrameGate(ref firstFrameGateState))
        {
            return;
        }

        long generation = Interlocked.Increment(ref firstFrameGateGeneration);
        try
        {
            Background = new SolidColorBrush(FirstFrameColor);
            Opacity = 0d;
            nint handle = new WindowInteropHelper(this).EnsureHandle();
            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = FirstFrameColor;
            }
            TryEnableNativeDarkTitleBar(handle);
        }
        catch
        {
            TryReleaseFirstFrameGate();
            return;
        }

        _ = ReleaseFirstFrameGateAfterTimeoutAsync(generation);
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
        InvalidateFirstFrameGate();
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

    private async Task ReleaseFirstFrameGateAfterTimeoutAsync(long generation)
    {
        await Task.Delay(FirstFrameFailOpenDelay).ConfigureAwait(false);
        try
        {
            await Dispatcher.InvokeAsync(
                () =>
                {
                    if (generation == Volatile.Read(ref firstFrameGateGeneration))
                    {
                        TryReleaseFirstFrameGate();
                    }
                },
                DispatcherPriority.Render);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private bool TryReleaseFirstFrameGate()
    {
        if (!TryReleaseFirstFrameGateState(ref firstFrameGateState))
        {
            return false;
        }

        Interlocked.Increment(ref firstFrameReleaseCount);
        Opacity = 1d;
        return true;
    }

    private void InvalidateFirstFrameGate()
    {
        Interlocked.Increment(ref firstFrameGateGeneration);
        int prior = Interlocked.Exchange(ref firstFrameGateState, 3);
        if (prior == 1)
        {
            Opacity = 1d;
        }
    }

    private static void TryEnableNativeDarkTitleBar(nint handle)
    {
        if (handle == nint.Zero)
        {
            return;
        }

        int enabled = 1;
        try
        {
            if (DwmSetWindowAttribute(
                    handle,
                    DwmUseImmersiveDarkMode,
                    ref enabled,
                    sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(
                    handle,
                    DwmUseImmersiveDarkModeLegacy,
                    ref enabled,
                    sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (ExternalException)
        {
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

}
