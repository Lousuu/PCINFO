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

public sealed class CpuViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel? dashboard;
    private bool isActive;
    private bool isDisposed;
    private string cpuName = "--";
    private string coreThreadSummary = "--";
    private int selectedChartWindowSeconds = 60;

    public CpuViewModel()
    {
        InitializeCharts();
    }

    public CpuViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
        InitializeCharts();
        dashboard.PropertyChanged += OnDashboardPropertyChanged;
    }

    public string CpuName
    {
        get => cpuName;
        private set => SetProperty(ref cpuName, value);
    }

    public string CoreThreadSummary
    {
        get => coreThreadSummary;
        private set => SetProperty(ref coreThreadSummary, value);
    }

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public ObservableCollection<DetailSensorRowViewModel> CoreRows { get; } = new();

    public ObservableCollection<RealtimeMetricChartViewModel> Charts { get; } = new();

    public ObservableCollection<int> ChartWindowOptions { get; } = new([30, 60, 120]);

    public int SelectedChartWindowSeconds
    {
        get => selectedChartWindowSeconds;
        set
        {
            int normalized = value switch
            {
                <= 30 => 30,
                <= 60 => 60,
                _ => 120
            };

            if (SetProperty(ref selectedChartWindowSeconds, normalized))
            {
                foreach (RealtimeMetricChartViewModel chart in Charts)
                {
                    chart.WindowSeconds = normalized;
                }
            }
        }
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
            Refresh();
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
        if (e.PropertyName == nameof(DashboardViewModel.CurrentSensorReadings))
        {
            AppendChartValues(dashboard?.CurrentSensorReadings ?? Array.Empty<SensorReading>());
            if (isActive)
            {
                Refresh();
            }

            return;
        }

        if (isActive && e.PropertyName == nameof(DashboardViewModel.CurrentSnapshot))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (dashboard is null)
        {
            return;
        }

        SensorReading[] cpuReadings = dashboard.CurrentSensorReadings.Where(reading => reading.Category == SensorCategory.Cpu).ToArray();
        HardwareDevice? cpuDevice = dashboard.CurrentSnapshot?.Devices.FirstOrDefault(device => device.Category == SensorCategory.Cpu);
        CpuName = ViewModelHelpers.FirstAvailable(dashboard.CurrentSnapshot?.CpuName, cpuDevice?.Name, cpuReadings.FirstOrDefault()?.DeviceName, "CPU")!;

        string? cores = ViewModelHelpers.Prop(cpuDevice, "NumberOfCores");
        string? threads = ViewModelHelpers.Prop(cpuDevice, "NumberOfLogicalProcessors");
        CoreThreadSummary = string.IsNullOrWhiteSpace(cores) && string.IsNullOrWhiteSpace(threads)
            ? "--"
            : $"{ViewModelHelpers.ValueOrFallback(cores)} 核心 / {ViewModelHelpers.ValueOrFallback(threads)} 线程";

        ReplaceMetricCollection(Metrics, BuildMetrics(cpuReadings, cpuDevice).Select(dashboard.ConfigureMetric));
        ViewModelHelpers.UpdateSensorRows(CoreRows, cpuReadings
            .Where(HardwareDetailReadingHelpers.IsPerCoreReading)
            .Select(DetailSensorRowViewModel.FromReading)
            .Where(row => row.IsVisible));
    }

    private void InitializeCharts()
    {
        Charts.Add(new RealtimeMetricChartViewModel("总负载", "%", 0d, 100d));
        Charts.Add(new RealtimeMetricChartViewModel("封装温度", "℃", 0d, 110d));
        Charts.Add(new RealtimeMetricChartViewModel("封装功耗", "W", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("平均核心频率", "MHz", 0d, double.NaN));

        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.WindowSeconds = selectedChartWindowSeconds;
        }
    }

    private void AppendChartValues(IReadOnlyList<SensorReading> readings)
    {
        if (Charts.Count < 4)
        {
            return;
        }

        Charts[0].Append(HardwareDetailReadingHelpers.FindPreferredReading(readings, SensorType.Load, "Total")?.Value);
        Charts[1].Append(HardwareDetailReadingHelpers.FindPreferredReading(readings, SensorType.Temperature, "Package", "CPU")?.Value);
        Charts[2].Append(HardwareDetailReadingHelpers.FindPreferredReading(readings, SensorType.Power, "Package", "CPU")?.Value);
        Charts[3].Append(CalculateAverageCpuClockMhz(readings));
    }

    private static double? CalculateAverageCpuClockMhz(IEnumerable<SensorReading> readings)
    {
        double[] values = readings
            .Where(HardwareDetailReadingHelpers.IsCpuClockReadingUsableForFrequency)
            .Select(reading => reading.Value!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Average();
    }

    private IEnumerable<HardwareMetric> BuildMetrics(IReadOnlyList<SensorReading> readings, HardwareDevice? cpuDevice)
    {
        yield return SensorMetric("cpu.temperature.current", "当前温度", "CPU Temperature", HardwareDetailReadingHelpers.FindPreferredReading(readings, SensorType.Temperature, "Package", "CPU"), "当前可用 CPU 温度读数。", true, 0);
        yield return SensorMetric("cpu.load.total", "总负载", "CPU Total Load", HardwareDetailReadingHelpers.FindPreferredReading(readings, SensorType.Load, "Total"), "CPU 总负载。", true, 1);
        yield return SensorMetric("cpu.clock.current", "当前频率", "CPU Clock", HardwareDetailReadingHelpers.FindPreferredCpuClockReading(readings), "当前 CPU 频率。", true, 2);
        yield return SensorMetric("cpu.power.package", "当前功耗", "CPU Package Power", HardwareDetailReadingHelpers.FindPreferredReading(readings, SensorType.Power, "Package", "CPU"), "CPU 封装功耗。", true, 3);
        yield return ValueMetric("cpu.core.count", "核心数量", "NumberOfCores", ViewModelHelpers.Prop(cpuDevice, "NumberOfCores"), string.Empty, "WMI", "Win32_Processor.NumberOfCores。", false, 10);
        yield return ValueMetric("cpu.thread.count", "线程数量", "NumberOfLogicalProcessors", ViewModelHelpers.Prop(cpuDevice, "NumberOfLogicalProcessors"), string.Empty, "WMI", "Win32_Processor.NumberOfLogicalProcessors。", false, 11);
    }

    private static HardwareMetric SensorMetric(string id, string displayName, string technicalName, SensorReading? reading, string description, bool important, int order)
    {
        return HardwareMetricService.FromSensorReading(id, "cpu", HardwareMetricCategory.Cpu, displayName, technicalName, reading, description, important, true, order, "CPU", fallbackSource: reading?.Source ?? "LibreHardwareMonitor");
    }

    private static HardwareMetric ValueMetric(string id, string displayName, string technicalName, string? value, string unit, string source, string description, bool important, int order)
    {
        return HardwareMetricService.FromValue(id, "cpu", HardwareMetricCategory.Cpu, displayName, technicalName, value, unit, source, string.IsNullOrWhiteSpace(value) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, true, order, "CPU");
    }
}
