using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class PresentMonGamePerformanceService : IGamePerformanceService
{
    private const int MaxStoredSamples = 60_000;
    private readonly object syncRoot = new();
    private readonly List<GameFrameSample> samples = new();
    private readonly string? presentMonPath;
    private readonly bool isElevated;
    private Process? captureProcess;
    private CancellationTokenSource? captureCancellation;
    private Task? stdoutTask;
    private Task? stderrTask;
    private Dictionary<string, int>? csvHeader;
    private string statusText;
    private GameProcessInfo? activeProcess;
    private int captureGeneration;
    private long captureSampleCount;

    public PresentMonGamePerformanceService()
    {
        presentMonPath = FindPresentMonPath();
        isElevated = IsCurrentProcessElevated();
        statusText = ResolveIdleStatus();
    }

    public event EventHandler<GameFrameSample>? FrameReceived;

    public event EventHandler<string>? StatusChanged;

    public bool IsCaptureAvailable => presentMonPath is not null && isElevated;

    public string StatusText => statusText;

    public string? CaptureToolPath => presentMonPath;

    public IReadOnlyList<GameFrameSample> RecentSamples
    {
        get
        {
            lock (syncRoot)
            {
                return samples.ToArray();
            }
        }
    }

    public Task<IReadOnlyList<GameProcessInfo>> GetCandidateProcessesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            int currentProcessId = Environment.ProcessId;
            List<GameProcessInfo> processes = new();

            foreach (Process process in Process.GetProcesses())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (process)
                {
                    try
                    {
                        if (process.Id == currentProcessId || process.HasExited || IsSystemProcess(process.ProcessName))
                        {
                            continue;
                        }

                        string processName = process.ProcessName;
                        string? title = NullIfWhiteSpace(process.MainWindowTitle);
                        string? path = TryGetMainModulePath(process);

                        if (title is null && path is null)
                        {
                            continue;
                        }

                        processes.Add(new GameProcessInfo
                        {
                            ProcessId = process.Id,
                            ProcessName = processName,
                            WindowTitle = title,
                            FilePath = path,
                            DisplayName = title is null
                                ? $"{processName} ({process.Id})"
                                : $"{title} - {processName} ({process.Id})"
                        });
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }

            return (IReadOnlyList<GameProcessInfo>)processes
                .OrderByDescending(process => !string.IsNullOrWhiteSpace(process.WindowTitle))
                .ThenBy(process => process.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(process => process.ProcessId)
                .ToArray();
        }, cancellationToken);
    }

    public async Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        await StopCaptureAsync(cancellationToken).ConfigureAwait(false);

        if (presentMonPath is null)
        {
            UpdateStatus("采集组件未就绪");
            return;
        }

        if (!isElevated)
        {
            UpdateStatus("需要管理员权限运行");
            return;
        }

        activeProcess = process;
        csvHeader = null;
        Interlocked.Exchange(ref captureSampleCount, 0);
        int generation = Interlocked.Increment(ref captureGeneration);
        captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string sessionName = $"HardwareVision-{Environment.ProcessId}-{Guid.NewGuid():N}";

        ProcessStartInfo startInfo = new()
        {
            FileName = presentMonPath,
            Arguments = BuildArguments(process.ProcessId, sessionName),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        captureProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        captureProcess.Exited += OnCaptureProcessExited;

        try
        {
            if (!captureProcess.Start())
            {
                UpdateStatus("采集启动失败");
                return;
            }

            stdoutTask = ReadStdoutAsync(captureProcess, captureCancellation.Token);
            stderrTask = ReadStderrAsync(captureProcess, captureCancellation.Token);
            UpdateStatus($"采集中：{process.ProcessName} ({process.ProcessId})");
            _ = ReportNoDataIfNeededAsync(generation, process, captureCancellation.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            AppLogger.LogError("PresentMon capture start failed.", ex, $"presentmon-start:{ex.GetType().FullName}", TimeSpan.FromMinutes(5));
            UpdateStatus("采集启动失败");
            await StopCaptureAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        Process? processToStop = captureProcess;
        CancellationTokenSource? cancellationToStop = captureCancellation;
        Task? stdoutToWait = stdoutTask;
        Task? stderrToWait = stderrTask;

        captureProcess = null;
        captureCancellation = null;
        stdoutTask = null;
        stderrTask = null;
        csvHeader = null;
        Interlocked.Increment(ref captureGeneration);

        cancellationToStop?.Cancel();

        if (processToStop is not null)
        {
            try
            {
                processToStop.Exited -= OnCaptureProcessExited;
                if (!processToStop.HasExited)
                {
                    processToStop.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
            }
        }

        await ObserveAsync(stdoutToWait, cancellationToken).ConfigureAwait(false);
        await ObserveAsync(stderrToWait, cancellationToken).ConfigureAwait(false);

        processToStop?.Dispose();
        cancellationToStop?.Dispose();

        UpdateStatus(ResolveIdleStatus());
    }

    public async Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default)
    {
        GameFrameSample[] snapshot;
        lock (syncRoot)
        {
            snapshot = samples.ToArray();
        }

        if (snapshot.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"game-performance-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv");
        StringBuilder builder = new();
        builder.AppendLine("Timestamp,ProcessId,ProcessName,FPS,FrameTimeMs,CpuBusyMs,GpuTimeMs,RenderLatencyMs,DisplayLatencyMs,ClickToPhotonLatencyMs,Runtime,PresentMode");

        foreach (GameFrameSample sample in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append(Csv(sample.Timestamp.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(sample.ProcessId.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(sample.ProcessName)).Append(',');
            builder.Append(Format(sample.Fps)).Append(',');
            builder.Append(Format(sample.FrameTimeMs)).Append(',');
            builder.Append(Format(sample.CpuBusyMs)).Append(',');
            builder.Append(Format(sample.GpuTimeMs)).Append(',');
            builder.Append(Format(sample.RenderLatencyMs)).Append(',');
            builder.Append(Format(sample.DisplayLatencyMs)).Append(',');
            builder.Append(Format(sample.ClickToPhotonLatencyMs)).Append(',');
            builder.Append(Csv(sample.Runtime)).Append(',');
            builder.AppendLine(Csv(sample.PresentMode));
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public void Dispose()
    {
        try
        {
            StopCaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
        }
    }

    private static string BuildArguments(int processId, string sessionName)
    {
        return string.Join(
            " ",
            "--process_id", processId.ToString(CultureInfo.InvariantCulture),
            "--output_stdout",
            "--session_name", Quote(sessionName),
            "--terminate_on_proc_exit",
            "--no_console_stats",
            "--set_circular_buffer_size", "8192",
            "--v2_metrics");
    }

    private async Task ReadStdoutAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                HandleOutputLine(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            AppLogger.LogError("PresentMon stdout read failed.", ex, $"presentmon-stdout:{ex.GetType().FullName}", TimeSpan.FromMinutes(5));
        }
    }

    private async Task ReadStderrAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    AppLogger.LogKeyEvent($"PresentMon: {line}");
                    HandleDiagnosticLine(line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            AppLogger.LogError("PresentMon stderr read failed.", ex, $"presentmon-stderr:{ex.GetType().FullName}", TimeSpan.FromMinutes(5));
        }
    }

    private void HandleOutputLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains(',', StringComparison.Ordinal))
        {
            return;
        }

        string[] columns = SplitCsv(line).ToArray();
        if (columns.Length < 3)
        {
            return;
        }

        if (csvHeader is null && LooksLikeHeader(columns))
        {
            csvHeader = columns
                .Select((name, index) => new { Name = NormalizeColumnName(name), Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (csvHeader is null || !TryCreateSample(columns, out GameFrameSample? sample))
        {
            return;
        }

        if (sample is not null)
        {
            AddSample(sample);
        }
    }

    private bool TryCreateSample(IReadOnlyList<string> columns, out GameFrameSample? sample)
    {
        double? frameTime = GetDouble(columns, "msbetweenpresents", "msbetweenpresent", "frametimems", "msperframe");
        double? fps = frameTime.HasValue && frameTime.Value > 0d
            ? 1000d / frameTime.Value
            : GetDouble(columns, "fps");

        if (!frameTime.HasValue && fps.HasValue && fps.Value > 0d)
        {
            frameTime = 1000d / fps.Value;
        }

        if (!frameTime.HasValue && !fps.HasValue)
        {
            sample = null;
            return false;
        }

        int processId = GetInt(columns, "processid", "pid") ?? activeProcess?.ProcessId ?? 0;
        string processName = GetString(columns, "application", "processname", "app") ?? activeProcess?.ProcessName ?? string.Empty;

        sample = new GameFrameSample
        {
            Timestamp = DateTimeOffset.Now,
            ProcessId = processId,
            ProcessName = processName,
            FrameTimeMs = frameTime,
            Fps = fps,
            CpuBusyMs = GetDouble(columns, "mscpubusy", "cpubusyms"),
            CpuWaitMs = GetDouble(columns, "mscpuwait", "cpuwaitms"),
            GpuTimeMs = GetDouble(columns, "msgputime", "gputimems", "msinpresentapi"),
            GpuBusyMs = GetDouble(columns, "msgpubusy", "gpubusyms"),
            GpuWaitMs = GetDouble(columns, "msgpuwait", "gpuwaitms"),
            RenderLatencyMs = GetDouble(columns, "msuntilrendercomplete", "renderlatencyms", "msrenderpresentlatency"),
            DisplayLatencyMs = GetDouble(columns, "msuntildisplayed", "displaylatencyms", "msdisplaylatency"),
            ClickToPhotonLatencyMs = GetDouble(columns, "msuntildisplayed", "clicktophotonlatencyms", "mspclatency"),
            Runtime = GetString(columns, "runtime", "presentruntime"),
            PresentMode = GetString(columns, "presentmode"),
            RawLine = string.Join(",", columns)
        };
        return true;
    }

    private void AddSample(GameFrameSample sample)
    {
        lock (syncRoot)
        {
            samples.Add(sample);
            if (samples.Count > MaxStoredSamples)
            {
                samples.RemoveRange(0, samples.Count - MaxStoredSamples);
            }
        }

        FrameReceived?.Invoke(this, sample);
        long sampleCount = Interlocked.Increment(ref captureSampleCount);
        if (sampleCount == 1 && activeProcess is not null)
        {
            UpdateStatus($"采集中：{activeProcess.ProcessName} ({activeProcess.ProcessId})");
        }
    }

    private async Task ReportNoDataIfNeededAsync(int generation, GameProcessInfo process, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (generation == Volatile.Read(ref captureGeneration)
                && Interlocked.Read(ref captureSampleCount) == 0
                && captureProcess is not null)
            {
                UpdateStatus($"未采集到帧：{process.ProcessName} ({process.ProcessId})");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandleDiagnosticLine(string line)
    {
        if (line.Contains("requires elevated privilege", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus("需要管理员权限运行");
            return;
        }

        if (line.Contains("ETW events were lost", StringComparison.OrdinalIgnoreCase)
            && Interlocked.Read(ref captureSampleCount) == 0)
        {
            UpdateStatus("ETW 事件丢失，未采集到帧");
        }
    }

    private double? GetDouble(IReadOnlyList<string> columns, params string[] names)
    {
        string? value = GetString(columns, names);
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("NA", StringComparison.OrdinalIgnoreCase)
            || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            && !double.IsNaN(result)
            && !double.IsInfinity(result)
            ? result
            : null;
    }

    private int? GetInt(IReadOnlyList<string> columns, params string[] names)
    {
        string? value = GetString(columns, names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;
    }

    private string? GetString(IReadOnlyList<string> columns, params string[] names)
    {
        if (csvHeader is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (csvHeader.TryGetValue(NormalizeColumnName(name), out int index)
                && index >= 0
                && index < columns.Count)
            {
                return NullIfWhiteSpace(columns[index]);
            }
        }

        return null;
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> columns)
    {
        return columns.Any(column =>
        {
            string normalized = NormalizeColumnName(column);
            return normalized is "application" or "processid" or "msbetweenpresents" or "presentmode";
        });
    }

    private static IEnumerable<string> SplitCsv(string line)
    {
        StringBuilder builder = new();
        bool inQuotes = false;

        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (character == ',' && !inQuotes)
            {
                yield return builder.ToString();
                builder.Clear();
            }
            else
            {
                builder.Append(character);
            }
        }

        yield return builder.ToString();
    }

    private static string NormalizeColumnName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private void OnCaptureProcessExited(object? sender, EventArgs e)
    {
        UpdateStatus(ResolveIdleStatus());
    }

    private void UpdateStatus(string value)
    {
        statusText = value;
        StatusChanged?.Invoke(this, value);
    }

    private static async Task ObserveAsync(Task? task, CancellationToken cancellationToken)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    private static bool IsSystemProcess(string processName)
    {
        return processName.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("System", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("Registry", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("smss", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("csrss", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("wininit", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("services", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("lsass", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("svchost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("dwm", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveIdleStatus()
    {
        if (presentMonPath is null)
        {
            return "采集组件未就绪";
        }

        return isElevated ? "就绪" : "需要管理员权限运行";
    }

    private static bool IsCurrentProcessElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FindPresentMonPath()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("PRESENTMON_PATH");
        if (File.Exists(configuredPath))
        {
            return configuredPath;
        }

        IEnumerable<string> directories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", "PresentMon"),
            Path.Combine(AppContext.BaseDirectory, "PresentMon"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "PresentMon"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Intel", "PresentMon")
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator));

        foreach (string directory in directories.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? match = FindPresentMonInDirectory(directory);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string? FindPresentMonInDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            foreach (string fileName in new[] { "PresentMon.exe", "presentmon.exe" })
            {
                string direct = Path.Combine(directory, fileName);
                if (File.Exists(direct))
                {
                    return direct;
                }
            }

            SearchOption searchOption = directory.Contains(
                Path.Combine("Microsoft", "WinGet", "Packages"),
                StringComparison.OrdinalIgnoreCase)
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            return Directory.EnumerateFiles(directory, "*presentmon*.exe", searchOption).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string Quote(string value)
    {
        return '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Format(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
