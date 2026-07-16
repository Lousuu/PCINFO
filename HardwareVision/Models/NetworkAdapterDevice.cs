using System.Collections.Generic;

namespace HardwareVision.Models;

public sealed class NetworkAdapterDevice
{
	public string Id { get; set; } = string.Empty;


	public string Name { get; set; } = string.Empty;


	public string? Description { get; set; }

	public string? InterfaceType { get; set; }

	public string? MacAddress { get; set; }

	public List<string> IPv4Addresses { get; set; } = new List<string>();


	public List<string> IPv6Addresses { get; set; } = new List<string>();


	public string? Gateway { get; set; }

	public List<string> DnsServers { get; set; } = new List<string>();


	public bool? DhcpEnabled { get; set; }

	public ulong? LinkSpeed { get; set; }

	public bool IsWireless { get; set; }

	public bool IsVirtual { get; set; }

	public bool IsUp { get; set; }

	public double? UploadSpeed { get; set; }

	public double? DownloadSpeed { get; set; }

	public ulong? TotalUploaded { get; set; }

	public ulong? TotalDownloaded { get; set; }

	public double? Utilization { get; set; }

	public double? SignalQuality { get; set; }

	public string Source { get; set; } = string.Empty;


	public SensorAvailability Availability { get; set; } = SensorAvailability.Unknown;

}
