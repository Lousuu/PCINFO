using System.Text.Json;
using System.Text.Json.Serialization;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionPathSecurityTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Session path 01 normal file resolves", NormalFileResolves),
        ("Session path 02 traversal is rejected", () => Rejected("..\\other.csv", SessionFileKind.FrameCsv)),
        ("Session path 03 absolute path is rejected", () => Rejected("C:\\Windows\\file.csv", SessionFileKind.FrameCsv)),
        ("Session path 04 UNC path is rejected", () => Rejected("\\\\server\\share\\file.csv", SessionFileKind.FrameCsv)),
        ("Session path 05 subdirectory is rejected", () => Rejected("child\\file.csv", SessionFileKind.FrameCsv)),
        ("Session path 06 expected suffix is case insensitive", ExpectedSuffixIsCaseInsensitive),
        ("Session path 07 unsupported suffix is rejected", () => Rejected("session.txt", SessionFileKind.FrameCsv)),
        ("Session path 08 malicious summary remains partial", TestSupport.Run(MaliciousSummaryRemainsPartialAsync)),
        ("Session path 09 index traversal is rejected", RelativeTraversalIsRejected)
    ];

    private static void NormalFileResolves()
    {
        TestSupport.InTemporaryDirectory(directory =>
        {
            bool success = SessionFilePathResolver.TryResolve(
                directory,
                "game.summary.json",
                SessionFileKind.SummaryJson,
                out string? fullPath,
                out string? warning);
            TestSupport.True(success, warning ?? "normal path rejected");
            TestSupport.Equal(
                Path.Combine(Path.GetFullPath(directory), "game.summary.json"),
                fullPath,
                "resolved path");
        });
    }

    private static void Rejected(string name, SessionFileKind kind)
    {
        bool success = SessionFilePathResolver.TryResolve(
            Path.GetTempPath(), name, kind, out string? path, out string? warning);
        TestSupport.False(success, "unsafe path accepted");
        TestSupport.True(path is null, "unsafe path returned a target");
        TestSupport.True(!string.IsNullOrWhiteSpace(warning), "rejection warning missing");
    }

    private static void ExpectedSuffixIsCaseInsensitive()
    {
        bool success = SessionFilePathResolver.TryResolve(
            Path.GetTempPath(), "SESSION.CSV", SessionFileKind.FrameCsv, out _, out _);
        TestSupport.True(success, "uppercase expected suffix rejected");
    }

    private static void RelativeTraversalIsRejected()
    {
        bool success = SessionFilePathResolver.TryResolveRelativePath(
            Path.GetTempPath(), "2026/../outside.csv", SessionFileKind.FrameCsv, out _, out string? warning);
        TestSupport.False(success, "relative traversal accepted");
        TestSupport.True(!string.IsNullOrWhiteSpace(warning), "relative traversal warning missing");
    }

    private static Task MaliciousSummaryRemainsPartialAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        Guid sessionId = Guid.NewGuid();
        string csvPath = Path.Combine(directory, "safe.csv");
        string summaryPath = Path.Combine(directory, "safe.summary.json");
        string outside = Path.Combine(Path.GetDirectoryName(directory)!, "outside.hardware-timeline.csv");
        await File.WriteAllTextAsync(csvPath, GameCsvFormatting.Header + Environment.NewLine);
        await File.WriteAllTextAsync(outside, "must-not-be-read");
        try
        {
            GameSessionSummary summary = new()
            {
                CaptureSessionId = sessionId,
                CaptureGeneration = 1,
                CaptureStartedAt = DateTimeOffset.UtcNow,
                CaptureEndedAt = DateTimeOffset.UtcNow,
                CsvFileName = Path.GetFileName(csvPath),
                HardwareTimelineCsvFileName = "..\\outside.hardware-timeline.csv"
            };
            JsonSerializerOptions options = new() { Converters = { new JsonStringEnumConverter() } };
            await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(summary, options));
            GameSessionRecordInfo record = new()
            {
                GameName = "safe",
                StartedAt = summary.CaptureStartedAt,
                Duration = TimeSpan.Zero,
                IsComplete = true,
                CsvPath = csvPath,
                SummaryPath = summaryPath,
                HardwareTimelineCsvPath = outside
            };

            GameSessionReport report = await new GameSessionReportService().LoadAsync(record);
            TestSupport.True(report.Warnings.Count > 0, "unsafe related file did not create a warning");
            TestSupport.Equal(
                SessionAuxiliaryFileStatus.Unavailable,
                report.HardwareTimelineFileStatus,
                "unsafe timeline status");
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    });
}
