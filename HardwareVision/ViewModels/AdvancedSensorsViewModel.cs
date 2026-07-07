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

public sealed class AdvancedSensorsViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel? dashboard;
    private bool isActive;
    private bool isDisposed;
    private string statusText = "完整传感器列表仅在打开本页面时刷新";
    private IReadOnlyList<DetailSensorRowViewModel> sensorRows = Array.Empty<DetailSensorRowViewModel>();

    public AdvancedSensorsViewModel()
    {
    }

    public AdvancedSensorsViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public IReadOnlyList<DetailSensorRowViewModel> SensorRows
    {
        get => sensorRows;
        private set => SetProperty(ref sensorRows, value);
    }

    public void SetActive(bool active)
    {
        if (isDisposed || dashboard is null || isActive == active)
        {
            return;
        }

        isActive = active;
        if (active)
        {
            dashboard.PropertyChanged += OnDashboardPropertyChanged;
            ApplyReadings(dashboard.CurrentSensorReadings);
        }
        else
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }
    }

    public void Dispose()
    {
        if (dashboard is not null)
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }

        isDisposed = true;
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isActive && e.PropertyName == nameof(DashboardViewModel.CurrentSensorReadings) && dashboard is not null)
        {
            ApplyReadings(dashboard.CurrentSensorReadings);
        }
    }

    private void ApplyReadings(IReadOnlyList<SensorReading> readings)
    {
        DetailSensorRowViewModel[] rows = readings
            .OrderBy(reading => reading.Category)
            .ThenBy(reading => reading.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reading => reading.SensorName, StringComparer.OrdinalIgnoreCase)
            .Select(DetailSensorRowViewModel.FromReading)
            .ToArray();

        if (!HardwareDetailReadingHelpers.DetailRowsEqual(SensorRows, rows))
        {
            SensorRows = rows;
        }

        StatusText = readings.Count == 0
            ? "当前传感器源未返回可显示数据"
            : $"{rows.Length} / {readings.Count} 个传感器读数正在显示";
    }
}
