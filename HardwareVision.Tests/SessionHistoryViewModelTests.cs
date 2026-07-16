using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class SessionHistoryViewModelTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session history 01 zero records text", ZeroRecordsText),
        ("Session history 02 five records show all", FiveRecordsShowAll),
        ("Session history 03 ten records have no more", TenRecordsHaveNoMore),
        ("Session history 04 eleven records show ten", ElevenRecordsShowTen),
        ("Session history 05 pages append without duplicates", PagesAppendWithoutDuplicates),
        ("Session history 06 collapse returns to ten", CollapseReturnsToTen),
        ("Session history 07 expand after collapse keeps order", ExpandAfterCollapseKeepsOrder),
        ("Session history 08 XAML uses bounded recycling list", XamlUsesBoundedRecyclingList),
        ("Session history 09 concurrent load-more is single-flight", ConcurrentLoadMoreIsSingleFlight),
        ("Session history 10 deactivation cancels stale page", DeactivationCancelsStalePage),
        ("Session history 11 load failure preserves current page", LoadFailurePreservesCurrentPage)
    ];

    private static void ZeroRecordsText()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page([], 0, false), true);
        TestSupport.Equal("暂无会话记录", viewModel.SessionRecordCountText, "zero text");
    }

    private static void FiveRecordsShowAll()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(0, 5), 5, false), true);
        TestSupport.Equal(5, viewModel.DisplayedSessionRecordCount, "five displayed");
        TestSupport.Equal("已显示全部 5 条记录", viewModel.SessionRecordCountText, "five text");
    }

    private static void TenRecordsHaveNoMore()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(0, 10), 10, false), true);
        TestSupport.False(viewModel.HasMoreSessionRecords, "ten HasMore");
        TestSupport.False(viewModel.CanCollapseSessionRecords, "ten collapse");
    }

    private static void ElevenRecordsShowTen()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(0, 10), 11, true), true);
        TestSupport.Equal("已显示 10 / 11 条", viewModel.SessionRecordCountText, "eleven text");
        TestSupport.True(viewModel.HasMoreSessionRecords, "eleven HasMore");
    }

    private static void PagesAppendWithoutDuplicates()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(0, 10), 25, true), true);
        GameSessionRecordInfo[] second = Records(9, 11);
        viewModel.ApplySessionRecordPageForDiagnostics(Page(second, 25, true, 10), false);
        TestSupport.Equal(20, viewModel.DisplayedSessionRecordCount, "deduplicated append count");
        TestSupport.Equal(20, viewModel.RecentRecords.Select(item => item.CsvPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "duplicate paths");
    }

    private static void CollapseReturnsToTen()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(0, 20), 25, true), true);
        viewModel.CollapseSessionRecordsForDiagnostics();
        TestSupport.Equal(10, viewModel.DisplayedSessionRecordCount, "collapsed count");
        TestSupport.True(viewModel.HasMoreSessionRecords, "collapsed HasMore");
    }

    private static void ExpandAfterCollapseKeepsOrder()
    {
        using GamePerformanceViewModel viewModel = Create();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(0, 20), 25, true), true);
        viewModel.CollapseSessionRecordsForDiagnostics();
        viewModel.ApplySessionRecordPageForDiagnostics(Page(Records(10, 10), 25, true, 10), false);
        TestSupport.Equal(string.Join('|', Enumerable.Range(0, 20).Select(index => $"session-{index:D2}")),
            string.Join('|', viewModel.RecentRecords.Select(item => item.GameName)), "expanded order");
    }

    private static void XamlUsesBoundedRecyclingList()
    {
        string xaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "HardwareVision", "Views", "GamePerformanceView.xaml"));
        TestSupport.True(xaml.Contains("Header=\"最近记录\"", StringComparison.Ordinal), "recent header");
        TestSupport.True(xaml.Contains("MaxHeight=\"420\"", StringComparison.Ordinal), "bounded height");
        TestSupport.True(xaml.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", StringComparison.Ordinal), "virtualization");
        TestSupport.True(xaml.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", StringComparison.Ordinal), "recycling");
        TestSupport.True(xaml.Contains("ScrollViewer.CanContentScroll=\"True\"", StringComparison.Ordinal), "logical scrolling");
    }

    private static void ConcurrentLoadMoreIsSingleFlight() => RunOnDispatcher(async dispatcher =>
    {
        TaskCompletionSource<GameSessionRecordPage> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSessionRecorder recorder = new((call, _, _, _, _) => call == 1
            ? Task.FromResult(Page(Records(0, 10), 20, true))
            : pending.Task);
        using GamePerformanceViewModel viewModel = Create(dispatcher, recorder);
        viewModel.SetActive(true);
        await WaitUntilAsync(() => viewModel.DisplayedSessionRecordCount == 10);

        Task first = viewModel.LoadMoreSessionRecordsCommand.ExecuteAsync(null);
        Task second = viewModel.LoadMoreSessionRecordsCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => recorder.PageCallCount == 2);
        TestSupport.Equal(2, recorder.PageCallCount, "initial plus one load-more request");
        pending.SetResult(Page(Records(10, 10), 20, false, 10));
        await Task.WhenAll(first, second);
        TestSupport.Equal(20, viewModel.DisplayedSessionRecordCount, "single page appended");
    });

    private static void DeactivationCancelsStalePage() => RunOnDispatcher(async dispatcher =>
    {
        TaskCompletionSource<GameSessionRecordPage> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSessionRecorder recorder = new((_, _, _, _, _) => pending.Task);
        using GamePerformanceViewModel viewModel = Create(dispatcher, recorder);
        viewModel.SetActive(true);
        await WaitUntilAsync(() => recorder.PageCallCount == 1);
        viewModel.SetActive(false);
        pending.SetResult(Page(Records(0, 10), 10, false));
        await Task.Delay(30);
        TestSupport.Equal(0, viewModel.DisplayedSessionRecordCount, "stale result ignored");
        TestSupport.False(viewModel.IsLoadingSessionRecords, "loading state cleared");
    });

    private static void LoadFailurePreservesCurrentPage() => RunOnDispatcher(async dispatcher =>
    {
        FakeSessionRecorder recorder = new((call, _, _, _, _) => call == 1
            ? Task.FromResult(Page(Records(0, 10), 20, true))
            : Task.FromException<GameSessionRecordPage>(new IOException("expected test failure")));
        using GamePerformanceViewModel viewModel = Create(dispatcher, recorder);
        viewModel.SetActive(true);
        await WaitUntilAsync(() => viewModel.DisplayedSessionRecordCount == 10);
        await viewModel.LoadMoreSessionRecordsCommand.ExecuteAsync(null);
        TestSupport.Equal(10, viewModel.DisplayedSessionRecordCount, "existing page retained");
        TestSupport.True(viewModel.SessionRecordLoadError?.Contains("已保留", StringComparison.Ordinal) == true, "retained-data error text");
    });

    private static GamePerformanceViewModel Create() => new(
        new NoopGamePerformanceService(),
        Dispatcher.CurrentDispatcher,
        EmptyForegroundProcessTracker.Instance,
        null,
        new AppSettings(),
        new SettingsService());

    private static GamePerformanceViewModel Create(Dispatcher dispatcher, IGameSessionRecorder recorder) => new(
        new NoopGamePerformanceService(),
        dispatcher,
        EmptyForegroundProcessTracker.Instance,
        recorder,
        new AppSettings(),
        new SettingsService());

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        TestSupport.True(predicate(), "asynchronous condition completed");
    }

    private static void RunOnDispatcher(Func<Dispatcher, Task> test)
    {
        Exception? failure = null;
        using ManualResetEventSlim completed = new();
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await test(dispatcher);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    completed.Set();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true, Name = "SessionHistoryViewModelTests.Dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!completed.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("Session history Dispatcher test timed out.");
        thread.Join(TimeSpan.FromSeconds(3));
        if (failure is not null)
            throw new InvalidOperationException("Session history Dispatcher test failed.", failure);
    }

    private static GameSessionRecordPage Page(
        IReadOnlyList<GameSessionRecordInfo> records,
        int total,
        bool hasMore,
        int offset = 0) => new()
    {
        Records = records,
        Offset = offset,
        PageSize = 10,
        TotalCount = total,
        HasMore = hasMore,
        SnapshotToken = "snapshot"
    };

    private static GameSessionRecordInfo[] Records(int start, int count) => Enumerable.Range(start, count)
        .Select(index => new GameSessionRecordInfo
        {
            GameName = $"session-{index:D2}",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-index),
            IsComplete = true,
            CsvPath = Path.Combine(Path.GetTempPath(), $"session-{index:D2}.csv")
        }).ToArray();

    private sealed class NoopGamePerformanceService : IGamePerformanceService
    {
        public event EventHandler<GameFrameSample>? FrameReceived { add { } remove { } }
        public event EventHandler<string>? StatusChanged { add { } remove { } }
        public event EventHandler<GameCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public bool IsCaptureAvailable => false;
        public string StatusText => "idle";
        public GameCaptureState CaptureState => GameCaptureState.Idle;
        public string? CaptureToolPath => null;
        public IReadOnlyList<GameFrameSample> RecentSamples => [];
        public GamePerformanceSnapshot GetSnapshot(TimeSpan window) => new();
        public Task<IReadOnlyList<GameProcessInfo>> GetCandidateProcessesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameProcessInfo>>([]);
        public Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopCaptureAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> ExportWindowCsvAsync(string directory, TimeSpan window, string? processName = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> ExportCacheCsvAsync(string directory, string? processName = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class FakeSessionRecorder(
        Func<int, int, int, string?, CancellationToken, Task<GameSessionRecordPage>> pageHandler) : IGameSessionRecorder
    {
        private int pageCallCount;
        public event EventHandler<GameSessionRecorderStateChangedEventArgs>? StateChanged { add { } remove { } }
        public int PageCallCount => Volatile.Read(ref pageCallCount);
        public string RootDirectory => Path.GetTempPath();
        public bool IsRecording => false;
        public string RecordingStatusText => "idle";
        public string? CurrentFilePath => null;
        public long DroppedSampleCount => 0;
        public Task RecoverIncompleteSessionsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(GameSessionStartInfo startInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool TryRecord(GameFrameSample sample, Guid captureSessionId, int generation) => false;
        public Task<GameSessionRecordInfo?> CompleteAsync(GameSessionEndReason reason, bool completedNormally, CancellationToken cancellationToken = default) => Task.FromResult<GameSessionRecordInfo?>(null);
        public Task<IReadOnlyList<GameSessionRecordInfo>> GetRecentRecordsAsync(int maximumCount = 10, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GameSessionRecordInfo>>([]);
        public Task<GameSessionRecordPage> GetRecordsPageAsync(int offset, int pageSize, string? snapshotToken = null, CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref pageCallCount);
            return pageHandler(call, offset, pageSize, snapshotToken, cancellationToken);
        }
        public Task<long> GetDirectorySizeAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
