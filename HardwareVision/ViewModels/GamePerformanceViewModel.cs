using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.ViewModels;

public sealed class GamePerformanceViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(500);
    private readonly IGamePerformanceService gamePerformanceService;
    private readonly Dispatcher dispatcher;
    private GameProcessInfo? selectedProcess;
    private string statusText;
    private bool isCapturing;
    private bool isActive;
    private bool isDisposed;
    private int selectedChartWindowSeconds = 60;
    private long lastUiUpdateTicks;

    public GamePerformanceViewModel()
        : this(new PresentMonGamePerformanceService(), Dispatcher.CurrentDispatcher)
    {
    }

    public GamePerformanceViewModel(Dispatcher dispatcher)
        : this(new PresentMonGamePerformanceService(), dispatcher)
    {
    }

    public GamePerformanceViewModel(IGamePerformanceService gamePerformanceService, Dispatcher dispatcher)
    {
        this.gamePerformanceService = gamePerformanceService;
        this.dispatcher = dispatcher;
        statusText = gamePerformanceService.StatusText;

        gamePerformanceService.FrameReceived += OnFrameReceived;
        gamePerformanceService.StatusChanged += OnStatusChanged;

        InitializeCharts();
        RefreshProcessesCommand = new AsyncRelayCommand(RefreshProcessesAsync);
        StartCaptureCommand = new AsyncRelayCommand(StartCaptureAsync);
        StopCaptureCommand = new AsyncRelayCommand(StopCaptureAsync);
        ResetChartsCommand = new RelayCommand(ResetCharts);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);
    }

    public ObservableCollection<GameProcessInfo> ProcessOptions { get; } = new();

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public ObservableCollection<RealtimeMetricChartViewModel> Charts { get; } = new();

    public ObservableCollection<int> ChartWindowOptions { get; } = new([30, 60, 120]);

    public GameProcessInfo? SelectedProcess
    {
        get => selectedProcess;
        set => SetProperty(ref selectedProcess, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public bool IsCapturing
    {
        get => isCapturing;
        private set => SetProperty(ref isCapturing, value);
    }

    public int SelectedChartWindowSeconds
    {
        get => selectedChartWindowSeconds;
        set
        {
            int normalized = value switch
            {
                <= 30 => 30,
                <= 60 => 60,
                _ => 120
            };

            if (SetProperty(ref selectedChartWindowSeconds, normalized))
            {
                foreach (RealtimeMetricChartViewModel chart in Charts)
                {
                    chart.WindowSeconds = normalized;
                }

                UpdateMetrics();
            }
        }
    }

    public IAsyncRelayCommand RefreshProcessesCommand { get; }

    public IAsyncRelayCommand StartCaptureCommand { get; }

    public IAsyncRelayCommand StopCaptureCommand { get; }

    public IRelayCommand ResetChartsCommand { get; }

    public IAsyncRelayCommand ExportCsvCommand { get; }

    public void SetActive(bool active)
    {
        if (isDisposed || isActive == active)
        {
            return;
        }

        isActive = active;
        if (active)
        {
            _ = RefreshProcessesAsync();
            UpdateMetrics();
        }
        else
        {
            _ = StopCaptureAsync();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        gamePerformanceService.FrameReceived -= OnFrameReceived;
        gamePerformanceService.StatusChanged -= OnStatusChanged;
        gamePerformanceService.Dispose();
        isDisposed = true;
    }

    private async Task RefreshProcessesAsync()
    {
        IReadOnlyList<GameProcessInfo> processes = await gamePerformanceService.GetCandidateProcessesAsync();

        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            string? selectedKey = SelectedProcess is null ? null : $"{SelectedProcess.ProcessName}:{SelectedProcess.ProcessId}";
            ProcessOptions.Clear();
            foreach (GameProcessInfo process in processes)
            {
                ProcessOptions.Add(process);
            }

            SelectedProcess = ProcessOptions.FirstOrDefault(process => $"{process.ProcessName}:{process.ProcessId}" == selectedKey)
                ?? ProcessOptions.FirstOrDefault();

            if (!gamePerformanceService.IsCaptureAvailable)
            {
                StatusText = "采集组件未就绪";
            }
        });
    }

    private async Task StartCaptureAsync()
    {
        if (SelectedProcess is null)
        {
            await RefreshProcessesAsync();
        }

        if (SelectedProcess is null)
        {
            StatusText = "未选择进程";
            return;
        }

        if (!gamePerformanceService.IsCaptureAvailable)
        {
            StatusText = "采集组件未就绪";
            return;
        }

        ResetCharts();
        await gamePerformanceService.StartCaptureAsync(SelectedProcess);
        IsCapturing = true;
    }

    private async Task StopCaptureAsync()
    {
        await gamePerformanceService.StopCaptureAsync();
        IsCapturing = false;
        UpdateMetrics();
    }

    private async Task ExportCsvAsync()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HardwareVision");
        string? path = await gamePerformanceService.ExportCsvAsync(directory);
        StatusText = path is null ? "没有可导出的样本" : "已导出 CSV";
    }

    private void ResetCharts()
    {
        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.Clear();
        }

        ViewModelHelpers.UpdateMetricCollection(Metrics, Array.Empty<HardwareMetric>());
        Interlocked.Exchange(ref lastUiUpdateTicks, 0);
    }

    private void OnFrameReceived(object? sender, GameFrameSample sample)
    {
        long nowTicks = DateTimeOffset.UtcNow.Ticks;
        long previousTicks = Interlocked.Read(ref lastUiUpdateTicks);
        if (nowTicks - previousTicks < UiUpdateInterval.Ticks)
        {
            return;
        }

        Interlocked.Exchange(ref lastUiUpdateTicks, nowTicks);
        ViewModelHelpers.Dispatch(dispatcher, ApplyFrameUpdate);
    }

    private void OnStatusChanged(object? sender, string e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            StatusText = e;
            IsCapturing = e.StartsWith("采集中", StringComparison.Ordinal);
        });
    }

    private void ApplyFrameUpdate()
    {
        GamePerformanceSnapshot snapshot = GetSnapshot();
        if (Charts.Count >= 5)
        {
            Charts[0].Append(snapshot.CurrentFps);
            Charts[1].Append(snapshot.AverageFrameTimeMs);
            Charts[2].Append(snapshot.AverageCpuBusyMs);
            Charts[3].Append(snapshot.AverageGpuTimeMs);
            Charts[4].Append(snapshot.AverageLatencyMs);
        }

        UpdateMetrics(snapshot);
    }

    private void UpdateMetrics()
    {
        UpdateMetrics(GetSnapshot());
    }

    private void UpdateMetrics(GamePerformanceSnapshot snapshot)
    {
        ViewModelHelpers.UpdateMetricCollection(Metrics, BuildMetrics(snapshot));
    }

    private GamePerformanceSnapshot GetSnapshot()
    {
        return GameFrameStatisticsCalculator.Calculate(
            gamePerformanceService.RecentSamples,
            TimeSpan.FromSeconds(SelectedChartWindowSeconds));
    }

    private void InitializeCharts()
    {
        Charts.Add(new RealtimeMetricChartViewModel("FPS", "FPS", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("帧时间", "ms", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("CPU 帧耗时", "ms", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("GPU 帧耗时", "ms", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("延迟", "ms", 0d, double.NaN));

        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.WindowSeconds = selectedChartWindowSeconds;
        }
    }

    private IEnumerable<HardwareMetric> BuildMetrics(GamePerformanceSnapshot snapshot)
    {
        yield return Metric("game.fps.current", "当前 FPS", "Current FPS", snapshot.CurrentFps, "FPS", 0);
        yield return Metric("game.fps.average", "平均 FPS", "Average FPS", snapshot.AverageFps, "FPS", 1);
        yield return Metric("game.fps.one.percent.low", "1% Low", "1% Low FPS", snapshot.OnePercentLowFps, "FPS", 2);
        yield return Metric("game.fps.zero.point.one.low", "0.1% Low", "0.1% Low FPS", snapshot.ZeroPointOnePercentLowFps, "FPS", 3);
        yield return Metric("game.frame.time.average", "平均帧时间", "Average Frame Time", snapshot.AverageFrameTimeMs, "ms", 4);
        yield return Metric("game.cpu.busy.average", "CPU 帧耗时", "Average CPU Busy", snapshot.AverageCpuBusyMs, "ms", 5);
        yield return Metric("game.gpu.time.average", "GPU 帧耗时", "Average GPU Time", snapshot.AverageGpuTimeMs, "ms", 6);
        yield return Metric("game.latency.average", "平均延迟", "Average Latency", snapshot.AverageLatencyMs, "ms", 7);
        yield return Metric("game.sample.count", "样本数", "Sample Count", snapshot.SampleCount > 0 ? snapshot.SampleCount.ToString(CultureInfo.InvariantCulture) : null, string.Empty, 8);
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, double? value, string unit, int order)
    {
        return Metric(
            id,
            displayName,
            technicalName,
            value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : null,
            unit,
            order);
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, string? value, string unit, int order)
    {
        return HardwareMetricService.FromValue(
            id,
            "game",
            HardwareMetricCategory.System,
            displayName,
            technicalName,
            value,
            unit,
            "PresentMon",
            string.IsNullOrWhiteSpace(value) ? MetricAvailability.NotReported : MetricAvailability.Available,
            string.Empty,
            true,
            true,
            order,
            "游戏性能");
    }
}
