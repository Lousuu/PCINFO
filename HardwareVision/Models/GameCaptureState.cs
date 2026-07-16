namespace HardwareVision.Models;

public enum GameCaptureState
{
    Idle,
    Preparing,
    WaitingForFirstFrame,
    Capturing,
    Stopping,
    ToolUnavailable,
    PermissionDenied,
    ProcessExited,
    SchemaMismatch,
    Failed
}

public sealed class GameCaptureStateChangedEventArgs : EventArgs
{
    public GameCaptureStateChangedEventArgs(
        GameCaptureState state,
        string statusText,
        Guid? captureSessionId)
    {
        State = state;
        StatusText = statusText;
        CaptureSessionId = captureSessionId;
    }

    public GameCaptureState State { get; }

    public string StatusText { get; }

    public Guid? CaptureSessionId { get; }
}
