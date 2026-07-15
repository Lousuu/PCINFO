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
    private string? toolTip;
    private string? labelToolTip;
    private string? valueToolTip;
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

    public string Id => Metric?.Id ?? string.Empty;

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

    public string? ToolTip
    {
        get => toolTip;
        set => SetProperty(ref toolTip, value);
    }

    public string? LabelToolTip
    {
        get => labelToolTip;
        private set => SetProperty(ref labelToolTip, value);
    }

    public string? ValueToolTip
    {
        get => valueToolTip;
        private set => SetProperty(ref valueToolTip, value);
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
        LabelToolTip = ViewModelHelpers.NullIfShortOrSame(Label, Label, 12);
        ValueToolTip = ViewModelHelpers.NullIfShortOrSame(Value, Value, 16);
        ToolTip = BuildToolTip(updatedMetric);
        IsVisible = updatedMetric.IsVisible
            && (HasDisplayValue(updatedMetric) || updatedMetric.ShowWhenUnavailable)
            && !IsSourceMetric(updatedMetric);
        DisplayOrder = updatedMetric.DisplayOrder;
        GroupName = updatedMetric.GroupName;
        OnPropertyChanged(nameof(Id));
    }

    private static bool HasDisplayValue(HardwareMetric metric)
    {
        return metric.Availability == MetricAvailability.Available
            && !string.IsNullOrWhiteSpace(metric.Value)
            && !string.Equals(metric.Value, HardwareMetricService.EmptyValue, StringComparison.Ordinal);
    }

    private static string? BuildToolTip(HardwareMetric metric)
    {
        string displayValue = HardwareMetricService.FormatDisplayValue(metric);
        string? labelToolTip = ViewModelHelpers.NullIfShortOrSame(metric.DisplayName, metric.DisplayName, 12);
        string? valueToolTip = ViewModelHelpers.NullIfShortOrSame(displayValue, displayValue, 16);
        string? technicalToolTip = ViewModelHelpers.NullIfShortOrSame(metric.TechnicalName, metric.TechnicalName, 36);

        if (labelToolTip is null && valueToolTip is null && technicalToolTip is null)
        {
            return null;
        }

        string[] lines = new[] { labelToolTip, technicalToolTip, valueToolTip }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return lines.Length == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static bool IsSourceMetric(HardwareMetric metric)
    {
        return string.Equals(metric.TechnicalName, "Source", StringComparison.OrdinalIgnoreCase)
            || metric.Id.Contains(".source", StringComparison.OrdinalIgnoreCase)
            || metric.Id.Contains(".note", StringComparison.OrdinalIgnoreCase);
    }
}
