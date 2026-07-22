using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class StartupLifecycleRegressionTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup lifecycle hidden completion is terminal", HiddenCompletion),
        ("Startup lifecycle late visual signal cannot reopen", LateVisualSignal),
        ("Startup lifecycle late projection cannot reopen", LateProjection),
        ("Startup lifecycle dispose cancels safely", DisposeSafely)
    ];

    private static void HiddenCompletion()
    {
        using StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Full);
        service.CompleteForHiddenWindow();
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "hidden completion");
    }
    private static void LateVisualSignal()
    {
        using StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Full);
        service.CompleteForHiddenWindow();
        TestSupport.False(service.ReportSurfaceReady(1120, 720), "late visual signal");
    }
    private static void LateProjection()
    {
        using StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Full);
        service.CompleteForHiddenWindow();
        TestSupport.False(service.ReportInitialProjection(StartupInitialProjectionSnapshot.Pending), "late projection");
    }
    private static void DisposeSafely()
    {
        StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Full);
        service.Dispose();
        service.Dispose();
    }
}
