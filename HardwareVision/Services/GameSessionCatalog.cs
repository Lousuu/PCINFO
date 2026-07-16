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

    public GameSessionCatalog(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
    }

    public string IndexPath => Path.Combine(rootDirectory, FileName);

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
            writeLock.Release();
        }
    }

    public async Task<CatalogReadResult> ReadRecentAsync(int maximumCount, CancellationToken cancellationToken)
    {
        if (!File.Exists(IndexPath)) return new CatalogReadResult(false, false, [], 0, 0);
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
            return new CatalogReadResult(true, false, [], validLines, corruptLines + 1);
        }

        IReadOnlyList<GameSessionRecordInfo> recent = records
            .OrderByDescending(static record => record.StartedAt)
            .Take(Math.Max(0, maximumCount))
            .ToArray();
        bool usable = validLines > 0 || corruptLines == 0;
        return new CatalogReadResult(true, usable, recent, validLines, corruptLines);
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
            writeLock.Release();
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

        string? limit = ResolveOptional(entry.PerformanceLimitCsvPath, SessionFileKind.PerformanceLimitsCsv);
        string? timeline = ResolveOptional(entry.HardwareTimelineCsvPath, SessionFileKind.HardwareTimelineCsv);
        return new GameSessionRecordInfo
        {
            GameName = entry.GameName,
            StartedAt = entry.StartedAt,
            Duration = TimeSpan.FromTicks(Math.Max(0L, entry.DurationTicks)),
            FileSize = entry.FileSize,
            IsComplete = true,
            CsvPath = csv,
            SummaryPath = summary,
            PerformanceLimitCsvPath = limit,
            HardwareTimelineCsvPath = timeline,
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

    private string? ResolveOptional(string? path, SessionFileKind kind) =>
        SessionFilePathResolver.TryResolveRelativePath(rootDirectory, path, kind, out string? fullPath, out _)
            ? fullPath
            : null;

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

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

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
