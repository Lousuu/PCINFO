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

public sealed class SensorReadingRowViewModel : ObservableObject
{
    public string DeviceName { get; init; } = "--";

    public string SensorName { get; init; } = "--";

    public string Category { get; init; } = "--";

    public string Type { get; init; } = "--";

    public string Value { get; init; } = "--";

    public string Source { get; init; } = string.Empty;

    public string ToolTip { get; init; } = string.Empty;

    public static SensorReadingRowViewModel FromReading(SensorReading reading)
    {
        return new SensorReadingRowViewModel
        {
            DeviceName = reading.DeviceName,
            SensorName = reading.SensorName,
            Category = reading.Category.ToString(),
            Type = reading.Type.ToString(),
            Value = MetricFormatService.FormatSensorValue(reading),
            Source = reading.Source,
            ToolTip = $"{reading.DeviceName}\n{reading.SensorName}"
        };
    }
}
