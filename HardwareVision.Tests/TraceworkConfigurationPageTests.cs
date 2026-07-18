using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Sensors;
using HardwareVision.Themes;
using HardwareVision.ViewModels;
using HardwareVision.Views;
using HardwareVision.Views.AdvancedSensors;
using HardwareVision.Views.MetricVisibility;
using HardwareVision.Views.Settings;

namespace HardwareVision.Tests;

internal static class TraceworkConfigurationPageTests
{
    private static readonly Size LayoutSize = new(1280d, 900d);
    private static readonly Size MinimumLayoutSize = new(920d, 620d);

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("XAML 46 Classic Advanced Sensors layout instantiates", ClassicAdvancedSensorsLayoutInstantiates),
        ("XAML 47 Tracework Advanced Sensors layout instantiates", TraceworkAdvancedSensorsLayoutInstantiates),
        ("XAML 48 Tracework Advanced Sensors lays out at 920x620", TraceworkAdvancedSensorsMinimumLayout),
        ("XAML 49 Advanced Sensors matrix columns instantiate", AdvancedSensorsMatrixColumnsInstantiate),
        ("XAML 50 Classic Settings layout instantiates", ClassicSettingsLayoutInstantiates),
        ("XAML 51 Tracework Settings layout instantiates", TraceworkSettingsLayoutInstantiates),
        ("XAML 52 Tracework Settings lays out at 920x620", TraceworkSettingsMinimumLayout),
        ("XAML 53 Tracework theme cards instantiate", TraceworkThemeCardsInstantiate),
        ("XAML 54 Settings theme switch preserves ViewModel and avoids duplicate writes", SettingsSwitchAvoidsSideEffects),
        ("XAML 55 Settings inputs steppers toggles and commands instantiate", SettingsControlsInstantiate),
        ("XAML 56 Classic Metric Visibility layout instantiates", ClassicMetricVisibilityLayoutInstantiates),
        ("XAML 57 Tracework Metric Visibility layout instantiates", TraceworkMetricVisibilityLayoutInstantiates),
        ("XAML 58 Tracework Metric Visibility lays out at 920x620", TraceworkMetricVisibilityMinimumLayout),
        ("XAML 59 Category selector and metric catalog instantiate", MetricVisibilityControlsInstantiate),
        ("XAML 60 Metric Visibility switch preserves selected category and items", MetricVisibilitySwitchPreservesState),
        ("XAML 61 three-page bidirectional theme switch preserves DataContext", ThreePageBidirectionalSwitchPreservesDataContext),
        ("Configuration pages 01 layouts are presentation-only controls", LayoutsArePresentationOnlyControls),
        ("Configuration pages 02 dual-template architecture is isolated", DualTemplateArchitectureIsIsolated),
        ("Configuration pages 03 forbidden visual material is absent", ForbiddenVisualMaterialIsAbsent),
        ("Configuration pages 04 Advanced Sensors switch preserves refresh state", AdvancedSensorsSwitchPreservesRefreshState),
        ("Configuration pages 05 Metric toggle persists only user changes", MetricTogglePersistsOnlyUserChanges)
    ];

    private static void ClassicAdvancedSensorsLayoutInstantiates() =>
        AssertTemplate(new AdvancedSensorsView { DataContext = CreateAdvancedSensorsViewModel() }, AppTheme.Classic,
            typeof(ClassicAdvancedSensorsLayout), typeof(TraceworkAdvancedSensorsLayout));

    private static void TraceworkAdvancedSensorsLayoutInstantiates() =>
        AssertTemplate(new AdvancedSensorsView { DataContext = CreateAdvancedSensorsViewModel() }, AppTheme.Tracework,
            typeof(TraceworkAdvancedSensorsLayout), typeof(ClassicAdvancedSensorsLayout));

    private static void TraceworkAdvancedSensorsMinimumLayout()
    {
        using AdvancedSensorsViewModel viewModel = CreateAdvancedSensorsViewModel();
        AdvancedSensorsView view = new() { DataContext = viewModel };
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, MinimumLayoutSize, _ =>
        {
            TraceworkAdvancedSensorsLayout layout = FindSingle<TraceworkAdvancedSensorsLayout>(view, "Advanced Sensors layout");
            TestSupport.True(layout.ActualWidth > 0d && layout.ActualHeight > 0d, "Advanced Sensors minimum layout size");
            TestSupport.True(layout.DesiredSize.Width <= view.ActualWidth, "Advanced Sensors page-level horizontal overflow");
        });
    }

    private static void AdvancedSensorsMatrixColumnsInstantiate()
    {
        using AdvancedSensorsViewModel viewModel = CreateAdvancedSensorsViewModel();
        AdvancedSensorsView view = new() { DataContext = viewModel };
        ThemeContext.SetCurrentTheme(view, AppTheme.Tracework);
        WithHostedView(view, LayoutSize, _ =>
        {
            DataGrid grid = FindSingle<DataGrid>(view, "Advanced Sensors DataGrid");
            TestSupport.Equal(4, grid.Columns.Count, "Advanced Sensors column count");
            TestSupport.Equal(1, grid.Items.Count, "Advanced Sensors row count");
        });
    }

    private static void ClassicSettingsLayoutInstantiates() =>
        WithSettingsFixture(fixture => AssertTemplate(fixture.View, AppTheme.Classic,
            typeof(ClassicSettingsLayout), typeof(TraceworkSettingsLayout)));

    private static void TraceworkSettingsLayoutInstantiates() =>
        WithSettingsFixture(fixture => AssertTemplate(fixture.View, AppTheme.Tracework,
            typeof(TraceworkSettingsLayout), typeof(ClassicSettingsLayout)));

    private static void TraceworkSettingsMinimumLayout() => WithSettingsFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, MinimumLayoutSize, _ =>
        {
            TraceworkSettingsLayout layout = FindSingle<TraceworkSettingsLayout>(fixture.View, "Settings layout");
            ScrollViewer scroll = FindVisualDescendants<ScrollViewer>(layout).First();
            TestSupport.Equal(0d, scroll.ScrollableWidth, "Settings horizontal overflow");
            TestSupport.True(layout.ActualWidth > 0d && layout.ActualHeight > 0d, "Settings minimum layout size");
        });
    });

    private static void TraceworkThemeCardsInstantiate() => WithSettingsFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            ToggleButton[] cards = FindVisualDescendants<ToggleButton>(fixture.View)
                .Where(toggle => toggle.DataContext is ThemeDescriptor).ToArray();
            TestSupport.Equal(2, cards.Length, "Tracework theme card count");
            TestSupport.True(cards.All(card => card.Focusable), "Tracework theme cards are keyboard focusable");
            TestSupport.True(cards.Single(card => ((ThemeDescriptor)card.DataContext).Theme == AppTheme.Tracework).IsChecked == true,
                "Tracework current theme card state");
        });
    }, AppTheme.Tracework);

    private static void SettingsSwitchAvoidsSideEffects() => WithSettingsFixture(fixture =>
    {
        object viewModel = fixture.ViewModel;
        int saveCount = fixture.SettingsService.SaveCount;
        int updateCount = fixture.SettingsService.UpdateCount;
        int startupCalls = fixture.StartupService.CallCount;
        int refreshCalls = fixture.HardwareRefreshService.RefreshCount;
        int directoryCalls = fixture.Recorder.DirectoryCallCount;
        int recorderCalls = fixture.Recorder.OperationCount;
        int sensorPolls = fixture.SensorService.PollCount;
        ThemeDescriptor selectedTheme = fixture.ViewModel.SelectedTheme;
        string themeStatus = fixture.ViewModel.ThemeStatusText;
        long themeChangeVersion = ReadPrivate<long>(fixture.ViewModel, "themeChangeVersion");
        bool autoStart = fixture.ViewModel.AutoStartEnabled;
        bool startMinimized = fixture.ViewModel.StartMinimizedToTray;
        object selectedFrameMode = fixture.ViewModel.SelectedFrameStorageMode;
        bool closeToTray = fixture.ViewModel.CloseToTray;
        double refreshInterval = fixture.ViewModel.RefreshIntervalSeconds;
        int backgroundRefreshInterval = fixture.ViewModel.BackgroundRefreshIntervalSeconds;
        bool autoHardwareRefresh = fixture.ViewModel.AutoRefreshHardwareOnDeviceChange;
        bool recordGameSessions = fixture.ViewModel.RecordGameSessions;
        string currentStage = fixture.ViewModel.CurrentStage;
        string lastSelectedPage = fixture.ViewModel.LastSelectedPage;
        bool isHardwareScanning = fixture.ViewModel.IsHardwareScanning;
        string hardwareScanStatus = fixture.ViewModel.HardwareScanStatusText;
        string lastHardwareScanTime = fixture.ViewModel.LastHardwareScanTimeText;
        string directorySize = fixture.ViewModel.GameSessionDirectorySizeText;
        object? directoryCancellation = ReadPrivate<CancellationTokenSource?>(fixture.ViewModel, "directorySizeCancellation");
        TimeSpan foreground = ReadPrivate<TimeSpan>(fixture.PollingService, "foregroundInterval");
        TimeSpan background = ReadPrivate<TimeSpan>(fixture.PollingService, "backgroundInterval");

        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchThemeContext(fixture.View, host, AppTheme.Tracework);
            SwitchThemeContext(fixture.View, host, AppTheme.Classic);

            TestSupport.True(ReferenceEquals(viewModel, fixture.View.DataContext), "Settings ViewModel reference");
            TestSupport.Equal(saveCount, fixture.SettingsService.SaveCount, "settings SaveAsync calls during template switch");
            TestSupport.Equal(updateCount, fixture.SettingsService.UpdateCount, "settings UpdateAsync calls during template switch");
            TestSupport.Equal(startupCalls, fixture.StartupService.CallCount, "startup service calls during template switch");
            TestSupport.Equal(refreshCalls, fixture.HardwareRefreshService.RefreshCount, "hardware scan calls during template switch");
            TestSupport.Equal(directoryCalls, fixture.Recorder.DirectoryCallCount, "directory recalculation calls during template switch");
            TestSupport.Equal(recorderCalls, fixture.Recorder.OperationCount, "session recorder calls during template switch");
            TestSupport.Equal(sensorPolls, fixture.SensorService.PollCount, "sensor polling calls during template switch");
            TestSupport.True(ReferenceEquals(selectedTheme, fixture.ViewModel.SelectedTheme), "selected theme descriptor");
            TestSupport.Equal(themeStatus, fixture.ViewModel.ThemeStatusText, "theme status text");
            TestSupport.Equal(themeChangeVersion, ReadPrivate<long>(fixture.ViewModel, "themeChangeVersion"), "theme change version");
            TestSupport.Equal(autoStart, fixture.ViewModel.AutoStartEnabled, "auto-start setting");
            TestSupport.Equal(startMinimized, fixture.ViewModel.StartMinimizedToTray, "start minimized setting");
            TestSupport.True(Equals(selectedFrameMode, fixture.ViewModel.SelectedFrameStorageMode), "frame storage mode");
            TestSupport.Equal(closeToTray, fixture.ViewModel.CloseToTray, "tray setting");
            TestSupport.Equal(refreshInterval, fixture.ViewModel.RefreshIntervalSeconds, "foreground refresh setting");
            TestSupport.Equal(backgroundRefreshInterval, fixture.ViewModel.BackgroundRefreshIntervalSeconds, "background refresh setting");
            TestSupport.Equal(autoHardwareRefresh, fixture.ViewModel.AutoRefreshHardwareOnDeviceChange, "automatic hardware refresh setting");
            TestSupport.Equal(recordGameSessions, fixture.ViewModel.RecordGameSessions, "record sessions setting");
            TestSupport.Equal(currentStage, fixture.ViewModel.CurrentStage, "current stage");
            TestSupport.Equal(lastSelectedPage, fixture.ViewModel.LastSelectedPage, "last selected page");
            TestSupport.Equal(isHardwareScanning, fixture.ViewModel.IsHardwareScanning, "hardware scanning state");
            TestSupport.Equal(hardwareScanStatus, fixture.ViewModel.HardwareScanStatusText, "hardware scan status");
            TestSupport.Equal(lastHardwareScanTime, fixture.ViewModel.LastHardwareScanTimeText, "last hardware scan time");
            TestSupport.Equal(directorySize, fixture.ViewModel.GameSessionDirectorySizeText, "directory size text");
            TestSupport.True(ReferenceEquals(directoryCancellation,
                ReadPrivate<CancellationTokenSource?>(fixture.ViewModel, "directorySizeCancellation")), "directory cancellation reference");
            TestSupport.Equal(foreground, ReadPrivate<TimeSpan>(fixture.PollingService, "foregroundInterval"), "foreground polling interval");
            TestSupport.Equal(background, ReadPrivate<TimeSpan>(fixture.PollingService, "backgroundInterval"), "background polling interval");

            fixture.ViewModel.SelectThemeCommand.Execute(fixture.ViewModel.TraceworkTheme);
            SwitchThemeContext(fixture.View, host, AppTheme.Tracework);
            TestSupport.Equal(1, fixture.ThemeService.ApplyCount, "single user theme apply");
            TestSupport.Equal(saveCount + 1, fixture.SettingsService.SaveCount, "single user theme save");
            TestSupport.Equal(updateCount, fixture.SettingsService.UpdateCount, "no secondary settings update");
        });
    });

    private static void SettingsControlsInstantiate() => WithSettingsFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            TestSupport.True(FindVisualDescendants<TextBox>(fixture.View).Count() >= 2, "Settings inputs");
            TestSupport.True(FindVisualDescendants<CheckBox>(fixture.View).Count() >= 5, "Settings toggles");
            Button[] buttons = FindVisualDescendants<Button>(fixture.View).ToArray();
            TestSupport.True(buttons.Count(button => Equals(button.Content, "+") || Equals(button.Content, "-")) == 4,
                "Settings steppers");
            TestSupport.True(buttons.Where(button => button.Command is not null).Count() >= 9, "Settings command buttons");
        });
    });

    private static void ClassicMetricVisibilityLayoutInstantiates() => WithMetricFixture(fixture =>
        AssertTemplate(fixture.View, AppTheme.Classic, typeof(ClassicMetricVisibilityLayout), typeof(TraceworkMetricVisibilityLayout)));

    private static void TraceworkMetricVisibilityLayoutInstantiates() => WithMetricFixture(fixture =>
        AssertTemplate(fixture.View, AppTheme.Tracework, typeof(TraceworkMetricVisibilityLayout), typeof(ClassicMetricVisibilityLayout)));

    private static void TraceworkMetricVisibilityMinimumLayout() => WithMetricFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, MinimumLayoutSize, _ =>
        {
            TraceworkMetricVisibilityLayout layout = FindSingle<TraceworkMetricVisibilityLayout>(fixture.View, "Metric Visibility layout");
            TestSupport.True(layout.ActualWidth > 0d && layout.ActualHeight > 0d, "Metric Visibility minimum layout size");
            TestSupport.True(layout.DesiredSize.Width <= fixture.View.ActualWidth, "Metric Visibility page-level horizontal overflow");
        });
    });

    private static void MetricVisibilityControlsInstantiate() => WithMetricFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            ListBox[] lists = FindVisualDescendants<ListBox>(fixture.View).ToArray();
            TestSupport.Equal(2, lists.Length, "category and metric catalog ListBoxes");
            TestSupport.True(ReferenceEquals(fixture.ViewModel.Categories, lists[0].ItemsSource), "category ItemsSource");
            TestSupport.True(ReferenceEquals(fixture.ViewModel.SelectedCategory!.VisibleMetrics, lists[1].ItemsSource), "metric ItemsSource");
            MetricVisibilityCategoryViewModel second = fixture.ViewModel.Categories[1];
            lists[0].SelectedItem = second;
            fixture.View.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            TestSupport.True(ReferenceEquals(second, fixture.ViewModel.SelectedCategory), "ListBox updates SelectedCategory");
        });
    });

    private static void MetricVisibilitySwitchPreservesState() => WithMetricFixture(fixture =>
    {
        MetricVisibilityCategoryViewModel selected = fixture.ViewModel.Categories[1];
        fixture.ViewModel.SelectedCategory = selected;
        object categories = fixture.ViewModel.Categories;
        object categoryItem = fixture.ViewModel.Categories[0];
        object metrics = selected.VisibleMetrics;
        MetricVisibilityItemViewModel metric = selected.VisibleMetrics[0];
        bool isVisible = metric.IsVisible;
        string status = fixture.ViewModel.StatusText;
        int writes = fixture.SettingsService.UpdateCount;
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Classic);
        WithHostedView(fixture.View, MinimumLayoutSize, host =>
        {
            SwitchThemeContext(fixture.View, host, AppTheme.Tracework);
            SwitchThemeContext(fixture.View, host, AppTheme.Classic);
            TestSupport.True(ReferenceEquals(categories, fixture.ViewModel.Categories), "Categories collection reference");
            TestSupport.True(ReferenceEquals(categoryItem, fixture.ViewModel.Categories[0]), "category item reference");
            TestSupport.True(ReferenceEquals(selected, fixture.ViewModel.SelectedCategory), "SelectedCategory reference");
            TestSupport.True(ReferenceEquals(metrics, selected.VisibleMetrics), "VisibleMetrics reference");
            TestSupport.True(ReferenceEquals(metric, selected.VisibleMetrics[0]), "metric item reference");
            TestSupport.Equal(isVisible, metric.IsVisible, "metric visibility value");
            TestSupport.Equal(status, fixture.ViewModel.StatusText, "metric status text");
            TestSupport.Equal(writes, fixture.SettingsService.UpdateCount, "metric settings writes during template switch");
        });
    });

    private static void ThreePageBidirectionalSwitchPreservesDataContext()
    {
        using AdvancedSensorsViewModel advanced = CreateAdvancedSensorsViewModel();
        WithSettingsFixture(settings => WithMetricFixture(metric =>
        {
            (UserControl View, Type Classic, Type Tracework)[] pages =
            [
                (new AdvancedSensorsView { DataContext = advanced }, typeof(ClassicAdvancedSensorsLayout), typeof(TraceworkAdvancedSensorsLayout)),
                (settings.View, typeof(ClassicSettingsLayout), typeof(TraceworkSettingsLayout)),
                (metric.View, typeof(ClassicMetricVisibilityLayout), typeof(TraceworkMetricVisibilityLayout))
            ];
            foreach ((UserControl view, Type classic, Type tracework) in pages)
            {
                object dataContext = TestSupport.NotNull(view.DataContext, "page DataContext");
                ThemeContext.SetCurrentTheme(view, AppTheme.Classic);
                WithHostedView(view, MinimumLayoutSize, host =>
                {
                    TestSupport.Equal(1, CountLayout(view, classic), "Classic layout before switch");
                    SwitchThemeContext(view, host, AppTheme.Tracework);
                    TestSupport.Equal(0, CountLayout(view, classic), "Classic layout after forward switch");
                    TestSupport.Equal(1, CountLayout(view, tracework), "Tracework layout after forward switch");
                    SwitchThemeContext(view, host, AppTheme.Classic);
                    TestSupport.Equal(1, CountLayout(view, classic), "Classic layout after reverse switch");
                    TestSupport.Equal(0, CountLayout(view, tracework), "Tracework layout after reverse switch");
                    TestSupport.True(ReferenceEquals(dataContext, view.DataContext), "bidirectional DataContext reference");
                });
            }
        }));
    }

    private static void LayoutsArePresentationOnlyControls()
    {
        Type[] layouts =
        [
            typeof(ClassicAdvancedSensorsLayout), typeof(TraceworkAdvancedSensorsLayout),
            typeof(ClassicSettingsLayout), typeof(TraceworkSettingsLayout),
            typeof(ClassicMetricVisibilityLayout), typeof(TraceworkMetricVisibilityLayout)
        ];
        foreach (Type type in layouts)
        {
            ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            TestSupport.Equal(1, constructors.Length, $"{type.Name} constructor count");
            TestSupport.Equal(0, constructors[0].GetParameters().Length, $"{type.Name} constructor parameters");
            TestSupport.Equal(0, type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Count(field => field.FieldType.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                    || field.FieldType.Namespace == typeof(ISettingsService).Namespace), $"{type.Name} business fields");
        }

        Type[] noThemeDependency = [typeof(AdvancedSensorsViewModel), typeof(MetricVisibilityViewModel)];
        foreach (Type type in noThemeDependency)
        {
            TestSupport.False(type.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => typeof(IThemeService).IsAssignableFrom(parameter.ParameterType)), $"{type.Name} theme dependency");
        }
        TestSupport.Equal(1, typeof(SettingsViewModel).GetConstructors().Single().GetParameters()
            .Count(parameter => typeof(IThemeService).IsAssignableFrom(parameter.ParameterType)), "Settings IThemeService dependency count");
    }

    private static void DualTemplateArchitectureIsIsolated()
    {
        string root = FindRepositoryRoot();
        string views = Path.Combine(root, "HardwareVision", "Views");
        foreach (string page in new[] { "AdvancedSensors", "Settings", "MetricVisibility" })
        {
            string source = File.ReadAllText(Path.Combine(views, page + "View.xaml"));
            TestSupport.Equal(1, Regex.Matches(source, "<ContentControl(?=\\s|>)").Count, $"{page} root ContentControl");
            TestSupport.True(source.Contains($"Classic{page}Template", StringComparison.Ordinal), $"{page} Classic template");
            TestSupport.True(source.Contains($"Tracework{page}Template", StringComparison.Ordinal), $"{page} Tracework template");
            TestSupport.True(source.Contains("ThemeContext.CurrentTheme", StringComparison.Ordinal), $"{page} inherited ThemeContext");
        }

        string layoutSources = string.Join(Environment.NewLine,
            new[] { "AdvancedSensors", "Settings", "MetricVisibility" }
                .SelectMany(page => Directory.EnumerateFiles(Path.Combine(views, page), "*.*"))
                .Select(File.ReadAllText));
        string[] forbiddenBehavior =
        [
            "new AdvancedSensorsViewModel", "new SettingsViewModel", "new MetricVisibilityViewModel",
            "ISettingsService", "IThemeService", "PollingService", "IGameSessionRecorder", "IHardwareRefreshService",
            ".Execute(", "SetActive(", "LoadCategories(", "ApplyTheme(", "RefreshAsync(", "Navigate("
        ];
        TestSupport.False(forbiddenBehavior.Any(layoutSources.Contains), "configuration layouts contain business behavior");
    }

    private static void ForbiddenVisualMaterialIsAbsent()
    {
        string root = FindRepositoryRoot();
        string views = Path.Combine(root, "HardwareVision", "Views");
        string sources = string.Join(Environment.NewLine,
            new[] { "AdvancedSensors", "Settings", "MetricVisibility" }
                .SelectMany(page => Directory.EnumerateFiles(Path.Combine(views, page), "*.*"))
                .Select(File.ReadAllText));
        string[] forbidden =
        [
            "Storyboard", "DoubleAnimation", "ColorAnimation", "ThicknessAnimation", "ObjectAnimationUsingKeyFrames",
            "CompositionTarget.Rendering", "DispatcherTimer", "PixelShader", "BlurEffect", "<Image", "<ImageBrush",
            "<BitmapImage", "file:", "pack://siteoforigin", "Binding CurrentPage"
        ];
        TestSupport.False(forbidden.Any(sources.Contains), "configuration layouts contain forbidden visual material");
        TestSupport.False(new Regex("(?<![A-Z])[A-Z]:[\\\\/]").IsMatch(sources), "configuration layouts contain absolute paths");

        string pages = File.ReadAllText(Path.Combine(root, "HardwareVision", "Themes", "Tracework", "Pages.xaml"));
        TestSupport.False(new Regex("<Style(?=\\s|>)(?![^>]*\\bx:Key\\s*=)[^>]*>", RegexOptions.Singleline).IsMatch(pages),
            "Tracework Pages contains an implicit Style");
        TestSupport.False(pages.Contains("BasedOn=\"{DynamicResource", StringComparison.Ordinal), "DynamicResource BasedOn");
    }

    private static void AdvancedSensorsSwitchPreservesRefreshState()
    {
        using AdvancedSensorsViewModel viewModel = CreateAdvancedSensorsViewModel();
        object rows = viewModel.SensorRows;
        object row = viewModel.SensorRows[0];
        SetPrivate(viewModel, "isActive", true);
        DateTime applied = new(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);
        SetPrivate(viewModel, "lastAppliedUtc", applied);
        CancellationTokenSource cancellation = new();
        SetPrivate(viewModel, "refreshCancellation", cancellation);
        SetPrivate(viewModel, "statusText", "synthetic stable state");
        AdvancedSensorsView view = new() { DataContext = viewModel };
        ThemeContext.SetCurrentTheme(view, AppTheme.Classic);
        WithHostedView(view, MinimumLayoutSize, host =>
        {
            SwitchThemeContext(view, host, AppTheme.Tracework);
            SwitchThemeContext(view, host, AppTheme.Classic);
            TestSupport.True(ReferenceEquals(rows, viewModel.SensorRows), "SensorRows collection reference");
            TestSupport.True(ReferenceEquals(row, viewModel.SensorRows[0]), "sensor row reference");
            TestSupport.Equal("synthetic stable state", viewModel.StatusText, "sensor status text");
            TestSupport.True(ReadPrivate<bool>(viewModel, "isActive"), "sensor active state");
            TestSupport.Equal(applied, ReadPrivate<DateTime>(viewModel, "lastAppliedUtc"), "lastAppliedUtc");
            TestSupport.True(ReferenceEquals(cancellation, ReadPrivate<CancellationTokenSource?>(viewModel, "refreshCancellation")),
                "refresh cancellation reference");
        });
    }

    private static void MetricTogglePersistsOnlyUserChanges() => WithMetricFixture(fixture =>
    {
        ThemeContext.SetCurrentTheme(fixture.View, AppTheme.Tracework);
        WithHostedView(fixture.View, LayoutSize, _ =>
        {
            int writes = fixture.SettingsService.UpdateCount;
            MetricVisibilityItemViewModel metric = fixture.ViewModel.SelectedCategory!.VisibleMetrics[0];
            CheckBox toggle = FindVisualDescendants<CheckBox>(fixture.View)
                .First(checkBox => ReferenceEquals(checkBox.DataContext, metric));
            TestSupport.Equal(writes, fixture.SettingsService.UpdateCount, "no automatic metric write during template creation");
            bool expected = !metric.IsVisible;
            toggle.IsChecked = expected;
            fixture.View.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            TestSupport.Equal(expected, metric.IsVisible, "metric Toggle TwoWay value");
            TestSupport.Equal(writes + 1, fixture.SettingsService.UpdateCount, "single metric user write");
        });
    });

    private static AdvancedSensorsViewModel CreateAdvancedSensorsViewModel()
    {
        AdvancedSensorsViewModel viewModel = new();
        viewModel.SensorRows.Add(new DetailSensorRowViewModel());
        return viewModel;
    }

    private static void WithSettingsFixture(Action<SettingsFixture> test, AppTheme theme = AppTheme.Classic)
    {
        string directory = Path.Combine(Path.GetTempPath(), "HardwareVision-TraceworkConfigurationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SettingsFixture? fixture = null;
        try
        {
            fixture = new SettingsFixture(directory, theme);
            test(fixture);
        }
        finally
        {
            fixture?.Dispose();
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private static void WithMetricFixture(Action<MetricFixture> test)
    {
        AppSettings settings = new();
        SideEffectSettingsService settingsService = new(settings);
        MetricVisibilityViewModel viewModel = new(settings, settingsService, null!, Dispatcher.CurrentDispatcher);
        test(new MetricFixture(viewModel, settingsService, new MetricVisibilityView { DataContext = viewModel }));
    }

    private static void AssertTemplate(UserControl view, AppTheme theme, Type expected, Type excluded)
    {
        ThemeContext.SetCurrentTheme(view, theme);
        WithHostedView(view, LayoutSize, _ =>
        {
            FrameworkElement layout = TestSupport.NotNull(
                FindVisualDescendants<FrameworkElement>(view).SingleOrDefault(element => element.GetType() == expected),
                $"{theme} {expected.Name}");
            TestSupport.Equal(0, CountLayout(view, excluded), $"excluded {excluded.Name}");
            TestSupport.True(ReferenceEquals(view.DataContext, layout.DataContext), $"{expected.Name} DataContext");
        });
        (view.DataContext as IDisposable)?.Dispose();
    }

    private static void SwitchThemeContext(UserControl view, Window host, AppTheme theme)
    {
        ThemeContext.SetCurrentTheme(view, theme);
        host.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        host.ApplyTemplate();
        view.ApplyTemplate();
        host.Measure(MinimumLayoutSize);
        host.Arrange(new Rect(new Point(), MinimumLayoutSize));
        host.UpdateLayout();
        view.UpdateLayout();
    }

    private static int CountLayout(DependencyObject root, Type type) =>
        FindVisualDescendants<FrameworkElement>(root).Count(element => element.GetType() == type);

    private static T FindSingle<T>(DependencyObject root, string label) where T : DependencyObject =>
        TestSupport.NotNull(FindVisualDescendants<T>(root).SingleOrDefault(), label);

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void WithHostedView(UserControl view, Size size, Action<Window> test)
    {
        Application application = GetApplication();
        application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Window host = new()
        {
            Content = view,
            Width = size.Width,
            Height = size.Height,
            Left = -32000d,
            Top = -32000d,
            Opacity = 0d,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None
        };
        try
        {
            host.Show();
            host.ApplyTemplate();
            view.ApplyTemplate();
            host.Measure(size);
            host.Arrange(new Rect(new Point(), size));
            host.UpdateLayout();
            view.UpdateLayout();
            test(host);
        }
        finally
        {
            host.Content = null;
            host.Close();
        }
    }

    private static Application GetApplication()
    {
        if (Application.Current is not null) return Application.Current;
        HardwareVision.App application = new();
        application.InitializeComponent();
        return application;
    }

    private static T ReadPrivate<T>(object instance, string fieldName) =>
        (T)TestSupport.NotNull(instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic), fieldName)
            .GetValue(instance)!;

    private static void SetPrivate(object instance, string fieldName, object? value) =>
        TestSupport.NotNull(instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic), fieldName)
            .SetValue(instance, value);

    private static string FindRepositoryRoot()
    {
        foreach (string origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            for (DirectoryInfo? candidate = new(origin); candidate is not null; candidate = candidate.Parent)
            {
                if (File.Exists(Path.Combine(candidate.FullName, "HardwareVision", "MainWindow.xaml"))) return candidate.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed record MetricFixture(
        MetricVisibilityViewModel ViewModel,
        SideEffectSettingsService SettingsService,
        MetricVisibilityView View);

    private sealed class SettingsFixture : IDisposable
    {
        public SettingsFixture(string directory, AppTheme theme)
        {
            AppSettings settings = new()
            {
                Theme = AppThemeParser.ToStorageValue(theme),
                AutoStartEnabled = true,
                StartMinimizedToTray = true,
                CloseToTray = true,
                RefreshIntervalSeconds = 1.5d,
                BackgroundRefreshIntervalSeconds = 15,
                AutoRefreshHardwareOnDeviceChange = true,
                RecordGameSessions = true
            };
            SettingsService = new SideEffectSettingsService(settings);
            ThemeService = new TestThemeService(theme);
            StartupService = new CountingStartupService();
            SensorService = new CountingSensorService();
            PollingService = new PollingService(SensorService, settings);
            Recorder = new CountingGameSessionRecorder(directory);
            HardwareRefreshService = new CountingHardwareRefreshService();
            ViewModel = new SettingsViewModel(
                settings, SettingsService, ThemeService, StartupService, PollingService,
                new SensorDiagnosticService(), Dispatcher.CurrentDispatcher, () => { }, Recorder, HardwareRefreshService);
            View = new SettingsView { DataContext = ViewModel };
        }

        public SideEffectSettingsService SettingsService { get; }
        public TestThemeService ThemeService { get; }
        public CountingStartupService StartupService { get; }
        public CountingSensorService SensorService { get; }
        public PollingService PollingService { get; }
        public CountingGameSessionRecorder Recorder { get; }
        public CountingHardwareRefreshService HardwareRefreshService { get; }
        public SettingsViewModel ViewModel { get; }
        public SettingsView View { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            PollingService.Dispose();
            Recorder.Dispose();
        }
    }

    private sealed class SideEffectSettingsService(AppSettings settings) : ISettingsService
    {
        public int SaveCount { get; private set; }
        public int UpdateCount { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken = default) { SaveCount++; return Task.CompletedTask; }
        public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task<AppSettings> UpdateAsync(Action<AppSettings> updateAction, CancellationToken cancellationToken = default)
        {
            updateAction(settings);
            UpdateCount++;
            return Task.FromResult(settings);
        }
    }

    private sealed class CountingStartupService : IStartupService
    {
        public int CallCount { get; private set; }
        public string StatusMessage => string.Empty;
        public bool IsAdministratorStartupAvailable => true;
        public bool IsUsingFallbackStartup => false;
        public bool IsEnabled() { CallCount++; return false; }
        public void Enable() => CallCount++;
        public void Disable() => CallCount++;
        public void SetEnabled(bool enabled) => CallCount++;
        public Task<bool> IsStartupEnabledAsync(CancellationToken cancellationToken = default) { CallCount++; return Task.FromResult(false); }
        public Task SetStartupEnabledAsync(bool isEnabled, CancellationToken cancellationToken = default) { CallCount++; return Task.CompletedTask; }
    }

    private sealed class CountingHardwareRefreshService : IHardwareRefreshService
    {
        public event EventHandler<HardwareRefreshStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<HardwareSnapshot>? SnapshotRefreshed { add { } remove { } }
        public int RefreshCount { get; private set; }
        public bool IsRefreshing => false;
        public HardwareRefreshResult? LastResult => null;
        public Task<HardwareRefreshResult> RefreshAsync(HardwareRefreshReason reason, CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(new HardwareRefreshResult { Reason = reason, State = HardwareRefreshState.Completed });
        }
    }

    private sealed class CountingGameSessionRecorder(string rootDirectory) : IGameSessionRecorder
    {
        public event EventHandler<GameSessionRecorderStateChangedEventArgs>? StateChanged { add { } remove { } }
        public string RootDirectory => rootDirectory;
        public bool IsRecording => false;
        public string RecordingStatusText => "Idle";
        public string? CurrentFilePath => null;
        public long DroppedSampleCount => 0;
        public int OperationCount { get; private set; }
        public int DirectoryCallCount { get; private set; }
        public Task RecoverIncompleteSessionsAsync(CancellationToken cancellationToken = default) { OperationCount++; return Task.CompletedTask; }
        public Task StartAsync(GameSessionStartInfo startInfo, CancellationToken cancellationToken = default) { OperationCount++; return Task.CompletedTask; }
        public bool TryRecord(GameFrameSample sample, Guid captureSessionId, int generation) { OperationCount++; return false; }
        public Task<GameSessionRecordInfo?> CompleteAsync(GameSessionEndReason reason, bool completedNormally, CancellationToken cancellationToken = default)
        { OperationCount++; return Task.FromResult<GameSessionRecordInfo?>(null); }
        public Task<IReadOnlyList<GameSessionRecordInfo>> GetRecentRecordsAsync(int maximumCount = 10, CancellationToken cancellationToken = default)
        { OperationCount++; return Task.FromResult<IReadOnlyList<GameSessionRecordInfo>>([]); }
        public Task<long> GetDirectorySizeAsync(CancellationToken cancellationToken = default)
        { OperationCount++; DirectoryCallCount++; return Task.FromResult(0L); }
        public Task<long> RecalculateDirectorySizeAsync(CancellationToken cancellationToken = default)
        { OperationCount++; DirectoryCallCount++; return Task.FromResult(0L); }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
