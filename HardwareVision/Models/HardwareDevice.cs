using System.Collections.Generic;

namespace HardwareVision.Models;

public sealed class HardwareDevice
{
	public string Id { get; set; } = string.Empty;


	public string Name { get; set; } = string.Empty;


	public string? Vendor { get; set; }

	public string? Model { get; set; }

	public SensorCategory Category { get; set; } = SensorCategory.Unknown;


	public string? Description { get; set; }

	public Dictionary<string, string?> Properties { get; set; } = new Dictionary<string, string>();

}
