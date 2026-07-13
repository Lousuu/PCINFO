using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class PresentMonGamePerformanceService : IGamePerformanceService
{
    private const int MaxStoredSamples = 60_000;
    private const long DiagnosticSummaryRowInterval = 1_000;
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly object stateLock = new();
    private readonly GameFrameSampleStore sampleStore = new(MaxStoredSamples);
    private readonly IGameSessionRecorder? sessionRecorder;
    private readonly Func<bool> isSessionRecordingEnabled;
    private readonly bool hasBundledPresentMon;
    private readonly bool isElevated;
    private readonly string? injectedPresentMonPath;
    private string? presentMonPath;
    private Process? captureProcess;
    private CancellationTokenSource? captureCancellation;
    private Task? outputFileTask;
    private Task? stdoutTask;
    private Task? stderrTask;
    private Task? processExitTask;
    private Task? targetExitTask;
    private Task? noDataTask;
    private string? captureOutputFilePath;
    private string? captureSessionName;
    private CaptureDiagnostics? currentDiagnostics;
    private GameCaptureState captureState;
    private string statusText;
    private int captureGeneration;
    private bool isDisposed;

    public PresentMonGamePerformanceService()
        : this(sessionRecorder: null, isSessionRecordingEnabled: null)
    {
    }

    public PresentMonGamePerformanceService(
        IGameSessionRecorder? sessionRecorder,
        Func<bool>? isSessionRecordingEnabled)
    {
        this.sessionRecorder = sessionRecorder;
        this.isSessionRecordingEnabled = isSessionRecordingEnabled ?? (() => false);
        presentMonPath = FindConfiguredPresentMonPath();
        hasBundledPresentMon = PresentMonRuntimeExtractor.IsEmbeddedAvailable;
        isElevated = IsCurrentProcessElevated();
        (captureState, statusText) = ResolveIdleState();
    }

    internal PresentMonGamePerformanceService(
        string captureToolPath,
        bool isElevated,
        IGameSessionRecorder? sessionRecorder = null,
        Func<bool>? isSessionRecordingEnabled = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureToolPath);
        injectedPresentMonPath = Path.GetFullPath(captureToolPath);
        presentMonPath = injectedPresentMonPath;
        hasBundledPresentMon = false;
        this.isElevated = isElevated;
        this.sessionRecorder = sessionRecorder;
        this.isSessionRecordingEnabled = isSessionRecordingEnabled ?? (() => false);
        (captureState, statusText) = ResolveIdleState();
    }

    public event EventHandler<GameFrameSample>? FrameReceived;

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<GameCaptureStateChangedEventArgs>? CaptureStateChanged;

    public bool IsCaptureAvailable => (presentMonPath is not null || hasBundledPresentMon) && isElevated;

    public string StatusText
    {
        get
        {
            lock (stateLock)
            {
                return statusText;
            }
        }
    }

    public GameCaptureState CaptureState
    {
        get
        {
            lock (stateLock)
            {
                return captureState;
            }
        }
    }

    public string? CaptureToolPath => presentMonPath
        ?? (hasBundledPresentMon ? PresentMonRuntimeExtractor.ExecutablePath : null);

    public IReadOnlyList<GameFrameSample> RecentSamples => sampleStore.Snapshot();

    public GamePerformanceSnapshot GetSnapshot(TimeSpan window) => sampleStore.Calculate(window);

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
                        if (process.Id == currentProcessId
                            || process.HasExited
                            || GameProcessScorer.IsCoreSystemProcess(process.ProcessName))
                        {
                            continue;
                        }

                        string processName = process.ProcessName;
                        string? title = NullIfWhiteSpace(process.MainWindowTitle);
                        string? path = TryGetMainModulePath(process);
                        DateTimeOffset? startTimeUtc = TryGetStartTimeUtc(process);
                        nint mainWindowHandle = process.MainWindowHandle;
                        bool hasVisibleMainWindow = title is not null
                            && mainWindowHandle != nint.Zero
                            && IsWindowVisible(mainWindowHandle);

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
                            StartTimeUtc = startTimeUtc,
                            IsRunning = true,
                            HasVisibleMainWindow = hasVisibleMainWindow,
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
                .OrderBy(process => process.ProcessName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(process => process.ProcessId)
                .ToArray();
        }, cancellationToken);
    }

    public async Task StartCaptureAsync(GameProcessInfo process, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            await StopCaptureCoreAsync(returnToIdle: false, GameSessionEndReason.UserStopped).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Guid sessionId = Guid.NewGuid();
            int generation = Interlocked.Increment(ref captureGeneration);
            sampleStore.StartSession(sessionId);
            UpdateCaptureState(
                GameCaptureState.Preparing,
                $"正在准备采集：{process.ProcessName} ({process.ProcessId})",
                sessionId);

            GameProcessInfo captureTarget = await GameCaptureTargetResolver
                .ResolveAsync(process, cancellationToken)
                .ConfigureAwait(false);
            CaptureDiagnostics diagnostics = new(sessionId, generation, captureTarget);
            PresentMonCsvParser parser = new(
                sessionId,
                captureTarget.ProcessId,
                captureTarget.ProcessName);
            currentDiagnostics = diagnostics;
            if (captureTarget.ProcessId != process.ProcessId)
            {
                AppLogger.LogKeyEvent(
                    "Game capture target resolved"
                    + $" | requested={process.ProcessName} ({process.ProcessId})"
                    + $"; target={captureTarget.ProcessName} ({captureTarget.ProcessId})"
                    + $"; window={captureTarget.WindowTitle ?? "NA"}");
            }

            string? captureToolPath = await ResolveCaptureToolPathAsync(cancellationToken).ConfigureAwait(false);
            if (captureToolPath is null)
            {
                AppLogger.LogError(
                    $"PresentMon unavailable | session={sessionId:N}; expected={PresentMonRuntimeExtractor.ExecutablePath}",
                    null,
                    "presentmon-unavailable",
                    TimeSpan.FromMinutes(5));
                UpdateCaptureState(GameCaptureState.ToolUnavailable, "采集组件未就绪", sessionId);
                return;
            }

            if (!isElevated)
            {
                AppLogger.LogError(
                    $"PresentMon permission denied before start | session={sessionId:N}; path={captureToolPath}",
                    null,
                    "presentmon-not-elevated",
                    TimeSpan.FromMinutes(5));
                UpdateCaptureState(GameCaptureState.PermissionDenied, "需要管理员权限运行", sessionId);
                return;
            }

            if (injectedPresentMonPath is null)
            {
                await CleanupStaleTraceSessionsAsync(cancellationToken).ConfigureAwait(false);
            }

            string sessionName = $"HardwareVision-{Environment.ProcessId}-{captureTarget.ProcessId}-{sessionId:N}";
            string arguments = BuildArguments(sessionName);
            ProcessStartInfo startInfo = new()
            {
                FileName = captureToolPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(captureToolPath) ?? Path.GetTempPath()
            };

            Process capture = new()
            {
                StartInfo = startInfo
            };
            CancellationTokenSource captureLifetime = new();
            captureProcess = capture;
            captureCancellation = captureLifetime;
            captureSessionName = sessionName;

            AppLogger.LogKeyEvent(
                $"PresentMon starting | session={sessionId:N}; path={captureToolPath}"
                + $"; captureMode=system-wide; appFilterProcessId={captureTarget.ProcessId}"
                + $"; arguments={arguments}");

            try
            {
                if (!capture.Start())
                {
                    throw new InvalidOperationException("PresentMon Process.Start returned false.");
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
            {
                AppLogger.LogError(
                    $"PresentMon capture start failed | session={sessionId:N}; path={captureToolPath}; arguments={arguments}",
                    ex,
                    $"presentmon-start:{ex.GetType().FullName}",
                    TimeSpan.FromMinutes(5));
                captureProcess = null;
                captureCancellation = null;
                captureSessionName = null;
                currentDiagnostics = null;
                Interlocked.Increment(ref captureGeneration);
                captureLifetime.Cancel();
                captureLifetime.Dispose();
                capture.Dispose();
                UpdateCaptureState(GameCaptureState.Failed, "采集启动失败", sessionId);
                return;
            }

            AppLogger.LogKeyEvent(
                $"PresentMon started | session={sessionId:N}; processId={capture.Id}; targetProcessId={captureTarget.ProcessId}");
            if (sessionRecorder is not null && isSessionRecordingEnabled())
            {
                try
                {
                    await sessionRecorder.StartAsync(new GameSessionStartInfo
                    {
                        CaptureSessionId = sessionId,
                        Generation = generation,
                        ProcessId = captureTarget.ProcessId,
                        ProcessName = captureTarget.ProcessName,
                        WindowTitle = captureTarget.WindowTitle,
                        ExecutablePath = captureTarget.FilePath,
                        CaptureStartedAt = DateTimeOffset.Now
                    }, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    AppLogger.LogError(
                        $"Automatic game session recording could not start | session={sessionId:N}",
                        exception,
                        $"game-recorder-start:{exception.GetType().FullName}",
                        TimeSpan.FromMinutes(1));
                }
            }

            UpdateCaptureState(
                GameCaptureState.WaitingForFirstFrame,
                $"等待首帧：{captureTarget.ProcessName} ({captureTarget.ProcessId})",
                sessionId);

            Task outputFileReader = Task.CompletedTask;
            Task stdoutReader = ReadStdoutAsync(capture, parser, diagnostics, captureLifetime.Token);
            Task stderrReader = ReadStderrAsync(capture, diagnostics, captureLifetime.Token);
            outputFileTask = outputFileReader;
            stdoutTask = stdoutReader;
            stderrTask = stderrReader;
            processExitTask = MonitorProcessExitAsync(
                capture,
                outputFileReader,
                stdoutReader,
                stderrReader,
                diagnostics);
            targetExitTask = MonitorTargetProcessExitAsync(
                captureTarget,
                diagnostics,
                captureLifetime.Token);
            noDataTask = ReportNoDataIfNeededAsync(diagnostics, captureLifetime.Token);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCaptureCoreAsync(returnToIdle: true, GameSessionEndReason.UserStopped).ConfigureAwait(false);
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    public Task<string?> ExportCsvAsync(string directory, CancellationToken cancellationToken = default)
        => ExportCacheCsvAsync(directory, processName: null, cancellationToken);

    public Task<string?> ExportWindowCsvAsync(
        string directory,
        TimeSpan window,
        string? processName = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GameFrameSample> snapshot = sampleStore.Snapshot(window);
        string gameName = GameSessionFileNaming.Sanitize(processName ?? snapshot.LastOrDefault()?.ProcessName, "Game");
        return GameCsvExporter.ExportAsync(
            snapshot,
            directory,
            $"{gameName}-last-{Math.Max(1, (int)window.TotalSeconds)}s-{DateTimeOffset.Now:yyyyMMdd-HHmmss}",
            cancellationToken);
    }

    public Task<string?> ExportCacheCsvAsync(
        string directory,
        string? processName = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GameFrameSample> snapshot = sampleStore.Snapshot();
        string gameName = GameSessionFileNaming.Sanitize(processName ?? snapshot.LastOrDefault()?.ProcessName, "Game");
        return GameCsvExporter.ExportAsync(
            snapshot,
            directory,
            $"{gameName}-cache-{DateTimeOffset.Now:yyyyMMdd-HHmmss}",
            cancellationToken);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        try
        {
            lifecycleLock.Wait();
            try
            {
                StopCaptureCoreAsync(returnToIdle: false, GameSessionEndReason.ApplicationShutdown).GetAwaiter().GetResult();
            }
            finally
            {
                lifecycleLock.Release();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
        }

        isDisposed = true;
        lifecycleLock.Dispose();
    }

    private async Task StopCaptureCoreAsync(bool returnToIdle, GameSessionEndReason endReason)
    {
        Process? processToStop = captureProcess;
        CancellationTokenSource? cancellationToStop = captureCancellation;
        Task? outputFileToWait = outputFileTask;
        Task? stdoutToWait = stdoutTask;
        Task? stderrToWait = stderrTask;
        Task? exitToWait = processExitTask;
        Task? targetExitToWait = targetExitTask;
        Task? noDataToWait = noDataTask;
        string? outputFileToDelete = captureOutputFilePath;
        string? traceSessionToStop = captureSessionName;
        CaptureDiagnostics? diagnostics = currentDiagnostics;
        diagnostics?.SetEndReason(endReason);
        bool hadCaptureResources = processToStop is not null || cancellationToStop is not null;

        if (hadCaptureResources)
        {
            UpdateCaptureState(
                GameCaptureState.Stopping,
                "正在停止采集",
                diagnostics?.SessionId);
        }

        captureProcess = null;
        captureCancellation = null;
        outputFileTask = null;
        stdoutTask = null;
        stderrTask = null;
        processExitTask = null;
        targetExitTask = null;
        noDataTask = null;
        captureOutputFilePath = null;
        captureSessionName = null;
        currentDiagnostics = null;
        Interlocked.Increment(ref captureGeneration);
        cancellationToStop?.Cancel();

        if (processToStop is not null)
        {
            TryKill(processToStop);
        }

        await ObserveAllAsync(
            outputFileToWait,
            stdoutToWait,
            stderrToWait,
            noDataToWait,
            targetExitToWait,
            exitToWait).ConfigureAwait(false);
        if (diagnostics is not null)
        {
            LogCaptureSummary(diagnostics, final: true);
            await CompleteRecordingAsync(diagnostics).ConfigureAwait(false);
        }

        processToStop?.Dispose();
        cancellationToStop?.Dispose();
        TryDeleteCaptureOutput(outputFileToDelete);
        if (injectedPresentMonPath is null && traceSessionToStop is not null)
        {
            await StopTraceSessionAsync(traceSessionToStop, CancellationToken.None).ConfigureAwait(false);
        }

        if (returnToIdle)
        {
            (GameCaptureState state, string text) = ResolveIdleState();
            UpdateCaptureState(state, text, captureSessionId: null);
        }
    }

    private static string BuildArguments(string sessionName)
    {
        return string.Join(
            " ",
            "--output_stdout",
            "--session_name", Quote(sessionName),
            "--no_console_stats",
            "--track_frame_type",
            "--v2_metrics");
    }

    private async Task ReadOutputFileAsync(
        Process process,
        string outputFilePath,
        PresentMonCsvParser parser,
        CaptureDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            FileStream? stream = null;
            while (!cancellationToken.IsCancellationRequested && stream is null)
            {
                try
                {
                    stream = new FileStream(
                        outputFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 16_384,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                catch (FileNotFoundException)
                {
                    if (HasExited(process))
                    {
                        return;
                    }
                }
                catch (IOException) when (!HasExited(process))
                {
                    // PresentMon may create the file before it enables read sharing.
                }

                if (stream is null)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
                }
            }

            if (stream is null)
            {
                return;
            }

            await using (stream)
            using (StreamReader reader = new(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 16_384,
                leaveOpen: false))
            {
                AppLogger.LogKeyEvent(
                    $"PresentMon output file opened | session={diagnostics.SessionId:N}; path={outputFilePath}");
                char[] buffer = new char[8_192];
                StringBuilder pendingLine = new(512);

                while (!cancellationToken.IsCancellationRequested)
                {
                    int charactersRead = await reader
                        .ReadAsync(buffer.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    if (charactersRead > 0)
                    {
                        AppendOutputCharacters(buffer.AsSpan(0, charactersRead), pendingLine, parser, diagnostics);
                        continue;
                    }

                    if (HasExited(process))
                    {
                        if (pendingLine.Length > 0)
                        {
                            HandleOutputLine(TrimTrailingCarriageReturn(pendingLine), parser, diagnostics);
                        }

                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            AppLogger.LogError(
                $"PresentMon output file read failed | session={diagnostics.SessionId:N}; path={outputFilePath}",
                ex,
                $"presentmon-output-file:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
            if (IsCurrentCapture(diagnostics))
            {
                UpdateCaptureState(
                    GameCaptureState.Failed,
                    "读取 PresentMon 输出文件失败",
                    diagnostics.SessionId);
                RequestCaptureTermination(diagnostics);
            }
        }
        finally
        {
            LogCaptureSummary(diagnostics, final: true);
        }
    }

    private async Task ReadStdoutAsync(
        Process process,
        PresentMonCsvParser parser,
        CaptureDiagnostics diagnostics,
        CancellationToken cancellationToken)
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

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (diagnostics.RecordStdoutLine() == 1)
                {
                    AppLogger.LogKeyEvent(
                        $"PresentMon first stdout | session={diagnostics.SessionId:N}; line={line}");
                }

                HandleOutputLine(line, parser, diagnostics);
                if (!line.Contains(',', StringComparison.Ordinal))
                {
                    HandleDiagnosticLine(line, diagnostics);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            AppLogger.LogError(
                $"PresentMon stdout read failed | session={diagnostics.SessionId:N}",
                ex,
                $"presentmon-stdout:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
            if (IsCurrentCapture(diagnostics))
            {
                UpdateCaptureState(
                    GameCaptureState.Failed,
                    "读取 PresentMon 输出失败",
                    diagnostics.SessionId);
                RequestCaptureTermination(diagnostics);
            }
        }
    }

    private async Task ReadStderrAsync(
        Process process,
        CaptureDiagnostics diagnostics,
        CancellationToken cancellationToken)
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

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (diagnostics.RecordStderrLine() == 1)
                {
                    AppLogger.LogKeyEvent(
                        $"PresentMon first stderr | session={diagnostics.SessionId:N}; line={line}");
                }

                HandleDiagnosticLine(line, diagnostics);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            AppLogger.LogError(
                $"PresentMon stderr read failed | session={diagnostics.SessionId:N}",
                ex,
                $"presentmon-stderr:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }

    private async Task MonitorProcessExitAsync(
        Process process,
        Task outputFileReader,
        Task stdoutReader,
        Task stderrReader,
        CaptureDiagnostics diagnostics)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            int exitCode = process.ExitCode;
            await ObserveAllAsync(outputFileReader, stdoutReader, stderrReader).ConfigureAwait(false);
            AppLogger.LogKeyEvent(
                $"PresentMon exited | session={diagnostics.SessionId:N}; exitCode={exitCode}");

            if (!IsCurrentCapture(diagnostics))
            {
                return;
            }

            GameCaptureState state = CaptureState;
            if (state is GameCaptureState.Preparing
                or GameCaptureState.WaitingForFirstFrame
                or GameCaptureState.Capturing)
            {
                string sampleText = diagnostics.ParsedSampleCount == 0
                    ? "，未收到有效帧"
                    : string.Empty;
                UpdateCaptureState(
                    GameCaptureState.ProcessExited,
                    $"PresentMon 已退出（代码 {exitCode}）{sampleText}",
                    diagnostics.SessionId);
            }

            await CompleteRecordingAsync(diagnostics).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            AppLogger.LogError(
                $"PresentMon exit monitoring failed | session={diagnostics.SessionId:N}",
                ex,
                $"presentmon-exit:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }

    private async Task ReportNoDataIfNeededAsync(
        CaptureDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (IsCurrentCapture(diagnostics)
                && diagnostics.ParsedSampleCount == 0
                && CaptureState == GameCaptureState.WaitingForFirstFrame)
            {
                string observedProcesses = diagnostics.FormatObservedProcesses();
                string detail = diagnostics.CsvLineCount > 0
                    ? $"，系统采集正常，但尚未发现目标进程帧；已观察：{observedProcesses}"
                    : "，5 秒内未收到任何系统帧事件";
                UpdateStatusOnly(
                    $"等待首帧：{diagnostics.Process.ProcessName} ({diagnostics.Process.ProcessId}){detail}");
                LogCaptureSummary(diagnostics, final: false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task MonitorTargetProcessExitAsync(
        GameProcessInfo target,
        CaptureDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            using Process targetProcess = Process.GetProcessById(target.ProcessId);
            await targetProcess.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (!IsCurrentCapture(diagnostics))
            {
                return;
            }

            UpdateCaptureState(
                GameCaptureState.ProcessExited,
                $"目标进程已退出：{target.ProcessName} ({target.ProcessId})",
                diagnostics.SessionId);
            diagnostics.SetEndReason(GameSessionEndReason.TargetProcessExited);
            RequestCaptureTermination(diagnostics);
        }
        catch (ArgumentException) when (IsCurrentCapture(diagnostics))
        {
            UpdateCaptureState(
                GameCaptureState.ProcessExited,
                $"目标进程已退出：{target.ProcessName} ({target.ProcessId})",
                diagnostics.SessionId);
            diagnostics.SetEndReason(GameSessionEndReason.TargetProcessExited);
            RequestCaptureTermination(diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLogger.LogError(
                $"Game target exit monitoring failed | session={diagnostics.SessionId:N}; targetProcessId={target.ProcessId}",
                ex,
                $"game-target-exit:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
    }

    private static async Task CleanupStaleTraceSessionsAsync(CancellationToken cancellationToken)
    {
        HashSet<string>? activeSessions = TryGetActivePresentMonSessionNames();
        if (activeSessions is null)
        {
            AppLogger.LogError(
                "PresentMon stale ETW cleanup skipped because active sessions could not be determined.",
                null,
                "presentmon-stale-cleanup-active-query",
                TimeSpan.FromMinutes(5));
            return;
        }

        LogmanResult query = await RunLogmanAsync(["query", "-ets"], cancellationToken).ConfigureAwait(false);
        if (query.ExitCode != 0)
        {
            AppLogger.LogError(
                $"PresentMon stale ETW cleanup query failed | exitCode={query.ExitCode}; diagnostic={query.Diagnostic}",
                null,
                "presentmon-stale-cleanup-query",
                TimeSpan.FromMinutes(5));
            return;
        }

        string[] staleSessions = Regex
            .Matches(query.StandardOutput, @"(?m)^\s*(HardwareVision-\S+)\s+")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sessionName => !activeSessions.Contains(sessionName))
            .Take(100)
            .ToArray();
        if (staleSessions.Length == 0)
        {
            return;
        }

        int stoppedCount = 0;
        foreach (string sessionName in staleSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await StopTraceSessionAsync(sessionName, cancellationToken).ConfigureAwait(false))
            {
                stoppedCount++;
            }
        }

        AppLogger.LogKeyEvent(
            $"PresentMon stale ETW cleanup completed | found={staleSessions.Length}; stopped={stoppedCount}");
    }

    private static HashSet<string>? TryGetActivePresentMonSessionNames()
    {
        try
        {
            HashSet<string> sessions = new(StringComparer.OrdinalIgnoreCase);
            using ManagementObjectSearcher searcher = new(
                "SELECT CommandLine FROM Win32_Process WHERE Name = 'PresentMon.exe'");
            using ManagementObjectCollection processes = searcher.Get();
            foreach (ManagementObject process in processes)
            {
                string? commandLine = process["CommandLine"] as string;
                if (string.IsNullOrWhiteSpace(commandLine))
                {
                    continue;
                }

                Match match = Regex.Match(
                    commandLine,
                    "--session_name(?:\\s+|=)(?:\"(?<quoted>[^\"]+)\"|(?<bare>\\S+))",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string sessionName = match.Groups["quoted"].Success
                        ? match.Groups["quoted"].Value
                        : match.Groups["bare"].Value;
                    if (sessionName.StartsWith("HardwareVision-", StringComparison.OrdinalIgnoreCase))
                    {
                        sessions.Add(sessionName);
                    }
                }
            }

            return sessions;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLogger.LogError(
                "PresentMon active session query failed.",
                ex,
                $"presentmon-active-session-query:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
            return null;
        }
    }

    private static async Task<bool> StopTraceSessionAsync(
        string sessionName,
        CancellationToken cancellationToken)
    {
        LogmanResult result = await RunLogmanAsync(
            ["stop", sessionName, "-ets"],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            AppLogger.LogKeyEvent($"PresentMon ETW session stopped | sessionName={sessionName}");
            return true;
        }

        return false;
    }

    private static async Task<LogmanResult> RunLogmanAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "logman.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new LogmanResult(-1, string.Empty, "Process.Start returned false.");
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string standardOutput = await stdoutTask.ConfigureAwait(false);
            string standardError = await stderrTask.ConfigureAwait(false);
            return new LogmanResult(process.ExitCode, standardOutput, standardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new LogmanResult(-1, string.Empty, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void HandleOutputLine(
        string line,
        PresentMonCsvParser parser,
        CaptureDiagnostics diagnostics)
    {
		RuntimePerformanceDiagnostics.RecordPresentMonRow();
        if (!IsCurrentCapture(diagnostics))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(line) && diagnostics.RecordCsvLine() == 1)
        {
            AppLogger.LogKeyEvent(
                $"PresentMon first CSV line | session={diagnostics.SessionId:N}; line={line}");
        }

        PresentMonCsvParseResult result = parser.ParseLine(line);
        switch (result.Kind)
        {
            case PresentMonCsvParseKind.HeaderAccepted:
                LogSchema(parser.Schema!, diagnostics, valid: true, error: null);
                break;

            case PresentMonCsvParseKind.SchemaMismatch:
                LogSchema(parser.Schema!, diagnostics, valid: false, result.Reason);
                diagnostics.SetEndReason(GameSessionEndReason.SchemaMismatch);
                UpdateCaptureState(
                    GameCaptureState.SchemaMismatch,
                    $"PresentMon 输出格式不兼容：{result.Reason}",
                    diagnostics.SessionId);
                RequestCaptureTermination(diagnostics);
                break;

            case PresentMonCsvParseKind.Sample when result.Sample is not null:
                diagnostics.RecordDataRow();
                diagnostics.RecordObservedProcess(result.Sample.ProcessId, result.Sample.ProcessName);
                if (result.Sample.ProcessId != diagnostics.Process.ProcessId)
                {
                    diagnostics.RecordFilteredRow();
                    LogPeriodicSummaryIfNeeded(diagnostics);
                    break;
                }

                diagnostics.RecordParsedSample();
                AddSample(result.Sample, diagnostics);
                LogPeriodicSummaryIfNeeded(diagnostics);
                break;

            case PresentMonCsvParseKind.Rejected:
                diagnostics.RecordDataRow();
                diagnostics.RecordDroppedRow(result.Reason ?? "unknown");
                LogPeriodicSummaryIfNeeded(diagnostics);
                break;
        }
    }

    private void AppendOutputCharacters(
        ReadOnlySpan<char> characters,
        StringBuilder pendingLine,
        PresentMonCsvParser parser,
        CaptureDiagnostics diagnostics)
    {
        foreach (char character in characters)
        {
            if (character == '\n')
            {
                HandleOutputLine(TrimTrailingCarriageReturn(pendingLine), parser, diagnostics);
                pendingLine.Clear();
                continue;
            }

            pendingLine.Append(character);
        }
    }

    private static string TrimTrailingCarriageReturn(StringBuilder line)
    {
        int length = line.Length;
        if (length > 0 && line[length - 1] == '\r')
        {
            length--;
        }

        return line.ToString(0, length);
    }

    private void AddSample(GameFrameSample sample, CaptureDiagnostics diagnostics)
    {
        if (!IsCurrentCapture(diagnostics) || !sampleStore.TryAdd(sample))
        {
            return;
        }

		RuntimePerformanceDiagnostics.RecordPresentMonSample();
        _ = sessionRecorder?.TryRecord(sample, diagnostics.SessionId, diagnostics.Generation);

        if (diagnostics.ParsedSampleCount == 1)
        {
            AppLogger.LogKeyEvent(
                "PresentMon first sample"
                + $" | session={diagnostics.SessionId:N}"
                + $"; pid={sample.ProcessId}"
                + $"; frameTimeMs={FormatLog(sample.FrameTimeMs)}"
                + $"; fps={FormatLog(sample.Fps)}"
                + $"; cpuBusyMs={FormatLog(sample.CpuBusyMs)}"
                + $"; gpuTimeMs={FormatLog(sample.GpuTimeMs)}"
                + $"; displayLatencyMs={FormatLog(sample.DisplayLatencyMs)}"
                + $"; clickToPhotonMs={FormatLog(sample.ClickToPhotonLatencyMs)}"
                + $"; swapChain={sample.SwapChainAddress ?? "NA"}"
                + $"; frameType={sample.FrameType ?? "NA"}");
            if (CaptureState == GameCaptureState.WaitingForFirstFrame)
            {
                UpdateCaptureState(
                    GameCaptureState.Capturing,
                    $"采集中：{diagnostics.Process.ProcessName} ({diagnostics.Process.ProcessId})",
                    diagnostics.SessionId);
            }
        }

        FrameReceived?.Invoke(this, sample);
    }

    private void HandleDiagnosticLine(string line, CaptureDiagnostics diagnostics)
    {
        if (!IsCurrentCapture(diagnostics))
        {
            return;
        }

        if (line.Contains("failed to start trace session", StringComparison.OrdinalIgnoreCase))
        {
            bool resourceExhausted = line.Contains("1450", StringComparison.OrdinalIgnoreCase);
            AppLogger.LogError(
                $"PresentMon ETW trace session start failed | session={diagnostics.SessionId:N}; diagnostic={line}",
                null,
                resourceExhausted ? "presentmon-etw-resource-exhausted" : "presentmon-etw-start-failed",
                TimeSpan.FromMinutes(1));
            UpdateCaptureState(
                GameCaptureState.Failed,
                resourceExhausted
                    ? "PresentMon ETW 资源不足，正在清理残留会话"
                    : "PresentMon ETW 会话启动失败",
                diagnostics.SessionId);
            RequestCaptureTermination(diagnostics);
            return;
        }

        if (line.Contains("requires elevated privilege", StringComparison.OrdinalIgnoreCase)
            || line.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || line.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.LogError(
                $"PresentMon permission failure | session={diagnostics.SessionId:N}; diagnostic={line}",
                null,
                "presentmon-runtime-permission",
                TimeSpan.FromMinutes(5));
            UpdateCaptureState(
                GameCaptureState.PermissionDenied,
                "PresentMon 采集权限不足",
                diagnostics.SessionId);
            RequestCaptureTermination(diagnostics);
            return;
        }

        if (line.Contains("ETW events were lost", StringComparison.OrdinalIgnoreCase)
            && diagnostics.ParsedSampleCount == 0)
        {
            AppLogger.LogError(
                $"PresentMon ETW events lost before first frame | session={diagnostics.SessionId:N}",
                null,
                "presentmon-etw-events-lost",
                TimeSpan.FromMinutes(1));
            UpdateStatusOnly("等待首帧：ETW 事件丢失");
        }
    }

    private async Task CompleteRecordingAsync(CaptureDiagnostics diagnostics)
    {
        if (sessionRecorder is null)
        {
            return;
        }

        GameSessionEndReason reason = diagnostics.EndReason == GameSessionEndReason.Unknown
            ? GameSessionEndReason.CaptureFailed
            : diagnostics.EndReason;
        bool completedNormally = reason is GameSessionEndReason.UserStopped or GameSessionEndReason.TargetProcessExited;
        try
        {
            await sessionRecorder.CompleteAsync(reason, completedNormally).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLogger.LogError(
                $"Game session finalization failed | session={diagnostics.SessionId:N}",
                exception,
                $"game-recorder-complete:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(1));
        }
    }

    private void RequestCaptureTermination(CaptureDiagnostics diagnostics)
    {
        if (!IsCurrentCapture(diagnostics))
        {
            return;
        }

        captureCancellation?.Cancel();
        Process? process = captureProcess;
        if (process is not null)
        {
            TryKill(process);
        }
    }

    private bool IsCurrentCapture(CaptureDiagnostics diagnostics)
    {
        return diagnostics.Generation == Volatile.Read(ref captureGeneration)
            && ReferenceEquals(diagnostics, currentDiagnostics);
    }

    private static void LogSchema(
        PresentMonCsvSchema schema,
        CaptureDiagnostics diagnostics,
        bool valid,
        string? error)
    {
        string rawHeader = string.Join(",", schema.RawColumns);
        string normalizedHeader = string.Join(",", schema.NormalizedColumns);
        AppLogger.LogKeyEvent(
            "PresentMon CSV schema"
            + $" | session={diagnostics.SessionId:N}"
            + $"; validFrameTime={valid}"
            + $"; raw={rawHeader}"
            + $"; normalized={normalizedHeader}"
            + (string.IsNullOrWhiteSpace(error) ? string.Empty : $"; error={error}"));
    }

    private static void LogPeriodicSummaryIfNeeded(CaptureDiagnostics diagnostics)
    {
        long dataRows = diagnostics.DataRowCount;
        if (dataRows > 0 && dataRows % DiagnosticSummaryRowInterval == 0)
        {
            LogCaptureSummary(diagnostics, final: false);
        }
    }

    private static void LogCaptureSummary(CaptureDiagnostics diagnostics, bool final)
    {
        if (final && !diagnostics.TryMarkFinalSummary())
        {
            return;
        }

        AppLogger.LogKeyEvent(
            $"PresentMon capture summary | session={diagnostics.SessionId:N}"
            + $"; final={final}"
            + $"; csvLines={diagnostics.CsvLineCount}"
            + $"; stdoutLines={diagnostics.StdoutLineCount}"
            + $"; stderrLines={diagnostics.StderrLineCount}"
            + $"; outputFileBytes={TryGetFileLength(diagnostics.OutputFilePath)}"
            + $"; dataRows={diagnostics.DataRowCount}"
            + $"; filteredRows={diagnostics.FilteredRowCount}"
            + $"; parsedSamples={diagnostics.ParsedSampleCount}"
            + $"; droppedRows={diagnostics.DroppedRowCount}"
            + $"; dropReasons={diagnostics.FormatDropReasons()}"
            + $"; observedProcesses={diagnostics.FormatObservedProcesses()}");
    }

    private void UpdateCaptureState(
        GameCaptureState value,
        string text,
        Guid? captureSessionId)
    {
        bool changed;
        lock (stateLock)
        {
            changed = captureState != value || !string.Equals(statusText, text, StringComparison.Ordinal);
            captureState = value;
            statusText = text;
        }

        if (!changed)
        {
            return;
        }

        CaptureStateChanged?.Invoke(
            this,
            new GameCaptureStateChangedEventArgs(value, text, captureSessionId));
        StatusChanged?.Invoke(this, text);
    }

    private void UpdateStatusOnly(string text)
    {
        lock (stateLock)
        {
            if (string.Equals(statusText, text, StringComparison.Ordinal))
            {
                return;
            }

            statusText = text;
        }

        StatusChanged?.Invoke(this, text);
    }

    private (GameCaptureState State, string Text) ResolveIdleState()
    {
        if (presentMonPath is null && !hasBundledPresentMon)
        {
            return (GameCaptureState.ToolUnavailable, "采集组件未就绪");
        }

        return isElevated
            ? (GameCaptureState.Idle, "就绪")
            : (GameCaptureState.PermissionDenied, "需要管理员权限运行");
    }

    private async Task<string?> ResolveCaptureToolPathAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(injectedPresentMonPath))
        {
            presentMonPath = injectedPresentMonPath;
            return injectedPresentMonPath;
        }

        string? configuredPath = FindConfiguredPresentMonPath();
        if (configuredPath is not null)
        {
            presentMonPath = configuredPath;
            return configuredPath;
        }

        if (hasBundledPresentMon)
        {
            try
            {
                presentMonPath = await PresentMonRuntimeExtractor.EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
                return presentMonPath;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                AppLogger.LogError(
                    "Bundled PresentMon extraction failed.",
                    exception,
                    $"presentmon-extract:{exception.GetType().FullName}",
                    TimeSpan.FromMinutes(5));
            }
        }

        presentMonPath = FindInstalledPresentMonPath();
        return presentMonPath;
    }

    private static string? FindConfiguredPresentMonPath()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("PRESENTMON_PATH");
        return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : null;
    }

    private static string? FindInstalledPresentMonPath()
    {
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

    private static string CreateCaptureOutputFilePath(Guid sessionId)
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HardwareVision",
            "PresentMonCaptures");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"capture-{sessionId:N}.csv");
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static long TryGetFileLength(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return -1;
        }
    }

    private static void TryDeleteCaptureOutput(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLogger.LogError(
                $"PresentMon output file cleanup failed | path={path}",
                ex,
                $"presentmon-output-cleanup:{ex.GetType().FullName}",
                TimeSpan.FromMinutes(5));
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

    private static DateTimeOffset? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task ObserveAllAsync(params Task?[] tasks)
    {
        foreach (Task? task in tasks)
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
            {
            }
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

    private static string FormatLog(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "NA";
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private readonly record struct LogmanResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string Diagnostic => string.IsNullOrWhiteSpace(StandardError)
            ? StandardOutput.Trim()
            : StandardError.Trim();
    }

    private sealed class CaptureDiagnostics
    {
        private readonly object dropReasonLock = new();
        private readonly Dictionary<string, long> dropReasons = new(StringComparer.OrdinalIgnoreCase);
        private readonly object observedProcessLock = new();
        private readonly Dictionary<(int ProcessId, string ProcessName), long> observedProcesses = new();
        private long csvLineCount;
        private long stdoutLineCount;
        private long stderrLineCount;
        private long dataRowCount;
        private long filteredRowCount;
        private long parsedSampleCount;
        private long droppedRowCount;
        private int finalSummaryLogged;
        private int endReason = (int)GameSessionEndReason.Unknown;

        public CaptureDiagnostics(Guid sessionId, int generation, GameProcessInfo process)
        {
            SessionId = sessionId;
            Generation = generation;
            Process = process;
        }

        public Guid SessionId { get; }

        public int Generation { get; }

        public GameProcessInfo Process { get; }

        public string? OutputFilePath { get; set; }

        public long CsvLineCount => Interlocked.Read(ref csvLineCount);

        public long StdoutLineCount => Interlocked.Read(ref stdoutLineCount);

        public long StderrLineCount => Interlocked.Read(ref stderrLineCount);

        public long DataRowCount => Interlocked.Read(ref dataRowCount);

        public long FilteredRowCount => Interlocked.Read(ref filteredRowCount);

        public long ParsedSampleCount => Interlocked.Read(ref parsedSampleCount);

        public long DroppedRowCount => Interlocked.Read(ref droppedRowCount);

        public GameSessionEndReason EndReason => (GameSessionEndReason)Volatile.Read(ref endReason);

        public long RecordCsvLine() => Interlocked.Increment(ref csvLineCount);

        public long RecordStdoutLine() => Interlocked.Increment(ref stdoutLineCount);

        public long RecordStderrLine() => Interlocked.Increment(ref stderrLineCount);

        public long RecordDataRow() => Interlocked.Increment(ref dataRowCount);

        public long RecordFilteredRow() => Interlocked.Increment(ref filteredRowCount);

        public long RecordParsedSample() => Interlocked.Increment(ref parsedSampleCount);

        public void SetEndReason(GameSessionEndReason reason)
        {
            Interlocked.CompareExchange(
                ref endReason,
                (int)reason,
                (int)GameSessionEndReason.Unknown);
        }

        public void RecordObservedProcess(int processId, string processName)
        {
            lock (observedProcessLock)
            {
                (int ProcessId, string ProcessName) key = (processId, processName);
                observedProcesses.TryGetValue(key, out long count);
                observedProcesses[key] = count + 1;
            }
        }

        public string FormatObservedProcesses()
        {
            lock (observedProcessLock)
            {
                return observedProcesses.Count == 0
                    ? "none"
                    : string.Join(
                        ",",
                        observedProcesses
                            .OrderByDescending(pair => pair.Value)
                            .ThenBy(pair => pair.Key.ProcessName, StringComparer.OrdinalIgnoreCase)
                            .Take(5)
                            .Select(pair => $"{pair.Key.ProcessName}({pair.Key.ProcessId}):{pair.Value}"));
            }
        }

        public void RecordDroppedRow(string reason)
        {
            Interlocked.Increment(ref droppedRowCount);
            lock (dropReasonLock)
            {
                dropReasons.TryGetValue(reason, out long count);
                dropReasons[reason] = count + 1;
            }
        }

        public string FormatDropReasons()
        {
            lock (dropReasonLock)
            {
                return dropReasons.Count == 0
                    ? "none"
                    : string.Join(
                        ",",
                        dropReasons
                            .OrderByDescending(pair => pair.Value)
                            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                            .Take(3)
                            .Select(pair => $"{pair.Key}:{pair.Value}"));
            }
        }

        public bool TryMarkFinalSummary()
        {
            return Interlocked.Exchange(ref finalSummaryLogged, 1) == 0;
        }
    }
}
