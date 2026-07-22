using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class StartupSequenceServiceTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Startup service 01 fixed milestone order", FixedMilestoneOrder),
        ("Startup service 02 fixed milestone names", FixedMilestoneNames),
        ("Startup service 03 dormant snapshot", DormantSnapshot),
        ("Startup service 04 phase sequence is monotonic", PhaseSequenceIsMonotonic),
        ("Startup service 05 versions are monotonic", VersionsAreMonotonic),
        ("Startup service 06 same milestone does not republish", SameMilestoneDoesNotRepublish),
        ("Startup service 07 ready milestone is terminal", ReadyMilestoneIsTerminal),
        ("Startup service 08 partial milestone is terminal", PartialMilestoneIsTerminal),
        ("Startup service 09 failed milestone is terminal", FailedMilestoneIsTerminal),
        ("Startup service 10 sensor pending does not block commit", SensorPendingDoesNotBlockCommit),
        ("Startup service 11 history ready needs no samples", HistoryReadyNeedsNoSamples),
        ("Startup service 12 Off creates no delay", OffCreatesNoDelay),
        ("Startup service 13 Full uses one-shot delays", FullUsesOneShotDelays),
        ("Startup service 14 completed service cannot restart", CompletedServiceCannotRestart),
        ("Startup service 15 hidden window completes once", HiddenWindowCompletesOnce),
        ("Startup service 16 late non-error update is ignored", LateUpdateIsIgnored),
        ("Startup service 17 shell readiness is real milestone", ShellReadinessIsMilestone),
        ("Startup service 18 core readiness drives CanCommit", CoreReadinessDrivesCanCommit),
        ("Startup service 19 failure remains visible", FailureRemainsVisible),
        ("Startup service 20 snapshot holds no WPF controls", SnapshotHasNoWpfControls)
    ];

    private static void FixedMilestoneOrder()
    {
        StartupSequenceSnapshot snapshot = NewService().CurrentSnapshot;
        StartupMilestoneId[] expected =
        [
            StartupMilestoneId.ThemeResources,
            StartupMilestoneId.ServiceGraph,
            StartupMilestoneId.PageRouter,
            StartupMilestoneId.SensorBus,
            StartupMilestoneId.HistoryBuffer,
            StartupMilestoneId.ShellSurface
        ];
        TestSupport.True(expected.SequenceEqual(snapshot.Milestones.Select(item => item.Id)), "milestone order");
    }

    private static void FixedMilestoneNames()
    {
        string[] expected = ["THEME RESOURCES", "SERVICE GRAPH", "PAGE ROUTER", "SENSOR BUS", "HISTORY BUFFER", "SHELL SURFACE"];
        TestSupport.True(expected.SequenceEqual(NewService().CurrentSnapshot.Milestones.Select(item => item.Name)), "milestone names");
    }

    private static void DormantSnapshot()
    {
        StartupSequenceSnapshot snapshot = NewService().CurrentSnapshot;
        TestSupport.Equal(StartupSequencePhase.Dormant, snapshot.Phase, "dormant phase");
        TestSupport.False(snapshot.IsActive, "dormant active");
        TestSupport.False(snapshot.HasCompleted, "dormant complete");
    }

    private static void PhaseSequenceIsMonotonic()
    {
        using StartupSequenceService service = ReadyService(MotionLevel.Off, new CountingClock());
        List<StartupSequencePhase> phases = [];
        service.SnapshotChanged += (_, args) => phases.Add(args.CurrentSnapshot.Phase);
        service.StartAsync().GetAwaiter().GetResult();
        StartupSequencePhase[] expected =
        [StartupSequencePhase.Index, StartupSequencePhase.Route, StartupSequencePhase.Bind, StartupSequencePhase.Lock, StartupSequencePhase.Reveal, StartupSequencePhase.Complete];
        TestSupport.True(expected.SequenceEqual(phases), "phase sequence");
    }

    private static void VersionsAreMonotonic()
    {
        using StartupSequenceService service = ReadyService(MotionLevel.Off, new CountingClock());
        List<long> versions = [];
        service.SnapshotChanged += (_, args) => versions.Add(args.CurrentSnapshot.Version);
        service.StartAsync().GetAwaiter().GetResult();
        TestSupport.True(versions.Zip(versions.Skip(1), (left, right) => right > left).All(value => value), "versions");
    }

    private static void SameMilestoneDoesNotRepublish()
    {
        using StartupSequenceService service = NewService();
        int changes = 0;
        service.SnapshotChanged += (_, _) => changes++;
        TestSupport.True(service.ReportMilestone(StartupMilestoneId.ThemeResources, StartupMilestoneState.Ready, "ready"), "first update");
        TestSupport.False(service.ReportMilestone(StartupMilestoneId.ThemeResources, StartupMilestoneState.Ready, "ready"), "duplicate update");
        TestSupport.Equal(1, changes, "change count");
    }

    private static void ReadyMilestoneIsTerminal() => TerminalMilestone(StartupMilestoneState.Ready);
    private static void PartialMilestoneIsTerminal() => TerminalMilestone(StartupMilestoneState.Partial);
    private static void FailedMilestoneIsTerminal() => TerminalMilestone(StartupMilestoneState.Failed);

    private static void TerminalMilestone(StartupMilestoneState terminal)
    {
        using StartupSequenceService service = NewService();
        TestSupport.True(service.ReportMilestone(StartupMilestoneId.SensorBus, terminal), "terminal transition");
        TestSupport.False(service.ReportMilestone(StartupMilestoneId.SensorBus, StartupMilestoneState.Pending), "terminal regressed");
    }

    private static void SensorPendingDoesNotBlockCommit()
    {
        using StartupSequenceService service = ReadyService(MotionLevel.Off, new CountingClock(), sensorPending: true);
        service.StartAsync().GetAwaiter().GetResult();
        TestSupport.True(service.CurrentSnapshot.HasCompleted, "completed with sensor pending");
    }

    private static void HistoryReadyNeedsNoSamples()
    {
        using StartupSequenceService service = NewService();
        TestSupport.True(service.ReportMilestone(StartupMilestoneId.HistoryBuffer, StartupMilestoneState.Ready, "subscribed"), "history ready");
    }

    private static void OffCreatesNoDelay()
    {
        CountingClock clock = new();
        using StartupSequenceService service = ReadyService(MotionLevel.Off, clock);
        service.StartAsync().GetAwaiter().GetResult();
        TestSupport.Equal(0, clock.DelayCount, "Off delays");
    }

    private static void FullUsesOneShotDelays()
    {
        CountingClock clock = new();
        using StartupSequenceService service = ReadyService(MotionLevel.Full, clock);
        service.StartAsync().GetAwaiter().GetResult();
        TestSupport.Equal(5, clock.DelayCount, "Full one-shot delay count");
    }

    private static void CompletedServiceCannotRestart()
    {
        CountingClock clock = new();
        using StartupSequenceService service = ReadyService(MotionLevel.Full, clock);
        service.StartAsync().GetAwaiter().GetResult();
        service.StartAsync().GetAwaiter().GetResult();
        TestSupport.Equal(5, clock.DelayCount, "restarted delays");
    }

    private static void HiddenWindowCompletesOnce()
    {
        using StartupSequenceService service = NewService();
        int completed = 0;
        service.SnapshotChanged += (_, args) => completed += args.CurrentSnapshot.HasCompleted ? 1 : 0;
        service.CompleteForHiddenWindow();
        service.CompleteForHiddenWindow();
        TestSupport.Equal(1, completed, "hidden completion count");
    }

    private static void LateUpdateIsIgnored()
    {
        using StartupSequenceService service = NewService();
        service.CompleteForHiddenWindow();
        TestSupport.False(service.ReportMilestone(StartupMilestoneId.SensorBus, StartupMilestoneState.Ready), "late update");
    }

    private static void ShellReadinessIsMilestone()
    {
        using StartupSequenceService service = NewService();
        service.ReportMilestone(StartupMilestoneId.ShellSurface, StartupMilestoneState.Ready, "measured");
        TestSupport.True(service.CurrentSnapshot.ShellReady, "shell ready");
    }

    private static void CoreReadinessDrivesCanCommit()
    {
        using StartupSequenceService service = NewService();
        ReadyCore(service, includeShell: false);
        TestSupport.False(service.CurrentSnapshot.CanCommit, "commit before shell");
        service.ReportMilestone(StartupMilestoneId.ShellSurface, StartupMilestoneState.Ready);
        TestSupport.True(service.CurrentSnapshot.CanCommit, "commit after shell");
    }

    private static void FailureRemainsVisible()
    {
        using StartupSequenceService service = NewService();
        service.ReportMilestone(StartupMilestoneId.SensorBus, StartupMilestoneState.Failed, "provider failed");
        TestSupport.True(service.CurrentSnapshot.FailureMessage?.Contains("provider failed", StringComparison.Ordinal) == true, "failure detail");
    }

    private static void SnapshotHasNoWpfControls()
    {
        Type snapshotType = typeof(StartupSequenceSnapshot);
        TestSupport.False(snapshotType.GetProperties().Any(property =>
            typeof(System.Windows.DependencyObject).IsAssignableFrom(property.PropertyType)), "WPF reference");
    }

    private static StartupSequenceService NewService() =>
        new(AppTheme.Tracework, MotionLevel.Standard, new CountingClock());

    private static StartupSequenceService ReadyService(
        MotionLevel level,
        CountingClock clock,
        bool sensorPending = false)
    {
        StartupSequenceService service = new(AppTheme.Tracework, level, clock);
        ReadyCore(service, includeShell: true);
        service.ReportMilestone(StartupMilestoneId.HistoryBuffer, StartupMilestoneState.Ready);
        service.ReportMilestone(
            StartupMilestoneId.SensorBus,
            sensorPending ? StartupMilestoneState.Pending : StartupMilestoneState.Ready);
        return service;
    }

    private static void ReadyCore(StartupSequenceService service, bool includeShell)
    {
        service.ReportMilestone(StartupMilestoneId.ThemeResources, StartupMilestoneState.Ready);
        service.ReportMilestone(StartupMilestoneId.ServiceGraph, StartupMilestoneState.Ready);
        service.ReportMilestone(StartupMilestoneId.PageRouter, StartupMilestoneState.Ready);
        if (includeShell)
        {
            service.ReportMilestone(StartupMilestoneId.ShellSurface, StartupMilestoneState.Ready);
        }
    }

    private sealed class CountingClock : IStartupSequenceClock
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
