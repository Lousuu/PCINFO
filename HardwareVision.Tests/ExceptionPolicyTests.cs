using System.ComponentModel;
using System.Security;
using HardwareVision.Utilities;

namespace HardwareVision.Tests;

internal static class ExceptionPolicyTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Exception policy 01 IO is recoverable UI error", () => Recoverable(new IOException())),
        ("Exception policy 02 security and Win32 are recoverable", SecurityAndWin32AreRecoverable),
        ("Exception policy 03 cancellation is recoverable", () => Recoverable(new OperationCanceledException())),
        ("Exception policy 04 fatal runtime errors are never recoverable", FatalErrorsAreNeverRecoverable),
        ("Exception policy 05 unknown exception requires controlled shutdown", UnknownExceptionRequiresControlledShutdown),
        ("Exception policy 06 App has shutdown reentry guard", AppHasShutdownReentryGuard)
    ];

    private static void Recoverable(Exception exception)
    {
        TestSupport.True(ApplicationExceptionPolicy.IsRecoverableUi(exception), $"{exception.GetType().Name} was not recoverable");
        TestSupport.False(ApplicationExceptionPolicy.IsFatal(exception), $"{exception.GetType().Name} was fatal");
    }

    private static void SecurityAndWin32AreRecoverable()
    {
        Recoverable(new SecurityException());
        Recoverable(new Win32Exception());
        Recoverable(new UnauthorizedAccessException());
    }

    private static void FatalErrorsAreNeverRecoverable()
    {
        foreach (Exception exception in new Exception[]
                 {
                     new OutOfMemoryException(),
                     new StackOverflowException(),
                     new AccessViolationException()
                 })
        {
            TestSupport.True(ApplicationExceptionPolicy.IsFatal(exception), $"{exception.GetType().Name} was not fatal");
            TestSupport.False(ApplicationExceptionPolicy.IsRecoverableUi(exception), $"{exception.GetType().Name} was recoverable");
        }
    }

    private static void UnknownExceptionRequiresControlledShutdown()
    {
        InvalidOperationException exception = new("unknown state corruption");
        TestSupport.False(ApplicationExceptionPolicy.IsFatal(exception), "ordinary unknown exception marked fatal-runtime");
        TestSupport.False(ApplicationExceptionPolicy.IsRecoverableUi(exception), "unknown exception was swallowed as recoverable");
    }

    private static void AppHasShutdownReentryGuard()
    {
        string path = Path.Combine(Environment.CurrentDirectory, "HardwareVision", "App.xaml.cs");
        string source = File.ReadAllText(path);
        TestSupport.True(source.Contains("unhandledShutdownStarted", StringComparison.Ordinal), "controlled shutdown guard missing");
        TestSupport.True(source.Contains("CompleteAsync(", StringComparison.Ordinal), "session completion missing from shutdown path");
        TestSupport.True(source.Contains("ApplicationExceptionPolicy.IsRecoverableUi", StringComparison.Ordinal), "recoverable exception classification missing");
    }
}
