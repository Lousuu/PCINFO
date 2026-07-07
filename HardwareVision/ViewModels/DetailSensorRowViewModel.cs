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

public sealed class DetailSensorRowViewModel : ObservableObject
{
    public string Name { get; init; } = "--";

    public string Type { get; init; } = "--";

    public string Value { get; init; } = "--";

    public string Unit { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Availability { get; init; } = "--";

    public string ToolTip { get; init; } = string.Empty;

    public static DetailSensorRowViewModel FromReading(SensorReading reading)
    {
        HardwareMetric metric = HardwareMetricService.FromSensorReading(
            HardwareMetricService.CreateSensorMetricId(reading),
            reading.DeviceName,
            HardwareMetricService.MapCategory(reading.Category),
            ViewModelHelpers.FirstAvailable(reading.SensorName, reading.DeviceName, "--")!,
            $"{reading.DeviceName}/{reading.SensorName}/{reading.Type}",
            reading,
            "原始传感器读数。",
            false,
            true,
            0,
            reading.Category.ToString(),
            fallbackSource: reading.Source);

        return FromMetric(metric);
    }

    public static DetailSensorRowViewModel FromMetric(HardwareMetric metric)
    {
        return new DetailSensorRowViewModel
        {
            Name = metric.DisplayName,
            Type = ViewModelHelpers.FirstAvailable(metric.TechnicalName, metric.Category.ToString(), "--")!,
            Value = HardwareMetricService.FormatDisplayValue(metric),
            Unit = MetricFormatService.NormalizeUnit(metric.Unit),
            Source = metric.Source,
            Availability = metric.Availability.ToString(),
            ToolTip = HardwareMetricService.BuildToolTip(metric)
        };
    }

    public bool HasSameValuesAs(DetailSensorRowViewModel other)
    {
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Type, other.Type, StringComparison.Ordinal)
            && string.Equals(Value, other.Value, StringComparison.Ordinal)
            && string.Equals(Unit, other.Unit, StringComparison.Ordinal)
            && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && string.Equals(Availability, other.Availability, StringComparison.Ordinal);
    }
}
