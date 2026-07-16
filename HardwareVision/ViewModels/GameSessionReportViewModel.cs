using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.ViewModels;

public sealed class GameSessionReportViewModel : ObservableObject, IDisposable
{
    private readonly IGameSessionReportService reportService;
    private readonly GameSessionRecordInfo record;
    private readonly Action closeAction;
    private readonly IGameIconService iconService;
    private CancellationTokenSource? loadCancellation;
    private GameSessionReport? report;
    private SessionChartModel? selectedChart;
    private bool isLoading;
    private bool isDisposed;
    private string statusText = "正在加载会话数据…";
    private ImageSource? gameIcon;

    public GameSessionReportViewModel(
        GameSessionRecordInfo record,
        IGameSessionReportService reportService,
        Action closeAction,
        IGameIconService? iconService = null)
    {
        this.record = record ?? throw new ArgumentNullException(nameof(record));
        this.reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        this.closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        this.iconService = iconService ?? GameIconService.Shared;
        BackCommand = new RelayCommand(Close);
        OpenDirectoryCommand = new RelayCommand(OpenDirectory);
        ExportPlainCsvCommand = new AsyncRelayCommand(ExportPlainCsvAsync, () => IsCompressedFrameRecord);
    }

    public ObservableCollection<GameSessionReportMetric> KeyMetrics { get; } = new();

    public ObservableCollection<GameSessionReportMetric> HardwareMetrics { get; } = new();

    public ObservableCollection<GameSessionReportMetric> ThrottleMetrics { get; } = new();

