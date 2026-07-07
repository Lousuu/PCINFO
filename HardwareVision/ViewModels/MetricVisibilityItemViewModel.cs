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

public sealed class MetricVisibilityItemViewModel : ObservableObject
{
    private readonly AppSettings settings;
    private readonly ISettingsService settingsService;
    private bool isVisible;

    public MetricVisibilityItemViewModel(HardwareMetricCatalogItem catalogItem, AppSettings settings, ISettingsService settingsService)
    {
        CatalogItem = catalogItem;
        this.settings = settings;
        this.settingsService = settingsService;
        isVisible = ResolveVisibility(catalogItem.Metric.Id);
    }

    public HardwareMetricCatalogItem CatalogItem { get; }

    public string Id => CatalogItem.Metric.Id;

    public string DisplayName => CatalogItem.Metric.DisplayName;

    public string TechnicalName => CatalogItem.Metric.TechnicalName;

    public string Unit => MetricFormatService.NormalizeUnit(CatalogItem.Metric.Unit);

    public string Source => CatalogItem.Metric.Source;

    public string Description => CatalogItem.Metric.Description;

    public string ImportanceText => CatalogItem.Metric.IsImportant ? "核心" : "专业";

    public bool IsVisible
    {
        get => isVisible;
        set
        {
            if (SetProperty(ref isVisible, value))
            {
                settings.MetricVisibility[Id] = value;
                _ = settingsService.UpdateAsync(updated => updated.MetricVisibility[Id] = value);
            }
        }
    }

    private bool ResolveVisibility(string id)
    {
        return settings.MetricVisibility.TryGetValue(id, out bool visible)
            ? visible
            : CatalogItem.Metric.IsVisible;
    }
}
