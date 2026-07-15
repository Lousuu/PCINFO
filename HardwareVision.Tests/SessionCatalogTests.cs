using System.Text.Json;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionCatalogTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session catalog 01 uses JSONL index name", TestSupport.Run(UsesJsonlIndexNameAsync)),
        ("Session catalog 02 reads recent from 5000 entries", TestSupport.Run(ReadsRecentFromFiveThousandAsync)),
        ("Session catalog 03 corrupt line is skipped", TestSupport.Run(CorruptLineIsSkippedAsync)),
        ("Session catalog 04 corrupt-only index requests fallback", TestSupport.Run(CorruptOnlyIndexRequestsFallbackAsync)),
        ("Session catalog 05 malicious index path is skipped", TestSupport.Run(MaliciousIndexPathIsSkippedAsync)),
        ("Session catalog 06 legacy 5000-summary scan rebuilds index", TestSupport.Run(LegacyFiveThousandSummaryScanRebuildsAsync)),
        ("Session catalog 07 directory size cache scans once", TestSupport.Run(DirectorySizeCacheScansOnceAsync)),
        ("Session catalog 08 directory size cache supports increments and rebuild", TestSupport.Run(DirectorySizeCacheSupportsIncrementAndRebuildAsync))
    ];

    private static Task UsesJsonlIndexNameAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.AppendAsync(Record(directory, 1), CancellationToken.None);
        TestSupport.Equal("session-index.jsonl", Path.GetFileName(catalog.IndexPath), "index filename");
        TestSupport.Equal(1, File.ReadLines(catalog.IndexPath).Count(), "JSONL line count");
    });

    private static Task ReadsRecentFromFiveThousandAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        GameSessionRecordInfo[] records = Enumerable.Range(0, 5000)
            .Select(index => Record(directory, index))
            .ToArray();
        await catalog.RebuildAsync(records, CancellationToken.None);
        CatalogReadResult? result = null;
        Measurement measurement = TestSupport.Measure("session-index-5000-read", () =>
        {
            result = catalog.ReadRecentAsync(25, CancellationToken.None).GetAwaiter().GetResult();
        });
        TestSupport.True(result?.IsUsable == true, "index was unusable");
        TestSupport.Equal(5000, result!.ValidLineCount, "valid index entries");
        TestSupport.Equal(25, result.Records.Count, "retained recent entries");
        TestSupport.True(measurement.Elapsed < TimeSpan.FromSeconds(10), "5000-entry index read exceeded synthetic-test threshold");
    });

    private static Task CorruptLineIsSkippedAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync([Record(directory, 1)], CancellationToken.None);
        await File.AppendAllTextAsync(catalog.IndexPath, "{broken-json" + Environment.NewLine);
        CatalogReadResult result = await catalog.ReadRecentAsync(10, CancellationToken.None);
        TestSupport.Equal(1, result.ValidLineCount, "valid line count");
        TestSupport.Equal(1, result.CorruptLineCount, "corrupt line count");
        TestSupport.Equal(1, result.Records.Count, "valid record count");
    });

    private static Task CorruptOnlyIndexRequestsFallbackAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await File.WriteAllTextAsync(catalog.IndexPath, "not-json");
        CatalogReadResult result = await catalog.ReadRecentAsync(10, CancellationToken.None);
        TestSupport.False(result.IsUsable, "corrupt-only index was accepted");
    });

    private static Task MaliciousIndexPathIsSkippedAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        string line = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            GameName = "evil",
            StartedAt = DateTimeOffset.UtcNow,
            DurationTicks = 1,
            FileSize = 1,
            CsvPath = "../outside.csv",
            SummaryPath = "../outside.summary.json",
            EndReason = "UserStopped"
        });
        await File.WriteAllTextAsync(catalog.IndexPath, line);
        CatalogReadResult result = await catalog.ReadRecentAsync(10, CancellationToken.None);
        TestSupport.Equal(0, result.Records.Count, "malicious record was returned");
        TestSupport.False(result.IsUsable, "malicious-only index was accepted");
    });

    private static Task LegacyFiveThousandSummaryScanRebuildsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(-1);
        for (int index = 0; index < 5000; index++)
        {
            string stem = $"legacy-{index:D4}";
            GameSessionSummary summary = new()
            {
                CaptureSessionId = Guid.NewGuid(),
                CaptureGeneration = 1,
                ProcessName = stem,
                CaptureStartedAt = start.AddSeconds(index),
                CaptureEndedAt = start.AddSeconds(index + 1),
                Duration = TimeSpan.FromSeconds(1),
                CsvFileName = stem + ".csv",
                CsvFileSize = 64
            };
            File.WriteAllText(Path.Combine(directory, stem + ".summary.json"), JsonSerializer.Serialize(summary));
        }

        await using CsvGameSessionRecorder recorder = new(directory, 8);
        IReadOnlyList<GameSessionRecordInfo>? recent = null;
        Measurement measurement = TestSupport.Measure("legacy-5000-summary-scan", () =>
        {
            recent = recorder.GetRecentRecordsAsync(20).GetAwaiter().GetResult();
        });
        TestSupport.Equal(20, recent!.Count, "legacy recent count");
        TestSupport.Equal(5000, File.ReadLines(Path.Combine(directory, GameSessionCatalog.FileName)).Count(), "rebuilt index count");
        TestSupport.True(measurement.Elapsed < TimeSpan.FromMinutes(2), "legacy scan exceeded synthetic-test threshold");
    });

    private static Task DirectorySizeCacheScansOnceAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await File.WriteAllBytesAsync(Path.Combine(directory, "a.bin"), new byte[128]);
        SessionDirectorySizeCache cache = new(directory);
        cache.StartInitialScan();
        long size = await cache.GetExactSizeAsync(false, CancellationToken.None);
        for (int index = 0; index < 20; index++) _ = cache.GetInfo();
        TestSupport.Equal(128L, size, "initial directory size");
        TestSupport.Equal(1, cache.FullScanCount, "full scan count");
    });

    private static Task DirectorySizeCacheSupportsIncrementAndRebuildAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string first = Path.Combine(directory, "a.bin");
        await File.WriteAllBytesAsync(first, new byte[100]);
        SessionDirectorySizeCache cache = new(directory);
        long initial = await cache.GetExactSizeAsync(false, CancellationToken.None);
        cache.AddBytes(50);
        TestSupport.Equal(150L, cache.GetInfo().Bytes, "incremental size");
        File.Delete(first);
        long rebuilt = await cache.GetExactSizeAsync(true, CancellationToken.None);
        TestSupport.Equal(0L, rebuilt, "rebuilt size after deletion");
        TestSupport.Equal(2, cache.FullScanCount, "rebuild scan count");
        TestSupport.Equal(100L, initial, "initial size");
    });

    private static GameSessionRecordInfo Record(string root, int index)
    {
        string month = "2026-07";
        string directory = Path.Combine(root, month);
        string stem = $"session-{index:D4}";
        return new GameSessionRecordInfo
        {
            GameName = stem,
            StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(index),
            Duration = TimeSpan.FromSeconds(index % 300),
            FileSize = 100 + index,
            IsComplete = true,
            CsvPath = Path.Combine(directory, stem + ".csv"),
            SummaryPath = Path.Combine(directory, stem + ".summary.json"),
            EndReason = GameSessionEndReason.UserStopped
        };
    }
}
