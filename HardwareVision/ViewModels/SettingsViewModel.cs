using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
    private readonly IThemeService themeService;
    private readonly IMotionService motionService;
    private readonly IThemeTransitionService themeTransitionService;
    private readonly IStartupService startupService;
    private readonly PollingService pollingService;
    private readonly SensorDiagnosticService sensorDiagnosticService;
    private readonly Dispatcher dispatcher;
    private readonly Action openMetricVisibility;
    private readonly IGameSessionRecorder gameSessionRecorder;
    private readonly IHardwareRefreshService? hardwareRefreshService;
    private CancellationTokenSource? directorySizeCancellation;
    private bool autoStartEnabled;
    private bool startMinimizedToTray;
    private bool closeToTray;
    private double refreshIntervalSeconds;
    private int backgroundRefreshIntervalSeconds;
    private ThemeDescriptor selectedTheme;
    private MotionLevelDescriptor selectedMotionLevel;
    private MotionLevel requestedMotionLevel;
    private MotionLevel effectiveMotionLevel;
    private string themeStatusText;
    private string motionStatusText;
    private long themeChangeVersion;
    private string currentStage = "Ready";
    private string lastSelectedPage;
    private string sensorIntegrationMessage = "--";
    private string cpuTemperatureSource = "--";
    private string cpuTemperatureAvailability = "--";
    private string cpuTemperatureDetectedButNull = "--";
    private bool recordGameSessions;
    private string gameSessionDirectorySizeText = "正在计算";
    private bool autoRefreshHardwareOnDeviceChange;
    private bool isHardwareScanning;
    private string hardwareScanStatusText = "等待";
    private string lastHardwareScanTimeText = "尚未扫描";
    private bool isActive;
    private bool isDisposed;

    internal SettingsViewModel(
        AppSettings settings,
        ISettingsService settingsService,
        IThemeService themeService,
        IMotionService motionService,
        IStartupService startupService,
        PollingService pollingService,
        SensorDiagnosticService sensorDiagnosticService,
        Dispatcher dispatcher,
        Action openMetricVisibility,
        IGameSessionRecorder gameSessionRecorder,
        IHardwareRefreshService? hardwareRefreshService = null)
        : this(
            settings,
            settingsService,
            themeService,
            motionService,
            new ThemeTransitionService(themeService, motionService, dispatcher, new ImmediateThemeTransitionClock()),
            startupService,
            pollingService,
            sensorDiagnosticService,
            dispatcher,
            openMetricVisibility,
            gameSessionRecorder,
            hardwareRefreshService)
    {
    }

    public SettingsViewModel(
        AppSettings settings,
        ISettingsService settingsService,
        IThemeService themeService,
        IMotionService motionService,
        IThemeTransitionService themeTransitionService,
        IStartupService startupService,
        PollingService pollingService,
        SensorDiagnosticService sensorDiagnosticService,
        Dispatcher dispatcher,
        Action openMetricVisibility,
        IGameSessionRecorder gameSessionRecorder,
        IHardwareRefreshService? hardwareRefreshService = null)
    {
        this.settings = settings;
        this.settingsService = settingsService;
        this.themeService = themeService;
        this.motionService = motionService;
        this.themeTransitionService = themeTransitionService;
        this.startupService = startupService;
        this.pollingService = pollingService;
        this.sensorDiagnosticService = sensorDiagnosticService;
        this.dispatcher = dispatcher;
        this.openMetricVisibility = openMetricVisibility;
        this.gameSessionRecorder = gameSessionRecorder;
        this.hardwareRefreshService = hardwareRefreshService;

        pollingService.UpdateIntervals(settings.RefreshIntervalSeconds, settings.BackgroundRefreshIntervalSeconds);

        autoStartEnabled = settings.AutoStartEnabled;
        startMinimizedToTray = settings.StartMinimizedToTray;
        closeToTray = settings.CloseToTray;
        refreshIntervalSeconds = settings.RefreshIntervalSeconds;
        backgroundRefreshIntervalSeconds = settings.BackgroundRefreshIntervalSeconds;
        selectedTheme = themeService.AvailableThemes.Single(item => item.Theme == themeService.CurrentTheme);
        requestedMotionLevel = motionService.RequestedLevel;
        effectiveMotionLevel = motionService.EffectiveLevel;
        selectedMotionLevel = MotionOptions.Single(item => item.Level == requestedMotionLevel);
        motionStatusText = BuildMotionStatusText(motionService.CurrentProfile);
        themeStatusText = $"当前主题：{selectedTheme.DisplayName}";
        lastSelectedPage = settings.LastSelectedPage;
        recordGameSessions = settings.RecordGameSessions;
        autoRefreshHardwareOnDeviceChange = settings.AutoRefreshHardwareOnDeviceChange;

        IncreaseRefreshIntervalCommand = new RelayCommand(() => RefreshIntervalSeconds += 0.5d);
        DecreaseRefreshIntervalCommand = new RelayCommand(() => RefreshIntervalSeconds -= 0.5d);
        IncreaseBackgroundRefreshIntervalCommand = new RelayCommand(() => BackgroundRefreshIntervalSeconds++);
        DecreaseBackgroundRefreshIntervalCommand = new RelayCommand(() => BackgroundRefreshIntervalSeconds--);
        SelectThemeCommand = new AsyncRelayCommand<ThemeDescriptor?>(SelectThemeAsync, CanSelectTheme);
        SelectMotionLevelCommand = new RelayCommand<MotionLevelDescriptor?>(SelectMotionLevel);
        ExportSensorDiagnosticsCommand = new AsyncRelayCommand(ExportSensorDiagnosticsAsync);
        ExportOfficialComparisonDiagnosticsCommand = new AsyncRelayCommand(ExportOfficialComparisonDiagnosticsAsync);
        OpenMetricVisibilityCommand = new RelayCommand(openMetricVisibility);
        OpenGameSessionDirectoryCommand = new RelayCommand(OpenGameSessionDirectory);
        RecalculateGameSessionDirectorySizeCommand = new AsyncRelayCommand(
            () => RefreshGameSessionDirectorySizeAsync(force: true));
        RescanHardwareCommand = new AsyncRelayCommand(RescanHardwareAsync, () => !IsHardwareScanning);
        if (hardwareRefreshService is not null)
        {
            hardwareRefreshService.StatusChanged += OnHardwareRefreshStatusChanged;
        }
        motionService.MotionChanged += OnMotionChanged;
        themeTransitionService.TransitionChanged += OnThemeTransitionChanged;

        RefreshDiagnosticsText();
    }

    public bool AutoStartEnabled
    {
        get => autoStartEnabled;
        set
        {
            if (SetProperty(ref autoStartEnabled, value))
            {
                settings.AutoStartEnabled = value;
                try
                {
                    startupService.SetEnabled(value);
                }
                catch (Exception exception)
                {
                    CurrentStage = $"开机自启设置失败：{exception.Message}";
                }

                _ = SaveSettingsAsync();
            }
        }
    }

    public bool StartMinimizedToTray
    {
        get => startMinimizedToTray;
        set
        {
            if (SetProperty(ref startMinimizedToTray, value))
            {
                settings.StartMinimizedToTray = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    public bool CloseToTray
    {
        get => closeToTray;
        set
        {
            if (SetProperty(ref closeToTray, value))
            {
                settings.CloseToTray = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    public double RefreshIntervalSeconds
    {
        get => refreshIntervalSeconds;
        set
        {
            double normalized = SettingsService.NormalizeForegroundRefreshInterval(value);
            if (SetProperty(ref refreshIntervalSeconds, normalized))
            {
                settings.RefreshIntervalSeconds = normalized;
                pollingService.UpdateIntervals(settings.RefreshIntervalSeconds, settings.BackgroundRefreshIntervalSeconds);
                _ = SaveSettingsAsync();
            }
        }
    }

    public int BackgroundRefreshIntervalSeconds
    {
        get => backgroundRefreshIntervalSeconds;
        set
        {
            int normalized = Math.Clamp(value, 5, 120);
            if (SetProperty(ref backgroundRefreshIntervalSeconds, normalized))
            {
                settings.BackgroundRefreshIntervalSeconds = normalized;
                pollingService.UpdateIntervals(settings.RefreshIntervalSeconds, settings.BackgroundRefreshIntervalSeconds);
                _ = SaveSettingsAsync();
            }
        }
    }

    public IReadOnlyList<ThemeDescriptor> AvailableThemes => themeService.AvailableThemes;

    public ThemeDescriptor ClassicTheme => AvailableThemes.Single(item => item.Theme == AppTheme.Classic);

    public ThemeDescriptor TraceworkTheme => AvailableThemes.Single(item => item.Theme == AppTheme.Tracework);

    public IReadOnlyList<MotionLevelDescriptor> MotionOptions { get; } =
    [
        new(MotionLevel.Full, "完整", "淡入与短距离位移"),
        new(MotionLevel.Standard, "标准", "默认的轻量动效"),
        new(MotionLevel.Reduced, "减弱", "仅保留短淡入"),
        new(MotionLevel.Off, "关闭", "即时切换")
    ];

    public ThemeDescriptor SelectedTheme
    {
        get => selectedTheme;
        private set
        {
            if (SetProperty(ref selectedTheme, value))
            {
                OnPropertyChanged(nameof(IsClassicThemeSelected));
                OnPropertyChanged(nameof(IsTraceworkThemeSelected));
            }
        }
    }

    public bool IsClassicThemeSelected => SelectedTheme.Theme == AppTheme.Classic;

    public bool IsTraceworkThemeSelected => SelectedTheme.Theme == AppTheme.Tracework;

    public string ThemeStatusText
    {
        get => themeStatusText;
        private set => SetProperty(ref themeStatusText, value);
    }

    public MotionLevelDescriptor SelectedMotionLevel
    {
        get => selectedMotionLevel;
        private set
        {
            if (SetProperty(ref selectedMotionLevel, value))
            {
                OnPropertyChanged(nameof(IsFullMotionSelected));
                OnPropertyChanged(nameof(IsStandardMotionSelected));
                OnPropertyChanged(nameof(IsReducedMotionSelected));
                OnPropertyChanged(nameof(IsOffMotionSelected));
            }
        }
    }

    public MotionLevel RequestedMotionLevel
    {
        get => requestedMotionLevel;
        private set
        {
            if (SetProperty(ref requestedMotionLevel, value))
            {
                OnPropertyChanged(nameof(IsFullMotionSelected));
                OnPropertyChanged(nameof(IsStandardMotionSelected));
                OnPropertyChanged(nameof(IsReducedMotionSelected));
                OnPropertyChanged(nameof(IsOffMotionSelected));
            }
        }
    }

    public MotionLevel EffectiveMotionLevel
    {
        get => effectiveMotionLevel;
        private set => SetProperty(ref effectiveMotionLevel, value);
    }

    public bool IsFullMotionSelected => RequestedMotionLevel == MotionLevel.Full;

    public bool IsStandardMotionSelected => RequestedMotionLevel == MotionLevel.Standard;

    public bool IsReducedMotionSelected => RequestedMotionLevel == MotionLevel.Reduced;

    public bool IsOffMotionSelected => RequestedMotionLevel == MotionLevel.Off;

    public string MotionStatusText
    {
        get => motionStatusText;
        private set => SetProperty(ref motionStatusText, value);
    }

    public string CurrentStage
    {
        get => currentStage;
        private set => SetProperty(ref currentStage, value);
    }

    public string LastSelectedPage
    {
        get => lastSelectedPage;
        private set => SetProperty(ref lastSelectedPage, value);
    }

    public string SensorIntegrationMessage
    {
        get => sensorIntegrationMessage;
        private set => SetProperty(ref sensorIntegrationMessage, value);
    }

    public string AdministratorStatus => SensorRuntimeDiagnostics.IsAdministrator() ? "管理员" : "普通用户";

    public string ProcessArchitectureStatus => SensorRuntimeDiagnostics.GetProcessArchitecture();

    public string LibreHardwareMonitorVersion => SensorRuntimeDiagnostics.GetLibreHardwareMonitorFullName();

    public string LibreHardwareMonitorPath => SensorRuntimeDiagnostics.GetLibreHardwareMonitorLocation();

    public string CpuTemperatureSource
    {
        get => cpuTemperatureSource;
        private set => SetProperty(ref cpuTemperatureSource, value);
    }

    public string CpuTemperatureAvailability
    {
        get => cpuTemperatureAvailability;
        private set => SetProperty(ref cpuTemperatureAvailability, value);
    }

    public string CpuTemperatureDetectedButNull
    {
        get => cpuTemperatureDetectedButNull;
        private set => SetProperty(ref cpuTemperatureDetectedButNull, value);
    }

    public bool RecordGameSessions
    {
        get => recordGameSessions;
        set
        {
            if (SetProperty(ref recordGameSessions, value))
            {
                settings.RecordGameSessions = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    public bool AutoRefreshHardwareOnDeviceChange
    {
        get => autoRefreshHardwareOnDeviceChange;
        set
        {
            if (SetProperty(ref autoRefreshHardwareOnDeviceChange, value))
            {
                settings.AutoRefreshHardwareOnDeviceChange = value;
                _ = SaveSettingsAsync();
            }
        }
    }

    public IReadOnlyList<string> FrameStorageModeOptions { get; } = ["压缩 CSV（推荐）", "普通 CSV（兼容）"];

    public string SelectedFrameStorageMode
    {
        get => settings.GameSessionFrameStorageMode == GameSessionFrameStorageMode.CompressedCsv
            ? FrameStorageModeOptions[0]
            : FrameStorageModeOptions[1];
        set
        {
            GameSessionFrameStorageMode mode = string.Equals(value, FrameStorageModeOptions[1], StringComparison.Ordinal)
                ? GameSessionFrameStorageMode.PlainCsv
                : GameSessionFrameStorageMode.CompressedCsv;
            if (settings.GameSessionFrameStorageMode == mode) return;
            settings.GameSessionFrameStorageMode = mode;
            OnPropertyChanged();
            _ = SaveSettingsAsync();
        }
    }

    public bool IsHardwareScanning
    {
        get => isHardwareScanning;
        private set
        {
            if (SetProperty(ref isHardwareScanning, value))
            {
                RescanHardwareCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string HardwareScanStatusText
    {
        get => hardwareScanStatusText;
        private set => SetProperty(ref hardwareScanStatusText, value);
    }

    public string LastHardwareScanTimeText
    {
        get => lastHardwareScanTimeText;
        private set => SetProperty(ref lastHardwareScanTimeText, value);
    }

    public string GameSessionDirectory => gameSessionRecorder.RootDirectory;

    public string GameSessionDirectorySizeText
    {
        get => gameSessionDirectorySizeText;
        private set => SetProperty(ref gameSessionDirectorySizeText, value);
    }

    public IRelayCommand IncreaseRefreshIntervalCommand { get; }

    public IRelayCommand DecreaseRefreshIntervalCommand { get; }

    public IRelayCommand IncreaseBackgroundRefreshIntervalCommand { get; }

    public IRelayCommand DecreaseBackgroundRefreshIntervalCommand { get; }

    public IAsyncRelayCommand<ThemeDescriptor?> SelectThemeCommand { get; }

    public IRelayCommand<MotionLevelDescriptor?> SelectMotionLevelCommand { get; }

    public IAsyncRelayCommand ExportSensorDiagnosticsCommand { get; }

    public IAsyncRelayCommand ExportOfficialComparisonDiagnosticsCommand { get; }

    public IRelayCommand OpenMetricVisibilityCommand { get; }

    public IRelayCommand OpenGameSessionDirectoryCommand { get; }

    public IAsyncRelayCommand RecalculateGameSessionDirectorySizeCommand { get; }

    public IAsyncRelayCommand RescanHardwareCommand { get; }

    public void SetActive(bool active)
    {
        if (isDisposed || isActive == active)
        {
            return;
        }

        isActive = active;
        if (active)
        {
            SetProperty(ref recordGameSessions, settings.RecordGameSessions, nameof(RecordGameSessions));
            SetProperty(ref autoRefreshHardwareOnDeviceChange, settings.AutoRefreshHardwareOnDeviceChange, nameof(AutoRefreshHardwareOnDeviceChange));
            OnPropertyChanged(nameof(SelectedFrameStorageMode));
            _ = RefreshGameSessionDirectorySizeAsync(force: false);
        }
        else
        {
            CancelDirectorySizeRefresh();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        isActive = false;
        CancelDirectorySizeRefresh();
        if (hardwareRefreshService is not null)
        {
            hardwareRefreshService.StatusChanged -= OnHardwareRefreshStatusChanged;
        }
        motionService.MotionChanged -= OnMotionChanged;
        themeTransitionService.TransitionChanged -= OnThemeTransitionChanged;
    }

    private async Task RescanHardwareAsync()
    {
        if (isDisposed)
        {
            return;
        }

        if (hardwareRefreshService is null)
        {
            HardwareScanStatusText = "无法使用硬件重新扫描服务";
            return;
        }

        await hardwareRefreshService.RefreshAsync(HardwareRefreshReason.ManualSettings);
    }

    private void OnHardwareRefreshStatusChanged(object? sender, HardwareRefreshStatusChangedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (isDisposed)
            {
                return;
            }

            IsHardwareScanning = e.State == HardwareRefreshState.Scanning;
            HardwareScanStatusText = e.State switch
            {
                HardwareRefreshState.Scanning => "正在重新扫描硬件",
                HardwareRefreshState.Completed => "硬件重新扫描完成",
                HardwareRefreshState.PartiallyFailed => e.Result?.FailedProviders.Count > 0
                    ? "重新扫描完成，部分传感器不可用：" + string.Join("、", e.Result.FailedProviders)
                    : "重新扫描完成，部分传感器不可用",
                HardwareRefreshState.Failed => "无法重新扫描硬件：" + (e.Result?.ErrorMessage ?? "未知错误"),
                _ => "等待"
            };
            if (e.Result is not null)
            {
                LastHardwareScanTimeText = e.Result.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
            }
        });
    }

    public void ApplyStartupState(bool enabled)
    {
        if (SetProperty(ref autoStartEnabled, enabled, nameof(AutoStartEnabled)))
        {
            settings.AutoStartEnabled = enabled;
        }
    }

    private bool CanSelectTheme(ThemeDescriptor? requestedTheme)
    {
        return requestedTheme is not null && !themeTransitionService.IsTransitioning;
    }

    private async Task SelectThemeAsync(ThemeDescriptor? requestedTheme)
    {
        if (requestedTheme is null)
        {
            return;
        }

        if (requestedTheme.Theme == themeService.CurrentTheme)
        {
            OnPropertyChanged(nameof(IsClassicThemeSelected));
            OnPropertyChanged(nameof(IsTraceworkThemeSelected));
            return;
        }

        ThemeStatusText = $"Applying {requestedTheme.DisplayName} through System Rewire…";
        ThemeTransitionResult result = await themeTransitionService.ApplyThemeAsync(requestedTheme.Theme);
        if (isDisposed)
        {
            return;
        }

        if (result.Status == ThemeTransitionStatus.Applied && result.WasThemeCommitted)
        {
            SelectedTheme = themeService.AvailableThemes.Single(item => item.Theme == themeService.CurrentTheme);
            settings.Theme = AppThemeParser.ToStorageValue(SelectedTheme.Theme);
            ThemeStatusText = $"Applied {SelectedTheme.DisplayName}; saving…";
            long changeVersion = Interlocked.Increment(ref themeChangeVersion);
            await SaveSelectedThemeAsync(changeVersion);
            return;
        }

        SelectedTheme = themeService.AvailableThemes.Single(item => item.Theme == themeService.CurrentTheme);
        ThemeStatusText = result.Status switch
        {
            ThemeTransitionStatus.Failed => $"Unable to apply {requestedTheme.DisplayName}; restored {SelectedTheme.DisplayName}.",
            ThemeTransitionStatus.Superseded => $"Theme change superseded; current theme: {SelectedTheme.DisplayName}.",
            ThemeTransitionStatus.Cancelled => $"Theme change cancelled; current theme: {SelectedTheme.DisplayName}.",
            ThemeTransitionStatus.AlreadyCurrent => $"Current theme: {SelectedTheme.DisplayName}",
            _ => $"Current theme: {SelectedTheme.DisplayName}"
        };
    }

    private void SelectMotionLevel(MotionLevelDescriptor? requestedMotion)
    {
        if (requestedMotion is null)
        {
            return;
        }

        if (requestedMotion.Level == motionService.RequestedLevel)
        {
            ApplyMotionProfile(motionService.CurrentProfile);
            return;
        }

        bool changed = motionService.SetRequestedLevel(requestedMotion.Level);
        ApplyMotionProfile(motionService.CurrentProfile);
        if (!changed)
        {
            return;
        }

        settings.Motion = MotionLevelParser.ToStorageValue(requestedMotion.Level);
        MotionStatusText = $"{BuildMotionStatusText(motionService.CurrentProfile)}；正在保存";
        _ = SaveSelectedMotionAsync(motionService.CurrentProfile);
    }

    private async Task SaveSelectedMotionAsync(MotionProfile profile)
    {
        try
        {
            bool saved = await settingsService.TrySaveAsync(settings);
            if (!isDisposed)
            {
                MotionStatusText = saved
                    ? BuildMotionStatusText(profile)
                    : $"{BuildMotionStatusText(profile)}；本次无法保存，下次启动可能恢复旧档位";
                CurrentStage = saved ? "配置已保存" : "动效档位已应用，但无法保存配置";
            }
        }
        catch (Exception exception)
        {
            if (!isDisposed)
            {
                MotionStatusText = $"{BuildMotionStatusText(profile)}；无法保存：{exception.Message}";
                CurrentStage = $"无法保存配置：{exception.Message}";
            }
        }
    }

    private void OnMotionChanged(object? sender, MotionChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (!isDisposed)
            {
                ApplyMotionProfile(e.CurrentProfile);
            }
        });
    }

    private void OnThemeTransitionChanged(object? sender, ThemeTransitionChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (!isDisposed)
            {
                SelectThemeCommand.NotifyCanExecuteChanged();
            }
        });
    }

    private void ApplyMotionProfile(MotionProfile profile)
    {
        RequestedMotionLevel = profile.RequestedLevel;
        EffectiveMotionLevel = profile.EffectiveLevel;
        SelectedMotionLevel = MotionOptions.Single(item => item.Level == profile.RequestedLevel);
        MotionStatusText = BuildMotionStatusText(profile);
    }

    private static string BuildMotionStatusText(MotionProfile profile)
    {
        string requested = ToMotionDisplayName(profile.RequestedLevel);
        string effective = ToMotionDisplayName(profile.EffectiveLevel);
        if (string.IsNullOrWhiteSpace(profile.FallbackReason)
            || profile.RequestedLevel == profile.EffectiveLevel)
        {
            return $"请求：{requested}；实际：{effective}";
        }

        return $"请求：{requested}；实际：{effective}（{ToFallbackDisplayText(profile.FallbackReason)}）";
    }

    private static string ToMotionDisplayName(MotionLevel level) => level switch
    {
        MotionLevel.Full => "完整",
        MotionLevel.Standard => "标准",
        MotionLevel.Reduced => "减弱",
        MotionLevel.Off => "关闭",
        _ => "标准"
    };

    private static string ToFallbackDisplayText(string reason) => reason switch
    {
        "Requested Off" => "用户关闭",
        "Windows animations disabled" => "Windows 动画已关闭",
        "Render tier 0" => "渲染层级 Tier0",
        "Render tier 1" => "渲染层级 Tier1",
        "High contrast" => "高对比度",
        "Remote session" => "远程会话",
        _ => reason
    };

    private async Task SaveSelectedThemeAsync(long changeVersion)
    {
        try
        {
            bool saved = await settingsService.TrySaveAsync(settings);
            if (!isDisposed && changeVersion == Volatile.Read(ref themeChangeVersion))
            {
                ThemeStatusText = saved
                    ? $"当前主题：{SelectedTheme.DisplayName}（已保存）"
                    : $"当前主题：{SelectedTheme.DisplayName}（本次无法保存，下次启动可能恢复原主题）";
                CurrentStage = saved ? "配置已保存" : "主题已应用，但无法保存配置";
            }
        }
        catch (Exception exception)
        {
            if (isDisposed || changeVersion != Volatile.Read(ref themeChangeVersion))
            {
                return;
            }

            ThemeStatusText = $"主题已应用，但无法保存：{exception.Message}";
            CurrentStage = $"无法保存配置：{exception.Message}";
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await settingsService.SaveAsync(settings);
            if (!isDisposed)
            {
                CurrentStage = "配置已保存";
                LastSelectedPage = settings.LastSelectedPage;
            }
        }
        catch (Exception exception)
        {
            if (!isDisposed)
            {
                CurrentStage = $"无法保存配置：{exception.Message}";
            }
        }
    }

    private async Task ExportSensorDiagnosticsAsync()
    {
        if (isDisposed)
        {
            return;
        }

        CurrentStage = "正在导出传感器诊断";
        string path = await sensorDiagnosticService.ExportAsync();
        if (!isDisposed)
        {
            CurrentStage = $"传感器诊断已导出：{path}";
        }
    }

    private async Task ExportOfficialComparisonDiagnosticsAsync()
    {
        if (isDisposed)
        {
            return;
        }

        CurrentStage = "正在导出官方对比诊断";
        string path = await sensorDiagnosticService.ExportOfficialComparisonAsync();
        if (!isDisposed)
        {
            CurrentStage = $"官方对比诊断已导出：{path}";
        }
    }

    private async Task RefreshGameSessionDirectorySizeAsync(bool force)
    {
        if (isDisposed || !isActive)
        {
            return;
        }

        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref directorySizeCancellation, cancellation);
        previous?.Cancel();
        try
        {
            GameSessionDirectorySizeInfo info = await gameSessionRecorder
                .GetDirectorySizeInfoAsync(cancellation.Token).ConfigureAwait(false);
            if (info.IsCalculating && !force)
            {
                ViewModelHelpers.Dispatch(dispatcher, () =>
                {
                    if (CanApplyDirectorySize(cancellation))
                    {
                        GameSessionDirectorySizeText = "正在后台计算…";
                    }
                });
            }

            long bytes = force
                ? await gameSessionRecorder.RecalculateDirectorySizeAsync(cancellation.Token).ConfigureAwait(false)
                : info.Bytes ?? await gameSessionRecorder.GetDirectorySizeAsync(cancellation.Token).ConfigureAwait(false);
            string text = bytes < 1024L * 1024L
                ? $"{bytes / 1024d:0.0} KiB"
                : bytes < 1024L * 1024L * 1024L
                    ? $"{bytes / (1024d * 1024d):0.0} MiB"
                    : $"{bytes / (1024d * 1024d * 1024d):0.00} GiB";
            if (info.IsStale && !force) text += "（缓存可能过期）";
            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                if (CanApplyDirectorySize(cancellation))
                {
                    GameSessionDirectorySizeText = text;
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                if (CanApplyDirectorySize(cancellation))
                {
                    GameSessionDirectorySizeText = "无法读取";
                }
            });
        }
        finally
        {
            Interlocked.CompareExchange(ref directorySizeCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private bool CanApplyDirectorySize(CancellationTokenSource owner) =>
        !isDisposed
        && isActive
        && !owner.IsCancellationRequested
        && ReferenceEquals(Volatile.Read(ref directorySizeCancellation), owner);

    private void CancelDirectorySizeRefresh()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref directorySizeCancellation, null);
        cancellation?.Cancel();
    }

    private void OpenGameSessionDirectory()
    {
        try
        {
            Directory.CreateDirectory(gameSessionRecorder.RootDirectory);
            ProcessStartInfo startInfo = new("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(gameSessionRecorder.RootDirectory);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CurrentStage = $"无法打开记录目录：{exception.Message}";
        }
    }

    private void RefreshDiagnosticsText()
    {
        SensorIntegrationMessage = SensorRuntimeDiagnostics.IsAdministrator() ? "完整" : "受限";
        CpuTemperatureAvailability = "--";
        CpuTemperatureDetectedButNull = "--";
        CpuTemperatureSource = "--";
    }

}
