using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class DiskDeviceService
{
	private const string LibreHardwareMonitorSource = "LibreHardwareMonitor";

	private const string WmiSource = "WMI";

	private const string StorageWmiSource = "MSFT_PhysicalDisk";

	private const string PerformanceCounterSource = "PerformanceCounter";

	private static readonly string[] GenericDiskTokens = new string[11]
	{
		"ata", "disk", "drive", "fixed", "hdd", "media", "nvme", "scsi", "ssd", "storage",
		"usb"
	};

	public IReadOnlyList<DiskDevice> BuildDiskDevices(HardwareSnapshot? snapshot, IEnumerable<SensorReading> readings, IEnumerable<DiskPerformanceSnapshot>? performanceSnapshots)
	{
		ArgumentNullException.ThrowIfNull(readings, "readings");
		List<DiskDevice> list = new List<DiskDevice>();
		foreach (HardwareDevice diskDevice4 in GetDiskDevices(snapshot, "Win32_DiskDrive"))
		{
			list.Add(CreateFromDiskDrive(diskDevice4));
		}
		foreach (HardwareDevice diskDevice5 in GetDiskDevices(snapshot, "MSFT_PhysicalDisk"))
		{
			DiskDevice? diskDevice = FindMatchingDisk(list, diskDevice5);
			if (diskDevice == null)
			{
				diskDevice = CreateFromPhysicalDisk(diskDevice5);
				list.Add(diskDevice);
			}
			else
			{
				MergePhysicalDisk(diskDevice, diskDevice5);
			}
		}
		IEnumerable<IGrouping<string, SensorReading>> enumerable = readings.Where((SensorReading reading) => reading.Category == SensorCategory.Disk).GroupBy<SensorReading, string>(CreateLibreHardwareMonitorStorageKey, StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, SensorReading> item in enumerable)
		{
			List<SensorReading> list2 = item.OrderBy((SensorReading reading) => reading.Type).ThenBy<SensorReading, string>((SensorReading reading) => reading.SensorName, StringComparer.OrdinalIgnoreCase).ToList();
			if (list2.Count != 0)
			{
				string text = FirstAvailable(list2.Select((SensorReading sensor) => sensor.DeviceName));
				DiskDevice? diskDevice2 = FindMatchingDisk(list, text);
				if (diskDevice2 == null)
				{
					diskDevice2 = CreateFromLibreHardwareMonitor(text, item.Key);
					list.Add(diskDevice2);
				}
				MergeLibreHardwareMonitorSensors(diskDevice2, list2);
			}
		}
		foreach (DiskPerformanceSnapshot item2 in performanceSnapshots ?? Array.Empty<DiskPerformanceSnapshot>())
		{
			DiskDevice? diskDevice3 = FindMatchingPerformanceDevice(list, item2.InstanceName);
			if (diskDevice3 != null)
			{
				diskDevice3.ReadSpeed = item2.ReadBytesPerSecond;
				diskDevice3.WriteSpeed = item2.WriteBytesPerSecond;
				diskDevice3.PerformanceInstanceName = item2.InstanceName;
				diskDevice3.Source = CombineSources(diskDevice3.Source, "PerformanceCounter");
			}
		}
		foreach (DiskDevice item3 in list)
		{
			NormalizeDevice(item3);
			PopulateSensorMetrics(item3);
		}
		return (from device in list
			orderby device.IsSystemDisk descending, ParseInt32(device.Index).GetValueOrDefault(int.MaxValue)
			select device).ThenBy<DiskDevice, string>((DiskDevice device) => device.Name, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public DiskDevice? SelectPreferredDisk(IEnumerable<DiskDevice> devices, string? preferredDiskId)
	{
		DiskDevice[] array = devices.ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(preferredDiskId))
		{
			DiskDevice? preferred = array.FirstOrDefault(device => string.Equals(device.Id, preferredDiskId, StringComparison.OrdinalIgnoreCase));
			if (preferred != null)
			{
				return preferred;
			}
		}

		return array.FirstOrDefault(device => device.IsSystemDisk) ?? array.FirstOrDefault();
	}

	private static IEnumerable<HardwareDevice> GetDiskDevices(HardwareSnapshot? snapshot, string source)
	{
		if (snapshot?.Devices is null)
		{
			return Enumerable.Empty<HardwareDevice>();
		}
		string source2 = source;
		return (from device in snapshot.Devices
			where device.Category == SensorCategory.Disk
			where string.Equals(device.Properties.GetValueOrDefault("StorageSource"), source2, StringComparison.OrdinalIgnoreCase)
			select device);
	}

	private static DiskDevice CreateFromDiskDrive(HardwareDevice device)
	{
		ulong? usedSpace = ParseUInt64(device.Properties.GetValueOrDefault("UsedSpaceBytes"));
		ulong? freeSpace = ParseUInt64(device.Properties.GetValueOrDefault("FreeSpaceBytes"));
		DiskDevice diskDevice = new DiskDevice();
		diskDevice.Id = FirstAvailable(device.Id, device.Properties.GetValueOrDefault("DeviceID"), CreateStableId(device.Name)) ?? CreateStableId("disk");
		diskDevice.Name = FirstAvailable(device.Name, device.Model, "Disk") ?? "Disk";
		diskDevice.Model = FirstAvailable(device.Model, device.Name);
		diskDevice.Index = device.Properties.GetValueOrDefault("Index");
		diskDevice.SerialNumber = CleanSerialNumber(device.Properties.GetValueOrDefault("SerialNumber"));
		diskDevice.InterfaceType = NormalizeInterfaceType(device.Properties.GetValueOrDefault("InterfaceType"));
		diskDevice.MediaType = ResolveMediaType(device.Properties.GetValueOrDefault("MediaType"));
		diskDevice.Size = ParseUInt64(device.Properties.GetValueOrDefault("SizeBytes"));
		diskDevice.FirmwareRevision = device.Properties.GetValueOrDefault("FirmwareRevision");
		diskDevice.Partitions = SplitList(device.Properties.GetValueOrDefault("Partitions"));
		diskDevice.Volumes = SplitList(device.Properties.GetValueOrDefault("Volumes"));
		diskDevice.UsedSpace = usedSpace;
		diskDevice.FreeSpace = freeSpace;
		diskDevice.UsagePercent = CalculateUsagePercent(usedSpace, freeSpace);
		diskDevice.Source = "WMI";
		diskDevice.IsSystemDisk = string.Equals(device.Properties.GetValueOrDefault("IsSystemDisk"), "true", StringComparison.OrdinalIgnoreCase);
		diskDevice.SmartStatus = device.Properties.GetValueOrDefault("Status");
		diskDevice.Availability = SensorAvailability.Available;
		return diskDevice;
	}

	private static DiskDevice CreateFromPhysicalDisk(HardwareDevice device)
	{
		string text = FirstAvailable(device.Name, device.Model, "Disk");
		string? text2 = ResolveHealthStatus(device.Properties.GetValueOrDefault("HealthStatus"));
		DiskDevice diskDevice = new DiskDevice();
		diskDevice.Id = FirstAvailable(device.Id, device.Properties.GetValueOrDefault("DeviceId"), CleanSerialNumber(device.Properties.GetValueOrDefault("SerialNumber")), CreateStableId(text));
		diskDevice.Name = text;
		diskDevice.Model = FirstAvailable(device.Model, text);
		diskDevice.SerialNumber = CleanSerialNumber(device.Properties.GetValueOrDefault("SerialNumber"));
		diskDevice.MediaType = ResolveMediaType(device.Properties.GetValueOrDefault("MediaType"));
		diskDevice.BusType = ResolveBusType(device.Properties.GetValueOrDefault("BusType"));
		diskDevice.Size = ParseUInt64(device.Properties.GetValueOrDefault("SizeBytes"));
		diskDevice.FirmwareRevision = device.Properties.GetValueOrDefault("FirmwareVersion");
		diskDevice.SmartStatus = text2;
		diskDevice.NvmeHealthStatus = text2;
		diskDevice.Source = "MSFT_PhysicalDisk";
		diskDevice.Availability = SensorAvailability.Available;
		return diskDevice;
	}

	private static DiskDevice CreateFromLibreHardwareMonitor(string deviceName, string groupKey)
	{
		DiskDevice diskDevice = new DiskDevice();
		diskDevice.Id = FirstAvailable(groupKey, CreateStableId(deviceName)) ?? CreateStableId(deviceName);
		diskDevice.Name = deviceName;
		diskDevice.Model = deviceName;
		diskDevice.Source = "LibreHardwareMonitor";
		diskDevice.Availability = SensorAvailability.Unknown;
		return diskDevice;
	}

	private static void MergePhysicalDisk(DiskDevice device, HardwareDevice physicalDisk)
	{
		device.Name = ChooseBetterName(device.Name, physicalDisk.Name);
		device.Model = FirstAvailable(device.Model, physicalDisk.Model, physicalDisk.Name);
		device.SerialNumber = FirstAvailable(device.SerialNumber, CleanSerialNumber(physicalDisk.Properties.GetValueOrDefault("SerialNumber")));
		device.MediaType = FirstAvailable(device.MediaType, ResolveMediaType(physicalDisk.Properties.GetValueOrDefault("MediaType")));
		device.BusType = FirstAvailable(device.BusType, ResolveBusType(physicalDisk.Properties.GetValueOrDefault("BusType")));
		if (!device.Size.HasValue)
		{
			ulong? num2 = (device.Size = ParseUInt64(physicalDisk.Properties.GetValueOrDefault("SizeBytes")));
		}
		device.FirmwareRevision = FirstAvailable(device.FirmwareRevision, physicalDisk.Properties.GetValueOrDefault("FirmwareVersion"));
		string? text = ResolveHealthStatus(physicalDisk.Properties.GetValueOrDefault("HealthStatus"));
		device.SmartStatus = FirstAvailable(device.SmartStatus, text);
		device.NvmeHealthStatus = FirstAvailable(device.NvmeHealthStatus, text);
		device.Source = CombineSources(device.Source, "MSFT_PhysicalDisk");
		device.Availability = SensorAvailability.Available;
	}

	private static void MergeLibreHardwareMonitorSensors(DiskDevice device, List<SensorReading> sensors)
	{
		device.Name = ChooseBetterName(device.Name, FirstAvailable(sensors.Select((SensorReading sensor) => sensor.DeviceName)));
		device.Model = FirstAvailable(device.Model, device.Name);
		device.Sensors = sensors;
		device.Source = CombineSources(device.Source, "LibreHardwareMonitor");
	}

	private static void NormalizeDevice(DiskDevice device)
	{
		device.Name = FirstAvailable(device.Name, device.Model, "Disk") ?? "Disk";
		device.Model = FirstAvailable(device.Model, device.Name);
		device.SerialNumber = CleanSerialNumber(device.SerialNumber);
		device.InterfaceType = FirstAvailable(device.InterfaceType, device.BusType, InferInterfaceType(device));
		device.MediaType = ResolveMediaType(device.MediaType);
		device.BusType = ResolveBusType(device.BusType);
		if (string.IsNullOrWhiteSpace(device.Id))
		{
			device.Id = CreateStableId($"{device.Index}:{device.SerialNumber}:{device.Name}");
		}
		if (!device.UsagePercent.HasValue)
		{
			device.UsagePercent = CalculateUsagePercent(device.UsedSpace, device.FreeSpace);
		}
		if (string.IsNullOrWhiteSpace(device.Source))
		{
			device.Source = ((device.Sensors.Count > 0) ? "LibreHardwareMonitor" : "WMI");
		}
		device.Availability = ResolveAvailability(device);
	}

	private static void PopulateSensorMetrics(DiskDevice device)
	{
		device.Temperature = FindReading(device.Sensors, SensorType.Temperature, null, "Composite", "Temperature", "Drive", "Airflow");
		device.HealthStatus = FindReading(device.Sensors, SensorType.Load, (SensorReading reading) => IsHealthReading(reading), "Remaining Life", "Health", "Wear");
		device.ReadTotal = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsReadReading(reading) && IsTotalReading(reading), "Host Reads", "Total Host Reads", "Read Total", "Total Reads");
		device.WriteTotal = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsWriteReading(reading) && IsTotalReading(reading), "Host Writes", "Total Host Writes", "Write Total", "Total Writes");
		device.PowerOnHours = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => reading.SensorName.Contains("Power", StringComparison.OrdinalIgnoreCase), "Power On Hours", "Power-On Hours", "Power On");
		device.PowerCycleCount = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => reading.SensorName.Contains("Power", StringComparison.OrdinalIgnoreCase), "Power Cycle Count", "Power Cycles", "Cycle Count");
	}

	private static SensorReading? FindReading(IEnumerable<SensorReading> readings, SensorType type, Func<SensorReading, bool>? predicate, params string[] preferredNames)
	{
		Func<SensorReading, bool>? predicate2 = predicate;
		SensorReading[] source = (from reading in readings
			where reading.Type == type && reading.IsAvailable
			where predicate2?.Invoke(reading) ?? true
			select reading).ToArray();
		foreach (string preferredName in preferredNames)
		{
			SensorReading? sensorReading = source.FirstOrDefault((SensorReading reading) => reading.SensorName.Contains(preferredName, StringComparison.OrdinalIgnoreCase));
			if (sensorReading != null)
			{
				return sensorReading;
			}
		}
		return source.FirstOrDefault();
	}

	private static DiskDevice? FindMatchingDisk(IEnumerable<DiskDevice> devices, HardwareDevice device)
	{
		string? serialNumber = CleanSerialNumber(device.Properties.GetValueOrDefault("SerialNumber"));
		string name = FirstAvailable(device.Name, device.Model, device.Properties.GetValueOrDefault("FriendlyName"));
		ulong? size = ParseUInt64(device.Properties.GetValueOrDefault("SizeBytes"));
		return (from disk in devices
			select new
			{
				Disk = disk,
				Score = ScoreDiskMatch(disk, name, serialNumber, size)
			} into candidate
			where candidate.Score >= 0.55
			orderby candidate.Score descending
			select candidate.Disk).FirstOrDefault();
	}

	private static DiskDevice? FindMatchingDisk(IEnumerable<DiskDevice> devices, string lhmDeviceName)
	{
		string lhmDeviceName2 = lhmDeviceName;
		return (from disk in devices
			select new
			{
				Disk = disk,
				Score = ScoreDiskNameMatch(disk.Name, lhmDeviceName2)
			} into candidate
			where candidate.Score >= 0.45
			orderby candidate.Score descending
			select candidate.Disk).FirstOrDefault();
	}

	private static DiskDevice? FindMatchingPerformanceDevice(IEnumerable<DiskDevice> devices, string instanceName)
	{
		string instanceName2 = instanceName;
		string normalizedInstanceName = NormalizeForContains(instanceName2);
		foreach (DiskDevice device in devices)
		{
			if (!string.IsNullOrWhiteSpace(device.Index) && (instanceName2.StartsWith(device.Index + " ", StringComparison.OrdinalIgnoreCase) || string.Equals(instanceName2, device.Index, StringComparison.OrdinalIgnoreCase)))
			{
				return device;
			}
			if (device.Volumes.Any((string volume) => ContainsVolumeLetter(instanceName2, volume)))
			{
				return device;
			}
		}
		return (from device in devices
			select new
			{
				Disk = device,
				Score = ScoreDiskNameMatch(device.Name, normalizedInstanceName)
			} into candidate
			where candidate.Score >= 0.45
			orderby candidate.Score descending
			select candidate.Disk).FirstOrDefault();
	}

	private static double ScoreDiskMatch(DiskDevice disk, string? candidateName, string? candidateSerialNumber, ulong? candidateSize)
	{
		double num = 0.0;
		if (!string.IsNullOrWhiteSpace(candidateSerialNumber) && !string.IsNullOrWhiteSpace(disk.SerialNumber) && string.Equals(NormalizeForContains(disk.SerialNumber), NormalizeForContains(candidateSerialNumber), StringComparison.Ordinal))
		{
			num += 0.7;
		}
		if (candidateSize.HasValue && disk.Size.HasValue && candidateSize.Value == disk.Size.Value)
		{
			num += 0.2;
		}
		num += ScoreDiskNameMatch(disk.Name, candidateName) * 0.5;
		return Math.Min(num, 1.0);
	}

	private static double ScoreDiskNameMatch(string? left, string? right)
	{
		string text = NormalizeForContains(left);
		string text2 = NormalizeForContains(right);
		if (text.Length == 0 || text2.Length == 0)
		{
			return 0.0;
		}
		if (text.Contains(text2, StringComparison.Ordinal) || text2.Contains(text, StringComparison.Ordinal))
		{
			return 1.0;
		}
		string[] array = TokenizeName(left);
		string[] array2 = TokenizeName(right);
		if (array.Length == 0 || array2.Length == 0)
		{
			return 0.0;
		}
		int num = array.Intersect<string>(array2, StringComparer.OrdinalIgnoreCase).Count();
		return (double)num / (double)Math.Min(array.Length, array2.Length);
	}

	private static string CreateLibreHardwareMonitorStorageKey(SensorReading reading)
	{
		string? text = TryGetLibreHardwareMonitorRootIdentifier(reading.RawIdentifier);
		return FirstAvailable(text, reading.DeviceName, reading.RawIdentifier, CreateStableId(reading.DeviceName));
	}

	private static string? TryGetLibreHardwareMonitorRootIdentifier(string? rawIdentifier)
	{
		if (string.IsNullOrWhiteSpace(rawIdentifier))
		{
			return null;
		}
		string[] array = rawIdentifier.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length >= 2)
		{
			return "/" + array[0] + "/" + array[1];
		}
		return rawIdentifier.Trim();
	}

	private static bool ContainsVolumeLetter(string instanceName, string volumeSummary)
	{
		string[] array = volumeSummary.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (text.Length == 2 && text[1] == ':' && instanceName.Contains(text, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static SensorAvailability ResolveAvailability(DiskDevice device)
	{
		if (device.Sensors.Any((SensorReading sensor) => sensor.IsAvailable) || device.ReadSpeed.HasValue || device.WriteSpeed.HasValue || device.Size.HasValue || device.UsedSpace.HasValue || device.FreeSpace.HasValue)
		{
			return SensorAvailability.Available;
		}
		if (device.Sensors.Count > 0)
		{
			return SensorAvailability.NotReported;
		}
		return SensorAvailability.Unknown;
	}

	private static string? InferInterfaceType(DiskDevice device)
	{
		string text = $"{device.Name} {device.Model} {device.BusType} {device.InterfaceType}";
		if (text.Contains("nvme", StringComparison.OrdinalIgnoreCase))
		{
			return "NVMe";
		}
		if (text.Contains("usb", StringComparison.OrdinalIgnoreCase))
		{
			return "USB";
		}
		if (text.Contains("sata", StringComparison.OrdinalIgnoreCase) || text.Contains("ata", StringComparison.OrdinalIgnoreCase))
		{
			return "SATA";
		}
		return null;
	}

	private static string? ResolveMediaType(string? value)
	{
		string text = FirstAvailable(value);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = text.Trim();
		if (1 == 0)
		{
		}
		string? result = text2 switch
		{
			"0" => null, 
			"3" => "HDD", 
			"4" => "SSD", 
			"5" => "SCM", 
			_ => text.Contains("solid", StringComparison.OrdinalIgnoreCase) ? "SSD" : (text.Contains("ssd", StringComparison.OrdinalIgnoreCase) ? "SSD" : ((!text.Contains("hard", StringComparison.OrdinalIgnoreCase)) ? text.Trim() : "HDD")), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string? ResolveBusType(string? value)
	{
		string text = FirstAvailable(value);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = text.Trim();
		if (1 == 0)
		{
		}
		string result = text2 switch
		{
			"1" => "SCSI", 
			"2" => "ATAPI", 
			"3" => "ATA", 
			"4" => "IEEE 1394", 
			"6" => "Fibre Channel", 
			"7" => "USB", 
			"8" => "RAID", 
			"9" => "iSCSI", 
			"10" => "SAS", 
			"11" => "SATA", 
			"12" => "SD", 
			"13" => "MMC", 
			"14" => "Virtual", 
			"15" => "File Backed Virtual", 
			"16" => "Storage Spaces", 
			"17" => "NVMe", 
			_ => text.Trim(), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string? ResolveHealthStatus(string? value)
	{
		string text = FirstAvailable(value);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string text2 = text.Trim();
		if (1 == 0)
		{
		}
		string result = text2 switch
		{
			"0" => "Healthy", 
			"1" => "Warning", 
			"2" => "Unhealthy", 
			"5" => "Unknown", 
			_ => text.Trim(), 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string? NormalizeInterfaceType(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		return value.Contains("SCSI", StringComparison.OrdinalIgnoreCase) ? "SCSI" : value.Trim();
	}

	private static bool IsHealthReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Life", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Health", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Wear", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsReadReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Read", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsWriteReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Write", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTotalReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Host", StringComparison.OrdinalIgnoreCase);
	}

	private static double? CalculateUsagePercent(ulong? usedSpace, ulong? freeSpace)
	{
		if (!usedSpace.HasValue || !freeSpace.HasValue)
		{
			return null;
		}
		ulong num = usedSpace.Value + freeSpace.Value;
		return (num == 0L) ? null : new double?((double)usedSpace.Value * 100.0 / (double)num);
	}

	private static string ChooseBetterName(string? currentName, string? candidateName)
	{
		if (string.IsNullOrWhiteSpace(currentName))
		{
			return FirstAvailable(candidateName, "Disk") ?? "Disk";
		}
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return currentName.Trim();
		}
		string text = currentName.Trim();
		string text2 = candidateName.Trim();
		return (text2.Length > text.Length) ? text2 : text;
	}

	private static string CombineSources(string? existingSource, string newSource)
	{
		string[] value = (existingSource ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(newSource).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return string.Join(", ", value);
	}

	private static List<string> SplitList(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return new List<string>();
		}
		return value.Split(new char[2] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string? CleanSerialNumber(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		return value.Trim().TrimEnd('.');
	}

	private static ulong? ParseUInt64(string? value)
	{
		ulong result;
		return ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? new ulong?(result) : null;
	}

	private static int? ParseInt32(string? value)
	{
		int result;
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? new int?(result) : null;
	}

	private static string CreateStableId(string value)
	{
		string text = NormalizeForContains(value);
		return string.IsNullOrWhiteSpace(text) ? "disk" : ("disk-" + text);
	}

	private static string[] TokenizeName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Array.Empty<string>();
		}
		string text = NormalizeForTokens(value);
		return (from token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where !GenericDiskTokens.Contains<string>(token, StringComparer.OrdinalIgnoreCase)
			select token).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string NormalizeForContains(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		string text = value.ToLowerInvariant();
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static string NormalizeForTokens(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		string text = value.ToLowerInvariant();
		foreach (char c in text)
		{
			stringBuilder.Append(char.IsLetterOrDigit(c) ? c : ' ');
		}
		return stringBuilder.ToString();
	}

	private static string FirstAvailable(IEnumerable<string?> values)
	{
		return values.FirstOrDefault((string? value) => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
	}

	private static string FirstAvailable(params string?[] values)
	{
		return FirstAvailable(values.AsEnumerable());
	}
}
