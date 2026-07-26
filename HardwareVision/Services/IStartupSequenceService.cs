using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IStartupSequenceService : IDisposable
{
    StartupSequenceSnapshot CurrentSnapshot { get; }

    event EventHandler<StartupSequenceChangedEventArgs>? SnapshotChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    bool ReportSurfaceReady(double width, double height, string detail = "");

    bool ReportFirstFrameGateReleased(string reason);

    bool ReportInitialProjection(StartupInitialProjectionSnapshot projection);

    bool ReportPostDataLayout(long pollingVersion);

    bool ReportMilestone(StartupMilestoneId id, StartupMilestoneState state, string detail = "");

    void CompleteForHiddenWindow();

    void CompleteForVisualReadinessFailure(string detail);

    void Cancel();
}
