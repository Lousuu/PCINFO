namespace HardwareVision.Services;

public sealed class DiskPerformanceSnapshot
{
	public string InstanceName { get; init; } = string.Empty;


	public double? ReadBytesPerSecond { get; init; }

	public double? WriteBytesPerSecond { get; init; }
}
