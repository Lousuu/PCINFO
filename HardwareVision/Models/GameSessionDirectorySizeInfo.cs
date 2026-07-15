namespace HardwareVision.Models;

public sealed class GameSessionDirectorySizeInfo
{
    public long? Bytes { get; init; }

    public bool IsCalculating { get; init; }

    public bool IsStale { get; init; }
}
