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

public sealed class NetworkViewModel : ObservableObject, IDisposable
{
    private readonly DashboardViewModel? dashboard;
    private readonly AppSettings? settings;
    private readonly ISettingsService? settingsService;
    private bool isActive;
    private bool isDisposed;
    private bool showVirtualAdapters;
    private NetworkAdapterItemViewModel? selectedAdapter;
    private string selectedAdapterName = "--";
    private string selectedAdapterSubtitle = "默认隐藏虚拟网卡";
    private string adapterSelectionHint = "默认优先显示当前正在使用的物理网卡。";
    private string visibleAdapterSummary = "0 ADAPTERS";

    public NetworkViewModel()
    {
    }

    public NetworkViewModel(DashboardViewModel dashboard, AppSettings settings, ISettingsService settingsService)
    {
        this.dashboard = dashboard;
        this.settings = settings;
        this.settingsService = settingsService;
        showVirtualAdapters = settings.ShowVirtualNetworkAdapters;
    }

    public ObservableCollection<NetworkAdapterItemViewModel> NetworkAdapters { get; } = new();

    public ObservableCollection<DetailMetricViewModel> OverviewMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> ProfessionalMetrics { get; } = new();

    public bool ShowVirtualAdapters
    {
        get => showVirtualAdapters;
        set
        {
            if (SetProperty(ref showVirtualAdapters, value))
            {
                if (settings is not null && settingsService is not null)
                {
                    settings.ShowVirtualNetworkAdapters = value;
                    _ = settingsService.UpdateAsync(updated => updated.ShowVirtualNetworkAdapters = value);
                }

                Refresh();
            }
        }
    }

    public NetworkAdapterItemViewModel? SelectedAdapter
    {
        get => selectedAdapter;
        set
        {
            if (SetProperty(ref selectedAdapter, value))
            {
                if (settings is not null && settingsService is not null && value is not null)
                {
                    settings.PreferredNetworkAdapterId = value.Device.Id;
                    _ = settingsService.UpdateAsync(updated => updated.PreferredNetworkAdapterId = value.Device.Id);
                }

                RefreshSelectedAdapter();
            }
        }
    }

    public string SelectedAdapterName
    {
        get => selectedAdapterName;
        private set => SetProperty(ref selectedAdapterName, value);
    }

    public string SelectedAdapterSubtitle
    {
        get => selectedAdapterSubtitle;
        private set => SetProperty(ref selectedAdapterSubtitle, value);
    }

    public string AdapterSelectionHint
    {
        get => adapterSelectionHint;
        private set => SetProperty(ref adapterSelectionHint, value);
    }

    public string VisibleAdapterSummary
    {
        get => visibleAdapterSummary;
        private set => SetProperty(ref visibleAdapterSummary, value);
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
        if (isActive && e.PropertyName == nameof(DashboardViewModel.NetworkAdapters))
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

        IReadOnlyList<NetworkAdapterDevice> devices = dashboard.NetworkAdapters;

        NetworkAdapterDevice[] filtered = devices
            .Where(device => ShowVirtualAdapters || !device.IsVirtual)
            .OrderByDescending(device => IsPreferred(device))
            .ThenByDescending(device => device.IsUp)
            .ThenByDescending(device => device.IPv4Addresses.Count > 0)
            .ThenBy(device => device.IsVirtual)
            .ToArray();

        string? currentId = SelectedAdapter?.Device.Id ?? settings?.PreferredNetworkAdapterId;
        NetworkAdapters.Clear();
        foreach (NetworkAdapterDevice device in filtered)
        {
            NetworkAdapterItemViewModel item = new() { Device = device };
            ReplaceMetricCollection(item.Metrics, BuildAdapterMetrics(device).Select(metric => dashboard.ConfigureMetric(metric)));
            NetworkAdapters.Add(item);
        }

        VisibleAdapterSummary = $"{NetworkAdapters.Count} ADAPTERS";
        SelectedAdapter = NetworkAdapters.FirstOrDefault(item => string.Equals(item.Device.Id, currentId, StringComparison.OrdinalIgnoreCase))
            ?? NetworkAdapters.FirstOrDefault(item => item.Device.IsUp && item.Device.IPv4Addresses.Count > 0 && !item.Device.IsVirtual)
            ?? NetworkAdapters.FirstOrDefault();
    }

