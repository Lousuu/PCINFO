using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
    private readonly IThemeService themeService;
    private readonly IMotionService motionService;
    private readonly IThemeTransitionService themeTransitionService;
    private readonly INavigationTransitionService navigationTransitionService;
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
    private NavigationRouteDescriptor? currentNavigationRoute;
    private PendingNavigation? pendingNavigation;
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
    private AppTheme currentTheme;
    private ThemeTransitionSnapshot themeTransition = ThemeTransitionSnapshot.Idle(AppTheme.Classic);
    private NavigationTransitionSnapshot navigationTransition = NavigationTransitionSnapshot.Idle();
    private MotionLevel requestedMotionLevel;
    private MotionLevel effectiveMotionLevel;
    private MotionProfile currentMotionProfile;
    private string currentPageCode = "01";
    private string currentPageTitle = "首页";
    private string currentPageSubtitle = "整机状态摘要";
    private string statusText = "正在读取硬件信息";
    private string footerText = "HardwareVision";
    private bool isWindowVisible = true;
    private bool isWindowMinimized;
    private bool isWindowClosing;
    private bool isInitialNavigation = true;
    private bool isDisposed;

    internal MainViewModel(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IThemeService themeService,
        IMotionService motionService,
        IStartupService startupService,
        Dispatcher dispatcher,
        SensorDiagnosticService sensorDiagnosticService,
        IForegroundProcessTracker foregroundProcessTracker,
        ISensorHistoryService sensorHistoryService,
        IGameSessionRecorder gameSessionRecorder,
        IGameEnergyTracker? gameEnergyTracker = null,
        IGamePerformanceLimitTracker? gamePerformanceLimitTracker = null,
        IHardwareRefreshService? hardwareRefreshService = null)
        : this(
            settings,
            hardwareInfoService,
            pollingService,
            settingsService,
            themeService,
            motionService,
            new ThemeTransitionService(themeService, motionService, dispatcher, new ImmediateThemeTransitionClock()),
            new NavigationTransitionService(),
            startupService,
            dispatcher,
            sensorDiagnosticService,
            foregroundProcessTracker,
            sensorHistoryService,
            gameSessionRecorder,
            gameEnergyTracker,
            gamePerformanceLimitTracker,
            hardwareRefreshService)
    {
    }

    public MainViewModel(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IThemeService themeService,
        IMotionService motionService,
        IThemeTransitionService themeTransitionService,
        IStartupService startupService,
        Dispatcher dispatcher,
        SensorDiagnosticService sensorDiagnosticService,
        IForegroundProcessTracker foregroundProcessTracker,
        ISensorHistoryService sensorHistoryService,
        IGameSessionRecorder gameSessionRecorder,
        IGameEnergyTracker? gameEnergyTracker = null,
        IGamePerformanceLimitTracker? gamePerformanceLimitTracker = null,
        IHardwareRefreshService? hardwareRefreshService = null)
        : this(
            settings,
            hardwareInfoService,
            pollingService,
            settingsService,
            themeService,
            motionService,
            themeTransitionService,
            new NavigationTransitionService(),
            startupService,
            dispatcher,
            sensorDiagnosticService,
            foregroundProcessTracker,
            sensorHistoryService,
            gameSessionRecorder,
            gameEnergyTracker,
            gamePerformanceLimitTracker,
            hardwareRefreshService)
    {
    }

    public MainViewModel(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        IThemeService themeService,
        IMotionService motionService,
        IThemeTransitionService themeTransitionService,
        INavigationTransitionService navigationTransitionService,
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
        this.themeService = themeService;
        this.motionService = motionService;
        this.themeTransitionService = themeTransitionService;
        this.navigationTransitionService = navigationTransitionService;
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
        currentTheme = themeService.CurrentTheme;
        themeTransition = themeTransitionService.Current;
        navigationTransition = navigationTransitionService.CurrentSnapshot;
        currentMotionProfile = motionService.CurrentProfile;
        requestedMotionLevel = motionService.RequestedLevel;
        effectiveMotionLevel = motionService.EffectiveLevel;

        Dashboard = new DashboardViewModel(
            settings,
            hardwareInfoService,
            pollingService,
            settingsService,
            dispatcher,
            sensorHistoryService,
            hardwareRefreshService);
        Dashboard.PropertyChanged += OnDashboardPropertyChanged;

        NavigationItems.Add(new NavigationItemViewModel("Dashboard", "01", "首页", "硬件摘要", Dashboard));
        NavigationItems.Add(new NavigationItemViewModel("Cpu", "02", "CPU", "处理器指标", () => Cpu));
        NavigationItems.Add(new NavigationItemViewModel("Gpu", "03", "GPU", "显卡指标", () => Gpu));
        NavigationItems.Add(new NavigationItemViewModel("Memory", "04", "内存", "容量与内存模组", () => Memory));
        NavigationItems.Add(new NavigationItemViewModel("Disk", "05", "硬盘", "存储与健康", () => Disk));
        NavigationItems.Add(new NavigationItemViewModel("Network", "06", "网络", "网络适配器与流量", () => Network));
        NavigationItems.Add(new NavigationItemViewModel("Motherboard", "07", "主板", "主板与固件", () => Motherboard));
        NavigationItems.Add(new NavigationItemViewModel("GamePerformance", "08", "游戏", "帧率与延迟", () => GamePerformance));
        NavigationItems.Add(new NavigationItemViewModel("AdvancedSensors", "09", "高级传感器", "传感器列表", () => AdvancedSensors));
        NavigationItems.Add(new NavigationItemViewModel("Settings", "10", "设置", "应用设置与诊断", () => Settings));
        metricVisibilityNavigationItem = new NavigationItemViewModel(
            "MetricVisibility",
            "MX",
            "显示项管理",
            "指标显示与排序",
            () => MetricVisibility);

        NavigateCommand = new RelayCommand<NavigationItemViewModel?>(Navigate);
        Navigate(NavigationItems[0]);
        themeService.ThemeChanged += OnThemeChanged;
        motionService.MotionChanged += OnMotionChanged;
        themeTransitionService.TransitionChanged += OnThemeTransitionChanged;
        navigationTransitionService.TransitionChanged += OnNavigationTransitionChanged;
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
        gamePerformanceLimitTracker,
        reportNavigationCoordinator: CoordinateReportNavigationAsync);

    public MetricVisibilityViewModel MetricVisibility => metricVisibility ??= new MetricVisibilityViewModel(
        settings,
        settingsService,
        Dashboard,
        dispatcher);

    public SettingsViewModel Settings => settingsViewModel ??= new SettingsViewModel(
        settings,
        settingsService,
        themeService,
        motionService,
        themeTransitionService,
        startupService,
        pollingService,
        sensorDiagnosticService,
        dispatcher,
        ShowMetricVisibilityPage,
        gameSessionRecorder,
        hardwareRefreshService,
        PrepareForThemeTransitionAsync);

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = new();

    public IRelayCommand<NavigationItemViewModel?> NavigateCommand { get; }

    public object? CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    public AppTheme CurrentTheme
    {
        get => currentTheme;
        private set
        {
            if (SetProperty(ref currentTheme, value))
            {
                OnPropertyChanged(nameof(IsClassicTheme));
                OnPropertyChanged(nameof(IsTraceworkTheme));
            }
        }
    }

    public bool IsClassicTheme => CurrentTheme == AppTheme.Classic;

    public bool IsTraceworkTheme => CurrentTheme == AppTheme.Tracework;

    public ThemeTransitionSnapshot ThemeTransition
    {
        get => themeTransition;
        private set
        {
            if (SetProperty(ref themeTransition, value))
            {
                OnPropertyChanged(nameof(IsThemeTransitionActive));
                OnPropertyChanged(nameof(IsThemeInteractionBlocked));
                OnPropertyChanged(nameof(ThemeTransitionPhase));
                OnPropertyChanged(nameof(ThemeTransitionSource));
                OnPropertyChanged(nameof(ThemeTransitionTarget));
            }
        }
    }

    public bool IsThemeTransitionActive => ThemeTransition.IsActive;

    public bool IsThemeInteractionBlocked => ThemeTransition.IsInteractionBlocked;

    public ThemeTransitionPhase ThemeTransitionPhase => ThemeTransition.Phase;

    public AppTheme ThemeTransitionSource => ThemeTransition.SourceTheme;

    public AppTheme ThemeTransitionTarget => ThemeTransition.TargetTheme;

    public NavigationTransitionSnapshot NavigationTransition
    {
        get => navigationTransition;
        private set
        {
            if (SetProperty(ref navigationTransition, value))
            {
                OnPropertyChanged(nameof(IsNavigationTransitionActive));
                OnPropertyChanged(nameof(NavigationTransitionPhase));
            }
        }
    }

    public bool IsNavigationTransitionActive => NavigationTransition.IsActive;

    public NavigationTransitionPhase NavigationTransitionPhase => NavigationTransition.Phase;

    public MotionLevel RequestedMotionLevel
    {
        get => requestedMotionLevel;
        private set => SetProperty(ref requestedMotionLevel, value);
    }

    public MotionLevel EffectiveMotionLevel
    {
        get => effectiveMotionLevel;
        private set => SetProperty(ref effectiveMotionLevel, value);
    }

    public MotionProfile CurrentMotionProfile
    {
        get => currentMotionProfile;
        private set
        {
            if (SetProperty(ref currentMotionProfile, value))
            {
                OnPropertyChanged(nameof(IsMotionEnabled));
                OnPropertyChanged(nameof(AllowsSpatialMotion));
            }
        }
    }

    public bool IsMotionEnabled => CurrentMotionProfile.IsAnimationEnabled;

    public bool AllowsSpatialMotion => CurrentMotionProfile.AllowsSpatialMotion;

    public string CurrentPageCode
    {
        get => currentPageCode;
        private set => SetProperty(ref currentPageCode, value);
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
        if (!visible)
        {
            CompletePendingNavigationImmediately();
        }

        if (currentNavigationItem?.CreatedPage is object page)
        {
            SetPageActive(page, visible);
        }
    }

    public void SetWindowMinimized(bool minimized)
    {
        if (isDisposed || isWindowMinimized == minimized)
        {
            return;
        }

        isWindowMinimized = minimized;
        if (minimized)
        {
            CompletePendingNavigationImmediately();
        }
    }

    public void SetWindowClosing()
    {
        if (isDisposed || isWindowClosing)
        {
            return;
        }

        isWindowClosing = true;
        pendingNavigation = null;
        navigationTransitionService.Cancel();
    }

    public void NotifyShellUnloaded()
    {
        if (!isDisposed && !isWindowClosing)
        {
            CompletePendingNavigationImmediately();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        pendingNavigation = null;
        navigationTransitionService.Cancel();
        if (currentNavigationItem?.CreatedPage is object page)
        {
            SetPageActive(page, false);
        }

        Dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        themeService.ThemeChanged -= OnThemeChanged;
        motionService.MotionChanged -= OnMotionChanged;
        themeTransitionService.TransitionChanged -= OnThemeTransitionChanged;
        navigationTransitionService.TransitionChanged -= OnNavigationTransitionChanged;
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

        RequestNavigation(item);
    }

    private void RequestNavigation(NavigationItemViewModel item)
    {
        if (ReferenceEquals(currentNavigationItem, item)
            && pendingNavigation is null)
        {
            item.IsSelected = true;
            return;
        }

        if (!TryGetRoute(item, out NavigationRouteDescriptor? target)
            || target is null
            || currentNavigationRoute is null
            || !ShouldUseFlowRelay(target))
        {
            navigationTransitionService.Cancel();
            pendingNavigation = null;
            CommitNavigation(item, target);
            return;
        }

        if (pendingNavigation is { } active
            && active.Target.PageKey == target.PageKey
            && active.Task is { IsCompleted: false })
        {
            return;
        }

        NavigationTransitionIntent intent = new(currentNavigationRoute, target, currentMotionProfile);
        PendingNavigation pending = CreatePendingNavigation(
            target,
            cancellationToken => InvokeOnDispatcherAsync(
                () => CommitNavigation(item, target),
                cancellationToken));
        pendingNavigation = pending;
        pending.Task = navigationTransitionService.NavigateAsync(intent, pending.CommitAsync);
        ObserveNavigationTask(pending.Task, $"navigate-{target.PageKey}");
    }

    private void CommitNavigation(NavigationItemViewModel item, NavigationRouteDescriptor? target)
    {
        if (isDisposed || ReferenceEquals(currentNavigationItem, item))
        {
            if (target is not null)
            {
                currentNavigationRoute = target;
            }
            return;
        }

        if (currentNavigationItem?.CreatedPage is object previousPage)
        {
            SetPageActive(previousPage, false);
        }

        if (currentNavigationItem is not null)
        {
            currentNavigationItem.IsSelected = false;
        }

        object page = item.Page;
        currentNavigationItem = item;
        item.IsSelected = true;
        CurrentPage = page;
        CurrentPageCode = item.DisplayCode;
        CurrentPageTitle = item.Title;
        CurrentPageSubtitle = item.Subtitle;
        currentNavigationRoute = target;
        StatusText = Dashboard.LoadMessage;
        settings.LastSelectedPage = item.Key;
        _ = settingsService.UpdateAsync(updated => updated.LastSelectedPage = item.Key);
        if (isWindowVisible && !isWindowMinimized && !isWindowClosing)
        {
            SetPageActive(page, true);
        }

        isInitialNavigation = false;
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (!isDisposed)
            {
                CurrentTheme = e.CurrentTheme;
            }
        });
    }

    private void OnMotionChanged(object? sender, MotionChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (isDisposed)
            {
                return;
            }

            RequestedMotionLevel = e.CurrentRequestedLevel;
            EffectiveMotionLevel = e.CurrentEffectiveLevel;
            CurrentMotionProfile = e.CurrentProfile;
        });
    }

    private void OnThemeTransitionChanged(object? sender, ThemeTransitionChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (!isDisposed)
            {
                if (e.CurrentSnapshot.IsActive)
                {
                    CompletePendingNavigationImmediately();
                }

                ThemeTransition = e.CurrentSnapshot;
            }
        });
    }

    private void OnNavigationTransitionChanged(object? sender, NavigationTransitionChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (!isDisposed && e.CurrentSnapshot.Version >= NavigationTransition.Version)
            {
                NavigationTransition = e.CurrentSnapshot;
            }
        });
    }

    private bool ShouldUseFlowRelay(NavigationRouteDescriptor target)
    {
        return !isInitialNavigation
            && currentTheme == AppTheme.Tracework
            && currentMotionProfile.EffectiveLevel != MotionLevel.Off
            && currentNavigationRoute is not null
            && currentNavigationRoute.PageKey != target.PageKey
            && isWindowVisible
            && !isWindowMinimized
            && !isWindowClosing
            && !themeTransitionService.Current.IsActive
            && NavigationRouteDescriptor.ResolveDirection(currentNavigationRoute, target)
                != NavigationTransitionDirection.None;
    }

    private static bool TryGetRoute(
        NavigationItemViewModel item,
        out NavigationRouteDescriptor? descriptor) =>
        NavigationRouteDescriptor.TryCreate(
            item.Key,
            item.DisplayCode,
            item.Title,
            item.Subtitle,
            out descriptor);

    private async Task CoordinateReportNavigationAsync(bool opening, Func<Task> commitAsync)
    {
        NavigationRouteDescriptor origin = currentNavigationRoute
            ?? CreateGamePerformanceRoute();
        NavigationRouteDescriptor target = opening
            ? CreateGameReportRoute()
            : CreateGamePerformanceRoute();

        if (!ShouldUseFlowRelay(target))
        {
            navigationTransitionService.Cancel();
            pendingNavigation = null;
            await commitAsync();
            ApplyCommittedRoute(target);
            return;
        }

        if (pendingNavigation is { } active
            && active.Target.PageKey == target.PageKey
            && active.Task is { IsCompleted: false })
        {
            await active.Task;
            return;
        }

        NavigationTransitionIntent intent = new(origin, target, currentMotionProfile);
        PendingNavigation pending = CreatePendingNavigation(
            target,
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await InvokeOnDispatcherAsync(
                    () =>
                    {
                        commitAsync().GetAwaiter().GetResult();
                        ApplyCommittedRoute(target);
                    },
                    cancellationToken);
            });
        pendingNavigation = pending;
        pending.Task = navigationTransitionService.NavigateAsync(intent, pending.CommitAsync);
        await pending.Task;
    }

    private void ApplyCommittedRoute(NavigationRouteDescriptor target)
    {
        currentNavigationRoute = target;
        CurrentPageCode = target.Code;
        CurrentPageTitle = target.Title;
        CurrentPageSubtitle = target.Subtitle;
    }

    private static NavigationRouteDescriptor CreateGamePerformanceRoute() =>
        new("GamePerformance", "08", "游戏", "帧率与延迟", NavigationGroup.Session, 0);

    private static NavigationRouteDescriptor CreateGameReportRoute() =>
        new("GameSessionReport", "08R", "会话报告", "静态会话详情", NavigationGroup.Session, 1, true);

    private async Task PrepareForThemeTransitionAsync()
    {
        PendingNavigation? pending = pendingNavigation;
        navigationTransitionService.Cancel();
        if (pending is not null)
        {
            await pending.CommitAsync(CancellationToken.None);
        }
    }

    private void CompletePendingNavigationImmediately()
    {
        PendingNavigation? pending = pendingNavigation;
        navigationTransitionService.Cancel();
        if (pending is not null)
        {
            ObserveNavigationTask(
                pending.CommitAsync(CancellationToken.None),
                $"complete-{pending.Target.PageKey}");
        }
    }

    private PendingNavigation CreatePendingNavigation(
        NavigationRouteDescriptor target,
        Func<CancellationToken, Task> commitAsync)
    {
        PendingNavigation? pending = null;
        int commitStarted = 0;
        async Task CommitOnceAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref commitStarted, 1) != 0)
            {
                return;
            }

            await commitAsync(cancellationToken);
            if (ReferenceEquals(pendingNavigation, pending))
            {
                pendingNavigation = null;
            }
        }

        pending = new PendingNavigation(target, CommitOnceAsync);
        return pending;
    }

    private Task InvokeOnDispatcherAsync(Action action, CancellationToken cancellationToken)
    {
        if (dispatcher.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
            },
            DispatcherPriority.DataBind,
            cancellationToken).Task;
    }

    private static void ObserveNavigationTask(Task task, string operation)
    {
        _ = ObserveNavigationTaskAsync(task, operation);
    }

    private static async Task ObserveNavigationTaskAsync(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                $"FLOW RELAY operation failed: {operation}.",
                exception,
                $"flow-relay:{operation}:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
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

    private sealed class PendingNavigation(
        NavigationRouteDescriptor target,
        Func<CancellationToken, Task> commitAsync)
    {
        public NavigationRouteDescriptor Target { get; } = target;

        public Func<CancellationToken, Task> CommitAsync { get; } = commitAsync;

        public Task? Task { get; set; }
    }
}
