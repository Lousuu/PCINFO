namespace HardwareVision.Models;

public enum SessionFinalizationState
{
    Idle,
    Recording,
    Finalizing,
    Completed,
    Failed
}

public sealed class SessionFinalizationStepInfo
{
    public required string Name { get; init; }

    public bool Succeeded { get; init; }

    public string? Error { get; init; }
}

public sealed class SessionFinalizationResult
{
    public SessionFinalizationState State { get; init; }

    public GameSessionRecordInfo? Record { get; init; }

    public IReadOnlyList<SessionFinalizationStepInfo> Steps { get; init; } = [];
}
