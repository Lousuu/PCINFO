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

public sealed class RealtimeMetricChartViewModel : ObservableObject
{
    private const int MaxPoints = 80;
    private string title = string.Empty;
    private string unit = string.Empty;
    private bool hasData;

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public string Unit
    {
        get => unit;
        set => SetProperty(ref unit, value);
    }

    public bool HasData
    {
        get => hasData;
        set => SetProperty(ref hasData, value);
    }

    public ObservableCollection<double> Values { get; } = new();

    public void Append(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            HasData = Values.Count > 0;
            return;
        }

        Values.Add(value.Value);
        while (Values.Count > MaxPoints)
        {
            Values.RemoveAt(0);
        }

        HasData = true;
    }
}
