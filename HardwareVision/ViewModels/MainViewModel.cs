using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;

namespace HardwareVision.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
    private readonly IStartupService startupService;
    private readonly PollingService pollingService;
    private readonly SensorDiagnosticService sensorDiagnosticService;
    private readonly IForegroundProcessTracker foregroundProcessTracker;
    private readonly ISensorHistoryService sensorHistoryService;
    private readonly IGameSessionRecorder gameSessionRecorder;
    private readonly IGameEnergyTracker? gameEnergyTracker;
    private readonly IGamePerformanceLimitTracker? gamePerformanceLimitTracker;
    private readonly IHardwareRefreshService? hardwareRefreshService;
    private readonly Dispatcher dispatcher;
    private readonly NavigationItemViewModel metricVisibilityNavigationItem;
    private NavigationItemViewModel? currentNavigationItem;
    private AdvancedSensorsViewModel? advancedSensors;
    private CpuViewModel? cpu;
    private GpuViewModel? gpu;
    private MemoryViewModel? memory;
    private DiskViewModel? disk;
    private NetworkViewModel? network;
    private MotherboardViewModel? motherboard;
    private GamePerformanceViewModel? gamePerformance;
    private MetricVisibilityViewModel? metricVisibility;
    private SettingsViewModel? settingsViewModel;
    private object? currentPage;
    private string currentPageTitle = "首页";
    private string currentPageSubtitle = "整机状态摘要";
    private string statusText = "正在读取硬件信息";
    private string footerText = "HardwareVision";
    private bool isWindowVisible = true;
    private bool isDisposed;

    public MainViewModel(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IStartupService startupService,
        Dispatcher dispatcher,
        SensorDiagnosticService sensorDiagnosticService,
        IForegroundProcessTracker foregroundProcessTracker,
        ISensorHistoryService sensorHistoryService,
        IGameSessionRecorder gameSessionRecorder,
        IGameEnergyTracker? gameEnergyTracker = null,
        IGamePerformanceLimitTracker? gamePerformanceLimitTracker = null,
        IHardwareRefreshService? hardwareRefreshService = null)
    {
        this.settings = settings;
        this.settingsService = settingsService;
        this.startupService = startupService;
        this.pollingService = pollingService;
        this.dispatcher = dispatcher;
        this.sensorDiagnosticService = sensorDiagnosticService;
        this.foregroundProcessTracker = foregroundProcessTracker;
        this.sensorHistoryService = sensorHistoryService;
        this.gameSessionRecorder = gameSessionRecorder;
        this.gameEnergyTracker = gameEnergyTracker;
        this.gamePerformanceLimitTracker = gamePerformanceLimitTracker;
        this.hardwareRefreshService = hardwareRefreshService;

        Dashboard = new DashboardViewModel(
            settings,
            hardwareInfoService,
            pollingService,
            settingsService,
            dispatcher,
            sensorHistoryService,
            hardwareRefreshService);
        Dashboard.PropertyChanged += OnDashboardPropertyChanged;

        NavigationItems.Add(new NavigationItemViewModel("Dashboard", "首页", "硬件摘要", Dashboard));
        NavigationItems.Add(new NavigationItemViewModel("Cpu", "CPU", "处理器指标", () => Cpu));
        NavigationItems.Add(new NavigationItemViewModel("Gpu", "GPU", "显卡指标", () => Gpu));
        NavigationItems.Add(new NavigationItemViewModel("Memory", "内存", "容量与内存模组", () => Memory));
        NavigationItems.Add(new NavigationItemViewModel("Disk", "硬盘", "存储与健康", () => Disk));
        NavigationItems.Add(new NavigationItemViewModel("Network", "网络", "网络适配器与流量", () => Network));
        NavigationItems.Add(new NavigationItemViewModel("Motherboard", "主板", "主板与固件", () => Motherboard));
        NavigationItems.Add(new NavigationItemViewModel("GamePerformance", "游戏", "帧率与延迟", () => GamePerformance));
        NavigationItems.Add(new NavigationItemViewModel("AdvancedSensors", "高级传感器", "传感器列表", () => AdvancedSensors));
        NavigationItems.Add(new NavigationItemViewModel("Settings", "设置", "应用设置与诊断", () => Settings));
        metricVisibilityNavigationItem = new NavigationItemViewModel(
            "MetricVisibility",
            "显示项管理",
            "指标显示与排序",
            () => MetricVisibility);

        NavigateCommand = new RelayCommand<NavigationItemViewModel?>(Navigate);
        Navigate(NavigationItems[0]);
    }

    public string ApplicationName => "HardwareVision";

    public DashboardViewModel Dashboard { get; }

    public AdvancedSensorsViewModel AdvancedSensors => advancedSensors ??= new AdvancedSensorsViewModel(Dashboard, dispatcher);

    public CpuViewModel Cpu => cpu ??= new CpuViewModel(Dashboard, sensorHistoryService);

    public GpuViewModel Gpu => gpu ??= new GpuViewModel(Dashboard, settings, settingsService, sensorHistoryService);

    public MemoryViewModel Memory => memory ??= new MemoryViewModel(Dashboard);

    public DiskViewModel Disk => disk ??= new DiskViewModel(Dashboard);

    public NetworkViewModel Network => network ??= new NetworkViewModel(Dashboard, settings, settingsService);

    public MotherboardViewModel Motherboard => motherboard ??= new MotherboardViewModel(Dashboard);

    public GamePerformanceViewModel GamePerformance => gamePerformance ??= new GamePerformanceViewModel(
        new PresentMonGamePerformanceService(
            gameSessionRecorder,
            () => settings.RecordGameSessions,
            gameEnergyTracker,
            gamePerformanceLimitTracker,
            () => GameSessionHardwareMetadataFactory.Create(Dashboard.CurrentSnapshot, Dashboard.DiskDevices)),
        dispatcher,
        foregroundProcessTracker,
        gameSessionRecorder,
        settings,
        settingsService,
        gameEnergyTracker,
        gamePerformanceLimitTracker);

    public MetricVisibilityViewModel MetricVisibility => metricVisibility ??= new MetricVisibilityViewModel(
        settings,
        settingsService,
        Dashboard,
        dispatcher);

    public SettingsViewModel Settings => settingsViewModel ??= new SettingsViewModel(
        settings,
        settingsService,
        startupService,
        pollingService,
        sensorDiagnosticService,
        dispatcher,
        ShowMetricVisibilityPage,
        gameSessionRecorder,
        hardwareRefreshService);

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

    public Task RefreshHardwareInfoAsync(HardwareRefreshReason reason = HardwareRefreshReason.ManualSettings) =>
        Dashboard.RefreshHardwareInfoAsync(reason);

    public void ShowSettingsPage()
    {
        Navigate(NavigationItems.FirstOrDefault(item => item.Key == "Settings"));
    }

    public void ApplyStartupState(bool enabled)
    {
        settings.AutoStartEnabled = enabled;
        settingsViewModel?.ApplyStartupState(enabled);
    }

    public void SetWindowVisible(bool visible)
    {
        if (isDisposed || isWindowVisible == visible)
        {
            return;
        }

        isWindowVisible = visible;
        if (currentNavigationItem?.CreatedPage is object page)
        {
            SetPageActive(page, visible);
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        if (currentNavigationItem?.CreatedPage is object page)
        {
            SetPageActive(page, false);
        }

        Dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        Dashboard.Dispose();
        advancedSensors?.Dispose();
        cpu?.Dispose();
        gpu?.Dispose();
        memory?.Dispose();
        disk?.Dispose();
        network?.Dispose();
        motherboard?.Dispose();
        gamePerformance?.Dispose();
        settingsViewModel?.Dispose();
    }

    private void ShowMetricVisibilityPage()
    {
        Navigate(metricVisibilityNavigationItem);
    }

    private void Navigate(NavigationItemViewModel? item)
    {
        if (item is null || !item.IsEnabled || isDisposed)
        {
            return;
        }

        if (currentNavigationItem?.CreatedPage is object previousPage)
        {
            SetPageActive(previousPage, false);
        }

        currentNavigationItem = item;
        object page = item.Page;
        CurrentPage = page;
        CurrentPageTitle = item.Title;
        CurrentPageSubtitle = item.Subtitle;
        StatusText = Dashboard.LoadMessage;
        settings.LastSelectedPage = item.Key;
        _ = settingsService.UpdateAsync(updated => updated.LastSelectedPage = item.Key);
        if (isWindowVisible)
        {
            SetPageActive(page, true);
        }
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
            case GamePerformanceViewModel game:
                game.SetActive(active);
                break;
            case MetricVisibilityViewModel visibility:
                visibility.SetActive(active);
                break;
            case SettingsViewModel settings:
                settings.SetActive(active);
                break;
        }
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.LoadMessage) or nameof(DashboardViewModel.LastRefreshTime))
        {
            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                if (isDisposed)
                {
                    return;
                }

                StatusText = Dashboard.LoadMessage;
                FooterText = $"最后刷新：{Dashboard.LastRefreshTime}";
            });
        }
    }
}
