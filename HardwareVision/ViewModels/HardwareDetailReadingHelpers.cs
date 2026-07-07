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

public static class HardwareDetailReadingHelpers
{
    public static string ValueOrFallback(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
    }

    public static SensorReading? FindPreferredReading(
        IEnumerable<SensorReading> readings,
        SensorType type,
        params string[] preferredNames)
    {
        SensorReading[] candidates = readings
            .Where(reading => reading.Type == type && reading.IsAvailable)
            .ToArray();

        foreach (string preferredName in preferredNames)
        {
            SensorReading? match = candidates.FirstOrDefault(reading =>
                reading.SensorName.Contains(preferredName, StringComparison.OrdinalIgnoreCase)
                || reading.DeviceName.Contains(preferredName, StringComparison.OrdinalIgnoreCase)
                || reading.RawIdentifier.Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return candidates.FirstOrDefault();
    }

    public static IEnumerable<HardwareMetric> SortMetrics(IEnumerable<HardwareMetric> metrics)
    {
        return metrics
            .OrderBy(metric => metric.DisplayOrder)
            .ThenBy(metric => metric.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    public static bool DetailRowsEqual(IReadOnlyList<DetailSensorRowViewModel> left, IReadOnlyList<DetailSensorRowViewModel> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!left[index].HasSameValuesAs(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsPerCoreReading(SensorReading reading)
    {
        string name = $"{reading.SensorName} {reading.RawIdentifier}";
        return name.Contains("Core", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Thread", StringComparison.OrdinalIgnoreCase);
    }
}
