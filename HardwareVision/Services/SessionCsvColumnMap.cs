namespace HardwareVision.Services;

internal sealed class SessionCsvColumnMap
{
    private readonly Dictionary<string, int> indexes;

    private SessionCsvColumnMap(Dictionary<string, int> indexes)
    {
        this.indexes = indexes;
    }

    public static SessionCsvColumnMap Create(string header)
    {
        IReadOnlyList<string> columns = PresentMonCsvParser.ParseColumns(header);
        Dictionary<string, int> indexes = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < columns.Count; index++)
        {
            string normalized = PresentMonCsvSchema.NormalizeColumnName(columns[index]);
            if (normalized.Length > 0) indexes.TryAdd(normalized, index);
        }

        return new SessionCsvColumnMap(indexes);
    }

    public bool Has(params string[] names) => FindIndex(names) >= 0;

    public string Get(IReadOnlyList<string> fields, params string[] names)
    {
        int index = FindIndex(names);
        return index >= 0 && index < fields.Count ? fields[index] : string.Empty;
    }

    public bool HasRequired(out string? missing, params string[] names)
    {
        for (int index = 0; index < names.Length; index++)
        {
            if (!Has(names[index]))
            {
                missing = names[index];
                return false;
            }
        }

        missing = null;
        return true;
    }

    private int FindIndex(IReadOnlyList<string> names)
    {
        for (int index = 0; index < names.Count; index++)
        {
            string normalized = PresentMonCsvSchema.NormalizeColumnName(names[index]);
            if (indexes.TryGetValue(normalized, out int result)) return result;
        }

        return -1;
    }
}
