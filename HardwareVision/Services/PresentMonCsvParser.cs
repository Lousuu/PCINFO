using System.Globalization;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public enum PresentMonCsvParseKind
{
    Ignored,
    HeaderAccepted,
    SchemaMismatch,
    Sample,
    Rejected
}

public sealed class PresentMonCsvParseResult
{
    private PresentMonCsvParseResult(
        PresentMonCsvParseKind kind,
        GameFrameSample? sample = null,
        string? reason = null,
        bool isDataRow = false)
    {
        Kind = kind;
        Sample = sample;
        Reason = reason;
        IsDataRow = isDataRow;
    }

    public PresentMonCsvParseKind Kind { get; }

    public GameFrameSample? Sample { get; }

    public string? Reason { get; }

    public bool IsDataRow { get; }

    public static PresentMonCsvParseResult Ignored(string? reason = null) =>
        new(PresentMonCsvParseKind.Ignored, reason: reason);

    public static PresentMonCsvParseResult HeaderAccepted() =>
        new(PresentMonCsvParseKind.HeaderAccepted);

    public static PresentMonCsvParseResult SchemaMismatch(string reason) =>
        new(PresentMonCsvParseKind.SchemaMismatch, reason: reason);

    public static PresentMonCsvParseResult Parsed(GameFrameSample sample) =>
        new(PresentMonCsvParseKind.Sample, sample, isDataRow: true);

    public static PresentMonCsvParseResult Rejected(string reason) =>
        new(PresentMonCsvParseKind.Rejected, reason: reason, isDataRow: true);
}

public sealed class PresentMonCsvSchema
{
    private static readonly string[] FrameTimeColumns =
    [
        "frametime",
        "msbetweenpresents",
        "msbetweenpresent",
        "frametimems",
        "msperframe"
    ];

    private readonly Dictionary<string, int> columnIndexes;

