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

namespace HardwareVision.ViewModels;

internal static class ViewModelHelpers
{
    public static void Dispatch(Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    public static string? FirstAvailable(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    public static string? FirstAvailable(IEnumerable<string?> values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    public static string ValueOrFallback(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();
    }

    public static string? Prop(HardwareDevice? device, string key)
    {
        return device?.Properties.TryGetValue(key, out string? value) == true ? value : null;
    }

    public static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ToMetricValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            double number when double.IsNaN(number) || double.IsInfinity(number) => null,
            float number when float.IsNaN(number) || float.IsInfinity(number) => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    public static double? SensorValueToBytes(SensorReading? reading)
    {
        if (reading?.Value is not double value)
        {
            return null;
        }

        string unit = reading.Unit.Trim();
        if (unit.Equals("GB", StringComparison.OrdinalIgnoreCase))
        {
            return value * 1024d * 1024d * 1024d;
        }

        if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
        {
            return value * 1024d * 1024d;
        }

        if (unit.Equals("KB", StringComparison.OrdinalIgnoreCase))
        {
            return value * 1024d;
        }

        return value;
    }

    public static string ResolveNetworkType(NetworkAdapterDevice device)
    {
        string text = $"{device.Name} {device.Description} {device.InterfaceType}";
        if (text.Contains("VPN", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TAP", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Wintun", StringComparison.OrdinalIgnoreCase))
        {
            return "VPN";
        }

        if (device.IsVirtual)
        {
            return "虚拟";
        }

        if (device.IsWireless)
        {
            return "无线";
        }

        return "有线";
    }

    public static void ReplaceMetricCollection(ObservableCollection<DetailMetricViewModel> target, IEnumerable<HardwareMetric> metrics)
    {
        target.Clear();
        foreach (HardwareMetric metric in HardwareDetailReadingHelpers.SortMetrics(metrics))
        {
            DetailMetricViewModel item = new();
            item.Update(metric);
            target.Add(item);
        }
    }

    public static void UpdateSensorRows(ObservableCollection<DetailSensorRowViewModel> target, IEnumerable<DetailSensorRowViewModel> rows)
    {
        DetailSensorRowViewModel[] desiredRows = rows.ToArray();
        for (int index = 0; index < desiredRows.Length; index++)
        {
            DetailSensorRowViewModel desiredRow = desiredRows[index];
            int existingIndex = FindSensorRowIndex(target, desiredRow.Id, index);

            if (existingIndex < 0)
            {
                target.Insert(index, desiredRow);
                continue;
            }

            if (existingIndex != index)
            {
                target.Move(existingIndex, index);
            }

            target[index].UpdateFrom(desiredRow);
        }

        while (target.Count > desiredRows.Length)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static int FindSensorRowIndex(ObservableCollection<DetailSensorRowViewModel> rows, string id, int startIndex)
    {
        for (int index = startIndex; index < rows.Count; index++)
        {
            if (string.Equals(rows[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
