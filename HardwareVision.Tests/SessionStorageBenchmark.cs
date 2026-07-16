using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class SessionStorageBenchmark
{
    public static int Run()
    {
        (int Fps, int Hours)[] scenarios = [(60, 1), (120, 1), (240, 1), (120, 3), (240, 3)];
        foreach ((int fps, int hours) in scenarios)
        {
            Measurement plain = Measure(fps, hours, compressed: false);
            Measurement gzip = Measure(fps, hours, compressed: true);
            double sizePercent = 100d * gzip.Bytes / plain.Bytes;
            Console.WriteLine(
                $"STORAGE fps={fps}; hours={hours}; rows={plain.Rows}; plainBytes={plain.Bytes}; gzipBytes={gzip.Bytes}; " +
                $"sizePercent={sizePercent:0.00}; reductionPercent={100d - sizePercent:0.00}; " +
                $"plainElapsedMs={plain.Elapsed.TotalMilliseconds:0.0}; gzipElapsedMs={gzip.Elapsed.TotalMilliseconds:0.0}; " +
                $"plainCpuMs={plain.Cpu.TotalMilliseconds:0.0}; gzipCpuMs={gzip.Cpu.TotalMilliseconds:0.0}; " +
                $"plainAllocated={plain.Allocated}; gzipAllocated={gzip.Allocated}");
        }
        return 0;
    }

    private static Measurement Measure(int fps, int hours, bool compressed)
    {
        int rows = checked(fps * 3600 * hours);
        CountingStream sink = new();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Stream output = compressed
            ? new GZipStream(sink, CompressionLevel.Fastest, leaveOpen: true)
            : sink;
        using (output)
        using (StreamWriter writer = new(output, new UTF8Encoding(true), 64 * 1024, leaveOpen: true))
        {
            writer.WriteLine(GameCsvFormatting.Header);
            Guid sessionId = new("11111111-2222-3333-4444-555555555555");
            DateTimeOffset startedAt = DateTimeOffset.Parse("2026-07-16T00:00:00+00:00");
            double frameTime = 1000d / fps;
            for (int index = 0; index < rows; index++)
            {
                double elapsed = index / (double)fps;
                writer.WriteLine(GameCsvFormatting.FormatSample(new GameFrameSample
                {
                    CaptureSessionId = sessionId,
                    Timestamp = startedAt.AddSeconds(elapsed),
                    HasExplicitTimestamp = true,
                    CaptureElapsedSeconds = elapsed,
                    ProcessId = 4242,
                    ProcessName = "Synthetic-Game-Win64-Shipping",
                    SwapChainAddress = "0x00000000ABCDEF12",
                    Fps = fps,
                    FrameTimeMs = frameTime,
                    CpuBusyMs = frameTime * 0.42d,
                    CpuWaitMs = frameTime * 0.08d,
                    GpuLatencyMs = frameTime * 0.7d,
                    GpuTimeMs = frameTime * 0.65d,
                    GpuBusyMs = frameTime * 0.6d,
                    GpuWaitMs = frameTime * 0.05d,
                    RenderLatencyMs = frameTime * 0.8d,
                    DisplayLatencyMs = frameTime * 1.4d,
                    DisplayedTimeMs = frameTime,
                    Runtime = "DXGI",
                    PresentMode = "Hardware: Independent Flip",
                    FrameType = "Application"
                }));
            }
        }
        stopwatch.Stop();
        TimeSpan cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
        long allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;
        return new Measurement(rows, sink.BytesWritten, stopwatch.Elapsed, cpu, allocated);
    }

    private readonly record struct Measurement(int Rows, long Bytes, TimeSpan Elapsed, TimeSpan Cpu, long Allocated);

    private sealed class CountingStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
