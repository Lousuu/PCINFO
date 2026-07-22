using System.ComponentModel;
using HardwareVision.Models;
using HardwareVision.ViewModels;
using HardwareVision.Controls;

namespace HardwareVision.Views.Shell;

public partial class MainShellHost : System.Windows.Controls.UserControl
{
    private MainViewModel? viewModel;
    private long settledVersion = -1;
    private StartupShellRevealCoordinator? startupRevealCoordinator;
    private bool shellReadyReported;

    public MainShellHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        AttachViewModel();
        startupRevealCoordinator ??= new StartupShellRevealCoordinator(
            TraceworkChrome.StartupSignalRailTarget,
            TraceworkChrome.StartupTelemetryTarget,
            PageHost,
            TraceworkChrome.StartupTimeRibbonTarget);
        LayoutUpdated += OnLayoutUpdated;
        ApplyStartupSequence(viewModel?.StartupSequence);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PageHost.CancelTransition();
        RelayBandOverlay.CancelTransition();
        TraceworkChrome.CancelFlowRelayVisuals();
        LayoutUpdated -= OnLayoutUpdated;
        StartupSequenceOverlay.RestoreFinalState();
        startupRevealCoordinator?.RestoreFinalState();
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.NotifyShellUnloaded();
            viewModel = null;
        }
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
        else if (e.PropertyName == nameof(MainViewModel.ThemeTransition)
                 && viewModel.ThemeTransition.IsActive)
        {
            PageHost.CancelTransition();
            RelayBandOverlay.CancelTransition();
            TraceworkChrome.CancelFlowRelayVisuals();
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
        if (shellReadyReported
            || !IsLoaded
            || ActualWidth <= 0d
            || ActualHeight <= 0d
            || PageHost.ActualWidth <= 0d
            || PageHost.ActualHeight <= 0d
            || viewModel is null)
        {
            return;
        }

        shellReadyReported = true;
        viewModel.ReportStartupShellReady(ActualWidth, ActualHeight);
        LayoutUpdated -= OnLayoutUpdated;
    }

    private void ApplyStartupSequence(StartupSequenceSnapshot? snapshot)
    {
        if (snapshot is null || startupRevealCoordinator is null)
        {
            return;
        }

        if (snapshot.IsActive)
        {
            PageHost.CancelTransition();
            RelayBandOverlay.CancelTransition();
            TraceworkChrome.CancelFlowRelayVisuals();
        }

        startupRevealCoordinator.Apply(snapshot);
    }

    private void ApplyTransition(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            PageHost.RestoreFinalState();
            RelayBandOverlay.RestoreFinalState();
            return;
        }

        if (snapshot.Phase == NavigationTransitionPhase.Route)
        {
            PageHost.CancelTransition();
        }

        if (snapshot.Phase == NavigationTransitionPhase.Settle
            && snapshot.HasCommitted
            && settledVersion != snapshot.Version)
        {
            settledVersion = snapshot.Version;
            PageHost.PlaySettle(snapshot.Plan, snapshot.Direction);
        }
    }
}
