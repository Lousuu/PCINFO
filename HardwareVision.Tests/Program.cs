using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace HardwareVision.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--output_file", StringComparer.OrdinalIgnoreCase)
            || args.Contains("--output_stdout", StringComparer.OrdinalIgnoreCase))
        {
            return RunFakePresentMon(args);
        }

        (string Name, Action Test)[] tests =
        [
            ("PresentMon 2.x schema and row", PresentMon2SchemaAndRow),
            ("Legacy schema and row", LegacySchemaAndRow),
            ("NA values", NaValues),
            ("Quoted CSV fields", QuotedCsvFields),
            ("Missing frame-time schema", MissingFrameTimeSchema),
            ("FrameTime to FPS", FrameTimeToFps),
            ("CPU/GPU/latency semantic mapping", SemanticMapping),
            ("Process search by executable name", ProcessSearchByName),
            ("Process search by Chinese window title", ProcessSearchByChineseWindowTitle),
            ("Process search by path and file name", ProcessSearchByPathAndFileName),
            ("Process search is case-insensitive", ProcessSearchIsCaseInsensitive),
            ("Empty process search restores all candidates", EmptyProcessSearchRestoresAll),
            ("Recent foreground process has highest priority", RecentForegroundHasHighestPriority),
            ("Win64 Shipping process receives game score", ShippingProcessReceivesGameScore),
            ("Steam game directory receives game score", SteamDirectoryReceivesGameScore),
            ("Launcher helper and crash reporter are penalized", AuxiliaryProcessesArePenalized),
            ("Browser IDE and Shell are not high-confidence games", StrongNonGameProcessesAreNotSelected),
            ("Close candidate scores prevent auto-selection", CloseScoresPreventAutoSelection),
            ("Manual selection prevents refresh replacement", ManualSelectionPreventsReplacement),
            ("Exited recent foreground process is ignored", ExitedForegroundProcessIsIgnored),
            ("Capture states prevent target switching", CaptureStatesPreventTargetSwitching),
            ("ViewModel preserves manual selection on refresh", ViewModelPreservesManualSelection),
            ("ViewModel keeps capture target during refresh", ViewModelKeepsCaptureTarget),
            ("Bundled PresentMon extraction", BundledPresentMonExtraction),
            ("End-to-end stdout capture", EndToEndStdoutCapture),
            ("End-to-end schema mismatch state", EndToEndSchemaMismatch),
            ("Launcher child target resolution", LauncherChildTargetResolution),
            ("Capture session isolation", CaptureSessionIsolation),
            ("Current FPS rolling window", CurrentFpsRollingWindow),
            ("Latency sources remain distinct", LatencySourcesRemainDistinct),
            ("Low-FPS minimum samples", LowFpsMinimumSamples),
            ("Invalid numeric filtering", InvalidNumericFiltering),
            ("Single-flight first entry", SingleFlightFirstEntry),
            ("Single-flight exit allows next entry", SingleFlightExitAllowsNextEntry),
            ("Single-flight permits one concurrent winner", SingleFlightConcurrentWinner),
            ("Dashboard refresh requests coalesce", DashboardRefreshRequestsCoalesce),
            ("Dashboard refresh kinds combine", DashboardRefreshKindsCombine),
            ("Dashboard refresh ignores requests after dispose", DashboardRefreshIgnoresAfterDispose),
            ("Sensor history is bounded", SensorHistoryIsBounded),
            ("Sensor history preserves ring order", SensorHistoryPreservesOrder),
            ("Sensor history returns requested tail", SensorHistoryReturnsRequestedTail),
            ("Sensor history ignores invalid values", SensorHistoryIgnoresInvalidValues),
            ("Sensor history records disk and network", SensorHistoryRecordsDiskAndNetwork),
            ("Sensor history records GPU metrics", SensorHistoryRecordsGpuMetrics),
            ("Sensor history averages CPU core clocks", SensorHistoryAveragesCpuClocks),
            ("Sample store rejects foreign session", SampleStoreRejectsForeignSession),
            ("Sample store ring capacity", SampleStoreRingCapacity),
            ("Sample store version changes", SampleStoreVersionChanges),
            ("Sample store reuses exact statistics", SampleStoreReusesExactStatistics),
            ("Sample store applies time window", SampleStoreAppliesTimeWindow),
            ("WMI fallback skips valid primary clock", WmiFallbackSkipsPrimaryClock),
            ("WMI fallback accepts bus clock as insufficient", WmiFallbackDoesNotAcceptBusClock),
            ("WMI fallback caches for five seconds", WmiFallbackCachesForFiveSeconds),
            ("WMI fallback refreshes after expiry", WmiFallbackRefreshesAfterExpiry),
            ("WMI fallback failure backs off", WmiFallbackFailureBacksOff),
            ("Game file names are sanitized", GameFileNamesAreSanitized),
            ("Game file names are unique", GameFileNamesAreUnique),
            ("Game CSV escapes text", GameCsvEscapesText),
            ("Game CSV reads quoted numeric field", GameCsvReadsQuotedNumericField),
            ("Game CSV empty export returns null", GameCsvEmptyExportReturnsNull),
            ("Game CSV export writes BOM and header", GameCsvExportWritesBomAndHeader),
            ("Recorder exposes intended partial path", RecorderExposesPartialPath),
            ("Recorder removes empty session", RecorderRemovesEmptySession),
            ("Recorder finalizes CSV and summary", RecorderFinalizesCsvAndSummary),
            ("Recorder summary preserves counts", RecorderSummaryPreservesCounts),
            ("Recorder rejects wrong generation", RecorderRejectsWrongGeneration),
            ("Recorder recovers data partial", RecorderRecoversDataPartial),
            ("Recorder removes empty partial", RecorderRemovesEmptyPartial),
            ("Recorder recent list obeys maximum", RecorderRecentListObeysMaximum),
            ("Recorder reports directory size", RecorderReportsDirectorySize),
            ("Recorder groups sessions by month", RecorderGroupsSessionsByMonth),
            ("Recorder calculates session low FPS", RecorderCalculatesSessionLowFps),
            ("PresentMon end-to-end automatic recording", PresentMonEndToEndAutomaticRecording)
        ];

        int failed = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"HardwareVision tests: {tests.Length - failed} passed, {failed} failed, {tests.Length} total.");
        return failed == 0 ? 0 : 1;
    }

    private static void PresentMon2SchemaAndRow()
    {
        Guid sessionId = Guid.NewGuid();
        PresentMonCsvParser parser = new(sessionId, 7, "fallback.exe");
        const string header = "Application,ProcessID,SwapChainAddress,PresentRuntime,FrameTime,MsBetweenPresents,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,ClickToPhotonLatency,PresentMode,FrameType";
        const string row = "game.exe,42,0xABC,DXGI,16.5,100,4.1,1.2,5.3,6.4,5.8,0.6,23.4,15.9,31.2,Independent Flip,Application";

        Equal(PresentMonCsvParseKind.HeaderAccepted, parser.ParseLine(header).Kind, "v2 header");
        PresentMonCsvParseResult result = parser.ParseLine(row);
        Equal(PresentMonCsvParseKind.Sample, result.Kind, "v2 row");
        GameFrameSample sample = NotNull(result.Sample, "v2 sample");
        Equal(sessionId, sample.CaptureSessionId, "session id");
        Equal(42, sample.ProcessId, "process id");
        Equal("0xABC", sample.SwapChainAddress, "swap chain");
        NearlyEqual(16.5, sample.FrameTimeMs, "FrameTime must take priority over MsBetweenPresents");
        NearlyEqual(1000d / 16.5d, sample.Fps, "v2 FPS");
        NearlyEqual(4.1, sample.CpuBusyMs, "CPU busy");
        NearlyEqual(1.2, sample.CpuWaitMs, "CPU wait");
        NearlyEqual(5.3, sample.GpuLatencyMs, "GPU latency");
        NearlyEqual(6.4, sample.GpuTimeMs, "GPU time");
        NearlyEqual(5.8, sample.GpuBusyMs, "GPU busy");
        NearlyEqual(0.6, sample.GpuWaitMs, "GPU wait");
        NearlyEqual(23.4, sample.DisplayLatencyMs, "display latency");
        NearlyEqual(15.9, sample.DisplayedTimeMs, "displayed time");
        NearlyEqual(31.2, sample.ClickToPhotonLatencyMs, "click-to-photon latency");
        Equal("DXGI", sample.Runtime, "runtime");
        Equal("Application", sample.FrameType, "frame type");
    }

    private static void LegacySchemaAndRow()
    {
        PresentMonCsvParser parser = NewParser();
        const string header = "Application,ProcessID,SwapChainAddress,Runtime,MsBetweenPresents,MsCPUBusy,MsCPUWait,MsGPUTime,MsGPUBusy,MsGPUWait,MsUntilRenderComplete,MsUntilDisplayed,MsClickToPhotonLatency,PresentMode";
        const string row = "legacy.exe,51,0xDEF,D3D11,20,3,1,7,6,2,8,40,70,Composed Flip";

        Equal(PresentMonCsvParseKind.HeaderAccepted, parser.ParseLine(header).Kind, "legacy header");
        GameFrameSample sample = NotNull(parser.ParseLine(row).Sample, "legacy sample");
        NearlyEqual(20, sample.FrameTimeMs, "legacy frame time");
        NearlyEqual(50, sample.Fps, "legacy FPS");
        NearlyEqual(3, sample.CpuBusyMs, "legacy CPU busy");
        NearlyEqual(7, sample.GpuTimeMs, "legacy GPU time");
        NearlyEqual(8, sample.RenderLatencyMs, "legacy render latency");
        NearlyEqual(40, sample.DisplayLatencyMs, "legacy display latency");
        NearlyEqual(70, sample.ClickToPhotonLatencyMs, "legacy click latency");
    }

    private static void NaValues()
    {
        PresentMonCsvParser parser = NewParser();
        Equal(
            PresentMonCsvParseKind.HeaderAccepted,
            parser.ParseLine("Application,ProcessID,FrameTime,CPUBusy,GPUTime,DisplayLatency").Kind,
            "NA header");
        GameFrameSample sample = NotNull(
            parser.ParseLine("game.exe,1,10,NA,N/A,NA").Sample,
            "NA sample");
        Null(sample.CpuBusyMs, "NA CPU");
        Null(sample.GpuTimeMs, "N/A GPU");
        Null(sample.DisplayLatencyMs, "NA latency");
    }

    private static void QuotedCsvFields()
    {
        PresentMonCsvParser parser = NewParser();
        parser.ParseLine("Application,ProcessID,FrameTime,PresentMode");
        GameFrameSample sample = NotNull(
            parser.ParseLine("\"Game, \"\"Special\"\".exe\",12,8.333,\"Mode, Flip\"").Sample,
            "quoted sample");
        Equal("Game, \"Special\".exe", sample.ProcessName, "quoted application");
        Equal("Mode, Flip", sample.PresentMode, "quoted mode");

        IReadOnlyList<string> columns = PresentMonCsvParser.ParseColumns("a,\"b,c\",\"d\"\"e\"");
        Equal(3, columns.Count, "quoted column count");
        Equal("b,c", columns[1], "quoted comma");
        Equal("d\"e", columns[2], "escaped quote");
    }

    private static void MissingFrameTimeSchema()
    {
        PresentMonCsvParser parser = NewParser();
        PresentMonCsvParseResult result = parser.ParseLine("Application,ProcessID,GPUTime,PresentMode");
        Equal(PresentMonCsvParseKind.SchemaMismatch, result.Kind, "schema mismatch kind");
        True(result.Reason?.Contains("FrameTime", StringComparison.Ordinal) == true, "missing field message");
        False(parser.Schema?.HasFrameTimeColumn ?? true, "schema frame-time validation");
    }

    private static void FrameTimeToFps()
    {
        PresentMonCsvParser parser = NewParser();
        parser.ParseLine("Application,ProcessID,FrameTime");
        GameFrameSample sample = NotNull(parser.ParseLine("game.exe,1,20").Sample, "FPS sample");
        NearlyEqual(50, sample.Fps, "FrameTime FPS conversion");
    }

    private static void SemanticMapping()
    {
        PresentMonCsvParser parser = NewParser();
        parser.ParseLine("Application,ProcessID,FrameTime,MsInPresentAPI,MsUntilDisplayed,GPUTime,ClickToPhotonLatency");
        GameFrameSample sample = NotNull(
            parser.ParseLine("game.exe,1,16,99,40,7,80").Sample,
            "semantic sample");
        NearlyEqual(7, sample.GpuTimeMs, "MsInPresentAPI must not map to GPU time");
        NearlyEqual(40, sample.DisplayLatencyMs, "MsUntilDisplayed is display latency");
        NearlyEqual(80, sample.ClickToPhotonLatencyMs, "explicit click latency");

        PresentMonCsvParser noClickParser = NewParser();
        noClickParser.ParseLine("Application,ProcessID,FrameTime,MsUntilDisplayed");
        GameFrameSample noClick = NotNull(noClickParser.ParseLine("game.exe,1,16,40").Sample, "no click sample");
        Null(noClick.ClickToPhotonLatencyMs, "MsUntilDisplayed must not map to click latency");
    }

    private static void ProcessSearchByName()
    {
        GameProcessInfo process = Candidate(10, "StellarBlade-Win64-Shipping", "Stellar Blade");
        Equal(1, GameProcessFilter.Filter([process], "StellarBlade").Count, "process name match");
        Equal(1, GameProcessFilter.Filter([process], "StellarBlade-Win64-Shipping.exe").Count, "process exe match");
        Equal(0, GameProcessFilter.Filter([process], "OtherGame").Count, "unrelated process name");
    }

    private static void ProcessSearchByChineseWindowTitle()
    {
        GameProcessInfo process = Candidate(11, "game", "黑神话：悟空");
        Equal(1, GameProcessFilter.Filter([process], "悟空").Count, "Chinese window title match");
    }

    private static void ProcessSearchByPathAndFileName()
    {
        GameProcessInfo process = Candidate(
            12,
            "SB-Win64-Shipping",
            "Stellar Blade",
            @"D:\SteamLibrary\steamapps\common\StellarBlade\SB-Win64-Shipping.exe");
        Equal(1, GameProcessFilter.Filter([process], @"steamapps\common\StellarBlade").Count, "full path match");
        Equal(1, GameProcessFilter.Filter([process], "SB-Win64-Shipping.exe").Count, "file name match");
        Equal(1, GameProcessFilter.Filter([process], "SB-Win64-Shipping").Count, "file name without extension match");
    }

    private static void ProcessSearchIsCaseInsensitive()
    {
        GameProcessInfo process = Candidate(13, "Game-Win64-Shipping", "Example Game");
        Equal(1, GameProcessFilter.Filter([process], "GAME-WIN64-SHIPPING.EXE").Count, "case-insensitive search");
    }

    private static void EmptyProcessSearchRestoresAll()
    {
        GameProcessInfo[] processes =
        [
            Candidate(14, "first", "First"),
            Candidate(15, "second", "Second"),
            Candidate(16, "third", "Third")
        ];
        Equal(3, GameProcessFilter.Filter(processes, string.Empty).Count, "empty search");
        Equal(3, GameProcessFilter.Filter(processes, "   ").Count, "whitespace search");
    }

    private static void RecentForegroundHasHighestPriority()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameProcessInfo other = Candidate(20, "Other-Win64-Shipping", "Other Game", startTimeUtc: now.AddHours(-1));
        GameProcessInfo foreground = Candidate(21, "ForegroundGame", "Foreground Game", startTimeUtc: now.AddMinutes(-10));
        ForegroundProcessSnapshot snapshot = new()
        {
            ProcessId = foreground.ProcessId,
            ObservedAtUtc = now.AddSeconds(-5),
            WindowHandle = 123
        };

        IReadOnlyList<GameProcessDetectionResult> results = GameProcessScorer.ScoreAndSort(
            [other, foreground],
            snapshot,
            now);
        Equal(foreground.ProcessId, results[0].Process.ProcessId, "recent foreground sorted first");
        True(results[0].IsRecentForeground, "recent foreground flag");
        Equal(foreground.ProcessId, NotNull(GameProcessScorer.ChooseHighConfidence(results).Selection, "foreground decision").Process.ProcessId, "recent foreground selected");
    }

    private static void ShippingProcessReceivesGameScore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameProcessDetectionResult shipping = GameProcessScorer.ScoreAndSort(
            [Candidate(30, "Example-Win64-Shipping", "Example", startTimeUtc: now.AddHours(-1))],
            null,
            now)[0];
        GameProcessDetectionResult ordinary = GameProcessScorer.ScoreAndSort(
            [Candidate(31, "Example", "Example", startTimeUtc: now.AddHours(-1))],
            null,
            now)[0];
        True(shipping.Score > ordinary.Score, "Shipping score boost");
        True(shipping.IsLikelyGame, "Shipping likely game");
    }

    private static void SteamDirectoryReceivesGameScore()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameProcessDetectionResult steamGame = GameProcessScorer.ScoreAndSort(
            [Candidate(32, "ExampleGame", "Example", @"D:\SteamLibrary\steamapps\common\Example\ExampleGame.exe", now.AddHours(-1))],
            null,
            now)[0];
        GameProcessDetectionResult ordinary = GameProcessScorer.ScoreAndSort(
            [Candidate(33, "ExampleGame", "Example", @"D:\Apps\ExampleGame.exe", now.AddHours(-1))],
            null,
            now)[0];
        True(steamGame.Score > ordinary.Score, "Steam path score boost");
        True(steamGame.IsLikelyGame, "Steam game likely");
    }

    private static void AuxiliaryProcessesArePenalized()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int neutralScore = GameProcessScorer.ScoreAndSort(
            [Candidate(40, "ActualGame", "Actual Game", @"D:\Games\Example\ActualGame.exe", now.AddHours(-1))],
            null,
            now)[0].Score;
        foreach (string name in new[] { "GameLauncher", "GameHelper", "CrashReporter" })
        {
            int score = GameProcessScorer.ScoreAndSort(
                [Candidate(41, name, name, $@"D:\Games\Example\{name}.exe", now.AddHours(-1))],
                null,
                now)[0].Score;
            True(score < neutralScore, $"{name} penalty");
        }
    }

    private static void StrongNonGameProcessesAreNotSelected()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (string processName in new[] { "chrome", "code", "explorer", "powershell", "chatgpt", "codex" })
        {
            GameProcessInfo process = Candidate(50, processName, $"{processName} window", startTimeUtc: now.AddHours(-1));
            ForegroundProcessSnapshot foreground = new()
            {
                ProcessId = process.ProcessId,
                ObservedAtUtc = now.AddSeconds(-1),
                WindowHandle = 456
            };
            IReadOnlyList<GameProcessDetectionResult> results = GameProcessScorer.ScoreAndSort([process], foreground, now);
            False(results[0].IsLikelyGame, $"{processName} not likely game");
            Null(GameProcessScorer.ChooseHighConfidence(results).Selection, $"{processName} not auto-selected");
        }
    }

    private static void CloseScoresPreventAutoSelection()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<GameProcessDetectionResult> results = GameProcessScorer.ScoreAndSort(
            [
                Candidate(60, "First-Win64-Shipping", "First", startTimeUtc: now.AddHours(-1)),
                Candidate(61, "Second-Win64-Shipping", "Second", startTimeUtc: now.AddHours(-1))
            ],
            null,
            now);
        GameProcessDetectionDecision decision = GameProcessScorer.ChooseHighConfidence(results);
        True(decision.IsAmbiguous, "close scores ambiguous");
        Null(decision.Selection, "close scores no selection");
    }

    private static void ManualSelectionPreventsReplacement()
    {
        False(
            GameProcessSelectionPolicy.CanAutoSelect(GameCaptureState.Idle, hasValidUserSelection: true, hasSearchText: false),
            "manual selection prevents auto-selection");
        True(
            GameProcessSelectionPolicy.IsSameProcess(
                Candidate(70, "ManualGame", "Manual"),
                Candidate(70, "ManualGame", "Manual Refreshed")),
            "refresh preserves manual PID identity");
    }

    private static void ExitedForegroundProcessIsIgnored()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameProcessInfo exited = Candidate(80, "Exited-Win64-Shipping", "Exited", startTimeUtc: now.AddHours(-1), isRunning: false);
        ForegroundProcessSnapshot foreground = new()
        {
            ProcessId = exited.ProcessId,
            ObservedAtUtc = now.AddSeconds(-1),
            WindowHandle = 789
        };
        GameProcessDetectionResult result = GameProcessScorer.ScoreAndSort([exited], foreground, now)[0];
        False(result.IsRecentForeground, "exited foreground ignored");
        False(result.IsLikelyGame, "exited process not likely game");
    }

    private static void CaptureStatesPreventTargetSwitching()
    {
        foreach (GameCaptureState state in new[]
                 {
                     GameCaptureState.Preparing,
                     GameCaptureState.WaitingForFirstFrame,
                     GameCaptureState.Capturing,
                     GameCaptureState.Stopping
                 })
        {
            False(
                GameProcessSelectionPolicy.CanAutoSelect(state, hasValidUserSelection: false, hasSearchText: false),
                $"{state} prevents auto-selection");
        }
    }

    private static void ViewModelPreservesManualSelection()
    {
        RunOnDispatcher(async dispatcher =>
        {
            FakeGamePerformanceService service = new(
            [
                Candidate(90, "ManualGame", "Manual Game"),
                Candidate(91, "OtherApp", "Other App")
            ]);
            using GamePerformanceViewModel viewModel = new(service, dispatcher, new FixedForegroundProcessTracker());
            await viewModel.RefreshProcessesCommand.ExecuteAsync(null);
            viewModel.SelectedProcess = viewModel.ProcessOptions.Single(process => process.ProcessId == 90);

            service.SetCandidates(
            [
                Candidate(90, "ManualGame", "Manual Game Refreshed"),
                Candidate(92, "Best-Win64-Shipping", "Best Game", @"D:\Games\Best\Best-Win64-Shipping.exe")
            ]);
            await viewModel.RefreshProcessesCommand.ExecuteAsync(null);

            Equal(90, NotNull(viewModel.SelectedProcess, "manual ViewModel selection").ProcessId, "manual selection retained after refresh");
            Equal("Manual Game Refreshed", viewModel.SelectedProcess?.WindowTitle, "manual selection refreshed instance");
        });
    }

    private static void ViewModelKeepsCaptureTarget()
    {
        RunOnDispatcher(async dispatcher =>
        {
            FakeGamePerformanceService service = new([Candidate(93, "CaptureGame", "Capture Game")]);
            using GamePerformanceViewModel viewModel = new(service, dispatcher, new FixedForegroundProcessTracker());
            await viewModel.RefreshProcessesCommand.ExecuteAsync(null);
            viewModel.SelectedProcess = viewModel.ProcessOptions.Single(process => process.ProcessId == 93);
            service.RaiseCaptureState(GameCaptureState.Capturing, "capturing");

            service.SetCandidates(
            [
                Candidate(94, "Replacement-Win64-Shipping", "Replacement", @"D:\Games\Replacement\Replacement-Win64-Shipping.exe")
            ]);
            await viewModel.RefreshProcessesCommand.ExecuteAsync(null);

            Equal(93, NotNull(viewModel.SelectedProcess, "capturing ViewModel selection").ProcessId, "capture target retained");
            Equal(1, viewModel.ProcessOptions.Count, "capture list remains stable");
            Equal(93, viewModel.ProcessOptions[0].ProcessId, "capture list keeps selected process");
        });
    }

    private static void CaptureSessionIsolation()
    {
        GameFrameSampleStore store = new(2);
        Guid firstSession = Guid.NewGuid();
        Guid secondSession = Guid.NewGuid();
        store.StartSession(firstSession);
        True(store.TryAdd(Sample(firstSession, 10)), "first session add");
        Equal(1, store.Snapshot().Count, "first session count");

        store.StartSession(secondSession);
        Equal(0, store.Snapshot().Count, "new session clears old samples");
        False(store.TryAdd(Sample(firstSession, 11)), "stale session rejected");
        True(store.TryAdd(Sample(secondSession, 12)), "current session accepted");
        IReadOnlyList<GameFrameSample> snapshot = store.Snapshot();
        Equal(1, snapshot.Count, "isolated session count");
        Equal(secondSession, snapshot[0].CaptureSessionId, "isolated session id");

        True(store.TryAdd(Sample(secondSession, 13)), "second ring sample");
        True(store.TryAdd(Sample(secondSession, 14)), "third ring sample");
        snapshot = store.Snapshot();
        Equal(2, snapshot.Count, "ring buffer capacity");
        NearlyEqual(13, snapshot[0].FrameTimeMs, "ring buffer chronological start");
        NearlyEqual(14, snapshot[1].FrameTimeMs, "ring buffer chronological end");
    }

    private static void LauncherChildTargetResolution()
    {
        GameProcessInfo launcher = new()
        {
            ProcessId = 10,
            ProcessName = "Launcher",
            FilePath = @"C:\Games\Example\Launcher.exe",
            DisplayName = "Launcher (10)"
        };
        GameProcessNode[] processes =
        [
            new()
            {
                ProcessId = 10,
                ParentProcessId = 1,
                ProcessName = "Launcher.exe",
                FilePath = @"C:\Games\Example\Launcher.exe"
            },
            new()
            {
                ProcessId = 20,
                ParentProcessId = 10,
                ProcessName = "Game-Win64-Shipping.exe",
                WindowTitle = "Example Game",
                FilePath = @"C:\Games\Example\Game\Binaries\Win64\Game-Win64-Shipping.exe"
            },
            new()
            {
                ProcessId = 30,
                ParentProcessId = 1,
                ProcessName = "Unrelated.exe",
                WindowTitle = "Unrelated",
                FilePath = @"C:\Other\Unrelated.exe"
            }
        ];

        GameProcessInfo resolved = GameCaptureTargetResolver.Resolve(launcher, processes);
        Equal(20, resolved.ProcessId, "launcher child PID");
        Equal("Game-Win64-Shipping", resolved.ProcessName, "launcher child process name");
        Equal("Example Game", resolved.WindowTitle, "launcher child window title");

        GameProcessInfo windowedRequest = new()
        {
            ProcessId = 40,
            ProcessName = "DirectGame",
            WindowTitle = "Direct Game",
            DisplayName = "Direct Game (40)"
        };
        Equal(
            40,
            GameCaptureTargetResolver.Resolve(windowedRequest, processes).ProcessId,
            "windowed target remains selected");
    }

    private static void EndToEndStdoutCapture()
    {
        string executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test executable path is unavailable.");
        using PresentMonGamePerformanceService service = new(executablePath, isElevated: true);
        TaskCompletionSource<GameFrameSample> firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<GameCaptureState> states = new();
        object stateLock = new();
        service.FrameReceived += (_, sample) => firstFrame.TrySetResult(sample);
        service.CaptureStateChanged += (_, state) =>
        {
            lock (stateLock)
            {
                states.Add(state.State);
            }
        };

        GameProcessInfo process = new()
        {
            ProcessId = Environment.ProcessId,
            ProcessName = "fake-game.exe",
            DisplayName = "Fake Game"
        };
        service.StartCaptureAsync(process).GetAwaiter().GetResult();
        GameFrameSample sample = firstFrame.Task
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();

        NearlyEqual(10, sample.FrameTimeMs, "end-to-end frame time");
        NearlyEqual(100, sample.Fps, "end-to-end FPS");
        NearlyEqual(3, sample.CpuBusyMs, "end-to-end CPU busy");
        NearlyEqual(7, sample.GpuTimeMs, "end-to-end GPU time");
        NearlyEqual(30, sample.DisplayLatencyMs, "end-to-end display latency");
        True(sample.CaptureSessionId != Guid.Empty, "end-to-end session id");
        lock (stateLock)
        {
            True(states.Contains(GameCaptureState.WaitingForFirstFrame), "waiting state observed");
            True(states.Contains(GameCaptureState.Capturing), "capturing state observed after first frame");
        }

        GamePerformanceSnapshot snapshot = service.GetSnapshot(TimeSpan.FromMinutes(1));
        Equal(1, snapshot.SampleCount, "end-to-end snapshot sample count");
        NearlyEqual(100, snapshot.CurrentFps, "end-to-end snapshot FPS");
        service.StopCaptureAsync().GetAwaiter().GetResult();
        Equal(GameCaptureState.Idle, service.CaptureState, "end-to-end stopped state");
    }

    private static void BundledPresentMonExtraction()
    {
        True(PresentMonRuntimeExtractor.IsEmbeddedAvailable, "embedded PresentMon resources");
        string path = PresentMonRuntimeExtractor
            .EnsureAvailableAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        True(File.Exists(path), "extracted PresentMon executable");
        Equal("PresentMon.exe", Path.GetFileName(path), "extracted PresentMon file name");
    }

    private static void EndToEndSchemaMismatch()
    {
        const string schemaMismatchVariable = "HARDWAREVISION_FAKE_SCHEMA_MISMATCH";
        string? previousValue = Environment.GetEnvironmentVariable(schemaMismatchVariable);
        Environment.SetEnvironmentVariable(schemaMismatchVariable, "1");
        try
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Test executable path is unavailable.");
            using PresentMonGamePerformanceService service = new(executablePath, isElevated: true);
            TaskCompletionSource<GameCaptureStateChangedEventArgs> mismatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            service.CaptureStateChanged += (_, state) =>
            {
                if (state.State == GameCaptureState.SchemaMismatch)
                {
                    mismatch.TrySetResult(state);
                }
            };

            service.StartCaptureAsync(new GameProcessInfo
            {
                ProcessId = Environment.ProcessId,
                ProcessName = "schema-mismatch.exe",
                DisplayName = "Schema Mismatch"
            }).GetAwaiter().GetResult();
            GameCaptureStateChangedEventArgs state = mismatch.Task
                .WaitAsync(TimeSpan.FromSeconds(5))
                .GetAwaiter()
                .GetResult();
            True(state.StatusText.Contains("输出格式不兼容", StringComparison.Ordinal), "schema mismatch status text");
            True(state.StatusText.Contains("FrameTime", StringComparison.Ordinal), "schema mismatch missing field text");
            Equal(0, service.RecentSamples.Count, "schema mismatch stores no samples");
            service.StopCaptureAsync().GetAwaiter().GetResult();
        }
        finally
        {
            Environment.SetEnvironmentVariable(schemaMismatchVariable, previousValue);
        }
    }

    private static void LowFpsMinimumSamples()
    {
        GamePerformanceSnapshot ninetyNine = CalculateFrames(99);
        Null(ninetyNine.OnePercentLowFps, "1% Low needs 100 samples");
        Null(ninetyNine.ZeroPointOnePercentLowFps, "0.1% Low needs 1000 samples");

        GamePerformanceSnapshot oneHundred = CalculateFrames(100);
        NotNull(oneHundred.OnePercentLowFps, "1% Low at 100 samples");
        Null(oneHundred.ZeroPointOnePercentLowFps, "0.1% Low below 1000 samples");

        GamePerformanceSnapshot oneThousand = CalculateFrames(1000);
        NotNull(oneThousand.OnePercentLowFps, "1% Low at 1000 samples");
        NotNull(oneThousand.ZeroPointOnePercentLowFps, "0.1% Low at 1000 samples");
    }

    private static void InvalidNumericFiltering()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        GameFrameSample[] samples =
        [
            new() { Timestamp = now, FrameTimeMs = 16, Fps = 62.5, CpuBusyMs = 10, GpuTimeMs = 20, DisplayLatencyMs = 30 },
            new() { Timestamp = now, FrameTimeMs = 20, CpuBusyMs = 0, GpuTimeMs = -2, DisplayLatencyMs = double.NaN },
            new() { Timestamp = now, FrameTimeMs = 0, CpuBusyMs = 999, GpuTimeMs = 999, DisplayLatencyMs = 999 },
            new() { Timestamp = now, FrameTimeMs = -1 },
            new() { Timestamp = now, FrameTimeMs = double.NaN },
            new() { Timestamp = now, FrameTimeMs = double.PositiveInfinity }
        ];

        GamePerformanceSnapshot snapshot = GameFrameStatisticsCalculator.Calculate(samples, TimeSpan.FromMinutes(1));
        Equal(2, snapshot.SampleCount, "valid frame count");
        NearlyEqual(18, snapshot.AverageFrameTimeMs, "average frame time");
        NearlyEqual(1000d / 18d, snapshot.AverageFps, "average FPS from average frame time");
        NearlyEqual(1000d / 18d, snapshot.CurrentFps, "current FPS uses valid rolling frame-time mean");
        NearlyEqual(10, snapshot.AverageCpuBusyMs, "invalid CPU values filtered");
        NearlyEqual(20, snapshot.AverageGpuTimeMs, "invalid GPU values filtered");
        NearlyEqual(30, snapshot.AverageDisplayLatencyMs, "invalid latency values filtered");
    }

    private static void CurrentFpsRollingWindow()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        GameFrameSample[] samples = Enumerable.Range(0, 101)
            .Select(index => new GameFrameSample
            {
                Timestamp = now,
                FrameTimeMs = index == 100 ? 0.25d : 10d,
                Fps = index == 100 ? 4_000d : 100d
            })
            .ToArray();

        GamePerformanceSnapshot snapshot = GameFrameStatisticsCalculator.Calculate(
            samples,
            TimeSpan.FromMinutes(1));
        NearlyEqual(
            1000d / (1000.25d / 101d),
            snapshot.CurrentFps,
            "single sub-millisecond present must not become current FPS");
    }

    private static void LatencySourcesRemainDistinct()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        GameFrameSample[] samples =
        [
            new()
            {
                Timestamp = now,
                FrameTimeMs = 10,
                DisplayLatencyMs = 30,
                ClickToPhotonLatencyMs = 80,
                RenderLatencyMs = 20,
                GpuLatencyMs = 5
            },
            new()
            {
                Timestamp = now,
                FrameTimeMs = 10,
                DisplayLatencyMs = 40,
                RenderLatencyMs = 25,
                GpuLatencyMs = 6
            }
        ];

        GamePerformanceSnapshot snapshot = GameFrameStatisticsCalculator.Calculate(
            samples,
            TimeSpan.FromMinutes(1));
        NearlyEqual(35, snapshot.AverageDisplayLatencyMs, "display latency must not mix with click or render latency");

        GamePerformanceSnapshot unavailableDisplayLatency = GameFrameStatisticsCalculator.Calculate(
            [new GameFrameSample { Timestamp = now, FrameTimeMs = 10, ClickToPhotonLatencyMs = 80 }],
            TimeSpan.FromMinutes(1));
        Null(unavailableDisplayLatency.AverageDisplayLatencyMs, "display latency stays unavailable instead of falling back");
    }

    private static void SingleFlightFirstEntry()
    {
        SingleFlightGate gate = new();
        True(gate.TryEnter(), "first entry");
        True(gate.IsRunning, "running state");
        False(gate.TryEnter(), "second entry rejected");
    }

    private static void SingleFlightExitAllowsNextEntry()
    {
        SingleFlightGate gate = new();
        True(gate.TryEnter(), "initial entry");
        gate.Exit();
        False(gate.IsRunning, "exit state");
        True(gate.TryEnter(), "next entry");
    }

    private static void SingleFlightConcurrentWinner()
    {
        SingleFlightGate gate = new();
        int winners = 0;
        Parallel.For(0, 64, _ =>
        {
            if (gate.TryEnter())
            {
                Interlocked.Increment(ref winners);
            }
        });
        Equal(1, winners, "concurrent winner count");
    }

    private static void DashboardRefreshRequestsCoalesce()
    {
        int calls = 0;
        using DashboardRefreshCoordinator coordinator = new(_ => Interlocked.Increment(ref calls), TimeSpan.FromMilliseconds(20));
        for (int index = 0; index < 20; index++)
        {
            coordinator.Request(DashboardRefreshKind.Sensors);
        }

        WaitUntil(() => Volatile.Read(ref calls) == 1, "coalesced refresh");
        Equal(1, calls, "one coalesced apply");
    }

    private static void DashboardRefreshKindsCombine()
    {
        DashboardRefreshKind applied = DashboardRefreshKind.None;
        using DashboardRefreshCoordinator coordinator = new(kind => applied = kind, TimeSpan.FromMilliseconds(20));
        coordinator.Request(DashboardRefreshKind.Sensors);
        coordinator.Request(DashboardRefreshKind.Disk);
        coordinator.Request(DashboardRefreshKind.Network);
        WaitUntil(() => applied != DashboardRefreshKind.None, "combined refresh");
        Equal(DashboardRefreshKind.Sensors | DashboardRefreshKind.Disk | DashboardRefreshKind.Network, applied, "combined flags");
    }

    private static void DashboardRefreshIgnoresAfterDispose()
    {
        int calls = 0;
        DashboardRefreshCoordinator coordinator = new(_ => calls++, TimeSpan.FromMilliseconds(10));
        coordinator.Dispose();
        coordinator.Request(DashboardRefreshKind.All);
        Thread.Sleep(30);
        Equal(0, calls, "disposed coordinator");
    }

    private static void SensorHistoryIsBounded()
    {
        using SensorHistoryService history = new();
        for (int index = 0; index < 300; index++)
        {
            history.RecordDisk(new DiskDevice { ReadSpeed = index });
        }

        Equal(SensorHistoryService.MaximumPoints, history.GetSnapshot(SensorHistoryMetric.DiskRead).Count, "history capacity");
    }

    private static void SensorHistoryPreservesOrder()
    {
        using SensorHistoryService history = new();
        for (int index = 0; index < 245; index++)
        {
            history.RecordDisk(new DiskDevice { ReadSpeed = index });
        }

        IReadOnlyList<double> values = history.GetSnapshot(SensorHistoryMetric.DiskRead);
        NearlyEqual(5, values[0], "ring oldest");
        NearlyEqual(244, values[^1], "ring newest");
    }

    private static void SensorHistoryReturnsRequestedTail()
    {
        using SensorHistoryService history = new();
        for (int index = 0; index < 10; index++)
        {
            history.RecordDisk(new DiskDevice { ReadSpeed = index });
        }

        IReadOnlyList<double> values = history.GetSnapshot(SensorHistoryMetric.DiskRead, 3);
        Equal(3, values.Count, "tail count");
        NearlyEqual(7, values[0], "tail start");
        NearlyEqual(9, values[^1], "tail end");
    }

    private static void SensorHistoryIgnoresInvalidValues()
    {
        using SensorHistoryService history = new();
        history.RecordDisk(new DiskDevice { ReadSpeed = null });
        history.RecordDisk(new DiskDevice { ReadSpeed = double.NaN });
        history.RecordDisk(new DiskDevice { ReadSpeed = double.PositiveInfinity });
        Equal(0, history.GetSnapshot(SensorHistoryMetric.DiskRead).Count, "invalid history values");
    }

    private static void SensorHistoryRecordsDiskAndNetwork()
    {
        using SensorHistoryService history = new();
        history.RecordDisk(new DiskDevice { ReadSpeed = 12, WriteSpeed = 34 });
        history.RecordNetwork(new NetworkAdapterDevice { UploadSpeed = 56, DownloadSpeed = 78 });
        NearlyEqual(12, history.GetSnapshot(SensorHistoryMetric.DiskRead)[0], "disk read");
        NearlyEqual(34, history.GetSnapshot(SensorHistoryMetric.DiskWrite)[0], "disk write");
        NearlyEqual(56, history.GetSnapshot(SensorHistoryMetric.NetworkUpload)[0], "network upload");
        NearlyEqual(78, history.GetSnapshot(SensorHistoryMetric.NetworkDownload)[0], "network download");
    }

    private static void SensorHistoryRecordsGpuMetrics()
    {
        using SensorHistoryService history = new();
        history.RecordGpu(new GpuDevice
        {
            CoreLoad = Reading(70, SensorCategory.Gpu, SensorType.Load),
            TemperatureCore = Reading(65, SensorCategory.Gpu, SensorType.Temperature),
            PowerPackage = Reading(150, SensorCategory.Gpu, SensorType.Power),
            CoreClock = Reading(2500, SensorCategory.Gpu, SensorType.Clock),
            MemoryUsed = Reading(4096, SensorCategory.Gpu, SensorType.Data)
        });
        NearlyEqual(70, history.GetSnapshot(SensorHistoryMetric.GpuLoad)[0], "GPU load");
        NearlyEqual(4096, history.GetSnapshot(SensorHistoryMetric.GpuMemoryUsed)[0], "GPU memory");
    }

    private static void SensorHistoryAveragesCpuClocks()
    {
        using SensorHistoryService history = new();
        history.RecordSensorReadings(
        [
            Reading(3000, SensorCategory.Cpu, SensorType.Clock, "Core 0"),
            Reading(4000, SensorCategory.Cpu, SensorType.Clock, "Core 1"),
            Reading(100, SensorCategory.Cpu, SensorType.Clock, "Bus Clock")
        ]);
        NearlyEqual(3500, history.GetSnapshot(SensorHistoryMetric.CpuClock)[0], "average CPU clock");
    }

    private static void SampleStoreRejectsForeignSession()
    {
        GameFrameSampleStore store = new(4);
        Guid session = Guid.NewGuid();
        store.StartSession(session);
        False(store.TryAdd(Sample(Guid.NewGuid(), 10)), "foreign sample");
        Equal(0, store.Snapshot().Count, "foreign sample count");
    }

    private static void SampleStoreRingCapacity()
    {
        GameFrameSampleStore store = new(3);
        Guid session = Guid.NewGuid();
        store.StartSession(session);
        for (int index = 1; index <= 5; index++)
        {
            store.TryAdd(Sample(session, index));
        }

        IReadOnlyList<GameFrameSample> samples = store.Snapshot();
        Equal(3, samples.Count, "ring sample count");
        NearlyEqual(3, samples[0].FrameTimeMs, "ring oldest sample");
        NearlyEqual(5, samples[^1].FrameTimeMs, "ring newest sample");
    }

    private static void SampleStoreVersionChanges()
    {
        GameFrameSampleStore store = new(2);
        long initial = store.SampleVersion;
        Guid session = Guid.NewGuid();
        store.StartSession(session);
        True(store.SampleVersion > initial, "session version");
        long started = store.SampleVersion;
        store.TryAdd(Sample(session, 10));
        True(store.SampleVersion > started, "sample version");
    }

    private static void SampleStoreReusesExactStatistics()
    {
        GameFrameSampleStore store = new(4);
        Guid session = Guid.NewGuid();
        store.StartSession(session);
        store.TryAdd(Sample(session, 10));
        GamePerformanceSnapshot first = store.Calculate(TimeSpan.FromMinutes(1));
        GamePerformanceSnapshot second = store.Calculate(TimeSpan.FromMinutes(1));
        True(ReferenceEquals(first, second), "exact statistics cache");
    }

    private static void SampleStoreAppliesTimeWindow()
    {
        GameFrameSampleStore store = new(4);
        Guid session = Guid.NewGuid();
        store.StartSession(session);
        store.TryAdd(new GameFrameSample { CaptureSessionId = session, Timestamp = DateTimeOffset.Now.AddMinutes(-2), FrameTimeMs = 20 });
        store.TryAdd(Sample(session, 10));
        Equal(1, store.Snapshot(TimeSpan.FromSeconds(30)).Count, "window sample count");
    }

    private static void WmiFallbackSkipsPrimaryClock()
    {
        int queries = 0;
        using WmiCpuClockSensorProvider provider = new(new ManualTimeProvider(), _ =>
        {
            queries++;
            return [Reading(3200, SensorCategory.Cpu, SensorType.Clock)];
        });
        IReadOnlyList<SensorReading> result = provider.GetReadingsAsync(
            [Reading(4500, SensorCategory.Cpu, SensorType.Clock, "Core 0")]).GetAwaiter().GetResult();
        Equal(0, result.Count, "skip result");
        Equal(0, queries, "skip query count");
    }

    private static void WmiFallbackDoesNotAcceptBusClock()
    {
        int queries = 0;
        using WmiCpuClockSensorProvider provider = new(new ManualTimeProvider(), _ =>
        {
            queries++;
            return [Reading(3200, SensorCategory.Cpu, SensorType.Clock)];
        });
        _ = provider.GetReadingsAsync(
            [Reading(100, SensorCategory.Cpu, SensorType.Clock, "Bus Clock")]).GetAwaiter().GetResult();
        Equal(1, queries, "bus clock fallback query");
    }

    private static void WmiFallbackCachesForFiveSeconds()
    {
        int queries = 0;
        ManualTimeProvider time = new();
        using WmiCpuClockSensorProvider provider = new(time, _ =>
        {
            queries++;
            return [Reading(3200, SensorCategory.Cpu, SensorType.Clock)];
        });
        _ = provider.GetReadingsAsync().GetAwaiter().GetResult();
        time.Advance(TimeSpan.FromSeconds(4.9));
        _ = provider.GetReadingsAsync().GetAwaiter().GetResult();
        Equal(1, queries, "cached query count");
    }

    private static void WmiFallbackRefreshesAfterExpiry()
    {
        int queries = 0;
        ManualTimeProvider time = new();
        using WmiCpuClockSensorProvider provider = new(time, _ =>
        {
            queries++;
            return [Reading(3200 + queries, SensorCategory.Cpu, SensorType.Clock)];
        });
        _ = provider.GetReadingsAsync().GetAwaiter().GetResult();
        time.Advance(TimeSpan.FromSeconds(5));
        _ = provider.GetReadingsAsync().GetAwaiter().GetResult();
        Equal(2, queries, "expired query count");
    }

    private static void WmiFallbackFailureBacksOff()
    {
        int queries = 0;
        ManualTimeProvider time = new();
        using WmiCpuClockSensorProvider provider = new(time, _ =>
        {
            queries++;
            throw new InvalidOperationException("expected test failure");
        });
        _ = provider.GetReadingsAsync().GetAwaiter().GetResult();
        time.Advance(TimeSpan.FromSeconds(4));
        _ = provider.GetReadingsAsync().GetAwaiter().GetResult();
        Equal(1, queries, "failure backoff query count");
    }

    private static void GameFileNamesAreSanitized()
    {
        string invalid = "Bad" + Path.GetInvalidFileNameChars()[0] + "Game.";
        string result = GameSessionFileNaming.Sanitize(invalid);
        False(result.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0, "invalid file characters");
        False(result.EndsWith(".", StringComparison.Ordinal), "trailing period");
    }

    private static void GameFileNamesAreUnique()
    {
        RunInTempDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "Game.csv"), "x");
            string path = GameSessionFileNaming.CreateUniquePath(directory, "Game", ".csv");
            Equal("Game-2.csv", Path.GetFileName(path), "unique name");
            return Task.CompletedTask;
        });
    }

    private static void GameCsvEscapesText()
    {
        Guid session = Guid.NewGuid();
        string line = GameCsvFormatting.FormatSample(new GameFrameSample
        {
            CaptureSessionId = session,
            Timestamp = DateTimeOffset.UnixEpoch,
            ProcessId = 1,
            ProcessName = "Game, \"Special\"",
            FrameTimeMs = 10,
            Fps = 100,
            PresentMode = "Mode,Flip"
        });
        True(line.Contains("\"Game, \"\"Special\"\"\"", StringComparison.Ordinal), "quoted process name");
        True(line.Contains("\"Mode,Flip\"", StringComparison.Ordinal), "quoted mode");
    }

    private static void GameCsvReadsQuotedNumericField()
    {
        True(GameCsvFormatting.TryGetDoubleField("a,\"b,c\",\"12.5\",z", 2, out double value), "quoted numeric parse");
        NearlyEqual(12.5, value, "quoted numeric value");
    }

    private static void GameCsvEmptyExportReturnsNull()
    {
        RunInTempDirectory(async directory =>
        {
            string? path = await GameCsvExporter.ExportAsync([], directory, "empty");
            Null(path, "empty export path");
            Equal(0, Directory.GetFiles(directory).Length, "empty export files");
        });
    }

    private static void GameCsvExportWritesBomAndHeader()
    {
        RunInTempDirectory(async directory =>
        {
            string path = NotNull(await GameCsvExporter.ExportAsync(
                [Sample(Guid.NewGuid(), 10)], directory, "export"), "export path");
            byte[] bytes = File.ReadAllBytes(path);
            True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM");
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Equal(GameCsvFormatting.Header, lines[0], "CSV header");
            Equal(2, lines.Length, "CSV lines");
        });
    }

    private static void RecorderExposesPartialPath()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            await recorder.StartAsync(StartInfo(Guid.NewGuid(), 1));
            True(recorder.CurrentFilePath?.EndsWith(".csv.partial", StringComparison.OrdinalIgnoreCase) == true, "partial path");
            await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
        });
    }

    private static void RecorderRemovesEmptySession()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            await recorder.StartAsync(StartInfo(Guid.NewGuid(), 1));
            GameSessionRecordInfo? result = await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
            Null(result, "empty record");
            Equal(0, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length, "empty files");
        });
    }

    private static void RecorderFinalizesCsvAndSummary()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            Guid session = Guid.NewGuid();
            await recorder.StartAsync(StartInfo(session, 2));
            True(recorder.TryRecord(Sample(session, 10), session, 2), "record sample");
            GameSessionRecordInfo record = NotNull(
                await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true), "completed record");
            True(File.Exists(record.CsvPath), "final CSV");
            True(File.Exists(record.SummaryPath), "summary JSON");
            False(File.Exists(record.CsvPath + ".partial"), "partial removed");
        });
    }

    private static void RecorderSummaryPreservesCounts()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            Guid session = Guid.NewGuid();
            await recorder.StartAsync(StartInfo(session, 3));
            for (int index = 0; index < 5; index++)
            {
                recorder.TryRecord(Sample(session, 10 + index), session, 3);
            }

            GameSessionRecordInfo record = NotNull(await recorder.CompleteAsync(GameSessionEndReason.TargetProcessExited, true), "record");
            using JsonDocument summary = JsonDocument.Parse(await File.ReadAllTextAsync(record.SummaryPath!));
            Equal(5L, summary.RootElement.GetProperty("ReceivedSampleCount").GetInt64(), "received count");
            Equal(5L, summary.RootElement.GetProperty("WrittenSampleCount").GetInt64(), "written count");
            Equal("TargetProcessExited", summary.RootElement.GetProperty("EndReason").GetString(), "end reason");
        });
    }

    private static void RecorderRejectsWrongGeneration()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            Guid session = Guid.NewGuid();
            await recorder.StartAsync(StartInfo(session, 4));
            False(recorder.TryRecord(Sample(session, 10), session, 5), "wrong generation");
            False(recorder.TryRecord(Sample(Guid.NewGuid(), 10), Guid.NewGuid(), 4), "wrong session");
            Null(await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true), "no accepted samples");
        });
    }

    private static void RecorderRecoversDataPartial()
    {
        RunInTempDirectory(async directory =>
        {
            string partial = Path.Combine(directory, "Game.csv.partial");
            await File.WriteAllLinesAsync(partial, [GameCsvFormatting.Header, GameCsvFormatting.FormatSample(Sample(Guid.NewGuid(), 10))]);
            await using CsvGameSessionRecorder recorder = new(directory);
            await recorder.RecoverIncompleteSessionsAsync();
            False(File.Exists(partial), "partial moved");
            Equal(1, Directory.GetFiles(directory, "*.csv.incomplete").Length, "incomplete file");
        });
    }

    private static void RecorderRemovesEmptyPartial()
    {
        RunInTempDirectory(async directory =>
        {
            string partial = Path.Combine(directory, "Empty.csv.partial");
            await File.WriteAllTextAsync(partial, GameCsvFormatting.Header + Environment.NewLine);
            await using CsvGameSessionRecorder recorder = new(directory);
            await recorder.RecoverIncompleteSessionsAsync();
            False(File.Exists(partial), "header-only partial removed");
        });
    }

    private static void RecorderRecentListObeysMaximum()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            for (int index = 0; index < 3; index++)
            {
                Guid session = Guid.NewGuid();
                await recorder.StartAsync(StartInfo(session, index + 1, DateTimeOffset.Now.AddMinutes(index)));
                recorder.TryRecord(Sample(session, 10), session, index + 1);
                await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
            }

            IReadOnlyList<GameSessionRecordInfo> recent = await recorder.GetRecentRecordsAsync(2);
            Equal(2, recent.Count, "recent maximum");
            True(recent[0].StartedAt >= recent[1].StartedAt, "recent ordering");
        });
    }

    private static void RecorderReportsDirectorySize()
    {
        RunInTempDirectory(async directory =>
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "size.bin"), new byte[1234]);
            await using CsvGameSessionRecorder recorder = new(directory);
            Equal(1234L, await recorder.GetDirectorySizeAsync(), "directory size");
        });
    }

    private static void RecorderGroupsSessionsByMonth()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            Guid session = Guid.NewGuid();
            DateTimeOffset started = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
            await recorder.StartAsync(StartInfo(session, 1, started));
            recorder.TryRecord(Sample(session, 10), session, 1);
            GameSessionRecordInfo record = NotNull(await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true), "month record");
            Equal("2026-07", new DirectoryInfo(Path.GetDirectoryName(record.CsvPath)!).Name, "month directory");
        });
    }

    private static void RecorderCalculatesSessionLowFps()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory);
            Guid session = Guid.NewGuid();
            await recorder.StartAsync(StartInfo(session, 1));
            for (int index = 0; index < 100; index++)
            {
                recorder.TryRecord(Sample(session, index == 99 ? 25 : 10), session, 1);
            }

            GameSessionRecordInfo record = NotNull(await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true), "low record");
            using JsonDocument summary = JsonDocument.Parse(await File.ReadAllTextAsync(record.SummaryPath!));
            NearlyEqual(40, summary.RootElement.GetProperty("OnePercentLowFps").GetDouble(), "session 1% low");
            True(summary.RootElement.GetProperty("ZeroPointOnePercentLowFps").ValueKind == JsonValueKind.Null, "session 0.1% threshold");
        });
    }

    private static void PresentMonEndToEndAutomaticRecording()
    {
        RunInTempDirectory(async directory =>
        {
            string executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Test executable path is unavailable.");
            await using CsvGameSessionRecorder recorder = new(directory);
            using PresentMonGamePerformanceService service = new(
                executablePath,
                isElevated: true,
                sessionRecorder: recorder,
                isSessionRecordingEnabled: () => true);
            TaskCompletionSource<GameFrameSample> firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
            service.FrameReceived += (_, sample) => firstFrame.TrySetResult(sample);
            await service.StartCaptureAsync(Candidate(Environment.ProcessId, "fake-game.exe"));
            _ = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await service.StopCaptureAsync();

            IReadOnlyList<GameSessionRecordInfo> records = await recorder.GetRecentRecordsAsync();
            Equal(1, records.Count, "automatic record count");
            True(File.Exists(records[0].CsvPath), "automatic record CSV");
            True(File.Exists(records[0].SummaryPath), "automatic record summary");
        });
    }

    private static SensorReading Reading(
        double value,
        SensorCategory category,
        SensorType type,
        string sensorName = "Sensor")
    {
        return new SensorReading
        {
            DeviceName = "Device",
            SensorName = sensorName,
            Category = category,
            Type = type,
            Value = value,
            IsAvailable = true,
            Availability = SensorAvailability.Available,
            Status = HardwareStatus.Normal,
            Source = "Test",
            RawIdentifier = sensorName
        };
    }

    private static GameSessionStartInfo StartInfo(
        Guid sessionId,
        int generation,
        DateTimeOffset? startedAt = null)
    {
        return new GameSessionStartInfo
        {
            CaptureSessionId = sessionId,
            Generation = generation,
            ProcessId = 42,
            ProcessName = "TestGame",
            WindowTitle = "Test Game",
            ExecutablePath = @"C:\Games\TestGame.exe",
            CaptureStartedAt = startedAt ?? DateTimeOffset.Now
        };
    }

    private static void RunInTempDirectory(Func<string, Task> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "HardwareVision.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            action(directory).GetAwaiter().GetResult();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(message);
            }

            Thread.Sleep(5);
        }
    }

    private static PresentMonCsvParser NewParser()
    {
        return new PresentMonCsvParser(Guid.NewGuid(), 1, "fallback.exe");
    }

    private static GamePerformanceSnapshot CalculateFrames(int count)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        GameFrameSample[] samples = Enumerable.Range(0, count)
            .Select(index => new GameFrameSample
            {
                Timestamp = now,
                FrameTimeMs = index == count - 1 ? 25d : 10d,
                Fps = index == count - 1 ? 40d : 100d
            })
            .ToArray();
        return GameFrameStatisticsCalculator.Calculate(samples, TimeSpan.FromMinutes(1));
    }

    private static GameFrameSample Sample(Guid sessionId, double frameTime)
    {
        return new GameFrameSample
        {
            CaptureSessionId = sessionId,
            Timestamp = DateTimeOffset.Now,
            FrameTimeMs = frameTime,
            Fps = 1000d / frameTime
        };
    }

    private static GameProcessInfo Candidate(
        int processId,
        string processName,
        string? windowTitle = null,
        string? filePath = null,
        DateTimeOffset? startTimeUtc = null,
        bool isRunning = true)
    {
        return new GameProcessInfo
        {
            ProcessId = processId,
            ProcessName = processName,
            WindowTitle = windowTitle,
            FilePath = filePath,
            StartTimeUtc = startTimeUtc,
            IsRunning = isRunning,
            HasVisibleMainWindow = !string.IsNullOrWhiteSpace(windowTitle),
            DisplayName = string.IsNullOrWhiteSpace(windowTitle)
                ? $"{processName} ({processId})"
                : $"{windowTitle} - {processName} ({processId})"
        };
    }

    private static int RunFakePresentMon(IReadOnlyList<string> args)
    {
        int outputFileIndex = args
            .Select((value, index) => new { Value = value, Index = index })
            .FirstOrDefault(item => item.Value.Equals("--output_file", StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;
        string? outputFilePath = outputFileIndex >= 0 && outputFileIndex + 1 < args.Count
            ? args[outputFileIndex + 1]
            : null;
        int sessionNameIndex = args
            .Select((value, index) => new { Value = value, Index = index })
            .FirstOrDefault(item => item.Value.Equals("--session_name", StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;
        int targetProcessId = sessionNameIndex >= 0 && sessionNameIndex + 1 < args.Count
            ? ParseFakeTargetProcessId(args[sessionNameIndex + 1])
            : 0;
        if (Environment.GetEnvironmentVariable("HARDWAREVISION_FAKE_SCHEMA_MISMATCH") == "1")
        {
            WriteFakePresentMonOutput(outputFilePath, "Application,ProcessID,GPUTime,PresentMode");
            return 0;
        }

        WriteFakePresentMonOutput(
            outputFilePath,
            "Application,ProcessID,SwapChainAddress,PresentRuntime,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,ClickToPhotonLatency,PresentMode,FrameType",
            "other-game.exe,123,0xOTHER,DXGI,8,2,1,4,6,5,1,20,7,40,Independent Flip,Application",
            $"fake-game.exe,{targetProcessId},0xFAKE,DXGI,10,3,1,5,7,6,2,30,9,50,Independent Flip,Application");
        return 0;
    }

    private static int ParseFakeTargetProcessId(string sessionName)
    {
        string[] parts = sessionName.Split('-');
        return parts.Length >= 3 && int.TryParse(parts[2], out int processId)
            ? processId
            : 0;
    }

    private static void WriteFakePresentMonOutput(string? outputFilePath, params string[] lines)
    {
        if (outputFilePath is null)
        {
            foreach (string line in lines)
            {
                Console.WriteLine(line);
            }

            Thread.Sleep(1000);
            return;
        }

        using StreamWriter writer = new(outputFilePath, append: false, new UTF8Encoding(false));
        foreach (string line in lines)
        {
            writer.WriteLine(line);
            writer.Flush();
        }

        Thread.Sleep(1000);
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
        })
        {
            IsBackground = true,
            Name = "HardwareVision.Tests.Dispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!completed.Wait(TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("Dispatcher-based ViewModel test timed out.");
        }

        thread.Join(TimeSpan.FromSeconds(5));
        if (failure is not null)
        {
            throw new InvalidOperationException("Dispatcher-based ViewModel test failed.", failure);
        }
    }

    private sealed class FixedForegroundProcessTracker : IForegroundProcessTracker
    {
        public ForegroundProcessSnapshot? Snapshot { get; init; }

        public ForegroundProcessSnapshot? GetSnapshot()
        {
            return Snapshot;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow = new(2026, 7, 13, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow += duration;
        }
    }

    private sealed class FakeGamePerformanceService : IGamePerformanceService
    {
        private IReadOnlyList<GameProcessInfo> candidates;

        public FakeGamePerformanceService(IReadOnlyList<GameProcessInfo> candidates)
        {
            this.candidates = candidates;
        }

        public event EventHandler<GameFrameSample>? FrameReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<GameCaptureStateChangedEventArgs>? CaptureStateChanged;

        public bool IsCaptureAvailable => true;

        public string StatusText { get; private set; } = "idle";

        public GameCaptureState CaptureState { get; private set; } = GameCaptureState.Idle;

        public string? CaptureToolPath => null;

        public IReadOnlyList<GameFrameSample> RecentSamples => Array.Empty<GameFrameSample>();

        public GamePerformanceSnapshot GetSnapshot(TimeSpan window)
        {
            return new GamePerformanceSnapshot();
        }

        public Task<IReadOnlyList<GameProcessInfo>> GetCandidateProcessesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(candidates);
        }

        public Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RaiseCaptureState(GameCaptureState.Capturing, "capturing");
            return Task.CompletedTask;
        }

        public Task StopCaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RaiseCaptureState(GameCaptureState.Idle, "idle");
            return Task.CompletedTask;
        }

        public Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ExportWindowCsvAsync(
            string directory,
            TimeSpan window,
            string? processName = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public Task<string?> ExportCacheCsvAsync(
            string directory,
            string? processName = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>(null);
        }

        public void SetCandidates(IReadOnlyList<GameProcessInfo> value)
        {
            candidates = value;
        }

        public void RaiseCaptureState(GameCaptureState state, string status)
        {
            CaptureState = state;
            StatusText = status;
            CaptureStateChanged?.Invoke(this, new GameCaptureStateChangedEventArgs(state, status, null));
        }

        public void Dispose()
        {
        }
    }

    private static T NotNull<T>(T? value, string message) where T : class
    {
        return value ?? throw new InvalidOperationException($"{message}: expected non-null value.");
    }

    private static double NotNull(double? value, string message)
    {
        return value ?? throw new InvalidOperationException($"{message}: expected non-null value.");
    }

    private static void Null(object? value, string message)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"{message}: expected null, got {value}.");
        }
    }

    private static void NearlyEqual(double expected, double? actual, string message, double tolerance = 0.000001)
    {
        if (!actual.HasValue || Math.Abs(expected - actual.Value) > tolerance)
        {
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual?.ToString() ?? "null"}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}.");
        }
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{message}: expected true.");
        }
    }

    private static void False(bool value, string message)
    {
        if (value)
        {
            throw new InvalidOperationException($"{message}: expected false.");
        }
    }
}
