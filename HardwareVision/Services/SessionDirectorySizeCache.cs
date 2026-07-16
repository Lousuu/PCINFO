using System.IO;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

internal sealed class SessionDirectorySizeCache
{
    private readonly string rootDirectory;
    private readonly object syncRoot = new();
    private Task<long>? scanTask;
    private long? bytes;
    private bool isStale;
    private int fullScanCount;

    public SessionDirectorySizeCache(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
    }

    public int FullScanCount => Volatile.Read(ref fullScanCount);

    public void StartInitialScan() => _ = GetOrStartScan(force: false);

    public GameSessionDirectorySizeInfo GetInfo()
    {
        lock (syncRoot)
        {
            return new GameSessionDirectorySizeInfo
            {
                Bytes = bytes,
                IsCalculating = scanTask is { IsCompleted: false },
                IsStale = isStale
            };
        }
    }

    public async Task<long> GetExactSizeAsync(bool force, CancellationToken cancellationToken)
    {
        Task<long> task = GetOrStartScan(force);
        return cancellationToken.CanBeCanceled
            ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
            : await task.ConfigureAwait(false);
    }

    public void AddBytes(long delta)
    {
        if (delta == 0L) return;
        lock (syncRoot)
        {
            if (bytes.HasValue) bytes = Math.Max(0L, bytes.Value + delta);
            if (scanTask is { IsCompleted: false }) isStale = true;
        }
    }

    public void MarkStale()
    {
        lock (syncRoot) isStale = true;
    }

    private Task<long> GetOrStartScan(bool force)
    {
        lock (syncRoot)
        {
            if (!force && scanTask is not null) return scanTask;
            if (scanTask is { IsCompleted: false }) return scanTask;
            scanTask = Task.Run(ScanCore);
            return scanTask;
        }
    }

    private long ScanCore()
    {
        Interlocked.Increment(ref fullScanCount);
        long total = 0L;
        int skippedFiles = 0;
        try
        {
            if (Directory.Exists(rootDirectory))
            {
                foreach (string path in Directory.EnumerateFiles(
                    rootDirectory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    }))
                {
                    try { total += new FileInfo(path).Length; }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        skippedFiles++;
                    }
                }
            }

            if (skippedFiles > 0)
            {
                AppLogger.LogKeyEvent($"Game-session directory size scan skipped files | count={skippedFiles}");
            }

            lock (syncRoot)
            {
                bytes = total;
                isStale = false;
            }
            return total;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLogger.LogError("Game-session directory size scan failed.", exception,
                "session-directory-size", TimeSpan.FromMinutes(5));
            lock (syncRoot)
            {
                isStale = bytes.HasValue;
                return bytes ?? 0L;
            }
        }
    }
}
