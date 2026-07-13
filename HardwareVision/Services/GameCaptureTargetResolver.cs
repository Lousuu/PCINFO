using System.Diagnostics;
using System.IO;
using System.Management;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

internal sealed class GameProcessNode
{
    public int ProcessId { get; init; }

    public int ParentProcessId { get; init; }

    public string ProcessName { get; init; } = string.Empty;

    public string? WindowTitle { get; init; }

    public string? FilePath { get; init; }
}

internal static class GameCaptureTargetResolver
{
    public static Task<GameProcessInfo> ResolveAsync(
        GameProcessInfo requestedProcess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestedProcess);
        return Task.Run(() =>
        {
            try
            {
                IReadOnlyList<GameProcessNode> processes = ReadProcessTree(cancellationToken);
                return Resolve(requestedProcess, processes);
            }
            catch (Exception exception) when (exception is ManagementException
                or InvalidOperationException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
            {
                AppLogger.LogError(
                    $"Game capture target resolution failed | requestedPid={requestedProcess.ProcessId}",
                    exception,
                    $"game-target-resolution:{exception.GetType().FullName}",
                    TimeSpan.FromMinutes(5));
                return requestedProcess;
            }
        }, cancellationToken);
    }

    internal static GameProcessInfo Resolve(
        GameProcessInfo requestedProcess,
        IReadOnlyList<GameProcessNode> processes)
    {
        ArgumentNullException.ThrowIfNull(requestedProcess);
        ArgumentNullException.ThrowIfNull(processes);

        GameProcessNode? liveRequested = processes.FirstOrDefault(node => node.ProcessId == requestedProcess.ProcessId);
        if (liveRequested is not null && !string.IsNullOrWhiteSpace(liveRequested.WindowTitle))
        {
            return ToProcessInfo(liveRequested);
        }

        if (!string.IsNullOrWhiteSpace(requestedProcess.WindowTitle))
        {
            return requestedProcess;
        }

        Dictionary<int, int> depths = ResolveDescendantDepths(requestedProcess.ProcessId, processes);
        if (depths.Count == 0)
        {
            return requestedProcess;
        }

        string? requestedDirectory = GetDirectory(requestedProcess.FilePath ?? liveRequested?.FilePath);
        GameProcessNode? target = processes
            .Where(node => depths.ContainsKey(node.ProcessId) && !string.IsNullOrWhiteSpace(node.WindowTitle))
            .OrderByDescending(node => IsInDirectory(node.FilePath, requestedDirectory))
            .ThenByDescending(node => depths[node.ProcessId])
            .ThenBy(node => node.ProcessId)
            .FirstOrDefault();

        return target is null ? requestedProcess : ToProcessInfo(target);
    }

    private static IReadOnlyList<GameProcessNode> ReadProcessTree(CancellationToken cancellationToken)
    {
        List<GameProcessNode> nodes = new();
        using ManagementObjectSearcher searcher = new(
            "SELECT ProcessId, ParentProcessId, Name, ExecutablePath FROM Win32_Process");
        using ManagementObjectCollection results = searcher.Get();
        foreach (ManagementObject result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int processId = Convert.ToInt32(result["ProcessId"], System.Globalization.CultureInfo.InvariantCulture);
            int parentProcessId = Convert.ToInt32(result["ParentProcessId"], System.Globalization.CultureInfo.InvariantCulture);
            string processName = Convert.ToString(result["Name"], System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty;
            string? filePath = NullIfWhiteSpace(
                Convert.ToString(result["ExecutablePath"], System.Globalization.CultureInfo.InvariantCulture));
            string? windowTitle = null;

            try
            {
                using Process process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    windowTitle = NullIfWhiteSpace(process.MainWindowTitle);
                    filePath ??= TryGetMainModulePath(process);
                    processName = process.ProcessName;
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
            {
            }

            nodes.Add(new GameProcessNode
            {
                ProcessId = processId,
                ParentProcessId = parentProcessId,
                ProcessName = processName,
                WindowTitle = windowTitle,
                FilePath = filePath
            });
        }

        return nodes;
    }

    private static Dictionary<int, int> ResolveDescendantDepths(
        int requestedProcessId,
        IReadOnlyList<GameProcessNode> processes)
    {
        Dictionary<int, int> depths = new();
        HashSet<int> parents = [requestedProcessId];
        int depth = 0;

        while (parents.Count > 0)
        {
            depth++;
            int[] children = processes
                .Where(node => parents.Contains(node.ParentProcessId) && !depths.ContainsKey(node.ProcessId))
                .Select(node => node.ProcessId)
                .Distinct()
                .ToArray();
            foreach (int child in children)
            {
                depths[child] = depth;
            }

            parents = children.ToHashSet();
        }

        return depths;
    }

    private static GameProcessInfo ToProcessInfo(GameProcessNode process)
    {
        string processName = Path.GetFileNameWithoutExtension(process.ProcessName);
        return new GameProcessInfo
        {
            ProcessId = process.ProcessId,
            ProcessName = processName,
            WindowTitle = NullIfWhiteSpace(process.WindowTitle),
            FilePath = NullIfWhiteSpace(process.FilePath),
            DisplayName = string.IsNullOrWhiteSpace(process.WindowTitle)
                ? $"{processName} ({process.ProcessId})"
                : $"{process.WindowTitle.Trim()} - {processName} ({process.ProcessId})"
        };
    }

    private static bool IsInDirectory(string? filePath, string? directory)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string normalizedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(filePath).StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetDirectory(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
