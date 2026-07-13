namespace HardwareVision.Models;

public sealed class GameProcessDetectionResult
{
    public required GameProcessInfo Process { get; init; }

    public int Score { get; init; }

    public bool IsLikelyGame { get; init; }

    public bool IsRecentForeground { get; init; }

    public bool IsStronglyNonGame { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed class GameProcessDetectionDecision
{
    public GameProcessDetectionResult? Selection { get; init; }

    public bool HasLikelyCandidates { get; init; }

    public bool IsAmbiguous { get; init; }
}
