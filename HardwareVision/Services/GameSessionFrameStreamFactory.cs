using System.IO;
using System.IO.Compression;
using System.Text;

namespace HardwareVision.Services;

public interface IGameSessionFrameStreamFactory
{
    Stream OpenRead(string path);

    StreamReader OpenTextReader(string path, int bufferSize = 64 * 1024);

    bool IsCompressed(string path);
}

public sealed class GameSessionFrameStreamFactory : IGameSessionFrameStreamFactory
{
    public static GameSessionFrameStreamFactory Shared { get; } = new();

    public Stream OpenRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileStream file = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            return HasGZipSignature(file) || LooksCompressed(path)
                ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: false)
                : file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public StreamReader OpenTextReader(string path, int bufferSize = 64 * 1024) => new(
        OpenRead(path),
        Encoding.UTF8,
        detectEncodingFromByteOrderMarks: true,
        bufferSize,
        leaveOpen: false);

    public bool IsCompressed(string path)
    {
        if (!File.Exists(path))
        {
            return LooksCompressed(path);
        }

        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return HasGZipSignature(file) || LooksCompressed(path);
    }

    private static bool HasGZipSignature(FileStream stream)
    {
        int first = stream.ReadByte();
        int second = stream.ReadByte();
        stream.Position = 0;
        return first == 0x1f && second == 0x8b;
    }

    private static bool LooksCompressed(string path)
    {
        string name = Path.GetFileName(path);
        return name.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csv.gz.partial", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".csv.gz.incomplete", StringComparison.OrdinalIgnoreCase);
    }
}

public static class GameSessionCsvExportService
{
    public static async Task<string> ExportPlainCsvAsync(
        string sourcePath,
        string? destinationPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string directory = Path.GetDirectoryName(sourcePath)!;
        string baseName = GameSessionFileNaming.GetSessionBaseName(sourcePath);
        string destination = string.IsNullOrWhiteSpace(destinationPath)
            ? GameSessionFileNaming.CreateUniquePath(directory, baseName + ".export", ".csv")
            : Path.GetFullPath(destinationPath);
        if (File.Exists(destination))
        {
            throw new IOException("The export target already exists.");
        }

        string partial = destination + ".partial";
        try
        {
            await using (Stream source = GameSessionFrameStreamFactory.Shared.OpenRead(sourcePath))
            await using (FileStream target = new(
                partial,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, 64 * 1024, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(partial, destination);
            return destination;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
