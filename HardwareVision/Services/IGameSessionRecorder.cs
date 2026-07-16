using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IGameSessionRecorder : IDisposable, IAsyncDisposable
{
    event EventHandler<GameSessionRecorderStateChangedEventArgs>? StateChanged;

    string RootDirectory { get; }

    bool IsRecording { get; }

    SessionFinalizationState FinalizationState => IsRecording
        ? SessionFinalizationState.Recording
        : SessionFinalizationState.Idle;

    SessionFinalizationResult? LastFinalizationResult => null;

    string RecordingStatusText { get; }

    string? CurrentFilePath { get; }

    long DroppedSampleCount { get; }

    Task RecoverIncompleteSessionsAsync(CancellationToken cancellationToken = default);

    Task StartAsync(GameSessionStartInfo startInfo, CancellationToken cancellationToken = default);

    bool TryRecord(GameFrameSample sample, Guid captureSessionId, int generation);

    void SetFrameQualityDiagnostics(
        Guid captureSessionId,
        int generation,
        GameFrameQualityDiagnostics diagnostics)
    {
    }

    Task<GameSessionRecordInfo?> CompleteAsync(
        GameSessionEndReason reason,
        bool completedNormally,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameSessionRecordInfo>> GetRecentRecordsAsync(
        int maximumCount = 10,
        CancellationToken cancellationToken = default);

    async Task<GameSessionRecordPage> GetRecordsPageAsync(
        int offset,
        int pageSize,
        string? snapshotToken = null,
        CancellationToken cancellationToken = default)
    {
        int normalizedOffset = Math.Max(0, offset);
        int normalizedPageSize = Math.Max(1, pageSize);
        IReadOnlyList<GameSessionRecordInfo> records = await GetRecentRecordsAsync(
            normalizedOffset + normalizedPageSize,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<GameSessionRecordInfo> page = records.Skip(normalizedOffset).Take(normalizedPageSize).ToArray();
        return new GameSessionRecordPage
        {
            Records = page,
            Offset = normalizedOffset,
            PageSize = normalizedPageSize,
            TotalCount = records.Count,
            HasMore = records.Count >= normalizedOffset + normalizedPageSize,
            SnapshotToken = snapshotToken
        };
    }

    Task<long> GetDirectorySizeAsync(CancellationToken cancellationToken = default);

    async Task<GameSessionDirectorySizeInfo> GetDirectorySizeInfoAsync(CancellationToken cancellationToken = default)
    {
        long bytes = await GetDirectorySizeAsync(cancellationToken).ConfigureAwait(false);
        return new GameSessionDirectorySizeInfo { Bytes = bytes };
    }

    Task<long> RecalculateDirectorySizeAsync(CancellationToken cancellationToken = default) =>
        GetDirectorySizeAsync(cancellationToken);
}
