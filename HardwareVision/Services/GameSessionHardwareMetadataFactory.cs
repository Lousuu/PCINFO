using System.Globalization;
using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class GameSessionHardwareMetadataFactory
{
    public static GameSessionHardwareMetadata? Create(HardwareSnapshot? snapshot, IEnumerable<DiskDevice>? disks = null)
    {
        if (snapshot is null) return null;

        HardwareDevice? gpu = FindDevice(snapshot, SensorCategory.Gpu);
        string? videoMode = GetProperty(gpu, "VideoModeDescription");
        string? refreshRate = GetProperty(gpu, "CurrentRefreshRate");
        string? display = JoinNonEmpty(
            videoMode,
            string.IsNullOrWhiteSpace(refreshRate) ? null : $"{refreshRate} Hz");
        string? memory = snapshot.MemoryTotal.HasValue ? FormatBytes(snapshot.MemoryTotal.Value) : null;
        if (snapshot.MemoryModules.Count > 0)
        {
            memory = JoinNonEmpty(memory, string.Join(" · ", snapshot.MemoryModules.Take(4)));
        }

        return new GameSessionHardwareMetadata
        {
            OperatingSystem = Clean(snapshot.OperatingSystem),
            CpuName = Clean(snapshot.CpuName),
            GpuName = Clean(snapshot.GpuName),
            GpuDriverVersion = Clean(GetProperty(gpu, "DriverVersion")),
            MotherboardName = Clean(snapshot.MotherboardName),
            MemoryDescription = Clean(memory),
            DiskDescription = Clean(BuildDiskDescription(disks) ?? snapshot.DiskSummary ?? snapshot.DiskDrives.FirstOrDefault()),
            DisplayDescription = Clean(display)
        };
    }

    private static HardwareDevice? FindDevice(HardwareSnapshot snapshot, SensorCategory category)
    {
        for (int index = 0; index < snapshot.Devices.Count; index++)
        {
            if (snapshot.Devices[index].Category == category) return snapshot.Devices[index];
        }

        return null;
    }

    private static string? BuildDiskDescription(IEnumerable<DiskDevice>? disks)
    {
        if (disks is null) return null;
        string[] descriptions = disks
            .Where(disk => !string.IsNullOrWhiteSpace(disk.Name))
            .Select(disk => JoinNonEmpty(disk.Name, disk.Size.HasValue ? FormatBytes(disk.Size.Value) : null))
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Select(description => description!)
            .ToArray();
        return descriptions.Length == 0 ? null : string.Join("; ", descriptions);
    }

    private static string? GetProperty(HardwareDevice? device, string key) =>
        device is not null && device.Properties.TryGetValue(key, out string? value) ? value : null;

    private static string FormatBytes(ulong value) =>
        (value / 1024d / 1024d / 1024d).ToString("0.##", CultureInfo.InvariantCulture) + " GiB";

    private static string? JoinNonEmpty(params string?[] values)
    {
        List<string> result = [];
        for (int index = 0; index < values.Length; index++)
        {
            if (!string.IsNullOrWhiteSpace(values[index])) result.Add(values[index]!.Trim());
        }

        return result.Count == 0 ? null : string.Join(" · ", result);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
