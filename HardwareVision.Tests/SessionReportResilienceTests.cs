using System.Text;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionReportResilienceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Report resilience 01 InvalidDataException keeps parsed rows", TestSupport.Run(InvalidDataKeepsRowsAsync)),
        ("Report resilience 02 InvalidDataException marks partial", TestSupport.Run(InvalidDataMarksPartialAsync)),
        ("Report resilience 03 partial keeps average FPS", TestSupport.Run(PartialKeepsAverageAsync)),
        ("Report resilience 04 partial keeps maximum FPS", TestSupport.Run(PartialKeepsMaximumAsync)),
        ("Report resilience 05 partial keeps replay diagnostics", TestSupport.Run(PartialKeepsReplayDiagnosticsAsync)),
        ("Report resilience 06 capture and replay diagnostics stay separate", TestSupport.Run(CaptureAndReplayStaySeparateAsync)),
        ("Report resilience 07 corrupt source is not modified", TestSupport.Run(CorruptSourceIsNotModifiedAsync))
    ];

    private static async Task InvalidDataKeepsRowsAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.True(fixture.Factory.ThrowCount > 0, "InvalidDataException was not injected");
        TestSupport.True(fixture.Report.ParsedFrameCount > 0 && fixture.Report.AcceptedFrameCount > 0, "parsed rows were lost");
    }

    private static async Task InvalidDataMarksPartialAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.True(fixture.Report.FrameCsvIsPartial && fixture.Report.IsPartial, "partial flag");
        TestSupport.True(fixture.Report.Warnings.Any(item => item.Contains("压缩记录只能部分读取", StringComparison.Ordinal)), "partial warning");
    }

    private static async Task PartialKeepsAverageAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.True(fixture.Report.AverageFps is > 59d and < 61d, "partial average FPS");
    }

    private static async Task PartialKeepsMaximumAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.True(fixture.Report.MaximumFps is > 59d and < 61d, "partial maximum FPS");
    }

    private static async Task PartialKeepsReplayDiagnosticsAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.True(fixture.Report.FrameQualityDiagnostics.DuplicateCaptureElapsedSampleCount >= 1L, "replay duplicate diagnostic");
    }

    private static async Task CaptureAndReplayStaySeparateAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.Equal(0L, fixture.Report.CaptureFrameQualityDiagnostics.DuplicateCaptureElapsedSampleCount, "capture diagnostic contamination");
        TestSupport.True(fixture.Report.FrameQualityDiagnostics.DuplicateCaptureElapsedSampleCount >= 1L, "replay diagnostic missing");
    }

    private static async Task CorruptSourceIsNotModifiedAsync()
    {
        using PartialFixture fixture = await LoadPartialAsync();
        TestSupport.Equal(fixture.OriginalLength, new FileInfo(fixture.Path).Length, "source length changed");
    }

    private static async Task<PartialFixture> LoadPartialAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HardwareVision.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Guid sessionId = Guid.NewGuid();
        List<string> lines = [GameCsvFormatting.Header];
        for (int index = 0; index < 40; index++)
        {
            double elapsed = index == 30 ? 29d / 60d : index / 60d;
            lines.Add(GameCsvFormatting.FormatSample(TestSupport.Frame(sessionId, elapsed, 60d,
                DateTimeOffset.UtcNow.AddSeconds(elapsed))));
        }
        byte[] payload = new UTF8Encoding(true).GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine);
        string path = Path.Combine(directory, "damaged.csv.gz");
        await File.WriteAllBytesAsync(path, payload);
        ThrowingFrameStreamFactory factory = new(payload);
        GameSessionRecordInfo record = new()
        {
            GameName = "damaged",
            StartedAt = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromSeconds(1),
            IsComplete = false,
            CsvPath = path
        };
        GameSessionReport report = await new GameSessionReportService(factory).LoadAsync(record);
        return new PartialFixture(report, factory, path, payload.LongLength, directory);
    }

    private sealed record PartialFixture(
        GameSessionReport Report,
        ThrowingFrameStreamFactory Factory,
        string Path,
        long OriginalLength,
        string Directory) : IDisposable
    {
        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class ThrowingFrameStreamFactory(byte[] payload) : IGameSessionFrameStreamFactory
    {
        public int ThrowCount { get; private set; }

        public Stream OpenRead(string path) => new ThrowAtEndStream(payload, () => ThrowCount++);

        public StreamReader OpenTextReader(string path, int bufferSize = 64 * 1024) => new(
            OpenRead(path),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 128,
            leaveOpen: false);

        public bool IsCompressed(string path) => true;
    }

    private sealed class ThrowAtEndStream(byte[] payload, Action onThrow) : MemoryStream(payload, writable: false)
    {
        private void Throw()
        {
            onThrow();
            throw new InvalidDataException("deterministic damaged GZip payload");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= Length) Throw();
            return base.Read(buffer, offset, Math.Min(count, 128));
        }

        public override int Read(Span<byte> buffer)
        {
            if (Position >= Length) Throw();
            return base.Read(buffer[..Math.Min(buffer.Length, 128)]);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= Length) Throw();
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 128)], cancellationToken);
        }
    }
}
