using System.ComponentModel;
using HardwareVision.Models;
using HardwareVision.ViewModels;

namespace HardwareVision.Views.Shell;

public partial class MainShellHost : System.Windows.Controls.UserControl
{
    private MainViewModel? viewModel;
    private long settledVersion = -1;

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
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        PageHost.CancelTransition();
        RelayBandOverlay.CancelTransition();
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
        }
    }

    private void ApplyTransition(NavigationTransitionSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            PageHost.RestoreFinalState();
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
