using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class NetworkAdapterService : IDisposable
{
	private sealed class CachedNetworkAdapter
	{
		public string Id { get; }

		public NetworkAdapterDevice Device { get; }

		public string? SystemNetId { get; set; }

		public string? WmiGuid { get; set; }

		public string? ConfigurationId { get; set; }

		public int? InterfaceIndex { get; set; }

		public bool? PhysicalAdapter { get; set; }

		public CachedNetworkAdapter(string id)
		{
			Id = id;
			Device = new NetworkAdapterDevice
			{
				Id = id
			};
		}
	}

	private sealed class WmiAdapterInfo
	{
		public string? Guid { get; init; }

		public string? Name { get; init; }

		public string? Description { get; init; }

		public string? ProductName { get; init; }

		public string? ServiceName { get; init; }

		public string? MacAddress { get; init; }

		public string? AdapterType { get; init; }

		public string? NetConnectionId { get; init; }

		public int? InterfaceIndex { get; init; }

		public ulong? Speed { get; init; }

		public bool? PhysicalAdapter { get; init; }

		public bool? NetEnabled { get; init; }

		public int? NetConnectionStatus { get; init; }
	}

	private sealed class WmiConfigurationInfo
	{
		public string? SettingId { get; init; }

		public string? Description { get; init; }

		public string? MacAddress { get; init; }

		public int? InterfaceIndex { get; init; }

		public List<string> IPAddresses { get; init; } = new List<string>();


		public List<string> Gateways { get; init; } = new List<string>();


		public List<string> DnsServers { get; init; } = new List<string>();


		public bool? DhcpEnabled { get; init; }
	}

	private sealed record ThroughputSample(ulong UploadedBytes, ulong DownloadedBytes, DateTimeOffset Timestamp);

	private sealed class NetworkPerformanceSnapshot
	{
		public double? BytesReceivedPerSecond { get; init; }

		public double? BytesSentPerSecond { get; init; }
	}

	private sealed class NetworkCounterSet : IDisposable
	{
		private readonly PerformanceCounter bytesReceivedPerSecond;

		private readonly PerformanceCounter bytesSentPerSecond;

		public NetworkCounterSet(string instanceName)
		{
			bytesReceivedPerSecond = new PerformanceCounter("Network Interface", "Bytes Received/sec", instanceName, readOnly: true);
			bytesSentPerSecond = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instanceName, readOnly: true);
		}

		public NetworkPerformanceSnapshot Read()
		{
			return new NetworkPerformanceSnapshot
			{
				BytesReceivedPerSecond = NormalizeCounterValue(bytesReceivedPerSecond.NextValue()),
				BytesSentPerSecond = NormalizeCounterValue(bytesSentPerSecond.NextValue())
			};
		}

		public void Dispose()
		{
			bytesReceivedPerSecond.Dispose();
			bytesSentPerSecond.Dispose();
		}

		private static double? NormalizeCounterValue(float value)
		{
			if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
			{
				return null;
			}
			return value;
		}
	}

	private const string SystemNetSource = "System.Net.NetworkInformation";

	private const string WmiAdapterSource = "Win32_NetworkAdapter";

	private const string WmiConfigurationSource = "Win32_NetworkAdapterConfiguration";

	private const string LibreHardwareMonitorSource = "LibreHardwareMonitor";

	private const string PerformanceCounterSource = "PerformanceCounter";

	private const string NetworkInterfaceCategoryName = "Network Interface";

	private const string BytesReceivedCounterName = "Bytes Received/sec";

	private const string BytesSentCounterName = "Bytes Sent/sec";

	private static readonly string[] VirtualAdapterKeywords = new string[9] { "Virtual", "VMware", "Hyper-V", "Bluetooth", "Loopback", "TAP", "Wintun", "VPN", "WAN Miniport" };

	private static readonly string[] WirelessKeywords = new string[6] { "Wi-Fi", "WiFi", "Wireless", "802.11", "WLAN", "无线" };

	private readonly object syncRoot = new object();

	private readonly Dictionary<string, ThroughputSample> previousSamples = new Dictionary<string, ThroughputSample>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, NetworkCounterSet> performanceCounters = new Dictionary<string, NetworkCounterSet>(StringComparer.OrdinalIgnoreCase);

	private List<CachedNetworkAdapter> cachedAdapters = new List<CachedNetworkAdapter>();

	private string[]? performanceCounterInstances;

	private DateTimeOffset performanceCounterInstancesReadAt = DateTimeOffset.MinValue;

	private bool? performanceCountersAvailable;

	private bool isDisposed;

	public Task<IReadOnlyList<NetworkAdapterDevice>> RefreshStaticDevicesAsync(IReadOnlyList<SensorReading> sensorReadings, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<SensorReading> sensorReadings2 = sensorReadings;
		return Task.Run(delegate
		{
			lock (syncRoot)
			{
				ThrowIfDisposed();
				cachedAdapters = BuildStaticAdapters(cancellationToken);
				return BuildCurrentDevices(sensorReadings2, cancellationToken);
			}
		}, cancellationToken);
	}

	public Task<IReadOnlyList<NetworkAdapterDevice>> RefreshRealtimeDevicesAsync(IReadOnlyList<SensorReading> sensorReadings, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<SensorReading> sensorReadings2 = sensorReadings;
		return Task.Run(delegate
		{
			lock (syncRoot)
			{
				ThrowIfDisposed();
				if (cachedAdapters.Count == 0)
				{
					cachedAdapters = BuildStaticAdapters(cancellationToken);
				}
				return BuildCurrentDevices(sensorReadings2, cancellationToken);
			}
		}, cancellationToken);
	}

	public void Dispose()
	{
		lock (syncRoot)
		{
			if (isDisposed)
			{
				return;
			}
			foreach (NetworkCounterSet value in performanceCounters.Values)
			{
				value.Dispose();
			}
			performanceCounters.Clear();
			previousSamples.Clear();
			cachedAdapters.Clear();
			isDisposed = true;
		}
	}

	private IReadOnlyList<NetworkAdapterDevice> BuildCurrentDevices(IReadOnlyList<SensorReading> sensorReadings, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		List<NetworkAdapterDevice> list = cachedAdapters.Select((CachedNetworkAdapter adapter) => CloneDevice(adapter.Device)).ToList();
		EnsureLibreHardwareMonitorDevices(list, sensorReadings);
		MergeLibreHardwareMonitorReadings(list, sensorReadings);
		MergeSystemNetworkStatistics(list, cancellationToken);
		MergePerformanceCounterReadings(list, cancellationToken);
		foreach (NetworkAdapterDevice item in list)
		{
			if (!item.Utilization.HasValue)
			{
				item.Utilization = CalculateUtilization(item);
			}
			item.Availability = ResolveAvailability(item);
		}
		return list.OrderByDescending(GetActiveScore).ThenBy((NetworkAdapterDevice device) => device.IsVirtual).ThenByDescending((NetworkAdapterDevice device) => device.IsUp)
			.ThenBy<NetworkAdapterDevice, string>((NetworkAdapterDevice device) => device.Name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private List<CachedNetworkAdapter> BuildStaticAdapters(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		List<CachedNetworkAdapter> list = new List<CachedNetworkAdapter>();
		NetworkInterface[] networkInterfaces = GetNetworkInterfaces();
		foreach (NetworkInterface networkInterface in networkInterfaces)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AddOrMergeNetworkInterface(list, networkInterface);
		}
		foreach (WmiAdapterInfo item in ReadWmiAdapters(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			AddOrMergeWmiAdapter(list, item);
		}
		foreach (WmiConfigurationInfo item2 in ReadWmiConfigurations(cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			AddOrMergeWmiConfiguration(list, item2);
		}
		Dictionary<string, double> signalQualityByName = ReadWirelessSignalQuality(cancellationToken);
		foreach (CachedNetworkAdapter item3 in list)
		{
			NormalizeStaticAdapter(item3);
			ApplyWirelessSignalQuality(item3, signalQualityByName);
		}
		return (from @group in list.Where(HasMeaningfulAdapterData).GroupBy<CachedNetworkAdapter, string>((CachedNetworkAdapter adapter) => adapter.Device.Id, StringComparer.OrdinalIgnoreCase)
			select @group.First() into adapter
			orderby GetActiveScore(adapter.Device) descending, adapter.Device.IsVirtual, adapter.Device.IsUp descending
			select adapter).ThenBy<CachedNetworkAdapter, string>((CachedNetworkAdapter adapter) => adapter.Device.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void AddOrMergeNetworkInterface(List<CachedNetworkAdapter> adapters, NetworkInterface networkInterface)
	{
		string? text = FormatPhysicalAddress(networkInterface.GetPhysicalAddress());
		IPInterfaceProperties? properties = TryGetIPProperties(networkInterface);
		IPv4InterfaceProperties? pv4InterfaceProperties = TryGetIPv4Properties(properties);
		string text2 = FirstAvailable(networkInterface.Id, text, networkInterface.Name, networkInterface.Description, Guid.NewGuid().ToString("N"));
		CachedNetworkAdapter cachedNetworkAdapter = FindMatchingAdapter(adapters, networkInterface.Id, text, pv4InterfaceProperties?.Index, networkInterface.Name, networkInterface.Description) ?? new CachedNetworkAdapter(text2);
		if (!adapters.Contains(cachedNetworkAdapter))
		{
			adapters.Add(cachedNetworkAdapter);
		}
		cachedNetworkAdapter.SystemNetId = FirstAvailable(cachedNetworkAdapter.SystemNetId, networkInterface.Id);
		CachedNetworkAdapter cachedNetworkAdapter2 = cachedNetworkAdapter;
		if (!cachedNetworkAdapter2.InterfaceIndex.HasValue)
		{
			int? num2 = (cachedNetworkAdapter2.InterfaceIndex = pv4InterfaceProperties?.Index);
		}
		cachedNetworkAdapter.Device.Id = FirstAvailable(cachedNetworkAdapter.Device.Id, networkInterface.Id, text2);
		cachedNetworkAdapter.Device.Name = FirstAvailable(networkInterface.Name, cachedNetworkAdapter.Device.Name, networkInterface.Description, text2);
		cachedNetworkAdapter.Device.Description = FirstAvailable(cachedNetworkAdapter.Device.Description, networkInterface.Description);
		cachedNetworkAdapter.Device.InterfaceType = FirstAvailable(cachedNetworkAdapter.Device.InterfaceType, networkInterface.NetworkInterfaceType.ToString());
		cachedNetworkAdapter.Device.MacAddress = FirstAvailable(cachedNetworkAdapter.Device.MacAddress, text);
		cachedNetworkAdapter.Device.IsUp = cachedNetworkAdapter.Device.IsUp || networkInterface.OperationalStatus == OperationalStatus.Up;
		cachedNetworkAdapter.Device.IsWireless = cachedNetworkAdapter.Device.IsWireless || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
		NetworkAdapterDevice device = cachedNetworkAdapter.Device;
		if (!device.LinkSpeed.HasValue)
		{
			ulong? num4 = (device.LinkSpeed = ((networkInterface.Speed > 0) ? new ulong?((ulong)networkInterface.Speed) : null));
		}
		device = cachedNetworkAdapter.Device;
		if (!device.DhcpEnabled.HasValue)
		{
			bool? flag2 = (device.DhcpEnabled = pv4InterfaceProperties?.IsDhcpEnabled);
		}
		cachedNetworkAdapter.Device.IPv4Addresses = MergeLists(cachedNetworkAdapter.Device.IPv4Addresses, GetIPv4Addresses(properties));
		cachedNetworkAdapter.Device.IPv6Addresses = MergeLists(cachedNetworkAdapter.Device.IPv6Addresses, GetIPv6Addresses(properties));
		cachedNetworkAdapter.Device.Gateway = FirstAvailable(cachedNetworkAdapter.Device.Gateway, GetGateway(properties));
		cachedNetworkAdapter.Device.DnsServers = MergeLists(cachedNetworkAdapter.Device.DnsServers, GetDnsServers(properties));
		cachedNetworkAdapter.Device.Source = AppendSource(cachedNetworkAdapter.Device.Source, "System.Net.NetworkInformation");
	}

	private void AddOrMergeWmiAdapter(List<CachedNetworkAdapter> adapters, WmiAdapterInfo wmiAdapter)
	{
		CachedNetworkAdapter? cachedNetworkAdapter = FindMatchingAdapter(adapters, wmiAdapter.Guid, wmiAdapter.MacAddress, wmiAdapter.InterfaceIndex, wmiAdapter.NetConnectionId, wmiAdapter.Name, wmiAdapter.Description, wmiAdapter.ProductName);
		if (cachedNetworkAdapter == null)
		{
			string id = FirstAvailable(wmiAdapter.Guid, wmiAdapter.MacAddress, wmiAdapter.NetConnectionId, wmiAdapter.Name, wmiAdapter.Description, Guid.NewGuid().ToString("N"));
			cachedNetworkAdapter = new CachedNetworkAdapter(id);
			adapters.Add(cachedNetworkAdapter);
		}
		cachedNetworkAdapter.WmiGuid = FirstAvailable(cachedNetworkAdapter.WmiGuid, wmiAdapter.Guid);
		CachedNetworkAdapter cachedNetworkAdapter2 = cachedNetworkAdapter;
		if (!cachedNetworkAdapter2.InterfaceIndex.HasValue)
		{
			int? num = (cachedNetworkAdapter2.InterfaceIndex = wmiAdapter.InterfaceIndex);
		}
		cachedNetworkAdapter2 = cachedNetworkAdapter;
		if (!cachedNetworkAdapter2.PhysicalAdapter.HasValue)
		{
			bool? flag = (cachedNetworkAdapter2.PhysicalAdapter = wmiAdapter.PhysicalAdapter);
		}
		cachedNetworkAdapter.Device.Id = FirstAvailable(cachedNetworkAdapter.Device.Id, wmiAdapter.Guid, wmiAdapter.MacAddress, cachedNetworkAdapter.Id);
		cachedNetworkAdapter.Device.Name = FirstAvailable(cachedNetworkAdapter.Device.Name, wmiAdapter.NetConnectionId, wmiAdapter.Name, wmiAdapter.ProductName, wmiAdapter.Description, cachedNetworkAdapter.Id);
		cachedNetworkAdapter.Device.Description = FirstAvailable(cachedNetworkAdapter.Device.Description, wmiAdapter.Description, wmiAdapter.ProductName);
		cachedNetworkAdapter.Device.InterfaceType = FirstAvailable(cachedNetworkAdapter.Device.InterfaceType, wmiAdapter.AdapterType, wmiAdapter.ServiceName);
		cachedNetworkAdapter.Device.MacAddress = FirstAvailable(cachedNetworkAdapter.Device.MacAddress, NormalizeMacAddress(wmiAdapter.MacAddress));
		NetworkAdapterDevice device = cachedNetworkAdapter.Device;
		if (!device.LinkSpeed.HasValue)
		{
			ulong? num2 = (device.LinkSpeed = wmiAdapter.Speed);
		}
		cachedNetworkAdapter.Device.IsUp = cachedNetworkAdapter.Device.IsUp || wmiAdapter.NetEnabled.GetValueOrDefault() || wmiAdapter.NetConnectionStatus.GetValueOrDefault() == 2;
		cachedNetworkAdapter.Device.Source = AppendSource(cachedNetworkAdapter.Device.Source, "Win32_NetworkAdapter");
	}

	private void AddOrMergeWmiConfiguration(List<CachedNetworkAdapter> adapters, WmiConfigurationInfo configuration)
	{
		CachedNetworkAdapter? cachedNetworkAdapter = FindMatchingAdapter(adapters, configuration.SettingId, configuration.MacAddress, configuration.InterfaceIndex, configuration.Description);
		if (cachedNetworkAdapter == null)
		{
			if (string.IsNullOrWhiteSpace(configuration.Description) && string.IsNullOrWhiteSpace(configuration.MacAddress) && configuration.IPAddresses.Count == 0)
			{
				return;
			}
			string id = FirstAvailable(configuration.SettingId, configuration.MacAddress, configuration.Description, Guid.NewGuid().ToString("N"));
			cachedNetworkAdapter = new CachedNetworkAdapter(id);
			adapters.Add(cachedNetworkAdapter);
		}
		cachedNetworkAdapter.ConfigurationId = FirstAvailable(cachedNetworkAdapter.ConfigurationId, configuration.SettingId);
		CachedNetworkAdapter cachedNetworkAdapter2 = cachedNetworkAdapter;
		if (!cachedNetworkAdapter2.InterfaceIndex.HasValue)
		{
			int? num = (cachedNetworkAdapter2.InterfaceIndex = configuration.InterfaceIndex);
		}
		cachedNetworkAdapter.Device.Id = FirstAvailable(cachedNetworkAdapter.Device.Id, configuration.SettingId, configuration.MacAddress, cachedNetworkAdapter.Id);
		cachedNetworkAdapter.Device.Name = FirstAvailable(cachedNetworkAdapter.Device.Name, configuration.Description, cachedNetworkAdapter.Id);
		cachedNetworkAdapter.Device.Description = FirstAvailable(cachedNetworkAdapter.Device.Description, configuration.Description);
		cachedNetworkAdapter.Device.MacAddress = FirstAvailable(cachedNetworkAdapter.Device.MacAddress, NormalizeMacAddress(configuration.MacAddress));
		cachedNetworkAdapter.Device.IPv4Addresses = MergeLists(cachedNetworkAdapter.Device.IPv4Addresses, configuration.IPAddresses.Where(IsIPv4Address));
		cachedNetworkAdapter.Device.IPv6Addresses = MergeLists(cachedNetworkAdapter.Device.IPv6Addresses, configuration.IPAddresses.Where(IsIPv6Address));
		cachedNetworkAdapter.Device.Gateway = FirstAvailable(cachedNetworkAdapter.Device.Gateway, configuration.Gateways.FirstOrDefault());
		cachedNetworkAdapter.Device.DnsServers = MergeLists(cachedNetworkAdapter.Device.DnsServers, configuration.DnsServers);
		NetworkAdapterDevice device = cachedNetworkAdapter.Device;
		if (!device.DhcpEnabled.HasValue)
		{
			bool? flag = (device.DhcpEnabled = configuration.DhcpEnabled);
		}
		cachedNetworkAdapter.Device.Source = AppendSource(cachedNetworkAdapter.Device.Source, "Win32_NetworkAdapterConfiguration");
	}

	private void MergeSystemNetworkStatistics(List<NetworkAdapterDevice> devices, CancellationToken cancellationToken)
	{
		DateTimeOffset now = DateTimeOffset.Now;
		NetworkInterface[] networkInterfaces = GetNetworkInterfaces();
		foreach (NetworkAdapterDevice device in devices)
		{
			cancellationToken.ThrowIfCancellationRequested();
			NetworkInterface? networkInterface = FindMatchingNetworkInterface(device, networkInterfaces);
			if (networkInterface == null)
			{
				continue;
			}
			IPInterfaceStatistics? iPInterfaceStatistics = TryGetIPStatistics(networkInterface);
			if (iPInterfaceStatistics == null)
			{
				continue;
			}
			ulong? num = NormalizeCounterTotal(iPInterfaceStatistics.BytesSent);
			ulong? num2 = NormalizeCounterTotal(iPInterfaceStatistics.BytesReceived);
			device.IsUp = networkInterface.OperationalStatus == OperationalStatus.Up;
			NetworkAdapterDevice networkAdapterDevice = device;
			if (!networkAdapterDevice.LinkSpeed.HasValue)
			{
				ulong? num4 = (networkAdapterDevice.LinkSpeed = ((networkInterface.Speed > 0) ? new ulong?((ulong)networkInterface.Speed) : null));
			}
			device.TotalUploaded = num ?? device.TotalUploaded;
			device.TotalDownloaded = num2 ?? device.TotalDownloaded;
			if (!num.HasValue || !num2.HasValue)
			{
				continue;
			}
			string key = FirstAvailable(networkInterface.Id, device.Id);
			if (previousSamples.TryGetValue(key, out ThroughputSample? value))
			{
				double num5 = Math.Max(0.001, (now - value.Timestamp).TotalSeconds);
				if (num5 >= 0.25)
				{
					device.UploadSpeed = CalculateDeltaPerSecond(num.Value, value.UploadedBytes, num5) ?? device.UploadSpeed;
					device.DownloadSpeed = CalculateDeltaPerSecond(num2.Value, value.DownloadedBytes, num5) ?? device.DownloadSpeed;
				}
			}
			previousSamples[key] = new ThroughputSample(num.Value, num2.Value, now);
			device.Source = AppendSource(device.Source, "System.Net.NetworkInformation");
		}
	}

	private void MergePerformanceCounterReadings(List<NetworkAdapterDevice> devices, CancellationToken cancellationToken)
	{
		foreach (NetworkAdapterDevice device in devices)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (device.UploadSpeed.HasValue && device.DownloadSpeed.HasValue)
			{
				continue;
			}
			NetworkPerformanceSnapshot? networkPerformanceSnapshot = ReadPerformanceSnapshot(device);
			if (networkPerformanceSnapshot != null)
			{
				NetworkAdapterDevice networkAdapterDevice = device;
				if (!networkAdapterDevice.UploadSpeed.HasValue)
				{
					double? num = (networkAdapterDevice.UploadSpeed = networkPerformanceSnapshot.BytesSentPerSecond);
				}
				networkAdapterDevice = device;
				if (!networkAdapterDevice.DownloadSpeed.HasValue)
				{
					double? num = (networkAdapterDevice.DownloadSpeed = networkPerformanceSnapshot.BytesReceivedPerSecond);
				}
				device.Source = AppendSource(device.Source, "PerformanceCounter");
			}
		}
	}

	private void EnsureLibreHardwareMonitorDevices(List<NetworkAdapterDevice> devices, IReadOnlyList<SensorReading> sensorReadings)
	{
		foreach (IGrouping<string, SensorReading> item in sensorReadings.Where((SensorReading reading) => reading.Category == SensorCategory.Network).GroupBy<SensorReading, string>((SensorReading reading) => reading.DeviceName, StringComparer.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrWhiteSpace(item.Key) && FindMatchingDeviceByName(devices, item.Key) == null)
			{
				devices.Add(new NetworkAdapterDevice
				{
					Id = HardwareMetricService.NormalizeMetricId("network.lhm." + item.Key),
					Name = item.Key,
					Description = item.Key,
					InterfaceType = "Network",
					IsVirtual = ContainsAnyKeyword(item.Key, VirtualAdapterKeywords),
					IsWireless = ContainsAnyKeyword(item.Key, WirelessKeywords),
					Source = "LibreHardwareMonitor",
					Availability = SensorAvailability.Available
				});
			}
		}
	}

	private static void MergeLibreHardwareMonitorReadings(List<NetworkAdapterDevice> devices, IReadOnlyList<SensorReading> sensorReadings)
	{
		SensorReading[] source = sensorReadings.Where((SensorReading reading) => reading.Category == SensorCategory.Network && reading.IsAvailable).ToArray();
		foreach (NetworkAdapterDevice device in devices)
		{
			SensorReading[] array = source.Where((SensorReading reading) => IsLikelySameAdapter(device, reading.DeviceName)).ToArray();
			if (array.Length == 0)
			{
				continue;
			}
			SensorReading? reading2 = FindReading(array, SensorType.Throughput, "upload", "sent", "send", "transmit", "tx");
			SensorReading? reading3 = FindReading(array, SensorType.Throughput, "download", "received", "receive", "rx");
			SensorReading? reading4 = FindReading(array, SensorType.Data, "upload", "sent", "send", "transmit", "tx");
			SensorReading? reading5 = FindReading(array, SensorType.Data, "download", "received", "receive", "rx");
			SensorReading? sensorReading = FindReading(array, SensorType.Load, "utilization", "load", "usage", "network");
			NetworkAdapterDevice networkAdapterDevice = device;
			if (!networkAdapterDevice.UploadSpeed.HasValue)
			{
				double? num2 = (networkAdapterDevice.UploadSpeed = ConvertSensorValueToBytes(reading2));
			}
			networkAdapterDevice = device;
			if (!networkAdapterDevice.DownloadSpeed.HasValue)
			{
				double? num2 = (networkAdapterDevice.DownloadSpeed = ConvertSensorValueToBytes(reading3));
			}
			networkAdapterDevice = device;
			if (!networkAdapterDevice.TotalUploaded.HasValue)
			{
				NetworkAdapterDevice networkAdapterDevice2 = networkAdapterDevice;
				double? num4 = ConvertSensorValueToBytes(reading4);
				ulong? obj;
				if (num4.HasValue)
				{
					double valueOrDefault = num4.GetValueOrDefault();
					obj = (ulong)Math.Max(0.0, valueOrDefault);
				}
				else
				{
					obj = null;
				}
				ulong? num5 = obj;
				networkAdapterDevice2.TotalUploaded = obj;
			}
			networkAdapterDevice = device;
			if (!networkAdapterDevice.TotalDownloaded.HasValue)
			{
				NetworkAdapterDevice networkAdapterDevice3 = networkAdapterDevice;
				double? num4 = ConvertSensorValueToBytes(reading5);
				ulong? obj2;
				if (num4.HasValue)
				{
					double valueOrDefault2 = num4.GetValueOrDefault();
					obj2 = (ulong)Math.Max(0.0, valueOrDefault2);
				}
				else
				{
					obj2 = null;
				}
				ulong? num5 = obj2;
				networkAdapterDevice3.TotalDownloaded = obj2;
			}
			networkAdapterDevice = device;
			if (!networkAdapterDevice.Utilization.HasValue)
			{
				double? num2 = (networkAdapterDevice.Utilization = sensorReading?.Value);
			}
			device.Source = AppendSource(device.Source, "LibreHardwareMonitor");
		}
	}

	private NetworkPerformanceSnapshot? ReadPerformanceSnapshot(NetworkAdapterDevice device)
	{
		string? text = FindPerformanceCounterInstance(device);
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		try
		{
			if (!performanceCounters.TryGetValue(text, out NetworkCounterSet? value))
			{
				value = new NetworkCounterSet(text);
				performanceCounters[text] = value;
			}
			return value.Read();
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			AppLogger.LogError("Network performance counter read failed for " + text + ".", ex, "network-perf-read:" + text + ":" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
			return null;
		}
	}

	private string? FindPerformanceCounterInstance(NetworkAdapterDevice device)
	{
		string[] array = GetPerformanceCounterInstances();
		if (array.Length == 0)
		{
			return null;
		}
		string[] candidateNames = new string[3]
		{
			device.Description ?? string.Empty,
			device.Name,
			device.InterfaceType ?? string.Empty
		};
		return (from instanceName in array
			select new
			{
				InstanceName = instanceName,
				Score = candidateNames.Max((string candidateName) => ScoreNameMatch(instanceName, candidateName))
			} into candidate
			where candidate.Score > 0.45
			orderby candidate.Score descending
			select candidate.InstanceName).FirstOrDefault();
	}

	private string[] GetPerformanceCounterInstances()
	{
		if (performanceCountersAvailable == false)
		{
			return Array.Empty<string>();
		}
		if (performanceCounterInstances != null && DateTimeOffset.Now - performanceCounterInstancesReadAt < TimeSpan.FromSeconds(30.0))
		{
			return performanceCounterInstances;
		}
		try
		{
			if (!PerformanceCounterCategory.Exists("Network Interface"))
			{
				performanceCountersAvailable = false;
				performanceCounterInstances = Array.Empty<string>();
				return Array.Empty<string>();
			}
			PerformanceCounterCategory performanceCounterCategory = new PerformanceCounterCategory("Network Interface");
			performanceCounterInstances = performanceCounterCategory.GetInstanceNames().OrderBy<string, string>((string instanceName) => instanceName, StringComparer.OrdinalIgnoreCase).ToArray();
			performanceCounterInstancesReadAt = DateTimeOffset.Now;
			performanceCountersAvailable = true;
			RemoveStalePerformanceCounters((IReadOnlyCollection<string>)(object)performanceCounterInstances);
			return performanceCounterInstances;
		}
		catch (Exception ex) when (((ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException || ex is Win32Exception) ? 1 : 0) != 0)
		{
			performanceCountersAvailable = false;
			performanceCounterInstances = Array.Empty<string>();
			AppLogger.LogError("Network performance counters are unavailable.", ex, "network-perf-category:" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
			return Array.Empty<string>();
		}
	}

	private void RemoveStalePerformanceCounters(IReadOnlyCollection<string> instanceNames)
	{
		IReadOnlyCollection<string> instanceNames2 = instanceNames;
		string[] array = performanceCounters.Keys.Where((string key) => !instanceNames2.Contains<string>(key, StringComparer.OrdinalIgnoreCase)).ToArray();
		string[] array2 = array;
		foreach (string key2 in array2)
		{
			performanceCounters[key2].Dispose();
			performanceCounters.Remove(key2);
		}
	}

	private static CachedNetworkAdapter? FindMatchingAdapter(IEnumerable<CachedNetworkAdapter> adapters, string? guid, string? macAddress, int? interfaceIndex, params string?[] names)
	{
		string?[] names2 = names;
		string? text = NormalizeGuid(guid);
		string? text2 = NormalizeMacAddress(macAddress);
		foreach (CachedNetworkAdapter adapter in adapters)
		{
			if (!string.IsNullOrWhiteSpace(text) && (string.Equals(NormalizeGuid(adapter.SystemNetId), text, StringComparison.OrdinalIgnoreCase) || string.Equals(NormalizeGuid(adapter.WmiGuid), text, StringComparison.OrdinalIgnoreCase) || string.Equals(NormalizeGuid(adapter.ConfigurationId), text, StringComparison.OrdinalIgnoreCase) || string.Equals(NormalizeGuid(adapter.Device.Id), text, StringComparison.OrdinalIgnoreCase)))
			{
				return adapter;
			}
			if (!string.IsNullOrWhiteSpace(text2) && string.Equals(NormalizeMacAddress(adapter.Device.MacAddress), text2, StringComparison.OrdinalIgnoreCase))
			{
				return adapter;
			}
			if (interfaceIndex.HasValue && adapter.InterfaceIndex == interfaceIndex)
			{
				return adapter;
			}
		}
		return (from adapter in adapters
			select new
			{
				Adapter = adapter,
				Score = names2.Max((string? name) => Math.Max(ScoreNameMatch(adapter.Device.Name, name), ScoreNameMatch(adapter.Device.Description, name)))
			} into candidate
			where candidate.Score > 0.82
			orderby candidate.Score descending
			select candidate.Adapter).FirstOrDefault();
	}

	private static NetworkInterface? FindMatchingNetworkInterface(NetworkAdapterDevice device, IEnumerable<NetworkInterface> networkInterfaces)
	{
		NetworkAdapterDevice device2 = device;
		string? text = NormalizeGuid(device2.Id);
		string? text2 = NormalizeMacAddress(device2.MacAddress);
		foreach (NetworkInterface networkInterface in networkInterfaces)
		{
			if (!string.IsNullOrWhiteSpace(text) && string.Equals(NormalizeGuid(networkInterface.Id), text, StringComparison.OrdinalIgnoreCase))
			{
				return networkInterface;
			}
			string? macAddress = FormatPhysicalAddress(networkInterface.GetPhysicalAddress());
			if (!string.IsNullOrWhiteSpace(text2) && string.Equals(NormalizeMacAddress(macAddress), text2, StringComparison.OrdinalIgnoreCase))
			{
				return networkInterface;
			}
		}
		return (from networkInterface in networkInterfaces
			select new
			{
				NetworkInterface = networkInterface,
				Score = Math.Max(ScoreNameMatch(networkInterface.Name, device2.Name), ScoreNameMatch(networkInterface.Description, device2.Description))
			} into candidate
			where candidate.Score > 0.82
			orderby candidate.Score descending
			select candidate.NetworkInterface).FirstOrDefault();
	}

	private static NetworkAdapterDevice? FindMatchingDeviceByName(IEnumerable<NetworkAdapterDevice> devices, string name)
	{
		string name2 = name;
		return (from device in devices
			select new
			{
				Device = device,
				Score = Math.Max(ScoreNameMatch(device.Name, name2), ScoreNameMatch(device.Description, name2))
			} into candidate
			where candidate.Score > 0.7
			orderby candidate.Score descending
			select candidate.Device).FirstOrDefault();
	}

	private static void NormalizeStaticAdapter(CachedNetworkAdapter adapter)
	{
		NetworkAdapterDevice device = adapter.Device;
		device.Id = FirstAvailable(device.Id, adapter.SystemNetId, adapter.WmiGuid, adapter.ConfigurationId, device.MacAddress, device.Name);
		device.Name = FirstAvailable(device.Name, device.Description, device.InterfaceType, "Network Adapter");
		device.Description = FirstAvailable(device.Description, device.Name);
		device.InterfaceType = FirstAvailable(device.InterfaceType, device.IsWireless ? "Wireless80211" : "Ethernet");
		device.MacAddress = NormalizeMacAddress(device.MacAddress);
		device.IPv4Addresses = CleanAddressList(device.IPv4Addresses);
		device.IPv6Addresses = CleanAddressList(device.IPv6Addresses);
		device.DnsServers = CleanAddressList(device.DnsServers);
		device.IsWireless = device.IsWireless || ContainsAnyKeyword(JoinForMatching(device.Name, device.Description, device.InterfaceType), WirelessKeywords);
		device.IsVirtual = device.IsVirtual || adapter.PhysicalAdapter == false || ContainsAnyKeyword(JoinForMatching(device.Name, device.Description, device.InterfaceType), VirtualAdapterKeywords) || string.Equals(device.InterfaceType, NetworkInterfaceType.Loopback.ToString(), StringComparison.OrdinalIgnoreCase) || string.Equals(device.InterfaceType, NetworkInterfaceType.Tunnel.ToString(), StringComparison.OrdinalIgnoreCase);
		device.Source = FirstAvailable(device.Source, "HardwareVision");
		device.Availability = ResolveAvailability(device);
	}

	private static void ApplyWirelessSignalQuality(CachedNetworkAdapter adapter, IReadOnlyDictionary<string, double> signalQualityByName)
	{
		if (!adapter.Device.IsWireless || signalQualityByName.Count == 0)
		{
			return;
		}
		foreach (var (left, value) in signalQualityByName)
		{
			if (ScoreNameMatch(left, adapter.Device.Description) > 0.55 || ScoreNameMatch(left, adapter.Device.Name) > 0.55)
			{
				adapter.Device.SignalQuality = value;
				adapter.Device.Source = AppendSource(adapter.Device.Source, "WMI Wi-Fi Signal");
				break;
			}
		}
	}

	private static bool HasMeaningfulAdapterData(CachedNetworkAdapter adapter)
	{
		NetworkAdapterDevice device = adapter.Device;
		return !string.IsNullOrWhiteSpace(device.Name) || !string.IsNullOrWhiteSpace(device.Description) || !string.IsNullOrWhiteSpace(device.MacAddress) || device.IPv4Addresses.Count > 0 || device.IPv6Addresses.Count > 0;
	}

	private static int GetActiveScore(NetworkAdapterDevice device)
	{
		int num = 0;
		if (device.IsUp)
		{
			num += 100;
		}
		if (!device.IsVirtual)
		{
			num += 40;
		}
		if (device.IPv4Addresses.Count > 0)
		{
			num += 20;
		}
		if (!string.IsNullOrWhiteSpace(device.Gateway))
		{
			num += 20;
		}
		if (device.DownloadSpeed.GetValueOrDefault() > 0.0 || device.UploadSpeed.GetValueOrDefault() > 0.0)
		{
			num += 10;
		}
		return num;
	}

	private static SensorAvailability ResolveAvailability(NetworkAdapterDevice device)
	{
		if (device.IsUp || !string.IsNullOrWhiteSpace(device.MacAddress) || device.IPv4Addresses.Count > 0 || device.IPv6Addresses.Count > 0)
		{
			return SensorAvailability.Available;
		}
		return SensorAvailability.NotReported;
	}

	private static double? CalculateUtilization(NetworkAdapterDevice device)
	{
		if (!device.LinkSpeed.HasValue || device.LinkSpeed.Value == 0)
		{
			return null;
		}
		double num = Math.Max(0.0, device.DownloadSpeed.GetValueOrDefault()) + Math.Max(0.0, device.UploadSpeed.GetValueOrDefault());
		double num2 = num * 8.0 / (double)device.LinkSpeed.Value * 100.0;
		return double.IsFinite(num2) ? new double?(Math.Clamp(num2, 0.0, 100.0)) : null;
	}

	private static double? CalculateDeltaPerSecond(ulong currentValue, ulong previousValue, double elapsedSeconds)
	{
		if (currentValue < previousValue || elapsedSeconds <= 0.0)
		{
			return null;
		}
		return (double)(currentValue - previousValue) / elapsedSeconds;
	}

	private static ulong? NormalizeCounterTotal(long value)
	{
		return (value < 0) ? null : new ulong?((ulong)value);
	}

	private static NetworkInterface[] GetNetworkInterfaces()
	{
		try
		{
			return NetworkInterface.GetAllNetworkInterfaces();
		}
		catch (Exception ex) when (((ex is NetworkInformationException || ex is PlatformNotSupportedException) ? 1 : 0) != 0)
		{
			AppLogger.LogError("Network interface enumeration failed.", ex, "network-interface-enumeration:" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
			return Array.Empty<NetworkInterface>();
		}
	}

	private static IPInterfaceProperties? TryGetIPProperties(NetworkInterface networkInterface)
	{
		try
		{
			return networkInterface.GetIPProperties();
		}
		catch (Exception ex) when (((ex is NetworkInformationException || ex is PlatformNotSupportedException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static IPv4InterfaceProperties? TryGetIPv4Properties(IPInterfaceProperties? properties)
	{
		if (properties == null)
		{
			return null;
		}
		try
		{
			return properties.GetIPv4Properties();
		}
		catch (Exception ex) when (((ex is NetworkInformationException || ex is PlatformNotSupportedException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static IPInterfaceStatistics? TryGetIPStatistics(NetworkInterface networkInterface)
	{
		try
		{
			return networkInterface.GetIPStatistics();
		}
		catch (Exception ex) when (((ex is NetworkInformationException || ex is PlatformNotSupportedException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static IEnumerable<string> GetIPv4Addresses(IPInterfaceProperties? properties)
	{
		if (properties == null)
		{
			return Array.Empty<string>();
		}
		return from address in properties.UnicastAddresses
			where address.Address.AddressFamily == AddressFamily.InterNetwork
			select address.Address.ToString();
	}

	private static IEnumerable<string> GetIPv6Addresses(IPInterfaceProperties? properties)
	{
		if (properties == null)
		{
			return Array.Empty<string>();
		}
		return from address in properties.UnicastAddresses
			where address.Address.AddressFamily == AddressFamily.InterNetworkV6
			select address.Address.ToString();
	}

	private static string? GetGateway(IPInterfaceProperties? properties)
	{
		return properties?.GatewayAddresses.Select((GatewayIPAddressInformation address) => address.Address.ToString()).FirstOrDefault((string address) => !string.IsNullOrWhiteSpace(address) && address != "0.0.0.0");
	}

	private static IEnumerable<string> GetDnsServers(IPInterfaceProperties? properties)
	{
		return properties?.DnsAddresses.Select((IPAddress address) => address.ToString()) ?? Array.Empty<string>();
	}

	private static IReadOnlyList<WmiAdapterInfo> ReadWmiAdapters(CancellationToken cancellationToken)
	{
		List<WmiAdapterInfo> adapters = new List<WmiAdapterInfo>();
		QueryWmi("root\\CIMV2", "Win32_NetworkAdapter", delegate(ManagementBaseObject obj)
		{
			adapters.Add(new WmiAdapterInfo
			{
				Guid = GetString(obj, "GUID"),
				Name = GetString(obj, "Name"),
				Description = GetString(obj, "Description"),
				ProductName = GetString(obj, "ProductName"),
				ServiceName = GetString(obj, "ServiceName"),
				MacAddress = NormalizeMacAddress(GetString(obj, "MACAddress")),
				AdapterType = GetString(obj, "AdapterType"),
				NetConnectionId = GetString(obj, "NetConnectionID"),
				InterfaceIndex = GetInt32(obj, "InterfaceIndex"),
				Speed = GetUInt64(obj, "Speed"),
				PhysicalAdapter = GetBoolean(obj, "PhysicalAdapter"),
				NetEnabled = GetBoolean(obj, "NetEnabled"),
				NetConnectionStatus = GetInt32(obj, "NetConnectionStatus")
			});
		}, cancellationToken);
		return adapters;
	}

	private static IReadOnlyList<WmiConfigurationInfo> ReadWmiConfigurations(CancellationToken cancellationToken)
	{
		List<WmiConfigurationInfo> configurations = new List<WmiConfigurationInfo>();
		QueryWmi("root\\CIMV2", "Win32_NetworkAdapterConfiguration", delegate(ManagementBaseObject obj)
		{
			configurations.Add(new WmiConfigurationInfo
			{
				SettingId = GetString(obj, "SettingID"),
				Description = GetString(obj, "Description"),
				MacAddress = NormalizeMacAddress(GetString(obj, "MACAddress")),
				InterfaceIndex = GetInt32(obj, "InterfaceIndex"),
				IPAddresses = GetStringArray(obj, "IPAddress"),
				Gateways = GetStringArray(obj, "DefaultIPGateway"),
				DnsServers = GetStringArray(obj, "DNSServerSearchOrder"),
				DhcpEnabled = GetBoolean(obj, "DHCPEnabled")
			});
		}, cancellationToken);
		return configurations;
	}

	private static Dictionary<string, double> ReadWirelessSignalQuality(CancellationToken cancellationToken)
	{
		Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		QueryWmi("root\\WMI", "MSNdis_80211_ReceivedSignalStrength", delegate(ManagementBaseObject obj)
		{
			string @string = GetString(obj, "InstanceName");
			int? @int = GetInt32(obj, "Ndis80211ReceivedSignalStrength");
			if (!string.IsNullOrWhiteSpace(@string) && @int.HasValue)
			{
				result[@string] = ConvertDbmToQuality(@int.Value);
			}
		}, cancellationToken, logFailures: false);
		return result;
	}

	private static void QueryWmi(string scopePath, string wmiClass, Action<ManagementBaseObject> processObject, CancellationToken cancellationToken, bool logFailures = true)
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
					if (logFailures)
					{
						AppLogger.LogError($"WMI object processing failed for {scopePath}:{wmiClass}.", ex, $"network-wmi-object:{scopePath}:{wmiClass}:{ex.GetType().FullName}", TimeSpan.FromMinutes(5.0));
					}
				}
			}
		}
		catch (Exception ex2) when (!(ex2 is OperationCanceledException))
		{
			if (logFailures)
			{
				AppLogger.LogError($"WMI query failed for {scopePath}:{wmiClass}.", ex2, $"network-wmi-query:{scopePath}:{wmiClass}:{ex2.GetType().FullName}", TimeSpan.FromMinutes(5.0));
			}
		}
	}

	private static string GetString(ManagementBaseObject obj, string propertyName)
	{
		object? value2 = GetValue(obj, propertyName);
		if (value2 == null)
		{
			return string.Empty;
		}
		if (value2 is string[] source)
		{
			return source.FirstOrDefault((string? value) => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
		}
		string? text = Convert.ToString(value2, CultureInfo.InvariantCulture)?.Trim();
		return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
	}

	private static List<string> GetStringArray(ManagementBaseObject obj, string propertyName)
	{
		object? value = GetValue(obj, propertyName);
		if (value is string[] values)
		{
			return CleanAddressList(values);
		}
		string @string = GetString(obj, propertyName);
		List<string> list;
		if (!string.IsNullOrWhiteSpace(@string))
		{
			int num = 1;
			list = new List<string>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<string> span = CollectionsMarshal.AsSpan(list);
			int num2 = 0;
			span[num2] = @string;
			num2++;
		}
		else
		{
			list = new List<string>();
		}
		return list;
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

	private static int? GetInt32(ManagementBaseObject obj, string propertyName)
	{
		object? value = GetValue(obj, propertyName);
		if (value == null)
		{
			return null;
		}
		try
		{
			return Convert.ToInt32(value, CultureInfo.InvariantCulture);
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

	private static SensorReading? FindReading(IEnumerable<SensorReading> readings, SensorType type, params string[] nameTokens)
	{
		string[] nameTokens2 = nameTokens;
		return readings.FirstOrDefault((SensorReading reading) => reading.Type == type && nameTokens2.Any((string token) => reading.SensorName.Contains(token, StringComparison.OrdinalIgnoreCase))) ?? readings.FirstOrDefault((SensorReading reading) => reading.Type == type);
	}

	private static double? ConvertSensorValueToBytes(SensorReading? reading)
	{
		double? num = reading?.Value;
		if (num.HasValue)
		{
			double valueOrDefault = num.GetValueOrDefault();
			if (true)
			{
				string text = reading?.Unit.Trim() ?? string.Empty;
				if (text.Contains("GB", StringComparison.OrdinalIgnoreCase))
				{
					return valueOrDefault * 1024.0 * 1024.0 * 1024.0;
				}
				if (text.Contains("MB", StringComparison.OrdinalIgnoreCase))
				{
					return valueOrDefault * 1024.0 * 1024.0;
				}
				if (text.Contains("KB", StringComparison.OrdinalIgnoreCase))
				{
					return valueOrDefault * 1024.0;
				}
				return valueOrDefault;
			}
		}
		return null;
	}

	private static bool IsLikelySameAdapter(NetworkAdapterDevice device, string sensorDeviceName)
	{
		return ScoreNameMatch(device.Name, sensorDeviceName) > 0.55 || ScoreNameMatch(device.Description, sensorDeviceName) > 0.55 || (!string.IsNullOrWhiteSpace(device.MacAddress) && sensorDeviceName.Contains(device.MacAddress, StringComparison.OrdinalIgnoreCase));
	}

	private static double ScoreNameMatch(string? left, string? right)
	{
		string text = NormalizeName(left);
		string text2 = NormalizeName(right);
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
		{
			return 0.0;
		}
		if (string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
		{
			return 1.0;
		}
		if (text.Contains(text2, StringComparison.OrdinalIgnoreCase) || text2.Contains(text, StringComparison.OrdinalIgnoreCase))
		{
			int num = Math.Min(text.Length, text2.Length);
			int num2 = Math.Max(text.Length, text2.Length);
			return Math.Max(0.55, (double)num / (double)num2);
		}
		string[] array = SplitTokens(text);
		string[] rightTokens = SplitTokens(text2);
		if (array.Length == 0 || rightTokens.Length == 0)
		{
			return 0.0;
		}
		int num3 = array.Count((string leftToken) => rightTokens.Contains<string>(leftToken, StringComparer.OrdinalIgnoreCase));
		return (double)num3 / (double)Math.Max(array.Length, rightTokens.Length);
	}

	private static string NormalizeName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		return new string(value.Where((char character) => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)).ToArray()).Trim().ToLowerInvariant();
	}

	private static string[] SplitTokens(string value)
	{
		return (from token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where token.Length > 1
			select token).ToArray();
	}

	private static bool ContainsAnyKeyword(string value, IEnumerable<string> keywords)
	{
		string value2 = value;
		return keywords.Any((string keyword) => value2.Contains(keyword, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsIPv4Address(string value)
	{
		IPAddress? address;
		return IPAddress.TryParse(value, out address) && address.AddressFamily == AddressFamily.InterNetwork;
	}

	private static bool IsIPv6Address(string value)
	{
		IPAddress? address;
		return IPAddress.TryParse(value, out address) && address.AddressFamily == AddressFamily.InterNetworkV6;
	}

	private static List<string> MergeLists(IEnumerable<string> existingValues, IEnumerable<string> newValues)
	{
		return (from value in existingValues.Concat(newValues)
			where !string.IsNullOrWhiteSpace(value)
			select value.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<string> CleanAddressList(IEnumerable<string> values)
	{
		return (from value in values
			where !string.IsNullOrWhiteSpace(value)
			select value.Trim()).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static string AppendSource(string existingSource, string source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return existingSource;
		}
		string[] value = existingSource.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(source).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return string.Join(" / ", value);
	}

	private static string? FormatPhysicalAddress(PhysicalAddress? physicalAddress)
	{
		byte[]? array = physicalAddress?.GetAddressBytes();
		if (array == null || array.Length == 0)
		{
			return null;
		}
		return string.Join(":", array.Select((byte value) => value.ToString("X2", CultureInfo.InvariantCulture)));
	}

	private static string? NormalizeMacAddress(string? macAddress)
	{
		if (string.IsNullOrWhiteSpace(macAddress))
		{
			return null;
		}
		string hex = new string(macAddress.Where(Uri.IsHexDigit).ToArray());
		if (hex.Length != 12)
		{
			return macAddress.Trim();
		}
		return string.Join(":", from index in Enumerable.Range(0, 6)
			select hex.Substring(index * 2, 2).ToUpperInvariant());
	}

	private static string? NormalizeGuid(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}
		return value.Trim().Trim('{', '}');
	}

	private static string FirstAvailable(params string?[] values)
	{
		return values.FirstOrDefault((string? value) => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
	}

	private static string JoinForMatching(params string?[] values)
	{
		return string.Join(" ", values.Where((string? value) => !string.IsNullOrWhiteSpace(value)));
	}

	private static double ConvertDbmToQuality(int signalStrengthDbm)
	{
		return Math.Clamp((double)(signalStrengthDbm + 100) * 2.0, 0.0, 100.0);
	}

	private static NetworkAdapterDevice CloneDevice(NetworkAdapterDevice device)
	{
		NetworkAdapterDevice obj = new NetworkAdapterDevice
		{
			Id = device.Id,
			Name = device.Name,
			Description = device.Description,
			InterfaceType = device.InterfaceType,
			MacAddress = device.MacAddress
		};
		List<string> iPv4Addresses = device.IPv4Addresses;
		int count = iPv4Addresses.Count;
		List<string> list = new List<string>(count);
		CollectionsMarshal.SetCount(list, count);
		Span<string> span = CollectionsMarshal.AsSpan(list);
		int num = 0;
		Span<string> span2 = CollectionsMarshal.AsSpan(iPv4Addresses);
		span2.CopyTo(span.Slice(num, span2.Length));
		num += span2.Length;
		obj.IPv4Addresses = list;
		List<string> iPv6Addresses = device.IPv6Addresses;
		num = iPv6Addresses.Count;
		list = new List<string>(num);
		CollectionsMarshal.SetCount(list, num);
		span2 = CollectionsMarshal.AsSpan(list);
		count = 0;
		span = CollectionsMarshal.AsSpan(iPv6Addresses);
		span.CopyTo(span2.Slice(count, span.Length));
		count += span.Length;
		obj.IPv6Addresses = list;
		obj.Gateway = device.Gateway;
		List<string> dnsServers = device.DnsServers;
		count = dnsServers.Count;
		list = new List<string>(count);
		CollectionsMarshal.SetCount(list, count);
		span = CollectionsMarshal.AsSpan(list);
		num = 0;
		span2 = CollectionsMarshal.AsSpan(dnsServers);
		span2.CopyTo(span.Slice(num, span2.Length));
		num += span2.Length;
		obj.DnsServers = list;
		obj.DhcpEnabled = device.DhcpEnabled;
		obj.LinkSpeed = device.LinkSpeed;
		obj.IsWireless = device.IsWireless;
		obj.IsVirtual = device.IsVirtual;
		obj.IsUp = device.IsUp;
		obj.UploadSpeed = device.UploadSpeed;
		obj.DownloadSpeed = device.DownloadSpeed;
		obj.TotalUploaded = device.TotalUploaded;
		obj.TotalDownloaded = device.TotalDownloaded;
		obj.Utilization = device.Utilization;
		obj.SignalQuality = device.SignalQuality;
		obj.Source = device.Source;
		obj.Availability = device.Availability;
		return obj;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}
}
