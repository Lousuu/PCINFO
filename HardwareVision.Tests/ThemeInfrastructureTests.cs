using System.Windows;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Themes;
using HardwareVision.ViewModels;
using HardwareVision.Views;

namespace HardwareVision.Tests;

internal static class ThemeInfrastructureTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Theme 01 Classic parses", () => Parse("Classic", AppTheme.Classic)),
        ("Theme 02 Tracework parses", () => Parse("Tracework", AppTheme.Tracework)),
        ("Theme 03 Dark migrates to Classic", () => Parse("Dark", AppTheme.Classic)),
        ("Theme 04 null migrates to Classic", () => Parse(null, AppTheme.Classic)),
        ("Theme 05 empty migrates to Classic", () => Parse(string.Empty, AppTheme.Classic)),
        ("Theme 06 whitespace migrates to Classic", () => Parse("   ", AppTheme.Classic)),
        ("Theme 07 unknown migrates to Classic", () => Parse("Neon", AppTheme.Classic)),
        ("Theme 08 parsing ignores case", () => Parse("tRaCeWoRk", AppTheme.Tracework)),
        ("Theme 09 parsing trims whitespace", () => Parse("  Tracework  ", AppTheme.Tracework)),
        ("Theme 10 Tracework settings round trip", TestSupport.Run(TraceworkSettingsRoundTripAsync)),
        ("Theme 11 Classic resources load", () => ThemeResourcesLoad(AppTheme.Classic)),
        ("Theme 12 Tracework resources load", () => ThemeResourcesLoad(AppTheme.Tracework)),
        ("Theme 13 resource key sets match", ThemeResourceKeySetsMatch),
        ("Theme 14 settings view loads at minimum size", SettingsViewLoadsAtMinimumSize),
        ("Theme 15 repeated switching keeps one dictionary", RepeatedSwitchingKeepsOneDictionary),
        ("Theme 16 failed load preserves previous theme", FailedLoadPreservesPreviousTheme),
        ("Theme 17 failed apply is not persisted", FailedApplyIsNotPersisted),
        ("Theme 18 save failure keeps applied theme with warning", SaveFailureKeepsAppliedThemeWithWarning),
        ("Theme 19 Classic to Tracework raises one event on Dispatcher", ClassicToTraceworkRaisesOneEventOnDispatcher),
        ("Theme 20 Tracework to Classic raises one event", TraceworkToClassicRaisesOneEvent),
        ("Theme 21 repeated apply raises no event", RepeatedApplyRaisesNoEvent),
        ("Theme 22 failed load raises no event", FailedLoadRaisesNoEvent),
        ("Theme 23 failed replacement rolls back without event", FailedReplacementRollsBackWithoutEvent)
    ];

    private static void Parse(string? value, AppTheme expected) =>
        TestSupport.Equal(expected, AppThemeParser.Parse(value), $"parsed theme for '{value ?? "<null>"}'");

    private static Task TraceworkSettingsRoundTripAsync() => TestSupport.InTemporaryDirectory(async directory =>
    {
        SettingsService writer = new(directory);
        AppSettings settings = await writer.LoadAsync();
        settings.Theme = AppThemeParser.ToStorageValue(AppTheme.Tracework);
        await writer.SaveAsync(settings);

        SettingsService reader = new(directory);
        AppSettings reloaded = await reader.LoadAsync();
        TestSupport.Equal("Tracework", reloaded.Theme, "persisted theme");
        TestSupport.Equal(AppTheme.Tracework, AppThemeParser.Parse(reloaded.Theme), "reloaded theme");
    });

    private static void ThemeResourcesLoad(AppTheme theme)
    {
        ThemeResourceDictionary dictionary = ThemeService.LoadThemeDictionary(theme);
        IReadOnlyCollection<object> keys = ThemeService.GetEffectiveResourceKeys(dictionary);
        TestSupport.True(keys.Contains("AppBackgroundBrush"), $"{theme} AppBackgroundBrush");
        TestSupport.True(keys.Contains("AppFontFamily"), $"{theme} font family");
    }

    private static void ThemeResourceKeySetsMatch()
    {
        HashSet<object> classic = [.. ThemeService.GetEffectiveResourceKeys(ThemeService.LoadThemeDictionary(AppTheme.Classic))];
        HashSet<object> tracework = [.. ThemeService.GetEffectiveResourceKeys(ThemeService.LoadThemeDictionary(AppTheme.Tracework))];
        TestSupport.True(classic.SetEquals(tracework),
            $"theme resource keys differ; classic-only={string.Join(',', classic.Except(tracework))}; tracework-only={string.Join(',', tracework.Except(classic))}");
    }

    private static void RepeatedSwitchingKeepsOneDictionary()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(application);
            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "initial Classic apply");
            for (int index = 0; index < 20; index++)
            {
                AppTheme next = index % 2 == 0 ? AppTheme.Tracework : AppTheme.Classic;
                TestSupport.True(service.ApplyTheme(next), $"switch {index + 1} to {next}");
                TestSupport.Equal(1,
                    application.Resources.MergedDictionaries.OfType<ThemeResourceDictionary>().Count(),
                    $"theme dictionary count after switch {index + 1}");
            }
        });
    }

    private static void FailedLoadPreservesPreviousTheme()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(
                application,
                theme => theme == AppTheme.Tracework
                    ? throw new IOException("synthetic theme load failure")
                    : ThemeService.LoadThemeDictionary(theme));

            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "Classic apply before failure");
            TestSupport.False(service.ApplyTheme(AppTheme.Tracework), "Tracework apply should fail");
            TestSupport.Equal(AppTheme.Classic, service.CurrentTheme, "theme after failed apply");
            TestSupport.Equal(1,
                application.Resources.MergedDictionaries.OfType<ThemeResourceDictionary>().Count(),
                "dictionary count after failed apply");
        });
    }

    private static void ClassicToTraceworkRaisesOneEventOnDispatcher()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(application);
            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "initial Classic apply");
            int eventCount = 0;
            ThemeChangedEventArgs? observed = null;
            bool dispatcherAccess = false;
            service.ThemeChanged += (_, args) =>
            {
                eventCount++;
                observed = args;
                dispatcherAccess = application.Dispatcher.CheckAccess();
            };

            TestSupport.True(service.ApplyTheme(AppTheme.Tracework), "Classic to Tracework apply");

            TestSupport.Equal(1, eventCount, "forward ThemeChanged count");
            ThemeChangedEventArgs args = TestSupport.NotNull(observed, "forward ThemeChanged args");
            TestSupport.Equal(AppTheme.Classic, args.PreviousTheme, "forward previous theme");
            TestSupport.Equal(AppTheme.Tracework, args.CurrentTheme, "forward current theme");
            TestSupport.True(dispatcherAccess, "ThemeChanged Dispatcher access");
        });
    }

    private static void TraceworkToClassicRaisesOneEvent()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(application);
            TestSupport.True(service.ApplyTheme(AppTheme.Tracework), "initial Tracework apply");
            int eventCount = 0;
            ThemeChangedEventArgs? observed = null;
            service.ThemeChanged += (_, args) =>
            {
                eventCount++;
                observed = args;
            };

            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "Tracework to Classic apply");

            TestSupport.Equal(1, eventCount, "reverse ThemeChanged count");
            ThemeChangedEventArgs args = TestSupport.NotNull(observed, "reverse ThemeChanged args");
            TestSupport.Equal(AppTheme.Tracework, args.PreviousTheme, "reverse previous theme");
            TestSupport.Equal(AppTheme.Classic, args.CurrentTheme, "reverse current theme");
        });
    }

    private static void RepeatedApplyRaisesNoEvent()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(application);
            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "initial Classic apply");
            int eventCount = 0;
            service.ThemeChanged += (_, _) => eventCount++;

            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "repeated Classic apply");

            TestSupport.Equal(0, eventCount, "repeated apply ThemeChanged count");
        });
    }

    private static void FailedLoadRaisesNoEvent()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(
                application,
                theme => theme == AppTheme.Tracework
                    ? throw new IOException("synthetic theme load failure")
                    : ThemeService.LoadThemeDictionary(theme));
            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "Classic apply before load failure");
            int eventCount = 0;
            service.ThemeChanged += (_, _) => eventCount++;

            TestSupport.False(service.ApplyTheme(AppTheme.Tracework), "Tracework load should fail");

            TestSupport.Equal(0, eventCount, "load failure ThemeChanged count");
        });
    }

    private static void FailedReplacementRollsBackWithoutEvent()
    {
        WithIsolatedMergedDictionaries(application =>
        {
            ThemeService service = new(
                application,
                ThemeService.LoadThemeDictionary,
                (merged, previousIndex, candidate) =>
                {
                    if (previousIndex >= 0)
                    {
                        merged[previousIndex] = candidate;
                    }
                    else
                    {
                        merged.Add(candidate);
                    }

                    if (candidate.Source?.OriginalString.Contains("/Tracework/", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        throw new IOException("synthetic theme replacement failure");
                    }
                });
            TestSupport.True(service.ApplyTheme(AppTheme.Classic), "Classic apply before replacement failure");
            int eventCount = 0;
            service.ThemeChanged += (_, _) => eventCount++;

            TestSupport.False(service.ApplyTheme(AppTheme.Tracework), "Tracework replacement should fail");

            TestSupport.Equal(AppTheme.Classic, service.CurrentTheme, "theme after replacement rollback");
            TestSupport.Equal(0, eventCount, "replacement failure ThemeChanged count");
            TestSupport.Equal(1,
                application.Resources.MergedDictionaries.OfType<ThemeResourceDictionary>().Count(),
                "theme dictionary count after replacement rollback");
        });
    }

    private static void SettingsViewLoadsAtMinimumSize()
    {
        TestSupport.InTemporaryDirectory(directory =>
        {
            _ = GetApplication();
            AppSettings settings = new() { Theme = "Classic" };
            using PollingService polling = new(new CountingSensorService(), settings);
            using CsvGameSessionRecorder recorder = new(Path.Combine(directory, "sessions"), 8);
            using MotionService motion = new(new FakeMotionEnvironment(), MotionLevel.Standard, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            using SettingsViewModel viewModel = new(
                settings,
                new CountingSettingsService(settings),
                new TestThemeService(AppTheme.Classic),
                motion,
                new NoopStartupService(),
                polling,
                new SensorDiagnosticService(),
                System.Windows.Threading.Dispatcher.CurrentDispatcher,
                () => { },
                recorder);
            SettingsView view = new() { DataContext = viewModel };
            view.Measure(new Size(920, 620));
            view.Arrange(new Rect(0, 0, 920, 620));
            TestSupport.True(view.DesiredSize.Width > 0d && view.DesiredSize.Height > 0d,
                "settings view minimum-size layout");
        });
    }

    private static void FailedApplyIsNotPersisted()
    {
        TestSupport.InTemporaryDirectory(directory =>
        {
            AppSettings settings = new() { Theme = "Classic" };
            CountingSettingsService settingsService = new(settings);
            using PollingService polling = new(new CountingSensorService(), settings);
            using CsvGameSessionRecorder recorder = new(Path.Combine(directory, "sessions"), 8);
            using MotionService motion = new(new FakeMotionEnvironment(), MotionLevel.Standard, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            using SettingsViewModel viewModel = new(
                settings,
                settingsService,
                new TestThemeService(AppTheme.Classic, failTheme: AppTheme.Tracework),
                motion,
                new NoopStartupService(),
                polling,
                new SensorDiagnosticService(),
                System.Windows.Threading.Dispatcher.CurrentDispatcher,
                () => { },
                recorder);

            viewModel.SelectThemeCommand.Execute(viewModel.TraceworkTheme);

            TestSupport.Equal("Classic", settings.Theme, "stored value after failed apply");
            TestSupport.Equal(AppTheme.Classic, viewModel.SelectedTheme.Theme, "selection after failed apply");
            TestSupport.Equal(0, settingsService.SaveCount, "settings save count after failed apply");
        });
    }

    private static void SaveFailureKeepsAppliedThemeWithWarning()
    {
        TestSupport.InTemporaryDirectory(directory =>
        {
            AppSettings settings = new() { Theme = "Classic" };
            FailingSaveSettingsService settingsService = new(settings);
            TestThemeService themeService = new(AppTheme.Classic);
            using PollingService polling = new(new CountingSensorService(), settings);
            using CsvGameSessionRecorder recorder = new(Path.Combine(directory, "sessions"), 8);
            using MotionService motion = new(new FakeMotionEnvironment(), MotionLevel.Standard, System.Windows.Threading.Dispatcher.CurrentDispatcher);
            using SettingsViewModel viewModel = new(
                settings,
                settingsService,
                themeService,
                motion,
                new NoopStartupService(),
                polling,
                new SensorDiagnosticService(),
                System.Windows.Threading.Dispatcher.CurrentDispatcher,
                () => { },
                recorder);

            viewModel.SelectThemeCommand.Execute(viewModel.TraceworkTheme);

            TestSupport.Equal(AppTheme.Tracework, themeService.CurrentTheme, "applied theme after save failure");
            TestSupport.Equal("Tracework", settings.Theme, "in-memory setting after save failure");
            TestSupport.True(viewModel.ThemeStatusText.Contains("无法保存", StringComparison.Ordinal),
                "save failure status");
            TestSupport.Equal(1, settingsService.SaveCount, "save attempts");
        });
    }

    private static System.Windows.Application GetApplication()
    {
        if (System.Windows.Application.Current is not null)
        {
            return System.Windows.Application.Current;
        }

        HardwareVision.App application = new();
        application.InitializeComponent();
        return application;
    }

    private static void WithIsolatedMergedDictionaries(Action<System.Windows.Application> test)
    {
        System.Windows.Application application = GetApplication();
        ResourceDictionary[] original = [.. application.Resources.MergedDictionaries];
        application.Resources.MergedDictionaries.Clear();
        try
        {
            test(application);
        }
        finally
        {
            application.Resources.MergedDictionaries.Clear();
            foreach (ResourceDictionary dictionary in original)
            {
                application.Resources.MergedDictionaries.Add(dictionary);
            }
        }
    }
}

internal sealed class TestThemeService : IThemeService
{
    private static readonly IReadOnlyList<ThemeDescriptor> Themes =
    [
        new(AppTheme.Classic, "经典复古", "Classic", []),
        new(AppTheme.Tracework, "迹构", "Tracework", [])
    ];

    private readonly AppTheme? failTheme;

    public TestThemeService(AppTheme currentTheme, AppTheme? failTheme = null)
    {
        CurrentTheme = currentTheme;
        this.failTheme = failTheme;
    }

    public AppTheme CurrentTheme { get; private set; }

    public int ApplyCount { get; private set; }

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public IReadOnlyList<ThemeDescriptor> AvailableThemes => Themes;

    public bool ApplyTheme(AppTheme theme)
    {
        ApplyCount++;
        if (theme == failTheme)
        {
            return false;
        }

        if (CurrentTheme == theme)
        {
            return true;
        }

        AppTheme previousTheme = CurrentTheme;
        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(previousTheme, theme));
        return true;
    }
}

internal sealed class CountingSettingsService : ISettingsService
{
    private readonly AppSettings settings;

    public CountingSettingsService(AppSettings settings) => this.settings = settings;

    public int SaveCount { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(settings);

    public Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
    {
        updateAction(settings);
        return Task.FromResult(settings);
    }
}

internal sealed class FailingSaveSettingsService : ISettingsService
{
    private readonly AppSettings settings;

    public FailingSaveSettingsService(AppSettings settings) => this.settings = settings;

    public int SaveCount { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> TrySaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(false);
    }

    public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);

    public Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
    {
        updateAction(settings);
        return Task.FromResult(settings);
    }
}
