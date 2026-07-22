using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class StartupVisualReadinessTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        List<(string, Action)> tests = [];
        for (int index = 1; index <= 10; index++)
        {
            int iteration = index;
            tests.Add(($"Startup visual/projection gate {iteration:00} visual ready blocks clock", TestSupport.Run(VisualReadyBlocksClockAsync)));
        }
        for (int index = 11; index <= 20; index++)
        {
            int iteration = index;
            tests.Add(($"Startup visual/projection gate {iteration:00} projection and layout gate commit", ProjectionAndLayoutGateCommit));
        }
        return tests;
    }

    private static async Task VisualReadyBlocksClockAsync()
    {
        RecordingClock clock = new();
        using StartupSequenceService service = ReadyMilestones(clock);
        Task running = service.StartAsync();
        await Task.Yield();
        TestSupport.Equal(StartupSequencePhase.Dormant, service.CurrentSnapshot.Phase, "phase before visual ready");
        TestSupport.Equal(0, clock.DelayCount, "visual clock before ready");
        service.ReportSurfaceReady(1120, 720, "ContentRendered / Loaded / Render");
        service.ReportInitialProjection(Projection(postLayout: false));
        service.ReportPostDataLayout(7);
        await running;
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "sequence completion");
    }

    private static void ProjectionAndLayoutGateCommit()
    {
        using StartupSequenceService service = ReadyMilestones(new RecordingClock());
        service.ReportSurfaceReady(1120, 720, "rendered");
        TestSupport.False(service.CurrentSnapshot.CanCommit, "commit before projection");
        service.ReportInitialProjection(Projection(postLayout: false));
        TestSupport.True(service.CurrentSnapshot.InitialProjection.DispatcherApplied, "dispatcher applied");
        TestSupport.False(service.CurrentSnapshot.CanCommit, "commit before post-data layout");
        service.ReportPostDataLayout(7);
        TestSupport.True(service.CurrentSnapshot.CanCommit, "commit after post-data layout");
        TestSupport.True(service.CurrentSnapshot.InitialProjection.Slots.All(slot => slot.IsResolved), "six resolved slots");
    }

    private static StartupSequenceService ReadyMilestones(IStartupSequenceClock clock)
    {
        StartupSequenceService service = new(AppTheme.Tracework, MotionLevel.Standard, clock);
        foreach (StartupMilestoneId id in Enum.GetValues<StartupMilestoneId>()
                     .Where(id => id != StartupMilestoneId.ShellSurface))
        {
            service.ReportMilestone(id, StartupMilestoneState.Ready, "ready");
        }
        return service;
    }

    private static StartupInitialProjectionSnapshot Projection(bool postLayout) => new(
        7,
        Enum.GetValues<HardwareOverviewKind>()
            .Select(kind => new StartupProjectionSlotSnapshot(kind, StartupProjectionState.Value, "value"))
            .ToArray(),
        DispatcherApplied: true,
        PostDataLayoutObserved: postLayout);

    private sealed class RecordingClock : IStartupSequenceClock
    {
        public int DelayCount { get; private set; }
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DelayCount++;
            return Task.CompletedTask;
        }
    }
}
