using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HardwareVision.ViewModels;

public sealed class VisibleMetricProjection : ObservableObject, IDisposable
{
    private readonly ObservableCollection<DetailMetricViewModel> source;
    private readonly HashSet<DetailMetricViewModel> observedMetrics = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<DetailMetricViewModel> visibleMetrics = Array.Empty<DetailMetricViewModel>();
    private bool isDisposed;

    public VisibleMetricProjection(ObservableCollection<DetailMetricViewModel> source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        source.CollectionChanged += OnCollectionChanged;
        SynchronizeSubscriptions();
        Refresh();
    }

    public IReadOnlyList<DetailMetricViewModel> VisibleMetrics => visibleMetrics;

    public DetailMetricViewModel? PrimaryMetric => visibleMetrics.FirstOrDefault();

    public IReadOnlyList<DetailMetricViewModel> SecondaryMetrics => visibleMetrics.Count <= 1
        ? Array.Empty<DetailMetricViewModel>()
        : visibleMetrics.Skip(1).ToArray();

    public int VisibleMetricCount => visibleMetrics.Count;

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        source.CollectionChanged -= OnCollectionChanged;
        foreach (DetailMetricViewModel metric in observedMetrics)
        {
            metric.PropertyChanged -= OnMetricPropertyChanged;
        }

        observedMetrics.Clear();
        isDisposed = true;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        SynchronizeSubscriptions();
        Refresh();
    }

    private void OnMetricPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, nameof(DetailMetricViewModel.IsVisible), StringComparison.Ordinal))
        {
            Refresh();
        }
    }

    private void SynchronizeSubscriptions()
    {
        DetailMetricViewModel[] removed = observedMetrics
            .Where(metric => !source.Contains(metric))
            .ToArray();
        foreach (DetailMetricViewModel metric in removed)
        {
            metric.PropertyChanged -= OnMetricPropertyChanged;
            observedMetrics.Remove(metric);
        }

        foreach (DetailMetricViewModel metric in source)
        {
            if (observedMetrics.Add(metric))
            {
                metric.PropertyChanged += OnMetricPropertyChanged;
            }
        }
    }

    private void Refresh()
    {
        visibleMetrics = source.Where(metric => metric.IsVisible).ToArray();
        OnPropertyChanged(nameof(VisibleMetrics));
        OnPropertyChanged(nameof(PrimaryMetric));
        OnPropertyChanged(nameof(SecondaryMetrics));
        OnPropertyChanged(nameof(VisibleMetricCount));
    }
}
