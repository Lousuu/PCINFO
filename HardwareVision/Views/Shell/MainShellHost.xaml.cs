using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using HardwareVision.Models;
using HardwareVision.ViewModels;
using HardwareVision.Controls;
using HardwareVision.Themes;
using HardwareVision.Utilities;
using MediaBrush = System.Windows.Media.Brush;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfSize = System.Windows.Size;

namespace HardwareVision.Views.Shell;

public partial class MainShellHost : System.Windows.Controls.UserControl
{
    private MainViewModel? viewModel;
    private long settledVersion = -1;
    private long preparedNavigationVersion = -1;
    private long exitedNavigationVersion = -1;
    private long committedNavigationVersion = -1;
    private StartupShellRevealCoordinator? startupRevealCoordinator;
    private bool startupSurfaceReadyReported;
    private long postDataLayoutVersion = -1;
    private bool startupFirstFramePrepared;
    private bool startupSequenceOwnsMotion;
    private long activeStartupRevealVersion = -1;
    private bool startupOverlayRevealCompleted;
    private bool startupShellRevealCompleted;
    private long themeVisualValidationGeneration;

    public MainShellHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        AttachViewModel();
        PrepareFirstFrame();
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            SystemRewireOverlay.EnsureTemplateReady();
            TryReportStartupSurfaceReady("MainShellHost.Loaded / DispatcherPriority.Loaded");
        });
        startupRevealCoordinator ??= new StartupShellRevealCoordinator(
            TraceworkChrome.StartupSignalRailTarget,
            TraceworkChrome.StartupTelemetryTarget,
            PageHost,
            TraceworkChrome.StartupTimeRibbonTarget);
        startupRevealCoordinator.VisualCompleted -= OnStartupShellVisualCompleted;
        startupRevealCoordinator.VisualCompleted += OnStartupShellVisualCompleted;
        StartupSequenceOverlay.RevealVisualExitCompleted -= OnStartupOverlayVisualCompleted;
        StartupSequenceOverlay.RevealVisualExitCompleted += OnStartupOverlayVisualCompleted;
        LayoutUpdated += OnLayoutUpdated;
        ApplyStartupSequence(viewModel?.StartupSequence);
    }

    public void PrepareFirstFrame()
    {
        if (startupFirstFramePrepared
            || DataContext is not MainViewModel currentViewModel)
        {
            return;
        }

        if (currentViewModel.StartupSequence.HasCompleted)
        {
            StartupSequenceOverlay.RestoreFinalState();
            startupFirstFramePrepared = true;
            return;
        }

        StartupSequenceOverlay.PrepareFirstFrame(
            currentViewModel.CurrentTheme,
            currentViewModel.EffectiveMotionLevel);
        startupFirstFramePrepared = true;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PageHost.CancelAllMotionForUnload();
        RelayBandOverlay.CancelTransition();
        TraceworkChrome.CancelFlowRelayVisuals();
        LayoutUpdated -= OnLayoutUpdated;
        StartupSequenceOverlay.RevealVisualExitCompleted -= OnStartupOverlayVisualCompleted;
        if (startupRevealCoordinator is not null)
        {
            startupRevealCoordinator.VisualCompleted -= OnStartupShellVisualCompleted;
        }
        StartupSequenceOverlay.RestoreFinalState();
        startupRevealCoordinator?.RestoreFinalState();
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.NotifyShellUnloaded();
            viewModel = null;
        }
    }

    public bool TryReportStartupSurfaceReady(string detail)
    {
        if (startupSurfaceReadyReported)
        {
            return true;
        }

        bool ready = IsLoaded
            && StartupSequenceOverlay.IsLoaded
            && ActualWidth > 0d
            && ActualHeight > 0d
            && PageHost.ActualWidth > 0d
            && PageHost.ActualHeight > 0d
            && viewModel is not null;
        if (ready)
        {
            startupSurfaceReadyReported = viewModel!.ReportStartupSurfaceReady(
                ActualWidth,
                ActualHeight,
                $"{detail}; shell, PageHost and overlay ready at {ActualWidth:0} × {ActualHeight:0}");
            if (startupSurfaceReadyReported)
            {
                SizeChanged -= OnSizeChanged;
            }
        }
        return startupSurfaceReadyReported;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        TryReportStartupSurfaceReady("MainShellHost.SizeChanged");
    }

    private void AttachViewModel()
    {
        MainViewModel? next = DataContext as MainViewModel;
        if (ReferenceEquals(viewModel, next))
        {
            return;
        }

        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
        viewModel = next;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTransition(viewModel.NavigationTransition);
            ApplyStartupSequence(viewModel.StartupSequence);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.NavigationTransition))
        {
            ApplyTransition(viewModel.NavigationTransition);
        }
        else if (e.PropertyName == nameof(MainViewModel.ThemeTransition))
        {
            if (viewModel.ThemeTransition.IsActive)
            {
                PageHost.CancelNavigationTransitionForTheme();
                PageHost.CancelStartupReveal("ThemeTakeover");
                RelayBandOverlay.CancelTransition();
                TraceworkChrome.CancelFlowRelayVisuals();
            }
            TryBeginThemeVisualReadiness(viewModel.ThemeTransition);
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentTheme))
        {
            TryBeginThemeVisualReadiness(viewModel.ThemeTransition);
        }
        else if (e.PropertyName == nameof(MainViewModel.StartupSequence))
        {
            ApplyStartupSequence(viewModel.StartupSequence);
        }
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        TryReportStartupSurfaceReady("MainShellHost.LayoutUpdated");
        if (!IsLoaded || viewModel is null)
        {
            return;
        }

        StartupInitialProjectionSnapshot projection = viewModel.StartupSequence.InitialProjection;
        if (projection.DispatcherApplied
            && !projection.PostDataLayoutObserved
            && projection.PollingVersion > postDataLayoutVersion)
        {
            postDataLayoutVersion = projection.PollingVersion;
            viewModel.ReportStartupPostDataLayout(projection.PollingVersion);
        }

        if (viewModel.StartupSequence.HasCompleted)
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    private void ApplyStartupSequence(StartupSequenceSnapshot? snapshot)
    {
        if (snapshot is null || startupRevealCoordinator is null)
        {
            return;
        }

        if (snapshot.IsActive)
        {
            if (!startupSequenceOwnsMotion)
            {
                startupSequenceOwnsMotion = true;
                PageHost.CancelNavigationTransitionForStartup();
                RelayBandOverlay.CancelTransition();
                TraceworkChrome.CancelFlowRelayVisuals();
            }
        }
        else if (startupSequenceOwnsMotion)
        {
            startupSequenceOwnsMotion = false;
        }

        if (snapshot.Phase == StartupSequencePhase.Reveal
            && activeStartupRevealVersion < 0)
        {
            activeStartupRevealVersion = snapshot.Version;
            startupOverlayRevealCompleted =
                snapshot.CurrentTheme != AppTheme.Tracework
                || snapshot.MotionLevel == MotionLevel.Off;
            startupShellRevealCompleted = snapshot.MotionLevel == MotionLevel.Off;
        }

        startupRevealCoordinator.Apply(snapshot);
    }

    private void TryBeginThemeVisualReadiness(ThemeTransitionSnapshot snapshot)
    {
        if (!IsLoaded
            || viewModel is null
            || !snapshot.IsActive
            || snapshot.Phase != ThemeTransitionPhase.Latch
            || viewModel.CurrentTheme != snapshot.TargetTheme)
        {
            return;
        }

        long generation = ++themeVisualValidationGeneration;
        RestoreThemeStableState(snapshot.TargetTheme, snapshot.Version);
        WpfSize firstSize = RenderSize;
        LogThemeDiagnostic(
            "ThemeRenderPass1",
            snapshot,
            "scheduled");
        OnNextRenderedFrame(() =>
        {
            if (!IsCurrentThemeGate(snapshot, generation))
            {
                return;
            }

            ThemeVisualReadinessResult pass1 =
                ValidateThemeVisualState(snapshot.TargetTheme, renderPass: 1);
            LogThemeDiagnostic(
                "ThemeRenderPass1",
                snapshot,
                pass1.IsReady ? "validated" : pass1.FailureReason);
            WpfSize secondSize = RenderSize;
            OnNextRenderedFrame(() =>
            {
                if (!IsCurrentThemeGate(snapshot, generation))
                {
                    return;
                }

                ThemeVisualReadinessResult pass2 =
                    ValidateThemeVisualState(snapshot.TargetTheme, renderPass: 2);
                LogThemeDiagnostic(
                    "ThemeRenderPass2",
                    snapshot,
                    pass2.IsReady ? "validated" : pass2.FailureReason);
                bool sizeChanged = firstSize != secondSize
                    || secondSize != RenderSize;
                if (sizeChanged)
                {
                    OnNextRenderedFrame(() =>
                    {
                        if (!IsCurrentThemeGate(snapshot, generation))
                        {
                            return;
                        }

                        ThemeVisualReadinessResult pass3 =
                            ValidateThemeVisualState(
                                snapshot.TargetTheme,
                                renderPass: 3);
                        LogThemeDiagnostic(
                            "ThemeRenderPass3",
                            snapshot,
                            pass3.IsReady
                                ? "validated after SizeChanged"
                                : pass3.FailureReason);
                        ReportThemeVisualReadiness(snapshot, pass3);
                    });
                    return;
                }

                ReportThemeVisualReadiness(snapshot, pass2);
            });
        });
    }

    private void OnNextRenderedFrame(Action action)
    {
        InvalidateVisual();
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(() =>
            {
                _ = Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle,
                    action);
            }));
    }

    private bool IsCurrentThemeGate(
        ThemeTransitionSnapshot snapshot,
        long generation) =>
        generation == themeVisualValidationGeneration
        && viewModel?.ThemeTransition.Version == snapshot.Version
        && viewModel.ThemeTransition.IsActive
        && viewModel.CurrentTheme == snapshot.TargetTheme;

    private void RestoreThemeStableState(AppTheme targetTheme, long version)
    {
        PageHost.CancelNavigationTransitionForTheme();
        PageHost.CancelStartupReveal("ThemeStableState");
        PageHost.RestoreFinalState();
        PageHost.Opacity = 1d;
        PageHost.Clip = null;
        RelayBandOverlay.CancelTransition();
        RelayBandOverlay.RestoreFinalState();
        TraceworkChrome.CancelFlowRelayVisuals();
        LogThemeDiagnostic(
            "ThemeStableStateRestored",
            viewModel?.ThemeTransition
                ?? ThemeTransitionSnapshot.Idle(targetTheme) with { Version = version },
            "navigation/startup clocks cleared; PageHost final state restored");
    }

    private ThemeVisualReadinessResult ValidateThemeVisualState(
        AppTheme targetTheme,
        int renderPass)
    {
        List<string> failures = [];
        bool tracework = targetTheme == AppTheme.Tracework;
        if (TraceworkChrome.Visibility !=
            (tracework ? Visibility.Visible : Visibility.Collapsed))
        {
            failures.Add($"TraceworkChrome={TraceworkChrome.Visibility}");
        }
        if (ClassicChrome.Visibility !=
            (tracework ? Visibility.Collapsed : Visibility.Visible))
        {
            failures.Add($"ClassicChrome={ClassicChrome.Visibility}");
        }

        MediaBrush? expectedSurface = TryFindResource("AppBackgroundBrush") as MediaBrush;
        if (!BrushesMatch(ThemeSurface.Background, expectedSurface))
        {
            failures.Add("ThemeSurface background does not match AppBackgroundBrush");
        }

        Thickness expectedMargin = tracework
            ? new Thickness(120d, 72d, 16d, 46d)
            : new Thickness(18d, 128d, 18d, 42d);
        if (PageHost.Margin != expectedMargin)
        {
            failures.Add($"PageHost.Margin={PageHost.Margin}");
        }
        double expectedMaxWidth = tracework
            ? 10000d
            : ResolveRetroContentMaxWidth();
        if (Math.Abs(PageHost.MaxWidth - expectedMaxWidth) > 0.1d)
        {
            failures.Add($"PageHost.MaxWidth={PageHost.MaxWidth:0.##}");
        }
        WpfHorizontalAlignment expectedAlignment = tracework
            ? WpfHorizontalAlignment.Stretch
            : WpfHorizontalAlignment.Center;
        if (PageHost.HorizontalAlignment != expectedAlignment)
        {
            failures.Add($"PageHost.Alignment={PageHost.HorizontalAlignment}");
        }
        if (PageHost.ActualWidth <= 0d || PageHost.ActualHeight <= 0d)
        {
            failures.Add(
                $"PageHost.Actual=({PageHost.ActualWidth:0.##},{PageHost.ActualHeight:0.##})");
        }
        if (Math.Abs(PageHost.Opacity - 1d) > 0.001d
            || PageHost.Clip is not null)
        {
            failures.Add(
                $"PageHost opacity/clip={PageHost.Opacity:0.###}/{PageHost.Clip?.GetType().Name ?? "null"}");
        }
        if (PageHost.ActiveRoot is FrameworkElement root
            && (Math.Abs(root.Opacity - 1d) > 0.001d || root.Clip is not null))
        {
            failures.Add(
                $"PageRoot opacity/clip={root.Opacity:0.###}/{root.Clip?.GetType().Name ?? "null"}");
        }
        if (ThemeContext.GetCurrentTheme(PageHost) != targetTheme)
        {
            failures.Add(
                $"ThemeContext={ThemeContext.GetCurrentTheme(PageHost)}");
        }
        string prefix = tracework ? "Tracework" : "Classic";
        if (!ContainsThemeLayout(PageHost, prefix))
        {
            failures.Add($"{prefix} page template not realized");
        }

        string reason = string.Join("; ", failures);
        LogThemeDiagnostic(
            failures.Count == 0
                ? "ThemePageTemplateValidated"
                : "ThemeVisualValidationFailed",
            viewModel?.ThemeTransition
                ?? ThemeTransitionSnapshot.Idle(targetTheme),
            failures.Count == 0
                ? $"renderPass={renderPass}; margin={PageHost.Margin}; actual=({PageHost.ActualWidth:0.##},{PageHost.ActualHeight:0.##})"
                : reason);
        return failures.Count == 0
            ? ThemeVisualReadinessResult.Ready(renderPass)
            : ThemeVisualReadinessResult.Failed(reason, renderPass);
    }

    private void ReportThemeVisualReadiness(
        ThemeTransitionSnapshot snapshot,
        ThemeVisualReadinessResult result)
    {
        if (result.IsReady)
        {
            LogThemeDiagnostic(
                "ThemeVisualReady",
                snapshot,
                $"renderPasses={result.RenderPassCount}");
        }
        viewModel?.ReportThemeVisualReady(
            snapshot.Version,
            snapshot.TargetTheme,
            result);
    }

    private double ResolveRetroContentMaxWidth()
    {
        object resource = TryFindResource("RetroContentMaxWidth");
        return resource is double value ? value : 1040d;
    }

    private static bool BrushesMatch(MediaBrush? actual, MediaBrush? expected)
    {
        if (ReferenceEquals(actual, expected))
        {
            return true;
        }
        return actual is SolidColorBrush actualSolid
            && expected is SolidColorBrush expectedSolid
            && actualSolid.Color == expectedSolid.Color;
    }

    private static bool ContainsThemeLayout(DependencyObject root, string prefix)
    {
        if (root.GetType().Name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return true;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            if (ContainsThemeLayout(VisualTreeHelper.GetChild(root, index), prefix))
            {
                return true;
            }
        }
        return false;
    }

    private void LogThemeDiagnostic(
        string eventName,
        ThemeTransitionSnapshot snapshot,
        string reason)
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX * 100d;
        AppLogger.LogKeyEvent(
            $"{eventName} | version={snapshot.Version}; target={snapshot.TargetTheme}; "
            + $"phase={snapshot.Phase}; window=({ActualWidth:0.##},{ActualHeight:0.##}); dpi={dpi:0.#}%; "
            + $"margin={PageHost.Margin}; page=({PageHost.ActualWidth:0.##},{PageHost.ActualHeight:0.##}); "
            + $"chrome=Classic:{ClassicChrome.Visibility}/Tracework:{TraceworkChrome.Visibility}; "
            + $"opacity={PageHost.Opacity:0.###}; translate=(0,0); reason={reason}");
    }

    private void OnStartupOverlayVisualCompleted(long startupVersion)
    {
        if (startupVersion != activeStartupRevealVersion)
        {
            return;
        }

        startupOverlayRevealCompleted = true;
        TryReportStartupRevealVisualCompleted();
    }

    private void OnStartupShellVisualCompleted(
        object? sender,
        StartupRevealVisualCompletedEventArgs e)
    {
        _ = sender;
        if (e.StartupVersion != activeStartupRevealVersion)
        {
            return;
        }

        startupShellRevealCompleted = true;
        TryReportStartupRevealVisualCompleted();
    }

    private void TryReportStartupRevealVisualCompleted()
    {
        if (!startupOverlayRevealCompleted
            || !startupShellRevealCompleted
            || activeStartupRevealVersion < 0
            || viewModel is null)
        {
            return;
        }

        long version = activeStartupRevealVersion;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(() =>
            {
                if (version == activeStartupRevealVersion
                    && startupOverlayRevealCompleted
                    && startupShellRevealCompleted)
                {
                    viewModel?.ReportStartupRevealVisualCompleted(version);
                }
            }));
    }

    private void ApplyTransition(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            PageHost.CompleteNavigation(snapshot.Version);
            RelayBandOverlay.RestoreFinalState();
            return;
        }

        if (snapshot.Phase == NavigationTransitionPhase.Route
            && preparedNavigationVersion != snapshot.Version)
        {
            preparedNavigationVersion = snapshot.Version;
            PageHost.PrepareNavigation(
                snapshot.Plan,
                snapshot.Direction,
                snapshot.Version);
        }

        if (snapshot.Phase == NavigationTransitionPhase.Shift
            && exitedNavigationVersion != snapshot.Version)
        {
            exitedNavigationVersion = snapshot.Version;
            PageHost.PlayExit(
                snapshot.Plan,
                snapshot.Direction,
                snapshot.Version);
        }

        if (snapshot.Phase == NavigationTransitionPhase.Relay
            && snapshot.HasCommitted
            && committedNavigationVersion != snapshot.Version)
        {
            committedNavigationVersion = snapshot.Version;
            PageHost.PrepareCommittedContent(
                snapshot.Plan,
                snapshot.Direction,
                snapshot.Version);
        }

        if (snapshot.Phase == NavigationTransitionPhase.Settle
            && snapshot.HasCommitted
            && settledVersion != snapshot.Version)
        {
            settledVersion = snapshot.Version;
            PageHost.PlayEnter(
                snapshot.Plan,
                snapshot.Direction,
                snapshot.Version);
        }
    }
}
