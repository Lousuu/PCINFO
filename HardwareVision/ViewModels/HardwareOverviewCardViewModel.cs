using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;
using static HardwareVision.ViewModels.ViewModelHelpers;

namespace HardwareVision.ViewModels;

public sealed class HardwareOverviewCardViewModel : ObservableObject
{
    private string title = "--";
    private string hardwareName = "--";
    private string headerNote = string.Empty;
    private string badgeText = string.Empty;
    private bool isVisible = true;
    private string toolTip = string.Empty;

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public string HardwareName
    {
        get => hardwareName;
        set => SetProperty(ref hardwareName, value);
    }

    public string HeaderNote
    {
        get => headerNote;
        set => SetProperty(ref headerNote, value);
    }

    public string BadgeText
    {
        get => badgeText;
        set => SetProperty(ref badgeText, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    public string ToolTip
    {
        get => toolTip;
        set => SetProperty(ref toolTip, value);
    }

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public void ReplaceMetrics(IEnumerable<HardwareMetric> metrics)
    {
        Metrics.Clear();

        foreach (HardwareMetric metric in HardwareDetailReadingHelpers.SortMetrics(metrics))
        {
            DetailMetricViewModel viewModel = new();
            viewModel.Update(metric);
            Metrics.Add(viewModel);
        }

        IsVisible = Metrics.Any(metric => metric.IsVisible);
        ToolTip = string.Empty;
    }
}
