using System.ComponentModel;
using System.IO;
using System.Security;

namespace HardwareVision.Utilities;

internal static class ApplicationExceptionPolicy
{
    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException;

    public static bool IsRecoverableUi(Exception exception) =>
        !IsFatal(exception)
        && exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or Win32Exception
            or OperationCanceledException;
}
