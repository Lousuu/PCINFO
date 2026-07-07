using System.Collections.Generic;

namespace HardwareVision.Models;

public sealed class GpuDevice
{
	public string Id { get; set; } = string.Empty;


	public string Name { get; set; } = string.Empty;


	public string? Vendor { get; set; }

	public string HardwareType { get; set; } = string.Empty;


	public string? DriverVersion { get; set; }

	public ulong? AdapterRam { get; set; }

	public bool IsIntegrated { get; set; }

	public bool IsDiscrete { get; set; }

	public bool IsPreferred { get; set; }

	public string Source { get; set; } = string.Empty;


	public List<SensorReading> Sensors { get; set; } = new List<SensorReading>();


	public SensorReading? TemperatureCore { get; set; }

	public SensorReading? TemperatureHotSpot { get; set; }

	public SensorReading? TemperatureMemoryJunction { get; set; }

	public SensorReading? CoreClock { get; set; }

	public SensorReading? MemoryClock { get; set; }

	public SensorReading? CoreLoad { get; set; }

	public SensorReading? MemoryLoad { get; set; }

	public SensorReading? MemoryUsed { get; set; }

	public SensorReading? MemoryFree { get; set; }

	public SensorReading? MemoryTotal { get; set; }

	public SensorReading? PowerPackage { get; set; }

	public SensorReading? CoreVoltage { get; set; }

	public SensorReading? FanSpeed { get; set; }

	public SensorReading? PcieRx { get; set; }

	public SensorReading? PcieTx { get; set; }

	public SensorAvailability Availability { get; set; } = SensorAvailability.Unknown;

}
