using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Utilities;
using Microsoft.Win32;

namespace HardwareVision.Services;

public sealed class HardwareInfoService : IHardwareInfoService
{
	private sealed class DiskAssociationInfo
	{
		public List<string> Partitions { get; } = new List<string>();


		public List<string> Volumes { get; } = new List<string>();


		public List<string> VolumeLetters { get; } = new List<string>();


		public ulong? VolumeSize { get; set; }

		public ulong? UsedSpace { get; set; }

		public ulong? FreeSpace { get; set; }

		public bool IsSystemDisk { get; set; }
	}

	public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.Run(() => BuildSnapshot(cancellationToken), cancellationToken);
	}

	public async Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return (await GetHardwareSnapshotAsync(cancellationToken)).Devices;
	}

	public async Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		HardwareSnapshot snapshot = await GetHardwareSnapshotAsync(cancellationToken);
		return new HardwareSummary(snapshot.ComputerName ?? Environment.MachineName, snapshot.OperatingSystem ?? "Unknown", snapshot.CpuName, snapshot.GpuName, snapshot.MotherboardName, FormatBytes(snapshot.MemoryTotal));
	}

	private static HardwareSnapshot BuildSnapshot(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		HardwareSnapshot hardwareSnapshot = new HardwareSnapshot
		{
			Timestamp = DateTimeOffset.Now,
			ComputerName = Environment.MachineName,
			CurrentUserName = GetCurrentUserName()
		};
		ReadComputerSystem(hardwareSnapshot, cancellationToken);
		ReadComputerSystemProduct(hardwareSnapshot, cancellationToken);
		ReadOperatingSystem(hardwareSnapshot, cancellationToken);
		ReadProcessors(hardwareSnapshot, cancellationToken);
		ReadVideoControllers(hardwareSnapshot, cancellationToken);
		ReadBaseBoard(hardwareSnapshot, cancellationToken);
		ReadBios(hardwareSnapshot, cancellationToken);
		ReadSystemEnclosure(hardwareSnapshot, cancellationToken);
		ReadPhysicalMemory(hardwareSnapshot, cancellationToken);
		ReadPhysicalMemoryArrays(hardwareSnapshot, cancellationToken);
		ReadDiskDrives(hardwareSnapshot, cancellationToken);
		ReadPhysicalDisks(hardwareSnapshot, cancellationToken);
		ReadNetworkAdapters(hardwareSnapshot, cancellationToken);
		if (hardwareSnapshot.DiskDrives.Count > 0)
		{
			hardwareSnapshot.DiskSummary = string.Join("; ", hardwareSnapshot.DiskDrives);
		}
		return hardwareSnapshot;
	}

	private static void ReadComputerSystem(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		QueryWmi("Win32_ComputerSystem", delegate(ManagementBaseObject obj)
		{
			string @string = GetString(obj, "Name");
			string string2 = GetString(obj, "Manufacturer");
			string string3 = GetString(obj, "Model");
			string string4 = GetString(obj, "UserName");
			ulong? uInt = GetUInt64(obj, "TotalPhysicalMemory");
			snapshot2.ComputerName = FirstAvailable(@string, snapshot2.ComputerName);
			snapshot2.CurrentUserName = FirstAvailable(string4, snapshot2.CurrentUserName);
			HardwareSnapshot hardwareSnapshot = snapshot2;
			if (!hardwareSnapshot.MemoryTotal.HasValue)
			{
				ulong? num2 = (hardwareSnapshot.MemoryTotal = uInt);
			}
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(@string, "computer-system"),
				Name = FirstAvailable(string3, @string, "Computer System"),
				Vendor = string2,
				Model = string3,
				Category = SensorCategory.Unknown,
				Description = GetString(obj, "Description"),
				Properties = CreateProperties(
					("HardwareSource", "Win32_ComputerSystem"),
					("Name", @string),
					("Manufacturer", string2),
					("Model", string3),
					("Domain", GetString(obj, "Domain")),
					("UserName", string4),
					("TotalPhysicalMemoryBytes", uInt?.ToString(CultureInfo.InvariantCulture)),
					("TotalPhysicalMemory", FormatBytes(uInt)),
					("SystemType", GetString(obj, "SystemType")),
					("SystemSKUNumber", GetString(obj, "SystemSKUNumber")),
					("PCSystemType", GetString(obj, "PCSystemType")),
					("PCSystemTypeEx", GetString(obj, "PCSystemTypeEx")))
			});
		}, cancellationToken);
	}

	private static void ReadComputerSystemProduct(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		QueryWmi("Win32_ComputerSystemProduct", delegate(ManagementBaseObject obj)
		{
			string name = GetString(obj, "Name");
			string vendor = GetString(obj, "Vendor");
			string uuid = GetString(obj, "UUID");
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(uuid, GetString(obj, "IdentifyingNumber"), "computer-system-product"),
				Name = FirstAvailable(name, "Computer System Product"),
				Vendor = vendor,
				Model = name,
				Category = SensorCategory.Unknown,
				Description = JoinNonEmpty(" ", vendor, name, GetString(obj, "Version")),
				Properties = CreateProperties(
					("HardwareSource", "Win32_ComputerSystemProduct"),
					("Name", name),
					("Vendor", vendor),
					("Version", GetString(obj, "Version")),
					("IdentifyingNumber", GetString(obj, "IdentifyingNumber")),
					("UUID", uuid),
					("SKUNumber", GetString(obj, "SKUNumber")))
			});
		}, cancellationToken);
	}

	private static void ReadOperatingSystem(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		QueryWmi("Win32_OperatingSystem", delegate(ManagementBaseObject obj)
		{
			string @string = GetString(obj, "Caption");
			string string2 = GetString(obj, "Version");
			string string3 = GetString(obj, "BuildNumber");
			string string4 = GetString(obj, "OSArchitecture");
			snapshot2.OperatingSystem = JoinNonEmpty(" ", @string, string2, "Build " + string3, string4);
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = "operating-system",
				Name = FirstAvailable(@string, "Windows"),
				Vendor = "Microsoft",
				Model = string2,
				Category = SensorCategory.Unknown,
				Description = snapshot2.OperatingSystem,
				Properties = CreateProperties(("Version", string2), ("BuildNumber", string3), ("Architecture", string4), ("InstallDate", GetWmiDate(obj, "InstallDate")), ("LastBootUpTime", GetWmiDate(obj, "LastBootUpTime")))
			});
		}, cancellationToken);
	}

	private static void ReadProcessors(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		List<string> names = new List<string>();
		int index = 0;
		QueryWmi("Win32_Processor", delegate(ManagementBaseObject obj)
		{
			string text = FirstAvailable(GetString(obj, "Name"), "CPU");
			string id = FirstAvailable(GetString(obj, "ProcessorId"), $"cpu-{index}");
			string @string = GetString(obj, "Manufacturer");
			names.Add(text);
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = id,
				Name = text,
				Vendor = @string,
				Model = text,
				Category = SensorCategory.Cpu,
				Description = GetString(obj, "Description"),
				Properties = CreateProperties(("SocketDesignation", GetString(obj, "SocketDesignation")), ("NumberOfCores", GetString(obj, "NumberOfCores")), ("NumberOfLogicalProcessors", GetString(obj, "NumberOfLogicalProcessors")), ("MaxClockSpeedMHz", GetString(obj, "MaxClockSpeed")), ("CurrentClockSpeedMHz", GetString(obj, "CurrentClockSpeed")), ("Architecture", GetString(obj, "Architecture")))
			});
			index++;
		}, cancellationToken);
		snapshot2.CpuName = JoinDistinct(names);
	}

	private static void ReadVideoControllers(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		List<string> names = new List<string>();
		int index = 0;
		QueryWmi("Win32_VideoController", delegate(ManagementBaseObject obj)
		{
			string text = FirstAvailable(GetString(obj, "Name"), $"GPU {index + 1}");
			ulong? uInt = GetUInt64(obj, "AdapterRAM");
			names.Add(text);
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "PNPDeviceID"), $"gpu-{index}"),
				Name = text,
				Vendor = GetString(obj, "AdapterCompatibility"),
				Model = GetString(obj, "VideoProcessor"),
				Category = SensorCategory.Gpu,
				Description = GetString(obj, "Description"),
				Properties = CreateProperties(("AdapterRAMBytes", uInt?.ToString(CultureInfo.InvariantCulture)), ("AdapterRAM", FormatBytes(uInt)), ("DriverVersion", GetString(obj, "DriverVersion")), ("VideoProcessor", GetString(obj, "VideoProcessor")), ("PNPDeviceID", GetString(obj, "PNPDeviceID")), ("VideoModeDescription", GetString(obj, "VideoModeDescription")), ("CurrentRefreshRate", GetString(obj, "CurrentRefreshRate")))
			});
			index++;
		}, cancellationToken);
		snapshot2.GpuName = JoinDistinct(names);
	}

	private static void ReadBaseBoard(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		QueryWmi("Win32_BaseBoard", delegate(ManagementBaseObject obj)
		{
			string @string = GetString(obj, "Manufacturer");
			string string2 = GetString(obj, "Product");
			string string3 = GetString(obj, "Version");
			string string4 = GetString(obj, "SerialNumber");
			snapshot2.MotherboardName = JoinNonEmpty(" ", @string, string2, string3);
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(string4, "motherboard"),
				Name = FirstAvailable(snapshot2.MotherboardName, "Motherboard"),
				Vendor = @string,
				Model = string2,
				Category = SensorCategory.Motherboard,
				Description = GetString(obj, "Description"),
				Properties = CreateProperties(
					("HardwareSource", "Win32_BaseBoard"),
					("Manufacturer", @string),
					("Product", string2),
					("Version", string3),
					("SerialNumber", string4),
					("HostingBoard", GetString(obj, "HostingBoard")),
					("Status", GetString(obj, "Status")),
					("Tag", GetString(obj, "Tag")))
			});
		}, cancellationToken);
	}

	private static void ReadBios(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		QueryWmi("Win32_BIOS", delegate(ManagementBaseObject obj)
		{
			string @string = GetString(obj, "Manufacturer");
			string string2 = GetString(obj, "Name");
			string version = GetString(obj, "Version");
			string text = FirstAvailable(GetString(obj, "SMBIOSBIOSVersion"), version);
			string? wmiDate = GetWmiDate(obj, "ReleaseDate");
			string smbiosMajor = GetString(obj, "SMBIOSMajorVersion");
			string smbiosMinor = GetString(obj, "SMBIOSMinorVersion");
			snapshot2.BiosInfo = JoinNonEmpty(" ", @string, text, wmiDate);
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "SerialNumber"), "bios"),
				Name = FirstAvailable(string2, "BIOS"),
				Vendor = @string,
				Model = text,
				Category = SensorCategory.Motherboard,
				Description = snapshot2.BiosInfo,
				Properties = CreateProperties(
					("HardwareSource", "Win32_BIOS"),
					("Manufacturer", @string),
					("Name", string2),
					("Version", version),
					("SMBIOSBIOSVersion", GetString(obj, "SMBIOSBIOSVersion")),
					("ReleaseDate", wmiDate),
					("SerialNumber", GetString(obj, "SerialNumber")),
					("SMBIOSMajorVersion", smbiosMajor),
					("SMBIOSMinorVersion", smbiosMinor),
					("SMBIOSVersion", JoinNonEmpty(".", smbiosMajor, smbiosMinor)),
					("BiosMode", GetFirmwareMode()),
					("SecureBoot", GetSecureBootState()))
			});
		}, cancellationToken);
	}

	private static void ReadSystemEnclosure(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		int index = 0;
		QueryWmi("Win32_SystemEnclosure", delegate(ManagementBaseObject obj)
		{
			string[] chassisTypes = GetStringArray(obj, "ChassisTypes");
			string chassisTypeNames = JoinNonEmpty(", ", chassisTypes.Select(ResolveChassisTypeName).ToArray());
			string name = FirstAvailable(GetString(obj, "Name"), GetString(obj, "Caption"), $"System Enclosure {index + 1}");
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "SerialNumber"), GetString(obj, "Tag"), $"system-enclosure-{index}"),
				Name = name,
				Vendor = GetString(obj, "Manufacturer"),
				Model = GetString(obj, "Model"),
				Category = SensorCategory.Unknown,
				Description = JoinNonEmpty(" ", name, chassisTypeNames),
				Properties = CreateProperties(
					("HardwareSource", "Win32_SystemEnclosure"),
					("Name", name),
					("Manufacturer", GetString(obj, "Manufacturer")),
					("Model", GetString(obj, "Model")),
					("Version", GetString(obj, "Version")),
					("SerialNumber", GetString(obj, "SerialNumber")),
					("SMBIOSAssetTag", GetString(obj, "SMBIOSAssetTag")),
					("ChassisTypes", string.Join(", ", chassisTypes)),
					("ChassisTypeNames", chassisTypeNames),
					("LockPresent", GetString(obj, "LockPresent")),
					("SecurityStatus", GetString(obj, "SecurityStatus")))
			});
			index++;
		}, cancellationToken);
	}

	private static void ReadPhysicalMemory(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		ulong totalCapacity = 0uL;
		int index = 0;
		QueryWmi("Win32_PhysicalMemory", delegate(ManagementBaseObject obj)
		{
			ulong? uInt = GetUInt64(obj, "Capacity");
			if (uInt.HasValue)
			{
				totalCapacity += uInt.Value;
			}
			string @string = GetString(obj, "Manufacturer");
			string string2 = GetString(obj, "PartNumber");
			string string3 = GetString(obj, "Speed");
			string string4 = GetString(obj, "BankLabel");
			string string5 = GetString(obj, "DeviceLocator");
			string name = FirstAvailable(string2, string5, string4, $"Memory Module {index + 1}");
			string text = JoinNonEmpty(" ", string5, FormatBytes(uInt), (string3 == null) ? null : (string3 + " MHz"), string2);
			if (!string.IsNullOrWhiteSpace(text))
			{
				snapshot2.MemoryModules.Add(text);
			}
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "SerialNumber"), (string5 == null) ? null : $"{string5}-{index}", $"memory-{index}"),
				Name = name,
				Vendor = @string,
				Model = string2,
				Category = SensorCategory.Memory,
				Description = text,
				Properties = CreateProperties(("CapacityBytes", uInt?.ToString(CultureInfo.InvariantCulture)), ("Capacity", FormatBytes(uInt)), ("SpeedMHz", string3), ("ConfiguredClockSpeedMHz", GetString(obj, "ConfiguredClockSpeed")), ("BankLabel", string4), ("DeviceLocator", string5), ("Manufacturer", @string), ("PartNumber", string2), ("SerialNumber", GetString(obj, "SerialNumber")), ("FormFactor", GetString(obj, "FormFactor")), ("MemoryType", GetString(obj, "MemoryType")), ("SMBIOSMemoryType", GetString(obj, "SMBIOSMemoryType")), ("DataWidth", GetString(obj, "DataWidth")), ("TotalWidth", GetString(obj, "TotalWidth")))
			});
			index++;
		}, cancellationToken);
		if (totalCapacity != 0)
		{
			snapshot2.MemoryTotal = totalCapacity;
		}
	}

	private static void ReadPhysicalMemoryArrays(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		int index = 0;
		QueryWmi("Win32_PhysicalMemoryArray", delegate(ManagementBaseObject obj)
		{
			string name = FirstAvailable(GetString(obj, "Tag"), $"Memory Array {index + 1}");
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "Tag"), $"memory-array-{index}"),
				Name = name,
				Category = SensorCategory.Memory,
				Description = GetString(obj, "Description"),
				Properties = CreateProperties(("MemoryDevices", GetString(obj, "MemoryDevices")), ("MaxCapacity", GetString(obj, "MaxCapacity")), ("MaxCapacityEx", GetString(obj, "MaxCapacityEx")), ("Use", GetString(obj, "Use")), ("Location", GetString(obj, "Location")))
			});
			index++;
		}, cancellationToken);
	}

	private static void ReadDiskDrives(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		int index = 0;
		QueryWmi("Win32_DiskDrive", delegate(ManagementBaseObject obj)
		{
			string text = FirstAvailable(GetString(obj, "Model"), GetString(obj, "Caption"), $"Disk {index + 1}");
			ulong? uInt = GetUInt64(obj, "Size");
			string text2 = JoinNonEmpty(" ", text, FormatBytes(uInt), GetString(obj, "InterfaceType"));
			DiskAssociationInfo diskAssociationInfo = ReadDiskAssociationInfo(obj, cancellationToken);
			if (!string.IsNullOrWhiteSpace(text2))
			{
				snapshot2.DiskDrives.Add(text2);
			}
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "SerialNumber"), GetString(obj, "DeviceID"), $"disk-{index}"),
				Name = text,
				Vendor = GetString(obj, "Manufacturer"),
				Model = GetString(obj, "Model"),
				Category = SensorCategory.Disk,
				Description = text2,
				Properties = CreateProperties(("StorageSource", "Win32_DiskDrive"), ("DeviceID", GetString(obj, "DeviceID")), ("PNPDeviceID", GetString(obj, "PNPDeviceID")), ("Index", GetString(obj, "Index")), ("SizeBytes", uInt?.ToString(CultureInfo.InvariantCulture)), ("Size", FormatBytes(uInt)), ("InterfaceType", GetString(obj, "InterfaceType")), ("MediaType", GetString(obj, "MediaType")), ("FirmwareRevision", GetString(obj, "FirmwareRevision")), ("SerialNumber", GetString(obj, "SerialNumber")), ("Partitions", string.Join("; ", diskAssociationInfo.Partitions)), ("PartitionCount", GetString(obj, "Partitions")), ("Volumes", string.Join("; ", diskAssociationInfo.Volumes)), ("VolumeLetters", string.Join(", ", diskAssociationInfo.VolumeLetters)), ("VolumeSizeBytes", diskAssociationInfo.VolumeSize?.ToString(CultureInfo.InvariantCulture)), ("UsedSpaceBytes", diskAssociationInfo.UsedSpace?.ToString(CultureInfo.InvariantCulture)), ("FreeSpaceBytes", diskAssociationInfo.FreeSpace?.ToString(CultureInfo.InvariantCulture)), ("IsSystemDisk", diskAssociationInfo.IsSystemDisk ? "true" : null), ("Status", GetString(obj, "Status")), ("StatusInfo", GetString(obj, "StatusInfo")))
			});
			index++;
		}, cancellationToken);
	}

	private static void ReadPhysicalDisks(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		int index = 0;
		QueryWmi("root\\Microsoft\\Windows\\Storage", "MSFT_PhysicalDisk", delegate(ManagementBaseObject obj)
		{
			string text = FirstAvailable(GetString(obj, "FriendlyName"), GetString(obj, "Model"), $"Physical Disk {index + 1}");
			ulong? uInt = GetUInt64(obj, "Size");
			string @string = GetString(obj, "SerialNumber");
			snapshot2.Devices.Add(new HardwareDevice
			{
				Id = FirstAvailable(GetString(obj, "UniqueId"), @string, GetString(obj, "DeviceId"), $"msft-disk-{index}"),
				Name = text,
				Vendor = GetString(obj, "Manufacturer"),
				Model = FirstAvailable(GetString(obj, "Model"), text),
				Category = SensorCategory.Disk,
				Description = JoinNonEmpty(" ", text, FormatBytes(uInt), GetString(obj, "BusType")),
				Properties = CreateProperties(("StorageSource", "MSFT_PhysicalDisk"), ("DeviceId", GetString(obj, "DeviceId")), ("FriendlyName", GetString(obj, "FriendlyName")), ("SerialNumber", @string), ("SizeBytes", uInt?.ToString(CultureInfo.InvariantCulture)), ("Size", FormatBytes(uInt)), ("MediaType", GetString(obj, "MediaType")), ("BusType", GetString(obj, "BusType")), ("FirmwareVersion", GetString(obj, "FirmwareVersion")), ("HealthStatus", GetString(obj, "HealthStatus")), ("OperationalStatus", GetString(obj, "OperationalStatus")), ("Usage", GetString(obj, "Usage")))
			});
			index++;
		}, cancellationToken);
	}

	private static DiskAssociationInfo ReadDiskAssociationInfo(ManagementBaseObject diskObject, CancellationToken cancellationToken)
	{
		if (!(diskObject is ManagementObject managementObject))
		{
			return new DiskAssociationInfo();
		}
		DiskAssociationInfo diskAssociationInfo = new DiskAssociationInfo();
		try
		{
			string relativePath = managementObject.Path.RelativePath;
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\CIMV2", "ASSOCIATORS OF {" + relativePath + "} WHERE AssocClass = Win32_DiskDriveToDiskPartition");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementBaseObject item in managementObjectCollection)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string @string = GetString(item, "DeviceID");
				string text = FormatBytes(GetUInt64(item, "Size"));
				string text2 = JoinNonEmpty(" ", @string, GetString(item, "Type"), text);
				if (!string.IsNullOrWhiteSpace(text2))
				{
					diskAssociationInfo.Partitions.Add(text2);
				}
				if (!(item is ManagementObject managementObject2))
				{
					continue;
				}
				string relativePath2 = managementObject2.Path.RelativePath;
				using ManagementObjectSearcher managementObjectSearcher2 = new ManagementObjectSearcher("root\\CIMV2", "ASSOCIATORS OF {" + relativePath2 + "} WHERE AssocClass = Win32_LogicalDiskToPartition");
				using ManagementObjectCollection managementObjectCollection2 = managementObjectSearcher2.Get();
				foreach (ManagementBaseObject item2 in managementObjectCollection2)
				{
					cancellationToken.ThrowIfCancellationRequested();
					string string2 = GetString(item2, "DeviceID");
					ulong? uInt = GetUInt64(item2, "Size");
					ulong? uInt2 = GetUInt64(item2, "FreeSpace");
					ulong? num = ((uInt.HasValue && uInt2.HasValue && uInt.Value >= uInt2.Value) ? new ulong?(uInt.Value - uInt2.Value) : null);
					string text3 = JoinNonEmpty(" ", string2, GetString(item2, "FileSystem"), GetString(item2, "VolumeName"), FormatBytes(uInt));
					if (!string.IsNullOrWhiteSpace(text3))
					{
						diskAssociationInfo.Volumes.Add(text3);
					}
					if (!string.IsNullOrWhiteSpace(string2))
					{
						diskAssociationInfo.VolumeLetters.Add(string2);
						string? environmentVariable = Environment.GetEnvironmentVariable("SystemDrive");
						if (string.Equals(string2, environmentVariable, StringComparison.OrdinalIgnoreCase))
						{
							diskAssociationInfo.IsSystemDisk = true;
						}
					}
					if (uInt.HasValue)
					{
						diskAssociationInfo.VolumeSize = diskAssociationInfo.VolumeSize.GetValueOrDefault() + uInt.Value;
					}
					if (uInt2.HasValue)
					{
						diskAssociationInfo.FreeSpace = diskAssociationInfo.FreeSpace.GetValueOrDefault() + uInt2.Value;
					}
					if (num.HasValue)
					{
						diskAssociationInfo.UsedSpace = diskAssociationInfo.UsedSpace.GetValueOrDefault() + num.Value;
					}
				}
			}
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError("Disk partition/logical disk association query failed.", ex, "disk-association:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
		}
		return diskAssociationInfo;
	}

	private static void ReadNetworkAdapters(HardwareSnapshot snapshot, CancellationToken cancellationToken)
	{
		HardwareSnapshot snapshot2 = snapshot;
		int index = 0;
		QueryWmi("Win32_NetworkAdapter", delegate(ManagementBaseObject obj)
		{
			bool? boolean = GetBoolean(obj, "PhysicalAdapter");
			string @string = GetString(obj, "Name");
			if (boolean != false && !string.IsNullOrWhiteSpace(@string))
			{
				string string2 = GetString(obj, "MACAddress");
				string string3 = GetString(obj, "AdapterType");
				string string4 = GetString(obj, "NetConnectionID");
				string text = JoinNonEmpty(" ", string4, @string, string3);
				if (!string.IsNullOrWhiteSpace(text))
				{
					snapshot2.NetworkAdapters.Add(text);
				}
				snapshot2.Devices.Add(new HardwareDevice
				{
					Id = FirstAvailable(GetString(obj, "GUID"), string2, $"network-{index}"),
					Name = @string,
					Vendor = GetString(obj, "Manufacturer"),
					Model = GetString(obj, "ProductName"),
					Category = SensorCategory.Network,
					Description = GetString(obj, "Description"),
					Properties = CreateProperties(("MACAddress", string2), ("AdapterType", string3), ("NetConnectionID", string4), ("NetEnabled", GetString(obj, "NetEnabled")), ("Speed", GetString(obj, "Speed")), ("PhysicalAdapter", GetString(obj, "PhysicalAdapter")))
				});
				index++;
			}
		}, cancellationToken);
	}

	private static void QueryWmi(string wmiClass, Action<ManagementBaseObject> processObject, CancellationToken cancellationToken)
	{
		QueryWmi("root\\CIMV2", wmiClass, processObject, cancellationToken);
	}

	private static void QueryWmi(string scopePath, string wmiClass, Action<ManagementBaseObject> processObject, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(scopePath, "SELECT * FROM " + wmiClass);
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementBaseObject item in managementObjectCollection)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					processObject(item);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					AppLogger.LogError($"WMI object processing failed for {scopePath}:{wmiClass}.", ex, $"wmi-object:{scopePath}:{wmiClass}:{ex.GetType().FullName}");
				}
			}
		}
		catch (Exception ex2) when (!(ex2 is OperationCanceledException))
		{
			AppLogger.LogError($"WMI query failed for {scopePath}:{wmiClass}.", ex2, $"wmi-query:{scopePath}:{wmiClass}:{ex2.GetType().FullName}");
		}
	}

	private static string GetString(ManagementBaseObject obj, string propertyName)
	{
		object? value = GetValue(obj, propertyName);
		if (value == null)
		{
			return string.Empty;
		}
		if (value is string[] values)
		{
			return JoinNonEmpty(", ", values);
		}
		string? text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
		return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
	}

	private static string[] GetStringArray(ManagementBaseObject obj, string propertyName)
	{
		object? value = GetValue(obj, propertyName);
		if (value is null)
		{
			return Array.Empty<string>();
		}

		if (value is string[] stringValues)
		{
			return stringValues.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).ToArray();
		}

		if (value is ushort[] ushortValues)
		{
			return ushortValues.Select(item => item.ToString(CultureInfo.InvariantCulture)).ToArray();
		}

		if (value is int[] intValues)
		{
			return intValues.Select(item => item.ToString(CultureInfo.InvariantCulture)).ToArray();
		}

		string? text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
		return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text };
	}

	private static ulong? GetUInt64(ManagementBaseObject obj, string propertyName)
	{
		object? value = GetValue(obj, propertyName);
		if (value == null)
		{
			return null;
		}
		try
		{
			return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
		}
		catch (Exception ex) when (((ex is FormatException || ex is InvalidCastException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static bool? GetBoolean(ManagementBaseObject obj, string propertyName)
	{
		object? value = GetValue(obj, propertyName);
		if (value == null)
		{
			return null;
		}
		try
		{
			return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
		}
		catch (Exception ex) when (((ex is FormatException || ex is InvalidCastException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static object? GetValue(ManagementBaseObject obj, string propertyName)
	{
		try
		{
			return obj.Properties[propertyName]?.Value;
		}
		catch (Exception ex) when (((ex is ManagementException || ex is ArgumentException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static string? GetWmiDate(ManagementBaseObject obj, string propertyName)
	{
		string @string = GetString(obj, propertyName);
		if (string.IsNullOrWhiteSpace(@string))
		{
			return null;
		}
		try
		{
			return ManagementDateTimeConverter.ToDateTime(@string).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		}
		catch (Exception ex) when (((ex is ArgumentOutOfRangeException || ex is FormatException) ? 1 : 0) != 0)
		{
			return @string;
		}
	}

	private static Dictionary<string, string?> CreateProperties(params (string Key, string? Value)[] properties)
	{
		Dictionary<string, string?> dictionary = new Dictionary<string, string?>();
		for (int i = 0; i < properties.Length; i++)
		{
			var (key, value) = properties[i];
			if (!string.IsNullOrWhiteSpace(value))
			{
				dictionary[key] = value;
			}
		}
		return dictionary;
	}

	private static string? GetFirmwareMode()
	{
		try
		{
			return GetFirmwareType(out FirmwareType firmwareType)
				? firmwareType switch
				{
					FirmwareType.Bios => "Legacy BIOS",
					FirmwareType.Uefi => "UEFI",
					FirmwareType.Max => "Unknown",
					_ => "Unknown"
				}
				: null;
		}
		catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static string? GetSecureBootState()
	{
		try
		{
			using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
			object? value = key?.GetValue("UEFISecureBootEnabled");
			if (value is null)
			{
				return null;
			}

			int enabled = Convert.ToInt32(value, CultureInfo.InvariantCulture);
			return enabled == 1 ? "Enabled" : "Disabled";
		}
		catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or InvalidCastException or FormatException or OverflowException)
		{
			return null;
		}
	}

	private static string? ResolveChassisTypeName(string? value)
	{
		return value switch
		{
			"1" => "Other",
			"2" => "Unknown",
			"3" => "Desktop",
			"4" => "Low Profile Desktop",
			"5" => "Pizza Box",
			"6" => "Mini Tower",
			"7" => "Tower",
			"8" => "Portable",
			"9" => "Laptop",
			"10" => "Notebook",
			"11" => "Hand Held",
			"12" => "Docking Station",
			"13" => "All-in-One",
			"14" => "Sub Notebook",
			"15" => "Space-Saving",
			"16" => "Lunch Box",
			"17" => "Main System Chassis",
			"18" => "Expansion Chassis",
			"19" => "SubChassis",
			"20" => "Bus Expansion Chassis",
			"21" => "Peripheral Chassis",
			"22" => "Storage Chassis",
			"23" => "Rack Mount Chassis",
			"24" => "Sealed-Case PC",
			"30" => "Tablet",
			"31" => "Convertible",
			"32" => "Detachable",
			_ => value
		};
	}

	private static string? JoinDistinct(IEnumerable<string?> values)
	{
		string[] array = (from value in values
			where !string.IsNullOrWhiteSpace(value)
			select value.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		return (array.Length == 0) ? null : string.Join(", ", array);
	}

	private static string JoinNonEmpty(string separator, params string?[] values)
	{
		string[] array = (from value in values
			where !string.IsNullOrWhiteSpace(value)
			select value.Trim()).ToArray();
		return (array.Length == 0) ? string.Empty : string.Join(separator, array);
	}

	private static string FirstAvailable(params string?[] values)
	{
		return values.FirstOrDefault((string? value) => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
	}

	private static string FormatBytes(ulong? bytes)
	{
		if (!bytes.HasValue)
		{
			return string.Empty;
		}
		string[] array = new string[6] { "B", "KB", "MB", "GB", "TB", "PB" };
		double num = bytes.Value;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		return $"{num:0.##} {array[num2]}";
	}

	private static string GetCurrentUserName()
	{
		string userName = Environment.UserName;
		string userDomainName = Environment.UserDomainName;
		return string.IsNullOrWhiteSpace(userDomainName) ? userName : (userDomainName + "\\" + userName);
	}

	private enum FirmwareType
	{
		Unknown = 0,
		Bios = 1,
		Uefi = 2,
		Max = 3
	}

	[DllImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFirmwareType(out FirmwareType firmwareType);
}