    private bool IsPreferred(NetworkAdapterDevice device)
    {
        return !string.IsNullOrWhiteSpace(settings?.PreferredNetworkAdapterId)
            && string.Equals(settings.PreferredNetworkAdapterId, device.Id, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSelectedAdapter()
    {
        NetworkAdapterDevice? device = SelectedAdapter?.Device;
        SelectedAdapterName = ViewModelHelpers.FirstAvailable(device?.Name, device?.Description, "--")!;
        SelectedAdapterSubtitle = device is null
            ? "未选择网卡"
            : $"{ViewModelHelpers.ResolveNetworkType(device)} / {(device.IsUp ? "已连接" : "未连接")}";
        AdapterSelectionHint = ShowVirtualAdapters ? "正在显示虚拟网卡。" : "已隐藏虚拟网卡，可打开“显示虚拟网卡”查看。";

        ReplaceMetricCollection(OverviewMetrics, BuildOverviewMetrics(device).Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));
        ReplaceMetricCollection(ProfessionalMetrics, BuildProfessionalMetrics(device).Select(metric => dashboard?.ConfigureMetric(metric) ?? metric));
    }

    private static IEnumerable<HardwareMetric> BuildOverviewMetrics(NetworkAdapterDevice? device)
    {
        yield return Metric("network.active.adapter", "当前活动网卡", "Active Adapter", device?.Name, string.Empty, device?.Source, "当前展示的活动网卡。", true, 0);
        yield return Metric("network.ipv4", "IPv4 地址", "IPv4Address", JoinOrNull(device?.IPv4Addresses), string.Empty, device?.Source, "选中网卡 IPv4 地址。", true, 1);
        yield return Metric("network.gateway", "网关", "Gateway", device?.Gateway, string.Empty, device?.Source, "默认网关。", true, 2);
        yield return Metric("network.download.speed", "下载速度", "Download Speed", device?.DownloadSpeed, "B/s", device?.Source, "当前下载速度。", true, 3);
        yield return Metric("network.upload.speed", "上传速度", "Upload Speed", device?.UploadSpeed, "B/s", device?.Source, "当前上传速度。", true, 4);
        yield return Metric("network.link.speed", "连接速度", "Link Speed", device?.LinkSpeed, "bps", device?.Source, "网卡链路速度。", true, 5);
        yield return Metric("network.utilization", "网络利用率", "Network Utilization", device?.Utilization, "%", device?.Source, "基于吞吐和链路速度计算的网络利用率。", true, 6);
    }

    private static IEnumerable<HardwareMetric> BuildAdapterMetrics(NetworkAdapterDevice device)
    {
        yield return Metric("network.adapter.type", "类型", "Interface Type", ViewModelHelpers.ResolveNetworkType(device), string.Empty, device.Source, "有线、无线、虚拟或 VPN。", true, 10, device.Id);
        yield return Metric("network.adapter.status", "状态", "Operational Status", device.IsUp ? "已连接" : "未连接", string.Empty, device.Source, "网卡连接状态。", true, 11, device.Id);
        yield return Metric("network.adapter.ipv4", "IPv4", "IPv4Address", JoinOrNull(device.IPv4Addresses), string.Empty, device.Source, "网卡 IPv4 地址。", true, 12, device.Id);
        yield return Metric("network.mac", "MAC 地址", "MACAddress", device.MacAddress, string.Empty, device.Source, "MAC 地址，默认隐藏。", false, 13, device.Id, false);
        yield return Metric("network.adapter.link.speed", "链路速度", "Link Speed", device.LinkSpeed, "bps", device.Source, "链路速度。", true, 14, device.Id);
        yield return Metric("network.adapter.download.speed", "下载", "Download Speed", device.DownloadSpeed, "B/s", device.Source, "实时下载速度。", true, 15, device.Id);
        yield return Metric("network.adapter.upload.speed", "上传", "Upload Speed", device.UploadSpeed, "B/s", device.Source, "实时上传速度。", true, 16, device.Id);
    }

    private static IEnumerable<HardwareMetric> BuildProfessionalMetrics(NetworkAdapterDevice? device)
    {
        yield return Metric("network.dns", "DNS", "DNSServerSearchOrder", JoinOrNull(device?.DnsServers), string.Empty, device?.Source, "DNS 服务器。", false, 30);
        yield return Metric("network.dhcp", "DHCP", "DHCPEnabled", device?.DhcpEnabled is null ? null : (device.DhcpEnabled.Value ? "已启用" : "未启用"), string.Empty, device?.Source, "是否启用 DHCP。", false, 31);
        yield return Metric("network.ipv6", "IPv6", "IPv6Address", JoinOrNull(device?.IPv6Addresses), string.Empty, device?.Source, "IPv6 地址。", false, 32, visible: false);
        yield return Metric("network.interface.description", "接口描述", "Interface Description", device?.Description, string.Empty, device?.Source, "驱动或系统报告的接口描述。", false, 33);
        yield return Metric("network.total.uploaded", "总上传", "Total Uploaded", device?.TotalUploaded, "B", device?.Source, "累计上传字节数。", false, 34);
        yield return Metric("network.total.downloaded", "总下载", "Total Downloaded", device?.TotalDownloaded, "B", device?.Source, "累计下载字节数。", false, 35);
        yield return Metric("network.selected.utilization", "网络利用率", "Network Utilization", device?.Utilization, "%", device?.Source, "选中网卡实时利用率。", false, 36);
        yield return Metric("network.signal.quality", "信号质量", "Signal Quality", device?.SignalQuality, "%", device?.Source, "无线信号质量。", false, 37, visible: false);
        yield return Metric("network.source", "数据来源", "Source", device?.Source, string.Empty, "HardwareVision", "合并后的网卡数据来源。", false, 38);
        yield return Metric("network.availability", "可用性", "Availability", device?.Availability.ToString(), string.Empty, "HardwareVision", "网卡数据可用性。", false, 39);
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, object? value, string unit, string? source, string description, bool important, int order, string? hardwareId = null, bool visible = true)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        return HardwareMetricService.FromValue(id, hardwareId ?? "network", HardwareMetricCategory.Network, displayName, technicalName, textValue, unit, ViewModelHelpers.FirstAvailable(source, "System.Net.NetworkInformation")!, string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, "网络");
    }

    private static string? JoinOrNull(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        string[] filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return filtered.Length == 0 ? null : string.Join(", ", filtered);
    }
}
