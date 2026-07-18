using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Themes;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class TraceworkDashboardTests
{
    private static readonly HardwareOverviewKind[] ExpectedKinds =
    [
        HardwareOverviewKind.Cpu,
        HardwareOverviewKind.Gpu,
        HardwareOverviewKind.Memory,
        HardwareOverviewKind.Disk,
        HardwareOverviewKind.Network,
        HardwareOverviewKind.System
    ];

    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("ThemeContext 01 default theme is Classic", DefaultThemeIsClassic),
        ("ThemeContext 02 theme value inherits and updates", ThemeValueInheritsAndUpdates),
        ("ThemeContext 03 DashboardViewModel has no theme service dependency", DashboardViewModelHasNoThemeServiceDependency),
        ("Dashboard cards 01 semantic order and references are stable", SemanticOrderAndReferencesAreStable),
        ("Dashboard cards 02 summary refresh preserves card references", SummaryRefreshPreservesCardReferences),
        ("Dashboard cards 03 GPU and disk selection use existing settings", GpuAndDiskSelectionUseExistingSettings),
        ("Dashboard metrics 01 visible projection preserves metric order", VisibleProjectionPreservesMetricOrder),
        ("Dashboard metrics 02 empty visible projection reports no primary", EmptyVisibleProjectionReportsNoPrimary),
        ("Dashboard metrics 03 replacement raises projection notifications", ReplacementRaisesProjectionNotifications)
    ];

    private static void DefaultThemeIsClassic()
    {
        Border element = new();
        TestSupport.Equal(AppTheme.Classic, ThemeContext.GetCurrentTheme(element), "ThemeContext default");
    }

    private static void ThemeValueInheritsAndUpdates()
    {
        Grid parent = new();
        Border child = new();
        parent.Children.Add(child);

        ThemeContext.SetCurrentTheme(parent, AppTheme.Tracework);
        TestSupport.Equal(AppTheme.Tracework, ThemeContext.GetCurrentTheme(child), "inherited Tracework theme");

        ThemeContext.SetCurrentTheme(parent, AppTheme.Classic);
        TestSupport.Equal(AppTheme.Classic, ThemeContext.GetCurrentTheme(child), "updated inherited Classic theme");
    }

    private static void DashboardViewModelHasNoThemeServiceDependency()
    {
        bool dependsOnThemeService = typeof(DashboardViewModel)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => typeof(IThemeService).IsAssignableFrom(parameter.ParameterType));
        TestSupport.False(dependsOnThemeService, "DashboardViewModel theme-service constructor dependency");

        FieldInfo[] mutableThemeFields = typeof(ThemeContext)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(AppTheme))
            .ToArray();
        TestSupport.Equal(0, mutableThemeFields.Length, "static mutable current-theme fields");
    }

    private static void SemanticOrderAndReferencesAreStable() =>
        WithEnvironment(environment =>
        {
            DashboardViewModel viewModel = environment.ViewModel;
            TestSupport.Equal(6, viewModel.OverviewCards.Count, "overview card count");
            TestSupport.True(
                ExpectedKinds.SequenceEqual(viewModel.OverviewCards.Select(card => card.Kind)),
                "overview card semantic order");

            HardwareOverviewCardViewModel[] semanticCards = SemanticCards(viewModel);
            for (int index = 0; index < semanticCards.Length; index++)
            {
                TestSupport.True(
                    ReferenceEquals(viewModel.OverviewCards[index], semanticCards[index]),
                    $"semantic card reference {ExpectedKinds[index]}");
            }

            TestSupport.Equal(6, semanticCards.Distinct(ReferenceEqualityComparer.Instance).Count(), "unique semantic cards");
        });

    private static void SummaryRefreshPreservesCardReferences() =>
        WithEnvironment(environment =>
        {
            DashboardViewModel viewModel = environment.ViewModel;
            object collection = viewModel.OverviewCards;
            HardwareOverviewCardViewModel[] cards = viewModel.OverviewCards.ToArray();

            viewModel.SetSummaryActive(true);
            viewModel.SetSummaryActive(true);

            TestSupport.True(ReferenceEquals(collection, viewModel.OverviewCards), "OverviewCards collection after refresh");
            for (int index = 0; index < cards.Length; index++)
            {
                TestSupport.True(
                    ReferenceEquals(cards[index], viewModel.OverviewCards[index]),
                    $"card reference after refresh {ExpectedKinds[index]}");
            }
        });

    private static void GpuAndDiskSelectionUseExistingSettings()
    {
        WithEnvironment(environment =>
        {
            DashboardViewModel viewModel = environment.ViewModel;
            viewModel.GpuOverviewCard.UpdateHardwareOptions(
                [("gpu-0", "Synthetic GPU 0"), ("gpu-1", "Synthetic GPU 1")],
                "gpu-0",
                id => viewModel.PreferredGpuId = id ?? string.Empty);
            viewModel.GpuOverviewCard.SelectedHardwareOption = viewModel.GpuOverviewCard.HardwareOptions[1];
            TestSupport.Equal("gpu-1", environment.Settings.PreferredGpuId, "preferred GPU setting");
        });

        WithEnvironment(environment =>
        {
            DashboardViewModel viewModel = environment.ViewModel;
            viewModel.DiskOverviewCard.UpdateHardwareOptions(
                [("disk-0", "Synthetic Disk 0"), ("disk-1", "Synthetic Disk 1")],
                "disk-0",
                id => viewModel.PreferredDiskId = id ?? string.Empty);
            viewModel.DiskOverviewCard.SelectedHardwareOption = viewModel.DiskOverviewCard.HardwareOptions[1];
            TestSupport.Equal("disk-1", environment.Settings.PreferredDiskId, "preferred disk setting");
        });
    }

    private static void VisibleProjectionPreservesMetricOrder()
    {
        HardwareOverviewCardViewModel card = new(HardwareOverviewKind.Cpu);
        card.ReplaceMetrics(
        [
            Metric("first", "Primary", "10", displayOrder: 1),
            Metric("hidden", "Hidden", "20", displayOrder: 2, isVisible: false),
            Metric("third", "Secondary", "30", displayOrder: 3)
        ]);

        TestSupport.Equal(3, card.Metrics.Count, "original Metrics count");
        TestSupport.Equal("first", card.Metrics[0].Id, "original first metric");
        TestSupport.Equal("hidden", card.Metrics[1].Id, "original hidden metric position");
        TestSupport.Equal("third", card.Metrics[2].Id, "original third metric");
        TestSupport.True(ReferenceEquals(card.Metrics[0], card.PrimaryMetric), "primary metric reference");
        TestSupport.Equal(1, card.SecondaryMetrics.Count, "secondary metric count");
        TestSupport.True(ReferenceEquals(card.Metrics[2], card.SecondaryMetrics[0]), "secondary metric reference");
        TestSupport.Equal(2, card.VisibleMetricCount, "visible metric count");
    }

    private static void EmptyVisibleProjectionReportsNoPrimary()
    {
        HardwareOverviewCardViewModel card = new(HardwareOverviewKind.System);
        card.ReplaceMetrics([Metric("hidden", "Hidden", "--", displayOrder: 1, isVisible: false)]);

        TestSupport.True(card.PrimaryMetric is null, "primary metric when all metrics are hidden");
        TestSupport.Equal(0, card.SecondaryMetrics.Count, "secondary metrics when all metrics are hidden");
        TestSupport.Equal(0, card.VisibleMetricCount, "visible metric count when all metrics are hidden");
    }

    private static void ReplacementRaisesProjectionNotifications()
    {
        HardwareOverviewCardViewModel card = new(HardwareOverviewKind.Gpu);
        List<string> propertyNames = [];
        card.PropertyChanged += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.PropertyName))
            {
                propertyNames.Add(args.PropertyName!);
            }
        };

        card.ReplaceMetrics([Metric("metric", "Metric", "42", displayOrder: 1)]);

        TestSupport.True(propertyNames.Contains(nameof(card.PrimaryMetric)), "PrimaryMetric notification");
        TestSupport.True(propertyNames.Contains(nameof(card.SecondaryMetrics)), "SecondaryMetrics notification");
        TestSupport.True(propertyNames.Contains(nameof(card.VisibleMetricCount)), "VisibleMetricCount notification");
    }

    private static HardwareOverviewCardViewModel[] SemanticCards(DashboardViewModel viewModel) =>
    [
        viewModel.CpuOverviewCard,
        viewModel.GpuOverviewCard,
        viewModel.MemoryOverviewCard,
        viewModel.DiskOverviewCard,
        viewModel.NetworkOverviewCard,
        viewModel.SystemOverviewCard
    ];

    private static HardwareMetric Metric(
        string id,
        string label,
        string value,
        int displayOrder,
        bool isVisible = true) =>
        new()
        {
            Id = id,
            HardwareId = "synthetic",
            Category = HardwareMetricCategory.System,
            DisplayName = label,
            TechnicalName = label,
            Value = value,
            Availability = MetricAvailability.Available,
            IsVisible = isVisible,
            DisplayOrder = displayOrder
        };

    private static void WithEnvironment(Action<DashboardTestEnvironment> test)
    {
        using DashboardTestEnvironment environment = new();
        test(environment);
    }

    private sealed class DashboardTestEnvironment : IDisposable
    {
        private readonly PollingService pollingService;
        private readonly SensorHistoryService sensorHistoryService;

        public DashboardTestEnvironment()
        {
            Settings = new AppSettings();
            CountingSettingsService settingsService = new(Settings);
            pollingService = new PollingService(new CountingSensorService(), Settings);
            sensorHistoryService = new SensorHistoryService(pollingService);
            ViewModel = new DashboardViewModel(
                Settings,
                new EmptyHardwareInfoService(),
                pollingService,
                settingsService,
                Dispatcher.CurrentDispatcher,
                sensorHistoryService);
        }

        public AppSettings Settings { get; }

        public DashboardViewModel ViewModel { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            sensorHistoryService.Dispose();
            pollingService.Dispose();
        }
    }

    private sealed class EmptyHardwareInfoService : IHardwareInfoService
    {
        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot { Timestamp = DateTimeOffset.Now });

        public Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>([]);

        public Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("test", "test", null, null, null, null));
    }
}
