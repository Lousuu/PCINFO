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

public sealed class DetailSensorRowViewModel : ObservableObject
{
    public string Name { get; init; } = "--";

    public string Type { get; init; } = "--";

    public string FullName { get; init; } = "--";

    public string FullType { get; init; } = "--";

    public string ShortName { get; init; } = "--";

    public string ShortType { get; init; } = "--";

    public string DisplayName => ShortName;

    public string DisplayType => ShortType;

    public string Value { get; init; } = "--";

    public string Unit { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Availability { get; init; } = "--";

    public bool IsVisible { get; init; } = true;

    public string ToolTip { get; init; } = string.Empty;

    public string FullToolTip => ToolTip;

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
        string fullName = ViewModelHelpers.FirstAvailable(metric.DisplayName, metric.HardwareId, "--")!;
        string fullType = ViewModelHelpers.FirstAvailable(metric.TechnicalName, metric.Category.ToString(), "--")!;
        string value = HardwareMetricService.FormatDisplayValue(metric);
        string unit = MetricFormatService.NormalizeUnit(metric.Unit);
        string availability = metric.Availability.ToString();

        return new DetailSensorRowViewModel
        {
            Name = fullName,
            Type = fullType,
            FullName = fullName,
            FullType = fullType,
            ShortName = CreateReadableSensorName(fullName),
            ShortType = CreateReadableTechnicalName(fullType),
            Value = value,
            Unit = unit,
            Source = metric.Source,
            Availability = availability,
            IsVisible = metric.Availability == MetricAvailability.Available
                && !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, HardwareMetricService.EmptyValue, StringComparison.Ordinal),
            ToolTip = BuildToolTip(fullName, fullType, value, unit, availability)
        };
    }

    public bool HasSameValuesAs(DetailSensorRowViewModel other)
    {
        return string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Type, other.Type, StringComparison.Ordinal)
            && string.Equals(Value, other.Value, StringComparison.Ordinal)
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
        return AbbreviatePathLikeName(technicalName, 38);
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
            string last = parts[^1];
            string previous = parts[^2];
            result = IsGenericSensorToken(last) && parts.Length >= 3
                ? $"{parts[^3]} · {previous} · {last}"
                : $"{previous} · {last}";
        }

        return Ellipsize(result, maxLength);
    }

    private static string BuildToolTip(string fullName, string fullType, string value, string unit, string availability)
    {
        string unitSuffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        return $"名称：{fullName}\n技术名：{fullType}\n数值：{value}{unitSuffix}\n可用性：{availability}";
    }

    private static string[] SplitPathLikeName(string value)
    {
        string normalized = value
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace("::", "/", StringComparison.Ordinal)
            .Replace(">", "/", StringComparison.Ordinal);

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
}
