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
using Point = System.Windows.Point;

namespace HardwareVision;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;
    private const int DwmDefaultColor = unchecked((int)0xFFFFFFFF);
    private const int TraceworkCaptionColorRef = 0x00110E0B;
    private const int TraceworkBorderColorRef = 0x002D2620;
    private const int TraceworkTextColorRef = 0x00F7F3EE;
    private static readonly Color FirstFrameColor = Color.FromRgb(0x0B, 0x0E, 0x11);
    private static readonly TimeSpan FirstFrameFailOpenDelay = TimeSpan.FromMilliseconds(500);
    private readonly AppSettings settings;
    private readonly PollingService pollingService;
    private readonly IStartupSequenceService startupSequenceService;
    private readonly IThemeService themeService;
    private readonly IMotionService motionService;
    private readonly HardwareChangeMonitor? hardwareChangeMonitor;
    private HwndSource? windowSource;
    private bool isExitRequested;
    private bool isWindowClosing;
    private bool startupContentRenderedHandled;
    private int firstFrameReleaseCount;
    private int firstFrameRenderCallbackCount;
    private int firstFrameGateState;
    private long firstFrameGateGeneration;
    private nint nativeThemeHandle;
    private long nativeThemeGeneration = -1;
    private AppTheme? nativeTheme;
    private FirstFramePlacement firstFramePlacement;
    private NativeThemeDiagnostic nativeThemeDiagnostic;

    internal FirstFrameGatePhase FirstFrameGateState =>
        (FirstFrameGatePhase)Volatile.Read(ref firstFrameGateState);
    internal bool IsFirstFrameGateArmed =>
        FirstFrameGateState is >= FirstFrameGatePhase.NativePrepared
            and <= FirstFrameGatePhase.FinalPlacementCommitted;
    internal bool IsFirstFrameGateReleased =>
        FirstFrameGateState is FirstFrameGatePhase.Released
            or FirstFrameGatePhase.FailOpenReleased;
    internal int FirstFrameReleaseCount => Volatile.Read(ref firstFrameReleaseCount);
    internal int FirstFrameRenderCallbackCount =>
        Volatile.Read(ref firstFrameRenderCallbackCount);
    internal NativeThemeDiagnostic LastNativeThemeDiagnostic => nativeThemeDiagnostic;
    internal static TimeSpan FirstFrameFailOpenTimeout => FirstFrameFailOpenDelay;
    internal static bool TryArmFirstFrameGate(ref int state) =>
        Interlocked.CompareExchange(
            ref state,
            (int)FirstFrameGatePhase.NativePrepared,
            (int)FirstFrameGatePhase.Dormant) == (int)FirstFrameGatePhase.Dormant;
    internal static bool TryReleaseFirstFrameGateState(ref int state) =>
        Interlocked.CompareExchange(
            ref state,
            (int)FirstFrameGatePhase.Released,
            (int)FirstFrameGatePhase.NativePrepared)
        == (int)FirstFrameGatePhase.NativePrepared;
    internal static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);
    internal static Point ResolveOffscreenStagingPoint(
        double virtualLeft,
        double virtualTop,
        double expectedWidth,
        double expectedHeight,
        double minimumWidth,
        double minimumHeight) =>
        new(
            virtualLeft - Math.Max(Math.Max(expectedWidth, minimumWidth), 640d) - 128d,
            virtualTop - Math.Max(Math.Max(expectedHeight, minimumHeight), 480d) - 128d);

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
        this.themeService = themeService;
        this.motionService = motionService;

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
        }
        SourceInitialized += OnSourceInitialized;
        themeService.ThemeChanged += OnThemeChanged;
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
            themeService.ThemeChanged -= OnThemeChanged;
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
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.NativePrepared,
                FirstFrameGatePhase.ShownHidden))
        {
            _ = MainShell.TryReportStartupSurfaceReady(
                "MainWindow.ContentRendered / fail-open surface");
            return;
        }

        long generation = Volatile.Read(ref firstFrameGateGeneration);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => CommitFirstRenderedFrame(generation));
    }

    public void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        if (FirstFrameGateState == FirstFrameGatePhase.Cancelled)
        {
            RestoreFirstFramePlacement();
            Opacity = 1d;
            firstFramePlacement = default;
        }
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
        if (themeService.CurrentTheme != AppTheme.Tracework
            || motionService.EffectiveLevel == MotionLevel.Off)
        {
            CompleteFirstFrameRelease(
                generation,
                FirstFrameGatePhase.FailOpenReleased,
                restorePlacement: false);
            return;
        }

        try
        {
            Background = new SolidColorBrush(FirstFrameColor);
            Opacity = 0d;
            firstFramePlacement = CaptureFirstFramePlacement();
            Point staging = ResolveOffscreenStagingPoint(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                firstFramePlacement.ExpectedWidth,
                firstFramePlacement.ExpectedHeight,
                MinWidth,
                MinHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = staging.X;
            Top = staging.Y;
            ShowActivated = false;
            nint handle = new WindowInteropHelper(this).EnsureHandle();
            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source?.CompositionTarget is not null)
            {
                source.CompositionTarget.BackgroundColor = FirstFrameColor;
            }
            ApplyNativeWindowTheme(handle, generation, AppTheme.Tracework);
        }
        catch
        {
            CompleteFirstFrameRelease(
                generation,
                FirstFrameGatePhase.FailOpenReleased,
                restorePlacement: true);
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
        isWindowClosing = true;
        InvalidateFirstFrameGate();
        if (isExitRequested || !settings.CloseToTray)
        {
            startupSequenceService.Cancel();
            (DataContext as MainViewModel)?.SetWindowClosing();
            return;
        }

        e.Cancel = true;
        HideToTray();
        isWindowClosing = false;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(handle);
        if (hardwareChangeMonitor is not null)
        {
            windowSource?.AddHook(WindowMessageHook);
        }
        ApplyNativeWindowTheme(
            handle,
            Volatile.Read(ref firstFrameGateGeneration),
            themeService.CurrentTheme);
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
                        FailOpenFirstFrame(generation);
                    }
                },
                DispatcherPriority.Send);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void CommitFirstRenderedFrame(long generation)
    {
        Interlocked.Increment(ref firstFrameRenderCallbackCount);
        if (!ValidateFirstFrameGeneration(
                generation,
                FirstFrameGatePhase.ShownHidden))
        {
            return;
        }

        nint handle = new WindowInteropHelper(this).Handle;
        HwndSource? source = HwndSource.FromHwnd(handle);
        if (!IsFirstFrameSurfaceReady(source))
        {
            return;
        }

        UpdateLayout();
        if (!ValidateFirstFrameGeneration(
                generation,
                FirstFrameGatePhase.ShownHidden)
            || !IsFirstFrameSurfaceReady(source))
        {
            return;
        }

        _ = MainShell.TryReportStartupSurfaceReady(
            "MainWindow.ContentRendered / DispatcherPriority.Render / first commit");
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.ShownHidden,
                FirstFrameGatePhase.FirstRenderCommitted))
        {
            return;
        }

        TryFlushNativeComposition();
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.FirstRenderCommitted,
                FirstFrameGatePhase.NativeCompositionFlushed))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => CommitFinalFirstFramePlacement(generation));
    }

    private void CommitFinalFirstFramePlacement(long generation)
    {
        Interlocked.Increment(ref firstFrameRenderCallbackCount);
        if (!ValidateFirstFrameGeneration(
                generation,
                FirstFrameGatePhase.NativeCompositionFlushed))
        {
            return;
        }

        RestoreFirstFramePlacement();
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.NativeCompositionFlushed,
                FirstFrameGatePhase.FinalPlacementCommitted))
        {
            return;
        }

        TryFlushNativeComposition();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        CompleteFirstFrameRelease(
            generation,
            FirstFrameGatePhase.Released,
            restorePlacement: false);
    }

    private bool IsFirstFrameSurfaceReady(HwndSource? source) =>
        source?.CompositionTarget is not null
        && source.CompositionTarget.BackgroundColor == FirstFrameColor
        && IsLoaded
        && MainShell.IsLoaded
        && MainShell.ActualWidth > 0d
        && MainShell.ActualHeight > 0d
        && MainShell.StartupSequenceOverlay.IsLoaded
        && Background is SolidColorBrush brush
        && brush.Color == FirstFrameColor;

    private bool ValidateFirstFrameGeneration(
        long generation,
        FirstFrameGatePhase expectedPhase) =>
        !isWindowClosing
        && generation == Volatile.Read(ref firstFrameGateGeneration)
        && FirstFrameGateState == expectedPhase;

    private void FailOpenFirstFrame(long generation)
    {
        if (isWindowClosing
            || generation != Volatile.Read(ref firstFrameGateGeneration)
            || !IsFirstFrameGateArmed)
        {
            return;
        }

        RestoreFirstFramePlacement();
        nint handle = new WindowInteropHelper(this).Handle;
        HwndSource? source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = FirstFrameColor;
        }
        ApplyNativeWindowTheme(handle, generation, themeService.CurrentTheme);
        TryFlushNativeComposition();
        CompleteFirstFrameRelease(
            generation,
            FirstFrameGatePhase.FailOpenReleased,
            restorePlacement: false);
    }

    private void CompleteFirstFrameRelease(
        long generation,
        FirstFrameGatePhase releasedPhase,
        bool restorePlacement)
    {
        if (isWindowClosing
            || generation != Volatile.Read(ref firstFrameGateGeneration)
            || FirstFrameGateState is FirstFrameGatePhase.Cancelled
                or FirstFrameGatePhase.Released
                or FirstFrameGatePhase.FailOpenReleased)
        {
            return;
        }

        if (restorePlacement)
        {
            RestoreFirstFramePlacement();
        }
        Interlocked.Exchange(ref firstFrameGateState, (int)releasedPhase);
        Opacity = 1d;
        if (firstFramePlacement.ShowActivated && IsVisible)
        {
            Activate();
        }
        Interlocked.Increment(ref firstFrameReleaseCount);
        Interlocked.Increment(ref firstFrameGateGeneration);
        firstFramePlacement = default;
    }

    private void InvalidateFirstFrameGate()
    {
        Interlocked.Increment(ref firstFrameGateGeneration);
        FirstFrameGatePhase state = FirstFrameGateState;
        if (state is >= FirstFrameGatePhase.NativePrepared
            and <= FirstFrameGatePhase.FinalPlacementCommitted)
        {
            Interlocked.Exchange(
                ref firstFrameGateState,
                (int)FirstFrameGatePhase.Cancelled);
        }
        if (state is not (>= FirstFrameGatePhase.NativePrepared
            and <= FirstFrameGatePhase.FinalPlacementCommitted))
        {
            firstFramePlacement = default;
        }
    }

    private bool TryTransitionFirstFrame(
        FirstFrameGatePhase expected,
        FirstFrameGatePhase next) =>
        Interlocked.CompareExchange(
            ref firstFrameGateState,
            (int)next,
            (int)expected) == (int)expected;

    private FirstFramePlacement CaptureFirstFramePlacement()
    {
        double expectedWidth = ResolveExpectedDimension(Width, ActualWidth, MinWidth, 640d);
        double expectedHeight = ResolveExpectedDimension(Height, ActualHeight, MinHeight, 480d);
        Point final = ResolveFinalPlacement(
            WindowStartupLocation,
            Left,
            Top,
            expectedWidth,
            expectedHeight);
        return new(
            WindowStartupLocation,
            ShowActivated,
            final.X,
            final.Y,
            expectedWidth,
            expectedHeight);
    }

    private Point ResolveFinalPlacement(
        WindowStartupLocation startupLocation,
        double configuredLeft,
        double configuredTop,
        double width,
        double height)
    {
        if (startupLocation == WindowStartupLocation.Manual)
        {
            return new(
                double.IsNaN(configuredLeft) ? SystemParameters.WorkArea.Left : configuredLeft,
                double.IsNaN(configuredTop) ? SystemParameters.WorkArea.Top : configuredTop);
        }

        Rect workArea = startupLocation == WindowStartupLocation.CenterOwner
            && Owner is { } owner
            ? new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight)
            : ResolveCursorMonitorWorkArea();
        return new(
            workArea.Left + Math.Max(0d, (workArea.Width - width) / 2d),
            workArea.Top + Math.Max(0d, (workArea.Height - height) / 2d));
    }

    private void RestoreFirstFramePlacement()
    {
        if (!firstFramePlacement.IsCaptured)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = firstFramePlacement.FinalLeft;
        Top = firstFramePlacement.FinalTop;
        ShowActivated = firstFramePlacement.ShowActivated;
        WindowStartupLocation = firstFramePlacement.StartupLocation;
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        _ = sender;
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        nativeTheme = null;
        ApplyNativeWindowTheme(
            handle,
            Volatile.Read(ref firstFrameGateGeneration),
            e.CurrentTheme);
    }

    private void ApplyNativeWindowTheme(
        nint handle,
        long generation,
        AppTheme theme)
    {
        if (handle == nint.Zero
            || nativeThemeHandle == handle
                && nativeThemeGeneration == generation
                && nativeTheme == theme)
        {
            return;
        }

        int immersiveResult = int.MinValue;
        int legacyResult = int.MinValue;
        int borderResult = int.MinValue;
        int captionResult = int.MinValue;
        int textResult = int.MinValue;
        bool usedLegacy = false;
        int enabled = theme == AppTheme.Tracework ? 1 : 0;
        int borderColor = theme == AppTheme.Tracework
            ? TraceworkBorderColorRef
            : DwmDefaultColor;
        int captionColor = theme == AppTheme.Tracework
            ? TraceworkCaptionColorRef
            : DwmDefaultColor;
        int textColor = theme == AppTheme.Tracework
            ? TraceworkTextColorRef
            : DwmDefaultColor;
        try
        {
            immersiveResult = DwmSetWindowAttribute(
                handle,
                DwmUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
            if (immersiveResult != 0)
            {
                usedLegacy = true;
                legacyResult = DwmSetWindowAttribute(
                    handle,
                    DwmUseImmersiveDarkModeLegacy,
                    ref enabled,
                    sizeof(int));
            }
            borderResult = DwmSetWindowAttribute(
                handle,
                DwmBorderColor,
                ref borderColor,
                sizeof(int));
            captionResult = DwmSetWindowAttribute(
                handle,
                DwmCaptionColor,
                ref captionColor,
                sizeof(int));
            textResult = DwmSetWindowAttribute(
                handle,
                DwmTextColor,
                ref textColor,
                sizeof(int));
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

        nativeThemeHandle = handle;
        nativeThemeGeneration = generation;
        nativeTheme = theme;
        nativeThemeDiagnostic = new(
            generation,
            immersiveResult,
            legacyResult,
            borderResult,
            captionResult,
            textResult,
            theme,
            captionColor,
            usedLegacy);
    }

    private static void TryFlushNativeComposition()
    {
        try
        {
            _ = DwmFlush();
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch
        {
        }
    }

    private static double ResolveExpectedDimension(
        double configured,
        double actual,
        double minimum,
        double fallback)
    {
        if (!double.IsNaN(configured) && configured > 0d)
        {
            return configured;
        }
        if (actual > 0d)
        {
            return actual;
        }
        return Math.Max(minimum, fallback);
    }

    private static Rect ResolveCursorMonitorWorkArea()
    {
        try
        {
            if (GetCursorPos(out NativePoint cursor))
            {
                nint monitor = MonitorFromPoint(cursor, 2);
                MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
                if (monitor != nint.Zero && GetMonitorInfo(monitor, ref info))
                {
                    return new Rect(
                        info.Work.Left,
                        info.Work.Top,
                        info.Work.Right - info.Work.Left,
                        info.Work.Bottom - info.Work.Top);
                }
            }
        }
        catch
        {
        }
        return SystemParameters.WorkArea;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    internal enum FirstFrameGatePhase
    {
        Dormant,
        NativePrepared,
        ShownHidden,
        FirstRenderCommitted,
        NativeCompositionFlushed,
        FinalPlacementCommitted,
        Released,
        Cancelled,
        FailOpenReleased
    }

    internal readonly record struct NativeThemeDiagnostic(
        long Generation,
        int ImmersiveDarkResult,
        int LegacyDarkResult,
        int BorderColorResult,
        int CaptionColorResult,
        int TextColorResult,
        AppTheme Theme,
        int ExplicitCaptionColor,
        bool UsedLegacyFallback);

    private readonly record struct FirstFramePlacement(
        WindowStartupLocation StartupLocation,
        bool ShowActivated,
        double FinalLeft,
        double FinalTop,
        double ExpectedWidth,
        double ExpectedHeight)
    {
        public bool IsCaptured => ExpectedWidth > 0d && ExpectedHeight > 0d;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
