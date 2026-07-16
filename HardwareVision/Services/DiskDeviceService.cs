using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HardwareVision.Models;
using HardwareVision.Utilities;

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

	private static readonly string[] BridgeHints =
	[
		"bridge", "rtl9210", "jms578", "jms583", "asm2362", "uasp", "usb to", "usb-to"
	];

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
		List<LhmDiskGroup> lhmGroups = readings
			.Where(reading => reading.Category == SensorCategory.Disk)
			.GroupBy(CreateLibreHardwareMonitorStorageKey, StringComparer.OrdinalIgnoreCase)
			.Select(group => CreateLhmDiskGroup(group.Key, group))
			.Where(group => group.Sensors.Count > 0)
			.ToList();
		HashSet<DiskDevice> assignedWmiDevices = new();
		List<LhmDiskGroup> unmatchedLhmGroups = new();
		foreach (LhmDiskGroup group in lhmGroups)
		{
			DiskDevice? diskDevice2 = list.FirstOrDefault(device => MatchesIdentity(device, group.Key));
			diskDevice2 ??= FindMatchingDisk(
				list.Where(device => !IsLibreHardwareMonitorOnly(device) && !assignedWmiDevices.Contains(device)),
				group.DeviceName);
			if (diskDevice2 is null)
			{
				unmatchedLhmGroups.Add(group);
				continue;
			}

			MergeLibreHardwareMonitorSensors(diskDevice2, group, preferSensorIdentity: false);
			assignedWmiDevices.Add(diskDevice2);
		}

		MergeExternalBridgeGroups(list, unmatchedLhmGroups, assignedWmiDevices);
		foreach (LhmDiskGroup group in unmatchedLhmGroups.Where(group => !group.IsMatched))
		{
			DiskDevice diskDevice2 = CreateFromLibreHardwareMonitor(group.DeviceName, group.Key);
			list.Add(diskDevice2);
			MergeLibreHardwareMonitorSensors(diskDevice2, group, preferSensorIdentity: false);
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
			DiskDevice? preferred = array.FirstOrDefault(device => MatchesIdentity(device, preferredDiskId));
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
		diskDevice.IsExternalBridge = IsExternalBridgeDevice(
			diskDevice.Name,
			diskDevice.Model,
			device.Properties.GetValueOrDefault("MediaType"),
			diskDevice.InterfaceType,
			null,
			diskDevice.PnpDeviceId);
		if (diskDevice.IsExternalBridge)
		{
			diskDevice.TransportDeviceName = diskDevice.Name;
			diskDevice.BridgeControllerName = diskDevice.Name;
		}
		AddIdentityAliases(diskDevice, device.Id, device.Properties.GetValueOrDefault("DeviceID"), diskDevice.SerialNumber, diskDevice.PnpDeviceId);
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
		diskDevice.Index = device.Properties.GetValueOrDefault("DeviceId");
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
		diskDevice.IsExternalBridge = IsExternalBridgeDevice(
			diskDevice.Name,
			diskDevice.Model,
			diskDevice.MediaType,
			null,
			diskDevice.BusType,
			null);
		if (diskDevice.IsExternalBridge)
		{
			diskDevice.TransportDeviceName = diskDevice.Name;
			diskDevice.BridgeControllerName = diskDevice.Name;
		}
		AddIdentityAliases(diskDevice, device.Id, device.Properties.GetValueOrDefault("DeviceId"), diskDevice.SerialNumber, diskDevice.UniqueId, diskDevice.ObjectId);
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
		AddIdentityAliases(diskDevice, groupKey);
		return diskDevice;
	}

	private static void MergePhysicalDisk(DiskDevice device, HardwareDevice physicalDisk)
	{
		string previousName = device.Name;
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
		device.Index = FirstAvailable(device.Index, physicalDisk.Properties.GetValueOrDefault("DeviceId"));
		bool physicalIsBridge = IsExternalBridgeDevice(
			physicalDisk.Name,
			physicalDisk.Model,
			physicalDisk.Properties.GetValueOrDefault("MediaType"),
			null,
			ResolveBusType(physicalDisk.Properties.GetValueOrDefault("BusType")),
			null);
		device.IsExternalBridge |= physicalIsBridge;
		if (device.IsExternalBridge)
		{
			device.TransportDeviceName = FirstAvailable(device.TransportDeviceName, previousName, physicalDisk.Name);
			device.BridgeControllerName = FirstAvailable(device.BridgeControllerName, previousName, physicalDisk.Name);
		}
		AddIdentityAliases(
			device,
			physicalDisk.Id,
			physicalDisk.Properties.GetValueOrDefault("DeviceId"),
			physicalDisk.Properties.GetValueOrDefault("UniqueId"),
			physicalDisk.Properties.GetValueOrDefault("ObjectId"));
		ApplyStorageReliability(device, physicalDisk);
		device.Source = CombineSources(device.Source, "MSFT_PhysicalDisk");
		device.Availability = SensorAvailability.Available;
	}

	private static void MergeLibreHardwareMonitorSensors(DiskDevice device, LhmDiskGroup group, bool preferSensorIdentity)
	{
		if (preferSensorIdentity)
		{
			device.TransportDeviceName = FirstAvailable(device.TransportDeviceName, device.Name);
			device.BridgeControllerName = FirstAvailable(device.BridgeControllerName, device.Name);
			device.Name = FirstAvailable(group.DeviceName, device.Name);
			device.Model = FirstAvailable(group.DeviceName, device.Model, device.Name);
		}
		else
		{
			device.Name = ChooseBetterName(device.Name, group.DeviceName);
			device.Model = FirstAvailable(device.Model, device.Name);
		}
		device.Sensors = group.Sensors;
		AddIdentityAliases(device, group.Key);
		device.Source = CombineSources(device.Source, "LibreHardwareMonitor");
		group.IsMatched = true;
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
		AddIdentityAliases(device, device.Id);
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
			where candidate.Score >= 55d
			orderby candidate.Score descending
			select candidate).ToArray();
		if (candidates.Length == 0
			|| (candidates.Length > 1 && candidates[0].Score - candidates[1].Score < 10d))
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
		bool hasBothSerials = !string.IsNullOrWhiteSpace(candidateSerialNumber) && !string.IsNullOrWhiteSpace(disk.SerialNumber);
		bool hasBothSizes = candidateSize.HasValue && disk.Size.HasValue;
		bool hasDiskIndex = TryGetPhysicalIndex(disk.Index, out int diskIndex);
		bool hasCandidateIndex = TryGetPhysicalIndex(candidateDeviceId, out int candidateIndex);
		bool hasBothIndices = hasDiskIndex && hasCandidateIndex;
		if ((hasBothSerials && !IdentifiersEqual(disk.SerialNumber, candidateSerialNumber))
			|| (hasBothSizes && !AreCapacitiesCompatible(disk.Size!.Value, candidateSize!.Value))
			|| (hasBothIndices && diskIndex != candidateIndex)
			|| IdentifiersConflict(disk.UniqueId, candidateUniqueId)
			|| IdentifiersConflict(disk.ObjectId, candidateObjectId))
		{
			return -1d;
		}

		if ((hasBothSerials && IdentifiersEqual(disk.SerialNumber, candidateSerialNumber))
			|| IdentifiersEqual(disk.UniqueId, candidateUniqueId)
			|| IdentifiersEqual(disk.ObjectId, candidateObjectId)
			|| MatchesIdentity(disk, candidateUniqueId)
			|| MatchesIdentity(disk, candidateObjectId)
			|| StableIdentifierPartsEqual(disk.PnpDeviceId, candidateUniqueId)
			|| StableIdentifierPartsEqual(disk.PnpDeviceId, candidateObjectId))
		{
			return 100d;
		}

		if (hasBothIndices && diskIndex == candidateIndex)
		{
			return 95d;
		}

		double score = ScoreDiskNameMatch(disk.Name, candidateName) * 30d;
		if (hasBothSizes)
		{
			score += 30d;
		}
		return score;
	}

	private static bool IdentifiersConflict(string? left, string? right)
	{
		return !string.IsNullOrWhiteSpace(left)
			&& !string.IsNullOrWhiteSpace(right)
			&& !IdentifiersEqual(left, right);
	}

	private static bool AreCapacitiesCompatible(ulong left, ulong right)
	{
		ulong difference = left >= right ? left - right : right - left;
		if (Math.Max(left, right) < 1024UL * 1024UL * 1024UL)
		{
			return difference == 0UL;
		}
		ulong relativeTolerance = Math.Max(left, right) / 100UL;
		const ulong absoluteTolerance = 64UL * 1024UL * 1024UL;
		return difference <= Math.Max(relativeTolerance, absoluteTolerance);
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

	private static LhmDiskGroup CreateLhmDiskGroup(string key, IEnumerable<SensorReading> readings)
	{
		List<SensorReading> sensors = readings
			.OrderBy(reading => reading.Type)
			.ThenBy(reading => reading.SensorName, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return new LhmDiskGroup
		{
			Key = key,
			DeviceName = FirstAvailable(sensors.Select(sensor => sensor.DeviceName)),
			RootIndex = TryGetPhysicalIndex(key, out int index) ? index : null,
			HasSmartEvidence = sensors.Any(IsStorageIdentitySensor),
			Sensors = sensors
		};
	}

	private static bool IsStorageIdentitySensor(SensorReading reading)
	{
		return reading.Type is SensorType.Temperature or SensorType.Data or SensorType.Load
			|| reading.SensorName.Contains("SMART", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Life", StringComparison.OrdinalIgnoreCase)
			|| reading.SensorName.Contains("Wear", StringComparison.OrdinalIgnoreCase);
	}

	private static void MergeExternalBridgeGroups(
		List<DiskDevice> devices,
		List<LhmDiskGroup> groups,
		HashSet<DiskDevice> assignedWmiDevices)
	{
		LhmDiskGroup[] unmatchedGroups = groups.Where(group => !group.IsMatched).ToArray();
		DiskDevice[] bridges = devices
			.Where(device => !IsLibreHardwareMonitorOnly(device) && device.IsExternalBridge && !assignedWmiDevices.Contains(device))
			.ToArray();
		if (unmatchedGroups.Length == 0 || bridges.Length == 0)
		{
			return;
		}

		bool uniquePair = unmatchedGroups.Length == 1 && bridges.Length == 1;
		List<BridgeProposal> proposals = new();
		foreach (LhmDiskGroup group in unmatchedGroups)
		{
			BridgeCandidate[] candidates = bridges
				.Select(bridge => ScoreBridgeCandidate(bridge, group, uniquePair))
				.OrderByDescending(candidate => candidate.Score)
				.ToArray();
			BridgeCandidate? best = candidates.FirstOrDefault(candidate => candidate.IsEligible);
			BridgeCandidate? second = candidates.Where(candidate => candidate.IsEligible).Skip(1).FirstOrDefault();
			if (best is not null && best.Score >= 7d && (second is null || best.Score - second.Score >= 3d))
			{
				proposals.Add(new BridgeProposal(group, best.Device));
			}
		}

		foreach (IGrouping<DiskDevice, BridgeProposal> candidateGroup in proposals.GroupBy(proposal => proposal.Device))
		{
			BridgeProposal[] deviceProposals = candidateGroup.ToArray();
			if (deviceProposals.Length != 1)
			{
				continue;
			}
			BridgeProposal proposal = deviceProposals[0];
			MergeLibreHardwareMonitorSensors(proposal.Device, proposal.Group, preferSensorIdentity: true);
			assignedWmiDevices.Add(proposal.Device);
		}

		int remaining = unmatchedGroups.Count(group => !group.IsMatched);
		if (remaining > 0)
		{
			AppLogger.LogError(
				$"Disk identity bridge pairing remained ambiguous; externalCandidates={bridges.Length}; sensorGroups={unmatchedGroups.Length}; unmatched={remaining}.",
				null,
				$"disk-identity-ambiguous:{bridges.Length}:{unmatchedGroups.Length}:{remaining}",
				TimeSpan.FromMinutes(10));
		}
	}

	private static BridgeCandidate ScoreBridgeCandidate(DiskDevice device, LhmDiskGroup group, bool uniquePair)
	{
		bool indexMatch = group.RootIndex.HasValue
			&& TryGetPhysicalIndex(device.Index, out int deviceIndex)
			&& group.RootIndex.Value == deviceIndex;
		bool pairEvidence = uniquePair && group.HasSmartEvidence && device.Size.HasValue;
		double score = 0d;
		if (indexMatch) score += 6d;
		if (group.HasSmartEvidence) score += 2d;
		if (device.Size.HasValue) score += 1d;
		if (pairEvidence) score += 4d;
		score += ScoreDiskNameMatch(device.Name, group.DeviceName);
		return new BridgeCandidate(device, score, device.IsExternalBridge && group.HasSmartEvidence && (indexMatch || pairEvidence));
	}

	private static bool IsExternalBridgeDevice(
		string? name,
		string? model,
		string? mediaType,
		string? interfaceType,
		string? busType,
		string? pnpDeviceId)
	{
		string text = $"{name} {model} {mediaType} {interfaceType} {busType} {pnpDeviceId}";
		bool externalSignal = text.Contains("external", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("usb", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("uasp", StringComparison.OrdinalIgnoreCase);
		bool bridgeSignal = BridgeHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase))
			|| text.Contains("scsi", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("sata", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("nvme", StringComparison.OrdinalIgnoreCase);
		return externalSignal && bridgeSignal;
	}

	private static bool MatchesIdentity(DiskDevice device, string? identity)
	{
		if (string.IsNullOrWhiteSpace(identity))
		{
			return false;
		}
		return IdentityTextEqual(device.Id, identity)
			|| device.IdentityAliases.Any(alias => IdentityTextEqual(alias, identity));
	}

	private static void AddIdentityAliases(DiskDevice device, params string?[] aliases)
	{
		foreach (string alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)).Select(alias => alias!.Trim()))
		{
			if (!IdentityTextEqual(device.Id, alias)
				&& !device.IdentityAliases.Any(existing => IdentityTextEqual(existing, alias)))
			{
				device.IdentityAliases.Add(alias);
			}
		}
	}

	private static bool IdentityTextEqual(string? left, string? right)
	{
		return !string.IsNullOrWhiteSpace(left)
			&& !string.IsNullOrWhiteSpace(right)
			&& string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	private static bool StableIdentifierPartsEqual(string? left, string? right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return false;
		}
		string[] leftParts = NormalizeForTokens(left).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string[] rightParts = NormalizeForTokens(right).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return leftParts.Where(part => part.Length >= 8)
			.Intersect(rightParts.Where(part => part.Length >= 8), StringComparer.OrdinalIgnoreCase)
			.Any();
	}

	private static bool TryGetPhysicalIndex(string? value, out int index)
	{
		index = -1;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string trimmed = value.Trim();
		if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
		{
			return true;
		}
		for (int position = trimmed.Length - 1; position >= 0; position--)
		{
			if (!char.IsDigit(trimmed[position]))
			{
				string suffix = trimmed[(position + 1)..];
				return suffix.Length > 0 && int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
			}
		}
		return false;
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

	private sealed class LhmDiskGroup
	{
		public string Key { get; init; } = string.Empty;

		public string DeviceName { get; init; } = string.Empty;

		public int? RootIndex { get; init; }

		public bool HasSmartEvidence { get; init; }

		public List<SensorReading> Sensors { get; init; } = new();

		public bool IsMatched { get; set; }
	}

	private sealed record BridgeCandidate(DiskDevice Device, double Score, bool IsEligible);

	private sealed record BridgeProposal(LhmDiskGroup Group, DiskDevice Device);
}
