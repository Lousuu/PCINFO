using System.Windows.Media;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class GameIconServiceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Game icon 01 loads and freezes image off caller path", TestSupport.Run(LoadsAndFreezesAsync)),
        ("Game icon 02 same path is single-flight", TestSupport.Run(SamePathIsSingleFlightAsync)),
        ("Game icon 03 cache capacity is bounded", TestSupport.Run(CacheCapacityIsBoundedAsync)),
        ("Game icon 04 file metadata invalidates cache", TestSupport.Run(FileMetadataInvalidatesCacheAsync)),
        ("Game icon 05 UNC is skipped by default", TestSupport.Run(UncIsSkippedAsync)),
        ("Game icon 06 cancellation only cancels caller wait", TestSupport.Run(CancellationOnlyCancelsCallerAsync)),
        ("Game icon 07 missing executable degrades quietly", TestSupport.Run(MissingExecutableDegradesQuietlyAsync))
    ];

    private static Task LoadsAndFreezesAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string executable = CopyExecutable(directory, "one.exe");
        GameIconService service = new(4);
        ImageSource? image = await service.LoadAsync(executable);
        TestSupport.True(image is not null, "associated icon was not loaded");
        TestSupport.True(image!.IsFrozen, "cached ImageSource was not frozen");
    });

    private static Task SamePathIsSingleFlightAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string executable = CopyExecutable(directory, "single-flight.exe");
        GameIconService service = new(4);
        ImageSource?[] images = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(_ => service.LoadAsync(executable)));
        TestSupport.True(images.All(image => image is not null), "a concurrent icon load failed");
        TestSupport.Equal(1, service.ExtractionCount, "icon extraction count");
    });

    private static Task CacheCapacityIsBoundedAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        GameIconService service = new(2);
        for (int index = 0; index < 5; index++)
        {
            await service.LoadAsync(CopyExecutable(directory, $"capacity-{index}.exe"));
        }
        TestSupport.Equal(2, service.CacheCount, "icon cache size");
    });

    private static Task FileMetadataInvalidatesCacheAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string executable = CopyExecutable(directory, "updated.exe");
        GameIconService service = new(4);
        await service.LoadAsync(executable);
        int before = service.ExtractionCount;
        File.SetLastWriteTimeUtc(executable, DateTime.UtcNow.AddMinutes(1));
        await service.LoadAsync(executable);
        TestSupport.Equal(before + 1, service.ExtractionCount, "metadata change did not invalidate icon");
        TestSupport.Equal(1, service.CacheCount, "stale metadata entry remained cached");
    });

    private static async Task UncIsSkippedAsync()
    {
        GameIconService service = new(4);
        ImageSource? image = await service.LoadAsync("\\\\server\\share\\game.exe");
        TestSupport.True(image is null, "UNC icon path was accessed");
        TestSupport.Equal(0, service.ExtractionCount, "UNC path started extraction");
    }

    private static Task CancellationOnlyCancelsCallerAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        string executable = CopyExecutable(directory, "canceled.exe");
        GameIconService service = new(4);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            await service.LoadAsync(executable, cancellation.Token);
            throw new InvalidOperationException("canceled icon wait unexpectedly completed");
        }
        catch (OperationCanceledException)
        {
        }

        ImageSource? image = await service.LoadAsync(executable);
        TestSupport.True(image is not null, "shared background icon load did not remain usable");
        TestSupport.Equal(1, service.ExtractionCount, "canceled caller started duplicate extraction");
    });

    private static async Task MissingExecutableDegradesQuietlyAsync()
    {
        GameIconService service = new(4);
        ImageSource? image = await service.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));
        TestSupport.True(image is null, "missing executable returned an icon");
    }

    private static string CopyExecutable(string directory, string fileName)
    {
        string source = TestSupport.NotNull(Environment.ProcessPath, "test process path missing");
        string destination = Path.Combine(directory, fileName);
        File.Copy(source, destination);
        return destination;
    }
}
