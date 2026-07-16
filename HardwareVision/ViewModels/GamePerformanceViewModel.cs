using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly IGameSessionRecorder? sessionRecorder;
    private readonly IGameEnergyTracker? energyTracker;
    private readonly IGamePerformanceLimitTracker? performanceLimitTracker;
    private readonly IGameSessionReportService sessionReportService;
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
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
    private bool autoRecordGameSessions;
    private string recordingStatusText = "自动记录已关闭";
    private string? recordingCurrentPath;
    private string? lastExportedFilePath;
    private string performanceLimitStatusText = "开始游戏采集后，将记录硬件明确上报的限制原因";
    private bool hasPerformanceLimitEvents;
    private GameSessionReportViewModel? sessionReport;

    public GamePerformanceViewModel()
        : this(
            new PresentMonGamePerformanceService(),
            Dispatcher.CurrentDispatcher,
            EmptyForegroundProcessTracker.Instance,
            null,
            new AppSettings(),
            new SettingsService())
    {
    }

    public GamePerformanceViewModel(Dispatcher dispatcher)
        : this(new PresentMonGamePerformanceService(), dispatcher, EmptyForegroundProcessTracker.Instance, null, new AppSettings(), new SettingsService())
    {
    }

    public GamePerformanceViewModel(IGamePerformanceService gamePerformanceService, Dispatcher dispatcher)
        : this(gamePerformanceService, dispatcher, EmptyForegroundProcessTracker.Instance, null, new AppSettings(), new SettingsService())
    {
    }

    public GamePerformanceViewModel(
        IGamePerformanceService gamePerformanceService,
        Dispatcher dispatcher,
        IForegroundProcessTracker foregroundProcessTracker)
        : this(gamePerformanceService, dispatcher, foregroundProcessTracker, null, new AppSettings(), new SettingsService())
    {
    }

    public GamePerformanceViewModel(
        IGamePerformanceService gamePerformanceService,
        Dispatcher dispatcher,
        IForegroundProcessTracker foregroundProcessTracker,
        IGameSessionRecorder? sessionRecorder,
        AppSettings settings,
        ISettingsService settingsService,
        IGameEnergyTracker? energyTracker = null,
        IGamePerformanceLimitTracker? performanceLimitTracker = null,
        IGameSessionReportService? sessionReportService = null)
    {
        this.gamePerformanceService = gamePerformanceService;
        this.dispatcher = dispatcher;
        this.foregroundProcessTracker = foregroundProcessTracker;
        this.sessionRecorder = sessionRecorder;
        this.energyTracker = energyTracker;
        this.performanceLimitTracker = performanceLimitTracker;
        this.sessionReportService = sessionReportService ?? new GameSessionReportService();
        this.settings = settings;
        this.settingsService = settingsService;
        autoRecordGameSessions = settings.RecordGameSessions;
        recordingStatusText = sessionRecorder is null
            ? (autoRecordGameSessions ? "自动记录将在捕获开始后启动" : "自动记录已关闭")
            : sessionRecorder.RecordingStatusText;
        recordingCurrentPath = sessionRecorder?.CurrentFilePath;
        statusText = gamePerformanceService.StatusText;
        captureState = gamePerformanceService.CaptureState;
        isCapturing = captureState == GameCaptureState.Capturing;

        gamePerformanceService.StatusChanged += OnStatusChanged;
        gamePerformanceService.CaptureStateChanged += OnCaptureStateChanged;
        if (sessionRecorder is not null)
        {
            sessionRecorder.StateChanged += OnRecorderStateChanged;
        }
        if (energyTracker is not null)
        {
            energyTracker.SnapshotChanged += OnEnergySnapshotChanged;
        }
        if (performanceLimitTracker is not null)
        {
            performanceLimitTracker.SnapshotChanged += OnPerformanceLimitSnapshotChanged;
            ApplyPerformanceLimitSnapshot(performanceLimitTracker.CurrentSnapshot);
        }

        uiRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = UiUpdateInterval
        };
        uiRefreshTimer.Tick += OnUiRefreshTimerTick;

        InitializeCharts();
        RefreshProcessesCommand = new AsyncRelayCommand(() => RefreshProcessesAsync(reportDetectionResult: false));
        DetectGameCommand = new AsyncRelayCommand(DetectGameAsync, CanDetectGame);
        StartCaptureCommand = new AsyncRelayCommand(StartCaptureAsync);
        StopCaptureCommand = new AsyncRelayCommand(StopCaptureAsync);
        ResetChartsCommand = new RelayCommand(ResetCharts);
        ExportCsvCommand = new AsyncRelayCommand(ExportCurrentWindowAsync);
        ExportCurrentWindowCommand = new AsyncRelayCommand(ExportCurrentWindowAsync);
        ExportCacheCommand = new AsyncRelayCommand(ExportCacheAsync);
        OpenRecordingDirectoryCommand = new RelayCommand(OpenRecordingDirectory);
        OpenCurrentRecordingCommand = new RelayCommand(OpenCurrentRecording);
        OpenLastExportCommand = new RelayCommand(OpenLastExport);
        OpenSessionReportCommand = new AsyncRelayCommand<GameSessionRecordInfo?>(OpenSessionReportAsync);
    }

    public ObservableCollection<GameProcessInfo> ProcessOptions { get; } = new();

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public ObservableCollection<RealtimeMetricChartViewModel> Charts { get; } = new();

    public ObservableCollection<int> ChartWindowOptions { get; } = new([30, 60, 120]);

    public ObservableCollection<GameSessionRecordInfo> RecentRecords { get; } = new();

    public ObservableCollection<GamePerformanceLimitEvent> PerformanceLimitEvents { get; } = new();

    public GameSessionReportViewModel? SessionReport
    {
        get => sessionReport;
        private set
        {
            if (!SetProperty(ref sessionReport, value)) return;
            OnPropertyChanged(nameof(HasSessionReport));
            OnPropertyChanged(nameof(HasNoSessionReport));
        }
    }

    public bool HasSessionReport => SessionReport is not null;

    public bool HasNoSessionReport => !HasSessionReport;

    public string PerformanceLimitStatusText
    {
        get => performanceLimitStatusText;
        private set => SetProperty(ref performanceLimitStatusText, value);
    }

    public bool HasPerformanceLimitEvents
    {
        get => hasPerformanceLimitEvents;
        private set
        {
            if (SetProperty(ref hasPerformanceLimitEvents, value))
            {
                OnPropertyChanged(nameof(HasNoPerformanceLimitEvents));
            }
        }
    }

    public bool HasNoPerformanceLimitEvents => !HasPerformanceLimitEvents;

    internal bool IsUiRefreshTimerEnabled => uiRefreshTimer.IsEnabled;

    internal void SuspendRealtimeUiForSessionReport() => uiRefreshTimer.Stop();

    internal void ResumeRealtimeUiAfterSessionReport()
    {
        if (isActive && !isDisposed && !HasSessionReport) uiRefreshTimer.Start();
    }

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

    public bool AutoRecordGameSessions
    {
        get => autoRecordGameSessions;
        set
        {
            if (!SetProperty(ref autoRecordGameSessions, value))
            {
                return;
            }

            settings.RecordGameSessions = value;
            OnPropertyChanged(nameof(ShowManualExport));
            RecordingStatusText = value
                ? (sessionRecorder?.RecordingStatusText ?? "自动记录将在捕获开始后启动")
                : "自动记录已关闭（当前已开始的记录会在本次会话结束后停止）";
            _ = settingsService.UpdateAsync(updated => updated.RecordGameSessions = value);
        }
    }

    public bool ShowManualExport => !AutoRecordGameSessions;

    public string RecordingStatusText
    {
        get => recordingStatusText;
        private set => SetProperty(ref recordingStatusText, value);
    }

    public string? RecordingCurrentPath
    {
        get => recordingCurrentPath;
        private set
        {
            if (SetProperty(ref recordingCurrentPath, value))
            {
                OnPropertyChanged(nameof(HasRecordingCurrentPath));
            }
        }
    }

    public bool HasRecordingCurrentPath => !string.IsNullOrWhiteSpace(RecordingCurrentPath);

    public string? LastExportedFilePath
    {
        get => lastExportedFilePath;
        private set
        {
            if (SetProperty(ref lastExportedFilePath, value))
            {
                OnPropertyChanged(nameof(HasLastExportedFile));
            }
        }
    }

    public bool HasLastExportedFile => !string.IsNullOrWhiteSpace(LastExportedFilePath);

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

    public IAsyncRelayCommand ExportCurrentWindowCommand { get; }

    public IAsyncRelayCommand ExportCacheCommand { get; }

    public IRelayCommand OpenRecordingDirectoryCommand { get; }

    public IRelayCommand OpenCurrentRecordingCommand { get; }

    public IRelayCommand OpenLastExportCommand { get; }

    public IAsyncRelayCommand<GameSessionRecordInfo?> OpenSessionReportCommand { get; }

    public void SetActive(bool active)
    {
        if (isDisposed || isActive == active)
        {
            return;
        }

        isActive = active;
        if (active)
        {
            if (autoRecordGameSessions != settings.RecordGameSessions)
            {
                SetProperty(ref autoRecordGameSessions, settings.RecordGameSessions, nameof(AutoRecordGameSessions));
                OnPropertyChanged(nameof(ShowManualExport));
            }

            _ = RefreshProcessesAsync(reportDetectionResult: false);
            _ = RefreshRecentRecordsAsync();
            if (performanceLimitTracker is not null)
            {
                ApplyPerformanceLimitSnapshot(performanceLimitTracker.CurrentSnapshot);
            }
            UpdateMetrics();
            if (!HasSessionReport) uiRefreshTimer.Start();
        }
        else
        {
            uiRefreshTimer.Stop();
            CancelProcessRefresh();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        GameSessionReportViewModel? reportViewModel = SessionReport;
        SessionReport = null;
        reportViewModel?.Dispose();
        uiRefreshTimer.Stop();
        uiRefreshTimer.Tick -= OnUiRefreshTimerTick;
        CancelProcessRefresh();
        gamePerformanceService.StatusChanged -= OnStatusChanged;
        gamePerformanceService.CaptureStateChanged -= OnCaptureStateChanged;
        if (sessionRecorder is not null)
        {
            sessionRecorder.StateChanged -= OnRecorderStateChanged;
        }
        if (energyTracker is not null)
        {
            energyTracker.SnapshotChanged -= OnEnergySnapshotChanged;
        }
        if (performanceLimitTracker is not null)
        {
            performanceLimitTracker.SnapshotChanged -= OnPerformanceLimitSnapshotChanged;
        }
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

    private async Task ExportCurrentWindowAsync()
    {
        string? path = await gamePerformanceService.ExportWindowCsvAsync(
            GetExportDirectory(),
            TimeSpan.FromSeconds(SelectedChartWindowSeconds),
            SelectedProcess?.ProcessName);
        ApplyExportResult(path);
    }

    private async Task ExportCacheAsync()
    {
        string? path = await gamePerformanceService.ExportCacheCsvAsync(
            GetExportDirectory(),
            SelectedProcess?.ProcessName);
        ApplyExportResult(path);
    }

    private void ApplyExportResult(string? path)
    {
        LastExportedFilePath = path;
        StatusText = path is null ? "没有可导出的样本" : $"已导出 CSV：{path}";
    }

    private string GetExportDirectory()
    {
        string root = sessionRecorder?.RootDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HardwareVision", "GameSessions");
        return Path.Combine(root, "Exports");
    }

    private async Task RefreshRecentRecordsAsync()
    {
        if (sessionRecorder is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<GameSessionRecordInfo> records = await sessionRecorder.GetRecentRecordsAsync(10).ConfigureAwait(false);
            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                RecentRecords.Clear();
                foreach (GameSessionRecordInfo record in records)
                {
                    RecentRecords.Add(record);
                }
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Recent game session records could not be loaded.", exception,
                $"game-recent-records:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
        }
    }

    private void OpenRecordingDirectory() => OpenPath(sessionRecorder?.RootDirectory ?? GetExportDirectory(), selectFile: false);

    private void OpenCurrentRecording() => OpenPath(RecordingCurrentPath, selectFile: true);

    private void OpenLastExport() => OpenPath(LastExportedFilePath, selectFile: true);

    private async Task OpenSessionReportAsync(GameSessionRecordInfo? record)
    {
        if (record is null || isDisposed) return;
        GameSessionReportViewModel? previous = SessionReport;
        previous?.Dispose();
        GameSessionReportViewModel detail = new(record, sessionReportService, CloseSessionReport);
        SessionReport = detail;
        SuspendRealtimeUiForSessionReport();
        await detail.LoadAsync();
    }

    private void CloseSessionReport()
    {
        GameSessionReportViewModel? detail = SessionReport;
        if (detail is null) return;
        SessionReport = null;
        detail.Dispose();
        ResumeRealtimeUiAfterSessionReport();
    }

    private static void OpenPath(string? path, bool selectFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string target = path;
        bool fileExists = File.Exists(target);
        if (selectFile && !fileExists)
        {
            target = Path.GetDirectoryName(target) ?? target;
            selectFile = false;
        }

        if (!selectFile)
        {
            Directory.CreateDirectory(target);
        }

        try
        {
            ProcessStartInfo startInfo = new("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(selectFile ? $"/select,{target}" : target);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLogger.LogError("Game session path could not be opened.", exception,
                $"game-open-path:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
        }
    }

    private void ResetCharts()
    {
        foreach (RealtimeMetricChartViewModel chart in Charts)
        {
            chart.Clear();
        }

        ViewModelHelpers.UpdateMetricCollection(Metrics, Array.Empty<HardwareMetric>());
    }

    private void OnUiRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!isActive || isDisposed || HasSessionReport)
        {
            return;
        }

        ApplyFrameUpdate();
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

    private void OnRecorderStateChanged(object? sender, GameSessionRecorderStateChangedEventArgs e)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            RecordingStatusText = e.StatusText + (e.DroppedSamples > 0 ? $"（丢弃 {e.DroppedSamples} 条记录样本）" : string.Empty);
            RecordingCurrentPath = e.CurrentPath;
            if (e.CompletedRecord is not null)
            {
                _ = RefreshRecentRecordsAsync();
            }
        });
    }

    private void OnEnergySnapshotChanged(object? sender, GameEnergySnapshot e)
    {
        if (!isActive || isDisposed)
        {
            return;
        }

        ViewModelHelpers.Dispatch(dispatcher, UpdateMetrics);
    }

    private void OnPerformanceLimitSnapshotChanged(object? sender, GamePerformanceLimitSnapshot snapshot)
    {
        if (!isActive || isDisposed)
        {
            return;
        }

        ViewModelHelpers.Dispatch(dispatcher, () => ApplyPerformanceLimitSnapshot(snapshot));
    }

    internal void ApplyPerformanceLimitSnapshot(GamePerformanceLimitSnapshot snapshot)
    {
        int desiredCount = Math.Min(50, snapshot.Events.Count);
        for (int desiredIndex = 0; desiredIndex < desiredCount; desiredIndex++)
        {
            GamePerformanceLimitEvent desired = snapshot.Events[desiredIndex];
            int existingIndex = FindPerformanceLimitEvent(desired.EventId, desiredIndex);
            if (existingIndex < 0)
            {
                PerformanceLimitEvents.Insert(desiredIndex, desired);
            }
            else
            {
                if (existingIndex != desiredIndex)
                {
                    PerformanceLimitEvents.Move(existingIndex, desiredIndex);
                }

                if (!ReferenceEquals(PerformanceLimitEvents[desiredIndex], desired))
                {
                    PerformanceLimitEvents[desiredIndex] = desired;
                }
            }
        }

        while (PerformanceLimitEvents.Count > desiredCount)
        {
            PerformanceLimitEvents.RemoveAt(PerformanceLimitEvents.Count - 1);
        }

        HasPerformanceLimitEvents = PerformanceLimitEvents.Count > 0;
        string support = $"CPU {FormatSupportStatus(snapshot.CpuSupportStatus)} · GPU {FormatSupportStatus(snapshot.GpuSupportStatus)}";
        PerformanceLimitStatusText = snapshot.IsTracking
            ? snapshot.ActiveEventCount > 0
                ? $"{support} · {snapshot.ActiveEventCount} 个活动事件"
                : support
            : snapshot.CpuSupportStatus == PerformanceLimitSupportStatus.NotStarted
                && snapshot.GpuSupportStatus == PerformanceLimitSupportStatus.NotStarted
                ? "开始游戏采集后，将记录硬件明确上报的限制原因"
                : $"会话已结束 · {support} · 共 {snapshot.Events.Count} 条";
    }

    internal static string FormatSupportStatus(PerformanceLimitSupportStatus status) => status switch
    {
        PerformanceLimitSupportStatus.NotStarted => "尚未采集",
        PerformanceLimitSupportStatus.SupportedNormal => "正常",
        PerformanceLimitSupportStatus.ActiveLimit => "存在限制",
        PerformanceLimitSupportStatus.Unsupported => "不支持",
        PerformanceLimitSupportStatus.TemporarilyUnavailable => "暂时不可用",
        _ => "未知"
    };

    private int FindPerformanceLimitEvent(long eventId, int startIndex)
    {
        for (int index = startIndex; index < PerformanceLimitEvents.Count; index++)
        {
            if (PerformanceLimitEvents[index].EventId == eventId)
            {
                return index;
            }
        }

        return -1;
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

        GameEnergySnapshot energy = energyTracker?.CurrentSnapshot ?? GameEnergySnapshot.Empty;
        string coverage = GameEnergyFormatting.FormatCoverage(energy.CoveragePercent);
        string components = string.IsNullOrWhiteSpace(energy.IncludedComponents)
            ? "未检测到可用的 CPU/GPU 整机功耗传感器"
            : energy.IncludedComponents;
        string disclaimer = $"传感器估算值；仅包含 {components}，不代表整机墙上功耗；有效积分覆盖率 {coverage}。";
        yield return EnergyMetric(
            "game.energy.estimated",
            "估算能耗",
            $"Estimated CPU + GPU Energy (monotonic trapezoidal integration). {disclaimer}",
            GameEnergyFormatting.FormatEnergy(energy.EstimatedEnergyWh),
            energy.EstimatedEnergyWh.HasValue,
            9);
        yield return EnergyMetric(
            "game.power.estimated.current",
            "当前估算功耗",
            $"Current Selected CPU + GPU Power. {disclaimer}",
            GameEnergyFormatting.FormatPower(energy.CurrentEstimatedPowerWatts),
            energy.CurrentEstimatedPowerWatts.HasValue,
            10);
        yield return EnergyMetric(
            "game.power.estimated.average",
            "平均估算功耗",
            $"Average Estimated CPU + GPU Power over valid integration time. {disclaimer}",
            GameEnergyFormatting.FormatPower(energy.AverageEstimatedPowerWatts),
            energy.AverageEstimatedPowerWatts.HasValue,
            11);
    }

    private static HardwareMetric EnergyMetric(
        string id,
        string displayName,
        string technicalName,
        string value,
        bool isAvailable,
        int order)
    {
        return HardwareMetricService.FromValue(
            id,
            "game-energy",
            HardwareMetricCategory.System,
            displayName,
            technicalName,
            value,
            string.Empty,
            "传感器估算",
            isAvailable ? MetricAvailability.Available : MetricAvailability.NotReported,
            "仅依据可用 CPU/GPU 功耗传感器估算，存在采样缺口时不跨缺口积分。",
            true,
            true,
            order,
            "游戏性能");
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
