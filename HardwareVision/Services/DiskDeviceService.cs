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
				DiskDevice? diskDevice2 = list.FirstOrDefault(device => string.Equals(device.Id, item.Key, StringComparison.OrdinalIgnoreCase))
					?? FindMatchingDisk(list.Where(device => !IsLibreHardwareMonitorOnly(device)), text);
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

	private static bool IsLibreHardwareMonitorOnly(DiskDevice device)
	{
		return string.Equals(device.Source, LibreHardwareMonitorSource, StringComparison.OrdinalIgnoreCase);
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
		diskDevice.PnpDeviceId = FirstAvailable(device.Properties.GetValueOrDefault("PNPDeviceID"));
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
		diskDevice.UniqueId = FirstAvailable(device.Properties.GetValueOrDefault("UniqueId"), device.Id);
		diskDevice.ObjectId = FirstAvailable(device.Properties.GetValueOrDefault("ObjectId"));
		diskDevice.MediaType = ResolveMediaType(device.Properties.GetValueOrDefault("MediaType"));
		diskDevice.BusType = ResolveBusType(device.Properties.GetValueOrDefault("BusType"));
		diskDevice.Size = ParseUInt64(device.Properties.GetValueOrDefault("SizeBytes"));
		diskDevice.FirmwareRevision = device.Properties.GetValueOrDefault("FirmwareVersion");
		diskDevice.SmartStatus = text2;
		diskDevice.NvmeHealthStatus = text2;
		diskDevice.OperationalStatus = device.Properties.GetValueOrDefault("OperationalStatus");
		diskDevice.Source = "MSFT_PhysicalDisk";
		diskDevice.Availability = SensorAvailability.Available;
		ApplyStorageReliability(diskDevice, device);
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
		device.UniqueId = FirstAvailable(physicalDisk.Properties.GetValueOrDefault("UniqueId"), device.UniqueId);
		device.ObjectId = FirstAvailable(physicalDisk.Properties.GetValueOrDefault("ObjectId"), device.ObjectId);
		device.MediaType = FirstAvailable(device.MediaType, ResolveMediaType(physicalDisk.Properties.GetValueOrDefault("MediaType")));
		device.BusType = FirstAvailable(device.BusType, ResolveBusType(physicalDisk.Properties.GetValueOrDefault("BusType")));
		if (!device.Size.HasValue)
		{
			ulong? num2 = (device.Size = ParseUInt64(physicalDisk.Properties.GetValueOrDefault("SizeBytes")));
		}
		device.FirmwareRevision = FirstAvailable(device.FirmwareRevision, physicalDisk.Properties.GetValueOrDefault("FirmwareVersion"));
		string? text = ResolveHealthStatus(physicalDisk.Properties.GetValueOrDefault("HealthStatus"));
		device.SmartStatus = FirstAvailable(text, device.SmartStatus);
		device.NvmeHealthStatus = FirstAvailable(text, device.NvmeHealthStatus);
		device.OperationalStatus = FirstAvailable(physicalDisk.Properties.GetValueOrDefault("OperationalStatus"), device.OperationalStatus);
		ApplyStorageReliability(device, physicalDisk);
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
		device.Temperature ??= FindReading(device.Sensors, SensorType.Temperature, null, "Composite", "Temperature", "Drive", "Airflow");
		device.RemainingLife ??= FindReading(device.Sensors, SensorType.Load, (SensorReading reading) => IsHealthReading(reading), "Remaining Life", "Health");
		device.Wear ??= FindReading(device.Sensors, SensorType.Load, (SensorReading reading) => reading.SensorName.Contains("Wear", StringComparison.OrdinalIgnoreCase), "Wear");
		device.HealthStatus = device.RemainingLife ?? device.HealthStatus;
		device.ReadTotal ??= NormalizeDataReading(FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsReadReading(reading) && IsTotalReading(reading), "Host Reads", "Total Host Reads", "Data Units Read", "Data Read", "Read Total", "Total Reads"));
		device.WriteTotal ??= NormalizeDataReading(FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsWriteReading(reading) && IsTotalReading(reading), "Host Writes", "Total Host Writes", "Data Units Written", "Data Written", "Write Total", "Total Writes"));
		device.PowerOnHours ??= FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => reading.SensorName.Contains("Power", StringComparison.OrdinalIgnoreCase), "Power On Hours", "Power-On Hours", "Power On");
		device.PowerCycleCount ??= FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => reading.SensorName.Contains("Power", StringComparison.OrdinalIgnoreCase), "Power Cycle Count", "Power Cycles", "Cycle Count");
	}

	private static void ApplyStorageReliability(DiskDevice device, HardwareDevice physicalDisk)
	{
		IReadOnlyDictionary<string, string?> properties = physicalDisk.Properties;
		device.Temperature ??= ReliabilityReading(device, properties, "ReliabilityTemperature", "Temperature", SensorType.Temperature, "C");
		device.MaximumTemperature ??= ReliabilityReading(device, properties, "ReliabilityTemperatureMax", "Temperature Max", SensorType.Temperature, "C");
		device.Wear ??= ReliabilityReading(device, properties, "ReliabilityWear", "Wear", SensorType.Load, "%");

		SensorReading? remaining = ReliabilityReading(device, properties, "ReliabilityRemainingLife", "Remaining Life", SensorType.Load, "%");
		if (remaining is null && device.Wear?.Value is double wear && wear is >= 0d and <= 100d)
		{
			// MSFT_StorageReliabilityCounter.Wear is consumed endurance: 100% means the estimated wear limit is reached.
			remaining = CreateReading(device, "Remaining Life", 100d - wear, "%", SensorType.Load);
		}
		device.RemainingLife ??= remaining;
		device.HealthStatus ??= device.RemainingLife;
		device.PowerOnHours ??= ReliabilityReading(device, properties, "ReliabilityPowerOnHours", "Power On Hours", SensorType.Data, "h");
		device.PowerCycleCount ??= ReliabilityReading(device, properties, "ReliabilityPowerCycleCount", "Power Cycle Count", SensorType.Data, string.Empty);
		device.ReadTotal ??= ReliabilityReading(device, properties, "ReliabilityReadBytesTotal", "Total Host Reads", SensorType.Data, "B");
		device.WriteTotal ??= ReliabilityReading(device, properties, "ReliabilityWriteBytesTotal", "Total Host Writes", SensorType.Data, "B");
		device.ReadErrorsTotal ??= ReliabilityReading(device, properties, "ReliabilityReadErrorsTotal", "Read Errors Total", SensorType.Data, string.Empty);
		device.WriteErrorsTotal ??= ReliabilityReading(device, properties, "ReliabilityWriteErrorsTotal", "Write Errors Total", SensorType.Data, string.Empty);
		device.ReadLatencyMax ??= ReliabilityReading(device, properties, "ReliabilityReadLatencyMax", "Read Latency Max", SensorType.Data, "ms");
		device.WriteLatencyMax ??= ReliabilityReading(device, properties, "ReliabilityWriteLatencyMax", "Write Latency Max", SensorType.Data, "ms");
		device.FlushLatencyMax ??= ReliabilityReading(device, properties, "ReliabilityFlushLatencyMax", "Flush Latency Max", SensorType.Data, "ms");
		if (properties.Keys.Any(key => key.StartsWith("Reliability", StringComparison.OrdinalIgnoreCase)))
		{
			device.Source = CombineSources(device.Source, "MSFT_StorageReliabilityCounter");
		}
	}

	private static SensorReading? ReliabilityReading(
		DiskDevice device,
		IReadOnlyDictionary<string, string?> properties,
		string propertyName,
		string sensorName,
		SensorType type,
		string unit)
	{
		double? value = ParseDouble(properties.GetValueOrDefault(propertyName));
		return value.HasValue && double.IsFinite(value.Value) && value.Value >= 0d
			? CreateReading(device, sensorName, value.Value, unit, type)
			: null;
	}

	private static SensorReading CreateReading(DiskDevice device, string sensorName, double value, string unit, SensorType type)
	{
		return new SensorReading
		{
			DeviceName = device.Name,
			SensorName = sensorName,
			Category = SensorCategory.Disk,
			Type = type,
			Value = value,
			Unit = unit,
			IsAvailable = true,
			Availability = SensorAvailability.Available,
			Source = "MSFT_StorageReliabilityCounter",
			RawIdentifier = $"storage-reliability:{device.Id}:{sensorName}",
			Timestamp = DateTimeOffset.Now,
			LastUpdated = DateTimeOffset.Now
		};
	}

	private static SensorReading? NormalizeDataReading(SensorReading? reading)
	{
		if (reading?.Value is not double value || !double.IsFinite(value))
		{
			return reading;
		}

		double multiplier = reading.Unit.Trim().ToUpperInvariant() switch
		{
			"B" => 1d,
			"KB" => 1024d,
			"MB" => 1024d * 1024d,
			"GB" => 1024d * 1024d * 1024d,
			"TB" => 1024d * 1024d * 1024d * 1024d,
			_ => double.NaN
		};
		if (!double.IsFinite(multiplier) || !double.IsFinite(value * multiplier))
		{
			return reading;
		}

		return new SensorReading
		{
			DeviceName = reading.DeviceName,
			SensorName = reading.SensorName,
			Category = reading.Category,
			Type = reading.Type,
			Value = value * multiplier,
			Unit = "B",
			Min = reading.Min.HasValue ? reading.Min.Value * multiplier : null,
			Max = reading.Max.HasValue ? reading.Max.Value * multiplier : null,
			Status = reading.Status,
			Timestamp = reading.Timestamp,
			IsAvailable = reading.IsAvailable,
			Source = reading.Source,
			Availability = reading.Availability,
			RawIdentifier = reading.RawIdentifier,
			LastUpdated = reading.LastUpdated,
			ErrorMessage = reading.ErrorMessage
		};
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
		var candidates = (from disk in devices
			select new
			{
				Disk = disk,
				Score = ScoreDiskMatch(
					disk,
					name,
					serialNumber,
					size,
					device.Properties.GetValueOrDefault("UniqueId"),
					device.Properties.GetValueOrDefault("ObjectId"),
					device.Properties.GetValueOrDefault("DeviceId"))
			} into candidate
			where candidate.Score >= 0.7
			orderby candidate.Score descending
			select candidate).ToArray();
		if (candidates.Length == 0
			|| (candidates.Length > 1 && candidates[0].Score < 0.95 && candidates[0].Score - candidates[1].Score < 0.1))
		{
			return null;
		}
		return candidates[0].Disk;
	}

	private static DiskDevice? FindMatchingDisk(IEnumerable<DiskDevice> devices, string lhmDeviceName)
	{
		string lhmDeviceName2 = lhmDeviceName;
		var candidates = (from disk in devices
			select new
			{
				Disk = disk,
				Score = ScoreDiskNameMatch(disk.Name, lhmDeviceName2)
			} into candidate
			where candidate.Score >= 0.55
			orderby candidate.Score descending
			select candidate).ToArray();
		return candidates.Length > 1 && candidates[0].Score - candidates[1].Score < 0.1
			? null
			: candidates.FirstOrDefault()?.Disk;
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
		var candidates = (from device in devices
			select new
			{
				Disk = device,
				Score = ScoreDiskNameMatch(device.Name, normalizedInstanceName)
			} into candidate
			where candidate.Score >= 0.45
			orderby candidate.Score descending
			select candidate).ToArray();
		return candidates.Length > 1 && candidates[0].Score - candidates[1].Score < 0.1
			? null
			: candidates.FirstOrDefault()?.Disk;
	}

	private static double ScoreDiskMatch(
		DiskDevice disk,
		string? candidateName,
		string? candidateSerialNumber,
		ulong? candidateSize,
		string? candidateUniqueId,
		string? candidateObjectId,
		string? candidateDeviceId)
	{
		if (IdentifiersEqual(disk.UniqueId, candidateUniqueId)
			|| IdentifiersEqual(disk.ObjectId, candidateObjectId)
			|| IdentifiersEqual(disk.PnpDeviceId, candidateUniqueId))
		{
			return 1d;
		}

		bool hasBothSerials = !string.IsNullOrWhiteSpace(candidateSerialNumber) && !string.IsNullOrWhiteSpace(disk.SerialNumber);
		if (hasBothSerials)
		{
			return IdentifiersEqual(disk.SerialNumber, candidateSerialNumber) ? 1d : 0d;
		}

		bool hasBothSizes = candidateSize.HasValue && disk.Size.HasValue;
		if (hasBothSizes && candidateSize!.Value != disk.Size!.Value)
		{
			return 0d;
		}

		double score = ScoreDiskNameMatch(disk.Name, candidateName) * 0.55d;
		if (hasBothSizes)
		{
			score += 0.35d;
		}
		if (!string.IsNullOrWhiteSpace(candidateDeviceId)
			&& !string.IsNullOrWhiteSpace(disk.Index)
			&& string.Equals(candidateDeviceId.Trim(), disk.Index.Trim(), StringComparison.OrdinalIgnoreCase))
		{
			score += 0.4d;
		}
		return Math.Min(score, 1d);
	}

	private static bool IdentifiersEqual(string? left, string? right)
	{
		string normalizedLeft = NormalizeForContains(left);
		string normalizedRight = NormalizeForContains(right);
		return normalizedLeft.Length >= 4
			&& normalizedRight.Length >= 4
			&& string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);
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
		return reading.SensorName.Contains("Write", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Written", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTotalReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Host", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Data Read", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Data Written", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Data Units", StringComparison.OrdinalIgnoreCase);
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

	private static double? ParseDouble(string? value)
	{
		double result;
		return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : null;
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
