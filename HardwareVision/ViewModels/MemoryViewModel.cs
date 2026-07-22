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
    private bool hasMemoryModules;

    public MemoryViewModel()
    {
        OverviewProjection = new VisibleMetricProjection(OverviewMetrics);
        ProfessionalProjection = new VisibleMetricProjection(ProfessionalMetrics);
    }

    public MemoryViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
        OverviewProjection = new VisibleMetricProjection(OverviewMetrics);
        ProfessionalProjection = new VisibleMetricProjection(ProfessionalMetrics);
    }

    public ObservableCollection<DetailMetricViewModel> OverviewMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> ProfessionalMetrics { get; } = new();

    public ObservableCollection<MemoryModuleViewModel> MemoryModules { get; } = new();

    public VisibleMetricProjection OverviewProjection { get; }

    public VisibleMetricProjection ProfessionalProjection { get; }

    public bool HasMemoryModules
    {
        get => hasMemoryModules;
        private set => SetProperty(ref hasMemoryModules, value);
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

        OverviewProjection.Dispose();
        ProfessionalProjection.Dispose();
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
        ulong? pageFileUsed = status is null ? null : status.TotalPageFile - status.AvailablePageFile;

        ReplaceMetricCollection(OverviewMetrics, new[]
        {
            Metric("memory.physical.total", "总物理内存", "TotalPhysicalMemory", status?.TotalPhysical ?? dashboard.CurrentSnapshot?.MemoryTotal, "B", "Windows API / WMI", "物理内存总量。", true, 0),
            Metric("memory.physical.used", "已用物理内存", "Memory Used", used, "B", "Windows API", "当前已用物理内存。", true, 1),
            Metric("memory.physical.available", "可用物理内存", "Memory Available", status?.AvailablePhysical, "B", "Windows API", "当前可用物理内存。", true, 2),
            Metric("memory.physical.load", "物理内存使用率", "Memory Load", status?.MemoryLoad, "%", "Windows API", "当前物理内存使用率。", true, 3),
            Metric("memory.pagefile.total", "提交总量", "TotalPageFile", status?.TotalPageFile, "B", "Windows API", "提交限制。", false, 4),
            Metric("memory.pagefile.used", "已提交", "UsedPageFile", pageFileUsed, "B", "Windows API", "当前已提交容量。", false, 5),
            Metric("memory.pagefile.available", "可提交", "AvailablePageFile", status?.AvailablePageFile, "B", "Windows API", "剩余可提交容量。", false, 6)
        }.Select(dashboard.ConfigureMetric));

        ReplaceMetricCollection(ProfessionalMetrics, BuildProfessionalMetrics(dashboard.CurrentSnapshot).Select(dashboard.ConfigureMetric));
        RefreshModules(dashboard.CurrentSnapshot);
    }

    private static IEnumerable<HardwareMetric> BuildProfessionalMetrics(HardwareSnapshot? snapshot)
    {
        HardwareDevice[] modules = GetInstalledMemoryModules(snapshot);
        HardwareDevice[] arrays = GetMemoryArrays(snapshot);
        string? configuredClock = modules.Select(GetConfiguredClock).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        string? maxCapacity = arrays.Select(array => ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(array, "MaxCapacityEx"), ViewModelHelpers.Prop(array, "MaxCapacity"))).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        string? slotCount = arrays.Select(array => ViewModelHelpers.Prop(array, "MemoryDevices")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        yield return Metric("memory.module.count", "已安装内存条数量", "InstalledModuleCount", modules.Length, string.Empty, "WMI", "已检测到的内存模块数量。", true, 40);
        yield return Metric("memory.module.configured.clock", "ConfiguredClockSpeed", "ConfiguredClockSpeed", configuredClock, "MHz", "WMI", "内存配置频率。", true, 41);
        yield return Metric("memory.array.slot.count", "内存插槽", "MemoryDevices", slotCount, string.Empty, "WMI", "内存插槽数量。", false, 42);
        yield return Metric("memory.array.max.capacity", "最大容量", "MaxCapacity / MaxCapacityEx", maxCapacity, "KB", "WMI", "内存阵列最大容量。", false, 43);
    }

    private void RefreshModules(HardwareSnapshot? snapshot)
    {
        MemoryModules.Clear();
        foreach (HardwareDevice module in GetInstalledMemoryModules(snapshot))
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
                Metric("memory.module.speed", "Speed", "Speed", GetRatedSpeed(module), "MHz", "WMI", "标称频率。", true, 25),
                Metric("memory.module.configured.clock", "ConfiguredClockSpeed", "ConfiguredClockSpeed", GetConfiguredClock(module), "MHz", "WMI", "配置频率。", true, 26),
                Metric("memory.module.form.factor", "FormFactor", "FormFactor", ViewModelHelpers.Prop(module, "FormFactor"), string.Empty, "WMI", "内存外形规格。", false, 27),
                Metric("memory.module.type", "MemoryType", "MemoryType / SMBIOSMemoryType", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(module, "SMBIOSMemoryType"), ViewModelHelpers.Prop(module, "MemoryType")), string.Empty, "WMI", "内存类型。", false, 28),
                Metric("memory.module.width", "位宽", "DataWidth / TotalWidth", JoinNonEmpty(" / ", ViewModelHelpers.Prop(module, "DataWidth"), ViewModelHelpers.Prop(module, "TotalWidth")), string.Empty, "WMI", "内存数据位宽。", false, 29)
            }.Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));

            MemoryModules.Add(item);
        }

        HasMemoryModules = MemoryModules.Count > 0;
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, object? value, string unit, string source, string description, bool important, int order, bool visible = true)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        return HardwareMetricService.FromValue(id, "memory", HardwareMetricCategory.Memory, displayName, technicalName, textValue, unit, source, string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, "内存");
    }

    private static string? GetConfiguredClock(HardwareDevice module)
    {
        return ViewModelHelpers.FirstAvailable(
            ViewModelHelpers.Prop(module, "ConfiguredClockSpeedMHz"),
            ViewModelHelpers.Prop(module, "ConfiguredClockSpeed"),
            ViewModelHelpers.Prop(module, "SpeedMHz"),
            ViewModelHelpers.Prop(module, "Speed"));
    }

    private static string? GetRatedSpeed(HardwareDevice module)
    {
        return ViewModelHelpers.FirstAvailable(
            ViewModelHelpers.Prop(module, "SpeedMHz"),
            ViewModelHelpers.Prop(module, "Speed"),
            ViewModelHelpers.Prop(module, "ConfiguredClockSpeedMHz"),
            ViewModelHelpers.Prop(module, "ConfiguredClockSpeed"));
    }

    private static HardwareDevice[] GetMemoryArrays(HardwareSnapshot? snapshot)
    {
        return snapshot?.Devices
            .Where(device => device.Category == SensorCategory.Memory)
            .Where(device => !string.IsNullOrWhiteSpace(ViewModelHelpers.Prop(device, "MemoryDevices")))
            .ToArray() ?? Array.Empty<HardwareDevice>();
    }

    private static HardwareDevice[] GetInstalledMemoryModules(HardwareSnapshot? snapshot)
    {
        return snapshot?.Devices
            .Where(device => device.Category == SensorCategory.Memory)
            .Where(IsRealMemoryModule)
            .GroupBy(GetMemoryModuleDedupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray() ?? Array.Empty<HardwareDevice>();
    }

    private static bool IsRealMemoryModule(HardwareDevice module)
    {
        if (!TryGetCapacityBytes(module, out ulong capacityBytes) || capacityBytes == 0)
        {
            return false;
        }

        return HasUsefulIdentity(module);
    }

    private static string GetMemoryModuleDedupKey(HardwareDevice module)
    {
        string serial = NormalizeIdentity(ViewModelHelpers.Prop(module, "SerialNumber"));
        if (IsUsefulIdentity(serial))
        {
            return $"serial:{serial}";
        }

        string partNumber = NormalizeIdentity(ViewModelHelpers.Prop(module, "PartNumber"));
        string deviceLocator = NormalizeIdentity(ViewModelHelpers.Prop(module, "DeviceLocator"));
        if (IsUsefulIdentity(partNumber) && IsUsefulIdentity(deviceLocator))
        {
            return $"part-slot:{partNumber}:{deviceLocator}";
        }

        string bankLabel = NormalizeIdentity(ViewModelHelpers.Prop(module, "BankLabel"));
        string capacity = ViewModelHelpers.Prop(module, "CapacityBytes") ?? string.Empty;
        if (IsUsefulIdentity(bankLabel) || IsUsefulIdentity(deviceLocator))
        {
            return $"slot-capacity:{bankLabel}:{deviceLocator}:{capacity}";
        }

        return $"name-capacity:{NormalizeIdentity(module.Name)}:{NormalizeIdentity(module.Model)}:{capacity}";
    }

    private static bool HasUsefulIdentity(HardwareDevice module)
    {
        return IsUsefulIdentity(ViewModelHelpers.Prop(module, "DeviceLocator"))
            || IsUsefulIdentity(ViewModelHelpers.Prop(module, "BankLabel"))
            || IsUsefulIdentity(ViewModelHelpers.Prop(module, "SerialNumber"))
            || IsUsefulIdentity(ViewModelHelpers.Prop(module, "PartNumber"));
    }

    private static bool TryGetCapacityBytes(HardwareDevice module, out ulong capacityBytes)
    {
        string? value = ViewModelHelpers.Prop(module, "CapacityBytes");
        return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out capacityBytes);
    }

    private static string NormalizeIdentity(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static bool IsUsefulIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        return !normalized.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("00000000", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("None", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase)
            && !normalized.Equals("Default string", StringComparison.OrdinalIgnoreCase);
    }

    private static string? JoinNonEmpty(string separator, params string?[] values)
    {
        string[] filtered = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        return filtered.Length == 0 ? null : string.Join(separator, filtered);
    }
}
