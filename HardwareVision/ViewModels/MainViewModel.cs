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

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
    private readonly Dispatcher dispatcher;
    private NavigationItemViewModel? currentNavigationItem;
    private object? currentPage;
    private string currentPageTitle = "首页";
    private string currentPageSubtitle = "整机状态摘要";
    private string statusText = "正在读取硬件信息";
    private string footerText = "HardwareVision";
    private bool isDisposed;

    public MainViewModel(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IStartupService startupService,
        Dispatcher dispatcher,
        SensorDiagnosticService sensorDiagnosticService)
    {
        this.settings = settings;
        this.settingsService = settingsService;
        this.dispatcher = dispatcher;

        Dashboard = new DashboardViewModel(settings, hardwareInfoService, pollingService, settingsService, dispatcher);
        AdvancedSensors = new AdvancedSensorsViewModel(Dashboard, dispatcher);
        Cpu = new CpuViewModel(Dashboard);
        Gpu = new GpuViewModel(Dashboard, settings, settingsService);
        Memory = new MemoryViewModel(Dashboard);
        Disk = new DiskViewModel(Dashboard);
        Network = new NetworkViewModel(Dashboard, settings, settingsService);
        Motherboard = new MotherboardViewModel(Dashboard);
        MetricVisibility = new MetricVisibilityViewModel(settings, settingsService, Dashboard, dispatcher);
        Settings = new SettingsViewModel(
            settings,
            settingsService,
            startupService,
            pollingService,
            sensorDiagnosticService,
            dispatcher,
            ShowMetricVisibilityPage);

        Dashboard.PropertyChanged += OnDashboardPropertyChanged;

        NavigationItems.Add(new NavigationItemViewModel("Dashboard", "首页", "硬件摘要", Dashboard));
        NavigationItems.Add(new NavigationItemViewModel("Cpu", "CPU", "处理器指标", Cpu));
        NavigationItems.Add(new NavigationItemViewModel("Gpu", "GPU", "显卡指标", Gpu));
        NavigationItems.Add(new NavigationItemViewModel("Memory", "内存", "容量与模块", Memory));
        NavigationItems.Add(new NavigationItemViewModel("Disk", "硬盘", "存储与健康", Disk));
        NavigationItems.Add(new NavigationItemViewModel("Network", "网络", "网卡与吞吐", Network));
        NavigationItems.Add(new NavigationItemViewModel("Motherboard", "主板", "主板与固件", Motherboard));
        NavigationItems.Add(new NavigationItemViewModel("AdvancedSensors", "高级传感器", "传感器列表", AdvancedSensors));
        NavigationItems.Add(new NavigationItemViewModel("Settings", "设置", "启动与诊断", Settings));

        NavigateCommand = new RelayCommand<NavigationItemViewModel?>(Navigate);
        Navigate(NavigationItems.FirstOrDefault(item => item.Key == "Dashboard") ?? NavigationItems[0]);
        _ = RefreshHardwareInfoAsync();
    }

    public string ApplicationName => "HardwareVision";

    public DashboardViewModel Dashboard { get; }

    public AdvancedSensorsViewModel AdvancedSensors { get; }

    public CpuViewModel Cpu { get; }

    public GpuViewModel Gpu { get; }

    public MemoryViewModel Memory { get; }

    public DiskViewModel Disk { get; }

    public NetworkViewModel Network { get; }

    public MotherboardViewModel Motherboard { get; }

    public MetricVisibilityViewModel MetricVisibility { get; }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

    public IRelayCommand<NavigationItemViewModel?> NavigateCommand { get; }

    public object? CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    public string CurrentPageTitle
    {
        get => currentPageTitle;
        private set => SetProperty(ref currentPageTitle, value);
    }

    public string CurrentPageSubtitle
    {
        get => currentPageSubtitle;
        private set => SetProperty(ref currentPageSubtitle, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string FooterText
    {
        get => footerText;
        private set => SetProperty(ref footerText, value);
    }

    public Task RefreshHardwareInfoAsync()
    {
        return Dashboard.RefreshHardwareInfoAsync();
    }

    public void ShowSettingsPage()
    {
        Navigate(NavigationItems.FirstOrDefault(item => item.Key == "Settings"));
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        Dashboard.Dispose();
        AdvancedSensors.Dispose();
        Cpu.Dispose();
        Gpu.Dispose();
        Memory.Dispose();
        Disk.Dispose();
        Network.Dispose();
        Motherboard.Dispose();
        Settings.Dispose();
        isDisposed = true;
    }

    private void ShowMetricVisibilityPage()
    {
        Navigate(NavigationItems.FirstOrDefault(item => item.Key == "MetricVisibility"));
    }

    private void Navigate(NavigationItemViewModel? item)
    {
        if (item is null || !item.IsEnabled)
        {
            return;
        }

        if (currentNavigationItem is not null)
        {
            SetPageActive(currentNavigationItem.Page, false);
        }

        currentNavigationItem = item;
        CurrentPage = item.Page;
        CurrentPageTitle = item.Title;
        CurrentPageSubtitle = item.Subtitle;
        StatusText = Dashboard.LoadMessage;
        settings.LastSelectedPage = item.Key;
        _ = settingsService.UpdateAsync(updated => updated.LastSelectedPage = item.Key);
        SetPageActive(item.Page, true);
    }

    private static void SetPageActive(object page, bool active)
    {
        switch (page)
        {
            case DashboardViewModel dashboard:
                dashboard.SetSummaryActive(active);
                break;
            case AdvancedSensorsViewModel advancedSensors:
                advancedSensors.SetActive(active);
                break;
            case CpuViewModel cpu:
                cpu.SetActive(active);
                break;
            case GpuViewModel gpu:
                gpu.SetActive(active);
                break;
            case MemoryViewModel memory:
                memory.SetActive(active);
                break;
            case DiskViewModel disk:
                disk.SetActive(active);
                break;
            case NetworkViewModel network:
                network.SetActive(active);
                break;
            case MotherboardViewModel motherboard:
                motherboard.SetActive(active);
                break;
            case MetricVisibilityViewModel visibility:
                visibility.SetActive(active);
                break;
        }
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.LoadMessage) or nameof(DashboardViewModel.LastRefreshTime))
        {
            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                StatusText = Dashboard.LoadMessage;
                FooterText = $"最后刷新：{Dashboard.LastRefreshTime}";
            });
        }
    }
}
