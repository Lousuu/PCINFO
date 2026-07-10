using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace HardwareVision.Services;

internal static class PresentMonRuntimeExtractor
{
    private const string Version = "2.5.1";
    private const string ExecutableResourceName = "HardwareVision.ThirdParty.PresentMon.PresentMon.exe";
    private const string LicenseResourceName = "HardwareVision.ThirdParty.PresentMon.LICENSE.txt";
    private const string ThirdPartyResourceName = "HardwareVision.ThirdParty.PresentMon.THIRD_PARTY.txt";
    private const string ExecutableSha256 = "9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191";
    private const string LicenseSha256 = "FD16285964EB4B980950AF0BCFD6A61F8DD9626A073AB4097F5F92BCA60ECDAF";
    private const string ThirdPartySha256 = "65F601A2334946923D9D0164B3BC72C5D41FEAFB624530DCAB01EF0D9C5EBD54";
    private const int LockRetryCount = 100;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(100);

    public static string RuntimeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HardwareVision",
        "Runtime",
        "PresentMon",
        Version);

    public static string ExecutablePath => Path.Combine(RuntimeDirectory, "PresentMon.exe");

    public static bool IsEmbeddedAvailable
    {
        get
        {
            Assembly assembly = typeof(PresentMonRuntimeExtractor).Assembly;
            return assembly.GetManifestResourceInfo(ExecutableResourceName) is not null
                && assembly.GetManifestResourceInfo(LicenseResourceName) is not null
                && assembly.GetManifestResourceInfo(ThirdPartyResourceName) is not null;
        }
    }

    public static async Task<string> EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RuntimeDirectory);
        string lockPath = Path.Combine(RuntimeDirectory, ".extract.lock");
        await using FileStream extractionLock = await AcquireLockAsync(lockPath, cancellationToken).ConfigureAwait(false);

        await EnsureResourceAsync(
            ExecutableResourceName,
            ExecutablePath,
            ExecutableSha256,
            cancellationToken).ConfigureAwait(false);
        await EnsureResourceAsync(
            LicenseResourceName,
            Path.Combine(RuntimeDirectory, "LICENSE.txt"),
            LicenseSha256,
            cancellationToken).ConfigureAwait(false);
        await EnsureResourceAsync(
            ThirdPartyResourceName,
            Path.Combine(RuntimeDirectory, "THIRD_PARTY.txt"),
            ThirdPartySha256,
            cancellationToken).ConfigureAwait(false);

        return Path.GetFullPath(ExecutablePath);
    }

    private static async Task<FileStream> AcquireLockAsync(string lockPath, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < LockRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (attempt + 1 < LockRetryCount)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException("Timed out while waiting to prepare the PresentMon runtime.");
    }

    private static async Task EnsureResourceAsync(
        string resourceName,
        string targetPath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath) && await HasExpectedHashAsync(targetPath, expectedSha256, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        string temporaryPath = targetPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using Stream resource = typeof(PresentMonRuntimeExtractor).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Embedded PresentMon resource is missing: {resourceName}");
            await using (FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await resource.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!await HasExpectedHashAsync(temporaryPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException($"Embedded PresentMon resource failed SHA-256 validation: {resourceName}");
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
