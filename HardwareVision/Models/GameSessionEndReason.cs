namespace HardwareVision.Models;

public enum GameSessionEndReason
{
    UserStopped,
    TargetProcessExited,
    CaptureFailed,
    PermissionDenied,
    ToolUnavailable,
    SchemaMismatch,
    ApplicationShutdown,
    RecorderFailed,
    Unknown
}
