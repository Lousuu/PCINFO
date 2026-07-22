using System.Windows.Threading;
using HardwareVision.Controls;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class MotionInfrastructureTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Motion 01 parser and storage normalization", ParserAndStorageNormalization),
        ("Motion 02 settings default and missing field migration", TestSupport.Run(SettingsDefaultAndMigrationAsync)),
        ("Motion 03 effective-level downgrade matrix", EffectiveLevelDowngradeMatrix),
        ("Motion 04 environment restoration preserves requested level", EnvironmentRestorationPreservesRequestedLevel),
        ("Motion 05 MotionChanged event semantics", MotionChangedEventSemantics),
        ("Motion 06 MotionService dispose stops events", MotionServiceDisposeStopsEvents),
        ("Motion 07 transition plans match profiles", TransitionPlansMatchProfiles),
        ("Motion 08 transition plan gates disabled states", TransitionPlanGatesDisabledStates),
        ("Motion 09 MotionContext default values are safe", MotionContextDefaultsAreSafe),
        ("Motion 10 Settings selection writes once", SettingsSelectionWritesOnce),
        ("Motion 11 Settings environment downgrade does not save", SettingsEnvironmentDowngradeDoesNotSave),
        ("Motion 12 Settings motion and theme are independent", SettingsMotionAndThemeAreIndependent),
        ("Motion 13 Settings save failure keeps session level", SettingsSaveFailureKeepsSessionLevel),
        ("Motion 14 static architecture constraints", StaticArchitectureConstraints)
    ];

    private static void ParserAndStorageNormalization()
    {
        TestSupport.Equal(MotionLevel.Full, MotionLevelParser.Parse("Full"), "parse Full");
        TestSupport.Equal(MotionLevel.Standard, MotionLevelParser.Parse("Standard"), "parse Standard");
        TestSupport.Equal(MotionLevel.Reduced, MotionLevelParser.Parse("Reduced"), "parse Reduced");
        TestSupport.Equal(MotionLevel.Off, MotionLevelParser.Parse("Off"), "parse Off");
        TestSupport.Equal(MotionLevel.Full, MotionLevelParser.Parse("  fUlL  "), "parse trim and case");
        TestSupport.Equal(MotionLevel.Standard, MotionLevelParser.Parse(null), "parse null");
        TestSupport.Equal(MotionLevel.Standard, MotionLevelParser.Parse(" "), "parse blank");
        TestSupport.Equal(MotionLevel.Standard, MotionLevelParser.Parse("Arcade"), "parse unknown");
        TestSupport.Equal("Full", MotionLevelParser.ToStorageValue(MotionLevel.Full), "store Full");
        TestSupport.Equal("Standard", MotionLevelParser.ToStorageValue(MotionLevel.Standard), "store Standard");
        TestSupport.Equal("Reduced", MotionLevelParser.ToStorageValue(MotionLevel.Reduced), "store Reduced");
        TestSupport.Equal("Off", MotionLevelParser.ToStorageValue(MotionLevel.Off), "store Off");
    }

    private static async Task SettingsDefaultAndMigrationAsync()
    {
        await TestSupport.InTemporaryDirectory(async directory =>
        {
            SettingsService defaultsService = new(directory);
            AppSettings defaults = await defaultsService.LoadAsync();
            TestSupport.Equal("Standard", defaults.Motion, "default motion");

            string settingsPath = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(settingsPath, """{"Theme":"Tracework","RefreshIntervalSeconds":2.0}""");
            SettingsService reader = new(directory);
            AppSettings migrated = await reader.LoadAsync();
            TestSupport.Equal("Standard", migrated.Motion, "missing motion migrates to Standard");
            TestSupport.Equal("Tracework", migrated.Theme, "theme unaffected");

            migrated.Motion = " reduced ";
            await reader.SaveAsync(migrated);
            AppSettings reloaded = await reader.LoadAsync();
            TestSupport.Equal("Reduced", reloaded.Motion, "stored motion normalized");
            TestSupport.Equal(2.0d, reloaded.RefreshIntervalSeconds, "other setting unaffected");
        });
    }

    private static void EffectiveLevelDowngradeMatrix()
    {
        TestSupport.Equal(MotionLevel.Off, CreateService(MotionLevel.Off).EffectiveLevel, "requested Off");
        TestSupport.Equal(MotionLevel.Off, CreateService(MotionLevel.Full, animations: false).EffectiveLevel, "Windows animations disabled");
        TestSupport.Equal(MotionLevel.Off, CreateService(MotionLevel.Standard, tier: 0).EffectiveLevel, "tier 0 disabled");
        TestSupport.Equal(MotionLevel.Reduced, CreateService(MotionLevel.Full, tier: 1).EffectiveLevel, "tier 1 caps Full");
        TestSupport.Equal(MotionLevel.Reduced, CreateService(MotionLevel.Standard, tier: 1).EffectiveLevel, "tier 1 caps Standard");
        TestSupport.Equal(MotionLevel.Reduced, CreateService(MotionLevel.Reduced, tier: 1).EffectiveLevel, "tier 1 keeps Reduced");
        TestSupport.Equal(MotionLevel.Reduced, CreateService(MotionLevel.Full, highContrast: true).EffectiveLevel, "high contrast caps");
        TestSupport.Equal(MotionLevel.Reduced, CreateService(MotionLevel.Full, remote: true).EffectiveLevel, "remote caps");
        TestSupport.Equal(MotionLevel.Full, CreateService(MotionLevel.Full, tier: 2).EffectiveLevel, "tier 2 Full");
        TestSupport.Equal(MotionLevel.Standard, CreateService(MotionLevel.Standard, tier: 2).EffectiveLevel, "tier 2 Standard");
    }

    private static void EnvironmentRestorationPreservesRequestedLevel()
    {
        FakeMotionEnvironment environment = new() { IsRemoteSession = true };
        using MotionService service = new(environment, MotionLevel.Full, Dispatcher.CurrentDispatcher);
        TestSupport.Equal(MotionLevel.Full, service.RequestedLevel, "requested remains Full while remote");
        TestSupport.Equal(MotionLevel.Reduced, service.EffectiveLevel, "remote effective Reduced");

        environment.IsRemoteSession = false;
        environment.RaiseChanged();

        TestSupport.Equal(MotionLevel.Full, service.RequestedLevel, "requested remains Full after restore");
        TestSupport.Equal(MotionLevel.Full, service.EffectiveLevel, "effective restores Full");
    }

    private static void MotionChangedEventSemantics()
    {
        FakeMotionEnvironment environment = new();
        using MotionService service = new(environment, MotionLevel.Standard, Dispatcher.CurrentDispatcher);
        int events = 0;
        MotionChangedEventArgs? observed = null;
        bool dispatcherThread = false;
        service.MotionChanged += (_, args) =>
        {
            events++;
            observed = args;
            dispatcherThread = Dispatcher.CurrentDispatcher.CheckAccess();
        };

        TestSupport.True(service.SetRequestedLevel(MotionLevel.Full), "requested change result");
        TestSupport.Equal(1, events, "requested change event count");
        TestSupport.Equal(MotionChangeReason.RequestedLevelChanged, TestSupport.NotNull(observed, "requested event").ChangeReason, "requested reason");
        TestSupport.True(dispatcherThread, "event on Dispatcher thread");

        TestSupport.False(service.SetRequestedLevel(MotionLevel.Full), "repeated set result");
        TestSupport.Equal(1, events, "repeated set no event");

        environment.IsHighContrast = true;
        environment.RaiseChanged();
        TestSupport.Equal(2, events, "effective downgrade event");
        MotionChangedEventArgs environmentEvent = TestSupport.NotNull(observed, "environment event");
        TestSupport.Equal(MotionLevel.Full, environmentEvent.CurrentRequestedLevel, "requested in environment event");
        TestSupport.Equal(MotionLevel.Reduced, environmentEvent.CurrentEffectiveLevel, "effective in environment event");

        environment.IsRemoteSession = true;
        environment.IsHighContrast = false;
        environment.RaiseChanged();
        TestSupport.Equal(2, events, "same effective no event");
    }

    private static void MotionServiceDisposeStopsEvents()
    {
        FakeMotionEnvironment environment = new();
        MotionService service = new(environment, MotionLevel.Standard, Dispatcher.CurrentDispatcher);
        int events = 0;
        service.MotionChanged += (_, _) => events++;
        service.Dispose();

        environment.RenderTier = 0;
        environment.RaiseChanged();

        TestSupport.Equal(0, events, "disposed service event count");
    }

    private static void TransitionPlansMatchProfiles()
    {
        MotionTransitionPlan full = CreatePlan(MotionLevel.Full);
        TestSupport.True(full.ShouldAnimate, "Full animates");
        TestSupport.True(full.AnimatesTranslation, "Full translates");
        TestSupport.Equal(TimeSpan.FromMilliseconds(220), full.Duration, "Full duration");
        TestSupport.Equal(0.52d, full.StartOpacity, "Full start opacity");
        TestSupport.Equal(8d, full.Offset, "Full offset");

        MotionTransitionPlan standard = CreatePlan(MotionLevel.Standard);
        TestSupport.Equal(TimeSpan.FromMilliseconds(175), standard.Duration, "Standard duration");
        TestSupport.Equal(0.66d, standard.StartOpacity, "Standard start opacity");
        TestSupport.Equal(5d, standard.Offset, "Standard offset");

        MotionTransitionPlan reduced = CreatePlan(MotionLevel.Reduced);
        TestSupport.True(reduced.ShouldAnimate, "Reduced animates");
        TestSupport.False(reduced.AnimatesTranslation, "Reduced does not translate");
        TestSupport.Equal(TimeSpan.FromMilliseconds(105), reduced.Duration, "Reduced duration");
        TestSupport.Equal(0.84d, reduced.StartOpacity, "Reduced start opacity");
        TestSupport.Equal(0d, reduced.Offset, "Reduced offset");

        MotionTransitionPlan off = CreatePlan(MotionLevel.Off);
        TestSupport.False(off.ShouldAnimate, "Off does not animate");
        TestSupport.Equal(TimeSpan.Zero, off.Duration, "Off duration");
    }

    private static void TransitionPlanGatesDisabledStates()
    {
        MotionProfile profile = MotionProfile.Create(MotionLevel.Standard, MotionLevel.Standard, string.Empty);
        TestSupport.False(MotionTransitionPlanFactory.Create(profile, false, true, true, true, MotionTransitionDirection.FromRight).ShouldAnimate, "disabled gate");
        TestSupport.False(MotionTransitionPlanFactory.Create(profile, true, false, true, true, MotionTransitionDirection.FromRight).ShouldAnimate, "unloaded gate");
        TestSupport.False(MotionTransitionPlanFactory.Create(profile, true, true, false, true, MotionTransitionDirection.FromRight).ShouldAnimate, "invisible gate");
        TestSupport.False(MotionTransitionPlanFactory.Create(profile, true, true, true, false, MotionTransitionDirection.FromRight).ShouldAnimate, "window invisible gate");
    }

    private static void MotionContextDefaultsAreSafe()
    {
        System.Windows.Controls.Border element = new();
        TestSupport.Equal(MotionLevel.Standard, HardwareVision.Themes.MotionContext.GetRequestedLevel(element), "default requested");
        TestSupport.Equal(MotionLevel.Standard, HardwareVision.Themes.MotionContext.GetEffectiveLevel(element), "default effective");
        TestSupport.True(HardwareVision.Themes.MotionContext.GetIsAnimationEnabled(element), "default animation flag");
        TestSupport.True(HardwareVision.Themes.MotionContext.GetAllowsSpatialMotion(element), "default spatial flag");
        TestSupport.Equal(MotionLevel.Standard, HardwareVision.Themes.MotionContext.GetCurrentProfile(element).EffectiveLevel, "default profile");
    }

    private static void SettingsSelectionWritesOnce()
    {
        using SettingsMotionFixture fixture = new();

        fixture.ViewModel.SelectMotionLevelCommand.Execute(fixture.ViewModel.MotionOptions[0]);

        TestSupport.Equal(1, fixture.SettingsService.SaveCount, "motion save count");
        TestSupport.Equal(0, fixture.ThemeService.ApplyCount, "theme apply count");
        TestSupport.Equal("Full", fixture.Settings.Motion, "stored motion");
        TestSupport.Equal(MotionLevel.Full, fixture.MotionService.RequestedLevel, "session requested");
    }

    private static void SettingsEnvironmentDowngradeDoesNotSave()
    {
        using SettingsMotionFixture fixture = new(requestedLevel: MotionLevel.Full);
        fixture.SettingsService.Reset();

        fixture.MotionEnvironment.IsRemoteSession = true;
        fixture.MotionEnvironment.RaiseChanged();

        TestSupport.Equal(0, fixture.SettingsService.SaveCount, "save count after downgrade");
        TestSupport.Equal(MotionLevel.Full, fixture.ViewModel.RequestedMotionLevel, "requested after downgrade");
        TestSupport.Equal(MotionLevel.Reduced, fixture.ViewModel.EffectiveMotionLevel, "effective after downgrade");
    }

    private static void SettingsMotionAndThemeAreIndependent()
    {
        using SettingsMotionFixture fixture = new();

        fixture.ViewModel.SelectMotionLevelCommand.Execute(fixture.ViewModel.MotionOptions[2]);

        TestSupport.Equal(0, fixture.ThemeService.ApplyCount, "theme not called by motion");
        TestSupport.Equal(AppTheme.Classic, fixture.ThemeService.CurrentTheme, "theme unchanged");
    }

    private static void SettingsSaveFailureKeepsSessionLevel()
    {
        AppSettings settings = new() { Motion = "Standard" };
        FailingSaveSettingsService settingsService = new(settings);
        TestThemeService themeService = new(AppTheme.Classic);
        using PollingService polling = new(new CountingSensorService(), settings);
        using CsvGameSessionRecorder recorder = new(Path.Combine(Path.GetTempPath(), "HardwareVision.Tests", Guid.NewGuid().ToString("N")), 8);
        using MotionService motion = new(new FakeMotionEnvironment(), MotionLevel.Standard, Dispatcher.CurrentDispatcher);
        using SettingsViewModel viewModel = new(
            settings,
            settingsService,
            themeService,
            motion,
            new NoopStartupService(),
            polling,
            new SensorDiagnosticService(),
            Dispatcher.CurrentDispatcher,
            () => { },
            recorder);

        viewModel.SelectMotionLevelCommand.Execute(viewModel.MotionOptions[0]);

        TestSupport.Equal(MotionLevel.Full, motion.RequestedLevel, "session requested after failed save");
        TestSupport.Equal("Full", settings.Motion, "in-memory setting after failed save");
        TestSupport.True(viewModel.MotionStatusText.Contains("无法保存", StringComparison.Ordinal), "failure warning");
    }

    private static void StaticArchitectureConstraints()
    {
        string root = FindRepositoryRoot();
        string app = Path.Combine(root, "HardwareVision");
        string allProductText = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(path => File.ReadAllText(path)));

        TestSupport.False(allProductText.Contains("RepeatBehavior=\"Forever\"", StringComparison.Ordinal), "no infinite XAML motion");
        TestSupport.False(allProductText.Contains("AutoReverse=\"True\"", StringComparison.Ordinal), "no autoreverse motion");
        TestSupport.False(allProductText.Contains("CompositionTarget.Rendering", StringComparison.Ordinal), "no rendering loop");
        TestSupport.False(allProductText.Contains("PixelShader", StringComparison.Ordinal), "no shader");
        TestSupport.False(allProductText.Contains("BlurEffect", StringComparison.Ordinal), "no blur");

        string motionXaml = File.ReadAllText(Path.Combine(app, "Themes", "Tracework", "Motion.xaml"));
        TestSupport.False(motionXaml.Contains("<Style TargetType", StringComparison.Ordinal), "Motion resources use keyed styles");
        TestSupport.False(motionXaml.Contains("LayoutTransform", StringComparison.Ordinal), "no layout transform in Motion resources");
    }

    private static MotionService CreateService(
        MotionLevel requested,
        bool animations = true,
        bool highContrast = false,
        bool remote = false,
        int tier = 2)
    {
        FakeMotionEnvironment environment = new()
        {
            AreClientAnimationsEnabled = animations,
            IsHighContrast = highContrast,
            IsRemoteSession = remote,
            RenderTier = tier
        };
        return new MotionService(environment, requested, Dispatcher.CurrentDispatcher);
    }

    private static MotionTransitionPlan CreatePlan(MotionLevel level)
    {
        MotionProfile profile = MotionProfile.Create(level, level, string.Empty);
        return MotionTransitionPlanFactory.Create(
            profile,
            isTransitionEnabled: true,
            isLoaded: true,
            isVisible: true,
            isWindowVisible: true,
            MotionTransitionDirection.FromRight);
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

    private sealed class SettingsMotionFixture : IDisposable
    {
        public SettingsMotionFixture(MotionLevel requestedLevel = MotionLevel.Standard)
        {
            Settings = new AppSettings { Motion = MotionLevelParser.ToStorageValue(requestedLevel) };
            SettingsService = new ResettableSettingsService(Settings);
            ThemeService = new TestThemeService(AppTheme.Classic);
            MotionEnvironment = new FakeMotionEnvironment();
            MotionService = new MotionService(MotionEnvironment, requestedLevel, Dispatcher.CurrentDispatcher);
            PollingService = new PollingService(new CountingSensorService(), Settings);
            Recorder = new CsvGameSessionRecorder(
                Path.Combine(Path.GetTempPath(), "HardwareVision.Tests", Guid.NewGuid().ToString("N")),
                8);
            ViewModel = new SettingsViewModel(
                Settings,
                SettingsService,
                ThemeService,
                MotionService,
                new NoopStartupService(),
                PollingService,
                new SensorDiagnosticService(),
                Dispatcher.CurrentDispatcher,
                () => { },
                Recorder);
        }

        public AppSettings Settings { get; }
        public ResettableSettingsService SettingsService { get; }
        public TestThemeService ThemeService { get; }
        public FakeMotionEnvironment MotionEnvironment { get; }
        public MotionService MotionService { get; }
        public PollingService PollingService { get; }
        public CsvGameSessionRecorder Recorder { get; }
        public SettingsViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            PollingService.Dispose();
            MotionService.Dispose();
            Recorder.Dispose();
        }
    }

    private sealed class ResettableSettingsService(AppSettings settings) : ISettingsService
    {
        public int SaveCount { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
        {
            updateAction(settings);
            return Task.FromResult(settings);
        }

        public void Reset() => SaveCount = 0;
    }
}
