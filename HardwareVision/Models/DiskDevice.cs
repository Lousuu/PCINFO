using System.Collections.Generic;

namespace HardwareVision.Models;

public sealed class DiskDevice
{
	public string Id { get; set; } = string.Empty;


	public string Name { get; set; } = string.Empty;


	public string? Model { get; set; }

	public string? TransportDeviceName { get; set; }

	public string? BridgeControllerName { get; set; }

	public string? Index { get; set; }

	public string? SerialNumber { get; set; }

	public string? UniqueId { get; set; }

	public string? ObjectId { get; set; }

	public string? PnpDeviceId { get; set; }

	public string? InterfaceType { get; set; }

	public string? MediaType { get; set; }

	public string? BusType { get; set; }

	public ulong? Size { get; set; }

	public string? FirmwareRevision { get; set; }

	public List<string> Partitions { get; set; } = new List<string>();


	public List<string> Volumes { get; set; } = new List<string>();


	public SensorReading? Temperature { get; set; }

	public SensorReading? HealthStatus { get; set; }

	public SensorReading? RemainingLife { get; set; }

	public SensorReading? Wear { get; set; }

	public SensorReading? MaximumTemperature { get; set; }

	public double? ReadSpeed { get; set; }

	public double? WriteSpeed { get; set; }

	public SensorReading? ReadTotal { get; set; }

	public SensorReading? WriteTotal { get; set; }

	public SensorReading? PowerOnHours { get; set; }

	public SensorReading? PowerCycleCount { get; set; }

	public SensorReading? ReadErrorsTotal { get; set; }

	public SensorReading? WriteErrorsTotal { get; set; }

	public SensorReading? ReadLatencyMax { get; set; }

	public SensorReading? WriteLatencyMax { get; set; }

	public SensorReading? FlushLatencyMax { get; set; }

	public ulong? UsedSpace { get; set; }

	public ulong? FreeSpace { get; set; }

	public double? UsagePercent { get; set; }

	public string Source { get; set; } = string.Empty;


	public SensorAvailability Availability { get; set; } = SensorAvailability.Unknown;


	public string? SmartStatus { get; set; }

	public string? NvmeHealthStatus { get; set; }

	public string? OperationalStatus { get; set; }

	public bool IsSystemDisk { get; set; }

	public string? PerformanceInstanceName { get; set; }

	public bool IsExternalBridge { get; set; }

	public List<string> IdentityAliases { get; set; } = new List<string>();

	public List<SensorReading> Sensors { get; set; } = new List<SensorReading>();

}