    internal PresentMonCsvSchema(IReadOnlyList<string> rawColumns)
    {
        RawColumns = rawColumns.ToArray();
        NormalizedColumns = rawColumns.Select(NormalizeColumnName).ToArray();
        columnIndexes = NormalizedColumns
            .Select((name, index) => new { Name = name, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> RawColumns { get; }

    public IReadOnlyList<string> NormalizedColumns { get; }

    public bool HasFrameTimeColumn => HasAny(FrameTimeColumns);

    internal bool HasAny(IEnumerable<string> names)
    {
        return names.Any(name => columnIndexes.ContainsKey(NormalizeColumnName(name)));
    }

    internal string? GetString(IReadOnlyList<string> columns, params string[] names)
    {
        foreach (string name in names)
        {
            if (columnIndexes.TryGetValue(NormalizeColumnName(name), out int index)
                && index >= 0
                && index < columns.Count)
            {
                string value = columns[index].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    public static string NormalizeColumnName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

public sealed class PresentMonCsvParser
{
    private static readonly HashSet<string> HeaderMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "application",
        "processid",
        "pid",
        "frametime",
        "msbetweenpresents",
        "presentmode",
        "presentruntime",
        "swapchainaddress"
    };

    private readonly Guid captureSessionId;
    private readonly int fallbackProcessId;
    private readonly string fallbackProcessName;

    public PresentMonCsvParser(Guid captureSessionId, int fallbackProcessId, string fallbackProcessName)
    {
        this.captureSessionId = captureSessionId;
        this.fallbackProcessId = fallbackProcessId;
        this.fallbackProcessName = fallbackProcessName;
    }

    public PresentMonCsvSchema? Schema { get; private set; }

    public PresentMonCsvParseResult ParseLine(string? line, DateTimeOffset? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains(',', StringComparison.Ordinal))
        {
            return PresentMonCsvParseResult.Ignored("not-csv");
        }

        string[] columns = SplitCsv(line).ToArray();
        if (columns.Length < 2)
        {
            return PresentMonCsvParseResult.Ignored("too-few-columns");
        }

        if (LooksLikeHeader(columns))
        {
            Schema = new PresentMonCsvSchema(columns);
            return Schema.HasFrameTimeColumn
                ? PresentMonCsvParseResult.HeaderAccepted()
                : PresentMonCsvParseResult.SchemaMismatch(
                    "缺少 FrameTime / MsBetweenPresents（或兼容别名）字段");
        }

        if (Schema is null)
        {
            return PresentMonCsvParseResult.Ignored("awaiting-header");
        }

        double? frameTime = GetPositiveDouble(
            columns,
            "frametime",
            "msbetweenpresents",
            "msbetweenpresent",
            "frametimems",
            "msperframe");
        if (!frameTime.HasValue)
        {
            return PresentMonCsvParseResult.Rejected("missing-or-invalid-frame-time");
        }

        int processId = GetInt(columns, "processid", "pid") ?? fallbackProcessId;
        string processName = GetString(columns, "application", "processname", "app") ?? fallbackProcessName;
        GameFrameSample sample = new()
        {
            CaptureSessionId = captureSessionId,
            Timestamp = timestamp ?? DateTimeOffset.Now,
            ProcessId = processId,
            ProcessName = processName,
            FrameTimeMs = frameTime,
            Fps = 1000d / frameTime.Value,
            CpuBusyMs = GetPositiveDouble(columns, "cpubusy", "mscpubusy", "cpubusyms"),
            CpuWaitMs = GetPositiveDouble(columns, "cpuwait", "mscpuwait", "cpuwaitms"),
            GpuLatencyMs = GetPositiveDouble(columns, "gpulatency", "msgpulatency", "gpulatencyms"),
            GpuTimeMs = GetPositiveDouble(columns, "gputime", "msgputime", "gputimems"),
            GpuBusyMs = GetPositiveDouble(columns, "gpubusy", "msgpubusy", "gpubusyms", "msgpuactive"),
            GpuWaitMs = GetPositiveDouble(columns, "gpuwait", "msgpuwait", "gpuwaitms"),
            RenderLatencyMs = GetPositiveDouble(
                columns,
                "msuntilrendercomplete",
                "msrenderpresentlatency",
                "renderlatencyms"),
            DisplayLatencyMs = GetPositiveDouble(
                columns,
                "displaylatency",
                "msdisplaylatency",
                "msuntildisplayed"),
            DisplayedTimeMs = GetPositiveDouble(columns, "displayedtime", "msdisplayedtime"),
            ClickToPhotonLatencyMs = GetPositiveDouble(
                columns,
                "clicktophotonlatency",
                "msclicktophotonlatency",
                "clicktophotonlatencyms",
                "mspclatency"),
            Runtime = GetString(columns, "presentruntime", "runtime"),
            PresentMode = GetString(columns, "presentmode"),
            SwapChainAddress = GetString(columns, "swapchainaddress", "swapchain"),
            FrameType = GetString(columns, "frametype"),
            RawLine = line
        };

        return PresentMonCsvParseResult.Parsed(sample);
    }

    public static IReadOnlyList<string> ParseColumns(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return SplitCsv(line).ToArray();
    }

    private string? GetString(IReadOnlyList<string> columns, params string[] names)
    {
        string? value = Schema?.GetString(columns, names);
        return IsMissingValue(value) ? null : value;
    }

    private double? GetPositiveDouble(IReadOnlyList<string> columns, params string[] names)
    {
        string? value = GetString(columns, names);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            && double.IsFinite(result)
            && result > 0d
                ? result
                : null;
    }

    private int? GetInt(IReadOnlyList<string> columns, params string[] names)
    {
        string? value = GetString(columns, names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
            ? result
            : null;
    }

    private static bool LooksLikeHeader(IEnumerable<string> columns)
    {
        string[] normalized = columns
            .Select(PresentMonCsvSchema.NormalizeColumnName)
            .ToArray();
        if (normalized.Length == 0)
        {
            return false;
        }

        return HeaderMarkers.Contains(normalized[0])
            && normalized.Count(HeaderMarkers.Contains) >= 2;
    }

    private static bool IsMissingValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Equals("NA", StringComparison.OrdinalIgnoreCase)
            || value.Equals("N/A", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitCsv(string line)
    {
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
                yield return builder.ToString();
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        yield return builder.ToString();
    }
}
