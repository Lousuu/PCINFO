using System.IO;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public static class GameCsvExporter
{
    public static async Task<string?> ExportAsync(
        IReadOnlyList<GameFrameSample> samples,
        string directory,
        string fileNameWithoutExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        string safeName = GameSessionFileNaming.Sanitize(fileNameWithoutExtension, "game-performance");
        string finalPath = GameSessionFileNaming.CreateUniquePath(directory, safeName, ".csv");
        string temporaryPath = finalPath + ".tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024))
            {
                await writer.WriteLineAsync(GameCsvFormatting.Header.AsMemory(), cancellationToken).ConfigureAwait(false);
                for (int index = 0; index < samples.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string line = GameCsvFormatting.FormatSample(samples[index]);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, finalPath);
            return finalPath;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
