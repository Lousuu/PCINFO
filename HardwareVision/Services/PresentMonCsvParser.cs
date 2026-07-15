using System.Globalization;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public enum PresentMonCsvParseKind
{
    Ignored,
    HeaderAccepted,
    SchemaMismatch,
    Filtered,
    Sample,
    Rejected
}

public readonly struct PresentMonCsvParseResult
{
    private PresentMonCsvParseResult(
        PresentMonCsvParseKind kind,
        GameFrameSample? sample = null,
        string? reason = null,
        bool isDataRow = false,
        int filteredProcessId = 0)
    {
        Kind = kind;
        Sample = sample;
        Reason = reason;
        IsDataRow = isDataRow;
        FilteredProcessId = filteredProcessId;
    }

    public PresentMonCsvParseKind Kind { get; }

    public GameFrameSample? Sample { get; }

    public string? Reason { get; }

    public bool IsDataRow { get; }

    public int FilteredProcessId { get; }

    public static PresentMonCsvParseResult Ignored(string? reason = null) =>
        new(PresentMonCsvParseKind.Ignored, reason: reason);

    public static PresentMonCsvParseResult HeaderAccepted() =>
        new(PresentMonCsvParseKind.HeaderAccepted);

    public static PresentMonCsvParseResult SchemaMismatch(string reason) =>
        new(PresentMonCsvParseKind.SchemaMismatch, reason: reason);

    public static PresentMonCsvParseResult Filtered(int processId) =>
        new(PresentMonCsvParseKind.Filtered, isDataRow: true, filteredProcessId: processId);

    public static PresentMonCsvParseResult Parsed(GameFrameSample sample) =>
        new(PresentMonCsvParseKind.Sample, sample, isDataRow: true);

    public static PresentMonCsvParseResult Rejected(string reason) =>
        new(PresentMonCsvParseKind.Rejected, reason: reason, isDataRow: true);
}

public sealed class PresentMonCsvSchema
{
    private readonly int[] slotsByColumn;

    internal PresentMonCsvSchema(IReadOnlyList<string> rawColumns)
    {
        string[] raw = new string[rawColumns.Count];
        string[] normalized = new string[rawColumns.Count];
        Dictionary<string, int> indexes = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < rawColumns.Count; index++)
        {
            raw[index] = rawColumns[index];
            string name = NormalizeColumnName(rawColumns[index]);
            normalized[index] = name;
            if (name.Length > 0)
            {
                indexes.TryAdd(name, index);
            }
        }

        RawColumns = raw;
        NormalizedColumns = normalized;
        slotsByColumn = new int[raw.Length];
        Array.Fill(slotsByColumn, -1);

