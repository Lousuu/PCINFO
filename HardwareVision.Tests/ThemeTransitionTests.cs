using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class ThemeTransitionTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Theme transition 01 Full plan duration is bounded", FullPlanDurationIsBounded),
        ("Theme transition 02 Standard plan duration is bounded", StandardPlanDurationIsBounded),
        ("Theme transition 03 Reduced plan is fade-only and short", ReducedPlanIsFadeOnlyAndShort),
        ("Theme transition 04 Off bypasses overlay clock", TestSupport.Run(OffBypassesOverlayClockAsync)),
        ("Theme transition 05 Standard publishes Trace Latch Splice Idle", TestSupport.Run(StandardPublishesPhaseSequenceAsync)),
        ("Theme transition 06 Latch is the only commit point", TestSupport.Run(LatchIsOnlyCommitPointAsync)),
        ("Theme transition 07 failed apply publishes fault and does not commit", TestSupport.Run(FailedApplyPublishesFaultAsync)),
        ("Theme transition 08 already current is not animated", TestSupport.Run(AlreadyCurrentIsNotAnimatedAsync)),
        ("Theme transition 09 duplicate target reuses active task", TestSupport.Run(DuplicateTargetReusesActiveTaskAsync)),
        ("Theme transition 10 result persistence flag is narrow", ResultPersistenceFlagIsNarrow),
        ("Theme transition 11 settings saves only committed transitions", TestSupport.Run(SettingsSavesOnlyCommittedTransitionsAsync)),
        ("Theme transition 12 static side-effect boundaries hold", StaticSideEffectBoundariesHold)
    ];

    private static void FullPlanDurationIsBounded()
    {
        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(
            MotionProfile.Create(MotionLevel.Full, MotionLevel.Full, string.Empty));

        TestSupport.True(plan.IsOverlayEnabled, "Full overlay enabled");
        TestSupport.True(plan.AllowsTraceTranslation, "Full spatial trace");
        TestSupport.True(plan.AllowsSegmentMotion, "Full segment motion");
        TestSupport.True(plan.TotalDuration >= TimeSpan.FromMilliseconds(720), "Full minimum duration");
        TestSupport.True(plan.TotalDuration <= TimeSpan.FromMilliseconds(820), "Full maximum duration");
    }

    private static void StandardPlanDurationIsBounded()
    {
        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(
            MotionProfile.Create(MotionLevel.Standard, MotionLevel.Standard, string.Empty));

        TestSupport.Equal(MotionLevel.Standard, plan.EffectiveLevel, "Standard effective level");
        TestSupport.True(plan.IsOverlayEnabled, "Standard overlay enabled");
        TestSupport.True(plan.TotalDuration >= TimeSpan.FromMilliseconds(520), "Standard minimum duration");
        TestSupport.True(plan.TotalDuration <= TimeSpan.FromMilliseconds(590), "Standard maximum duration");
    }

    private static void ReducedPlanIsFadeOnlyAndShort()
    {
        ThemeTransitionPlan plan = ThemeTransitionPlan.Create(
            MotionProfile.Create(MotionLevel.Full, MotionLevel.Reduced, "Remote session"));

        TestSupport.True(plan.IsOverlayEnabled, "Reduced overlay enabled");
        TestSupport.False(plan.AllowsTraceTranslation, "Reduced disables translation");
        TestSupport.False(plan.AllowsSegmentMotion, "Reduced disables segment motion");
        TestSupport.False(plan.ShowsSystemRewireLabel, "Reduced hides label");
        TestSupport.True(plan.TotalDuration <= TimeSpan.FromMilliseconds(160), "Reduced duration bound");
    }

    private static async Task OffBypassesOverlayClockAsync()
    {
        TestThemeService theme = new(AppTheme.Classic);
        using MotionService motion = CreateMotion(MotionLevel.Off);
        CountingClock clock = new();
        ThemeTransitionService service = new(theme, motion, Dispatcher.CurrentDispatcher, clock);
        int eventCount = 0;
        int activeEventCount = 0;
        service.TransitionChanged += (_, e) =>
        {
            eventCount++;
            if (e.CurrentSnapshot.IsActive)
            {
                activeEventCount++;
            }
        };

        ThemeTransitionResult result = await service.ApplyThemeAsync(AppTheme.Tracework);

        TestSupport.Equal(ThemeTransitionStatus.Applied, result.Status, "Off result");
        TestSupport.Equal(AppTheme.Tracework, theme.CurrentTheme, "Off applied theme");
        TestSupport.Equal(1, theme.ApplyCount, "Off apply count");
        TestSupport.Equal(0, clock.DelayCount, "Off clock delays");
        TestSupport.Equal(1, eventCount, "Off idle publication");
        TestSupport.Equal(0, activeEventCount, "Off active overlay events");
    }

    private static async Task StandardPublishesPhaseSequenceAsync()
    {
        TestThemeService theme = new(AppTheme.Classic);
        using MotionService motion = CreateMotion(MotionLevel.Standard);
        ThemeTransitionService service = new(theme, motion, Dispatcher.CurrentDispatcher, new ImmediateThemeTransitionClock());
        List<ThemeTransitionPhase> phases = [];
        service.TransitionChanged += (_, e) => phases.Add(e.CurrentSnapshot.Phase);

        ThemeTransitionResult result = await service.ApplyThemeAsync(AppTheme.Tracework);

        TestSupport.Equal(ThemeTransitionStatus.Applied, result.Status, "transition status");
        TestSupport.Equal(
            string.Join(",", new[] { ThemeTransitionPhase.Trace, ThemeTransitionPhase.Latch, ThemeTransitionPhase.Splice, ThemeTransitionPhase.Idle }),
            string.Join(",", phases),
            "phase order");
    }

    private static async Task LatchIsOnlyCommitPointAsync()
    {
        TestThemeService theme = new(AppTheme.Classic);
        using MotionService motion = CreateMotion(MotionLevel.Standard);
        ThemeTransitionService service = new(theme, motion, Dispatcher.CurrentDispatcher, new ImmediateThemeTransitionClock());
        List<(ThemeTransitionPhase Phase, int ApplyCount)> observations = [];
        service.TransitionChanged += (_, e) => observations.Add((e.CurrentSnapshot.Phase, theme.ApplyCount));

        await service.ApplyThemeAsync(AppTheme.Tracework);

        TestSupport.Equal(0, observations.Single(item => item.Phase == ThemeTransitionPhase.Trace).ApplyCount, "Trace before commit");
        TestSupport.Equal(0, observations.Single(item => item.Phase == ThemeTransitionPhase.Latch).ApplyCount, "Latch snapshot before commit");
        TestSupport.Equal(1, observations.Single(item => item.Phase == ThemeTransitionPhase.Splice).ApplyCount, "Splice after commit");
    }

    private static async Task FailedApplyPublishesFaultAsync()
    {
        TestThemeService theme = new(AppTheme.Classic, failTheme: AppTheme.Tracework);
        using MotionService motion = CreateMotion(MotionLevel.Standard);
        ThemeTransitionService service = new(theme, motion, Dispatcher.CurrentDispatcher, new ImmediateThemeTransitionClock());
        List<ThemeTransitionSnapshot> snapshots = [];
        service.TransitionChanged += (_, e) => snapshots.Add(e.CurrentSnapshot);

        ThemeTransitionResult result = await service.ApplyThemeAsync(AppTheme.Tracework);

        TestSupport.Equal(ThemeTransitionStatus.Failed, result.Status, "failed status");
        TestSupport.Equal(AppTheme.Classic, theme.CurrentTheme, "failed preserves theme");
        TestSupport.True(snapshots.Any(snapshot => snapshot.Phase == ThemeTransitionPhase.Failed), "fault snapshot");
        TestSupport.False(result.ShouldPersist, "failed not persisted");
    }

    private static async Task AlreadyCurrentIsNotAnimatedAsync()
    {
        TestThemeService theme = new(AppTheme.Tracework);
        using MotionService motion = CreateMotion(MotionLevel.Standard);
        CountingClock clock = new();
        ThemeTransitionService service = new(theme, motion, Dispatcher.CurrentDispatcher, clock);

        ThemeTransitionResult result = await service.ApplyThemeAsync(AppTheme.Tracework);

        TestSupport.Equal(ThemeTransitionStatus.AlreadyCurrent, result.Status, "already current status");
        TestSupport.Equal(0, theme.ApplyCount, "already current apply count");
        TestSupport.Equal(0, clock.DelayCount, "already current delays");
    }

    private static async Task DuplicateTargetReusesActiveTaskAsync()
    {
        TestThemeService theme = new(AppTheme.Classic);
        using MotionService motion = CreateMotion(MotionLevel.Standard);
        BlockingClock clock = new();
        ThemeTransitionService service = new(theme, motion, Dispatcher.CurrentDispatcher, clock);

        Task<ThemeTransitionResult> first = service.ApplyThemeAsync(AppTheme.Tracework);
        Task<ThemeTransitionResult> second = service.ApplyThemeAsync(AppTheme.Tracework);

        TestSupport.True(ReferenceEquals(first, second), "same target task");
        service.Cancel();
        clock.ReleaseAll();
        ThemeTransitionResult result = await first;
        TestSupport.Equal(ThemeTransitionStatus.Superseded, result.Status, "duplicate canceled result");
        TestSupport.Equal(0, theme.ApplyCount, "duplicate canceled before apply");
    }

    private static void ResultPersistenceFlagIsNarrow()
    {
        TestSupport.True(ThemeTransitionResult.Applied(AppTheme.Classic, AppTheme.Tracework).ShouldPersist, "applied persists");
        TestSupport.False(ThemeTransitionResult.AlreadyCurrent(AppTheme.Classic).ShouldPersist, "already current not persisted");
        TestSupport.False(ThemeTransitionResult.Failed(AppTheme.Classic, AppTheme.Tracework).ShouldPersist, "failed not persisted");
        TestSupport.False(ThemeTransitionResult.Superseded(AppTheme.Classic, AppTheme.Tracework, wasThemeCommitted: false).ShouldPersist, "superseded not persisted");
        TestSupport.False(ThemeTransitionResult.Cancelled(AppTheme.Classic, AppTheme.Tracework, wasThemeCommitted: false).ShouldPersist, "cancelled not persisted");
    }

    private static async Task SettingsSavesOnlyCommittedTransitionsAsync()
    {
        await TestSupport.InTemporaryDirectory(async directory =>
        {
            AppSettings settings = new() { Theme = "Classic" };
            CountingSettingsService settingsService = new(settings);
            TestThemeService theme = new(AppTheme.Classic);
            using MotionService motion = CreateMotion(MotionLevel.Standard);
            ThemeTransitionService transition = new(theme, motion, Dispatcher.CurrentDispatcher, new ImmediateThemeTransitionClock());
            using PollingService polling = new(new CountingSensorService(), settings);
            using CsvGameSessionRecorder recorder = new(Path.Combine(directory, "sessions"), 8);
            using SettingsViewModel viewModel = new(
                settings,
                settingsService,
                theme,
                motion,
                transition,
                new NoopStartupService(),
                polling,
                new SensorDiagnosticService(),
                Dispatcher.CurrentDispatcher,
                () => { },
                recorder);

            await viewModel.SelectThemeCommand.ExecuteAsync(viewModel.TraceworkTheme);
            await viewModel.SelectThemeCommand.ExecuteAsync(viewModel.TraceworkTheme);

            TestSupport.Equal(AppTheme.Tracework, theme.CurrentTheme, "settings applied theme");
            TestSupport.Equal("Tracework", settings.Theme, "settings stored theme");
            TestSupport.Equal(1, settingsService.SaveCount, "settings save count");
        });
    }

    private static void StaticSideEffectBoundariesHold()
    {
        string root = FindRepositoryRoot();
        string services = Path.Combine(root, "HardwareVision", "Services");
        string themeService = File.ReadAllText(Path.Combine(services, "ThemeService.cs"));
        string motionService = File.ReadAllText(Path.Combine(services, "MotionService.cs"));
        string settingsViewModel = File.ReadAllText(Path.Combine(root, "HardwareVision", "ViewModels", "SettingsViewModel.cs"));
        string transitionService = File.ReadAllText(Path.Combine(services, "ThemeTransitionService.cs"));

        TestSupport.False(themeService.Contains("ThemeTransition", StringComparison.Ordinal), "ThemeService unaware of transition service");
        TestSupport.False(motionService.Contains("ThemeTransition", StringComparison.Ordinal), "MotionService unaware of transition service");
        TestSupport.False(settingsViewModel.Contains("themeService.ApplyTheme", StringComparison.Ordinal), "Settings does not apply themes directly");
        TestSupport.True(transitionService.Contains("themeService.ApplyTheme", StringComparison.Ordinal), "transition service owns latch apply");
    }

    private static MotionService CreateMotion(MotionLevel level)
    {
        return new MotionService(new FakeMotionEnvironment(), level, Dispatcher.CurrentDispatcher);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? candidate = new(Directory.GetCurrentDirectory());
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class CountingClock : IThemeTransitionClock
    {
        public int DelayCount { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelayCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingClock : IThemeTransitionClock
    {
        private readonly List<TaskCompletionSource> pending = [];
        private bool isReleased;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (isReleased)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(source);
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            return source.Task;
        }

        public void ReleaseAll()
        {
            isReleased = true;
            while (pending.Count > 0)
            {
                TaskCompletionSource source = pending[0];
                pending.RemoveAt(0);
                source.TrySetResult();
            }
        }
    }
}
