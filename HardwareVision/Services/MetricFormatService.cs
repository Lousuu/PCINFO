using System.Globalization;
using HardwareVision.Models;

namespace HardwareVision.Services;

public static class MetricFormatService
{
    public const string EmptyValue = "--";

    private static readonly string[] KnownUnits =
    [
        "℃", "C", "°C", "%", "W", "V", "RPM", "MHz", "GHz", "GB", "TB",
        "B", "KB", "MB", "PB", "B/s", "KB/s", "MB/s", "GB/s", "bps",
        "Kbps", "Mbps", "Gbps", "bit", "h"
    ];

    public static string FormatMetricValue(HardwareMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);

        if (metric.Availability != MetricAvailability.Available
            || string.IsNullOrWhiteSpace(metric.Value)
            || string.Equals(metric.Value, EmptyValue, StringComparison.Ordinal))
        {
            return EmptyValue;
        }

        if (!TryParseDouble(metric.Value, out double numericValue))
        {
            return FormatTextValue(metric.Value.Trim(), metric.Unit);
        }

        return FormatByUnit(
            numericValue,
            metric.Unit,
            metric.Category,
            metric.DisplayName,
            metric.TechnicalName);
    }

    public static string FormatSensorValue(SensorReading? reading)
    {
        if (reading?.Value is not double value
            || !reading.IsAvailable
            || !IsValid(value))
        {
            return EmptyValue;
        }

        return FormatByUnit(
            value,
            reading.Unit,
            HardwareMetricService.MapCategory(reading.Category),
            reading.SensorName,
            reading.SensorName);
    }

    public static string FormatTemperature(double? celsius)
    {
        return celsius.HasValue && IsValid(celsius.Value)
            ? $"{FormatVariableDecimal(celsius.Value, 1)} ℃"
            : EmptyValue;
    }

    public static string FormatFrequencyFromMhz(double? megahertz, bool preserveMhz = false)
    {
        if (!megahertz.HasValue || !IsValid(megahertz.Value))
        {
            return EmptyValue;
        }

        return preserveMhz
            ? $"{megahertz.Value:0} MHz"
            : $"{megahertz.Value / 1000d:0.00} GHz";
    }

    public static string FormatLoadPercent(double? percent)
    {
        return percent.HasValue && IsValid(percent.Value)
            ? $"{FormatVariableDecimal(percent.Value, 1)} %"
            : EmptyValue;
    }

    public static string FormatPower(double? watts)
    {
        return watts.HasValue && IsValid(watts.Value)
            ? $"{watts.Value:0.0} W"
            : EmptyValue;
    }

    public static string FormatVoltage(double? volts)
    {
        return volts.HasValue && IsValid(volts.Value)
            ? $"{volts.Value:0.000} V"
            : EmptyValue;
    }

    public static string FormatFanRpm(double? rpm)
    {
        return rpm.HasValue && IsValid(rpm.Value)
            ? $"{rpm.Value:0} RPM"
            : EmptyValue;
    }

    public static string FormatMemoryGigabytes(double? gigabytes)
    {
        return gigabytes.HasValue && IsValid(gigabytes.Value)
            ? $"{FormatVariableDecimal(gigabytes.Value, 2)} GB"
            : EmptyValue;
    }

    public static string FormatMemoryBytes(ulong? bytes)
    {
        return bytes.HasValue
            ? FormatMemoryGigabytes(bytes.Value / 1024d / 1024d / 1024d)
            : EmptyValue;
    }

    public static string FormatStorageBytes(ulong? bytes)
    {
        if (!bytes.HasValue)
        {
            return EmptyValue;
        }

        double gigabytes = bytes.Value / 1024d / 1024d / 1024d;
        return gigabytes >= 1024d
            ? $"{FormatVariableDecimal(gigabytes / 1024d, 2)} TB"
            : $"{FormatVariableDecimal(gigabytes, 2)} GB";
    }

    public static string FormatBytesAuto(ulong? bytes)
    {
        if (!bytes.HasValue)
        {
            return EmptyValue;
        }

        return FormatByteQuantity(bytes.Value);
    }

    public static string FormatBytesPerSecond(double? bytesPerSecond)
    {
        if (!bytesPerSecond.HasValue || !IsValid(bytesPerSecond.Value))
        {
            return EmptyValue;
        }

        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        double value = Math.Max(0, bytesPerSecond.Value);
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{FormatVariableDecimal(value, 2)} {units[unitIndex]}";
    }

    public static string FormatBitsPerSecond(double? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue || !IsValid(bitsPerSecond.Value))
        {
            return EmptyValue;
        }

        double value = Math.Max(0, bitsPerSecond.Value);

        if (value >= 1_000_000_000d)
        {
            return $"{FormatVariableDecimal(value / 1_000_000_000d, 2)} Gbps";
        }

        if (value >= 1_000_000d)
        {
            return $"{FormatVariableDecimal(value / 1_000_000d, 2)} Mbps";
        }

        if (value >= 1_000d)
        {
            return $"{FormatVariableDecimal(value / 1_000d, 2)} Kbps";
        }

        return $"{value:0} bps";
    }

    public static string NormalizeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return string.Empty;
        }

        return unit.Trim() switch
        {
            "C" or "°C" => "℃",
            "Bytes" => "B",
            "Bytes/s" => "B/s",
            _ => unit.Trim()
        };
    }

    public static string BuildUnavailableReason(HardwareMetric metric)
    {
        if (metric.Availability == MetricAvailability.Available)
        {
            return string.Empty;
        }

        return metric.Availability switch
        {
            MetricAvailability.Loading => "数据仍在读取中。",
            MetricAvailability.NotReported => "当前设备或传感器源未报告该数据。",
            MetricAvailability.Unsupported => "当前硬件或驱动不支持该指标。",
            MetricAvailability.PermissionRequired => "读取该指标需要更高权限。",
            MetricAvailability.InvalidValue => "传感器返回了无效数值。",
            MetricAvailability.Error => "读取该指标时出现错误。",
            _ => "该指标当前不可用。"
        };
    }

    private static string FormatByUnit(
        double value,
        string unit,
        HardwareMetricCategory category,
        string displayName,
        string technicalName)
    {
        if (!IsValid(value))
        {
            return EmptyValue;
        }

        string normalizedUnit = NormalizeUnit(unit);
        string combinedName = $"{displayName} {technicalName}";

        if (normalizedUnit == "℃")
        {
            return FormatTemperature(value);
        }

        if (normalizedUnit.Equals("MHz", StringComparison.OrdinalIgnoreCase))
        {
            bool preserveMhz = category == HardwareMetricCategory.Gpu
                && combinedName.Contains("Memory", StringComparison.OrdinalIgnoreCase)
                && combinedName.Contains("Clock", StringComparison.OrdinalIgnoreCase);

            return FormatFrequencyFromMhz(value, preserveMhz);
        }

        if (normalizedUnit.Equals("GHz", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value:0.00} GHz";
        }

        if (normalizedUnit == "%")
        {
            return FormatLoadPercent(value);
        }

        if (normalizedUnit.Equals("W", StringComparison.OrdinalIgnoreCase))
        {
            return FormatPower(value);
        }

        if (normalizedUnit.Equals("V", StringComparison.OrdinalIgnoreCase))
        {
            return FormatVoltage(value);
        }

        if (normalizedUnit.Equals("RPM", StringComparison.OrdinalIgnoreCase))
        {
            return FormatFanRpm(value);
        }

        if (normalizedUnit.Equals("GB", StringComparison.OrdinalIgnoreCase))
        {
            return FormatMemoryGigabytes(value);
        }

        if (normalizedUnit.Equals("TB", StringComparison.OrdinalIgnoreCase))
        {
            return $"{FormatVariableDecimal(value, 2)} TB";
        }

        if (normalizedUnit.Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            return FormatByteQuantity(value);
        }

        if (normalizedUnit.Equals("B/s", StringComparison.OrdinalIgnoreCase)
            || normalizedUnit.Equals("KB/s", StringComparison.OrdinalIgnoreCase)
            || normalizedUnit.Equals("MB/s", StringComparison.OrdinalIgnoreCase))
        {
            double bytesPerSecond = normalizedUnit.ToUpperInvariant() switch
            {
                "KB/S" => value * 1024d,
                "MB/S" => value * 1024d * 1024d,
                _ => value
            };

            return FormatBytesPerSecond(bytesPerSecond);
        }

        if (normalizedUnit.Equals("bps", StringComparison.OrdinalIgnoreCase))
        {
            return FormatBitsPerSecond(value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedUnit))
        {
            return $"{FormatVariableDecimal(value, 2)} {normalizedUnit}";
        }

        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatTextValue(string value, string unit)
    {
        string normalizedUnit = NormalizeUnit(unit);

        if (string.IsNullOrWhiteSpace(normalizedUnit)
            || KnownUnits.Any(knownUnit => value.Contains(knownUnit, StringComparison.OrdinalIgnoreCase)))
        {
            return value;
        }

        return $"{value} {normalizedUnit}";
    }

    private static string FormatByteQuantity(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, bytes);
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{FormatVariableDecimal(value, 2)} {units[unitIndex]}";
    }

    private static bool TryParseDouble(string value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }

    private static bool IsValid(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static string FormatVariableDecimal(double value, int maxDecimals)
    {
        string format = maxDecimals switch
        {
            <= 0 => "0",
            1 => Math.Abs(value % 1) < 0.05 ? "0" : "0.0",
            _ => Math.Abs(value % 1) < 0.005 ? "0" : "0." + new string('#', maxDecimals)
        };

        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