        Assign(indexes, CsvFieldSlot.ProcessId, "processid", "pid");
        Assign(indexes, CsvFieldSlot.Application, "application", "processname", "app");
        Assign(indexes, CsvFieldSlot.FrameTime, "frametime", "msbetweenpresents", "msbetweenpresent", "frametimems", "msperframe");
        Assign(indexes, CsvFieldSlot.CpuBusy, "cpubusy", "mscpubusy", "cpubusyms");
        Assign(indexes, CsvFieldSlot.CpuWait, "cpuwait", "mscpuwait", "cpuwaitms");
        Assign(indexes, CsvFieldSlot.GpuLatency, "gpulatency", "msgpulatency", "gpulatencyms");
        Assign(indexes, CsvFieldSlot.GpuTime, "gputime", "msgputime", "gputimems");
        Assign(indexes, CsvFieldSlot.GpuBusy, "gpubusy", "msgpubusy", "gpubusyms", "msgpuactive");
        Assign(indexes, CsvFieldSlot.GpuWait, "gpuwait", "msgpuwait", "gpuwaitms");
        Assign(indexes, CsvFieldSlot.RenderLatency, "msuntilrendercomplete", "msrenderpresentlatency", "renderlatencyms");
        Assign(indexes, CsvFieldSlot.DisplayLatency, "displaylatency", "msdisplaylatency", "msuntildisplayed");
        Assign(indexes, CsvFieldSlot.DisplayedTime, "displayedtime", "msdisplayedtime");
        Assign(indexes, CsvFieldSlot.ClickToPhoton, "clicktophotonlatency", "msclicktophotonlatency", "clicktophotonlatencyms", "mspclatency");
        Assign(indexes, CsvFieldSlot.Runtime, "presentruntime", "runtime");
        Assign(indexes, CsvFieldSlot.PresentMode, "presentmode");
        Assign(indexes, CsvFieldSlot.SwapChain, "swapchainaddress", "swapchain");
        Assign(indexes, CsvFieldSlot.FrameType, "frametype");
    }

    public IReadOnlyList<string> RawColumns { get; }

    public IReadOnlyList<string> NormalizedColumns { get; }

    public bool HasFrameTimeColumn => HasSlot(CsvFieldSlot.FrameTime);

    internal int GetSlot(int columnIndex) =>
        (uint)columnIndex < (uint)slotsByColumn.Length ? slotsByColumn[columnIndex] : -1;

    internal bool HasSlot(CsvFieldSlot slot)
    {
        for (int index = 0; index < slotsByColumn.Length; index++)
        {
            if (slotsByColumn[index] == (int)slot)
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeColumnName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        StringBuilder builder = new(span.Length);
        foreach (char character in span)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private void Assign(Dictionary<string, int> indexes, CsvFieldSlot slot, params string[] aliases)
    {
        for (int index = 0; index < aliases.Length; index++)
        {
            if (indexes.TryGetValue(aliases[index], out int columnIndex))
            {
                slotsByColumn[columnIndex] = (int)slot;
                return;
            }
        }
    }
}

internal enum CsvFieldSlot
{
    ProcessId,
    Application,
    FrameTime,
    CpuBusy,
    CpuWait,
    GpuLatency,
    GpuTime,
    GpuBusy,
    GpuWait,
    RenderLatency,
    DisplayLatency,
    DisplayedTime,
    ClickToPhoton,
    Runtime,
    PresentMode,
    SwapChain,
    FrameType,
    Count
}

public sealed class PresentMonCsvParser
{
    private readonly Guid captureSessionId;
    private readonly int fallbackProcessId;
    private readonly string fallbackProcessName;
    private readonly int? targetProcessId;
    private string? cachedRuntime;
    private string? cachedPresentMode;
    private string? cachedFrameType;

    public PresentMonCsvParser(Guid captureSessionId, int fallbackProcessId, string fallbackProcessName)
        : this(captureSessionId, fallbackProcessId, fallbackProcessName, targetProcessId: null)
    {
    }

    internal PresentMonCsvParser(
        Guid captureSessionId,
        int fallbackProcessId,
        string fallbackProcessName,
        int? targetProcessId)
    {
        this.captureSessionId = captureSessionId;
        this.fallbackProcessId = fallbackProcessId;
        this.fallbackProcessName = fallbackProcessName;
        this.targetProcessId = targetProcessId;
    }

    public PresentMonCsvSchema? Schema { get; private set; }

    internal long NumericFieldParseCount { get; private set; }

    internal long SampleCreationCount { get; private set; }

    public PresentMonCsvParseResult ParseLine(string? line, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains(',', StringComparison.Ordinal))
        {
            return PresentMonCsvParseResult.Ignored("not-csv");
        }

        if (Schema is null || MightBeHeader(line))
        {
            IReadOnlyList<string> columns = ParseColumns(line);
            if (columns.Count < 2)
            {
                return PresentMonCsvParseResult.Ignored("too-few-columns");
            }

            if (!LooksLikeHeader(columns))
            {
                return Schema is null
                    ? PresentMonCsvParseResult.Ignored("awaiting-header")
                    : ParseDataLine(line, timestamp);
            }

            Schema = new PresentMonCsvSchema(columns);
            return Schema.HasFrameTimeColumn
                ? PresentMonCsvParseResult.HeaderAccepted()
                : PresentMonCsvParseResult.SchemaMismatch(
                    "缺少 FrameTime / MsBetweenPresents（或兼容别名）字段");
        }

        return ParseDataLine(line, timestamp);
    }

    public static IReadOnlyList<string> ParseColumns(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        List<string> columns = new(32);
        StringBuilder builder = new();
        bool inQuotes = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                columns.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        columns.Add(builder.ToString());
        return columns;
    }

    private PresentMonCsvParseResult ParseDataLine(string line, DateTimeOffset? timestamp)
    {
        PresentMonCsvSchema schema = Schema!;
        Span<CsvFieldRange> ranges = stackalloc CsvFieldRange[(int)CsvFieldSlot.Count];
        ranges.Fill(CsvFieldRange.Missing);
        ReadFields(line.AsSpan(), schema, ranges);

        int processId = TryParseInt(GetField(line, ranges, CsvFieldSlot.ProcessId), out int parsedProcessId)
            ? parsedProcessId
            : fallbackProcessId;
        if (targetProcessId.HasValue && processId != targetProcessId.Value)
        {
            return PresentMonCsvParseResult.Filtered(processId);
        }

        double? frameTime = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.FrameTime));
        if (!frameTime.HasValue)
        {
            return PresentMonCsvParseResult.Rejected("missing-or-invalid-frame-time");
        }

        string processName = targetProcessId.HasValue
            ? fallbackProcessName
            : GetText(GetField(line, ranges, CsvFieldSlot.Application)) ?? fallbackProcessName;
        GameFrameSample sample = new()
        {
            CaptureSessionId = captureSessionId,
            Timestamp = timestamp ?? DateTimeOffset.Now,
            ProcessId = processId,
            ProcessName = processName,
            FrameTimeMs = frameTime,
            Fps = 1000d / frameTime.Value,
            CpuBusyMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.CpuBusy)),
            CpuWaitMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.CpuWait)),
            GpuLatencyMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.GpuLatency)),
            GpuTimeMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.GpuTime)),
            GpuBusyMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.GpuBusy)),
            GpuWaitMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.GpuWait)),
            RenderLatencyMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.RenderLatency)),
            DisplayLatencyMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.DisplayLatency)),
            DisplayedTimeMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.DisplayedTime)),
            ClickToPhotonLatencyMs = GetPositiveDouble(GetField(line, ranges, CsvFieldSlot.ClickToPhoton)),
            Runtime = GetCachedText(GetField(line, ranges, CsvFieldSlot.Runtime), ref cachedRuntime),
            PresentMode = GetCachedText(GetField(line, ranges, CsvFieldSlot.PresentMode), ref cachedPresentMode),
            SwapChainAddress = GetText(GetField(line, ranges, CsvFieldSlot.SwapChain)),
            FrameType = GetCachedText(GetField(line, ranges, CsvFieldSlot.FrameType), ref cachedFrameType)
        };
        SampleCreationCount++;
        return PresentMonCsvParseResult.Parsed(sample);
    }

    private double? GetPositiveDouble(ReadOnlySpan<char> value)
    {
        NumericFieldParseCount++;
        value = TrimCsvValue(value);
        return !IsMissingValue(value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            && double.IsFinite(result)
            && result > 0d
                ? result
                : null;
    }

    private static bool TryParseInt(ReadOnlySpan<char> value, out int result)
    {
        value = TrimCsvValue(value);
        if (IsMissingValue(value))
        {
            result = 0;
            return false;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static string? GetCachedText(ReadOnlySpan<char> value, ref string? cached)
    {
        value = TrimCsvValue(value);
        if (IsMissingValue(value))
        {
            return null;
        }

        if (cached is not null && value.SequenceEqual(cached.AsSpan()))
        {
            return cached;
        }

        cached = GetText(value);
        return cached;
    }

    private static string? GetText(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (IsMissingValue(value))
        {
            return null;
        }

        int escapedQuote = value.IndexOf("\"\"".AsSpan());
        if (escapedQuote < 0)
        {
            return value.ToString();
        }

        StringBuilder builder = new(value.Length - 1);
        int start = 0;
        while (escapedQuote >= 0)
        {
            builder.Append(value[start..escapedQuote]);
            builder.Append('"');
            start = escapedQuote + 2;
            int next = value[start..].IndexOf("\"\"".AsSpan());
            escapedQuote = next < 0 ? -1 : start + next;
        }

        builder.Append(value[start..]);
        return builder.ToString();
    }

    private static ReadOnlySpan<char> TrimCsvValue(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        return value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Trim()
            : value;
    }

    private static bool IsMissingValue(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        return value.IsEmpty
            || value.Equals("NA".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || value.Equals("N/A".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadFields(
        ReadOnlySpan<char> line,
        PresentMonCsvSchema schema,
        Span<CsvFieldRange> ranges)
    {
        int fieldIndex = 0;
        int fieldStart = 0;
        bool inQuotes = false;
        for (int index = 0; index <= line.Length; index++)
        {
            bool atEnd = index == line.Length;
            if (!atEnd && line[index] == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!atEnd && (line[index] != ',' || inQuotes))
            {
                continue;
            }

            int slot = schema.GetSlot(fieldIndex);
            if (slot >= 0)
            {
                ranges[slot] = new CsvFieldRange(fieldStart, index - fieldStart);
            }

            fieldIndex++;
            fieldStart = index + 1;
        }
    }

    private static ReadOnlySpan<char> GetField(
        string line,
        ReadOnlySpan<CsvFieldRange> ranges,
        CsvFieldSlot slot)
    {
        CsvFieldRange range = ranges[(int)slot];
        return range.Start < 0 ? ReadOnlySpan<char>.Empty : line.AsSpan(range.Start, range.Length);
    }

    private static bool MightBeHeader(string line)
    {
        int comma = line.IndexOf(',');
        ReadOnlySpan<char> first = (comma < 0 ? line.AsSpan() : line.AsSpan(0, comma)).Trim().Trim('"');
        return first.Equals("application".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || first.Equals("processid".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || first.Equals("pid".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || first.Equals("capturesessionid".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> columns)
    {
        int markers = 0;
        for (int index = 0; index < columns.Count; index++)
        {
            string normalized = PresentMonCsvSchema.NormalizeColumnName(columns[index]);
            if (normalized is "application" or "processid" or "pid" or "frametime"
                or "frametimems" or "msbetweenpresents" or "presentmode" or "presentruntime" or "swapchainaddress")
            {
                markers++;
            }
        }

        string first = PresentMonCsvSchema.NormalizeColumnName(columns[0]);
        return (first is "application" or "processid" or "pid" or "capturesessionid") && markers >= 2;
    }

    private readonly record struct CsvFieldRange(int Start, int Length)
    {
        public static CsvFieldRange Missing { get; } = new(-1, 0);
    }
}
