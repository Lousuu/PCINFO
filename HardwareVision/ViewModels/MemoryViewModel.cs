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

public sealed class MemoryViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel? dashboard;
    private bool isActive;
    private bool isDisposed;

    public MemoryViewModel()
    {
    }

    public MemoryViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
    }

    public ObservableCollection<DetailMetricViewModel> OverviewMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> ProfessionalMetrics { get; } = new();

    public ObservableCollection<MemoryModuleViewModel> MemoryModules { get; } = new();

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
        if (isActive && e.PropertyName is nameof(DashboardViewModel.CurrentSnapshot) or nameof(DashboardViewModel.CurrentSensorReadings))
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

        MemoryStatusSnapshot? status = MemoryStatusService.GetCurrentStatus();
        ulong? used = status is null ? null : status.TotalPhysical - status.AvailablePhysical;

        ReplaceMetricCollection(OverviewMetrics, new[]
        {
            Metric("memory.physical.total", "总物理内存", "TotalPhysicalMemory", status?.TotalPhysical ?? dashboard.CurrentSnapshot?.MemoryTotal, "B", "Windows API / WMI", "物理内存总量。", true, 0),
            Metric("memory.physical.used", "已用物理内存", "Memory Used", used, "B", "Windows API", "当前已用物理内存。", true, 1),
            Metric("memory.physical.available", "可用物理内存", "Memory Available", status?.AvailablePhysical, "B", "Windows API", "当前可用物理内存。", true, 2),
            Metric("memory.physical.load", "物理内存使用率", "Memory Load", status?.MemoryLoad, "%", "Windows API", "当前物理内存使用率。", true, 3)
        }.Select(dashboard.ConfigureMetric));

        ReplaceMetricCollection(ProfessionalMetrics, BuildProfessionalMetrics(dashboard.CurrentSnapshot).Select(dashboard.ConfigureMetric));
        RefreshModules(dashboard.CurrentSnapshot);
    }

    private static IEnumerable<HardwareMetric> BuildProfessionalMetrics(HardwareSnapshot? snapshot)
    {
        HardwareDevice[] modules = snapshot?.Devices.Where(device => device.Category == SensorCategory.Memory).ToArray() ?? Array.Empty<HardwareDevice>();
        string? configuredClock = modules.Select(module => ViewModelHelpers.Prop(module, "ConfiguredClockSpeed")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        yield return Metric("memory.module.count", "已安装内存条数量", "InstalledModuleCount", modules.Length, string.Empty, "WMI", "已检测到的内存模块数量。", true, 40);
        yield return Metric("memory.module.configured.clock", "ConfiguredClockSpeed", "ConfiguredClockSpeed", configuredClock, "MHz", "WMI", "内存配置频率。", true, 41);
        yield return Metric("memory.frequency.note", "内存频率说明", "MemoryFrequencyNote", "Speed 为标称值，ConfiguredClockSpeed 为配置频率；两者都不是实时频率。", string.Empty, "HardwareVision", "内存频率字段解释。", false, 44);
    }

    private void RefreshModules(HardwareSnapshot? snapshot)
    {
        MemoryModules.Clear();
        foreach (HardwareDevice module in snapshot?.Devices.Where(device => device.Category == SensorCategory.Memory) ?? Enumerable.Empty<HardwareDevice>())
        {
            MemoryModuleViewModel item = new()
            {
                ModuleName = ViewModelHelpers.FirstAvailable(module.Name, module.Model, "Memory Module")!,
                SlotName = ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(module, "DeviceLocator"), ViewModelHelpers.Prop(module, "BankLabel"), "--")!
            };

            ReplaceMetricCollection(item.Metrics, new[]
            {
                Metric("memory.module.bank", "插槽位置", "BankLabel / DeviceLocator", item.SlotName, string.Empty, "WMI", "内存模块插槽。", true, 20),
                Metric("memory.module.capacity", "容量", "Capacity", ViewModelHelpers.Prop(module, "CapacityBytes"), "B", "WMI", "单条内存容量。", true, 21),
                Metric("memory.module.manufacturer", "厂商", "Manufacturer", ViewModelHelpers.Prop(module, "Manufacturer"), string.Empty, "WMI", "内存厂商。", false, 22),
                Metric("memory.module.part.number", "PartNumber", "PartNumber", ViewModelHelpers.Prop(module, "PartNumber"), string.Empty, "WMI", "内存料号。", false, 23),
                Metric("memory.module.serial", "SerialNumber", "SerialNumber", ViewModelHelpers.Prop(module, "SerialNumber"), string.Empty, "WMI", "内存序列号，默认隐藏。", false, 24, false),
                Metric("memory.module.speed", "Speed", "Speed", ViewModelHelpers.Prop(module, "Speed"), "MHz", "WMI", "标称频率。", true, 25),
                Metric("memory.module.configured.clock", "ConfiguredClockSpeed", "ConfiguredClockSpeed", ViewModelHelpers.Prop(module, "ConfiguredClockSpeed"), "MHz", "WMI", "配置频率。", true, 26)
            }.Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));

            MemoryModules.Add(item);
        }
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, object? value, string unit, string source, string description, bool important, int order, bool visible = true)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        return HardwareMetricService.FromValue(id, "memory", HardwareMetricCategory.Memory, displayName, technicalName, textValue, unit, source, string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, "内存");
    }
}
