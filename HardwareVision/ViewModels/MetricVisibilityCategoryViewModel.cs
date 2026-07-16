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

public sealed class MetricVisibilityCategoryViewModel : ObservableObject
{
    public MetricVisibilityCategoryViewModel(string title, IEnumerable<MetricVisibilityItemViewModel> metrics)
    {
        Title = title;
        VisibleMetrics = new ObservableCollection<MetricVisibilityItemViewModel>(metrics);
    }

    public string Title { get; }

    public string Summary => $"{VisibleMetrics.Count(metric => metric.IsVisible)} / {VisibleMetrics.Count} 已显示";

    public ObservableCollection<MetricVisibilityItemViewModel> VisibleMetrics { get; }
}
