using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class CompressedSessionRecorderTests
{
    public static IEnumerable<(string Name, Action Test)> GetTests()
    {
        yield return ("Compressed recorder 01 default writes csv.gz", TestSupport.Run(DefaultWritesGZipAsync));
        yield return ("Compressed recorder 02 plain mode writes csv", TestSupport.Run(PlainModeWritesCsvAsync));
        yield return ("Compressed recorder 03 gzip round trips exact CSV", TestSupport.Run(GZipMatchesPlainAsync));
        yield return ("Compressed recorder 04 summary references compressed file", TestSupport.Run(SummaryReferencesCompressedFileAsync));
        yield return ("Compressed recorder 05 report reads gzip stream", TestSupport.Run(ReportReadsGZipAsync));
        yield return ("Compressed recorder 06 truncated gzip preserves rows", TestSupport.Run(TruncatedGZipPreservesRowsAsync));
        yield return ("Compressed recorder 07 export creates plain CSV", TestSupport.Run(ExportPlainCsvAsync));
        yield return ("Compressed recorder 08 export cancellation removes partial", TestSupport.Run(ExportCancellationCleansPartialAsync));
        yield return ("Compressed recorder 09 empty session leaves no gzip", TestSupport.Run(EmptySessionLeavesNoFileAsync));
        yield return ("Compressed recorder 10 storage mode is captured per session", TestSupport.Run(StorageModeCapturedPerSessionAsync));
        yield return ("Compressed recorder 11 gzip partial recovers as incomplete", TestSupport.Run(GZipPartialRecoveryAsync));
        yield return ("Compressed recorder 12 compressed path traversal is rejected", CompressedTraversalIsRejected);
        yield return ("Compressed recorder 13 caller cancellation preserves gzip footer", TestSupport.Run(CancellationPreservesFooterAsync));
        yield return ("Compressed recorder 14 quality diagnostics enter summary", TestSupport.Run(QualityDiagnosticsEnterSummaryAsync));
        yield return ("Compressed recorder 15 truncated gzip filters retained outlier", TestSupport.Run(TruncatedGZipFiltersRetainedOutlierAsync));
    }

    private static Task DefaultWritesGZipAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionRecordInfo record = await RecordAsync(recorder, 12);
        TestSupport.True(record.CsvPath.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase), "gzip suffix");
        byte[] signature = new byte[2];
        await using FileStream file = File.OpenRead(record.CsvPath);
        _ = await file.ReadAsync(signature);
        TestSupport.Equal((byte)0x1f, signature[0], "gzip signature 1");
        TestSupport.Equal((byte)0x8b, signature[1], "gzip signature 2");
    });

    private static Task PlainModeWritesCsvAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = Create(directory, GameSessionFrameStorageMode.PlainCsv);
        GameSessionRecordInfo record = await RecordAsync(recorder, 4);
        TestSupport.True(record.CsvPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase), "plain suffix");
        TestSupport.False(record.CsvPath.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase), "not gzip");
    });

    private static Task GZipMatchesPlainAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string plainDirectory = Path.Combine(directory, "plain");
        string gzipDirectory = Path.Combine(directory, "gzip");
        Guid sessionId = Guid.NewGuid();
        GameSessionStartInfo info = TestSupport.StartInfo(sessionId);
        GameFrameSample[] samples =
        [
            new GameFrameSample
            {
                CaptureSessionId = sessionId,
                Timestamp = DateTimeOffset.Parse("2026-07-16T01:02:03+00:00"),
                ProcessId = 42,
                ProcessName = "游戏, \"测试\"",
                FrameTimeMs = 16.667,
                Fps = 59.999,
                CpuBusyMs = 0,
                SwapChainAddress = "0xABC",
                PresentMode = "Mode, Flip"
            }
        ];
        await using CsvGameSessionRecorder plain = Create(plainDirectory, GameSessionFrameStorageMode.PlainCsv);
        await using CsvGameSessionRecorder gzip = Create(gzipDirectory, GameSessionFrameStorageMode.CompressedCsv);
        GameSessionRecordInfo plainRecord = await RecordAsync(plain, info, samples);
        GameSessionRecordInfo gzipRecord = await RecordAsync(gzip, info, samples);
        string plainText = await File.ReadAllTextAsync(plainRecord.CsvPath, Encoding.UTF8);
        using StreamReader reader = GameSessionFrameStreamFactory.Shared.OpenTextReader(gzipRecord.CsvPath);
        string gzipText = await reader.ReadToEndAsync();
        TestSupport.Equal(plainText, gzipText, "lossless CSV text");
    });

    private static Task SummaryReferencesCompressedFileAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionRecordInfo record = await RecordAsync(recorder, 20);
        await using FileStream summaryFile = File.OpenRead(record.SummaryPath!);
        JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter() } };
        GameSessionSummary summary = (await JsonSerializer.DeserializeAsync<GameSessionSummary>(summaryFile, options))!;
        TestSupport.Equal(4, summary.SessionSchemaVersion, "schema v4");
        TestSupport.Equal(Path.GetFileName(record.CsvPath), summary.CsvFileName, "summary frame name");
        TestSupport.Equal("CompressedCsv", summary.FrameStorageFormat, "storage format");
        TestSupport.True(summary.CompressionRatioPercent is > 0d and < 100d, "compression ratio");
    });

    private static Task ReportReadsGZipAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionRecordInfo record = await RecordAsync(recorder, 120);
        GameSessionReport report = await new GameSessionReportService().LoadAsync(record);
        TestSupport.Equal(120L, report.ParsedFrameCount, "parsed gzip frames");
        TestSupport.False(report.FrameCsvIsPartial, "complete gzip");
    });

    private static Task TruncatedGZipPreservesRowsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionRecordInfo record = await RecordAsync(recorder, 240);
        string truncated = Path.Combine(directory, "truncated.csv.gz.incomplete");
        byte[] bytes = await File.ReadAllBytesAsync(record.CsvPath);
        await File.WriteAllBytesAsync(truncated, bytes[..Math.Max(2, bytes.Length - 16)]);
        GameSessionRecordInfo partialRecord = new()
        {
            GameName = record.GameName,
            StartedAt = record.StartedAt,
            Duration = record.Duration,
            CsvPath = truncated,
            IsComplete = false
        };
        GameSessionReport report = await new GameSessionReportService().LoadAsync(partialRecord);
        TestSupport.True(report.ParsedFrameCount > 0, "rows before damaged footer retained");
    });

    private static Task ExportPlainCsvAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionRecordInfo record = await RecordAsync(recorder, 10);
        string export = await GameSessionCsvExportService.ExportPlainCsvAsync(record.CsvPath);
        TestSupport.True(export.EndsWith(".export.csv", StringComparison.OrdinalIgnoreCase), "export suffix");
        string[] lines = await File.ReadAllLinesAsync(export, Encoding.UTF8);
        TestSupport.Equal(11, lines.Length, "header plus rows");
    });

    private static Task ExportCancellationCleansPartialAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionRecordInfo record = await RecordAsync(recorder, 10);
        string target = Path.Combine(directory, "cancel.csv");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            await GameSessionCsvExportService.ExportPlainCsvAsync(record.CsvPath, target, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        TestSupport.False(File.Exists(target + ".partial"), "partial cleaned");
        TestSupport.False(File.Exists(target), "target absent");
    });

    private static Task EmptySessionLeavesNoFileAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        await recorder.StartAsync(TestSupport.StartInfo());
        GameSessionRecordInfo? record = await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
        TestSupport.Equal<GameSessionRecordInfo?>(null, record, "empty record");
        TestSupport.Equal(0, Directory.GetFiles(directory, "*.csv.gz", SearchOption.AllDirectories).Length, "no gzip");
    });

    private static Task StorageModeCapturedPerSessionAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionFrameStorageMode current = GameSessionFrameStorageMode.CompressedCsv;
        await using CsvGameSessionRecorder recorder = new(directory, frameStorageModeProvider: () => current);
        GameSessionStartInfo info = TestSupport.StartInfo();
        await recorder.StartAsync(info);
        current = GameSessionFrameStorageMode.PlainCsv;
        recorder.TryRecord(TestSupport.Frame(info.CaptureSessionId), info.CaptureSessionId, info.Generation);
        GameSessionRecordInfo record = (await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))!;
        TestSupport.True(record.CsvPath.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase), "active session remains compressed");
    });

    private static Task GZipPartialRecoveryAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string month = Path.Combine(directory, "2026-07");
        Directory.CreateDirectory(month);
        string partial = Path.Combine(month, "recovery.csv.gz.partial");
        await using (FileStream file = File.Create(partial))
        await using (GZipStream gzip = new(file, CompressionLevel.Fastest))
        await using (StreamWriter writer = new(gzip, new UTF8Encoding(true)))
        {
            await writer.WriteLineAsync(GameCsvFormatting.Header);
            await writer.WriteLineAsync(GameCsvFormatting.FormatSample(TestSupport.Frame(Guid.NewGuid())));
        }
        await using CsvGameSessionRecorder recorder = new(directory);
        await recorder.RecoverIncompleteSessionsAsync();
        TestSupport.Equal(1, Directory.GetFiles(directory, "*.csv.gz.incomplete", SearchOption.AllDirectories).Length, "recovered gzip");
    });

    private static void CompressedTraversalIsRejected()
    {
        bool resolved = SessionFilePathResolver.TryResolve(
            Path.GetTempPath(),
            "..\\evil.csv.gz.incomplete",
            SessionFileKind.FrameCsv,
            out _,
            out _);
        TestSupport.False(resolved, "traversal rejected");
    }

    private static Task CancellationPreservesFooterAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionStartInfo info = TestSupport.StartInfo();
        await recorder.StartAsync(info);
        for (int index = 0; index < 200; index++)
        {
            recorder.TryRecord(TestSupport.Frame(info.CaptureSessionId, index / 60d), info.CaptureSessionId, info.Generation);
        }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        GameSessionRecordInfo record = (await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))!;
        using StreamReader reader = GameSessionFrameStreamFactory.Shared.OpenTextReader(record.CsvPath);
        int rows = -1;
        while (await reader.ReadLineAsync() is not null) rows++;
        TestSupport.Equal(200, rows, "all rows readable after caller cancellation");
    });

    private static Task QualityDiagnosticsEnterSummaryAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory);
        GameSessionStartInfo info = TestSupport.StartInfo();
        await recorder.StartAsync(info);
        recorder.SetFrameQualityDiagnostics(info.CaptureSessionId, info.Generation, new GameFrameQualityDiagnostics
        {
            WarmupCandidateSampleCount = 12,
            WarmupDiscardedSampleCount = 12,
            NonPrimarySwapChainSampleCount = 3,
            SanitizedMetricFieldCount = 2,
            FrameTimeOutlierSampleCount = 1,
            DuplicateCaptureElapsedSampleCount = 1,
            DisplayLatencySanitizedCount = 2,
            PrimarySwapChainAddress = "0xMAIN",
            CaptureWarmupDurationSeconds = 0.25,
            RawMaximumFps = 10_000d,
            SustainedMaximumFps = 240d
        });
        recorder.TryRecord(TestSupport.Frame(info.CaptureSessionId), info.CaptureSessionId, info.Generation);
        GameSessionRecordInfo record = (await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))!;
        string json = await File.ReadAllTextAsync(record.SummaryPath!);
        TestSupport.True(json.Contains("\"WarmupDiscardedSampleCount\": 12", StringComparison.Ordinal), "warmup summary field");
        TestSupport.True(json.Contains("\"PrimarySwapChainAddress\": \"0xMAIN\"", StringComparison.Ordinal), "primary summary field");
        TestSupport.True(json.Contains("\"FrameTimeOutlierSampleCount\": 1", StringComparison.Ordinal), "v4 outlier field");
        TestSupport.True(json.Contains("\"RawMaximumFps\": 10000", StringComparison.Ordinal), "v4 raw maximum field");
        TestSupport.True(json.Contains("\"SustainedMaximumFps\": 240", StringComparison.Ordinal), "v4 sustained maximum field");
    });

    private static Task TruncatedGZipFiltersRetainedOutlierAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        string complete = Path.Combine(directory, "quality.csv.gz");
        await using (FileStream file = File.Create(complete))
        await using (GZipStream gzip = new(file, CompressionLevel.Fastest))
        await using (StreamWriter writer = new(gzip, new UTF8Encoding(true)))
        {
            await writer.WriteLineAsync(GameCsvFormatting.Header);
            for (int index = 0; index < 50; index++)
            {
                double fps = index == 20 ? 10_000d : 60d;
                await writer.WriteLineAsync(GameCsvFormatting.FormatSample(
                    TestSupport.Frame(Guid.NewGuid(), index / 60d, fps, started.AddSeconds(index / 60d))));
            }
        }
        byte[] bytes = await File.ReadAllBytesAsync(complete);
        string truncated = Path.Combine(directory, "quality.csv.gz.incomplete");
        await File.WriteAllBytesAsync(truncated, bytes[..Math.Max(2, bytes.Length - 12)]);
        GameSessionReport report = await new GameSessionReportService().LoadAsync(new GameSessionRecordInfo
        {
            GameName = "quality",
            StartedAt = started,
            Duration = TimeSpan.FromSeconds(1),
            CsvPath = truncated,
            IsComplete = false
        });
        TestSupport.True(report.AcceptedFrameCount > 0, "valid rows before truncated footer lost");
        TestSupport.True(report.RawMaximumFps > 1000d, "retained raw outlier missing");
        TestSupport.True(report.SustainedMaximumFps is > 59d and < 61d, "truncated report robust maximum polluted");
        TestSupport.True(report.FrameQualityDiagnostics.FrameTimeOutlierSampleCount >= 1L, "truncated outlier diagnostic missing");
    });

    private static CsvGameSessionRecorder Create(string directory, GameSessionFrameStorageMode mode) =>
        new(directory, frameStorageModeProvider: () => mode);

    private static async Task<GameSessionRecordInfo> RecordAsync(CsvGameSessionRecorder recorder, int count)
    {
        GameSessionStartInfo info = TestSupport.StartInfo();
        GameFrameSample[] samples = Enumerable.Range(0, count)
            .Select(index => TestSupport.Frame(info.CaptureSessionId, index / 60d, 60d,
                info.CaptureStartedAt.AddSeconds(index / 60d)))
            .ToArray();
        return await RecordAsync(recorder, info, samples);
    }

    private static async Task<GameSessionRecordInfo> RecordAsync(
        CsvGameSessionRecorder recorder,
        GameSessionStartInfo info,
        IReadOnlyList<GameFrameSample> samples)
    {
        await recorder.StartAsync(info);
        foreach (GameFrameSample sample in samples)
        {
            TestSupport.True(recorder.TryRecord(sample, info.CaptureSessionId, info.Generation), "sample queued");
        }
        return (await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))!;
    }
}
