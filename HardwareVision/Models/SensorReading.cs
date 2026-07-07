using System;

namespace HardwareVision.Models;

public sealed class SensorReading
{
	public string DeviceName { get; set; } = string.Empty;


	public string SensorName { get; set; } = string.Empty;


	public SensorCategory Category { get; set; } = SensorCategory.Unknown;


	public SensorType Type { get; set; } = SensorType.Unknown;


	public double? Value { get; set; }

	public string Unit { get; set; } = string.Empty;


	public double? Min { get; set; }

	public double? Max { get; set; }

	public HardwareStatus Status { get; set; } = HardwareStatus.Unknown;


	public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;


	public bool IsAvailable { get; set; }

	public string Source { get; set; } = string.Empty;


	public SensorAvailability Availability { get; set; } = SensorAvailability.Unknown;


	public string RawIdentifier { get; set; } = string.Empty;


	public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.Now;


	public string? ErrorMessage { get; set; }
}
