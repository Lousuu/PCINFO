namespace HardwareVision.Models;

public enum AppTheme
{
    Classic,
    Tracework
}

public static class AppThemeParser
{
    public static AppTheme Parse(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Equals(nameof(AppTheme.Tracework), StringComparison.OrdinalIgnoreCase))
        {
            return AppTheme.Tracework;
        }

        return AppTheme.Classic;
    }

    public static string ToStorageValue(AppTheme theme) => theme switch
    {
        AppTheme.Tracework => nameof(AppTheme.Tracework),
        _ => nameof(AppTheme.Classic)
    };

    public static string NormalizeStorageValue(string? value) => ToStorageValue(Parse(value));
}
