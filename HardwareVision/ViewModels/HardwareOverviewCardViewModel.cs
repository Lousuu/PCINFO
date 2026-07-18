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

public sealed class HardwareOverviewCardViewModel : ObservableObject
{
    private string title = "--";
    private string hardwareName = "--";
    private string? hardwareNameToolTip;
    private string headerNote = string.Empty;
    private string badgeText = string.Empty;
    private bool isVisible = true;
    private string? toolTip;
    private bool canSelectHardware;
    private HardwareSelectionOptionViewModel? selectedHardwareOption;
    private Action<string?>? hardwareSelectionChanged;
    private bool isUpdatingHardwareSelection;

    public HardwareOverviewCardViewModel(HardwareOverviewKind kind)
    {
        Kind = kind;
    }

    public HardwareOverviewKind Kind { get; }

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public string HardwareName
    {
        get => hardwareName;
        set
        {
            if (SetProperty(ref hardwareName, value))
            {
                HardwareNameToolTip = ViewModelHelpers.NullIfShortOrSame(hardwareName, hardwareName, 28);
            }
        }
    }

    public string? HardwareNameToolTip
    {
        get => hardwareNameToolTip;
        private set => SetProperty(ref hardwareNameToolTip, value);
    }

    public string HeaderNote
    {
        get => headerNote;
        set => SetProperty(ref headerNote, value);
    }

    public string BadgeText
    {
        get => badgeText;
        set => SetProperty(ref badgeText, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    public string? ToolTip
    {
        get => toolTip;
        set => SetProperty(ref toolTip, value);
    }

    public ObservableCollection<DetailMetricViewModel> Metrics { get; } = new();

    public DetailMetricViewModel? PrimaryMetric => Metrics.FirstOrDefault(metric => metric.IsVisible);

    public IReadOnlyList<DetailMetricViewModel> SecondaryMetrics => Metrics
        .Where(metric => metric.IsVisible)
        .Skip(1)
        .ToArray();

    public int VisibleMetricCount => Metrics.Count(metric => metric.IsVisible);

    public ObservableCollection<HardwareSelectionOptionViewModel> HardwareOptions { get; } = new();

    public bool CanSelectHardware
    {
        get => canSelectHardware;
        private set
        {
            if (SetProperty(ref canSelectHardware, value))
            {
                OnPropertyChanged(nameof(HasHardwareSelector));
            }
        }
    }

    public bool HasHardwareSelector => CanSelectHardware && HardwareOptions.Count > 1;

    public HardwareSelectionOptionViewModel? SelectedHardwareOption
    {
        get => selectedHardwareOption;
        set
        {
            if (SetProperty(ref selectedHardwareOption, value)
                && !isUpdatingHardwareSelection
                && value is not null)
            {
                hardwareSelectionChanged?.Invoke(value.Id);
            }
        }
    }

    public void ReplaceMetrics(IEnumerable<HardwareMetric> metrics)
    {
        ViewModelHelpers.UpdateMetricCollection(Metrics, metrics);
        IsVisible = Metrics.Any(metric => metric.IsVisible);
        ToolTip = null;
        OnPropertyChanged(nameof(PrimaryMetric));
        OnPropertyChanged(nameof(SecondaryMetrics));
        OnPropertyChanged(nameof(VisibleMetricCount));
    }

    public void UpdateHardwareOptions(
        IEnumerable<(string Id, string Name)> options,
        string? selectedId,
        Action<string?>? selectionChanged)
    {
        hardwareSelectionChanged = selectionChanged;
        (string Id, string Name)[] desiredOptions = options
            .Where(option => !string.IsNullOrWhiteSpace(option.Id))
            .ToArray();

        for (int index = 0; index < desiredOptions.Length; index++)
        {
            (string id, string name) = desiredOptions[index];
            int existingIndex = FindOptionIndex(id, index);

            if (existingIndex < 0)
            {
                HardwareSelectionOptionViewModel option = new();
                option.Update(id, name);
                HardwareOptions.Insert(index, option);
                continue;
            }

            if (existingIndex != index)
            {
                HardwareOptions.Move(existingIndex, index);
            }

            HardwareOptions[index].Update(id, name);
        }

        while (HardwareOptions.Count > desiredOptions.Length)
        {
            HardwareOptions.RemoveAt(HardwareOptions.Count - 1);
        }

        CanSelectHardware = HardwareOptions.Count > 1;
        OnPropertyChanged(nameof(HasHardwareSelector));

        string? normalizedSelectedId = string.IsNullOrWhiteSpace(selectedId) ? null : selectedId;
        HardwareSelectionOptionViewModel? selected = HardwareOptions.FirstOrDefault(option =>
            string.Equals(option.Id, normalizedSelectedId, StringComparison.OrdinalIgnoreCase))
            ?? HardwareOptions.FirstOrDefault();

        isUpdatingHardwareSelection = true;
        try
        {
            SelectedHardwareOption = selected;
        }
        finally
        {
            isUpdatingHardwareSelection = false;
        }
    }

    public void ClearHardwareOptions()
    {
        hardwareSelectionChanged = null;
        HardwareOptions.Clear();
        CanSelectHardware = false;
        isUpdatingHardwareSelection = true;
        try
        {
            SelectedHardwareOption = null;
        }
        finally
        {
            isUpdatingHardwareSelection = false;
        }
        OnPropertyChanged(nameof(HasHardwareSelector));
    }

    private int FindOptionIndex(string id, int startIndex)
    {
        for (int index = startIndex; index < HardwareOptions.Count; index++)
        {
            if (string.Equals(HardwareOptions[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
