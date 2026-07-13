namespace HardwareVision.Models;

public sealed class GameSessionStartInfo
{
    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public int ProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string? WindowTitle { get; init; }

    public string? ExecutablePath { get; init; }

    public DateTimeOffset CaptureStartedAt { get; init; } = DateTimeOffset.Now;
}
