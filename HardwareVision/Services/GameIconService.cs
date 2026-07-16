using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HardwareVision.Services;

public interface IGameIconService
{
    Task<ImageSource?> LoadAsync(string? executablePath, CancellationToken cancellationToken = default);
}

public sealed class GameIconService : IGameIconService
{
    private readonly int capacity;
    private readonly object cacheLock = new();
    private readonly Dictionary<CacheKey, CacheEntry> cache = [];
    private readonly LinkedList<CacheKey> lru = [];
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> inFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private int extractionCount;

    public GameIconService(int capacity = 32)
    {
        this.capacity = Math.Clamp(capacity, 1, 64);
    }

    public static GameIconService Shared { get; } = new(32);

    internal int CacheCount { get { lock (cacheLock) return cache.Count; } }
    internal int ExtractionCount => Volatile.Read(ref extractionCount);

    public async Task<ImageSource?> LoadAsync(string? executablePath, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeLocalPath(executablePath, out string? normalized) || normalized is null) return null;
        Task<ImageSource?> task = inFlight.GetOrAdd(normalized, path => LoadCoreAsync(path));
        try
        {
            return cancellationToken.CanBeCanceled
                ? await task.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await task.ConfigureAwait(false);
        }
        finally
        {
            if (task.IsCompleted &&
                inFlight.TryGetValue(normalized, out Task<ImageSource?>? current) &&
                ReferenceEquals(current, task))
            {
                inFlight.TryRemove(normalized, out _);
            }
        }
    }

    private Task<ImageSource?> LoadCoreAsync(string path) => Task.Run(() =>
    {
        try
        {
            FileInfo file = new(path);
            if (!file.Exists) return null;
            CacheKey key = new(path, file.Length, file.LastWriteTimeUtc.Ticks);
            lock (cacheLock)
            {
                if (cache.TryGetValue(key, out CacheEntry? cached))
                {
                    lru.Remove(cached.Node);
                    lru.AddLast(cached.Node);
                    return cached.Image;
                }
            }

            Interlocked.Increment(ref extractionCount);
            using System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            BitmapSource image = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            image.Freeze();
            lock (cacheLock)
            {
                RemoveStalePathEntries(path, key);
                LinkedListNode<CacheKey> node = lru.AddLast(key);
                cache[key] = new CacheEntry(image, node);
                while (cache.Count > capacity && lru.First is not null)
                {
                    CacheKey oldest = lru.First.Value;
                    lru.RemoveFirst();
                    cache.Remove(oldest);
                }
            }
            return image;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception
            or ExternalException
            or NotSupportedException)
        {
            return null;
        }
    });

    private void RemoveStalePathEntries(string path, CacheKey current)
    {
        CacheKey[] stale = cache.Keys
            .Where(key => string.Equals(key.Path, path, StringComparison.OrdinalIgnoreCase) && key != current)
            .ToArray();
        foreach (CacheKey key in stale)
        {
            lru.Remove(cache[key].Node);
            cache.Remove(key);
        }
    }

    private static bool TryNormalizeLocalPath(string? path, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)) return false;
        try
        {
            normalized = Path.GetFullPath(path);
            return !new Uri(normalized).IsUnc;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or UriFormatException)
        {
            return false;
        }
    }

    private readonly record struct CacheKey(string Path, long Length, long LastWriteTicks);
    private sealed record CacheEntry(ImageSource Image, LinkedListNode<CacheKey> Node);
}
