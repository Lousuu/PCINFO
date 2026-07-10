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
    private string? gpuNameToolTip;
    private string gpuSelectionHint = "默认优先选择独立 GPU；可在下拉框切换。";
    private bool hasGpuDetails;
    private bool hasGpuMetrics;
    private bool hasGpuSensors;
    private int selectedChartWindowSeconds = 60;
    private bool isRefreshingGpuDevices;
    private string? chartGpuKey;

    public GpuViewModel()
    {
        InitializeCharts();
    }

    public GpuViewModel(DashboardViewModel dashboard, AppSettings settings, ISettingsService settingsService)
    {
        this.dashboard = dashboard;
        this.settings = settings;
        this.settingsService = settingsService;
        InitializeCharts();
        dashboard.PropertyChanged += OnDashboardPropertyChanged;
    }

    public ObservableCollection<GpuDevice> GpuDevices { get; } = new();

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> InfoItems { get; } = new();

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

    public GpuDevice? SelectedGpu
    {
        get => selectedGpu;
        set
        {
            if (isRefreshingGpuDevices && value is null)
            {
                return;
            }

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
                AppendChartValues(value);
            }
        }
    }

    public string GpuName
    {
        get => gpuName;
        private set => SetProperty(ref gpuName, value);
    }

    public string? GpuNameToolTip
    {
        get => gpuNameToolTip;
        private set => SetProperty(ref gpuNameToolTip, value);
    }

    public string GpuSelectionHint
    {
        get => gpuSelectionHint;
        private set => SetProperty(ref gpuSelectionHint, value);
    }

    public bool HasGpuDetails
    {
        get => hasGpuDetails;
        private set => SetProperty(ref hasGpuDetails, value);
    }

    public bool HasGpuMetrics
    {
        get => hasGpuMetrics;
        private set => SetProperty(ref hasGpuMetrics, value);
    }

    public bool HasGpuSensors
    {
        get => hasGpuSensors;
        private set => SetProperty(ref hasGpuSensors, value);
    }

    public ObservableCollection<DetailSensorRowViewModel> SensorRows { get; } = new();

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
        if (e.PropertyName != nameof(DashboardViewModel.GpuDevices))
        {
            return;
        }

        AppendChartValues(ResolveChartGpu());
        if (isActive)
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

        isRefreshingGpuDevices = true;
        try
        {
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
        }
        finally
        {
            isRefreshingGpuDevices = false;
        }

        RefreshSelectedGpu();
    }

    private void RefreshSelectedGpu()
    {
        GpuDevice? gpu = SelectedGpu;
        GpuName = ViewModelHelpers.FirstAvailable(gpu?.Name, "GPU")!;
        GpuNameToolTip = ViewModelHelpers.NullIfShortOrSame(GpuName, GpuName, 32);
        GpuSelectionHint = GpuDevices.Count <= 1 ? "当前仅检测到一个 GPU。" : "可在下拉框切换当前展示 GPU。";

        ReplaceMetricCollection(InfoItems, BuildInfoMetrics(gpu).Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));
        ReplaceMetricCollection(Metrics, BuildSensorMetrics(gpu).Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));
        ViewModelHelpers.UpdateSensorRows(
            SensorRows,
            gpu?.Sensors
                .Where(HasActualSensorReading)
                .OrderBy(reading => reading.Type)
                .ThenBy(reading => reading.SensorName, StringComparer.OrdinalIgnoreCase)
                .Select(DetailSensorRowViewModel.FromReading)
                .Where(row => row.IsVisible)
            ?? Enumerable.Empty<DetailSensorRowViewModel>());
        HasGpuDetails = InfoItems.Any(item => item.IsVisible);
        HasGpuMetrics = Metrics.Any(item => item.IsVisible);
        HasGpuSensors = SensorRows.Count > 0;
    }

    private void InitializeCharts()
    {
        RealtimeMetricChartViewModel loadChart = new("核心负载", "%", 0d, 100d);
        loadChart.ConfigureAdaptiveMaximum(10d, 100d, 10d);
        Charts.Add(loadChart);
        Charts.Add(new RealtimeMetricChartViewModel("核心温度", "℃", 0d, 110d));
        Charts.Add(new RealtimeMetricChartViewModel("当前功耗", "W", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("核心频率", "MHz", 0d, double.NaN));

        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.WindowSeconds = selectedChartWindowSeconds;
        }
    }

    private void AppendChartValues(GpuDevice? gpu)
    {
        if (gpu is null || Charts.Count < 4)
        {
            return;
        }

        string nextGpuKey = CreateChartGpuKey(gpu);
        if (chartGpuKey is null)
        {
            chartGpuKey = nextGpuKey;
        }
        else if (!string.Equals(chartGpuKey, nextGpuKey, StringComparison.OrdinalIgnoreCase))
        {
            chartGpuKey = nextGpuKey;
            ClearCharts();
        }

        Charts[0].Append(gpu.CoreLoad?.Value);
        Charts[1].Append(gpu.TemperatureCore?.Value);
        Charts[2].Append(gpu.PowerPackage?.Value);
        Charts[3].Append(gpu.CoreClock?.Value);
    }

    private GpuDevice? ResolveChartGpu()
    {
        if (dashboard is null)
        {
            return SelectedGpu;
        }

        string? selectedId = SelectedGpu?.Id ?? settings?.PreferredGpuId;
        return dashboard.GpuDevices.FirstOrDefault(
                gpu => string.Equals(gpu.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? dashboard.GpuDevices.FirstOrDefault(gpu => gpu.IsPreferred)
            ?? dashboard.GpuDevices.FirstOrDefault();
    }

    private void ClearCharts()
    {
        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.Clear();
        }
    }

    private static string CreateChartGpuKey(GpuDevice gpu)
    {
        string identity = string.Join(
            "|",
            NormalizeChartKeyPart(gpu.Vendor),
            NormalizeChartKeyPart(gpu.HardwareType),
            NormalizeChartKeyPart(gpu.Name),
            gpu.AdapterRam?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        return string.IsNullOrWhiteSpace(identity.Replace("|", string.Empty, StringComparison.Ordinal))
            ? NormalizeChartKeyPart(gpu.Id)
            : identity;
    }

    private static string NormalizeChartKeyPart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    }

    private static IEnumerable<HardwareMetric> BuildInfoMetrics(GpuDevice? gpu)
    {
        yield return ValueMetric("gpu.name", "GPU 名称", "Name", gpu?.Name, string.Empty, "HardwareVision", "GPU 名称。", true, 0, gpu?.Id);
        yield return ValueMetric("gpu.vendor", "厂商", "Vendor", gpu?.Vendor, string.Empty, "HardwareVision", "GPU 厂商。", false, 1, gpu?.Id);
        yield return ValueMetric("gpu.hardware.type", "硬件类型", "HardwareType", gpu?.HardwareType, string.Empty, "HardwareVision", "GPU 硬件类型。", false, 2, gpu?.Id);
        yield return ValueMetric("gpu.driver.version", "驱动版本", "DriverVersion", gpu?.DriverVersion, string.Empty, "HardwareVision", "GPU 驱动版本。", false, 3, gpu?.Id);
        yield return ValueMetric("gpu.adapter.ram", "适配器显存", "AdapterRAM", gpu?.AdapterRam, "B", "HardwareVision", "适配器显存。", false, 4, gpu?.Id);
        yield return ValueMetric("gpu.integrated", "集成显卡", "IsIntegrated", FormatBool(gpu?.IsIntegrated), string.Empty, "HardwareVision", "是否为集成显卡。", false, 5, gpu?.Id);
        yield return ValueMetric("gpu.discrete", "独立显卡", "IsDiscrete", FormatBool(gpu?.IsDiscrete), string.Empty, "HardwareVision", "是否为独立显卡。", false, 6, gpu?.Id);
        yield return ValueMetric("gpu.preferred", "优先 GPU", "IsPreferred", FormatBool(gpu?.IsPreferred), string.Empty, "HardwareVision", "是否为当前优先 GPU。", false, 7, gpu?.Id);
        yield return ValueMetric("gpu.availability", "可用性", "Availability", FormatAvailability(gpu?.Availability), string.Empty, "HardwareVision", "GPU 传感器数据可用性。", false, 8, gpu?.Id);
        yield return ValueMetric("gpu.id", "GPU Id", "Id", gpu?.Id, string.Empty, "HardwareVision", "GPU 标识。", false, 9, gpu?.Id);
    }

    private static IEnumerable<HardwareMetric> BuildSensorMetrics(GpuDevice? gpu)
    {
        yield return SensorMetric("gpu.temperature.core", "核心温度", "GPU Core Temperature", gpu?.TemperatureCore, "GPU 核心温度。", true, 10, gpu?.Id);
        yield return SensorMetric("gpu.temperature.hotspot", "热点温度", "GPU Hot Spot Temperature", gpu?.TemperatureHotSpot, "GPU 热点温度。", false, 11, gpu?.Id);
        yield return SensorMetric("gpu.temperature.memory.junction", "显存结温", "GPU Memory Junction Temperature", gpu?.TemperatureMemoryJunction, "GPU 显存结温。", false, 12, gpu?.Id);
        yield return SensorMetric("gpu.load.core", "核心负载", "GPU Core Load", gpu?.CoreLoad, "GPU 核心负载。", true, 13, gpu?.Id);
        yield return SensorMetric("gpu.load.memory", "显存负载", "GPU Memory Load", gpu?.MemoryLoad, "GPU 显存或控制器负载。", false, 14, gpu?.Id);
        yield return SensorMetric("gpu.memory.used", "显存使用", "GPU Memory Used", gpu?.MemoryUsed, "GPU 显存使用量。", true, 15, gpu?.Id);
        yield return SensorMetric("gpu.memory.free", "显存空闲", "GPU Memory Free", gpu?.MemoryFree, "GPU 显存空闲量。", false, 16, gpu?.Id);
        yield return ValueMetric("gpu.memory.total", "显存总量", "GPU Memory Total", ViewModelHelpers.SensorValueToBytes(gpu?.MemoryTotal) ?? gpu?.AdapterRam, "B", "HardwareVision", "GPU 显存总量。", true, 17, gpu?.Id);
        yield return SensorMetric("gpu.clock.core", "核心频率", "GPU Core Clock", gpu?.CoreClock, "GPU 核心频率。", true, 18, gpu?.Id);
        yield return SensorMetric("gpu.clock.memory", "显存频率", "GPU Memory Clock", gpu?.MemoryClock, "GPU 显存频率。", false, 19, gpu?.Id);
        yield return SensorMetric("gpu.power.package", "当前功耗", "GPU Power", gpu?.PowerPackage, "GPU 功耗。", true, 20, gpu?.Id);
        yield return SensorMetric("gpu.voltage.core", "核心电压", "GPU Core Voltage", gpu?.CoreVoltage, "GPU 核心电压。", false, 21, gpu?.Id);
        yield return SensorMetric("gpu.fan.speed", "风扇转速", "GPU Fan Speed", gpu?.FanSpeed, "GPU 风扇转速。", true, 22, gpu?.Id);
        yield return SensorMetric("gpu.pcie.rx", "PCIe Rx", "PCIe Receive Throughput", gpu?.PcieRx, "PCIe 接收吞吐。", false, 23, gpu?.Id);
        yield return SensorMetric("gpu.pcie.tx", "PCIe Tx", "PCIe Transmit Throughput", gpu?.PcieTx, "PCIe 发送吞吐。", false, 24, gpu?.Id);
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

    private static string? FormatBool(bool? value)
    {
        return value.HasValue ? (value.Value ? "是" : "否") : null;
    }

    private static string? FormatAvailability(SensorAvailability? availability)
    {
        return availability switch
        {
            SensorAvailability.Available => "可用",
            SensorAvailability.Unavailable => "不可用",
            SensorAvailability.NotReported => null,
            SensorAvailability.Error => "错误",
            SensorAvailability.Unknown => null,
            _ => availability?.ToString()
        };
    }

    private static bool HasActualSensorReading(SensorReading reading)
    {
        return reading.IsAvailable
            && reading.Value is double value
            && !double.IsNaN(value)
            && !double.IsInfinity(value);
    }
}
