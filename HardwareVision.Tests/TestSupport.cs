using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class TestSupport
{
    public static Action Run(Func<Task> test) => () => test().GetAwaiter().GetResult();

    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}.");
    }

    public static void Nearly(double expected, double? actual, string message, double tolerance = 0.001d)
    {
        if (!actual.HasValue || Math.Abs(expected - actual.Value) > tolerance)
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual?.ToString() ?? "null"}.");
    }

    public static T NotNull<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);

    public static void InTemporaryDirectory(Action<string> test) =>
        InTemporaryDirectory(directory =>
        {
            test(directory);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    public static async Task InTemporaryDirectory(Func<string, Task> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "HardwareVision.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await test(directory);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    public static GameSessionStartInfo StartInfo(Guid? sessionId = null, int generation = 1) => new()
    {
        CaptureSessionId = sessionId ?? Guid.NewGuid(),
        Generation = generation,
        ProcessId = 42,
        ProcessName = "synthetic-game",
        CaptureStartedAt = DateTimeOffset.UtcNow
    };

    public static GameFrameSample Frame(
        Guid sessionId,
        double elapsedSeconds = 0d,
        double fps = 60d,
        DateTimeOffset? timestamp = null) => new()
    {
        CaptureSessionId = sessionId,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        HasExplicitTimestamp = timestamp.HasValue,
        CaptureElapsedSeconds = elapsedSeconds,
        ProcessId = 42,
        ProcessName = "synthetic-game",
        Fps = fps,
        FrameTimeMs = 1000d / fps
    };

    public static Measurement Measure(string name, Action action)
    {
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        long peakBefore = Process.GetCurrentProcess().PeakWorkingSet64;
        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        long allocated = Math.Max(0L, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
        long peak = Math.Max(peakBefore, Process.GetCurrentProcess().PeakWorkingSet64);
        Measurement measurement = new(name, stopwatch.Elapsed, allocated, peak);
        Console.WriteLine(
            $"MEASURE {name}: elapsed={measurement.Elapsed.TotalMilliseconds:0.###}ms; " +
            $"allocated={measurement.AllocatedBytes}; peakWorkingSet={measurement.PeakWorkingSetBytes}");
        return measurement;
    }

    private static void TryDeleteDirectory(string directory)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 3)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }
}

internal readonly record struct Measurement(
    string Name,
    TimeSpan Elapsed,
    long AllocatedBytes,
    long PeakWorkingSetBytes);

internal sealed class CountingSensorService : ISensorService
{
    private readonly TimeSpan delay;
    private int activeCalls;
    private int maximumConcurrentCalls;
    private int pollCount;

    public CountingSensorService(TimeSpan? delay = null) => this.delay = delay ?? TimeSpan.Zero;

    public int PollCount => Volatile.Read(ref pollCount);
    public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(
        CancellationToken cancellationToken = default)
    {
        int active = Interlocked.Increment(ref activeCalls);
        int observed;
        while (active > (observed = Volatile.Read(ref maximumConcurrentCalls)))
            Interlocked.CompareExchange(ref maximumConcurrentCalls, active, observed);
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            Interlocked.Increment(ref pollCount);
            return [];
        }
        finally
        {
            Interlocked.Decrement(ref activeCalls);
        }
    }

    public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(
        CancellationToken cancellationToken = default) =>
        GetCurrentReadingsAsync(cancellationToken);

    public void Dispose()
    {
    }
}

internal sealed class NoopStartupService : IStartupService
{
    public string StatusMessage => string.Empty;
    public bool IsAdministratorStartupAvailable => true;
    public bool IsUsingFallbackStartup => false;
    public bool IsEnabled() => false;
    public void Enable() { }
    public void Disable() { }
    public void SetEnabled(bool enabled) { }
    public Task<bool> IsStartupEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task SetStartupEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
