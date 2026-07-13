using System.IO;

namespace HardwareVision.Services;

public static class GameSessionFileNaming
{
    public static string Sanitize(string? value, string fallback = "Game")
    {
        string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        HashSet<char> invalid = new(Path.GetInvalidFileNameChars());
        Span<char> buffer = source.Length <= 256 ? stackalloc char[source.Length] : new char[source.Length];
        int length = 0;
        for (int index = 0; index < source.Length && length < 80; index++)
        {
            char character = source[index];
            buffer[length++] = invalid.Contains(character) || char.IsControl(character) ? '_' : character;
        }

        string result = new string(buffer[..length]).Trim().TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    public static string CreateUniquePath(string directory, string fileNameWithoutExtension, string extension)
    {
        string path = Path.Combine(directory, fileNameWithoutExtension + extension);
        if (!File.Exists(path) && !File.Exists(path + ".partial") && !File.Exists(path + ".tmp"))
        {
            return path;
        }

        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            path = Path.Combine(directory, $"{fileNameWithoutExtension}-{suffix}{extension}");
            if (!File.Exists(path) && !File.Exists(path + ".partial") && !File.Exists(path + ".tmp"))
            {
                return path;
            }
        }

        throw new IOException("Unable to allocate a unique game session file name.");
    }
}
