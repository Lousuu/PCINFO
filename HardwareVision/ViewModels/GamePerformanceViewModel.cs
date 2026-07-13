using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.ViewModels;

public sealed class GamePerformanceViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(500);
    private readonly IGamePerformanceService gamePerformanceService;
    private readonly IForegroundProcessTracker foregroundProcessTracker;
    private readonly Dispatcher dispatcher;
    private readonly List<GameProcessInfo> allProcessOptions = new();
    private GameProcessInfo? selectedProcess;
    private GameProcessInfo? rememberedSelection;
    private CancellationTokenSource? refreshCancellation;
    private string processSearchText = string.Empty;
    private string statusText;
    private GameCaptureState captureState;
    private bool isCapturing;
    private bool isActive;
    private bool isDisposed;
    private bool isApplyingProcessOptions;
    private bool isDetectionInProgress;
    private GameProcessSelectionSource selectionSource;
    private int selectedChartWindowSeconds = 60;
    private int refreshGeneration;
    private long lastUiUpdateTicks;

    public GamePerformanceViewModel()
        : this(
            new PresentMonGamePerformanceService(),
            Dispatcher.CurrentDispatcher,
            EmptyForegroundProcessTracker.Instance)
    {
    }

    public GamePerformanceViewModel(Dispatcher dispatcher)
        : this(new PresentMonGamePerformanceService(), dispatcher, EmptyForegroundProcessTracker.Instance)
    {
    }

    public GamePerformanceViewModel(IGamePerformanceService gamePerformanceService, Dispatcher dispatcher)
        : this(gamePerformanceService, dispatcher, EmptyForegroundProcessTracker.Instance)
    {
    }

    public GamePerformanceViewModel(
        IGamePerformanceService gamePerformanceService,
        Dispatcher dispatcher,
        IForegroundProcessTracker foregroundProcessTracker)
    {
        this.gamePerformanceService = gamePerformanceService;
        this.dispatcher = dispatcher;
        this.foregroundProcessTracker = foregroundProcessTracker;
        statusText = gamePerformanceService.StatusText;
        captureState = gamePerformanceService.CaptureState;
        isCapturing = captureState == GameCaptureState.Capturing;

        gamePerformanceService.FrameReceived += OnFrameReceived;
        gamePerformanceService.StatusChanged += OnStatusChanged;
        gamePerformanceService.CaptureStateChanged += OnCaptureStateChanged;

        InitializeCharts();
        RefreshProcessesCommand = new AsyncRelayCommand(() => RefreshProcessesAsync(reportDetectionResult: false));
        DetectGameCommand = new AsyncRelayCommand(DetectGameAsync, CanDetectGame);
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
        set
        {
            if (!isApplyingProcessOptions && GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState))
            {
                return;
            }

            if (!SetProperty(ref selectedProcess, value) || isApplyingProcessOptions)
            {
                return;
            }

            if (value is not null)
            {
                rememberedSelection = value;
                selectionSource = string.IsNullOrWhiteSpace(ProcessSearchText)
                    ? GameProcessSelectionSource.Manual
                    : GameProcessSelectionSource.Search;
            }
            else if (string.IsNullOrWhiteSpace(ProcessSearchText))
            {
                rememberedSelection = null;
                selectionSource = GameProcessSelectionSource.None;
            }
        }
    }

    public string ProcessSearchText
    {
        get => processSearchText;
        set
        {
            if (SetProperty(ref processSearchText, value ?? string.Empty)
                && !GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState))
            {
                ApplyProcessFilter();
            }
        }
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

    public GameCaptureState CaptureState
    {
        get => captureState;
        private set => SetProperty(ref captureState, value);
    }

    public bool CanSelectProcess => !GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState)
        && !IsDetectionInProgress;

    public bool IsDetectionInProgress
    {
        get => isDetectionInProgress;
        private set
        {
            if (SetProperty(ref isDetectionInProgress, value))
            {
                OnPropertyChanged(nameof(CanSelectProcess));
                DetectGameCommand.NotifyCanExecuteChanged();
            }
        }
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

    public IAsyncRelayCommand DetectGameCommand { get; }

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
            _ = RefreshProcessesAsync(reportDetectionResult: false);
            UpdateMetrics();
        }
        else
        {
            CancelProcessRefresh();
            _ = StopCaptureAsync();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        CancelProcessRefresh();
        gamePerformanceService.FrameReceived -= OnFrameReceived;
        gamePerformanceService.StatusChanged -= OnStatusChanged;
        gamePerformanceService.CaptureStateChanged -= OnCaptureStateChanged;
        gamePerformanceService.Dispose();
    }

    private async Task DetectGameAsync()
    {
        if (!CanDetectGame())
        {
            return;
        }

        IsDetectionInProgress = true;
        try
        {
            await RefreshProcessesAsync(reportDetectionResult: true);
        }
        finally
        {
            if (!isDisposed)
            {
                ViewModelHelpers.Dispatch(dispatcher, () => IsDetectionInProgress = false);
            }
        }
    }

    private async Task RefreshProcessesAsync(bool reportDetectionResult)
    {
        int generation = Interlocked.Increment(ref refreshGeneration);
        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref refreshCancellation, cancellation);
        TryCancel(previous);

        try
        {
            IReadOnlyList<GameProcessInfo> processes = await gamePerformanceService
                .GetCandidateProcessesAsync(cancellation.Token)
                .ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            ForegroundProcessSnapshot? foreground = foregroundProcessTracker.GetSnapshot();
            IReadOnlyList<GameProcessDetectionResult> scored = GameProcessScorer.ScoreAndSort(
                processes,
                foreground,
                DateTimeOffset.UtcNow);

            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                if (isDisposed || generation != Volatile.Read(ref refreshGeneration))
                {
                    return;
                }

                ApplyProcessRefresh(scored, reportDetectionResult);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "Game process refresh failed.",
                exception,
                $"game-process-refresh:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
            if (!isDisposed && generation == Volatile.Read(ref refreshGeneration))
            {
                ViewModelHelpers.Dispatch(dispatcher, () => StatusText = "刷新进程失败，请重试");
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref refreshCancellation, null, cancellation);
            cancellation.Dispose();
        }
    }

    private void ApplyProcessRefresh(
        IReadOnlyList<GameProcessDetectionResult> scored,
        bool reportDetectionResult)
    {
        allProcessOptions.Clear();
        allProcessOptions.AddRange(scored.Select(result => result.Process));

        if (GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState))
        {
            return;
        }

        GameProcessInfo? previousSelection = rememberedSelection ?? SelectedProcess;
        GameProcessInfo? retainedSelection = previousSelection is null
            ? null
            : allProcessOptions.FirstOrDefault(candidate =>
                GameProcessSelectionPolicy.IsSameProcess(previousSelection, candidate));
        bool hasValidUserSelection = (selectionSource is GameProcessSelectionSource.Manual
                or GameProcessSelectionSource.Search)
            && retainedSelection is not null;
        if (retainedSelection is null)
        {
            rememberedSelection = null;
            selectionSource = GameProcessSelectionSource.None;
        }
        else
        {
            rememberedSelection = retainedSelection;
        }

        GameProcessDetectionDecision decision = GameProcessScorer.ChooseHighConfidence(scored);
        bool canAutoSelect = GameProcessSelectionPolicy.CanAutoSelect(
            CaptureState,
            hasValidUserSelection,
            !string.IsNullOrWhiteSpace(ProcessSearchText));
        bool selectedAutomatically = canAutoSelect && decision.Selection is not null;
        if (selectedAutomatically)
        {
            rememberedSelection = decision.Selection!.Process;
            selectionSource = GameProcessSelectionSource.Automatic;
        }

        ApplyProcessFilter();

        if (!gamePerformanceService.IsCaptureAvailable)
        {
            StatusText = gamePerformanceService.StatusText;
            return;
        }

        if (reportDetectionResult)
        {
            if (hasValidUserSelection && retainedSelection is not null)
            {
                StatusText = $"已保留手动选择：{GetExecutableName(retainedSelection)}";
            }
            else if (selectedAutomatically && decision.Selection is not null)
            {
                StatusText = $"已识别：{GetExecutableName(decision.Selection.Process)}\n依据：{decision.Selection.Reason}";
            }
            else if (!string.IsNullOrWhiteSpace(ProcessSearchText))
            {
                StatusText = "已保留搜索条件，请从筛选结果中选择";
            }
            else if (decision.IsAmbiguous)
            {
                StatusText = "检测到多个可能的游戏，请搜索或手动选择";
            }
            else if (decision.HasLikelyCandidates)
            {
                StatusText = "检测到可能的游戏，但置信度不足，请搜索或手动选择";
            }
            else
            {
                StatusText = "未检测到可能的游戏进程";
            }
        }
        else if (selectedAutomatically && decision.Selection is not null)
        {
            StatusText = $"已自动识别：{GetExecutableName(decision.Selection.Process)}";
        }
    }

    private void ApplyProcessFilter()
    {
        IReadOnlyList<GameProcessInfo> filtered = GameProcessFilter.Filter(allProcessOptions, ProcessSearchText);
        GameProcessInfo? desiredSelection = rememberedSelection is null
            ? null
            : filtered.FirstOrDefault(candidate =>
                GameProcessSelectionPolicy.IsSameProcess(rememberedSelection, candidate));

        isApplyingProcessOptions = true;
        try
        {
            ProcessOptions.Clear();
            foreach (GameProcessInfo process in filtered)
            {
                ProcessOptions.Add(process);
            }

            SetProperty(ref selectedProcess, desiredSelection, nameof(SelectedProcess));
        }
        finally
        {
            isApplyingProcessOptions = false;
        }
    }

    private bool CanDetectGame()
    {
        return !isDisposed
            && !IsDetectionInProgress
            && !GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState);
    }

    private void CancelProcessRefresh()
    {
        Interlocked.Increment(ref refreshGeneration);
        TryCancel(Interlocked.Exchange(ref refreshCancellation, null));
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string GetExecutableName(GameProcessInfo process)
    {
        string processName = process.ProcessName.Trim();
        return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : $"{processName}.exe";
    }

    private async Task StartCaptureAsync()
    {
        if (SelectedProcess is null)
        {
            await RefreshProcessesAsync(reportDetectionResult: false);
        }

        if (SelectedProcess is null)
        {
            StatusText = "未选择进程，请搜索或点击“识别游戏”";
            return;
        }

        if (!gamePerformanceService.IsCaptureAvailable)
        {
            StatusText = gamePerformanceService.StatusText;
            return;
        }

        GameProcessInfo captureTarget = SelectedProcess;
        ResetCharts();
        await gamePerformanceService.StartCaptureAsync(captureTarget);
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
        });
    }

    private void OnCaptureStateChanged(object? sender, GameCaptureStateChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            bool wasLocked = GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState);
            CaptureState = e.State;
            StatusText = e.StatusText;
            IsCapturing = e.State == GameCaptureState.Capturing;
            bool isLocked = GameProcessSelectionPolicy.IsCaptureTargetLocked(CaptureState);
            OnPropertyChanged(nameof(CanSelectProcess));
            DetectGameCommand.NotifyCanExecuteChanged();
            if (wasLocked && !isLocked)
            {
                ApplyProcessFilter();
            }
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
            Charts[4].Append(snapshot.AverageDisplayLatencyMs);
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
        return gamePerformanceService.GetSnapshot(TimeSpan.FromSeconds(SelectedChartWindowSeconds));
    }

    private void InitializeCharts()
    {
        Charts.Add(new RealtimeMetricChartViewModel("FPS", "FPS", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("帧时间", "ms", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("CPU 忙碌时间", "ms", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("GPU 总时间", "ms", 0d, double.NaN));
        Charts.Add(new RealtimeMetricChartViewModel("显示延迟", "ms", 0d, double.NaN));

        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.WindowSeconds = selectedChartWindowSeconds;
        }
    }

    private IEnumerable<HardwareMetric> BuildMetrics(GamePerformanceSnapshot snapshot)
    {
        yield return Metric(
            "game.fps.current",
            "当前 FPS",
            "Rolling 1-second frame-time mean converted to FPS",
            snapshot.CurrentFps,
            "FPS",
            0);
        yield return Metric(
            "game.fps.average",
            "平均 FPS",
            "Application FPS from mean PresentMon FrameTime over the selected window",
            snapshot.AverageFps,
            "FPS",
            1);
        yield return LowMetric(
            "game.fps.one.percent.low",
            "1% Low",
            "Slowest 1% frame-time mean converted to FPS (minimum 100 samples)",
            snapshot.OnePercentLowFps,
            2);
        yield return LowMetric(
            "game.fps.zero.point.one.low",
            "0.1% Low",
            "Slowest 0.1% frame-time mean converted to FPS (minimum 1000 samples)",
            snapshot.ZeroPointOnePercentLowFps,
            3);
        yield return Metric("game.frame.time.average", "平均帧时间", "Average Frame Time", snapshot.AverageFrameTimeMs, "ms", 4);
        yield return Metric("game.cpu.busy.average", "CPU 忙碌时间", "Average CPU Busy", snapshot.AverageCpuBusyMs, "ms", 5);
        yield return Metric(
            "game.gpu.time.average",
            "GPU 总时间",
            "Average GPU Time (GPU Busy + GPU Wait)",
            snapshot.AverageGpuTimeMs,
            "ms",
            6);
        yield return Metric(
            "game.latency.average",
            "显示延迟",
            "Average Display Latency (frame start to scan-out)",
            snapshot.AverageDisplayLatencyMs,
            "ms",
            7);
        yield return Metric("game.sample.count", "样本数", "Sample Count", snapshot.SampleCount > 0 ? snapshot.SampleCount.ToString(CultureInfo.InvariantCulture) : null, string.Empty, 8);
    }

    private static HardwareMetric LowMetric(
        string id,
        string displayName,
        string technicalName,
        double? value,
        int order)
    {
        return Metric(
            id,
            displayName,
            technicalName,
            value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "N/A",
            value.HasValue ? "FPS" : string.Empty,
            order);
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
