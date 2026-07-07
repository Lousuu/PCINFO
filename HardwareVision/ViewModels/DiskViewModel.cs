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
    private readonly DiskDeviceService diskDeviceService = new();
    private readonly DiskPerformanceService diskPerformanceService = new();
    private bool isActive;
    private bool isDisposed;
    private string statusText = "硬盘页面仅在打开时刷新列表。";
    private IReadOnlyList<DiskPerformanceSnapshot> performanceSnapshots = Array.Empty<DiskPerformanceSnapshot>();

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
            _ = RefreshAsync();
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

        diskPerformanceService.Dispose();
        isDisposed = true;
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isActive && e.PropertyName is nameof(DashboardViewModel.CurrentSnapshot) or nameof(DashboardViewModel.CurrentSensorReadings))
        {
            _ = RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        if (dashboard is null)
        {
            return;
        }

        performanceSnapshots = await diskPerformanceService.GetCurrentSnapshotsAsync();
        IReadOnlyList<DiskDevice> disks = diskDeviceService.BuildDiskDevices(dashboard.CurrentSnapshot, dashboard.CurrentSensorReadings, performanceSnapshots);
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
        yield return Metric("disk.health", "健康状态", "Health Status", ViewModelHelpers.FirstAvailable(disks.Select(disk => ViewModelHelpers.FirstAvailable(disk.SmartStatus, disk.NvmeHealthStatus, disk.HealthStatus?.Value?.ToString(CultureInfo.InvariantCulture))).ToArray()), string.Empty, "MSFT_PhysicalDisk / SMART", "硬盘健康状态。", true, 6);
    }

    private static IEnumerable<HardwareMetric> BuildDeviceMetrics(DiskDevice disk)
    {
        yield return Metric("disk.model", "型号", "Model", disk.Model, string.Empty, disk.Source, "硬盘型号。", true, 10, disk.Id);
        yield return Metric("disk.interface.type", "接口类型", "InterfaceType / BusType", ViewModelHelpers.FirstAvailable(disk.InterfaceType, disk.BusType), string.Empty, disk.Source, "接口或总线类型。", true, 11, disk.Id);
        yield return Metric("disk.volume.letters", "分区盘符", "Volumes", JoinOrNull(disk.Volumes), string.Empty, "WMI", "关联分区盘符。", true, 12, disk.Id);
        yield return SensorMetric("disk.temperature.current", "温度", "Storage Temperature", disk.Temperature, "当前硬盘温度。", true, 13, disk.Id);
        yield return Metric("disk.read.speed", "读取速率", "Disk Read Speed", disk.ReadSpeed, "B/s", "PerformanceCounter", "当前读取吞吐。", true, 14, disk.Id);
        yield return Metric("disk.write.speed", "写入速率", "Disk Write Speed", disk.WriteSpeed, "B/s", "PerformanceCounter", "当前写入吞吐。", true, 15, disk.Id);
        yield return Metric("disk.usage.percent", "使用率", "Usage Percent", disk.UsagePercent, "%", "WMI", "关联卷使用率。", true, 16, disk.Id);
        yield return Metric("disk.firmware", "固件版本", "FirmwareRevision", disk.FirmwareRevision, string.Empty, disk.Source, "固件版本。", false, 20, disk.Id);
        yield return Metric("disk.serial", "序列号", "SerialNumber", disk.SerialNumber, string.Empty, disk.Source, "硬盘序列号，默认隐藏。", false, 30, disk.Id, false);
    }

    private static HardwareMetric SensorMetric(string id, string displayName, string technicalName, SensorReading? reading, string description, bool important, int order, string hardwareId)
    {
        return HardwareMetricService.FromSensorReading(id, hardwareId, HardwareMetricCategory.Disk, displayName, technicalName, reading, description, important, true, order, "硬盘", fallbackSource: reading?.Source ?? "LibreHardwareMonitor");
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, object? value, string unit, string source, string description, bool important, int order, string? hardwareId = null, bool visible = true)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        if (textValue == "NaN")
        {
            textValue = null;
        }

        return HardwareMetricService.FromValue(id, hardwareId ?? "disk", HardwareMetricCategory.Disk, displayName, technicalName, textValue, unit, source, string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, "硬盘");
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

    private static string? JoinOrNull(IEnumerable<string> values)
    {
        string[] filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return filtered.Length == 0 ? null : string.Join(", ", filtered);
    }
}
