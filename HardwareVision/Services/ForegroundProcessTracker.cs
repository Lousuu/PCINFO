using System.Runtime.InteropServices;
using System.Text;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class ForegroundProcessSnapshot
{
    public int ProcessId { get; init; }

    public DateTimeOffset ObservedAtUtc { get; init; }

    public nint WindowHandle { get; init; }

    public string? WindowTitle { get; init; }
}

public interface IForegroundProcessTracker
{
    ForegroundProcessSnapshot? GetSnapshot();
}

public sealed class ForegroundProcessTracker : IForegroundProcessTracker, IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private const uint WineventSkipOwnProcess = 0x0002;
    private static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(1);
    private readonly object stateLock = new();
    private readonly int ownProcessId = Environment.ProcessId;
    private readonly WinEventDelegate winEventCallback;
    private readonly CancellationTokenSource fallbackCancellation = new();
    private readonly nint hookHandle;
    private Task? fallbackTask;
    private ForegroundProcessSnapshot? snapshot;
    private int isDisposed;

    public ForegroundProcessTracker()
    {
        winEventCallback = OnWinEvent;
        hookHandle = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            winEventCallback,
            0,
            0,
            WineventOutOfContext | WineventSkipOwnProcess);

        CaptureWindow(GetForegroundWindow());
        if (hookHandle == nint.Zero)
        {
            AppLogger.LogKeyEvent("Foreground WinEvent hook unavailable; using one-second polling fallback.");
            fallbackTask = Task.Run(() => PollForegroundWindowAsync(fallbackCancellation.Token));
        }
    }

    public ForegroundProcessSnapshot? GetSnapshot()
    {
        lock (stateLock)
        {
            return snapshot;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        fallbackCancellation.Cancel();
        if (hookHandle != nint.Zero)
        {
            UnhookWinEvent(hookHandle);
        }

        try
        {
            fallbackTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        fallbackCancellation.Dispose();
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (eventType == EventSystemForeground && Volatile.Read(ref isDisposed) == 0)
        {
            CaptureWindow(windowHandle);
        }
    }

    private async Task PollForegroundWindowAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(FallbackPollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            CaptureWindow(GetForegroundWindow());
        }
    }

    private void CaptureWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero
            || windowHandle == GetDesktopWindow()
            || windowHandle == GetShellWindow()
            || !IsWindowVisible(windowHandle)
            || IsShellSurface(windowHandle))
        {
            return;
        }

        GetWindowThreadProcessId(windowHandle, out uint processIdValue);
        int processId = unchecked((int)processIdValue);
        if (processId <= 0 || processId == ownProcessId)
        {
            return;
        }

        ForegroundProcessSnapshot next = new()
        {
            ProcessId = processId,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            WindowHandle = windowHandle,
            WindowTitle = GetWindowTitle(windowHandle)
        };

        lock (stateLock)
        {
            if (Volatile.Read(ref isDisposed) == 0)
            {
                snapshot = next;
            }
        }
    }

    private static bool IsShellSurface(nint windowHandle)
    {
        StringBuilder className = new(128);
        _ = GetClassName(windowHandle, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static string? GetWindowTitle(nint windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return null;
        }

        StringBuilder title = new(Math.Min(length + 1, 1024));
        _ = GetWindowText(windowHandle, title, title.Capacity);
        string value = title.ToString().Trim();
        return value.Length == 0 ? null : value;
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint windowHandle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint windowHandle, StringBuilder className, int maximumCount);
}

internal sealed class EmptyForegroundProcessTracker : IForegroundProcessTracker
{
    public static EmptyForegroundProcessTracker Instance { get; } = new();

    private EmptyForegroundProcessTracker()
    {
    }

    public ForegroundProcessSnapshot? GetSnapshot()
    {
        return null;
    }
}
