using System.Globalization;

namespace HardwareVision.Services;

public static class GameEnergyFormatting
{
    public static string FormatEnergy(double? wattHours)
    {
        if (!wattHours.HasValue || !double.IsFinite(wattHours.Value) || wattHours.Value < 0d)
        {
            return HardwareMetricService.EmptyValue;
        }

        return wattHours.Value >= 1_000d
            ? $"{(wattHours.Value / 1_000d).ToString("0.00", CultureInfo.InvariantCulture)} kWh"
            : wattHours.Value < 1d
                ? $"{wattHours.Value.ToString("0.000", CultureInfo.InvariantCulture)} Wh"
                : $"{wattHours.Value.ToString("0.00", CultureInfo.InvariantCulture)} Wh";
    }

    public static string FormatPower(double? watts)
    {
        return watts.HasValue && double.IsFinite(watts.Value) && watts.Value >= 0d
            ? $"{watts.Value.ToString("0.00", CultureInfo.InvariantCulture)} W"
            : HardwareMetricService.EmptyValue;
    }

    public static string FormatCoverage(double? coveragePercent)
    {
        return coveragePercent.HasValue && double.IsFinite(coveragePercent.Value)
            ? $"{Math.Clamp(coveragePercent.Value, 0d, 100d).ToString("0.0", CultureInfo.InvariantCulture)}%"
            : HardwareMetricService.EmptyValue;
    }
}
