using CommunityToolkit.Mvvm.ComponentModel;
using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.ViewModels;

public sealed class DetailSensorRowViewModel : ObservableObject
{
    private string name = "--";
    private string type = "--";
    private string fullName = "--";
    private string fullType = "--";
    private string shortName = "--";
    private string shortType = "--";
    private bool hasLongName;
    private bool hasLongType;
    private bool hasLongReadout;
    private string? nameToolTip;
    private string? typeToolTip;
    private string? readoutToolTip;
    private string rawValue = "--";
    private string value = "--";
    private string readout = "--";
    private string unit = string.Empty;
    private string source = string.Empty;
    private string availability = "--";
    private bool isVisible = true;
    private string? toolTip;

    public DetailSensorRowViewModel()
    {
        Id = string.Empty;
    }

    private DetailSensorRowViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    public string Type
    {
        get => type;
        private set => SetProperty(ref type, value);
    }

    public string FullName
    {
        get => fullName;
        private set => SetProperty(ref fullName, value);
    }

    public string FullType
    {
        get => fullType;
        private set => SetProperty(ref fullType, value);
    }

    public string ShortName
    {
        get => shortName;
        private set
        {
            if (SetProperty(ref shortName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string ShortType
    {
        get => shortType;
        private set
        {
            if (SetProperty(ref shortType, value))
            {
                OnPropertyChanged(nameof(DisplayType));
            }
        }
    }

    public string DisplayName => ShortName;

    public string DisplayType => ShortType;

    public bool HasLongName
    {
        get => hasLongName;
        private set => SetProperty(ref hasLongName, value);
    }

    public bool HasLongType
    {
        get => hasLongType;
        private set => SetProperty(ref hasLongType, value);
    }

    public bool HasLongValue
    {
        get => hasLongReadout;
        private set
        {
            if (SetProperty(ref hasLongReadout, value))
            {
                OnPropertyChanged(nameof(HasLongReadout));
            }
        }
    }

    public bool HasLongReadout => HasLongValue;

    public string? NameToolTip
    {
        get => nameToolTip;
        private set => SetProperty(ref nameToolTip, value);
    }

    public string? TypeToolTip
    {
        get => typeToolTip;
        private set => SetProperty(ref typeToolTip, value);
    }

    public string? ValueToolTip
    {
        get => readoutToolTip;
        private set
        {
            if (SetProperty(ref readoutToolTip, value))
            {
                OnPropertyChanged(nameof(ReadoutToolTip));
            }
        }
    }

    public string? ReadoutToolTip => ValueToolTip;

    public string RawValue
    {
        get => rawValue;
        private set => SetProperty(ref rawValue, value);
    }

    public string Value
    {
        get => value;
        private set => SetProperty(ref this.value, value);
    }

    public string Readout
    {
        get => readout;
        private set => SetProperty(ref readout, value);
    }

    public string Unit
    {
        get => unit;
        private set
        {
            if (SetProperty(ref unit, value))
            {
                OnPropertyChanged(nameof(DisplayUnit));
            }
        }
    }

    public string DisplayUnit => Unit;

    public string Source
    {
        get => source;
        private set => SetProperty(ref source, value);
    }

    public string Availability
    {
        get => availability;
        private set => SetProperty(ref availability, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        private set => SetProperty(ref isVisible, value);
    }

    public string? ToolTip
    {
        get => toolTip;
        private set
        {
            if (SetProperty(ref toolTip, value))
            {
                OnPropertyChanged(nameof(FullToolTip));
            }
        }
    }

    public string? FullToolTip => ToolTip;

    public static DetailSensorRowViewModel FromReading(SensorReading reading)
    {
        HardwareMetric metric = HardwareMetricService.FromSensorReading(
            HardwareMetricService.CreateSensorMetricId(reading),
            reading.DeviceName,
            HardwareMetricService.MapCategory(reading.Category),
            ViewModelHelpers.FirstAvailable(reading.SensorName, reading.DeviceName, "--")!,
            $"{reading.DeviceName}/{reading.SensorName}/{reading.Type}",
            reading,
            "原始传感器读数。",
            false,
            true,
            0,
            reading.Category.ToString(),
            fallbackSource: reading.Source);

        return FromMetric(metric);
    }

    public static DetailSensorRowViewModel FromMetric(HardwareMetric metric)
    {
        DetailSensorRowViewModel row = new(metric.Id);
        row.ApplyMetric(metric);
        return row;
    }

    public void UpdateFrom(DetailSensorRowViewModel other)
    {
        Name = other.Name;
        Type = other.Type;
        FullName = other.FullName;
        FullType = other.FullType;
        ShortName = other.ShortName;
        ShortType = other.ShortType;
        HasLongName = other.HasLongName;
        HasLongType = other.HasLongType;
        HasLongValue = other.HasLongValue;
        NameToolTip = other.NameToolTip;
        TypeToolTip = other.TypeToolTip;
        ValueToolTip = other.ValueToolTip;
        RawValue = other.RawValue;
        Value = other.Value;
        Readout = other.Readout;
        Unit = other.Unit;
        Source = other.Source;
        Availability = other.Availability;
        IsVisible = other.IsVisible;
        ToolTip = other.ToolTip;
    }

    public bool HasSameValuesAs(DetailSensorRowViewModel other)
    {
        return string.Equals(Id, other.Id, StringComparison.Ordinal)
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Type, other.Type, StringComparison.Ordinal)
            && string.Equals(Readout, other.Readout, StringComparison.Ordinal)
            && string.Equals(Unit, other.Unit, StringComparison.Ordinal)
            && string.Equals(Source, other.Source, StringComparison.Ordinal)
            && string.Equals(Availability, other.Availability, StringComparison.Ordinal);
    }

    public static string CreateReadableSensorName(string? fullName)
    {
        return AbbreviatePathLikeName(fullName, 32);
    }

    public static string CreateReadableTechnicalName(string? technicalName)
    {
        return AbbreviatePathLikeName(technicalName, 42);
    }

    public static string AbbreviatePathLikeName(string? value, int maxLength = 40)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "--";
        }

        string trimmed = value.Trim();
        string[] parts = SplitPathLikeName(trimmed);
        string result = trimmed;

        if (parts.Length >= 2)
        {
            string[] meaningfulParts = SelectMeaningfulTail(parts);
            result = string.Join(" · ", meaningfulParts);
        }

        return Ellipsize(result, maxLength);
    }

    private void ApplyMetric(HardwareMetric metric)
    {
        string nextFullName = ViewModelHelpers.FirstAvailable(metric.DisplayName, metric.HardwareId, "--")!;
        string nextFullType = ViewModelHelpers.FirstAvailable(metric.TechnicalName, metric.Category.ToString(), "--")!;
        string nextReadout = HardwareMetricService.FormatDisplayValue(metric);
        string nextUnit = MetricFormatService.NormalizeUnit(metric.Unit);
        string nextAvailability = FormatAvailability(metric.Availability);
        string nextShortName = CreateReadableSensorName(nextFullName);
        string nextShortType = CreateReadableTechnicalName(nextFullType);
        bool nextHasLongName = RequiresToolTip(nextShortName, nextFullName, 28);
        bool nextHasLongType = RequiresToolTip(nextShortType, nextFullType, 36);
        bool nextHasLongReadout = RequiresToolTip(nextReadout, nextReadout, 24);

        Name = nextFullName;
        Type = nextFullType;
        FullName = nextFullName;
        FullType = nextFullType;
        ShortName = nextShortName;
        ShortType = nextShortType;
        HasLongName = nextHasLongName;
        HasLongType = nextHasLongType;
        HasLongValue = nextHasLongReadout;
        NameToolTip = nextHasLongName ? nextFullName : null;
        TypeToolTip = nextHasLongType ? nextFullType : null;
        ValueToolTip = nextHasLongReadout ? nextReadout : null;
        RawValue = string.IsNullOrWhiteSpace(metric.Value) ? HardwareMetricService.EmptyValue : metric.Value.Trim();
        Value = nextReadout;
        Readout = nextReadout;
        Unit = nextUnit;
        Source = metric.Source;
        Availability = nextAvailability;
        IsVisible = metric.Availability == MetricAvailability.Available
            && !string.IsNullOrWhiteSpace(nextReadout)
            && !string.Equals(nextReadout, HardwareMetricService.EmptyValue, StringComparison.Ordinal);
        ToolTip = nextHasLongName || nextHasLongType || nextHasLongReadout
            ? BuildToolTip(nextFullName, nextFullType, nextReadout, nextAvailability)
            : null;
    }

    private static string[] SelectMeaningfulTail(string[] parts)
    {
        string[] cleaned = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .ToArray();

        if (cleaned.Length <= 3)
        {
            return cleaned;
        }

        string last = cleaned[^1];
        string previous = cleaned[^2];
        string beforePrevious = cleaned[^3];
        if (IsGenericSensorToken(last))
        {
            return new[] { beforePrevious, previous, last }
                .Where((part, index) => index == 0 || !string.Equals(part, cleaned[^4], StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return new[] { previous, last };
    }

    private static string BuildToolTip(string fullName, string fullType, string readout, string availability)
    {
        return $"名称：{fullName}\n技术名：{fullType}\n读数：{readout}\n状态：{availability}";
    }

    private static string[] SplitPathLikeName(string value)
    {
        string normalized = value
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace("::", "/", StringComparison.Ordinal)
            .Replace(">", "/", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal);

        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsGenericSensorToken(string value)
    {
        return value.Equals("Temperature", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Load", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Power", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Clock", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Voltage", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Fan", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Throughput", StringComparison.OrdinalIgnoreCase);
    }

    private static string Ellipsize(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, Math.Max(1, maxLength - 1)), "…");
    }

    private static bool RequiresToolTip(string displayValue, string fullValue, int maxVisibleLength)
    {
        return !string.Equals(displayValue, fullValue, StringComparison.Ordinal)
            || fullValue.Length > maxVisibleLength;
    }

    private static string FormatAvailability(MetricAvailability availability)
    {
        return availability switch
        {
            MetricAvailability.Available => "可用",
            MetricAvailability.Loading => "读取中",
            MetricAvailability.NotReported => "未报告",
            MetricAvailability.Unsupported => "不支持",
            MetricAvailability.PermissionRequired => "需权限",
            MetricAvailability.InvalidValue => "无效",
            MetricAvailability.Error => "错误",
            _ => "--"
        };
    }
}
