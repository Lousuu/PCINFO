namespace HardwareVision.Models;

public sealed class GameProcessInfo
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? WindowTitle { get; init; }

    public string? FilePath { get; init; }

    public string? ToolTip => string.IsNullOrWhiteSpace(FilePath) ? WindowTitle : FilePath;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName) ? $"{ProcessName} ({ProcessId})" : DisplayName;
    }
}
