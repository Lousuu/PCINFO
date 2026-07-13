using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HardwareVision.ViewModels;

public sealed class RealtimeMetricChartViewModel : ObservableObject
{
    private const int SamplesPerSecond = 2;
    private const int MaxWindowSeconds = 120;
    private const int MaxPoints = SamplesPerSecond * MaxWindowSeconds;

    private readonly double[] samples = new double[MaxPoints];
    private int writeIndex;
    private int sampleCount;
    private IReadOnlyList<double> values = Array.Empty<double>();
    private string title = string.Empty;
    private string unit = string.Empty;
    private bool hasData;
    private int windowSeconds = 60;
    private double minimumValue = double.NaN;
    private double maximumValue = double.NaN;
    private bool adaptiveMaximumEnabled;
    private double adaptiveMaximumFloor;
    private double adaptiveMaximumCeiling;
    private double adaptiveMaximumStep;
    private string currentText = "--";
    private string averageText = "--";
    private string minimumText = "--";
    private string maximumText = "--";

    public RealtimeMetricChartViewModel()
    {
    }

    public RealtimeMetricChartViewModel(string title, string unit, double minimumValue = double.NaN, double maximumValue = double.NaN)
    {
        Title = title;
        Unit = unit;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
    }

    public string Title
    {
        get => title;
        set => SetProperty(ref title, value);
    }

    public string Unit
    {
        get => unit;
        set => SetProperty(ref unit, value);
    }

    public bool HasData
    {
        get => hasData;
        private set => SetProperty(ref hasData, value);
    }

    public int WindowSeconds
    {
        get => windowSeconds;
        set
        {
            int normalized = value switch
            {
                <= 30 => 30,
                <= 60 => 60,
                _ => 120
            };

            if (SetProperty(ref windowSeconds, normalized))
            {
                RefreshSnapshot();
            }
        }
    }

    public double MinimumValue
    {
        get => minimumValue;
        set => SetProperty(ref minimumValue, value);
    }

    public double MaximumValue
    {
        get => maximumValue;
        set => SetProperty(ref maximumValue, value);
    }

    public IReadOnlyList<double> Values
    {
        get => values;
        private set => SetProperty(ref values, value);
    }

    public string CurrentText
    {
        get => currentText;
        private set => SetProperty(ref currentText, value);
    }

    public string AverageText
    {
        get => averageText;
        private set => SetProperty(ref averageText, value);
    }

    public string MinimumText
    {
        get => minimumText;
        private set => SetProperty(ref minimumText, value);
    }

    public string MaximumText
    {
        get => maximumText;
        private set => SetProperty(ref maximumText, value);
    }

    public ObservableCollection<int> WindowOptions { get; } = new([30, 60, 120]);

    public void ConfigureAdaptiveMaximum(double floor, double ceiling, double step)
    {
        if (!double.IsFinite(floor)
            || !double.IsFinite(ceiling)
            || !double.IsFinite(step)
            || floor <= 0d
            || ceiling <= floor
            || step <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(floor), "Adaptive chart range values are invalid.");
        }

        adaptiveMaximumEnabled = true;
        adaptiveMaximumFloor = floor;
        adaptiveMaximumCeiling = ceiling;
        adaptiveMaximumStep = step;
        MaximumValue = floor;
        RefreshSnapshot();
    }

    public void Append(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            HasData = sampleCount > 0;
            return;
        }

        samples[writeIndex] = value.Value;
        writeIndex = (writeIndex + 1) % samples.Length;
        sampleCount = Math.Min(sampleCount + 1, samples.Length);
        RefreshSnapshot();
    }

    public void LoadHistory(IReadOnlyList<double> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        writeIndex = 0;
        sampleCount = 0;
        int start = Math.Max(0, history.Count - samples.Length);
        for (int index = start; index < history.Count; index++)
        {
            double value = history[index];
            if (!double.IsFinite(value))
            {
                continue;
            }

            samples[writeIndex] = value;
            writeIndex = (writeIndex + 1) % samples.Length;
            sampleCount = Math.Min(sampleCount + 1, samples.Length);
        }

        RefreshSnapshot();
    }

    public void Clear()
    {
        writeIndex = 0;
        sampleCount = 0;
        Values = Array.Empty<double>();
        HasData = false;
        CurrentText = "--";
        AverageText = "--";
        MinimumText = "--";
        MaximumText = "--";
        if (adaptiveMaximumEnabled)
        {
            MaximumValue = adaptiveMaximumFloor;
        }
    }

    private void RefreshSnapshot()
    {
        int desiredCount = Math.Clamp(WindowSeconds * SamplesPerSecond, SamplesPerSecond, MaxPoints);
        int count = Math.Min(sampleCount, desiredCount);
        if (count <= 0)
        {
            Clear();
            return;
        }

        double[] snapshot = new double[count];
        int start = (writeIndex - count + samples.Length) % samples.Length;
        double sum = 0d;
        double observedMinimum = double.PositiveInfinity;
        double observedMaximum = double.NegativeInfinity;
        for (int index = 0; index < count; index++)
        {
            double value = samples[(start + index) % samples.Length];
            snapshot[index] = value;
            sum += value;
            observedMinimum = Math.Min(observedMinimum, value);
            observedMaximum = Math.Max(observedMaximum, value);
        }

        UpdateAdaptiveMaximum(observedMaximum);
        Values = snapshot;
        HasData = true;
        CurrentText = FormatValue(snapshot[^1]);
        AverageText = FormatValue(sum / count);
        MinimumText = FormatValue(observedMinimum);
        MaximumText = FormatValue(observedMaximum);
    }

    private void UpdateAdaptiveMaximum(double observedMaximum)
    {
        if (!adaptiveMaximumEnabled || !double.IsFinite(observedMaximum))
        {
            return;
        }

        double baseline = double.IsFinite(MinimumValue) ? MinimumValue : 0d;
        double observedSpan = Math.Max(0d, observedMaximum - baseline);
        double paddedMaximum = baseline + observedSpan * 1.15d;
        double roundedMaximum = Math.Ceiling(paddedMaximum / adaptiveMaximumStep) * adaptiveMaximumStep;
        MaximumValue = Math.Clamp(roundedMaximum, adaptiveMaximumFloor, adaptiveMaximumCeiling);
    }

    private string FormatValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "--";
        }

        return Unit switch
        {
            "%" => string.Create(CultureInfo.CurrentCulture, $"{value:0.0} %"),
            "C" or "℃" => string.Create(CultureInfo.CurrentCulture, $"{value:0.0} ℃"),
            "W" => string.Create(CultureInfo.CurrentCulture, $"{value:0.0} W"),
            "V" => string.Create(CultureInfo.CurrentCulture, $"{value:0.000} V"),
            "RPM" => string.Create(CultureInfo.CurrentCulture, $"{value:0} RPM"),
            "MHz" => value >= 1000d
                ? string.Create(CultureInfo.CurrentCulture, $"{value / 1000d:0.00} GHz")
                : string.Create(CultureInfo.CurrentCulture, $"{value:0} MHz"),
            "FPS" => string.Create(CultureInfo.CurrentCulture, $"{value:0.0} FPS"),
            "ms" => string.Create(CultureInfo.CurrentCulture, $"{value:0.00} ms"),
            _ => string.IsNullOrWhiteSpace(Unit)
                ? value.ToString("0.##", CultureInfo.CurrentCulture)
                : string.Create(CultureInfo.CurrentCulture, $"{value:0.##} {Unit}")
        };
    }
}
