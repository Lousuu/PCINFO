using System.IO;
using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameProcessFilter
{
    public static IReadOnlyList<GameProcessInfo> Filter(
        IEnumerable<GameProcessInfo> processes,
        string? searchText)
    {
        ArgumentNullException.ThrowIfNull(processes);

        string query = searchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return processes.ToArray();
        }

        return processes.Where(process => Matches(process, query)).ToArray();
    }

    public static bool Matches(GameProcessInfo process, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(process);

        string query = searchText?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return true;
        }

        string? fileName = GetFileName(process.FilePath);
        string? fileNameWithoutExtension = GetFileNameWithoutExtension(process.FilePath);
        string processExecutableName = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? process.ProcessName
            : $"{process.ProcessName}.exe";

        return Contains(process.DisplayName, query)
            || Contains(process.ProcessName, query)
            || Contains(processExecutableName, query)
            || Contains(process.WindowTitle, query)
            || Contains(process.FilePath, query)
            || Contains(fileName, query)
            || Contains(fileNameWithoutExtension, query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetFileName(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? GetFileNameWithoutExtension(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