    public GameSessionReport? Report
    {
        get => report;
        private set
        {
            if (!SetProperty(ref report, value)) return;
            OnPropertyChanged(nameof(Charts));
            OnPropertyChanged(nameof(PerformanceLimitEvents));
            OnPropertyChanged(nameof(Warnings));
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(HasPerformanceLimitEvents));
            OnPropertyChanged(nameof(HasNoPerformanceLimitEvents));
            OnPropertyChanged(nameof(SessionStatusText));
            OnPropertyChanged(nameof(ProcessName));
            OnPropertyChanged(nameof(StartedAtText));
            OnPropertyChanged(nameof(EndedAtText));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(EndReasonText));
            OnPropertyChanged(nameof(SessionDiagnostic));
            OnPropertyChanged(nameof(PerformanceLimitStatusText));
            OnPropertyChanged(nameof(TimelineStatusText));
        }
    }

    public IReadOnlyList<SessionChartModel> Charts => Report?.Charts ?? [];

    public IReadOnlyList<GamePerformanceLimitEvent> PerformanceLimitEvents => Report?.PerformanceLimitEvents ?? [];

    public IReadOnlyList<string> Warnings => Report?.Warnings ?? [];

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasPerformanceLimitEvents => PerformanceLimitEvents.Count > 0;

    public bool HasNoPerformanceLimitEvents => !HasPerformanceLimitEvents;

    public SessionChartModel? SelectedChart
    {
        get => selectedChart;
        set => SetProperty(ref selectedChart, value);
    }

    public bool IsLoading
    {
        get => isLoading;
        private set => SetProperty(ref isLoading, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string GameName => string.IsNullOrWhiteSpace(record.GameName) ? "未知游戏" : record.GameName;

    public string GameInitial => GameName.Length > 0 ? GameName[..1].ToUpper(CultureInfo.CurrentCulture) : "?";

    public ImageSource? GameIcon
    {
        get => gameIcon;
        private set
        {
            if (!SetProperty(ref gameIcon, value)) return;
            OnPropertyChanged(nameof(HasGameIcon));
            OnPropertyChanged(nameof(HasNoGameIcon));
        }
    }

    public bool HasGameIcon => GameIcon is not null;

    public bool HasNoGameIcon => !HasGameIcon;

    public string ProcessName => Report?.Summary?.ProcessName ?? record.GameName ?? "--";

    public string StartedAtText => (Report?.Summary?.CaptureStartedAt ?? record.StartedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    public string EndedAtText
    {
        get
        {
            DateTimeOffset end = Report?.Summary?.CaptureEndedAt
                ?? record.StartedAt + record.Duration;
            return end.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }
    }

    public string DurationText => FormatDuration(Report?.Summary?.Duration ?? record.Duration);

    public string SessionStatusText => Report?.IsPartial == true || !record.IsComplete ? "部分记录" : "完整记录";

    public string EndReasonText => FormatEndReason(Report?.Summary?.EndReason ?? record.EndReason);

    public string RecordDirectory => Path.GetDirectoryName(record.CsvPath) ?? "--";

    public bool IsCompressedFrameRecord => record.CsvPath.Contains(".csv.gz", StringComparison.OrdinalIgnoreCase);

    public string SessionDiagnostic
    {
        get
        {
            return Report?.IsPartial == true
                ? "检测到异常数据，请查看数据完整性提示"
                : "历史会话报告";
        }
    }

    public string PerformanceLimitStatusText => Report?.PerformanceLimitEventsLoadedFromLegacySummary == true
        ? PerformanceLimitEvents.Count == 0
            ? "兼容读取旧版 summary.json：未检测到性能限制事件（未记录独立 CSV）"
            : $"兼容读取旧版 summary.json：已加载 {PerformanceLimitEvents.Count} 个限制事件（未记录独立 CSV）"
        : FormatAuxiliaryStatus(
            Report?.PerformanceLimitFileStatus,
            "性能限制原因未记录",
            "已记录，未检测到性能限制事件",
            $"已记录 {PerformanceLimitEvents.Count} 个限制事件");

    public string TimelineStatusText => FormatAuxiliaryStatus(
        Report?.HardwareTimelineFileStatus,
        "该会话未记录硬件频率时间序列",
        "已记录时间线，但暂无可用传感器数据",
        "硬件时间线已加载");

    public IRelayCommand BackCommand { get; }

    public IRelayCommand OpenDirectoryCommand { get; }

    public IAsyncRelayCommand ExportPlainCsvCommand { get; }

    public async Task LoadAsync()
    {
        if (isDisposed) return;
        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref loadCancellation, cancellation);
        previous?.Cancel();
        IsLoading = true;
        StatusText = "正在加载会话数据…";
        try
        {
            GameSessionReport loaded = await reportService.LoadAsync(record, cancellation.Token);
            if (isDisposed || cancellation.IsCancellationRequested) return;
            Report = loaded;
            SelectedChart = loaded.Charts.Count > 0 ? loaded.Charts[0] : null;
            GameIcon = await iconService.LoadAsync(loaded.Summary?.ExecutablePath, cancellation.Token);
            PopulateMetrics(loaded);
            StatusText = loaded.IsPartial ? "已加载，部分数据不可用" : "会话报告已加载";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!isDisposed && !cancellation.IsCancellationRequested)
            {
                StatusText = "无法加载会话数据";
            }
            AppLogger.LogError("Game session report view could not be loaded.", exception,
                $"game-session-report-view:{Path.GetFileName(record.CsvPath)}", TimeSpan.FromMinutes(5));
        }
        finally
        {
            if (!isDisposed && ReferenceEquals(loadCancellation, cancellation)) IsLoading = false;
            Interlocked.CompareExchange(ref loadCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        ExportPlainCsvCommand.Cancel();
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref loadCancellation, null);
        cancellation?.Cancel();
        SelectedChart = null;
        Report = null;
        GameIcon = null;
        KeyMetrics.Clear();
        HardwareMetrics.Clear();
        ThrottleMetrics.Clear();
    }

    private void Close()
    {
        if (!isDisposed) closeAction();
    }

    private void OpenDirectory()
    {
        string? directory = Path.GetDirectoryName(record.CsvPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLogger.LogError("Game session report directory could not be opened.", exception,
                $"game-session-report-directory:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
        }
    }

    private async Task ExportPlainCsvAsync(CancellationToken cancellationToken)
    {
        if (isDisposed)
        {
            return;
        }

        try
        {
            StatusText = "正在导出普通 CSV…";
            string path = await GameSessionCsvExportService.ExportPlainCsvAsync(
                record.CsvPath,
                destinationPath: null,
                cancellationToken);
            if (!isDisposed)
            {
                StatusText = $"普通 CSV 已导出：{path}";
            }
        }
        catch (OperationCanceledException)
        {
            if (!isDisposed)
            {
                StatusText = "导出已取消";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!isDisposed)
            {
                StatusText = $"无法导出普通 CSV：{exception.Message}";
            }
        }
    }

    private void PopulateMetrics(GameSessionReport loaded)
    {
        KeyMetrics.Clear();
        HardwareMetrics.Clear();
        ThrottleMetrics.Clear();
        GameSessionSummary? summary = loaded.Summary;
        Add(KeyMetrics, "平均 FPS", Format(loaded.AverageFps ?? summary?.AverageFps, "0.##", "FPS"));
        Add(KeyMetrics, "最大 FPS", Format(loaded.MaximumFps ?? summary?.SustainedMaximumFps, "0.##", "FPS"),
            "最大 FPS 根据有效采样数据计算，已排除孤立异常帧。");
        Add(KeyMetrics, "1% Low", Format(loaded.OnePercentLowFps ?? summary?.OnePercentLowFps, "0.##", "FPS"));
        Add(KeyMetrics, "0.1% Low", Format(loaded.ZeroPointOnePercentLowFps ?? summary?.ZeroPointOnePercentLowFps, "0.##", "FPS"));
        Add(KeyMetrics, "平均帧时间", Format(loaded.AverageFrameTimeMs ?? summary?.AverageFrameTimeMs, "0.###", "ms"));
        Add(KeyMetrics, "CPU 忙碌时间", Format(loaded.AverageCpuBusyMs ?? summary?.AverageCpuBusyMs, "0.###", "ms"), "PresentMon CPUBusy");
        Add(KeyMetrics, "GPU 渲染时间", Format(loaded.AverageGpuTimeMs ?? summary?.AverageGpuTimeMs, "0.###", "ms"), "PresentMon GPUTime");
        Add(KeyMetrics, "显示延迟", Format(loaded.AverageDisplayLatencyMs ?? summary?.AverageDisplayLatencyMs, "0.###", "ms"), "PresentMon DisplayLatency");
        Add(KeyMetrics, "估算能耗", Format(summary?.EstimatedEnergyWh ?? record.EstimatedEnergyWh, "0.####", "Wh"));
        Add(KeyMetrics, "平均估算功率", Format(summary?.AverageEstimatedPowerWatts ?? record.AverageEstimatedPowerWatts, "0.##", "W"));
        Add(KeyMetrics, "能耗覆盖率", Format(summary?.EnergyCoveragePercent ?? record.EnergyCoveragePercent, "0.##", "%"));
        Add(KeyMetrics, "CPU 限制事件", FormatCount(summary?.CpuPerformanceLimitEventCount));
        Add(KeyMetrics, "GPU 限制事件", FormatCount(summary?.GpuPerformanceLimitEventCount));
        Add(KeyMetrics, "CPU 限制支持", FormatSupport(summary?.CpuPerformanceLimitSupportStatus, loaded.PerformanceLimitFileStatus));
        Add(KeyMetrics, "GPU 限制支持", FormatSupport(summary?.GpuPerformanceLimitSupportStatus, loaded.PerformanceLimitFileStatus));
        Add(KeyMetrics, "CPU 限制累计", FormatSeconds(summary?.CpuPerformanceLimitDurationSeconds));
        Add(KeyMetrics, "GPU 限制累计", FormatSeconds(summary?.GpuPerformanceLimitDurationSeconds));

        GameSessionHardwareMetadata? hardware = summary?.HardwareMetadata;
        Add(HardwareMetrics, "CPU", ValueOrPlaceholder(hardware?.CpuName));
        Add(HardwareMetrics, "GPU", ValueOrPlaceholder(hardware?.GpuName));
        Add(HardwareMetrics, "GPU 驱动", ValueOrPlaceholder(hardware?.GpuDriverVersion));
        Add(HardwareMetrics, "内存", ValueOrPlaceholder(hardware?.MemoryDescription));
        Add(HardwareMetrics, "主板", ValueOrPlaceholder(hardware?.MotherboardName));
        Add(HardwareMetrics, "硬盘", ValueOrPlaceholder(hardware?.DiskDescription));
        Add(HardwareMetrics, "系统", ValueOrPlaceholder(hardware?.OperatingSystem));
        Add(HardwareMetrics, "显示器 / 分辨率", ValueOrPlaceholder(hardware?.DisplayDescription));
        Add(HardwareMetrics, "会话平均 CPU 占用", Format(summary?.AverageCpuLoadPercent, "0.##", "%"));
        Add(HardwareMetrics, "会话平均 CPU 温度", Format(summary?.AverageCpuTemperatureCelsius, "0.##", "℃"));
        Add(HardwareMetrics, "会话平均 GPU 占用", Format(summary?.AverageGpuLoadPercent, "0.##", "%"));
        Add(HardwareMetrics, "会话平均 GPU 温度", Format(summary?.AverageGpuTemperatureCelsius, "0.##", "℃"));
        Add(HardwareMetrics, "会话平均内存占用", Format(summary?.AverageMemoryLoadPercent, "0.##", "%"));
        Add(HardwareMetrics, "CPU / GPU 估算能耗", $"{Format(summary?.CpuEstimatedEnergyWh, "0.####", "Wh")} / {Format(summary?.GpuEstimatedEnergyWh, "0.####", "Wh")}");

        AddThrottleMetrics("CPU", FindChart(loaded.Charts, "cpu-frequency"));
        AddThrottleMetrics("GPU", FindChart(loaded.Charts, "gpu-frequency"));
    }

    private void AddThrottleMetrics(string processor, SessionChartModel? chart)
    {
        SessionThrottleStatistics statistics = chart?.ThrottleStatistics
            ?? SessionThrottleStatisticsCalculator.CalculateRaw(
                [],
                chart?.LimitIntervals ?? [],
                chart?.DurationSeconds ?? (Report?.Summary?.Duration.TotalSeconds ?? record.Duration.TotalSeconds));
        Add(ThrottleMetrics, $"{processor} 限制事件数", statistics.EventCount.ToString(CultureInfo.CurrentCulture));
        Add(ThrottleMetrics, $"{processor} 限制累计时长", FormatSeconds(statistics.LimitedDurationSeconds));
        Add(ThrottleMetrics, $"{processor} 限制时间占比", Format(statistics.LimitedRatioPercent, "0.##", "%"));
        Add(ThrottleMetrics, $"{processor} 最常见原因", ValueOrPlaceholder(statistics.MostCommonReason));
        Add(ThrottleMetrics, $"{processor} 会话平均频率", Format(statistics.AverageFrequencyMHz, "0.##", "MHz"));
        Add(ThrottleMetrics, $"{processor} 最低 / 最高频率", $"{Format(statistics.MinimumFrequencyMHz, "0.##")} / {Format(statistics.MaximumFrequencyMHz, "0.##")} MHz");
        Add(ThrottleMetrics, $"{processor} 限制期间平均频率", Format(statistics.LimitedAverageFrequencyMHz, "0.##", "MHz"));
        Add(ThrottleMetrics, $"{processor} 正常期间平均频率", Format(statistics.NormalAverageFrequencyMHz, "0.##", "MHz"),
            statistics.HasSufficientFrequencyCoverage ? null : "时间线覆盖不足时不推测频率统计");
    }

    private static SessionChartModel? FindChart(IReadOnlyList<SessionChartModel> charts, string keyPrefix)
    {
        for (int index = 0; index < charts.Count; index++)
        {
            if (charts[index].Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase)) return charts[index];
        }
        return null;
    }

    private static void Add(ObservableCollection<GameSessionReportMetric> target, string label, string value, string? toolTip = null) =>
        target.Add(new GameSessionReportMetric { Label = label, Value = value, ToolTip = toolTip });

    private static string Format(double? value, string format, string? unit = null) =>
        value.HasValue && double.IsFinite(value.Value)
            ? value.Value.ToString(format, CultureInfo.CurrentCulture) + (string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit)
            : "--";

    private static string FormatCount(int? count) => count.HasValue ? count.Value.ToString(CultureInfo.CurrentCulture) : "--";

    private static string FormatSupport(PerformanceLimitSupportStatus? status, SessionAuxiliaryFileStatus fileStatus)
    {
        if (fileStatus == SessionAuxiliaryFileStatus.NotRecorded) return "未记录";
        return status switch
        {
            PerformanceLimitSupportStatus.SupportedNormal => "支持 · 本次正常",
            PerformanceLimitSupportStatus.ActiveLimit => "支持 · 存在限制",
            PerformanceLimitSupportStatus.Unsupported => "不支持",
            PerformanceLimitSupportStatus.TemporarilyUnavailable => "暂时不可用",
            PerformanceLimitSupportStatus.NotStarted => "未开始",
            _ => "--"
        };
    }

    private static string FormatSeconds(double? seconds) =>
        seconds.HasValue && double.IsFinite(seconds.Value) ? $"{seconds.Value:0.##} s" : "--";

    private static string ValueOrPlaceholder(string? value) => string.IsNullOrWhiteSpace(value) ? "--" : value;

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1d
        ? duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
        : duration.ToString(@"mm\:ss", CultureInfo.CurrentCulture);

    private static string FormatEndReason(GameSessionEndReason reason) => reason switch
    {
        GameSessionEndReason.UserStopped => "用户停止",
        GameSessionEndReason.TargetProcessExited => "游戏进程退出",
        GameSessionEndReason.CaptureFailed => "采集失败",
        GameSessionEndReason.PermissionDenied => "权限不足",
        GameSessionEndReason.ToolUnavailable => "采集工具不可用",
        GameSessionEndReason.SchemaMismatch => "数据格式不兼容",
        GameSessionEndReason.ApplicationShutdown => "应用退出",
        GameSessionEndReason.RecorderFailed => "记录器失败",
        _ => "未知"
    };

    private static string FormatAuxiliaryStatus(
        SessionAuxiliaryFileStatus? status,
        string notRecorded,
        string recordedNoData,
        string recorded) => status switch
        {
            SessionAuxiliaryFileStatus.NotRecorded => notRecorded,
            SessionAuxiliaryFileStatus.RecordedNoData => recordedNoData,
            SessionAuxiliaryFileStatus.Recorded => recorded,
            SessionAuxiliaryFileStatus.Unavailable => "文件存在，但部分数据不可用",
            _ => "正在读取…"
        };

}
