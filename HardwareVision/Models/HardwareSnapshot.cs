using System;
using System.Collections.Generic;

namespace HardwareVision.Models;

public sealed class HardwareSnapshot
{
	public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;


	public string? ComputerName { get; set; }

	public string? CurrentUserName { get; set; }

	public string? CpuName { get; set; }

	public string? GpuName { get; set; }

	public string? MotherboardName { get; set; }

	public string? BiosInfo { get; set; }

	public ulong? MemoryTotal { get; set; }

	public List<string> MemoryModules { get; set; } = new List<string>();


	public string? DiskSummary { get; set; }

	public List<string> DiskDrives { get; set; } = new List<string>();


	public List<string> NetworkAdapters { get; set; } = new List<string>();


	public string? OperatingSystem { get; set; }

	public List<HardwareDevice> Devices { get; set; } = new List<HardwareDevice>();


	public List<SensorReading> Sensors { get; set; } = new List<SensorReading>();

}
