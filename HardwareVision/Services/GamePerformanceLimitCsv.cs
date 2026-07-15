using System.Globalization;
using System.IO;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GamePerformanceLimitCsv
{
    public const string Header = "CaptureSessionId,CaptureGeneration,EventId,ProcessorType,StartedAt,EndedAt,ElapsedStartSeconds,ElapsedEndSeconds,DurationSeconds,ReasonCount,Reasons,RawReasonNames,Scopes,TriggerCount,WasMerged,SupportStatus,IsActiveFinalState,Source,RawIdentifiers,WasTruncatedSource,Notes";

    public static string GetPath(string frameCsvPath)
    {
        string directory = Path.GetDirectoryName(frameCsvPath)!;
        string baseName = Path.GetFileNameWithoutExtension(frameCsvPath);
        return Path.Combine(directory, baseName + ".performance-limits.csv");
    }

    public static async Task WriteAsync(
        string path,
        GamePerformanceLimitSnapshot snapshot,
        DateTimeOffset sessionStartedAt,
        CancellationToken cancellationToken)
    {
        string temporaryPath = path + ".tmp";
        try
        {
            {
                await using FileStream stream = new(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using StreamWriter writer = new(stream, new UTF8Encoding(true), 16 * 1024);
                await writer.WriteLineAsync(Header.AsMemory(), cancellationToken).ConfigureAwait(false);
                for (int index = snapshot.Events.Count - 1; index >= 0; index--)
                {
                    GamePerformanceLimitEvent item = snapshot.Events[index];
                    if (item.CaptureSessionId != snapshot.CaptureSessionId || item.Generation != snapshot.Generation)
                    {
                        continue;
                    }

                    PerformanceLimitSupportStatus support = item.ProcessorType == PerformanceLimitProcessorType.Cpu
                        ? snapshot.CpuSupportStatus
                        : snapshot.GpuSupportStatus;
                    double elapsedStart = Math.Max(0d, (item.StartedAt - sessionStartedAt).TotalSeconds);
                    double elapsedEnd = Math.Max(elapsedStart, elapsedStart + item.Duration.TotalSeconds);
                    string line = FormatLine(item, support, elapsedStart, elapsedEnd, snapshot.EventsTruncated);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async Task<PerformanceLimitCsvReadResult> ReadAsync(
        string path,
        Guid? expectedSessionId,
        int? expectedGeneration,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return PerformanceLimitCsvReadResult.NotPresent;
        }

        List<GamePerformanceLimitEvent> events = [];
        List<string> warnings = [];
        using StreamReader reader = new(path, Encoding.UTF8, true, 16 * 1024);
        string? header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(header, Header, StringComparison.Ordinal))
        {
            warnings.Add("性能限制 CSV 列头不受支持");
            return new PerformanceLimitCsvReadResult(true, false, events, warnings);
        }

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                IReadOnlyList<string> fields = PresentMonCsvParser.ParseColumns(line);
                if (fields.Count < 21) throw new FormatException("Performance-limit row has too few fields.");
                Guid sessionId = Guid.Parse(fields[0]);
                int generation = ParseInt(fields[1]);
                if (expectedSessionId.HasValue && sessionId != expectedSessionId.Value
                    || expectedGeneration.HasValue && generation != expectedGeneration.Value)
                {
                    warnings.Add("已忽略 SessionId 或 generation 不匹配的限制事件");
                    continue;
                }

                DateTimeOffset startedAt = DateTimeOffset.Parse(fields[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                double durationSeconds = ParseDouble(fields[8]);
                events.Add(new GamePerformanceLimitEvent
                {
                    CaptureSessionId = sessionId,
                    Generation = generation,
                    EventId = ParseLong(fields[2]),
                    ProcessorType = Enum.Parse<PerformanceLimitProcessorType>(fields[3], true),
                    StartedAt = startedAt,
                    Duration = TimeSpan.FromSeconds(Math.Max(0d, durationSeconds)),
                    Reasons = SplitValues(fields[10]),
                    RawReasonNames = SplitValues(fields[11]),
                    Scopes = SplitValues(fields[12]),
                    TriggerCount = ParseInt(fields[13]),
                    WasMerged = ParseBool(fields[14]),
                    IsActive = ParseBool(fields[16]),
                    RawIdentifiers = SplitValues(fields[18])
                });
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException or OverflowException)
            {
                warnings.Add($"已跳过损坏的限制事件行：{exception.Message}");
            }
        }

        return new PerformanceLimitCsvReadResult(true, warnings.Count == 0, events, warnings);
    }

    internal static string JoinValues(IReadOnlyList<string> values)
    {
        StringBuilder builder = new();
        for (int index = 0; index < values.Count; index++)
        {
            if (index > 0) builder.Append(';');
            string value = values[index] ?? string.Empty;
            for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
            {
                char character = value[characterIndex];
                if (character is '\\' or ';') builder.Append('\\');
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    internal static IReadOnlyList<string> SplitValues(string value)
    {
        if (string.IsNullOrEmpty(value)) return [];
        List<string> result = [];
        StringBuilder builder = new();
        bool escaped = false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else if (character == ';')
            {
                result.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        if (escaped) builder.Append('\\');
        result.Add(builder.ToString());
        return result;
    }

    private static string FormatLine(
        GamePerformanceLimitEvent item,
        PerformanceLimitSupportStatus support,
        double elapsedStart,
        double elapsedEnd,
        bool truncated)
    {
        string[] values =
        [
            item.CaptureSessionId.ToString("N", CultureInfo.InvariantCulture),
            item.Generation.ToString(CultureInfo.InvariantCulture),
            item.EventId.ToString(CultureInfo.InvariantCulture),
            item.ProcessorType.ToString(),
            item.StartedAt.ToString("O", CultureInfo.InvariantCulture),
            item.EndedAt.ToString("O", CultureInfo.InvariantCulture),
            elapsedStart.ToString("0.###", CultureInfo.InvariantCulture),
            elapsedEnd.ToString("0.###", CultureInfo.InvariantCulture),
            item.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            item.Reasons.Count.ToString(CultureInfo.InvariantCulture),
            JoinValues(item.Reasons),
            JoinValues(item.RawReasonNames),
            JoinValues(item.Scopes),
            item.TriggerCount.ToString(CultureInfo.InvariantCulture),
            item.WasMerged ? "true" : "false",
            support.ToString(),
            item.IsActive ? "true" : "false",
            "GamePerformanceLimitTracker",
            JoinValues(item.RawIdentifiers),
            truncated ? "true" : "false",
            item.IsActive ? "Event remained active at final snapshot" : string.Empty
        ];
        return string.Join(',', values.Select(GameCsvFormatting.Escape));
    }

    private static int ParseInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static long ParseLong(string value) => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static double ParseDouble(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static bool ParseBool(string value) => bool.Parse(value);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed record PerformanceLimitCsvReadResult(
    bool IsPresent,
    bool IsValid,
    IReadOnlyList<GamePerformanceLimitEvent> Events,
    IReadOnlyList<string> Warnings)
{
    public static PerformanceLimitCsvReadResult NotPresent { get; } = new(false, true, [], []);
}
