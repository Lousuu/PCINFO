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

public sealed class DiskViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel? dashboard;
    private bool isActive;
    private bool isDisposed;
    private string statusText = "硬盘页面仅在打开时刷新列表。";

    public DiskViewModel()
    {
    }

    public DiskViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
    }

    public ObservableCollection<DetailMetricViewModel> OverviewMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> ProfessionalMetrics { get; } = new();

    public ObservableCollection<DiskDeviceViewModel> DiskDevices { get; } = new();

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
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
        if (isActive && e.PropertyName is nameof(DashboardViewModel.CurrentSnapshot) or nameof(DashboardViewModel.DiskDevices))
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

        IReadOnlyList<DiskDevice> disks = dashboard.DiskDevices;
        DiskDevice? systemDisk = disks.FirstOrDefault(disk => disk.IsSystemDisk) ?? disks.FirstOrDefault();

        ReplaceMetricCollection(OverviewMetrics, BuildOverviewMetrics(disks, systemDisk).Select(dashboard.ConfigureMetric));
        ReplaceMetricCollection(ProfessionalMetrics, BuildProfessionalMetrics(disks).Select(dashboard.ConfigureMetric));
        DiskDevices.Clear();

        foreach (DiskDevice disk in disks)
        {
            DiskDeviceViewModel item = new()
            {
                Name = disk.Name,
                Subtitle = string.Join(" / ", new[] { disk.MediaType, disk.InterfaceType, disk.BusType }.Where(value => !string.IsNullOrWhiteSpace(value)))
            };

            ReplaceMetricCollection(item.Metrics, BuildDeviceMetrics(disk).Select(dashboard.ConfigureMetric));
            DiskDevices.Add(item);
        }

        StatusText = disks.Count == 0 ? "未检测到可显示的硬盘设备。" : $"检测到 {disks.Count} 个存储设备。";
    }

    private static IEnumerable<HardwareMetric> BuildOverviewMetrics(IReadOnlyList<DiskDevice> disks, DiskDevice? systemDisk)
    {
        yield return Metric("disk.count", "硬盘数量", "Disk Count", disks.Count, string.Empty, "WMI", "检测到的物理硬盘数量。", true, 0);
        yield return Metric("disk.system", "系统盘", "System Disk", systemDisk?.Name, string.Empty, systemDisk?.Source ?? "WMI", "当前系统盘。", true, 1);
        yield return Metric("disk.capacity.total", "总容量", "Total Disk Size", Sum(disks.Select(disk => disk.Size)), "B", "WMI", "全部硬盘总容量。", true, 2);
        yield return Metric("disk.capacity.used", "已用容量", "Used Space", Sum(disks.Select(disk => disk.UsedSpace)), "B", "WMI", "关联卷已用容量。", true, 3);
        yield return Metric("disk.capacity.free", "可用容量", "Free Space", Sum(disks.Select(disk => disk.FreeSpace)), "B", "WMI", "关联卷可用容量。", true, 4);
        yield return Metric("disk.temperature.max", "最高硬盘温度", "Max Storage Temperature", disks.Select(disk => disk.Temperature?.Value).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(double.NaN).Max(), "C", "LibreHardwareMonitor", "最高当前硬盘温度。", true, 5);
    }

    private static IEnumerable<HardwareMetric> BuildProfessionalMetrics(IReadOnlyList<DiskDevice> disks)
    {
        yield return Metric("disk.health", "健康状态", "Health Status", ViewModelHelpers.FirstAvailable(disks.Select(disk => ViewModelHelpers.FirstAvailable(disk.SmartStatus, disk.NvmeHealthStatus, disk.OperationalStatus)).ToArray()), string.Empty, "MSFT_PhysicalDisk / SMART", "优先显示 Windows Storage 健康状态，其次使用 SMART / Win32 状态。", true, 6, showWhenUnavailable: true);
        yield return Metric("disk.life.minimum", "最低剩余寿命", "Minimum Remaining Life", Minimum(disks.Select(disk => disk.RemainingLife?.Value)), "%", "MSFT_StorageReliabilityCounter / LibreHardwareMonitor", "全部磁盘中最低的可用剩余寿命估算。", true, 7, showWhenUnavailable: true);
    }

    internal static IEnumerable<HardwareMetric> BuildDeviceMetrics(DiskDevice disk)
    {
        yield return Metric("disk.model", "型号", "Model", disk.Model, string.Empty, disk.Source, "硬盘型号。", true, 10, disk.Id);
        yield return Metric("disk.interface.type", "接口类型", "InterfaceType / BusType", ViewModelHelpers.FirstAvailable(disk.InterfaceType, disk.BusType), string.Empty, disk.Source, "接口或总线类型。", true, 11, disk.Id);
        yield return Metric("disk.volume.letters", "分区盘符", "Volumes", JoinOrNull(disk.Volumes), string.Empty, "WMI", "关联分区盘符。", true, 12, disk.Id);
        yield return Metric("disk.capacity.total", "总容量", "Size", disk.Size, "B", "WMI", "物理磁盘容量。", true, 13, disk.Id);
        yield return Metric("disk.capacity.used", "已用容量", "UsedSpace", disk.UsedSpace, "B", "WMI", "关联卷已用容量。", true, 14, disk.Id);
        yield return Metric("disk.capacity.free", "可用容量", "FreeSpace", disk.FreeSpace, "B", "WMI", "关联卷可用容量。", true, 15, disk.Id);
        yield return SensorMetric("disk.temperature.current", "当前温度", "Storage Temperature", disk.Temperature, "当前硬盘温度；Windows Storage 可靠性计数器优先。", true, 16, disk.Id, true);
        yield return SensorMetric("disk.temperature.maximum", "最高记录温度", "Storage Temperature Max", disk.MaximumTemperature, "Windows Storage 报告的最高温度。", true, 17, disk.Id, true);
        yield return Metric("disk.read.speed", "读取速率", "Disk Read Speed", disk.ReadSpeed, "B/s", "PerformanceCounter", "当前读取吞吐。", true, 18, disk.Id);
        yield return Metric("disk.write.speed", "写入速率", "Disk Write Speed", disk.WriteSpeed, "B/s", "PerformanceCounter", "当前写入吞吐。", true, 19, disk.Id);
        yield return Metric("disk.usage.percent", "使用率", "Usage Percent", disk.UsagePercent, "%", "WMI", "关联卷使用率。", true, 20, disk.Id);
        yield return Metric("disk.health.status", "健康状态", "Health Status", ViewModelHelpers.FirstAvailable(disk.SmartStatus, disk.NvmeHealthStatus), string.Empty, disk.Source, "Windows Storage 健康状态优先，其次 SMART / Win32。", true, 21, disk.Id, showWhenUnavailable: true);
        yield return Metric("disk.operational.status", "运行状态", "Operational Status", disk.OperationalStatus, string.Empty, "MSFT_PhysicalDisk", "Windows Storage 物理磁盘运行状态。", true, 22, disk.Id, showWhenUnavailable: true);
        yield return SensorMetric("disk.health.remaining", "剩余寿命", "Remaining Life", disk.RemainingLife ?? disk.HealthStatus, "Windows Storage 或 SMART 剩余寿命。", true, 23, disk.Id, true);
        yield return SensorMetric("disk.health.wear", "磨损", "Wear", disk.Wear, "SSD 已消耗耐久度；数值越高表示磨损越多。", true, 24, disk.Id, true);
        yield return SensorMetric("disk.read.total", "累计读取", "Total Host Reads", disk.ReadTotal, "累计主机读取量；优先使用 Windows Storage，其次 SMART。", true, 25, disk.Id, true);
        yield return SensorMetric("disk.write.total", "累计写入", "Total Host Writes", disk.WriteTotal, "累计主机写入量；优先使用 Windows Storage，其次 SMART。", true, 26, disk.Id, true);
        yield return SensorMetric("disk.power.on.hours", "通电时间", "Power On Hours", disk.PowerOnHours, "累计通电小时。", true, 27, disk.Id, true);
        yield return SensorMetric("disk.power.cycle.count", "通电次数", "Power Cycle Count", disk.PowerCycleCount, "累计启停或通电次数。", true, 28, disk.Id, true);
        yield return SensorMetric("disk.read.errors.total", "读取错误", "Read Errors Total", disk.ReadErrorsTotal, "Windows Storage 累计读取错误。", true, 29, disk.Id, true);
        yield return SensorMetric("disk.write.errors.total", "写入错误", "Write Errors Total", disk.WriteErrorsTotal, "Windows Storage 累计写入错误。", true, 30, disk.Id, true);
        yield return SensorMetric("disk.read.latency.max", "最高读取延迟", "Read Latency Max", disk.ReadLatencyMax, "Windows Storage 报告的最高读取延迟。", true, 31, disk.Id, true);
        yield return SensorMetric("disk.write.latency.max", "最高写入延迟", "Write Latency Max", disk.WriteLatencyMax, "Windows Storage 报告的最高写入延迟。", true, 32, disk.Id, true);
        yield return SensorMetric("disk.flush.latency.max", "最高刷新延迟", "Flush Latency Max", disk.FlushLatencyMax, "Windows Storage 报告的最高缓存刷新延迟。", true, 33, disk.Id, true);
        yield return Metric("disk.firmware", "固件版本", "FirmwareRevision", disk.FirmwareRevision, string.Empty, disk.Source, "固件版本。", false, 30, disk.Id);
        yield return Metric("disk.serial", "序列号", "SerialNumber", disk.SerialNumber, string.Empty, disk.Source, "硬盘序列号，默认隐藏。", false, 40, disk.Id, false);
    }

    private static HardwareMetric SensorMetric(string id, string displayName, string technicalName, SensorReading? reading, string description, bool important, int order, string hardwareId, bool showWhenUnavailable = false)
    {
        HardwareMetric metric = HardwareMetricService.FromSensorReading(id, hardwareId, HardwareMetricCategory.Disk, displayName, technicalName, reading, description, important, true, order, "硬盘", fallbackSource: reading?.Source ?? "LibreHardwareMonitor");
        metric.ShowWhenUnavailable = showWhenUnavailable;
        return metric;
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, object? value, string unit, string source, string description, bool important, int order, string? hardwareId = null, bool visible = true, bool showWhenUnavailable = false)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        if (textValue == "NaN")
        {
            textValue = null;
        }

        HardwareMetric metric = HardwareMetricService.FromValue(id, hardwareId ?? "disk", HardwareMetricCategory.Disk, displayName, technicalName, textValue, unit, source, string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, "硬盘");
        metric.ShowWhenUnavailable = showWhenUnavailable;
        return metric;
    }

    private static ulong? Sum(IEnumerable<ulong?> values)
    {
        ulong total = 0;
        bool hasValue = false;
        foreach (ulong? value in values)
        {
            if (value.HasValue)
            {
                hasValue = true;
                total += value.Value;
            }
        }

        return hasValue ? total : null;
    }

    private static double? Minimum(IEnumerable<double?> values)
    {
        double[] available = values.Where(value => value.HasValue && double.IsFinite(value.Value)).Select(value => value!.Value).ToArray();
        return available.Length == 0 ? null : available.Min();
    }

    private static string? JoinOrNull(IEnumerable<string> values)
    {
        string[] filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return filtered.Length == 0 ? null : string.Join(", ", filtered);
    }
}
