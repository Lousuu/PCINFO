using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class GpuDeviceService
{
	private const string LibreHardwareMonitorSource = "LibreHardwareMonitor";

	private const string WmiSource = "WMI";

	private const ulong WmiAdapterRamCapBytes = 4UL * 1024 * 1024 * 1024;

	private const ulong WmiAdapterRamCapToleranceBytes = 64UL * 1024 * 1024;

	private static readonly string[] GenericGpuTokens = new string[13]
	{
		"adapter", "controller", "display", "family", "graphics", "gpu", "integrated", "laptop", "mobile", "processor",
		"r", "tm", "video"
	};

	public IReadOnlyList<GpuDevice> BuildGpuDevices(HardwareSnapshot? snapshot, IEnumerable<SensorReading> readings, string? preferredGpuId)
	{
		ArgumentNullException.ThrowIfNull(readings, "readings");
		List<GpuDevice> list = new List<GpuDevice>();
		foreach (HardwareDevice item in snapshot?.Devices.Where((HardwareDevice device) => device.Category == SensorCategory.Gpu) ?? Enumerable.Empty<HardwareDevice>())
		{
			list.Add(CreateFromWmiDevice(item));
		}
		IEnumerable<IGrouping<string, SensorReading>> enumerable = readings.Where((SensorReading reading) => reading.Category == SensorCategory.Gpu).GroupBy<SensorReading, string>(CreateLibreHardwareMonitorGpuKey, StringComparer.OrdinalIgnoreCase);
		foreach (IGrouping<string, SensorReading> item2 in enumerable)
		{
			List<SensorReading> list2 = item2.OrderBy((SensorReading reading) => reading.Type).ThenBy<SensorReading, string>((SensorReading reading) => reading.SensorName, StringComparer.OrdinalIgnoreCase).ToList();
			if (list2.Count != 0)
			{
				string text = FirstAvailable(list2.Select((SensorReading sensor) => sensor.DeviceName)) ?? "GPU";
				GpuDevice gpuDevice = FindMatchingDevice(list, text);
				if (gpuDevice == null)
				{
					gpuDevice = CreateFromLibreHardwareMonitor(text, item2.Key);
					list.Add(gpuDevice);
				}
				MergeLibreHardwareMonitorSensors(gpuDevice, list2, item2.Key);
			}
		}
		foreach (GpuDevice item3 in list)
		{
			NormalizeDevice(item3);
			PopulateMetrics(item3);
			item3.IsPreferred = false;
		}
		GpuDevice gpuDevice2 = SelectPreferredGpu(list, preferredGpuId);
		if (gpuDevice2 != null)
		{
			gpuDevice2.IsPreferred = true;
		}
		return list.OrderBy((GpuDevice device) => (!device.IsPreferred) ? 1 : 0).ThenBy(GetDefaultPreferenceRank).ThenBy<GpuDevice, string>((GpuDevice device) => device.Name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public GpuDevice? SelectPreferredGpu(IEnumerable<GpuDevice> devices, string? preferredGpuId)
	{
		string preferredGpuId2 = preferredGpuId;
		GpuDevice[] array = devices.ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(preferredGpuId2))
		{
			GpuDevice gpuDevice = array.FirstOrDefault((GpuDevice device) => string.Equals(device.Id, preferredGpuId2, StringComparison.OrdinalIgnoreCase));
			if (gpuDevice != null)
			{
				return gpuDevice;
			}
		}
		return array.OrderBy(GetDefaultPreferenceRank).ThenByDescending(HasAvailableSensors).ThenBy<GpuDevice, string>((GpuDevice device) => device.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	private static GpuDevice CreateFromWmiDevice(HardwareDevice device)
	{
		string text = FirstAvailable(device.Name, device.Model, "GPU") ?? "GPU";
		string vendor = ResolveVendor(device.Vendor, text, device.Model);
		GpuDevice gpuDevice = new GpuDevice();
		gpuDevice.Id = FirstAvailable(device.Id, device.Properties.GetValueOrDefault("PNPDeviceID"), CreateStableId(text)) ?? CreateStableId(text);
		gpuDevice.Name = text;
		gpuDevice.Vendor = vendor;
		gpuDevice.HardwareType = InferHardwareType(text, vendor, null);
		gpuDevice.DriverVersion = device.Properties.GetValueOrDefault("DriverVersion");
		gpuDevice.AdapterRam = ParseAdapterRam(device);
		gpuDevice.IsIntegrated = IsIntegratedGpu(text, vendor);
		gpuDevice.IsDiscrete = IsDiscreteGpu(text, vendor);
		gpuDevice.Source = "WMI";
		gpuDevice.Availability = SensorAvailability.Unknown;
		return gpuDevice;
	}

	private static GpuDevice CreateFromLibreHardwareMonitor(string deviceName, string groupKey)
	{
		string vendor = ResolveVendor(null, deviceName, groupKey);
		GpuDevice gpuDevice = new GpuDevice();
		gpuDevice.Id = FirstAvailable(groupKey, CreateStableId(deviceName)) ?? CreateStableId(deviceName);
		gpuDevice.Name = deviceName;
		gpuDevice.Vendor = vendor;
		gpuDevice.HardwareType = InferHardwareType(deviceName, vendor, groupKey);
		gpuDevice.IsIntegrated = IsIntegratedGpu(deviceName, vendor);
		gpuDevice.IsDiscrete = IsDiscreteGpu(deviceName, vendor);
		gpuDevice.Source = "LibreHardwareMonitor";
		return gpuDevice;
	}

	private static void MergeLibreHardwareMonitorSensors(GpuDevice device, List<SensorReading> sensors, string groupKey)
	{
		device.Name = ChooseBetterName(device.Name, FirstAvailable(sensors.Select((SensorReading sensor) => sensor.DeviceName)));
		device.Vendor = ResolveVendor(device.Vendor, device.Name, groupKey);
		device.HardwareType = InferHardwareType(device.Name, device.Vendor, groupKey);
		device.Source = CombineSources(device.Source, "LibreHardwareMonitor");
		device.Sensors = sensors;
	}

	private static void NormalizeDevice(GpuDevice device)
	{
		device.Name = FirstAvailable(device.Name, "GPU") ?? "GPU";
		device.Vendor = ResolveVendor(device.Vendor, device.Name, device.HardwareType);
		device.HardwareType = InferHardwareType(device.Name, device.Vendor, device.HardwareType);
		device.IsIntegrated = IsIntegratedGpu(device.Name, device.Vendor);
		device.IsDiscrete = IsDiscreteGpu(device.Name, device.Vendor);
		if (string.IsNullOrWhiteSpace(device.Id))
		{
			device.Id = CreateStableId(device.HardwareType + ":" + device.Name);
		}
		if (string.IsNullOrWhiteSpace(device.Source))
		{
			device.Source = ((device.Sensors.Count > 0) ? "LibreHardwareMonitor" : "WMI");
		}
		device.Availability = ResolveAvailability(device.Sensors);
	}

	private static void PopulateMetrics(GpuDevice device)
	{
		device.TemperatureHotSpot = FindReading(device.Sensors, SensorType.Temperature, null, "Hot Spot", "Hotspot");
		device.TemperatureMemoryJunction = FindReading(device.Sensors, SensorType.Temperature, IsMemoryReading, "Memory Junction", "Memory");
		device.TemperatureCore = FindReading(device.Sensors, SensorType.Temperature, (SensorReading reading) => !IsHotSpotReading(reading) && !IsMemoryReading(reading), "GPU Core", "Core", "GPU", "Temperature");
		device.CoreClock = FindReading(device.Sensors, SensorType.Clock, (SensorReading reading) => !IsMemoryReading(reading), "GPU Core", "Core", "Graphics", "Clock");
		device.MemoryClock = FindReading(device.Sensors, SensorType.Clock, IsMemoryReading, "Memory", "VRAM");
		device.CoreLoad = FindReading(device.Sensors, SensorType.Load, (SensorReading reading) => !IsMemoryReading(reading), "GPU Core", "Core", "GPU Load", "Load");
		device.MemoryLoad = FindReading(device.Sensors, SensorType.Load, IsMemoryReading, "Memory", "VRAM", "D3D");
		device.MemoryUsed = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsMemoryReading(reading) && IsUsedReading(reading), "Memory Used", "GPU Memory Used", "D3D Dedicated Memory Used", "Dedicated Memory Used");
		device.MemoryFree = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsMemoryReading(reading) && IsFreeReading(reading), "Memory Free", "GPU Memory Free", "D3D Dedicated Memory Free", "Dedicated Memory Free");
		device.MemoryTotal = FindReading(device.Sensors, SensorType.Data, (SensorReading reading) => IsMemoryReading(reading) && IsTotalReading(reading), "Memory Total", "GPU Memory Total", "D3D Dedicated Memory Total", "Dedicated Memory Total");
		device.PowerPackage = FindReading(device.Sensors, SensorType.Power, null, "Board", "Package", "GPU", "Power");
		device.CoreVoltage = FindReading(device.Sensors, SensorType.Voltage, (SensorReading reading) => !IsMemoryReading(reading), "GPU Core", "Core", "Voltage");
		device.FanSpeed = FindReading(device.Sensors, SensorType.Fan, null, "Fan", "GPU");
		device.PcieRx = FindReading(device.Sensors, SensorType.Throughput, (SensorReading reading) => IsPcieReading(reading) && IsReceiveReading(reading), "PCIe Rx", "PCIe Read", "Rx", "Read");
		device.PcieTx = FindReading(device.Sensors, SensorType.Throughput, (SensorReading reading) => IsPcieReading(reading) && IsTransmitReading(reading), "PCIe Tx", "PCIe Write", "Tx", "Write");
	}

	private static SensorReading? FindReading(IEnumerable<SensorReading> readings, SensorType type, Func<SensorReading, bool>? predicate, params string[] preferredNames)
	{
		Func<SensorReading, bool> predicate2 = predicate;
		SensorReading[] source = (from reading in readings
			where reading.Type == type && reading.IsAvailable
			where predicate2?.Invoke(reading) ?? true
			select reading).ToArray();
		foreach (string preferredName in preferredNames)
		{
			SensorReading sensorReading = source.FirstOrDefault((SensorReading reading) => reading.SensorName.Contains(preferredName, StringComparison.OrdinalIgnoreCase));
			if (sensorReading != null)
			{
				return sensorReading;
			}
		}
		return source.FirstOrDefault();
	}

	private static GpuDevice? FindMatchingDevice(IEnumerable<GpuDevice> devices, string lhmDeviceName)
	{
		string lhmDeviceName2 = lhmDeviceName;
		string lhmVendor = ResolveVendor(null, lhmDeviceName2, null);
		return (from device in devices
			select new
			{
				Device = device,
				Score = ScoreGpuNameMatch(device.Name, lhmDeviceName2),
				VendorCompatible = AreVendorsCompatible(device.Vendor, lhmVendor)
			} into candidate
			where candidate.VendorCompatible && candidate.Score >= 0.45
			orderby candidate.Score descending
			select candidate.Device).FirstOrDefault();
	}

	private static double ScoreGpuNameMatch(string? left, string? right)
	{
		string text = NormalizeForContains(left);
		string text2 = NormalizeForContains(right);
		if (text.Length == 0 || text2.Length == 0)
		{
			return 0.0;
		}
		if (text.Contains(text2, StringComparison.Ordinal) || text2.Contains(text, StringComparison.Ordinal))
		{
			return 1.0;
		}
		string[] array = TokenizeName(left);
		string[] array2 = TokenizeName(right);
		if (array.Length == 0 || array2.Length == 0)
		{
			return 0.0;
		}
		int num = array.Intersect<string>(array2, StringComparer.OrdinalIgnoreCase).Count();
		return (double)num / (double)Math.Min(array.Length, array2.Length);
	}

	private static string CreateLibreHardwareMonitorGpuKey(SensorReading reading)
	{
		string text = TryGetLibreHardwareMonitorRootIdentifier(reading.RawIdentifier);
		return FirstAvailable(text, reading.DeviceName, reading.RawIdentifier, CreateStableId(reading.DeviceName)) ?? CreateStableId("gpu");
	}

	private static string? TryGetLibreHardwareMonitorRootIdentifier(string? rawIdentifier)
	{
		if (string.IsNullOrWhiteSpace(rawIdentifier))
		{
			return null;
		}
		string[] array = rawIdentifier.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length >= 2)
		{
			return "/" + array[0] + "/" + array[1];
		}
		return rawIdentifier.Trim();
	}

	private static string InferHardwareType(string? name, string? vendor, string? identifier)
	{
		string text = $"{name} {vendor} {identifier}";
		if (text.Contains("nvidia", StringComparison.OrdinalIgnoreCase) || text.Contains("geforce", StringComparison.OrdinalIgnoreCase) || text.Contains("quadro", StringComparison.OrdinalIgnoreCase) || text.Contains("rtx", StringComparison.OrdinalIgnoreCase) || text.Contains("gtx", StringComparison.OrdinalIgnoreCase))
		{
			return "GpuNvidia";
		}
		if (text.Contains("amd", StringComparison.OrdinalIgnoreCase) || text.Contains("radeon", StringComparison.OrdinalIgnoreCase) || text.Contains("amdgpu", StringComparison.OrdinalIgnoreCase))
		{
			return "GpuAmd";
		}
		if (text.Contains("intel", StringComparison.OrdinalIgnoreCase) || text.Contains("intelgpu", StringComparison.OrdinalIgnoreCase) || text.Contains("iris", StringComparison.OrdinalIgnoreCase) || text.Contains("uhd", StringComparison.OrdinalIgnoreCase))
		{
			return "GpuIntel";
		}
		return "Gpu";
	}

	private static string? ResolveVendor(params string?[] values)
	{
		string text = string.Join(" ", values.Where((string value) => !string.IsNullOrWhiteSpace(value)));
		if (text.Contains("nvidia", StringComparison.OrdinalIgnoreCase) || text.Contains("geforce", StringComparison.OrdinalIgnoreCase))
		{
			return "NVIDIA";
		}
		if (text.Contains("amd", StringComparison.OrdinalIgnoreCase) || text.Contains("advanced micro devices", StringComparison.OrdinalIgnoreCase) || text.Contains("radeon", StringComparison.OrdinalIgnoreCase))
		{
			return "AMD";
		}
		if (text.Contains("intel", StringComparison.OrdinalIgnoreCase) || text.Contains("iris", StringComparison.OrdinalIgnoreCase) || text.Contains("uhd", StringComparison.OrdinalIgnoreCase))
		{
			return "Intel";
		}
		return FirstAvailable(values);
	}

	private static bool IsIntegratedGpu(string? name, string? vendor)
	{
		string text = name + " " + vendor;
		return text.Contains("intel", StringComparison.OrdinalIgnoreCase) || text.Contains("integrated", StringComparison.OrdinalIgnoreCase) || text.Contains("uhd", StringComparison.OrdinalIgnoreCase) || text.Contains("iris", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsDiscreteGpu(string? name, string? vendor)
	{
		string text = name + " " + vendor;
		if (IsIntegratedGpu(name, vendor))
		{
			return false;
		}
		return text.Contains("nvidia", StringComparison.OrdinalIgnoreCase) || text.Contains("geforce", StringComparison.OrdinalIgnoreCase) || text.Contains("amd", StringComparison.OrdinalIgnoreCase) || text.Contains("radeon", StringComparison.OrdinalIgnoreCase) || !text.Contains("intel", StringComparison.OrdinalIgnoreCase);
	}

	private static int GetDefaultPreferenceRank(GpuDevice device)
	{
		if (device.IsDiscrete && string.Equals(device.Vendor, "NVIDIA", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (device.IsDiscrete && string.Equals(device.Vendor, "AMD", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		if (device.IsDiscrete && !string.Equals(device.Vendor, "Intel", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		if (string.Equals(device.Vendor, "Intel", StringComparison.OrdinalIgnoreCase) || device.IsIntegrated)
		{
			return 3;
		}
		return 4;
	}

	private static bool HasAvailableSensors(GpuDevice device)
	{
		return device.Sensors.Any((SensorReading sensor) => sensor.IsAvailable);
	}

	private static SensorAvailability ResolveAvailability(IEnumerable<SensorReading> sensors)
	{
		SensorReading[] array = sensors.ToArray();
		if (array.Any((SensorReading reading) => reading.IsAvailable))
		{
			return SensorAvailability.Available;
		}
		if (array.Length != 0)
		{
			return SensorAvailability.NotReported;
		}
		return SensorAvailability.Unknown;
	}

	private static ulong? ParseAdapterRam(HardwareDevice device)
	{
		if (device.Properties.TryGetValue("AdapterRAMBytes", out string value) && ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return NormalizeWmiAdapterRam(result);
		}
		if (device.Properties.TryGetValue("AdapterRAM", out string value2))
		{
			return NormalizeWmiAdapterRam(ParseFormattedBytes(value2));
		}
		return null;
	}

	private static ulong? NormalizeWmiAdapterRam(ulong? bytes)
	{
		if (!bytes.HasValue)
		{
			return null;
		}

		// Win32_VideoController.AdapterRAM often caps modern GPUs at about 4 GiB.
		return IsLikelyWmiAdapterRamCap(bytes.Value) ? null : bytes;
	}

	private static bool IsLikelyWmiAdapterRamCap(ulong bytes)
	{
		return bytes >= WmiAdapterRamCapBytes - WmiAdapterRamCapToleranceBytes && bytes <= WmiAdapterRamCapBytes;
	}

	private static ulong? ParseFormattedBytes(string? formattedValue)
	{
		if (string.IsNullOrWhiteSpace(formattedValue))
		{
			return null;
		}
		string[] array = formattedValue.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (array.Length < 2 || !double.TryParse(array[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		string text = array[1].ToUpperInvariant();
		if (1 == 0)
		{
		}
		double num = text switch
		{
			"KB" => 1024.0, 
			"MB" => 1048576.0, 
			"GB" => 1073741824.0, 
			"TB" => 1099511627776.0, 
			_ => 1.0, 
		};
		if (1 == 0)
		{
		}
		double num2 = num;
		double num3 = result * num2;
		return (num3 > 0.0 && num3 <= 1.8446744073709552E+19) ? new ulong?(Convert.ToUInt64(num3)) : null;
	}

	private static string ChooseBetterName(string? currentName, string? candidateName)
	{
		if (string.IsNullOrWhiteSpace(currentName))
		{
			return FirstAvailable(candidateName, "GPU") ?? "GPU";
		}
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			return currentName.Trim();
		}
		string text = currentName.Trim();
		string text2 = candidateName.Trim();
		return (text2.Length > text.Length) ? text2 : text;
	}

	private static string CombineSources(string? existingSource, string newSource)
	{
		string[] value = (existingSource ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(newSource).Distinct<string>(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		return string.Join(", ", value);
	}

	private static bool AreVendorsCompatible(string? left, string? right)
	{
		if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
		{
			return true;
		}
		return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
	}

	private static string[] TokenizeName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Array.Empty<string>();
		}
		string text = NormalizeForTokens(value);
		return (from token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			where !GenericGpuTokens.Contains<string>(token, StringComparer.OrdinalIgnoreCase)
			select token).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string NormalizeForContains(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		string text = value.ToLowerInvariant();
		foreach (char c in text)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static string NormalizeForTokens(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		string text = value.ToLowerInvariant();
		foreach (char c in text)
		{
			stringBuilder.Append(char.IsLetterOrDigit(c) ? c : ' ');
		}
		return stringBuilder.ToString();
	}

	private static string CreateStableId(string value)
	{
		string text = NormalizeForContains(value);
		return string.IsNullOrWhiteSpace(text) ? "gpu" : ("gpu-" + text);
	}

	private static string? FirstAvailable(IEnumerable<string?> values)
	{
		return values.FirstOrDefault((string value) => !string.IsNullOrWhiteSpace(value))?.Trim();
	}

	private static string? FirstAvailable(params string?[] values)
	{
		return FirstAvailable(values.AsEnumerable());
	}

	private static bool IsHotSpotReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Hotspot", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsMemoryReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Memory", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("VRAM", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("D3D", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsUsedReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Used", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsFreeReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Free", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTotalReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Total", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsPcieReading(SensorReading reading)
	{
		return reading.SensorName.Contains("PCIe", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Bus", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsReceiveReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Rx", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Read", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Receive", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTransmitReading(SensorReading reading)
	{
		return reading.SensorName.Contains("Tx", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Write", StringComparison.OrdinalIgnoreCase) || reading.SensorName.Contains("Transmit", StringComparison.OrdinalIgnoreCase);
	}
}
