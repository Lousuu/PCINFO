using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

internal sealed class GameSessionCatalog
{
    public const string FileName = "session-index.jsonl";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string rootDirectory;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly SemaphoreSlim snapshotLock = new(1, 1);
    private CatalogSnapshot? snapshot;
    private int fullScanCount;

    public GameSessionCatalog(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
    }

    public string IndexPath => Path.Combine(rootDirectory, FileName);

    internal int FullScanCount => Volatile.Read(ref fullScanCount);

    public async Task AppendAsync(GameSessionRecordInfo record, CancellationToken cancellationToken)
    {
        CatalogEntry? entry = CreateEntry(record);
        if (entry is null) return;
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(rootDirectory);
            await using FileStream stream = new(
                IndexPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using StreamWriter writer = new(stream, new UTF8Encoding(false), 16 * 1024);
            await writer.WriteLineAsync(JsonSerializer.Serialize(entry, JsonOptions).AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InvalidateSnapshot();
            writeLock.Release();
        }
    }

    public async Task<CatalogReadResult> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken)
    {
        CatalogPageReadResult page = await ReadPageAsync(
            0,
            Math.Max(1, maximumCount),
            snapshotToken: null,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GameSessionRecordInfo> records = maximumCount <= 0 ? [] : page.Page.Records;
        return new CatalogReadResult(
            page.IsPresent,
            page.IsUsable,
            records,
            page.ValidLineCount,
            page.CorruptLineCount);
    }

    public async Task<CatalogPageReadResult> ReadPageAsync(
        int offset,
        int pageSize,
        string? snapshotToken,
        CancellationToken cancellationToken)
    {
        int normalizedOffset = Math.Max(0, offset);
        int normalizedPageSize = Math.Max(1, pageSize);
        CatalogSnapshot current = await GetSnapshotAsync(snapshotToken, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GameSessionRecordInfo> records = current.Records
            .Skip(normalizedOffset)
            .Take(normalizedPageSize)
            .ToArray();
        GameSessionRecordPage page = new()
        {
            Records = records,
            Offset = normalizedOffset,
            PageSize = normalizedPageSize,
            TotalCount = current.Records.Count,
            HasMore = normalizedOffset + records.Count < current.Records.Count,
            SnapshotToken = current.Token
        };
        return new CatalogPageReadResult(
            current.IsPresent,
            current.IsUsable,
            page,
            current.ValidLineCount,
            current.CorruptLineCount);
    }

    public async Task RebuildAsync(IReadOnlyList<GameSessionRecordInfo> records, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporaryPath = IndexPath + ".rebuild.tmp";
        try
        {
            Directory.CreateDirectory(rootDirectory);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (StreamWriter writer = new(stream, new UTF8Encoding(false), 32 * 1024))
            {
                foreach (GameSessionRecordInfo record in records.OrderBy(static item => item.StartedAt))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CatalogEntry? entry = CreateEntry(record);
                    if (entry is null) continue;
                    await writer.WriteLineAsync(JsonSerializer.Serialize(entry, JsonOptions).AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                }
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, IndexPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
            InvalidateSnapshot();
            writeLock.Release();
        }
    }

    internal void InvalidateSnapshot() => Volatile.Write(ref snapshot, null);

    private async Task<CatalogSnapshot> GetSnapshotAsync(
        string? requestedToken,
        CancellationToken cancellationToken)
    {
        CatalogSnapshot? cached = Volatile.Read(ref snapshot);
        if (cached is not null
            && !string.IsNullOrWhiteSpace(requestedToken)
            && string.Equals(cached.Token, requestedToken, StringComparison.Ordinal))
        {
            return cached;
        }

        await snapshotLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = Volatile.Read(ref snapshot);
            if (cached is not null
                && !string.IsNullOrWhiteSpace(requestedToken)
                && string.Equals(cached.Token, requestedToken, StringComparison.Ordinal))
            {
                return cached;
            }

            CatalogFingerprint fingerprint = GetFingerprint(cached?.UsedFallback == true);
            if (cached is not null && cached.Fingerprint.Equals(fingerprint)) return cached;

            CatalogSnapshot rebuilt = await BuildSnapshotAsync(fingerprint, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref snapshot, rebuilt);
            return rebuilt;
        }
        finally
        {
            snapshotLock.Release();
        }
    }

    private async Task<CatalogSnapshot> BuildSnapshotAsync(
        CatalogFingerprint fingerprint,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref fullScanCount);
        IndexReadResult index = await ReadIndexAsync(cancellationToken).ConfigureAwait(false);
        bool usedFallback = !index.IsUsable;
        List<GameSessionRecordInfo> records = usedFallback
            ? await ReadSummaryRecordsAsync(cancellationToken).ConfigureAwait(false)
            : new List<GameSessionRecordInfo>(index.Records);
        records.AddRange(ReadIncompleteRecords(cancellationToken));
        IReadOnlyList<GameSessionRecordInfo> normalized = NormalizeRecords(records);

        if (usedFallback)
        {
            try
            {
                await RebuildAsync(normalized.Where(static item => item.IsComplete).ToArray(), CancellationToken.None)
                    .ConfigureAwait(false);
                fingerprint = GetFingerprint(includeSummaryFiles: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AppLogger.LogError("Game-session index rebuild failed.", exception,
                    "session-index-rebuild", TimeSpan.FromMinutes(5));
                fingerprint = GetFingerprint(includeSummaryFiles: true);
            }
        }

        return new CatalogSnapshot(
            Guid.NewGuid().ToString("N"),
            fingerprint,
            normalized,
            index.IsPresent,
            index.IsUsable || normalized.Count > 0,
            index.ValidLineCount,
            index.CorruptLineCount,
            usedFallback && !File.Exists(IndexPath));
    }

    private async Task<IndexReadResult> ReadIndexAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(IndexPath)) return new IndexReadResult(false, false, [], 0, 0);
        List<GameSessionRecordInfo> records = [];
        int validLines = 0;
        int corruptLines = 0;
        try
        {
            using StreamReader reader = new(IndexPath, Encoding.UTF8, true, 32 * 1024);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    CatalogEntry? entry = JsonSerializer.Deserialize<CatalogEntry>(line, JsonOptions);
                    GameSessionRecordInfo? record = entry is null ? null : ToRecord(entry);
                    if (record is null)
                    {
                        corruptLines++;
                        continue;
                    }
                    validLines++;
                    records.Add(record);
                }
                catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
                {
                    corruptLines++;
                    AppLogger.LogError(
                        "A corrupt game-session index line was skipped.",
                        exception,
                        "session-index-corrupt-line",
                        TimeSpan.FromMinutes(5));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Game-session index could not be read.", exception,
                "session-index-read", TimeSpan.FromMinutes(5));
            return new IndexReadResult(true, false, [], validLines, corruptLines + 1);
        }

