using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using System.Diagnostics;

namespace HardwareVision.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--presentmon-benchmark", StringComparer.OrdinalIgnoreCase))
        {
            return RunPresentMonBenchmark();
        }

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
            ("Non-target PID filters before numeric parsing", NonTargetPidFiltersBeforeNumericParsing),
            ("Target PID still parses quoted fields", TargetPidParsesQuotedFields),
            ("Frame samples do not retain raw CSV", FrameSamplesDoNotRetainRawCsv),
            ("Game ViewModel does not subscribe per frame", GameViewModelDoesNotSubscribePerFrame),
            ("Game UI timer follows page activation", GameUiTimerFollowsPageActivation),
            ("Limit ViewModel updates collection incrementally", LimitViewModelUpdatesIncrementally),
            ("Single-flight first entry", SingleFlightFirstEntry),
            ("Single-flight exit allows next entry", SingleFlightExitAllowsNextEntry),
            ("Single-flight permits one concurrent winner", SingleFlightConcurrentWinner),
            ("Polling subscriber failures are isolated", PollingSubscriberFailuresAreIsolated),
            ("Polling does not reenter slow collection", PollingDoesNotReenterSlowCollection),
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
            ("Recorder recovery is single-flight", RecorderRecoveryIsSingleFlight),
            ("Recorder completion is idempotent", RecorderCompletionIsIdempotent),
            ("Recorder recent list obeys maximum", RecorderRecentListObeysMaximum),
            ("Recorder reports directory size", RecorderReportsDirectorySize),
            ("Recorder groups sessions by month", RecorderGroupsSessionsByMonth),
            ("Recorder calculates session low FPS", RecorderCalculatesSessionLowFps),
            ("PresentMon end-to-end automatic recording", PresentMonEndToEndAutomaticRecording),
            ("Energy selects CPU package once", EnergySelectsCpuPackageOnce),
            ("Energy prefers GPU board power", EnergyPrefersGpuBoardPower),
            ("Energy sums CPU and GPU", EnergySumsCpuAndGpu),
            ("Energy keeps multiple GPUs separate", EnergyKeepsMultipleGpusSeparate),
            ("Energy rejects invalid readings", EnergyRejectsInvalidReadings),
            ("Energy rejects non-watt units", EnergyRejectsNonWattUnits),
            ("Energy missing power does not fabricate zero", EnergyMissingPowerDoesNotFabricateZero),
            ("Energy integrates constant power", EnergyIntegratesConstantPower),
            ("Energy applies trapezoidal integration", EnergyAppliesTrapezoidalIntegration),
            ("Energy first sample is only an anchor", EnergyFirstSampleIsOnlyAnchor),
            ("Energy skips long foreground gaps", EnergySkipsLongForegroundGaps),
            ("Energy allows configured background interval", EnergyAllowsBackgroundInterval),
            ("Energy missing sample breaks continuity", EnergyMissingSampleBreaksContinuity),
            ("Energy keeps CPU integrating when GPU disappears", EnergyCpuContinuesWhenGpuDisappears),
            ("Energy keeps GPU integrating when CPU disappears", EnergyGpuContinuesWhenCpuDisappears),
            ("Energy does not bridge a returning component gap", EnergyReturningComponentDoesNotBridgeGap),
            ("Energy rejects foreign generation", EnergyRejectsForeignGeneration),
            ("Energy new session resets state", EnergyNewSessionResetsState),
            ("Energy completion freezes session", EnergyCompletionFreezesSession),
            ("Energy formatting thresholds", EnergyFormattingThresholds),
            ("Energy summary remains backward compatible", EnergySummaryBackwardCompatible),
            ("Recorder summary includes energy", RecorderSummaryIncludesEnergy),
            ("Limit reasons merge within one CPU poll", LimitReasonsMergeWithinOneCpuPoll),
            ("Limit events separate CPU and GPU", LimitEventsSeparateCpuAndGpu),
            ("Limit detection ignores inferred and configured values", LimitDetectionIgnoresNoise),
            ("Limit event duration extends without duplication", LimitEventDurationExtends),
            ("Limit reason changes create a new event", LimitReasonChangeCreatesNewEvent),
            ("Limit clear state finalizes the active event", LimitClearFinalizesEvent),
            ("Limit tracker rejects foreign generation", LimitTrackerRejectsForeignGeneration),
            ("Limit tracker resets for a new session", LimitTrackerResetsForNewSession),
            ("Limit tracker completion freezes events", LimitTrackerCompletionFreezesEvents),
            ("Limit event history is bounded", LimitEventHistoryIsBounded),
            ("Limit single-sample spike is ignored", LimitSingleSampleSpikeIsIgnored),
            ("Limit clear requires confirmation", LimitClearRequiresConfirmation),
            ("Limit temporary failure preserves active event", LimitTemporaryFailurePreservesActiveEvent),
            ("Limit matching event merges within five seconds", LimitMatchingEventMergesWithinFiveSeconds),
            ("Limit unchanged state suppresses snapshots", LimitUnchangedStateSuppressesSnapshots),
            ("Limit support states remain distinct", LimitSupportStatesRemainDistinct),
            ("NVIDIA normal reasons are not anomalies", NvidiaNormalReasonsAreNotAnomalies),
            ("Limit status text is localized", LimitStatusTextIsLocalized),
            ("Legacy summary reports limits as unrecorded", LegacySummaryLimitsAreUnrecorded),
            ("Recorder summary includes performance limits", RecorderSummaryIncludesPerformanceLimits),
            ("Windows CPU limit flags expand without guessing", WindowsCpuLimitFlagsExpand),
            ("NVIDIA NVML limit reasons map to explicit labels", NvidiaLimitReasonsMap),
            ("Disk merges by exact serial", DiskMergesByExactSerial),
            ("Disk refuses conflicting serials", DiskRefusesConflictingSerials),
            ("Disk merges matching model and size", DiskMergesMatchingModelAndSize),
            ("Disk refuses mismatched sizes", DiskRefusesMismatchedSizes),
            ("Disk refuses ambiguous equal devices", DiskRefusesAmbiguousEqualDevices),
            ("Disk reliability temperature has priority", DiskReliabilityTemperatureHasPriority),
            ("Disk falls back to LHM temperature", DiskFallsBackToLhmTemperature),
            ("Disk wear derives remaining life", DiskWearDerivesRemainingLife),
            ("Disk explicit remaining life wins", DiskExplicitRemainingLifeWins),
            ("Disk reliability counters map", DiskReliabilityCountersMap),
            ("Disk storage health overrides Win32", DiskStorageHealthOverridesWin32),
            ("Disk operational status maps", DiskOperationalStatusMaps),
            ("Disk ambiguous LHM sensor stays isolated", DiskAmbiguousLhmSensorStaysIsolated),
            ("Disk LHM identifiers remain separate", DiskLhmIdentifiersRemainSeparate),
            ("Disk performance matches exact index", DiskPerformanceMatchesExactIndex),
            ("Disk unsupported reliability remains empty", DiskUnsupportedReliabilityRemainsEmpty),
            ("Disk reliability source is visible", DiskReliabilitySourceIsVisible),
            ("Disk preferred selection uses system disk", DiskPreferredSelectionUsesSystemDisk),
            ("Disk reliability matches exact DeviceId", DiskReliabilityMatchesExactDeviceId),
            ("Disk total read and write names are recognized", DiskTotalNamesAreRecognized),
            ("Disk known data units normalize to bytes", DiskKnownDataUnitsNormalizeToBytes),
            ("Disk unknown data units remain unchanged", DiskUnknownDataUnitsRemainUnchanged),
            ("Disk lifetime metrics are default visible", DiskLifetimeMetricsAreDefaultVisible),
            ("Disk missing core values display placeholders", DiskMissingCoreValuesDisplayPlaceholders),
            ("Disk Storage WMI cache is low frequency", DiskStorageWmiCacheIsLowFrequency)
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

    private static void NonTargetPidFiltersBeforeNumericParsing()
    {
        PresentMonCsvParser parser = new(Guid.NewGuid(), 42, "target.exe", targetProcessId: 42);
        Equal(
            PresentMonCsvParseKind.HeaderAccepted,
            parser.ParseLine("Application,ProcessID,FrameTime,CPUBusy,GPUTime,DisplayLatency").Kind,
            "filter header");
        PresentMonCsvParseResult result = parser.ParseLine(
            "\"other,game.exe\",99,not-a-number,also-bad,Infinity,NaN");
        Equal(PresentMonCsvParseKind.Filtered, result.Kind, "non-target result");
        Equal(99, result.FilteredProcessId, "filtered PID");
        Equal(0L, parser.NumericFieldParseCount, "non-target numeric parsing");
        Equal(0L, parser.SampleCreationCount, "non-target sample creation");
        Null(result.Sample, "non-target sample");
    }

    private static void TargetPidParsesQuotedFields()
    {
        PresentMonCsvParser parser = new(Guid.NewGuid(), 42, "target.exe", targetProcessId: 42);
        parser.ParseLine("Application,ProcessID,FrameTime,PresentMode,FrameType");
        GameFrameSample sample = NotNull(
            parser.ParseLine("\"ignored,name.exe\",42,4.167,\"Mode, Flip\",\"App, Frame\"").Sample,
            "target sample");
        Equal("target.exe", sample.ProcessName, "configured target name reused");
        Equal("Mode, Flip", sample.PresentMode, "quoted target mode");
        Equal("App, Frame", sample.FrameType, "quoted target frame type");
        Equal(1L, parser.SampleCreationCount, "one target sample");
    }

    private static void FrameSamplesDoNotRetainRawCsv()
    {
        Null(typeof(GameFrameSample).GetProperty("RawLine"), "RawLine property removed");
        string formatted = GameCsvFormatting.FormatSample(new GameFrameSample
        {
            CaptureSessionId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UnixEpoch,
            ProcessId = 1,
            ProcessName = "game.exe",
            FrameTimeMs = 10,
            Fps = 100
        });
        True(formatted.Contains("game.exe", StringComparison.Ordinal), "structured CSV formatting");
    }

    private static void GameViewModelDoesNotSubscribePerFrame()
    {
        RunOnDispatcher(dispatcher =>
        {
            FakeGamePerformanceService service = new([]);
            using GamePerformanceViewModel viewModel = new(service, dispatcher);
            Equal(0, service.FrameSubscriberCount, "frame subscriber count");
            viewModel.SetActive(true);
            Equal(0, service.FrameSubscriberCount, "active frame subscriber count");
            return Task.CompletedTask;
        });
    }

    private static void GameUiTimerFollowsPageActivation()
    {
        RunOnDispatcher(dispatcher =>
        {
            using GamePerformanceViewModel viewModel = new(new FakeGamePerformanceService([]), dispatcher);
            False(viewModel.IsUiRefreshTimerEnabled, "initial UI timer");
            viewModel.SetActive(true);
            True(viewModel.IsUiRefreshTimerEnabled, "active UI timer");
            viewModel.SetActive(false);
            False(viewModel.IsUiRefreshTimerEnabled, "hidden UI timer");
            return Task.CompletedTask;
        });
    }

    private static void LimitViewModelUpdatesIncrementally()
    {
        RunOnDispatcher(dispatcher =>
        {
            using GamePerformanceViewModel viewModel = new(new FakeGamePerformanceService([]), dispatcher);
            int resetEvents = 0;
            viewModel.PerformanceLimitEvents.CollectionChanged += (_, args) =>
            {
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resetEvents++;
            };
            GamePerformanceLimitEvent first = new()
            {
                EventId = 1,
                ProcessorType = PerformanceLimitProcessorType.Cpu,
                Duration = TimeSpan.FromSeconds(1),
                IsActive = true,
                Reasons = ["CPU Thermal Throttling"]
            };
            viewModel.ApplyPerformanceLimitSnapshot(new GamePerformanceLimitSnapshot
            {
                IsTracking = true,
                Events = [first]
            });
            GamePerformanceLimitEvent updated = new()
            {
                EventId = 1,
                ProcessorType = PerformanceLimitProcessorType.Cpu,
                Duration = TimeSpan.FromSeconds(2),
                IsActive = true,
                Reasons = first.Reasons
            };
            GamePerformanceLimitEvent second = new()
            {
                EventId = 2,
                ProcessorType = PerformanceLimitProcessorType.Gpu,
                IsActive = false,
                Reasons = ["GPU Performance Limit - Power"]
            };
            viewModel.ApplyPerformanceLimitSnapshot(new GamePerformanceLimitSnapshot
            {
                IsTracking = true,
                Events = [second, updated]
            });
            Equal(0, resetEvents, "collection reset count");
            Equal(2L, viewModel.PerformanceLimitEvents[0].EventId, "incremental newest event");
            Equal(2d, viewModel.PerformanceLimitEvents[1].Duration.TotalSeconds, "incremental duration replacement");
            return Task.CompletedTask;
        });
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

    private static void PollingSubscriberFailuresAreIsolated()
    {
        FakeSensorService sensors = new(TimeSpan.Zero);
        using PollingService polling = new(sensors, new AppSettings { RefreshIntervalSeconds = 0.5 });
        using ManualResetEventSlim reachedSecondSubscriber = new();
        polling.ReadingsUpdated += (_, _) => throw new InvalidOperationException("expected test failure");
        polling.ReadingsUpdated += (_, _) => reachedSecondSubscriber.Set();
        polling.StartAsync().GetAwaiter().GetResult();
        True(reachedSecondSubscriber.Wait(TimeSpan.FromSeconds(5)), "second subscriber reached");
        polling.StopAsync().GetAwaiter().GetResult();
    }

    private static void PollingDoesNotReenterSlowCollection()
    {
        FakeSensorService sensors = new(TimeSpan.FromMilliseconds(700));
        using PollingService polling = new(sensors, new AppSettings { RefreshIntervalSeconds = 0.5 });
        polling.StartAsync().GetAwaiter().GetResult();
        Thread.Sleep(1_600);
        polling.StopAsync().GetAwaiter().GetResult();
        Equal(1, sensors.MaximumConcurrentReads, "maximum concurrent sensor reads");
        True(sensors.ReadCount >= 1, "slow polling read count");
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

    private static void RecorderRecoveryIsSingleFlight()
    {
        RunInTempDirectory(async directory =>
        {
            string partial = Path.Combine(directory, "single-flight.csv.partial");
            await File.WriteAllLinesAsync(partial, [GameCsvFormatting.Header, "data"]);
            await using CsvGameSessionRecorder recorder = new(directory, 8);
            Task first = recorder.RecoverIncompleteSessionsAsync();
            Task second = recorder.RecoverIncompleteSessionsAsync();
            await Task.WhenAll(first, second);
            Equal(1, Directory.GetFiles(directory, "*.csv.incomplete", SearchOption.AllDirectories).Length, "single recovered file");
            Equal(0, Directory.GetFiles(directory, "*.csv.partial", SearchOption.AllDirectories).Length, "no partial after recovery");
        });
    }

    private static void RecorderCompletionIsIdempotent()
    {
        RunInTempDirectory(async directory =>
        {
            await using CsvGameSessionRecorder recorder = new(directory, 8);
            Guid id = Guid.NewGuid();
            GameSessionStartInfo startInfo = new()
            {
                CaptureSessionId = id,
                Generation = 1,
                ProcessId = 1,
                ProcessName = "idempotent",
                CaptureStartedAt = DateTimeOffset.Now
            };
            await recorder.StartAsync(startInfo);
            True(recorder.TryRecord(Sample(id, 10), id, 1), "idempotent sample accepted");
            Task<GameSessionRecordInfo?> first = recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
            Task<GameSessionRecordInfo?> second = recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
            GameSessionRecordInfo?[] results = await Task.WhenAll(first, second);
            Equal(1, results.Count(result => result is not null), "single completion result");
            Equal(1, Directory.GetFiles(directory, "*.summary.json", SearchOption.AllDirectories).Length, "single summary file");
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

    private static void EnergySelectsCpuPackageOnce()
    {
        GameEnergyTracker.SelectedPowerSample sample = NotNull(
            GameEnergyTracker.SelectPowerSample([
                PowerReading(SensorCategory.Cpu, "CPU Core #1", 35, "/intelcpu/0/power/0"),
                PowerReading(SensorCategory.Cpu, "CPU Package", 80, "/intelcpu/0/power/1"),
                PowerReading(SensorCategory.Cpu, "CPU Total", 75, "/intelcpu/0/power/2")]),
            "CPU package selection");
        NearlyEqual(80, sample.PowerWatts, "one CPU package value");
    }

    private static void EnergyPrefersGpuBoardPower()
    {
        GameEnergyTracker.SelectedPowerSample sample = NotNull(
            GameEnergyTracker.SelectPowerSample([
                PowerReading(SensorCategory.Gpu, "GPU Package Power", 150, "/gpu-nvidia/0/power/0"),
                PowerReading(SensorCategory.Gpu, "GPU Board Power", 180, "/gpu-nvidia/0/power/1")]),
            "GPU board selection");
        NearlyEqual(180, sample.PowerWatts, "board power priority");
    }

    private static void EnergySumsCpuAndGpu()
    {
        GameEnergyTracker.SelectedPowerSample sample = NotNull(
            GameEnergyTracker.SelectPowerSample([
                PowerReading(SensorCategory.Cpu, "CPU Package", 90, "/intelcpu/0/power/0", "CPU A"),
                PowerReading(SensorCategory.Gpu, "GPU Board Power", 210, "/gpu-nvidia/0/power/0", "GPU A")]),
            "CPU and GPU selection");
        NearlyEqual(300, sample.PowerWatts, "CPU plus GPU");
        True(sample.IncludedComponents.Contains("CPU A", StringComparison.Ordinal), "CPU component text");
        True(sample.IncludedComponents.Contains("GPU A", StringComparison.Ordinal), "GPU component text");
    }

    private static void EnergyKeepsMultipleGpusSeparate()
    {
        GameEnergyTracker.SelectedPowerSample sample = NotNull(
            GameEnergyTracker.SelectPowerSample([
                PowerReading(SensorCategory.Gpu, "GPU Board Power", 100, "/gpu-nvidia/0/power/0", "GPU A"),
                PowerReading(SensorCategory.Gpu, "GPU Board Power", 120, "/gpu-nvidia/1/power/0", "GPU B")]),
            "multiple GPU selection");
        NearlyEqual(220, sample.PowerWatts, "two GPU sum");
        True(sample.IncludedComponents.Contains("GPU 1", StringComparison.Ordinal), "first GPU label");
        True(sample.IncludedComponents.Contains("GPU 2", StringComparison.Ordinal), "second GPU label");
    }

    private static void EnergyRejectsInvalidReadings()
    {
        SensorReading unavailable = PowerReading(SensorCategory.Cpu, "CPU Package", 50, "/intelcpu/0/power/0");
        unavailable.IsAvailable = false;
        Null(GameEnergyTracker.SelectPowerSample([
            unavailable,
            PowerReading(SensorCategory.Cpu, "CPU Package", double.NaN, "/intelcpu/1/power/0"),
            PowerReading(SensorCategory.Gpu, "GPU Board Power", -1, "/gpu-nvidia/0/power/0"),
            PowerReading(SensorCategory.Gpu, "GPU Board Power", 5000, "/gpu-nvidia/1/power/0")]),
            "invalid readings");
    }

    private static void EnergyRejectsNonWattUnits()
    {
        SensorReading reading = PowerReading(SensorCategory.Cpu, "CPU Package", 50, "/intelcpu/0/power/0");
        reading.Unit = "mW";
        Null(GameEnergyTracker.SelectPowerSample([reading]), "milliwatt reading");
    }

    private static void EnergyMissingPowerDoesNotFabricateZero()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        tracker.RecordReadings(id, generation, Array.Empty<SensorReading>(), 1000, false);
        Null(tracker.CurrentSnapshot.EstimatedEnergyWh, "missing energy");
        Null(tracker.CurrentSnapshot.CurrentEstimatedPowerWatts, "missing current power");
        Null(tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "missing average power");
    }

    private static void EnergyIntegratesConstantPower()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 0, false);
        tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 1000, false);
        NearlyEqual(100d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "constant energy");
        NearlyEqual(1, tracker.CurrentSnapshot.ValidIntegrationDuration.TotalSeconds, "constant valid duration");
        NearlyEqual(100, tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "constant average power");
    }

    private static void EnergyAppliesTrapezoidalIntegration()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 0, false);
        tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 200, "/intelcpu/0/power/0")], 2000, false);
        NearlyEqual(300d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "trapezoid energy");
        NearlyEqual(150, tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "trapezoid average");
    }

    private static void EnergyFirstSampleIsOnlyAnchor()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 1000, false);
        NearlyEqual(0, tracker.CurrentSnapshot.EstimatedEnergyWh, "first sample energy");
        NearlyEqual(0, tracker.CurrentSnapshot.ValidIntegrationDuration.TotalSeconds, "first sample duration");
        Null(tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "first sample average");
    }

    private static void EnergySkipsLongForegroundGaps()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading power = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        tracker.RecordReadings(id, generation, [power], 0, false);
        tracker.RecordReadings(id, generation, [power], 5000, false);
        tracker.RecordReadings(id, generation, [power], 6000, false);
        NearlyEqual(100d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "only post-gap interval");
        NearlyEqual(1, tracker.CurrentSnapshot.ValidIntegrationDuration.TotalSeconds, "post-gap duration");
    }

    private static void EnergyAllowsBackgroundInterval()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading power = PowerReading(SensorCategory.Gpu, "GPU Board Power", 100, "/gpu-nvidia/0/power/0");
        tracker.RecordReadings(id, generation, [power], 0, true);
        tracker.RecordReadings(id, generation, [power], 10000, true);
        NearlyEqual(1000d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "background energy");
        NearlyEqual(10, tracker.CurrentSnapshot.ValidIntegrationDuration.TotalSeconds, "background duration");
    }

    private static void EnergyMissingSampleBreaksContinuity()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading power = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        tracker.RecordReadings(id, generation, [power], 0, false);
        tracker.RecordReadings(id, generation, Array.Empty<SensorReading>(), 1000, false);
        tracker.RecordReadings(id, generation, [power], 2000, false);
        tracker.RecordReadings(id, generation, [power], 3000, false);
        NearlyEqual(100d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "missing interval excluded");
        NearlyEqual(1, tracker.CurrentSnapshot.ValidIntegrationDuration.TotalSeconds, "missing duration excluded");
    }

    private static void EnergyCpuContinuesWhenGpuDisappears()
    {
        using GameEnergyTracker tracker = NewEnergyTracker(() => 0);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading cpu = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        SensorReading gpu = PowerReading(SensorCategory.Gpu, "GPU Board Power", 200, "/gpu-nvidia/0/power/0");
        tracker.RecordReadings(id, generation, [cpu, gpu], 0, false);
        tracker.RecordReadings(id, generation, [cpu], 1000, false);
        tracker.RecordReadings(id, generation, [cpu], 2000, false);
        NearlyEqual(200d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "CPU-only continuation energy");
        NearlyEqual(100, tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "CPU-only average");
    }

    private static void EnergyGpuContinuesWhenCpuDisappears()
    {
        using GameEnergyTracker tracker = NewEnergyTracker(() => 0);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading cpu = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        SensorReading gpu = PowerReading(SensorCategory.Gpu, "GPU Board Power", 200, "/gpu-nvidia/0/power/0");
        tracker.RecordReadings(id, generation, [cpu, gpu], 0, false);
        tracker.RecordReadings(id, generation, [gpu], 1000, false);
        tracker.RecordReadings(id, generation, [gpu], 2000, false);
        NearlyEqual(400d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "GPU-only continuation energy");
        NearlyEqual(200, tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "GPU-only average");
    }

    private static void EnergyReturningComponentDoesNotBridgeGap()
    {
        using GameEnergyTracker tracker = NewEnergyTracker(() => 0);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading cpu = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        SensorReading gpu = PowerReading(SensorCategory.Gpu, "GPU Board Power", 200, "/gpu-nvidia/0/power/0");
        tracker.RecordReadings(id, generation, [cpu, gpu], 0, false);
        tracker.RecordReadings(id, generation, [cpu], 1000, false);
        tracker.RecordReadings(id, generation, [cpu, gpu], 2000, false);
        tracker.RecordReadings(id, generation, [cpu, gpu], 3000, false);
        NearlyEqual(500d / 3600d, tracker.CurrentSnapshot.EstimatedEnergyWh, "returning GPU excludes gap");
        NearlyEqual(500d / 3d, tracker.CurrentSnapshot.AverageEstimatedPowerWatts, "time-weighted component average");
    }

    private static void EnergyRejectsForeignGeneration()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        tracker.RecordReadings(id, generation + 1, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 1000, false);
        tracker.RecordReadings(Guid.NewGuid(), generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 1000, false);
        Null(tracker.CurrentSnapshot.EstimatedEnergyWh, "foreign data energy");
        Null(tracker.CurrentSnapshot.CurrentEstimatedPowerWatts, "foreign data power");
    }

    private static void EnergyNewSessionResetsState()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading power = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        tracker.RecordReadings(id, generation, [power], 0, false);
        tracker.RecordReadings(id, generation, [power], 1000, false);
        clock = 2000;
        Guid nextId = Guid.NewGuid();
        tracker.StartSession(new GameSessionStartInfo { CaptureSessionId = nextId, Generation = 2, ProcessId = 2, ProcessName = "next" });
        Equal(nextId, tracker.CurrentSnapshot.CaptureSessionId, "new session id");
        Null(tracker.CurrentSnapshot.EstimatedEnergyWh, "new session energy reset");
        NearlyEqual(0, tracker.CurrentSnapshot.ValidIntegrationDuration.TotalSeconds, "new session duration reset");
    }

    private static void EnergyCompletionFreezesSession()
    {
        long clock = 0;
        using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
        (Guid id, int generation) = StartEnergySession(tracker);
        SensorReading power = PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0");
        tracker.RecordReadings(id, generation, [power], 0, false);
        tracker.RecordReadings(id, generation, [power], 1000, false);
        clock = 1000;
        GameEnergySnapshot completed = NotNull(tracker.CompleteSession(id, generation), "completed energy snapshot");
        tracker.RecordReadings(id, generation, [power], 2000, false);
        False(completed.IsTracking, "completed tracking state");
        NearlyEqual(completed.EstimatedEnergyWh!.Value, tracker.CurrentSnapshot.EstimatedEnergyWh, "frozen energy");
        NearlyEqual(1, tracker.CurrentSnapshot.SessionDuration.TotalSeconds, "frozen session duration");
    }

    private static void EnergyFormattingThresholds()
    {
        Equal("0.123 Wh", GameEnergyFormatting.FormatEnergy(0.1234), "sub-Wh formatting");
        Equal("12.35 Wh", GameEnergyFormatting.FormatEnergy(12.345), "Wh formatting");
        Equal("1.50 kWh", GameEnergyFormatting.FormatEnergy(1500), "kWh formatting");
        Equal("--", GameEnergyFormatting.FormatEnergy(null), "missing energy formatting");
    }

    private static void EnergySummaryBackwardCompatible()
    {
        GameSessionSummary summary = NotNull(JsonSerializer.Deserialize<GameSessionSummary>("{\"ProcessName\":\"legacy\"}"), "legacy summary");
        Equal("legacy", summary.ProcessName, "legacy process name");
        Null(summary.EstimatedEnergyWh, "legacy energy");
        Null(summary.EnergyCoveragePercent, "legacy coverage");
    }

    private static void RecorderSummaryIncludesEnergy()
    {
        RunInTempDirectory(async directory =>
        {
            long clock = 0;
            using GameEnergyTracker tracker = NewEnergyTracker(() => clock);
            Guid id = Guid.NewGuid();
            const int generation = 4;
            GameSessionStartInfo startInfo = new()
            {
                CaptureSessionId = id,
                Generation = generation,
                ProcessId = 44,
                ProcessName = "energy-game",
                CaptureStartedAt = DateTimeOffset.Now
            };
            tracker.StartSession(startInfo);
            tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 0, false);
            tracker.RecordReadings(id, generation, [PowerReading(SensorCategory.Cpu, "CPU Package", 100, "/intelcpu/0/power/0")], 1000, false);
            clock = 1000;
            tracker.CompleteSession(id, generation);

            await using CsvGameSessionRecorder recorder = new(directory, tracker);
            await recorder.StartAsync(startInfo);
            True(recorder.TryRecord(Sample(id, 10), id, generation), "recorded energy session frame");
            GameSessionRecordInfo record = NotNull(
                await recorder.CompleteAsync(GameSessionEndReason.UserStopped, completedNormally: true),
                "energy session record");
            await using FileStream stream = File.OpenRead(NotNull(record.SummaryPath, "energy summary path"));
            JsonSerializerOptions options = new();
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            GameSessionSummary summary = NotNull(
                await JsonSerializer.DeserializeAsync<GameSessionSummary>(stream, options),
                "energy summary JSON");
            NearlyEqual(100d / 3600d, summary.EstimatedEnergyWh, "summary energy");
            NearlyEqual(100, summary.AverageEstimatedPowerWatts, "summary average power");
            True(summary.EnergyIncludedComponents?.Contains("CPU", StringComparison.Ordinal) == true, "summary components");
        });
    }

    private static void DiskMergesByExactSerial()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", "SER-1", 1000, "0"), PhysicalDisk("physical", "Model A", "SER-1", 1000)]);
        Equal(1, disks.Count, "serial merge count");
    }

    private static void DiskRefusesConflictingSerials()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", "SER-1", 1000, "0"), PhysicalDisk("physical", "Model A", "SER-2", 1000)]);
        Equal(2, disks.Count, "serial conflict count");
    }

    private static void DiskMergesMatchingModelAndSize()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", null, 1000, "0"), PhysicalDisk("physical", "Model A", null, 1000)]);
        Equal(1, disks.Count, "model and size merge");
    }

    private static void DiskRefusesMismatchedSizes()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", null, 1000, "0"), PhysicalDisk("physical", "Model A", null, 2000)]);
        Equal(2, disks.Count, "size mismatch count");
    }

    private static void DiskRefusesAmbiguousEqualDevices()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([
            Win32Disk("win-0", "Model A", null, 1000, "0"),
            Win32Disk("win-1", "Model A", null, 1000, "1"),
            PhysicalDisk("physical", "Model A", null, 1000)]);
        Equal(3, disks.Count, "ambiguous physical disk remains separate");
    }

    private static void DiskReliabilityTemperatureHasPriority()
    {
        HardwareDevice physical = PhysicalDisk("physical", "Model A", "SER-1", 1000, ("ReliabilityTemperature", "55"));
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", "SER-1", 1000, "0"), physical],
            [DiskSensor("Model A", "Temperature", SensorType.Temperature, 40, "/hdd/0/temperature/0")]);
        NearlyEqual(55, disks[0].Temperature?.Value, "reliability temperature priority");
        Equal("MSFT_StorageReliabilityCounter", disks[0].Temperature?.Source, "temperature source");
    }

    private static void DiskFallsBackToLhmTemperature()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", "SER-1", 1000, "0")],
            [DiskSensor("Model A", "Temperature", SensorType.Temperature, 40, "/hdd/0/temperature/0")]);
        NearlyEqual(40, disks[0].Temperature?.Value, "LHM temperature fallback");
    }

    private static void DiskWearDerivesRemainingLife()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([PhysicalDisk("physical", "Model A", "SER-1", 1000, ("ReliabilityWear", "30"))]);
        NearlyEqual(30, disks[0].Wear?.Value, "wear value");
        NearlyEqual(70, disks[0].RemainingLife?.Value, "derived remaining life");
    }

    private static void DiskExplicitRemainingLifeWins()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([PhysicalDisk(
            "physical", "Model A", "SER-1", 1000,
            ("ReliabilityWear", "30"),
            ("ReliabilityRemainingLife", "88"))]);
        NearlyEqual(88, disks[0].RemainingLife?.Value, "explicit remaining life");
    }

    private static void DiskReliabilityCountersMap()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([PhysicalDisk(
            "physical", "Model A", "SER-1", 1000,
            ("ReliabilityReadBytesTotal", "10000"),
            ("ReliabilityWriteBytesTotal", "20000"),
            ("ReliabilityPowerOnHours", "300"),
            ("ReliabilityPowerCycleCount", "40"),
            ("ReliabilityReadErrorsTotal", "5"),
            ("ReliabilityWriteErrorsTotal", "6"),
            ("ReliabilityReadLatencyMax", "7"),
            ("ReliabilityWriteLatencyMax", "8"),
            ("ReliabilityFlushLatencyMax", "9"))]);
        DiskDevice disk = disks[0];
        NearlyEqual(10000, disk.ReadTotal?.Value, "read total");
        NearlyEqual(20000, disk.WriteTotal?.Value, "write total");
        NearlyEqual(300, disk.PowerOnHours?.Value, "power-on hours");
        NearlyEqual(40, disk.PowerCycleCount?.Value, "power cycles");
        NearlyEqual(5, disk.ReadErrorsTotal?.Value, "read errors");
        NearlyEqual(6, disk.WriteErrorsTotal?.Value, "write errors");
        NearlyEqual(7, disk.ReadLatencyMax?.Value, "read latency");
        NearlyEqual(8, disk.WriteLatencyMax?.Value, "write latency");
        NearlyEqual(9, disk.FlushLatencyMax?.Value, "flush latency");
    }

    private static void DiskStorageHealthOverridesWin32()
    {
        HardwareDevice win32 = Win32Disk("win", "Model A", "SER-1", 1000, "0");
        win32.Properties["Status"] = "OK";
        HardwareDevice physical = PhysicalDisk("physical", "Model A", "SER-1", 1000, ("HealthStatus", "1"));
        IReadOnlyList<DiskDevice> disks = BuildDisks([win32, physical]);
        Equal("Warning", disks[0].SmartStatus, "storage health priority");
    }

    private static void DiskOperationalStatusMaps()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([PhysicalDisk("physical", "Model A", "SER-1", 1000, ("OperationalStatus", "OK"))]);
        Equal("OK", disks[0].OperationalStatus, "operational status");
    }

    private static void DiskAmbiguousLhmSensorStaysIsolated()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win-0", "Same Model", "SER-0", 1000, "0"), Win32Disk("win-1", "Same Model", "SER-1", 1000, "1")],
            [DiskSensor("Same Model", "Temperature", SensorType.Temperature, 45, "/hdd/9/temperature/0")]);
        Equal(3, disks.Count, "ambiguous LHM creates isolated device");
        Equal(2, disks.Count(disk => disk.Sensors.Count == 0), "base disks remain untouched");
    }

    private static void DiskLhmIdentifiersRemainSeparate()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            Array.Empty<HardwareDevice>(),
            [
                DiskSensor("Same Model", "Temperature", SensorType.Temperature, 40, "/hdd/0/temperature/0"),
                DiskSensor("Same Model", "Temperature", SensorType.Temperature, 50, "/hdd/1/temperature/0")
            ]);
        Equal(2, disks.Count, "LHM root identifiers");
        Equal(2, disks.Select(disk => disk.Temperature?.Value).Distinct().Count(), "separate LHM temperatures");
    }

    private static void DiskPerformanceMatchesExactIndex()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", "SER-1", 1000, "2")],
            Array.Empty<SensorReading>(),
            [new DiskPerformanceSnapshot { InstanceName = "2 C:", ReadBytesPerSecond = 123, WriteBytesPerSecond = 456 }]);
        NearlyEqual(123, disks[0].ReadSpeed, "indexed read speed");
        NearlyEqual(456, disks[0].WriteSpeed, "indexed write speed");
    }

    private static void DiskUnsupportedReliabilityRemainsEmpty()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([PhysicalDisk("physical", "Model A", "SER-1", 1000)]);
        Null(disks[0].MaximumTemperature, "missing maximum temperature");
        Null(disks[0].ReadErrorsTotal, "missing read errors");
        Null(disks[0].ReadLatencyMax, "missing read latency");
    }

    private static void DiskReliabilitySourceIsVisible()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks([PhysicalDisk("physical", "Model A", "SER-1", 1000, ("ReliabilityPowerOnHours", "100"))]);
        True(disks[0].Source.Contains("MSFT_StorageReliabilityCounter", StringComparison.Ordinal), "reliability source summary");
        Equal("MSFT_StorageReliabilityCounter", disks[0].PowerOnHours?.Source, "reliability metric source");
    }

    private static void DiskPreferredSelectionUsesSystemDisk()
    {
        DiskDevice first = new() { Id = "first", Name = "First" };
        DiskDevice system = new() { Id = "system", Name = "System", IsSystemDisk = true };
        DiskDevice selected = NotNull(new DiskDeviceService().SelectPreferredDisk([first, system], preferredDiskId: null), "system disk selection");
        Equal("system", selected.Id, "system disk selected");
    }

    private static void DiskReliabilityMatchesExactDeviceId()
    {
        Dictionary<string, string?> matching = new()
        {
            ["ReliabilityDeviceId"] = "7",
            ["ReliabilityTemperature"] = "45"
        };
        Dictionary<string, string?> other = new()
        {
            ["ReliabilityDeviceId"] = "8",
            ["ReliabilityTemperature"] = "55"
        };
        Dictionary<string, string?> selected = NotNull(
            HardwareInfoService.FindReliabilityCounter([other, matching], "7", objectId: null),
            "exact reliability DeviceId");
        Equal("45", selected["ReliabilityTemperature"], "matched reliability counter");
        Null(
            HardwareInfoService.FindReliabilityCounter([matching, new Dictionary<string, string?>(matching)], "7", objectId: null),
            "ambiguous reliability counters");
    }

    private static void DiskTotalNamesAreRecognized()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            [Win32Disk("win", "Model A", "SER-1", 1000, "0")],
            [
                DiskSensor("Model A", "Data Read", SensorType.Data, 12, "/hdd/0/data/0", "GB"),
                DiskSensor("Model A", "Data Written", SensorType.Data, 34, "/hdd/0/data/1", "GB")
            ]);
        NotNull(disks[0].ReadTotal, "Data Read sensor");
        NotNull(disks[0].WriteTotal, "Data Written sensor");
    }

    private static void DiskKnownDataUnitsNormalizeToBytes()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            Array.Empty<HardwareDevice>(),
            [
                DiskSensor("Model A", "Total Host Reads", SensorType.Data, 2, "/hdd/0/data/0", "GB"),
                DiskSensor("Model A", "Total Host Writes", SensorType.Data, 3, "/hdd/0/data/1", "TB")
            ]);
        NearlyEqual(2d * 1024d * 1024d * 1024d, disks[0].ReadTotal?.Value, "GB to bytes");
        NearlyEqual(3d * 1024d * 1024d * 1024d * 1024d, disks[0].WriteTotal?.Value, "TB to bytes");
        Equal("B", disks[0].ReadTotal?.Unit, "normalized read unit");
        Equal("B", disks[0].WriteTotal?.Unit, "normalized write unit");
    }

    private static void DiskUnknownDataUnitsRemainUnchanged()
    {
        IReadOnlyList<DiskDevice> disks = BuildDisks(
            Array.Empty<HardwareDevice>(),
            [DiskSensor("Model A", "Data Units Read", SensorType.Data, 99, "/hdd/0/data/0", "NVMe Data Units")]);
        NearlyEqual(99, disks[0].ReadTotal?.Value, "unknown unit value");
        Equal("NVMe Data Units", disks[0].ReadTotal?.Unit, "unknown unit preserved");
    }

    private static void DiskLifetimeMetricsAreDefaultVisible()
    {
        DiskDevice disk = new() { Id = "disk", Name = "Disk" };
        Dictionary<string, HardwareMetric> metrics = DiskViewModel.BuildDeviceMetrics(disk).ToDictionary(metric => metric.Id);
        string[] expectedVisible =
        [
            "disk.health.remaining",
            "disk.read.total",
            "disk.write.total",
            "disk.power.on.hours",
            "disk.power.cycle.count"
        ];
        foreach (string id in expectedVisible)
        {
            True(metrics[id].IsVisible, $"{id} default visibility");
            True(metrics[id].ShowWhenUnavailable, $"{id} placeholder visibility");
        }
        False(metrics["disk.serial"].IsVisible, "serial remains hidden");
    }

    private static void LimitReasonsMergeWithinOneCpuPoll()
    {
        IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> reasons =
            GamePerformanceLimitTracker.SelectActiveReasons([
                LimitReading(SensorCategory.Cpu, "P-core 5 Thermal Throttling", 1),
                LimitReading(SensorCategory.Cpu, "IA: Electrical Design Point (EDP) Throttling", 1),
                LimitReading(SensorCategory.Cpu, "P-core 5 Thermal Throttling", 1)
            ]);
        IReadOnlyList<string> cpu = reasons[PerformanceLimitProcessorType.Cpu];
        Equal(2, cpu.Count, "merged distinct CPU reasons");
        True(cpu.Contains("P-core 5 Thermal Throttling"), "thermal reason retained");
        True(cpu.Contains("IA: Electrical Design Point (EDP) Throttling"), "EDP reason retained");
    }

    private static void LimitEventsSeparateCpuAndGpu()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading[] readings = [
            LimitReading(SensorCategory.Cpu, "Package/Ring Thermal Throttling", 1),
            LimitReading(SensorCategory.Gpu, "GPU Performance Limit - Power", 1)
        ];
        tracker.RecordReadings(id, generation, readings, 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, readings, 500, DateTimeOffset.Now.AddMilliseconds(500));
        Equal(2, tracker.CurrentSnapshot.Events.Count, "CPU and GPU event count");
        True(tracker.CurrentSnapshot.Events.Any(item => item.ProcessorType == PerformanceLimitProcessorType.Cpu), "CPU event");
        True(tracker.CurrentSnapshot.Events.Any(item => item.ProcessorType == PerformanceLimitProcessorType.Gpu), "GPU event");
    }

    private static void LimitDetectionIgnoresNoise()
    {
        SensorReading unavailable = LimitReading(SensorCategory.Cpu, "Thermal Throttling", 1);
        unavailable.IsAvailable = false;
        IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> reasons =
            GamePerformanceLimitTracker.SelectActiveReasons([
                Reading(400, SensorCategory.Cpu, SensorType.Clock, "Core 0"),
                Reading(99, SensorCategory.Cpu, SensorType.Temperature, "CPU Package"),
                LimitReading(SensorCategory.Cpu, "Thermal Throttling", 0),
                new SensorReading
                {
                    Category = SensorCategory.Gpu,
                    Type = SensorType.Power,
                    SensorName = "GPU Power Limit",
                    Value = 300,
                    Unit = "W",
                    IsAvailable = true
                },
                unavailable
            ]);
        Equal(0, reasons.Count, "no inferred or configured reasons");
    }

    private static void LimitEventDurationExtends()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading thermal = LimitReading(SensorCategory.Cpu, "Thermal Throttling", 1);
        tracker.RecordReadings(id, generation, [thermal], 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [thermal], 1500, DateTimeOffset.Now.AddSeconds(1.5));
        Equal(1, tracker.CurrentSnapshot.Events.Count, "same reason event count");
        NearlyEqual(1.5, tracker.CurrentSnapshot.Events[0].Duration.TotalSeconds, "extended duration");
        True(tracker.CurrentSnapshot.Events[0].IsActive, "event remains active");
    }

    private static void LimitReasonChangeCreatesNewEvent()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        DateTimeOffset now = DateTimeOffset.Now;
        SensorReading thermal = LimitReading(SensorCategory.Cpu, "Thermal Throttling", 1);
        SensorReading current = LimitReading(SensorCategory.Cpu, "Current Limit Exceeded", 1);
        tracker.RecordReadings(id, generation, [thermal], 0, now);
        tracker.RecordReadings(id, generation, [thermal], 500, now.AddMilliseconds(500));
        tracker.RecordReadings(id, generation, [current], 1000, now.AddSeconds(1));
        tracker.RecordReadings(id, generation, [current], 1500, now.AddSeconds(1.5));
        Equal(2, tracker.CurrentSnapshot.Events.Count, "changed reason event count");
        True(tracker.CurrentSnapshot.Events[0].IsActive, "newest event active");
        False(tracker.CurrentSnapshot.Events[1].IsActive, "previous event finalized");
        NearlyEqual(1, tracker.CurrentSnapshot.Events[1].Duration.TotalSeconds, "previous event duration");
    }

    private static void LimitClearFinalizesEvent()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        DateTimeOffset now = DateTimeOffset.Now;
        SensorReading thermal = LimitReading(SensorCategory.Gpu, "GPU Performance Limit - Thermal", 1);
        SensorReading normal = NormalLimitReading(SensorCategory.Gpu);
        tracker.RecordReadings(id, generation, [thermal], 0, now);
        tracker.RecordReadings(id, generation, [thermal], 500, now.AddMilliseconds(500));
        tracker.RecordReadings(id, generation, [normal], 1000, now.AddSeconds(1));
        tracker.RecordReadings(id, generation, [normal], 1500, now.AddSeconds(1.5));
        tracker.RecordReadings(id, generation, [normal], 2000, now.AddSeconds(2));
        Equal(1, tracker.CurrentSnapshot.Events.Count, "final event count");
        False(tracker.CurrentSnapshot.Events[0].IsActive, "event finalized");
        NearlyEqual(2, tracker.CurrentSnapshot.Events[0].Duration.TotalSeconds, "final duration");
    }

    private static void LimitTrackerRejectsForeignGeneration()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading reason = LimitReading(SensorCategory.Cpu, "Thermal Throttling", 1);
        tracker.RecordReadings(id, generation + 1, [reason], 0, DateTimeOffset.Now);
        tracker.RecordReadings(Guid.NewGuid(), generation, [reason], 0, DateTimeOffset.Now);
        Equal(0, tracker.CurrentSnapshot.Events.Count, "foreign events rejected");
    }

    private static void LimitTrackerResetsForNewSession()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        tracker.RecordReadings(id, generation, [LimitReading(SensorCategory.Cpu, "Thermal Throttling", 1)], 0, DateTimeOffset.Now);
        Guid next = Guid.NewGuid();
        tracker.StartSession(new GameSessionStartInfo { CaptureSessionId = next, Generation = 2, ProcessId = 2, ProcessName = "next" });
        Equal(next, tracker.CurrentSnapshot.CaptureSessionId, "new limit session id");
        Equal(0, tracker.CurrentSnapshot.Events.Count, "new limit session is empty");
    }

    private static void LimitTrackerCompletionFreezesEvents()
    {
        long clock = 0;
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => clock);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading reason = LimitReading(SensorCategory.Gpu, "GPU Performance Limit - Power", 1);
        tracker.RecordReadings(id, generation, [reason], 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [reason], 500, DateTimeOffset.Now.AddMilliseconds(500));
        clock = 1000;
        GamePerformanceLimitSnapshot completed = NotNull(tracker.CompleteSession(id, generation), "completed limit snapshot");
        tracker.RecordReadings(id, generation, [reason], 2000, DateTimeOffset.Now.AddSeconds(2));
        False(completed.IsTracking, "limit tracking completed");
        False(completed.Events[0].IsActive, "active event finalized on completion");
        NearlyEqual(1, tracker.CurrentSnapshot.Events[0].Duration.TotalSeconds, "completed limit event frozen");
    }

    private static void LimitEventHistoryIsBounded()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0, capacity: 3);
        (Guid id, int generation) = StartLimitSession(tracker);
        for (int index = 0; index < 5; index++)
        {
            long start = index * 4000L;
            SensorReading reason = LimitReading(SensorCategory.Cpu, $"P-core {index} Thermal Throttling", 1);
            SensorReading normal = NormalLimitReading(SensorCategory.Cpu);
            tracker.RecordReadings(id, generation, [reason], start, DateTimeOffset.Now);
            tracker.RecordReadings(id, generation, [reason], start + 500, DateTimeOffset.Now);
            tracker.RecordReadings(id, generation, [normal], start + 1000, DateTimeOffset.Now);
            tracker.RecordReadings(id, generation, [normal], start + 1500, DateTimeOffset.Now);
            tracker.RecordReadings(id, generation, [normal], start + 2000, DateTimeOffset.Now);
        }

        Equal(3, tracker.CurrentSnapshot.Events.Count, "bounded limit event count");
        True(tracker.CurrentSnapshot.EventsTruncated, "bounded limit event reports truncation");
        Equal(5L, tracker.CurrentSnapshot.Events[0].EventId, "newest limit event retained");
        Equal(3L, tracker.CurrentSnapshot.Events[2].EventId, "oldest retained limit event");
    }

    private static void LimitSingleSampleSpikeIsIgnored()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        tracker.RecordReadings(id, generation, [LimitReading(SensorCategory.Cpu, "CPU Thermal Throttling", 1)], 0, DateTimeOffset.Now);
        SensorReading normal = NormalLimitReading(SensorCategory.Cpu);
        tracker.RecordReadings(id, generation, [normal], 500, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 1000, DateTimeOffset.Now);
        Equal(0, tracker.CurrentSnapshot.Events.Count, "single spike event count");
    }

    private static void LimitClearRequiresConfirmation()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading reason = LimitReading(SensorCategory.Cpu, "CPU Power Limit", 1);
        SensorReading normal = NormalLimitReading(SensorCategory.Cpu);
        tracker.RecordReadings(id, generation, [reason], 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [reason], 500, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 1000, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 1500, DateTimeOffset.Now);
        True(tracker.CurrentSnapshot.Events[0].IsActive, "two clears retain active event");
        tracker.RecordReadings(id, generation, [normal], 2000, DateTimeOffset.Now);
        False(tracker.CurrentSnapshot.Events[0].IsActive, "third clear finalizes event");
    }

    private static void LimitTemporaryFailurePreservesActiveEvent()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading reason = LimitReading(SensorCategory.Gpu, "GPU Performance Limit - Thermal", 1);
        tracker.RecordReadings(id, generation, [reason], 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [reason], 500, DateTimeOffset.Now);
        SensorReading unavailable = TemporaryLimitReading(SensorCategory.Gpu);
        tracker.RecordReadings(id, generation, [unavailable], 1000, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [unavailable], 3000, DateTimeOffset.Now);
        True(tracker.CurrentSnapshot.Events[0].IsActive, "temporary provider failure keeps event active");
        Equal(PerformanceLimitSupportStatus.TemporarilyUnavailable, tracker.CurrentSnapshot.GpuSupportStatus, "temporary GPU status");
    }

    private static void LimitMatchingEventMergesWithinFiveSeconds()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        SensorReading reason = LimitReading(SensorCategory.Cpu, "CPU Current Limit", 1);
        SensorReading normal = NormalLimitReading(SensorCategory.Cpu);
        tracker.RecordReadings(id, generation, [reason], 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [reason], 500, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 1000, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 1500, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 2000, DateTimeOffset.Now);
        long eventId = tracker.CurrentSnapshot.Events[0].EventId;
        tracker.RecordReadings(id, generation, [reason], 4000, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [reason], 4500, DateTimeOffset.Now);
        Equal(1, tracker.CurrentSnapshot.Events.Count, "merged event count");
        Equal(eventId, tracker.CurrentSnapshot.Events[0].EventId, "merged event id");
        True(tracker.CurrentSnapshot.Events[0].IsActive, "merged event active");
    }

    private static void LimitUnchangedStateSuppressesSnapshots()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        int snapshots = 0;
        tracker.SnapshotChanged += (_, _) => snapshots++;
        SensorReading normal = NormalLimitReading(SensorCategory.Cpu);
        tracker.RecordReadings(id, generation, [normal], 0, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [normal], 500, DateTimeOffset.Now);
        Equal(1, snapshots, "unchanged normal snapshot count");
        SensorReading reason = LimitReading(SensorCategory.Cpu, "CPU Thermal Throttling", 1);
        tracker.RecordReadings(id, generation, [reason], 1000, DateTimeOffset.Now);
        tracker.RecordReadings(id, generation, [reason], 1500, DateTimeOffset.Now);
        int afterStart = snapshots;
        tracker.RecordReadings(id, generation, [reason], 1750, DateTimeOffset.Now);
        Equal(afterStart, snapshots, "sub-second unchanged active snapshot count");
    }

    private static void LimitSupportStatesRemainDistinct()
    {
        using GamePerformanceLimitTracker tracker = NewLimitTracker(() => 0);
        (Guid id, int generation) = StartLimitSession(tracker);
        Equal(PerformanceLimitSupportStatus.NotStarted, tracker.CurrentSnapshot.CpuSupportStatus, "initial CPU status");
        tracker.RecordReadings(id, generation, [], 0, DateTimeOffset.Now);
        Equal(PerformanceLimitSupportStatus.Unsupported, tracker.CurrentSnapshot.CpuSupportStatus, "unsupported CPU status");
        tracker.RecordReadings(id, generation, [
            NormalLimitReading(SensorCategory.Cpu),
            TemporaryLimitReading(SensorCategory.Gpu)
        ], 500, DateTimeOffset.Now);
        Equal(PerformanceLimitSupportStatus.SupportedNormal, tracker.CurrentSnapshot.CpuSupportStatus, "normal CPU status");
        Equal(PerformanceLimitSupportStatus.TemporarilyUnavailable, tracker.CurrentSnapshot.GpuSupportStatus, "temporary GPU status");
    }

    private static void NvidiaNormalReasonsAreNotAnomalies()
    {
        IReadOnlyList<SensorReading> readings = NvidiaPerformanceLimitSensorProvider.CreateReadings(
            "GPU",
            0,
            0x001 | 0x002 | 0x010 | 0x100,
            DateTimeOffset.Now);
        IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> reasons =
            GamePerformanceLimitTracker.SelectActiveReasons(readings);
        Equal(0, reasons.Count, "normal NVML reason count");
    }

    private static void LimitStatusTextIsLocalized()
    {
        Equal("尚未采集", GamePerformanceViewModel.FormatSupportStatus(PerformanceLimitSupportStatus.NotStarted), "not started text");
        Equal("正常", GamePerformanceViewModel.FormatSupportStatus(PerformanceLimitSupportStatus.SupportedNormal), "normal text");
        Equal("不支持", GamePerformanceViewModel.FormatSupportStatus(PerformanceLimitSupportStatus.Unsupported), "unsupported text");
        Equal("暂时不可用", GamePerformanceViewModel.FormatSupportStatus(PerformanceLimitSupportStatus.TemporarilyUnavailable), "temporary text");
    }

    private static void LegacySummaryLimitsAreUnrecorded()
    {
        GameSessionSummary summary = NotNull(
            JsonSerializer.Deserialize<GameSessionSummary>("{\"ProcessName\":\"legacy\"}"),
            "legacy limit summary");
        Null(summary.PerformanceLimitEvents, "legacy limit events");
        GameSessionRecordInfo record = new() { GameName = "legacy" };
        Equal("未记录限制状态", record.PerformanceLimitText, "legacy limit text");
        GameSessionRecordInfo normal = new()
        {
            CpuPerformanceLimitEventCount = 0,
            GpuPerformanceLimitEventCount = 0,
            CpuPerformanceLimitSupportStatus = PerformanceLimitSupportStatus.SupportedNormal,
            GpuPerformanceLimitSupportStatus = PerformanceLimitSupportStatus.Unsupported
        };
        Equal("无限制事件", normal.PerformanceLimitText, "no limit event text");
    }

    private static void RecorderSummaryIncludesPerformanceLimits()
    {
        RunInTempDirectory(async directory =>
        {
            long clock = 0;
            using GamePerformanceLimitTracker tracker = NewLimitTracker(() => clock);
            Guid id = Guid.NewGuid();
            const int generation = 9;
            GameSessionStartInfo startInfo = new()
            {
                CaptureSessionId = id,
                Generation = generation,
                ProcessId = 9,
                ProcessName = "limit-game",
                CaptureStartedAt = DateTimeOffset.Now
            };
            tracker.StartSession(startInfo);
            SensorReading reason = LimitReading(SensorCategory.Cpu, "CPU Thermal Throttling", 1);
            tracker.RecordReadings(id, generation, [reason], 0, DateTimeOffset.Now);
            tracker.RecordReadings(id, generation, [reason], 500, DateTimeOffset.Now);
            clock = 1000;
            tracker.CompleteSession(id, generation);

            await using CsvGameSessionRecorder recorder = new(
                directory,
                8,
                energyTracker: null,
                performanceLimitTracker: tracker);
            await recorder.StartAsync(startInfo);
            True(recorder.TryRecord(Sample(id, 10), id, generation), "limit summary sample accepted");
            GameSessionRecordInfo record = NotNull(
                await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true),
                "limit summary record");
            await using FileStream stream = File.OpenRead(NotNull(record.SummaryPath, "limit summary path"));
            JsonSerializerOptions options = new();
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            GameSessionSummary summary = NotNull(
                await JsonSerializer.DeserializeAsync<GameSessionSummary>(stream, options),
                "limit summary JSON");
            Equal(1, summary.CpuPerformanceLimitEventCount, "summary CPU event count");
            Equal(0, summary.GpuPerformanceLimitEventCount, "summary GPU event count");
            Equal(1, summary.PerformanceLimitEvents?.Count ?? 0, "summary event list");
            Equal(PerformanceLimitSupportStatus.ActiveLimit, summary.CpuPerformanceLimitSupportStatus, "summary CPU support");
        });
    }

    private static void WindowsCpuLimitFlagsExpand()
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        IReadOnlyList<SensorReading> readings = WindowsCpuPerformanceLimitSensorProvider.CreateReadings(0x5, 80, timestamp);
        Equal(2, readings.Count, "CPU flag reading count");
        Equal("CPU Performance Limit Flag 0x00000001", readings[0].SensorName, "first CPU raw flag");
        Equal("CPU Performance Limit Flag 0x00000004", readings[1].SensorName, "second CPU raw flag");
        True(readings.All(item => item.IsAvailable && item.Unit == "state"), "CPU flags are explicit states");
        Equal(0, WindowsCpuPerformanceLimitSensorProvider.CreateReadings(0, 100, timestamp).Count, "zero CPU flags");
    }

    private static void NvidiaLimitReasonsMap()
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        IReadOnlyList<SensorReading> readings = NvidiaPerformanceLimitSensorProvider.CreateReadings("GPU", 0, 0x64, timestamp);
        Equal(3, readings.Count, "NVML reason count");
        True(readings.Any(item => item.SensorName.Contains("Software Power Cap", StringComparison.Ordinal)), "NVML power reason");
        True(readings.Any(item => item.SensorName.Contains("Software Thermal", StringComparison.Ordinal)), "NVML software thermal reason");
        True(readings.Any(item => item.SensorName.Contains("Hardware Thermal", StringComparison.Ordinal)), "NVML hardware thermal reason");
        IReadOnlyDictionary<PerformanceLimitProcessorType, IReadOnlyList<string>> selected =
            GamePerformanceLimitTracker.SelectActiveReasons(readings);
        Equal(3, selected[PerformanceLimitProcessorType.Gpu].Count, "NVML reasons reach tracker");
        Equal(0, NvidiaPerformanceLimitSensorProvider.CreateReadings("GPU", 0, 0, timestamp).Count, "zero NVML reasons");
    }

    private static void DiskMissingCoreValuesDisplayPlaceholders()
    {
        HardwareMetric metric = DiskViewModel.BuildDeviceMetrics(new DiskDevice { Id = "disk", Name = "Disk" })
            .Single(item => item.Id == "disk.read.total");
        DetailMetricViewModel viewModel = new();
        viewModel.Update(metric);
        True(viewModel.IsVisible, "missing read total remains visible");
        Equal("--", viewModel.Value, "missing read total placeholder");
    }

    private static void DiskStorageWmiCacheIsLowFrequency()
    {
        TimeSpan duration = HardwareInfoService.StorageWmiCacheDurationForTests;
        True(duration >= TimeSpan.FromSeconds(30), "Storage WMI cache lower bound");
        True(duration <= TimeSpan.FromSeconds(60), "Storage WMI cache upper bound");
    }

    private static GameEnergyTracker NewEnergyTracker(Func<long> clock)
    {
        return new GameEnergyTracker(
            pollingService: null,
            getTimestamp: clock,
            timestampFrequency: 1000,
            foregroundMaximumGap: TimeSpan.FromSeconds(3),
            backgroundMaximumGap: TimeSpan.FromSeconds(20));
    }

    private static (Guid Id, int Generation) StartEnergySession(GameEnergyTracker tracker)
    {
        Guid id = Guid.NewGuid();
        const int generation = 1;
        tracker.StartSession(new GameSessionStartInfo
        {
            CaptureSessionId = id,
            Generation = generation,
            ProcessId = 1,
            ProcessName = "game"
        });
        return (id, generation);
    }

    private static GamePerformanceLimitTracker NewLimitTracker(Func<long> clock, int capacity = 200)
    {
        return new GamePerformanceLimitTracker(
            pollingService: null,
            getTimestamp: clock,
            timestampFrequency: 1000,
            capacity: capacity);
    }

    private static (Guid Id, int Generation) StartLimitSession(GamePerformanceLimitTracker tracker)
    {
        Guid id = Guid.NewGuid();
        const int generation = 1;
        tracker.StartSession(new GameSessionStartInfo
        {
            CaptureSessionId = id,
            Generation = generation,
            ProcessId = 1,
            ProcessName = "game"
        });
        return (id, generation);
    }

    private static SensorReading LimitReading(SensorCategory category, string sensorName, double value)
    {
        return new SensorReading
        {
            Category = category,
            Type = SensorType.State,
            DeviceName = category == SensorCategory.Cpu ? "CPU" : "GPU",
            SensorName = sensorName,
            Value = value,
            Unit = string.Empty,
            IsAvailable = true,
            Availability = SensorAvailability.Available,
            Source = "test"
        };
    }

    private static SensorReading NormalLimitReading(SensorCategory category)
    {
        return new SensorReading
        {
            Category = category,
            Type = SensorType.State,
            DeviceName = category == SensorCategory.Cpu ? "CPU" : "GPU",
            SensorName = category == SensorCategory.Cpu
                ? "CPU Performance Limit Status"
                : "GPU Performance Limit Status",
            Value = 0,
            Unit = "state",
            IsAvailable = true,
            Availability = SensorAvailability.Available,
            RawIdentifier = category == SensorCategory.Cpu
                ? "/performance-limit-status/cpu/test"
                : "/performance-limit-status/gpu/test",
            Source = "test"
        };
    }

    private static SensorReading TemporaryLimitReading(SensorCategory category)
    {
        SensorReading reading = NormalLimitReading(category);
        reading.Value = null;
        reading.IsAvailable = false;
        reading.Availability = SensorAvailability.Error;
        return reading;
    }

    private static SensorReading PowerReading(
        SensorCategory category,
        string sensorName,
        double value,
        string rawIdentifier,
        string? deviceName = null)
    {
        return new SensorReading
        {
            Category = category,
            Type = SensorType.Power,
            DeviceName = deviceName ?? (category == SensorCategory.Cpu ? "CPU" : "GPU"),
            SensorName = sensorName,
            Value = value,
            Unit = "W",
            IsAvailable = true,
            Availability = SensorAvailability.Available,
            RawIdentifier = rawIdentifier,
            Source = "test"
        };
    }

    private static IReadOnlyList<DiskDevice> BuildDisks(
        IEnumerable<HardwareDevice> devices,
        IEnumerable<SensorReading>? readings = null,
        IEnumerable<DiskPerformanceSnapshot>? performance = null)
    {
        HardwareSnapshot snapshot = new();
        snapshot.Devices.AddRange(devices);
        return new DiskDeviceService().BuildDiskDevices(
            snapshot,
            readings ?? Array.Empty<SensorReading>(),
            performance ?? Array.Empty<DiskPerformanceSnapshot>());
    }

    private static HardwareDevice Win32Disk(string id, string model, string? serial, ulong size, string index)
    {
        return new HardwareDevice
        {
            Id = id,
            Name = model,
            Model = model,
            Category = SensorCategory.Disk,
            Properties = new Dictionary<string, string?>
            {
                ["StorageSource"] = "Win32_DiskDrive",
                ["DeviceID"] = id,
                ["Index"] = index,
                ["SerialNumber"] = serial,
                ["SizeBytes"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        };
    }

    private static HardwareDevice PhysicalDisk(
        string id,
        string model,
        string? serial,
        ulong size,
        params (string Key, string? Value)[] additionalProperties)
    {
        Dictionary<string, string?> properties = new()
        {
            ["StorageSource"] = "MSFT_PhysicalDisk",
            ["DeviceId"] = id,
            ["UniqueId"] = id,
            ["SerialNumber"] = serial,
            ["SizeBytes"] = size.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        foreach ((string key, string? value) in additionalProperties)
        {
            properties[key] = value;
        }
        return new HardwareDevice
        {
            Id = id,
            Name = model,
            Model = model,
            Category = SensorCategory.Disk,
            Properties = properties
        };
    }

    private static SensorReading DiskSensor(string deviceName, string sensorName, SensorType type, double value, string rawIdentifier, string? unit = null)
    {
        return new SensorReading
        {
            DeviceName = deviceName,
            SensorName = sensorName,
            Category = SensorCategory.Disk,
            Type = type,
            Value = value,
            Unit = unit ?? (type == SensorType.Temperature ? "C" : string.Empty),
            IsAvailable = true,
            Availability = SensorAvailability.Available,
            RawIdentifier = rawIdentifier,
            Source = "LibreHardwareMonitor"
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

    private static int RunPresentMonBenchmark()
    {
        const int targetProcessId = 4242;
        const int rowCount = 100_000;
        const int targetEvery = 20;
        const string v2Header = "Application,ProcessID,SwapChainAddress,PresentRuntime,PresentMode,FrameType,FrameTime,CPUBusy,CPUWait,GPULatency,GPUTime,GPUBusy,GPUWait,DisplayLatency,DisplayedTime,ClickToPhotonLatency";
        const string legacyHeader = "Application,ProcessID,SwapChainAddress,Runtime,PresentMode,FrameType,MsBetweenPresents,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,MsUntilDisplayed,DisplayedTime,MsClickToPhotonLatency";
        string[] rows = new string[rowCount];
        double[] frameTimes = [16.667, 8.333, 4.167, 2.000];
        int targetRows = 0;
        for (int index = 0; index < rows.Length; index++)
        {
            bool target = index % targetEvery == 0;
            int processId = target ? targetProcessId : 10_000 + index % 37;
            string application = target ? "target.exe" : $"\"background,{index % 7}.exe\"";
            double frameTime = frameTimes[index % frameTimes.Length];
            rows[index] = $"{application},{processId},0x{index:X},DXGI,\"Hardware, Flip\",Application,{frameTime.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},2.1,NA,1.2,3.1,2.9,N/A,5.4,6.2,NA";
            if (target) targetRows++;
        }

        PresentMonCsvParser warmup = new(Guid.NewGuid(), targetProcessId, "target.exe", targetProcessId);
        warmup.ParseLine(v2Header);
        for (int index = 0; index < 2_000; index++) _ = warmup.ParseLine(rows[index], DateTimeOffset.UnixEpoch);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        PresentMonCsvParser parser = new(Guid.NewGuid(), targetProcessId, "target.exe", targetProcessId);
        parser.ParseLine(v2Header);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        int acceptedSamples = 0;
        int filteredRows = 0;
        for (int index = 0; index < rows.Length; index++)
        {
            if (index == rows.Length / 2) parser.ParseLine(legacyHeader);
            PresentMonCsvParseResult result = parser.ParseLine(rows[index], DateTimeOffset.UnixEpoch);
            if (result.Kind == PresentMonCsvParseKind.Sample) acceptedSamples++;
            else if (result.Kind == PresentMonCsvParseKind.Filtered) filteredRows++;
        }

        stopwatch.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        const int retainedSampleCount = 60_000;
        string[] retainedRows = new string[retainedSampleCount];
        for (int index = 0; index < retainedRows.Length; index++)
        {
            retainedRows[index] = $"target.exe,{targetProcessId},0x{index:X},DXGI,Independent Flip,Application,4.167,2.1,NA,1.2,3.1,2.9,NA,5.4,6.2,N/A";
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        PresentMonCsvParser retainedParser = new(Guid.NewGuid(), targetProcessId, "target.exe", targetProcessId);
        retainedParser.ParseLine(v2Header);
        GameFrameSample?[] retainedSamples = new GameFrameSample[retainedSampleCount];
        for (int index = 0; index < retainedRows.Length; index++)
        {
            retainedSamples[index] = retainedParser.ParseLine(retainedRows[index], DateTimeOffset.UnixEpoch).Sample;
        }

        long retainedBytes = Math.Max(0L, GC.GetTotalMemory(forceFullCollection: true) - memoryBefore);
        GC.KeepAlive(retainedSamples);

        Console.WriteLine($"rows={rowCount}");
        Console.WriteLine($"targetRows={targetRows}");
        Console.WriteLine($"filteredRows={filteredRows}");
        Console.WriteLine($"acceptedSamples={acceptedSamples}");
        Console.WriteLine($"nonTargetSamplesCreated={parser.SampleCreationCount - acceptedSamples}");
        Console.WriteLine($"numericFieldsParsed={parser.NumericFieldParseCount}");
        Console.WriteLine($"elapsedMs={stopwatch.Elapsed.TotalMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"rowsPerSecond={(rowCount / stopwatch.Elapsed.TotalSeconds).ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"allocatedBytes={allocatedBytes}");
        Console.WriteLine($"allocatedBytesPerRow={(allocatedBytes / (double)rowCount).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"allocatedBytesPerTargetSample={(allocatedBytes / (double)targetRows).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
        Console.WriteLine($"retainedBytesFor60000StructuredSamples={retainedBytes}");
        Console.WriteLine("retainedRawLineBytesFor60000=0");
        return acceptedSamples == targetRows && filteredRows == rowCount - targetRows ? 0 : 1;
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
        int processIdIndex = args
            .Select((value, index) => new { Value = value, Index = index })
            .FirstOrDefault(item => item.Value.Equals("--process_id", StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;
        int sessionNameIndex = args
            .Select((value, index) => new { Value = value, Index = index })
            .FirstOrDefault(item => item.Value.Equals("--session_name", StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;
        int targetProcessId = processIdIndex >= 0
            && processIdIndex + 1 < args.Count
            && int.TryParse(args[processIdIndex + 1], out int filteredProcessId)
                ? filteredProcessId
                : sessionNameIndex >= 0 && sessionNameIndex + 1 < args.Count
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

    private sealed class FakeSensorService(TimeSpan delay) : ISensorService
    {
        private int concurrentReads;
        private int maximumConcurrentReads;
        private int readCount;

        public int MaximumConcurrentReads => Volatile.Read(ref maximumConcurrentReads);

        public int ReadCount => Volatile.Read(ref readCount);

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(CancellationToken cancellationToken = default)
        {
            int current = Interlocked.Increment(ref concurrentReads);
            int observed;
            do
            {
                observed = Volatile.Read(ref maximumConcurrentReads);
                if (current <= observed) break;
            }
            while (Interlocked.CompareExchange(ref maximumConcurrentReads, current, observed) != observed);

            try
            {
                Interlocked.Increment(ref readCount);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
                return [];
            }
            finally
            {
                Interlocked.Decrement(ref concurrentReads);
            }
        }

        public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken = default) =>
            GetCurrentReadingsAsync(cancellationToken);

        public void Dispose()
        {
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
        private EventHandler<GameFrameSample>? frameReceived;

        public FakeGamePerformanceService(IReadOnlyList<GameProcessInfo> candidates)
        {
            this.candidates = candidates;
        }

        public event EventHandler<GameFrameSample>? FrameReceived
        {
            add => frameReceived += value;
            remove => frameReceived -= value;
        }

        public int FrameSubscriberCount => frameReceived?.GetInvocationList().Length ?? 0;

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
