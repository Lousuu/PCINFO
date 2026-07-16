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
        ("Session catalog 08 directory size cache supports increments and rebuild", TestSupport.Run(DirectorySizeCacheSupportsIncrementAndRebuildAsync)),
        ("Session catalog 09 empty page reports zero total", TestSupport.Run(EmptyPageAsync)),
        ("Session catalog 10 five records fit first page", TestSupport.Run(FiveRecordsFitFirstPageAsync)),
        ("Session catalog 11 ten records have no more page", TestSupport.Run(TenRecordsHaveNoMoreAsync)),
        ("Session catalog 12 eleven records return ten first", TestSupport.Run(ElevenRecordsReturnTenAsync)),
        ("Session catalog 13 twenty five records page 10 10 5", TestSupport.Run(TwentyFiveRecordsPageAsync)),
        ("Session catalog 14 completed page has HasMore false", TestSupport.Run(CompletedPageHasNoMoreAsync)),
        ("Session catalog 15 snapshot token stays stable", TestSupport.Run(SnapshotTokenStaysStableAsync)),
        ("Session catalog 16 unchanged snapshot avoids full scan", TestSupport.Run(UnchangedSnapshotAvoidsFullScanAsync)),
        ("Session catalog 17 append invalidates total count", TestSupport.Run(AppendInvalidatesTotalAsync)),
        ("Session catalog 18 complete record wins over incomplete", TestSupport.Run(CompleteWinsOverIncompleteAsync)),
        ("Session catalog 19 equal timestamps have stable order", TestSupport.Run(EqualTimestampsHaveStableOrderAsync)),
        ("Session catalog 20 corrupt index fallback still paginates", TestSupport.Run(CorruptIndexFallbackPaginatesAsync))
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

    private static Task EmptyPageAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        CatalogPageReadResult page = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        TestSupport.Equal(0, page.Page.TotalCount, "empty total");
        TestSupport.Equal(0, page.Page.Records.Count, "empty records");
        TestSupport.False(page.Page.HasMore, "empty HasMore");
    });

    private static Task FiveRecordsFitFirstPageAsync() => AssertFirstPageAsync(5, 5, false);

    private static Task TenRecordsHaveNoMoreAsync() => AssertFirstPageAsync(10, 10, false);

    private static Task ElevenRecordsReturnTenAsync() => AssertFirstPageAsync(11, 10, true);

    private static Task AssertFirstPageAsync(int total, int expectedCount, bool hasMore) =>
        TestSupport.InTemporaryDirectory(async directory =>
        {
            GameSessionCatalog catalog = new(directory);
            await catalog.RebuildAsync(Enumerable.Range(0, total).Select(index => Record(directory, index)).ToArray(), CancellationToken.None);
            CatalogPageReadResult page = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
            TestSupport.Equal(total, page.Page.TotalCount, "page total");
            TestSupport.Equal(expectedCount, page.Page.Records.Count, "first-page count");
            TestSupport.Equal(hasMore, page.Page.HasMore, "first-page HasMore");
        });

    private static Task TwentyFiveRecordsPageAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync(Enumerable.Range(0, 25).Select(index => Record(directory, index)).ToArray(), CancellationToken.None);
        CatalogPageReadResult first = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        CatalogPageReadResult second = await catalog.ReadPageAsync(10, 10, first.Page.SnapshotToken, CancellationToken.None);
        CatalogPageReadResult third = await catalog.ReadPageAsync(20, 10, first.Page.SnapshotToken, CancellationToken.None);
        TestSupport.Equal(10, first.Page.Records.Count, "first ten");
        TestSupport.Equal(10, second.Page.Records.Count, "second ten");
        TestSupport.Equal(5, third.Page.Records.Count, "last five");
        TestSupport.Equal(25, first.Page.Records.Concat(second.Page.Records).Concat(third.Page.Records)
            .Select(item => item.CsvPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "page duplicates");
    });

    private static Task CompletedPageHasNoMoreAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync(Enumerable.Range(0, 25).Select(index => Record(directory, index)).ToArray(), CancellationToken.None);
        CatalogPageReadResult page = await catalog.ReadPageAsync(20, 10, null, CancellationToken.None);
        TestSupport.False(page.Page.HasMore, "last page HasMore");
    });

    private static Task SnapshotTokenStaysStableAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync(Enumerable.Range(0, 25).Select(index => Record(directory, index)).ToArray(), CancellationToken.None);
        CatalogPageReadResult first = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        CatalogPageReadResult second = await catalog.ReadPageAsync(10, 10, first.Page.SnapshotToken, CancellationToken.None);
        TestSupport.Equal(first.Page.SnapshotToken, second.Page.SnapshotToken, "snapshot token");
    });

    private static Task UnchangedSnapshotAvoidsFullScanAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync(Enumerable.Range(0, 25).Select(index => Record(directory, index)).ToArray(), CancellationToken.None);
        CatalogPageReadResult first = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        int scans = catalog.FullScanCount;
        _ = await catalog.ReadPageAsync(10, 10, first.Page.SnapshotToken, CancellationToken.None);
        _ = await catalog.ReadPageAsync(20, 10, first.Page.SnapshotToken, CancellationToken.None);
        TestSupport.Equal(scans, catalog.FullScanCount, "snapshot was rescanned");
    });

    private static Task AppendInvalidatesTotalAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync([Record(directory, 1)], CancellationToken.None);
        CatalogPageReadResult before = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        await catalog.AppendAsync(Record(directory, 2), CancellationToken.None);
        CatalogPageReadResult after = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        TestSupport.Equal(1, before.Page.TotalCount, "before append total");
        TestSupport.Equal(2, after.Page.TotalCount, "after append total");
    });

    private static Task CompleteWinsOverIncompleteAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string month = Path.Combine(directory, "2026-07");
        Directory.CreateDirectory(month);
        string completePath = Path.Combine(month, "same.csv");
        await File.WriteAllTextAsync(completePath + ".incomplete", "partial");
        GameSessionRecordInfo source = Record(directory, 1);
        GameSessionRecordInfo complete = WithPaths(source, completePath, Path.Combine(month, "same.summary.json"));
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync([complete], CancellationToken.None);
        CatalogPageReadResult page = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        TestSupport.Equal(1, page.Page.TotalCount, "complete/incomplete dedupe");
        TestSupport.True(page.Page.Records[0].IsComplete, "complete record did not win");

        static GameSessionRecordInfo WithPaths(GameSessionRecordInfo source, string csv, string summary) => new()
        {
            GameName = source.GameName,
            StartedAt = source.StartedAt,
            Duration = source.Duration,
            FileSize = source.FileSize,
            IsComplete = true,
            CsvPath = csv,
            SummaryPath = summary,
            EndReason = source.EndReason
        };
    });

    private static Task EqualTimestampsHaveStableOrderAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        GameSessionRecordInfo[] records = Enumerable.Range(0, 3)
            .Select(index => new GameSessionRecordInfo
            {
                GameName = "same",
                StartedAt = started,
                IsComplete = true,
                CsvPath = Path.Combine(directory, $"{index}.csv"),
                SummaryPath = Path.Combine(directory, $"{index}.summary.json")
            }).ToArray();
        GameSessionCatalog catalog = new(directory);
        await catalog.RebuildAsync(records.Reverse().ToArray(), CancellationToken.None);
        CatalogPageReadResult first = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        catalog.InvalidateSnapshot();
        CatalogPageReadResult second = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        TestSupport.Equal(string.Join('|', first.Page.Records.Select(item => item.CsvPath)),
            string.Join('|', second.Page.Records.Select(item => item.CsvPath)), "stable order");
    });

    private static Task CorruptIndexFallbackPaginatesAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        DateTimeOffset started = DateTimeOffset.UtcNow.AddMinutes(-25);
        for (int index = 0; index < 25; index++)
        {
            string stem = $"fallback-{index:D2}";
            GameSessionSummary summary = new()
            {
                CaptureSessionId = Guid.NewGuid(),
                CaptureGeneration = 1,
                ProcessName = stem,
                CaptureStartedAt = started.AddMinutes(index),
                CaptureEndedAt = started.AddMinutes(index).AddSeconds(1),
                Duration = TimeSpan.FromSeconds(1),
                CsvFileName = stem + ".csv"
            };
            await File.WriteAllTextAsync(Path.Combine(directory, stem + ".summary.json"), JsonSerializer.Serialize(summary));
        }
        await File.WriteAllTextAsync(Path.Combine(directory, GameSessionCatalog.FileName), "{broken");
        GameSessionCatalog catalog = new(directory);
        CatalogPageReadResult first = await catalog.ReadPageAsync(0, 10, null, CancellationToken.None);
        CatalogPageReadResult last = await catalog.ReadPageAsync(20, 10, first.Page.SnapshotToken, CancellationToken.None);
        TestSupport.Equal(25, first.Page.TotalCount, "fallback total");
        TestSupport.Equal(5, last.Page.Records.Count, "fallback final page");
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
