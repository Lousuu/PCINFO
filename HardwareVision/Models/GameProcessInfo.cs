namespace HardwareVision.Models;

public sealed class GameProcessInfo
{
    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? WindowTitle { get; init; }

    public string? FilePath { get; init; }

    public DateTimeOffset? StartTimeUtc { get; init; }

    public bool IsRunning { get; init; } = true;

    public bool HasVisibleMainWindow { get; init; }

    public int DetectionScore { get; init; }

    public bool IsLikelyGame { get; init; }

    public string? DetectionReason { get; init; }

    public bool IsRecentForeground { get; init; }

    public string DisplayLabel => IsLikelyGame ? $"{DisplayName}  [可能是游戏]" : DisplayName;

    public string? ToolTip => string.IsNullOrWhiteSpace(FilePath) ? WindowTitle : FilePath;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(DisplayName) ? $"{ProcessName} ({ProcessId})" : DisplayName;
    }
}
