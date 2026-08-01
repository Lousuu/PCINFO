using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Interop;
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
    private Stopwatch? firstFrameGateClock;
    private NativeThemeDiagnostic nativeThemeDiagnostic;

    internal FirstFrameGatePhase FirstFrameGateState =>
        (FirstFrameGatePhase)Volatile.Read(ref firstFrameGateState);
    internal bool IsFirstFrameGateArmed =>
        FirstFrameGateState is >= FirstFrameGatePhase.NativePrepared
            and <= FirstFrameGatePhase.FinalPositionCompositionFlushed;
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
                FirstFrameGatePhase.ShownHiddenOffscreen))
        {
            _ = MainShell.TryReportStartupSurfaceReady(
                "MainWindow.ContentRendered / fail-open surface");
            return;
        }

        long generation = Volatile.Read(ref firstFrameGateGeneration);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => CommitFirstOffscreenRenderedFrame(generation));
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
        firstFrameGateClock = Stopwatch.StartNew();
        LogFirstFrameDiagnostic("FirstFrameGateArmed", generation, "Armed");
        if (themeService.CurrentTheme != AppTheme.Tracework
            || motionService.EffectiveLevel == MotionLevel.Off)
        {
            CompleteFirstFrameRelease(
                generation,
                FirstFrameGatePhase.FailOpenReleased,
                restorePlacement: false,
                "MotionOffOrClassic");
            return;
        }

        try
        {
            Background = new SolidColorBrush(FirstFrameColor);
            Opacity = 0d;
            nint handle = new WindowInteropHelper(this).EnsureHandle();
            firstFramePlacement = CaptureFirstFramePlacement(handle);
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;
            if (!WindowPlacementInterop.TryStageWindowOffscreen(
                    handle,
                    firstFramePlacement.Physical.Bounds))
            {
                throw new InvalidOperationException(
                    "Unable to stage the first frame outside the virtual desktop.");
            }
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
                restorePlacement: true,
                "NativePreparationFailed");
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

    private void CommitFirstOffscreenRenderedFrame(long generation)
    {
        Interlocked.Increment(ref firstFrameRenderCallbackCount);
        if (!ValidateFirstFrameGeneration(
                generation,
                FirstFrameGatePhase.ShownHiddenOffscreen))
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
                FirstFrameGatePhase.ShownHiddenOffscreen)
            || !IsFirstFrameSurfaceReady(source))
        {
            return;
        }

        _ = MainShell.TryReportStartupSurfaceReady(
            "SurfaceMeasured / offscreen first render committed");
        LogFirstFrameDiagnostic("SurfaceMeasured", generation, "OffscreenLayoutReady");
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.ShownHiddenOffscreen,
                FirstFrameGatePhase.FirstOffscreenRenderCommitted))
        {
            return;
        }

        LogFirstFrameDiagnostic("OffscreenRenderCommitted", generation, "Render1");
        int flushResult = TryFlushNativeComposition();
        LogFirstFrameDiagnostic(
            "OffscreenDwmFlushResult",
            generation,
            $"HRESULT={flushResult}");
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.FirstOffscreenRenderCommitted,
                FirstFrameGatePhase.OffscreenCompositionFlushed))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => ApplyFinalFirstFramePlacement(generation));
    }

    private void ApplyFinalFirstFramePlacement(long generation)
    {
        Interlocked.Increment(ref firstFrameRenderCallbackCount);
        if (!ValidateFirstFrameGeneration(
                generation,
                FirstFrameGatePhase.OffscreenCompositionFlushed))
        {
            return;
        }

        RestoreFirstFramePlacement();
        nint handle = new WindowInteropHelper(this).Handle;
        if (!IsFinalPlacementApplied(handle))
        {
            FailOpenFirstFrame(generation);
            return;
        }
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.OffscreenCompositionFlushed,
                FirstFrameGatePhase.FinalPlacementAppliedHidden))
        {
            return;
        }

        LogFirstFrameDiagnostic("FinalPlacementApplied", generation, "Render2");
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () => CommitFinalPositionRenderedFrame(generation));
    }

    private void CommitFinalPositionRenderedFrame(long generation)
    {
        Interlocked.Increment(ref firstFrameRenderCallbackCount);
        if (!ValidateFirstFrameGeneration(
                generation,
                FirstFrameGatePhase.FinalPlacementAppliedHidden)
            || Opacity != 0d
            || startupSequenceService.CurrentSnapshot.Phase != StartupSequencePhase.Dormant
            || !IsFirstFrameSurfaceReady(
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)))
        {
            return;
        }

        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.FinalPlacementAppliedHidden,
                FirstFrameGatePhase.FinalPositionRenderCommitted))
        {
            return;
        }

        LogFirstFrameDiagnostic("FinalPositionRenderCommitted", generation, "Render3");
        int flushResult = TryFlushNativeComposition();
        LogFirstFrameDiagnostic(
            "FinalPositionDwmFlushResult",
            generation,
            $"HRESULT={flushResult}");
        if (!TryTransitionFirstFrame(
                FirstFrameGatePhase.FinalPositionRenderCommitted,
                FirstFrameGatePhase.FinalPositionCompositionFlushed))
        {
            return;
        }

        CompleteFirstFrameRelease(
            generation,
            FirstFrameGatePhase.Released,
            restorePlacement: false,
            "CompositorReady");
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
        _ = TryFlushNativeComposition();
        CompleteFirstFrameRelease(
            generation,
            FirstFrameGatePhase.FailOpenReleased,
            restorePlacement: false,
            "FailOpenTimeout");
    }

    private void CompleteFirstFrameRelease(
        long generation,
        FirstFrameGatePhase releasedPhase,
        bool restorePlacement,
        string releaseReason)
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
        _ = startupSequenceService.ReportFirstFrameGateReleased(releaseReason);
        LogFirstFrameDiagnostic("FirstFrameGateReleased", generation, releaseReason);
        Interlocked.Increment(ref firstFrameGateGeneration);
        firstFramePlacement = default;
        firstFrameGateClock = null;
    }

    private void InvalidateFirstFrameGate()
    {
        Interlocked.Increment(ref firstFrameGateGeneration);
        FirstFrameGatePhase state = FirstFrameGateState;
        if (state is >= FirstFrameGatePhase.NativePrepared
            and <= FirstFrameGatePhase.FinalPositionCompositionFlushed)
        {
            Interlocked.Exchange(
                ref firstFrameGateState,
                (int)FirstFrameGatePhase.Cancelled);
        }
        if (state is not (>= FirstFrameGatePhase.NativePrepared
            and <= FirstFrameGatePhase.FinalPositionCompositionFlushed))
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

    private FirstFramePlacement CaptureFirstFramePlacement(nint handle)
    {
        double expectedWidth = ResolveExpectedDimension(Width, ActualWidth, MinWidth, 640d);
        double expectedHeight = ResolveExpectedDimension(Height, ActualHeight, MinHeight, 480d);
        WindowStartupLocation startupLocation = WindowStartupLocation;
        FirstFramePhysicalPlacement physical;
        bool captured = startupLocation switch
        {
            WindowStartupLocation.CenterOwner when Owner is not null =>
                WindowPlacementInterop.TryCaptureOwnerCenteredPlacement(
                    new WindowInteropHelper(Owner).Handle,
                    expectedWidth,
                    expectedHeight,
                    out physical),
            WindowStartupLocation.CenterScreen =>
                WindowPlacementInterop.TryCaptureCursorCenteredPlacement(
                    expectedWidth,
                    expectedHeight,
                    out physical),
            _ => WindowPlacementInterop.TryCaptureCurrentPlacement(handle, out physical)
        };
        if (!captured || !physical.IsCaptured)
        {
            throw new InvalidOperationException(
                "Unable to capture a physical first-frame placement.");
        }

        return new(
            startupLocation,
            ShowActivated,
            physical);
    }

    private void RestoreFirstFramePlacement()
    {
        if (!firstFramePlacement.IsCaptured)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        nint handle = new WindowInteropHelper(this).Handle;
        _ = WindowPlacementInterop.TryApplyWindowBounds(
            handle,
            firstFramePlacement.Physical.Bounds);
        ShowActivated = firstFramePlacement.ShowActivated;
        WindowStartupLocation = firstFramePlacement.StartupLocation;
    }

    private bool IsFinalPlacementApplied(nint handle)
    {
        if (!firstFramePlacement.IsCaptured
            || !WindowPlacementInterop.TryCaptureCurrentPlacement(
                handle,
                out FirstFramePhysicalPlacement current))
        {
            return false;
        }

        PhysicalPixelBounds expected = firstFramePlacement.Physical.Bounds;
        PhysicalPixelBounds actual = current.Bounds;
        return Math.Abs(expected.Left - actual.Left) <= 1
            && Math.Abs(expected.Top - actual.Top) <= 1
            && Math.Abs(expected.Width - actual.Width) <= 1
            && Math.Abs(expected.Height - actual.Height) <= 1;
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

    private static int TryFlushNativeComposition()
    {
        try
        {
            return DwmFlush();
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
        return int.MinValue;
    }

    private void LogFirstFrameDiagnostic(
        string eventName,
        long generation,
        string reason)
    {
        FirstFramePhysicalPlacement physical = firstFramePlacement.Physical;
        AppLogger.LogKeyEvent(
            $"{eventName} | relative={firstFrameGateClock?.Elapsed.TotalMilliseconds ?? 0d:0} ms"
            + $"; generation={generation}; gate={FirstFrameGateState}; opacity={Opacity:0.##}"
            + $"; visible={IsVisible}; bounds={physical.Bounds.Left},{physical.Bounds.Top},"
            + $"{physical.Bounds.Width},{physical.Bounds.Height}; dpi={physical.DpiX}x{physical.DpiY}"
            + $"; startup={startupSequenceService.CurrentSnapshot.Phase}"
            + $"; motion={motionService.EffectiveLevel}; reason={reason}");
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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmFlush();

    internal enum FirstFrameGatePhase
    {
        Dormant,
        NativePrepared,
        ShownHiddenOffscreen,
        FirstOffscreenRenderCommitted,
        OffscreenCompositionFlushed,
        FinalPlacementAppliedHidden,
        FinalPositionRenderCommitted,
        FinalPositionCompositionFlushed,
        Released,
        FailOpenReleased,
        Cancelled
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
        FirstFramePhysicalPlacement Physical)
    {
        public bool IsCaptured => Physical.IsCaptured;
    }
}
