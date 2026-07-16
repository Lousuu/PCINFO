using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionFinalizationTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session finalization 01 concurrent callers single-flight", TestSupport.Run(ConcurrentCallersSingleFlightAsync)),
        ("Session finalization 02 caller cancellation preserves finalizer", TestSupport.Run(CallerCancellationPreservesFinalizerAsync)),
        ("Session finalization 03 final result records four file steps", TestSupport.Run(FinalResultRecordsStepsAsync)),
        ("Session finalization 04 move collision preserves partial", TestSupport.Run(MoveCollisionPreservesPartialAsync)),
        ("Session finalization 05 summary failure rolls back frame CSV", TestSupport.Run(SummaryFailureRollsBackAsync)),
        ("Session finalization 06 dispose after completion does not duplicate", TestSupport.Run(DisposeAfterCompletionDoesNotDuplicateAsync))
    ];

    private static Task ConcurrentCallersSingleFlightAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory, 8);
        GameSessionStartInfo start = TestSupport.StartInfo();
        await recorder.StartAsync(start);
        TestSupport.True(recorder.TryRecord(TestSupport.Frame(start.CaptureSessionId), start.CaptureSessionId, start.Generation), "sample rejected");

        Task<GameSessionRecordInfo?>[] tasks = Enumerable.Range(0, 16)
            .Select(_ => recorder.CompleteAsync(GameSessionEndReason.UserStopped, true))
            .ToArray();
        GameSessionRecordInfo?[] records = await Task.WhenAll(tasks);
        GameSessionRecordInfo first = TestSupport.NotNull(records[0], "completion result missing");
        TestSupport.True(records.All(record => ReferenceEquals(first, record)), "callers did not share one result");
        TestSupport.Equal(1, Directory.GetFiles(directory, "*.summary.json", SearchOption.AllDirectories).Length, "summary count");
    });

    private static Task CallerCancellationPreservesFinalizerAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory, 8);
        GameSessionStartInfo start = TestSupport.StartInfo();
        await recorder.StartAsync(start);
        recorder.TryRecord(TestSupport.Frame(start.CaptureSessionId), start.CaptureSessionId, start.Generation);
        using CancellationTokenSource canceledCaller = new();
        canceledCaller.Cancel();
        try
        {
            await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true, canceledCaller.Token);
            throw new InvalidOperationException("canceled caller unexpectedly completed");
        }
        catch (OperationCanceledException)
        {
        }

        GameSessionRecordInfo? record = await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
        TestSupport.True(record?.IsComplete == true, "background finalizer was canceled by the caller");
        TestSupport.True(File.Exists(record!.SummaryPath), "summary was not finalized");
    });

    private static Task FinalResultRecordsStepsAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory, 8);
        GameSessionStartInfo start = TestSupport.StartInfo();
        await recorder.StartAsync(start);
        recorder.TryRecord(TestSupport.Frame(start.CaptureSessionId), start.CaptureSessionId, start.Generation);
        await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);

        SessionFinalizationResult result = TestSupport.NotNull(recorder.LastFinalizationResult, "finalization result missing");
        TestSupport.Equal(SessionFinalizationState.Completed, result.State, "finalization state");
        foreach (string name in new[] { "frame-csv", "hardware-timeline", "performance-limit-csv", "summary-json" })
        {
            SessionFinalizationStepInfo? step = result.Steps.FirstOrDefault(item => item.Name == name);
            TestSupport.True(step?.Succeeded == true, $"successful {name} step missing");
        }
    });

    private static Task MoveCollisionPreservesPartialAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory, 8);
        GameSessionStartInfo start = TestSupport.StartInfo();
        await recorder.StartAsync(start);
        recorder.TryRecord(TestSupport.Frame(start.CaptureSessionId), start.CaptureSessionId, start.Generation);
        string partial = TestSupport.NotNull(recorder.CurrentFilePath, "partial path missing");
        string final = partial[..^".partial".Length];
        await File.WriteAllTextAsync(final, "pre-existing");

        GameSessionRecordInfo? record = await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
        TestSupport.False(record?.IsComplete == true, "collision was reported as complete");
        TestSupport.Equal("pre-existing", await File.ReadAllTextAsync(final), "existing destination was overwritten");
        TestSupport.True(File.Exists(partial), "recoverable partial was lost");
    });

    private static Task SummaryFailureRollsBackAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        await using CsvGameSessionRecorder recorder = new(directory, 8);
        GameSessionStartInfo start = TestSupport.StartInfo();
        await recorder.StartAsync(start);
        recorder.TryRecord(TestSupport.Frame(start.CaptureSessionId), start.CaptureSessionId, start.Generation);
        string partial = TestSupport.NotNull(recorder.CurrentFilePath, "partial path missing");
        string final = partial[..^".partial".Length];
        string summaryPath = Path.ChangeExtension(final, ".summary.json");
        Directory.CreateDirectory(summaryPath);

        GameSessionRecordInfo? record = await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
        TestSupport.False(record?.IsComplete == true, "summary failure was reported as complete");
        TestSupport.True(File.Exists(partial), "final frame CSV was not rolled back");
        TestSupport.False(File.Exists(final), "orphan final frame CSV remained");
    });

    private static Task DisposeAfterCompletionDoesNotDuplicateAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        CsvGameSessionRecorder recorder = new(directory, 8);
        GameSessionStartInfo start = TestSupport.StartInfo();
        await recorder.StartAsync(start);
        recorder.TryRecord(TestSupport.Frame(start.CaptureSessionId), start.CaptureSessionId, start.Generation);
        await recorder.CompleteAsync(GameSessionEndReason.UserStopped, true);
        await recorder.DisposeAsync();
        TestSupport.Equal(1, Directory.GetFiles(directory, "*.summary.json", SearchOption.AllDirectories).Length, "summary count after dispose");
        TestSupport.Equal(1, File.ReadAllLines(Path.Combine(directory, GameSessionCatalog.FileName)).Length, "index count after dispose");
    });
}
