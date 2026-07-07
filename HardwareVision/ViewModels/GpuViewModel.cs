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

public sealed class GpuViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel? dashboard;
    private readonly AppSettings? settings;
    private readonly ISettingsService? settingsService;
    private bool isActive;
    private bool isDisposed;
    private GpuDevice? selectedGpu;
    private string gpuName = "--";
    private string gpuSelectionHint = "默认优先选择独立 GPU；可在下拉框切换。";
    private IReadOnlyList<DetailSensorRowViewModel> sensorRows = Array.Empty<DetailSensorRowViewModel>();

    public GpuViewModel()
    {
    }

    public GpuViewModel(DashboardViewModel dashboard, AppSettings settings, ISettingsService settingsService)
    {
        this.dashboard = dashboard;
        this.settings = settings;
        this.settingsService = settingsService;
    }

    public ObservableCollection<GpuDevice> GpuDevices { get; } = new();

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> InfoItems { get; } = new();

    public GpuDevice? SelectedGpu
    {
        get => selectedGpu;
        set
        {
            if (SetProperty(ref selectedGpu, value))
            {
                if (settings is not null && settingsService is not null && value is not null)
                {
                    if (dashboard is not null)
                    {
                        dashboard.PreferredGpuId = value.Id;
                    }
                    else
                    {
                        settings.PreferredGpuId = value.Id;
                        _ = settingsService.UpdateAsync(updated => updated.PreferredGpuId = value.Id);
                    }
                }

                RefreshSelectedGpu();
            }
        }
    }

    public string GpuName
    {
        get => gpuName;
        private set => SetProperty(ref gpuName, value);
    }

    public string GpuSelectionHint
    {
        get => gpuSelectionHint;
        private set => SetProperty(ref gpuSelectionHint, value);
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
            Refresh();
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
        if (isActive && e.PropertyName is nameof(DashboardViewModel.GpuDevices) or nameof(DashboardViewModel.CurrentSensorReadings))
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

        GpuDevices.Clear();
        foreach (GpuDevice gpu in dashboard.GpuDevices)
        {
            GpuDevices.Add(gpu);
        }

        string? selectedId = dashboard.SelectedGpu?.Id ?? settings?.PreferredGpuId;
        selectedGpu = GpuDevices.FirstOrDefault(gpu => string.Equals(gpu.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? dashboard.SelectedGpu
            ?? GpuDevices.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedGpu));
        RefreshSelectedGpu();
    }

    private void RefreshSelectedGpu()
    {
        GpuDevice? gpu = SelectedGpu;
        GpuName = ViewModelHelpers.FirstAvailable(gpu?.Name, "GPU")!;
        GpuSelectionHint = GpuDevices.Count <= 1 ? "当前仅检测到一个 GPU。" : "可在下拉框切换当前展示 GPU。";

        ReplaceMetricCollection(InfoItems, BuildInfoMetrics(gpu).Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));
        ReplaceMetricCollection(Metrics, BuildSensorMetrics(gpu).Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));
        SensorRows = gpu?.Sensors.Select(DetailSensorRowViewModel.FromReading).ToArray() ?? Array.Empty<DetailSensorRowViewModel>();
    }

    private static IEnumerable<HardwareMetric> BuildInfoMetrics(GpuDevice? gpu)
    {
        yield return ValueMetric("gpu.hardware.type", "硬件类型", "HardwareType", gpu?.HardwareType, string.Empty, gpu?.Source ?? "WMI", "GPU 硬件类型。", false, 0, gpu?.Id);
        yield return ValueMetric("gpu.driver.version", "驱动版本", "DriverVersion", gpu?.DriverVersion, string.Empty, "WMI", "Win32_VideoController.DriverVersion。", false, 1, gpu?.Id);
        yield return ValueMetric("gpu.adapter.ram", "适配器显存", "AdapterRAM", gpu?.AdapterRam, "B", "WMI", "驱动报告的适配器显存。", false, 2, gpu?.Id);
        yield return ValueMetric("gpu.source", "数据来源", "Source", gpu?.Source, string.Empty, "HardwareVision", "合并后的 GPU 数据来源。", false, 3, gpu?.Id);
        yield return ValueMetric("gpu.availability", "可用性", "Availability", gpu?.Availability.ToString(), string.Empty, "HardwareVision", "GPU 传感器数据可用性。", false, 4, gpu?.Id);
    }

    private static IEnumerable<HardwareMetric> BuildSensorMetrics(GpuDevice? gpu)
    {
        yield return SensorMetric("gpu.temperature.core", "核心温度", "GPU Core Temperature", gpu?.TemperatureCore, "GPU 核心温度。", true, 10, gpu?.Id);
        yield return SensorMetric("gpu.temperature.hotspot", "热点温度", "GPU Hot Spot Temperature", gpu?.TemperatureHotSpot, "GPU 热点温度。", false, 11, gpu?.Id, false);
        yield return SensorMetric("gpu.temperature.memory.junction", "显存结温", "GPU Memory Junction Temperature", gpu?.TemperatureMemoryJunction, "GPU 显存结温。", false, 12, gpu?.Id, false);
        yield return SensorMetric("gpu.load.core", "核心负载", "GPU Core Load", gpu?.CoreLoad, "GPU 核心负载。", true, 13, gpu?.Id);
        yield return SensorMetric("gpu.load.memory", "显存负载", "GPU Memory Load", gpu?.MemoryLoad, "GPU 显存或控制器负载。", false, 14, gpu?.Id, false);
        yield return SensorMetric("gpu.memory.used", "显存使用", "GPU Memory Used", gpu?.MemoryUsed, "GPU 显存使用量。", true, 15, gpu?.Id);
        yield return ValueMetric("gpu.memory.total", "显存总量", "GPU Memory Total", ViewModelHelpers.SensorValueToBytes(gpu?.MemoryTotal) ?? gpu?.AdapterRam, "B", gpu?.Source ?? "LibreHardwareMonitor / WMI", "GPU 显存总量。", false, 16, gpu?.Id);
        yield return SensorMetric("gpu.clock.core", "核心频率", "GPU Core Clock", gpu?.CoreClock, "GPU 核心频率。", true, 17, gpu?.Id);
        yield return SensorMetric("gpu.clock.memory", "显存频率", "GPU Memory Clock", gpu?.MemoryClock, "GPU 显存频率。", false, 18, gpu?.Id, false);
        yield return SensorMetric("gpu.power.package", "当前功耗", "GPU Power", gpu?.PowerPackage, "GPU 功耗。", true, 19, gpu?.Id);
        yield return SensorMetric("gpu.voltage.core", "核心电压", "GPU Core Voltage", gpu?.CoreVoltage, "GPU 核心电压。", false, 20, gpu?.Id, false);
        yield return SensorMetric("gpu.fan.speed", "风扇转速", "GPU Fan Speed", gpu?.FanSpeed, "GPU 风扇转速。", true, 21, gpu?.Id);
        yield return SensorMetric("gpu.pcie.rx", "PCIe Rx", "PCIe Receive Throughput", gpu?.PcieRx, "PCIe 接收吞吐。", false, 22, gpu?.Id, false);
        yield return SensorMetric("gpu.pcie.tx", "PCIe Tx", "PCIe Transmit Throughput", gpu?.PcieTx, "PCIe 发送吞吐。", false, 23, gpu?.Id, false);
    }

    private static HardwareMetric SensorMetric(string id, string displayName, string technicalName, SensorReading? reading, string description, bool important, int order, string? hardwareId, bool visible = true)
    {
        return HardwareMetricService.FromSensorReading(id, hardwareId ?? "gpu", HardwareMetricCategory.Gpu, displayName, technicalName, reading, description, important, visible, order, "GPU", fallbackSource: reading?.Source ?? "LibreHardwareMonitor");
    }

    private static HardwareMetric ValueMetric(string id, string displayName, string technicalName, object? value, string unit, string source, string description, bool important, int order, string? hardwareId, bool visible = true)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        return HardwareMetricService.FromValue(id, hardwareId ?? "gpu", HardwareMetricCategory.Gpu, displayName, technicalName, textValue, unit, source, string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, "GPU");
    }
}
