using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.Sensors;

public sealed class WmiCpuClockSensorProvider : ISensorProvider
{
	public string Name => "WMI";

	public bool IsAvailable { get; private set; } = true;


	public int Priority => 20;

	public Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		IsAvailable = true;
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
	{
		return Task.Run(() => ReadCpuClockSpeed(cancellationToken), cancellationToken);
	}

	private IReadOnlyList<SensorReading> ReadCpuClockSpeed(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		List<SensorReading> list = new List<SensorReading>();
		DateTimeOffset now = DateTimeOffset.Now;
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name, CurrentClockSpeed FROM Win32_Processor");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			int num = 0;
			foreach (ManagementBaseObject item in managementObjectCollection)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					double? wmiDouble = GetWmiDouble(item, "CurrentClockSpeed");
					bool flag = wmiDouble.HasValue && wmiDouble.Value > 0.0;
					list.Add(new SensorReading
					{
						DeviceName = (GetWmiString(item, "Name") ?? $"CPU {num + 1}"),
						SensorName = "CurrentClockSpeed",
						Category = SensorCategory.Cpu,
						Type = SensorType.Clock,
						Value = (flag ? wmiDouble : null),
						Unit = "MHz",
						Status = ((!flag) ? HardwareStatus.NotReported : HardwareStatus.Normal),
						Timestamp = now,
						IsAvailable = flag,
						Source = Name,
						Availability = ((!flag) ? SensorAvailability.NotReported : SensorAvailability.Available),
						RawIdentifier = "Win32_Processor.CurrentClockSpeed",
						LastUpdated = now,
						ErrorMessage = (flag ? null : "WMI CurrentClockSpeed 未返回有效值")
					});
					num++;
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					AppLogger.LogError("WMI CPU clock speed object processing failed.", ex, "wmi-cpu-clock-object:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
				}
			}
			IsAvailable = list.Any((SensorReading reading) => reading.IsAvailable);
		}
		catch (Exception ex2) when (!(ex2 is OperationCanceledException))
		{
			IsAvailable = false;
			AppLogger.LogError("WMI CPU clock speed fallback failed.", ex2, "wmi-cpu-clock:" + ex2.GetType().FullName, TimeSpan.FromMinutes(10.0));
		}
		return list;
	}

	private static double? GetWmiDouble(ManagementBaseObject obj, string propertyName)
	{
		try
		{
			object obj2 = obj.Properties[propertyName]?.Value;
			return (obj2 == null) ? null : new double?(Convert.ToDouble(obj2, CultureInfo.InvariantCulture));
		}
		catch (Exception ex) when (((ex is FormatException || ex is InvalidCastException || ex is ManagementException || ex is ArgumentException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static string? GetWmiString(ManagementBaseObject obj, string propertyName)
	{
		try
		{
			string text = Convert.ToString(obj.Properties[propertyName]?.Value, CultureInfo.InvariantCulture)?.Trim();
			return string.IsNullOrWhiteSpace(text) ? null : text;
		}
		catch (Exception ex) when (((ex is FormatException || ex is InvalidCastException || ex is ManagementException || ex is ArgumentException) ? 1 : 0) != 0)
		{
			return null;
		}
	}
}
