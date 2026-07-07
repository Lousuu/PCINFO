using System.Globalization;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public static class HardwareMetricService
{
    public const string EmptyValue = "--";

    public static HardwareMetric FromSensorReading(
        string id,
        string hardwareId,
        HardwareMetricCategory category,
        string displayName,
        string technicalName,
        SensorReading? reading,
        string description,
        bool isImportant,
        bool defaultVisible,
        int displayOrder,
        string groupName,
        AppSettings? settings = null,
        string fallbackSource = "LibreHardwareMonitor")
    {
        HardwareMetric metric = new()
        {
            Id = NormalizeMetricId(id),
            HardwareId = string.IsNullOrWhiteSpace(hardwareId) ? "unknown" : hardwareId.Trim(),
            Category = category,
            DisplayName = displayName,
            TechnicalName = technicalName,
            Value = FormatRawValue(reading?.Value),
            Unit = reading?.Unit ?? string.Empty,
            Source = FirstAvailable(reading?.Source, fallbackSource, "Unknown"),
            Availability = ResolveAvailability(reading),
            Description = description,
            IsImportant = isImportant,
            IsVisible = defaultVisible,
            DisplayOrder = displayOrder,
            GroupName = groupName,
            LastUpdated = reading?.LastUpdated ?? DateTimeOffset.Now
        };

        return ApplySettings(metric, settings);
    }

    public static HardwareMetric FromValue(
        string id,
        string hardwareId,
        HardwareMetricCategory category,
        string displayName,
        string technicalName,
        string? value,
        string unit,
        string source,
        MetricAvailability availability,
        string description,
        bool isImportant,
        bool defaultVisible,
        int displayOrder,
        string groupName,
        AppSettings? settings = null)
    {
        string text = string.IsNullOrWhiteSpace(value) ? EmptyValue : value.Trim();
        if (IsInvalidNumericText(text))
        {
            availability = MetricAvailability.InvalidValue;
            text = EmptyValue;
        }

        HardwareMetric metric = new()
        {
            Id = NormalizeMetricId(id),
            HardwareId = string.IsNullOrWhiteSpace(hardwareId) ? "unknown" : hardwareId.Trim(),
            Category = category,
            DisplayName = displayName,
            TechnicalName = technicalName,
            Value = availability == MetricAvailability.Available ? text : EmptyValue,
            Unit = unit,
            Source = FirstAvailable(source, "Unknown"),
            Availability = availability,
            Description = description,
            IsImportant = isImportant,
            IsVisible = defaultVisible,
            DisplayOrder = displayOrder,
            GroupName = groupName,
            LastUpdated = DateTimeOffset.Now
        };

        return ApplySettings(metric, settings);
    }

    public static HardwareMetric CreateUnavailable(
        string id,
        string hardwareId,
        HardwareMetricCategory category,
        string displayName,
        string technicalName,
        string unit,
        string source,
        MetricAvailability availability,
        string description,
        bool isImportant,
        bool defaultVisible,
        int displayOrder,
        string groupName,
        AppSettings? settings = null)
    {
        return FromValue(
            id,
            hardwareId,
            category,
            displayName,
            technicalName,
            EmptyValue,
            unit,
            source,
            availability,
            description,
            isImportant,
            defaultVisible,
            displayOrder,
            groupName,
            settings);
    }

    public static HardwareMetric ApplySettings(HardwareMetric metric, AppSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(metric);

        if (settings?.MetricVisibility != null && settings.MetricVisibility.TryGetValue(metric.Id, out bool visible))
        {
            metric.IsVisible = visible;
        }

        if (settings?.MetricDisplayOrder != null && settings.MetricDisplayOrder.TryGetValue(metric.Id, out int displayOrder))
        {
            metric.DisplayOrder = displayOrder;
        }

        return metric;
    }

    public static HardwareMetricCategory MapCategory(SensorCategory category)
    {
        return category switch
        {
            SensorCategory.Cpu => HardwareMetricCategory.Cpu,
            SensorCategory.Gpu => HardwareMetricCategory.Gpu,
            SensorCategory.Memory => HardwareMetricCategory.Memory,
            SensorCategory.Disk => HardwareMetricCategory.Disk,
            SensorCategory.Network => HardwareMetricCategory.Network,
            SensorCategory.Motherboard => HardwareMetricCategory.Motherboard,
            SensorCategory.Battery => HardwareMetricCategory.Battery,
            _ => HardwareMetricCategory.Unknown
        };
    }

    public static string FormatDisplayValue(HardwareMetric metric)
    {
        return MetricFormatService.FormatMetricValue(metric);
    }

    public static string BuildToolTip(HardwareMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);

        string unit = MetricFormatService.NormalizeUnit(metric.Unit);
        string unavailableReason = MetricFormatService.BuildUnavailableReason(metric);

        StringBuilder builder = new();
        builder.AppendLine(metric.DisplayName);
        builder.Append("技术名：").AppendLine(FirstAvailable(metric.TechnicalName, metric.Id, EmptyValue));
        builder.Append("单位：").AppendLine(string.IsNullOrWhiteSpace(unit) ? "无或随数值标明" : unit);
        builder.Append("不可用原因：").Append(string.IsNullOrWhiteSpace(unavailableReason) ? EmptyValue : unavailableReason);
        return builder.ToString();
    }

    public static string CreateSensorMetricId(SensorReading reading)
    {
        string value = FirstAvailable(reading.RawIdentifier, reading.DeviceName, reading.SensorName);
        return NormalizeMetricId($"sensor.{value}.{reading.Type}");
    }

    public static string NormalizeMetricId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "metric.unknown";
        }

        StringBuilder builder = new(value.Length);
        bool previousWasSeparator = false;

        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('.');
                previousWasSeparator = true;
            }
        }

        string normalized = builder.ToString().Trim('.');
        return string.IsNullOrWhiteSpace(normalized) ? "metric.unknown" : normalized;
    }

    private static MetricAvailability ResolveAvailability(SensorReading? reading)
    {
        if (reading is null)
        {
            return MetricAvailability.NotReported;
        }

        if (!string.IsNullOrWhiteSpace(reading.ErrorMessage)
            && reading.ErrorMessage.Contains("权限", StringComparison.OrdinalIgnoreCase))
        {
            return MetricAvailability.PermissionRequired;
        }

        if (reading.Value is double value && (double.IsNaN(value) || double.IsInfinity(value)))
        {
            return MetricAvailability.InvalidValue;
        }

        if (reading.Value.HasValue && reading.IsAvailable)
        {
            return MetricAvailability.Available;
        }

        return reading.Availability switch
        {
            SensorAvailability.Available => MetricAvailability.Available,
            SensorAvailability.Error => MetricAvailability.Error,
            SensorAvailability.Unavailable => MetricAvailability.Unsupported,
            SensorAvailability.NotReported => MetricAvailability.NotReported,
            _ => MetricAvailability.NotReported
        };
    }

    private static string FormatRawValue(double? value)
    {
        return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : EmptyValue;
    }

    private static bool IsInvalidNumericText(string value)
    {
        return string.Equals(value, "NaN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Infinity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "-Infinity", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstAvailable(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }
}
