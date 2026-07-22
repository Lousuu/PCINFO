using HardwareVision.Models;

namespace HardwareVision.Services;

public interface IStartupSequenceService : IDisposable
{
    StartupSequenceSnapshot CurrentSnapshot { get; }

    event EventHandler<StartupSequenceChangedEventArgs>? SnapshotChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    bool ReportMilestone(StartupMilestoneId id, StartupMilestoneState state, string detail = "");

    void CompleteForHiddenWindow();

    void Cancel();
}
