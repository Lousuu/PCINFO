using System;
using System.IO;
using System.Linq;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public enum SessionFileKind
{
    FrameCsv,
    SummaryJson,
    PerformanceLimitsCsv,
    HardwareTimelineCsv
}

public static class SessionFilePathResolver
{
    public static bool TryResolve(
        string sessionDirectory,
        string? fileName,
        SessionFileKind kind,
        out string? fullPath,
        out string? warning)
    {
        fullPath = null;
        warning = null;
        if (string.IsNullOrWhiteSpace(sessionDirectory) || string.IsNullOrWhiteSpace(fileName))
        {
            warning = "会话文件名为空";
            return false;
        }

        string candidateName = fileName.Trim();
        if (candidateName is "." or ".."
            || Path.IsPathRooted(candidateName)
            || candidateName.StartsWith("\\\\", StringComparison.Ordinal)
            || candidateName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || candidateName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetFileName(candidateName), candidateName, StringComparison.Ordinal))
        {
            warning = $"已拒绝不安全的会话文件名：{candidateName}";
            LogRejection(candidateName, warning);
            return false;
        }

        if (!HasExpectedSuffix(candidateName, kind))
        {
            warning = $"已拒绝扩展名不受支持的会话文件：{candidateName}";
            LogRejection(candidateName, warning);
            return false;
        }

        try
        {
            string baseDirectory = Path.GetFullPath(sessionDirectory);
            string basePrefix = Path.TrimEndingDirectorySeparator(baseDirectory) + Path.DirectorySeparatorChar;
            string resolved = Path.GetFullPath(Path.Combine(baseDirectory, candidateName));
            if (!resolved.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
            {
                warning = $"已拒绝越过会话目录边界的文件：{candidateName}";
                LogRejection(candidateName, warning);
                return false;
            }

            fullPath = resolved;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            warning = $"会话文件路径无效：{candidateName}";
            AppLogger.LogError(
                warning,
                exception,
                $"session-path-invalid:{candidateName}",
                TimeSpan.FromMinutes(5));
            return false;
        }
    }

    public static bool TryValidatePath(
        string sessionDirectory,
        string? path,
        SessionFileKind kind,
        out string? fullPath,
        out string? warning)
    {
        fullPath = null;
        warning = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string directory = Path.GetFullPath(sessionDirectory);
            string candidate = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(candidate), directory, StringComparison.OrdinalIgnoreCase))
            {
                warning = $"已拒绝会话目录之外的文件：{Path.GetFileName(candidate)}";
                LogRejection(path, warning);
                return false;
            }

            return TryResolve(directory, Path.GetFileName(candidate), kind, out fullPath, out warning);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            warning = "会话文件路径无效";
            AppLogger.LogError(warning, exception, "session-path-validation", TimeSpan.FromMinutes(5));
            return false;
        }
    }

    public static bool TryResolveRelativePath(
        string rootDirectory,
        string? relativePath,
        SessionFileKind kind,
        out string? fullPath,
        out string? warning)
    {
        fullPath = null;
        warning = null;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            warning = "会话索引包含无效的相对路径";
            return false;
        }

        string normalizedRelative = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string[] segments = normalizedRelative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            warning = "会话索引包含不安全的路径段";
            LogRejection(relativePath, warning);
            return false;
        }

        string fileName = segments[^1];
        if (!HasExpectedSuffix(fileName, kind))
        {
            warning = $"会话索引文件扩展名不受支持：{fileName}";
            LogRejection(relativePath, warning);
            return false;
        }

        try
        {
            string root = Path.GetFullPath(rootDirectory);
            string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(root, normalizedRelative));
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                warning = "会话索引路径越过了根目录边界";
                LogRejection(relativePath, warning);
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            warning = "会话索引路径无效";
            AppLogger.LogError(warning, exception, "session-index-path", TimeSpan.FromMinutes(5));
            return false;
        }
    }

    private static bool HasExpectedSuffix(string fileName, SessionFileKind kind)
    {
        string suffix = kind switch
        {
            SessionFileKind.FrameCsv => ".csv",
            SessionFileKind.SummaryJson => ".summary.json",
            SessionFileKind.PerformanceLimitsCsv => ".performance-limits.csv",
            SessionFileKind.HardwareTimelineCsv => ".hardware-timeline.csv",
            _ => string.Empty
        };
        return suffix.Length > 0 && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static void LogRejection(string candidate, string warning)
    {
        AppLogger.LogError(
            $"{warning} | candidate={candidate}",
            null,
            $"session-path-rejected:{candidate}",
            TimeSpan.FromMinutes(5));
    }
}
