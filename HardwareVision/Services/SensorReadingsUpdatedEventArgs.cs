using System;
using System.Collections.Generic;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class SensorReadingsUpdatedEventArgs : EventArgs
{
	public IReadOnlyList<SensorReading> Readings { get; }

	public DateTimeOffset Timestamp { get; }

	public bool IsBackgroundMode { get; }

	public SensorReadingsUpdatedEventArgs(IReadOnlyList<SensorReading> readings, DateTimeOffset timestamp, bool isBackgroundMode)
	{
		Readings = readings;
		Timestamp = timestamp;
		IsBackgroundMode = isBackgroundMode;
	}
}