        bool usable = validLines > 0 || corruptLines == 0;
        return new IndexReadResult(true, usable, records, validLines, corruptLines);
    }

    private async Task<List<GameSessionRecordInfo>> ReadSummaryRecordsAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory)) return [];
        string[] summaryPaths = Directory.GetFiles(rootDirectory, "*.summary.json", SearchOption.AllDirectories);
        List<GameSessionRecordInfo> records = new(summaryPaths.Length);
        foreach (string summaryPath in summaryPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameSessionRecordInfo? record = await ReadSummaryRecordAsync(summaryPath, cancellationToken).ConfigureAwait(false);
            if (record is not null) records.Add(record);
        }
        return records;
    }

    private IReadOnlyList<GameSessionRecordInfo> ReadIncompleteRecords(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDirectory)) return [];
        List<GameSessionRecordInfo> records = [];
        IEnumerable<string> paths = Directory.EnumerateFiles(rootDirectory, "*.csv.incomplete", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(rootDirectory, "*.csv.gz.incomplete", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                FileInfo file = new(path);
                records.Add(new GameSessionRecordInfo
                {
                    GameName = ParseGameName(file.Name),
                    StartedAt = file.CreationTime,
                    Duration = TimeSpan.Zero,
                    FileSize = file.Length,
                    IsComplete = false,
                    CsvPath = file.FullName,
                    EndReason = GameSessionEndReason.ApplicationShutdown
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        return records;
    }

    private static IReadOnlyList<GameSessionRecordInfo> NormalizeRecords(
        IEnumerable<GameSessionRecordInfo> records)
    {
        Dictionary<string, GameSessionRecordInfo> unique = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameSessionRecordInfo record in records)
        {
            string key = NormalizeRecordKey(record.CsvPath);
            if (!unique.TryGetValue(key, out GameSessionRecordInfo? existing)
                || record.IsComplete && !existing.IsComplete
                || record.IsComplete == existing.IsComplete && record.StartedAt > existing.StartedAt)
            {
                unique[key] = record;
            }
        }
        return unique.Values
            .OrderByDescending(static item => item.StartedAt)
            .ThenByDescending(static item => item.IsComplete)
            .ThenBy(static item => item.CsvPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string NormalizeRecordKey(string path)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            fullPath = path;
        }
        if (fullPath.EndsWith(".csv.gz.incomplete", StringComparison.OrdinalIgnoreCase)
            || fullPath.EndsWith(".csv.incomplete", StringComparison.OrdinalIgnoreCase))
        {
            fullPath = fullPath[..^".incomplete".Length];
        }
        return fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private CatalogFingerprint GetFingerprint(bool includeSummaryFiles)
    {
        FileStamp index = GetFileStamp(IndexPath);
        FileStamp incomplete = GetAggregateStamp("*.csv.incomplete", "*.csv.gz.incomplete");
        FileStamp summaries = includeSummaryFiles ? GetAggregateStamp("*.summary.json") : default;
        return new CatalogFingerprint(index, incomplete, summaries);
    }

    private FileStamp GetAggregateStamp(params string[] patterns)
    {
        if (!Directory.Exists(rootDirectory)) return default;
        long length = 0L;
        long lastWriteTicks = 0L;
        long count = 0L;
        try
        {
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < patterns.Length; index++)
            {
                foreach (string path in Directory.EnumerateFiles(rootDirectory, patterns[index], SearchOption.AllDirectories))
                    paths.Add(path);
            }
            foreach (string path in paths)
            {
                FileInfo file = new(path);
                length = unchecked(length + file.Length);
                lastWriteTicks = Math.Max(lastWriteTicks, file.LastWriteTimeUtc.Ticks);
                count++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new FileStamp(-1L, -1L, -1L);
        }
        return new FileStamp(length, lastWriteTicks, count);
    }

    private static FileStamp GetFileStamp(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            FileInfo file = new(path);
            return new FileStamp(file.Length, file.LastWriteTimeUtc.Ticks, 1L);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new FileStamp(-1L, -1L, -1L);
        }
    }

    private CatalogEntry? CreateEntry(GameSessionRecordInfo record)
    {
        if (!record.IsComplete || string.IsNullOrWhiteSpace(record.SummaryPath)) return null;
        string? csv = SafeRelative(record.CsvPath);
        string? summary = SafeRelative(record.SummaryPath);
        if (csv is null || summary is null) return null;
        return new CatalogEntry
        {
            SchemaVersion = 1,
            GameName = record.GameName,
            StartedAt = record.StartedAt,
            DurationTicks = record.Duration.Ticks,
            FileSize = record.FileSize,
            CsvPath = csv,
            SummaryPath = summary,
            PerformanceLimitCsvPath = SafeRelative(record.PerformanceLimitCsvPath),
            HardwareTimelineCsvPath = SafeRelative(record.HardwareTimelineCsvPath),
            EndReason = record.EndReason,
            EstimatedEnergyWh = record.EstimatedEnergyWh,
            AverageEstimatedPowerWatts = record.AverageEstimatedPowerWatts,
            EnergyCoveragePercent = record.EnergyCoveragePercent,
            EnergyIncludedComponents = record.EnergyIncludedComponents,
            CpuPerformanceLimitEventCount = record.CpuPerformanceLimitEventCount,
            GpuPerformanceLimitEventCount = record.GpuPerformanceLimitEventCount,
            CpuPerformanceLimitSupportStatus = record.CpuPerformanceLimitSupportStatus,
            GpuPerformanceLimitSupportStatus = record.GpuPerformanceLimitSupportStatus
        };
    }

    private GameSessionRecordInfo? ToRecord(CatalogEntry entry)
    {
        if (entry.SchemaVersion > 1) return null;
        if (!SessionFilePathResolver.TryResolveRelativePath(rootDirectory, entry.CsvPath,
                SessionFileKind.FrameCsv, out string? csv, out _)
            || !SessionFilePathResolver.TryResolveRelativePath(rootDirectory, entry.SummaryPath,
                SessionFileKind.SummaryJson, out string? summary, out _)
            || csv is null || summary is null)
        {
            return null;
        }
        return new GameSessionRecordInfo
        {
            GameName = entry.GameName,
            StartedAt = entry.StartedAt,
            Duration = TimeSpan.FromTicks(Math.Max(0L, entry.DurationTicks)),
            FileSize = entry.FileSize,
            IsComplete = true,
            CsvPath = csv,
            SummaryPath = summary,
            PerformanceLimitCsvPath = ResolveOptional(entry.PerformanceLimitCsvPath, SessionFileKind.PerformanceLimitsCsv),
            HardwareTimelineCsvPath = ResolveOptional(entry.HardwareTimelineCsvPath, SessionFileKind.HardwareTimelineCsv),
            EndReason = entry.EndReason,
            EstimatedEnergyWh = entry.EstimatedEnergyWh,
            AverageEstimatedPowerWatts = entry.AverageEstimatedPowerWatts,
            EnergyCoveragePercent = entry.EnergyCoveragePercent,
            EnergyIncludedComponents = entry.EnergyIncludedComponents,
            CpuPerformanceLimitEventCount = entry.CpuPerformanceLimitEventCount,
            GpuPerformanceLimitEventCount = entry.GpuPerformanceLimitEventCount,
            CpuPerformanceLimitSupportStatus = entry.CpuPerformanceLimitSupportStatus,
            GpuPerformanceLimitSupportStatus = entry.GpuPerformanceLimitSupportStatus
        };
    }

    private async Task<GameSessionRecordInfo?> ReadSummaryRecordAsync(
        string summaryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(summaryPath);
            GameSessionSummary? summary = await JsonSerializer.DeserializeAsync<GameSessionSummary>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (summary is null) return null;
            string directory = Path.GetDirectoryName(summaryPath)!;
            if (!SessionFilePathResolver.TryResolve(directory, summary.CsvFileName, SessionFileKind.FrameCsv,
                    out string? csvPath, out _)
                || csvPath is null)
            {
                return null;
            }
            return new GameSessionRecordInfo
            {
                GameName = summary.ProcessName,
                StartedAt = summary.CaptureStartedAt,
                Duration = summary.Duration,
                FileSize = File.Exists(csvPath) ? new FileInfo(csvPath).Length : summary.CsvFileSize,
                IsComplete = true,
                CsvPath = csvPath,
                SummaryPath = summaryPath,
                PerformanceLimitCsvPath = ResolveSummaryFile(directory, summary.PerformanceLimitCsvFileName, SessionFileKind.PerformanceLimitsCsv),
                HardwareTimelineCsvPath = ResolveSummaryFile(directory, summary.HardwareTimelineCsvFileName, SessionFileKind.HardwareTimelineCsv),
                EndReason = summary.EndReason,
                EstimatedEnergyWh = summary.EstimatedEnergyWh,
                AverageEstimatedPowerWatts = summary.AverageEstimatedPowerWatts,
                EnergyCoveragePercent = summary.EnergyCoveragePercent,
                EnergyIncludedComponents = summary.EnergyIncludedComponents,
                CpuPerformanceLimitEventCount = summary.CpuPerformanceLimitEventCount,
                GpuPerformanceLimitEventCount = summary.GpuPerformanceLimitEventCount,
                CpuPerformanceLimitSupportStatus = summary.CpuPerformanceLimitSupportStatus,
                GpuPerformanceLimitSupportStatus = summary.GpuPerformanceLimitSupportStatus
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.LogError("Game session summary could not be read.", exception,
                $"game-summary-read:{Path.GetFileName(summaryPath)}", TimeSpan.FromMinutes(5));
            return null;
        }
    }

    private string? ResolveOptional(string? path, SessionFileKind kind) =>
        SessionFilePathResolver.TryResolveRelativePath(rootDirectory, path, kind, out string? fullPath, out _)
            ? fullPath
            : null;

    private static string? ResolveSummaryFile(string directory, string? fileName, SessionFileKind kind) =>
        SessionFilePathResolver.TryResolve(directory, fileName, kind, out string? path, out _) ? path : null;

    private string? SafeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string relative = Path.GetRelativePath(rootDirectory, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ParseGameName(string fileName)
    {
        int separator = fileName.IndexOf('-', StringComparison.Ordinal);
        return separator > 0 ? fileName[..separator] : "Game";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed record CatalogSnapshot(
        string Token,
        CatalogFingerprint Fingerprint,
        IReadOnlyList<GameSessionRecordInfo> Records,
        bool IsPresent,
        bool IsUsable,
        int ValidLineCount,
        int CorruptLineCount,
        bool UsedFallback);

    private readonly record struct CatalogFingerprint(FileStamp Index, FileStamp Incomplete, FileStamp Summaries);

    private readonly record struct FileStamp(long Length, long LastWriteTicks, long Count);

    private sealed record IndexReadResult(
        bool IsPresent,
        bool IsUsable,
        IReadOnlyList<GameSessionRecordInfo> Records,
        int ValidLineCount,
        int CorruptLineCount);

    private sealed class CatalogEntry
    {
        public int SchemaVersion { get; init; }
        public string GameName { get; init; } = string.Empty;
        public DateTimeOffset StartedAt { get; init; }
        public long DurationTicks { get; init; }
        public long FileSize { get; init; }
        public string CsvPath { get; init; } = string.Empty;
        public string SummaryPath { get; init; } = string.Empty;
        public string? PerformanceLimitCsvPath { get; init; }
        public string? HardwareTimelineCsvPath { get; init; }
        public GameSessionEndReason EndReason { get; init; }
        public double? EstimatedEnergyWh { get; init; }
        public double? AverageEstimatedPowerWatts { get; init; }
        public double? EnergyCoveragePercent { get; init; }
        public string? EnergyIncludedComponents { get; init; }
        public int? CpuPerformanceLimitEventCount { get; init; }
        public int? GpuPerformanceLimitEventCount { get; init; }
        public PerformanceLimitSupportStatus? CpuPerformanceLimitSupportStatus { get; init; }
        public PerformanceLimitSupportStatus? GpuPerformanceLimitSupportStatus { get; init; }
    }
}

internal sealed record CatalogReadResult(
    bool IsPresent,
    bool IsUsable,
    IReadOnlyList<GameSessionRecordInfo> Records,
    int ValidLineCount,
    int CorruptLineCount);

internal sealed record CatalogPageReadResult(
    bool IsPresent,
    bool IsUsable,
    GameSessionRecordPage Page,
    int ValidLineCount,
    int CorruptLineCount);
