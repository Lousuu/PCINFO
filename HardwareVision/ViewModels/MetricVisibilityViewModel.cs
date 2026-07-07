using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;
using static HardwareVision.ViewModels.ViewModelHelpers;

namespace HardwareVision.ViewModels;

public sealed class MetricVisibilityViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
    private readonly DashboardViewModel dashboard;
    private readonly Dispatcher dispatcher;
    private MetricVisibilityCategoryViewModel? selectedCategory;
    private string statusText = "可自定义首页和各硬件页面显示项。";

    public MetricVisibilityViewModel(AppSettings settings, ISettingsService settingsService, DashboardViewModel dashboard, Dispatcher dispatcher)
    {
        this.settings = settings;
        this.settingsService = settingsService;
        this.dashboard = dashboard;
        this.dispatcher = dispatcher;
        SelectCategoryCommand = new RelayCommand<MetricVisibilityCategoryViewModel?>(category => SelectedCategory = category);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults);
        ShowCoreOnlyCommand = new RelayCommand(ShowCoreOnly);
        ShowAllProfessionalCommand = new RelayCommand(ShowAll);
        LoadCategories();
    }

    public ObservableCollection<MetricVisibilityCategoryViewModel> Categories { get; } = new();

    public MetricVisibilityCategoryViewModel? SelectedCategory
    {
        get => selectedCategory;
        set => SetProperty(ref selectedCategory, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public IRelayCommand<MetricVisibilityCategoryViewModel?> SelectCategoryCommand { get; }

    public IRelayCommand RestoreDefaultsCommand { get; }

    public IRelayCommand ShowCoreOnlyCommand { get; }

    public IRelayCommand ShowAllProfessionalCommand { get; }

    public void SetActive(bool active)
    {
        if (active)
        {
            LoadCategories();
        }
    }

    private void LoadCategories()
    {
        Categories.Clear();
        foreach (IGrouping<string, HardwareMetricCatalogItem> group in HardwareMetricCatalog.GetDefaultMetrics().GroupBy(item => item.PageTitle))
        {
            Categories.Add(new MetricVisibilityCategoryViewModel(
                group.Key,
                group.Select(item => new MetricVisibilityItemViewModel(item, settings, settingsService))));
        }

        SelectedCategory = Categories.FirstOrDefault();
    }

    private void RestoreDefaults()
    {
        settings.MetricVisibility.Clear();
        _ = settingsService.SaveAsync(settings);
        LoadCategories();
        StatusText = "显示项已恢复默认。";
        dashboard.SetSummaryActive(true);
    }

    private void ShowCoreOnly()
    {
        foreach (HardwareMetricCatalogItem item in HardwareMetricCatalog.GetDefaultMetrics())
        {
            settings.MetricVisibility[item.Metric.Id] = item.Metric.IsImportant;
        }

        _ = settingsService.SaveAsync(settings);
        LoadCategories();
        StatusText = "已仅显示核心指标。";
        dashboard.SetSummaryActive(true);
    }

    private void ShowAll()
    {
        foreach (HardwareMetricCatalogItem item in HardwareMetricCatalog.GetDefaultMetrics())
        {
            settings.MetricVisibility[item.Metric.Id] = true;
        }

        _ = settingsService.SaveAsync(settings);
        LoadCategories();
        StatusText = "已显示全部专业指标。";
        dashboard.SetSummaryActive(true);
    }
}
