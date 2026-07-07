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

public sealed class DetailMetricViewModel : ObservableObject
{
    private string label = "--";
    private string value = "--";
    private string unit = string.Empty;
    private string source = string.Empty;
    private string technicalName = string.Empty;
    private MetricAvailability availability = MetricAvailability.NotReported;
    private string toolTip = string.Empty;
    private bool isVisible = true;
    private int displayOrder;
    private string groupName = string.Empty;
    private HardwareMetric? metric;

    public DetailMetricViewModel()
    {
    }

    public DetailMetricViewModel(string label, string value)
    {
        this.label = label;
        this.value = value;
    }

    public string Label
    {
        get => label;
        set => SetProperty(ref label, value);
    }

    public string Value
    {
        get => value;
        set => SetProperty(ref this.value, value);
    }

    public string Unit
    {
        get => unit;
        set => SetProperty(ref unit, value);
    }

    public string Source
    {
        get => source;
        set => SetProperty(ref source, value);
    }

    public string TechnicalName
    {
        get => technicalName;
        set => SetProperty(ref technicalName, value);
    }

    public MetricAvailability Availability
    {
        get => availability;
        set => SetProperty(ref availability, value);
    }

    public string ToolTip
    {
        get => toolTip;
        set => SetProperty(ref toolTip, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    public int DisplayOrder
    {
        get => displayOrder;
        set => SetProperty(ref displayOrder, value);
    }

    public string GroupName
    {
        get => groupName;
        set => SetProperty(ref groupName, value);
    }

    public HardwareMetric? Metric
    {
        get => metric;
        private set => SetProperty(ref metric, value);
    }

    public void Update(HardwareMetric updatedMetric)
    {
        Metric = updatedMetric;
        Label = updatedMetric.DisplayName;
        Value = HardwareMetricService.FormatDisplayValue(updatedMetric);
        Unit = MetricFormatService.NormalizeUnit(updatedMetric.Unit);
        Source = updatedMetric.Source;
        TechnicalName = updatedMetric.TechnicalName;
        Availability = updatedMetric.Availability;
        ToolTip = string.Empty;
        IsVisible = updatedMetric.IsVisible && !IsSourceMetric(updatedMetric);
        DisplayOrder = updatedMetric.DisplayOrder;
        GroupName = updatedMetric.GroupName;
    }

    private static bool IsSourceMetric(HardwareMetric metric)
    {
        return string.Equals(metric.TechnicalName, "Source", StringComparison.OrdinalIgnoreCase)
            || metric.Id.Contains(".source", StringComparison.OrdinalIgnoreCase)
            || metric.Id.Contains(".note", StringComparison.OrdinalIgnoreCase);
    }
}
