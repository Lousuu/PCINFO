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
    private readonly IStartupService startupService;
    private readonly PollingService pollingService;
    private readonly SensorDiagnosticService sensorDiagnosticService;
    private readonly Dispatcher dispatcher;
    private readonly Action openMetricVisibility;
    private readonly IGameSessionRecorder gameSessionRecorder;
    private readonly IHardwareRefreshService? hardwareRefreshService;
    private bool autoStartEnabled;
    private bool startMinimizedToTray;
    private bool closeToTray;
    private double refreshIntervalSeconds;
    private int backgroundRefreshIntervalSeconds;
    private string theme;
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

    public SettingsViewModel(
        AppSettings settings,
        ISettingsService settingsService,
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
        theme = settings.Theme;
        lastSelectedPage = settings.LastSelectedPage;
        recordGameSessions = settings.RecordGameSessions;
        autoRefreshHardwareOnDeviceChange = settings.AutoRefreshHardwareOnDeviceChange;

        IncreaseRefreshIntervalCommand = new RelayCommand(() => RefreshIntervalSeconds += 0.5d);
        DecreaseRefreshIntervalCommand = new RelayCommand(() => RefreshIntervalSeconds -= 0.5d);
        IncreaseBackgroundRefreshIntervalCommand = new RelayCommand(() => BackgroundRefreshIntervalSeconds++);
        DecreaseBackgroundRefreshIntervalCommand = new RelayCommand(() => BackgroundRefreshIntervalSeconds--);
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

    public string Theme
    {
        get => theme;
        set
        {
            if (SetProperty(ref theme, value))
            {
                settings.Theme = string.IsNullOrWhiteSpace(value) ? "Dark" : value.Trim();
                _ = SaveSettingsAsync();
            }
        }
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

    public IAsyncRelayCommand ExportSensorDiagnosticsCommand { get; }

    public IAsyncRelayCommand ExportOfficialComparisonDiagnosticsCommand { get; }

    public IRelayCommand OpenMetricVisibilityCommand { get; }

    public IRelayCommand OpenGameSessionDirectoryCommand { get; }

    public IAsyncRelayCommand RecalculateGameSessionDirectorySizeCommand { get; }

    public IAsyncRelayCommand RescanHardwareCommand { get; }

    public void SetActive(bool active)
    {
        if (active)
        {
            SetProperty(ref recordGameSessions, settings.RecordGameSessions, nameof(RecordGameSessions));
            SetProperty(ref autoRefreshHardwareOnDeviceChange, settings.AutoRefreshHardwareOnDeviceChange, nameof(AutoRefreshHardwareOnDeviceChange));
            OnPropertyChanged(nameof(SelectedFrameStorageMode));
            _ = RefreshGameSessionDirectorySizeAsync(force: false);
        }
    }

    public void Dispose()
    {
        if (hardwareRefreshService is not null)
        {
            hardwareRefreshService.StatusChanged -= OnHardwareRefreshStatusChanged;
        }
    }

    private async Task RescanHardwareAsync()
    {
        if (hardwareRefreshService is null)
        {
            HardwareScanStatusText = "硬件刷新服务不可用";
            return;
        }

        await hardwareRefreshService.RefreshAsync(HardwareRefreshReason.ManualSettings);
    }

    private void OnHardwareRefreshStatusChanged(object? sender, HardwareRefreshStatusChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            IsHardwareScanning = e.State == HardwareRefreshState.Scanning;
            HardwareScanStatusText = e.State switch
            {
                HardwareRefreshState.Scanning => "正在扫描",
                HardwareRefreshState.Completed => "扫描完成",
                HardwareRefreshState.PartiallyFailed => "部分失败：" + string.Join("、", e.Result?.FailedProviders ?? []),
                HardwareRefreshState.Failed => "扫描失败：" + (e.Result?.ErrorMessage ?? "未知错误"),
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

    private async Task SaveSettingsAsync()
    {
        try
        {
            await settingsService.SaveAsync(settings);
            CurrentStage = "配置已保存";
            LastSelectedPage = settings.LastSelectedPage;
        }
        catch (Exception exception)
        {
            CurrentStage = $"配置保存失败：{exception.Message}";
        }
    }

    private async Task ExportSensorDiagnosticsAsync()
    {
        CurrentStage = "正在导出传感器诊断";
        string path = await sensorDiagnosticService.ExportAsync();
        CurrentStage = $"传感器诊断已导出：{path}";
    }

    private async Task ExportOfficialComparisonDiagnosticsAsync()
    {
        CurrentStage = "正在导出官方对比诊断";
        string path = await sensorDiagnosticService.ExportOfficialComparisonAsync();
        CurrentStage = $"官方对比诊断已导出：{path}";
    }

    private async Task RefreshGameSessionDirectorySizeAsync(bool force)
    {
        try
        {
            GameSessionDirectorySizeInfo info = await gameSessionRecorder
                .GetDirectorySizeInfoAsync().ConfigureAwait(false);
            if (info.IsCalculating && !force)
            {
                ViewModelHelpers.Dispatch(dispatcher, () => GameSessionDirectorySizeText = "正在后台计算…");
            }

            long bytes = force
                ? await gameSessionRecorder.RecalculateDirectorySizeAsync().ConfigureAwait(false)
                : info.Bytes ?? await gameSessionRecorder.GetDirectorySizeAsync().ConfigureAwait(false);
            string text = bytes < 1024L * 1024L
                ? $"{bytes / 1024d:0.0} KiB"
                : bytes < 1024L * 1024L * 1024L
                    ? $"{bytes / (1024d * 1024d):0.0} MiB"
                    : $"{bytes / (1024d * 1024d * 1024d):0.00} GiB";
            if (info.IsStale && !force) text += "（缓存可能过期）";
            ViewModelHelpers.Dispatch(dispatcher, () => GameSessionDirectorySizeText = text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ViewModelHelpers.Dispatch(dispatcher, () => GameSessionDirectorySizeText = "不可用");
        }
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
