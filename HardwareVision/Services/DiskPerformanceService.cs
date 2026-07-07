using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class DiskPerformanceService : IDisposable
{
	private sealed class DiskCounterSet : IDisposable
	{
		private readonly string instanceName;

		private readonly PerformanceCounter readBytesPerSecond;

		private readonly PerformanceCounter writeBytesPerSecond;

		public DiskCounterSet(string instanceName)
		{
			this.instanceName = instanceName;
			readBytesPerSecond = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instanceName, readOnly: true);
			writeBytesPerSecond = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instanceName, readOnly: true);
		}

		public DiskPerformanceSnapshot Read()
		{
			return new DiskPerformanceSnapshot
			{
				InstanceName = instanceName,
				ReadBytesPerSecond = NormalizeCounterValue(readBytesPerSecond.NextValue()),
				WriteBytesPerSecond = NormalizeCounterValue(writeBytesPerSecond.NextValue())
			};
		}

		public void Dispose()
		{
			readBytesPerSecond.Dispose();
			writeBytesPerSecond.Dispose();
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

	private const string CategoryName = "PhysicalDisk";

	private const string ReadBytesCounterName = "Disk Read Bytes/sec";

	private const string WriteBytesCounterName = "Disk Write Bytes/sec";

	private readonly object syncRoot = new object();

	private readonly Dictionary<string, DiskCounterSet> counters = new Dictionary<string, DiskCounterSet>(StringComparer.OrdinalIgnoreCase);

	private bool isDisposed;

	public Task<IReadOnlyList<DiskPerformanceSnapshot>> GetCurrentSnapshotsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.Run(() => GetCurrentSnapshots(cancellationToken), cancellationToken);
	}

	public void Dispose()
	{
		lock (syncRoot)
		{
			if (isDisposed)
			{
				return;
			}
			foreach (DiskCounterSet value in counters.Values)
			{
				value.Dispose();
			}
			counters.Clear();
			isDisposed = true;
		}
	}

	private IReadOnlyList<DiskPerformanceSnapshot> GetCurrentSnapshots(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		lock (syncRoot)
		{
			if (isDisposed)
			{
				return Array.Empty<DiskPerformanceSnapshot>();
			}
			try
			{
				if (!PerformanceCounterCategory.Exists("PhysicalDisk"))
				{
					return Array.Empty<DiskPerformanceSnapshot>();
				}
				PerformanceCounterCategory performanceCounterCategory = new PerformanceCounterCategory("PhysicalDisk");
				string[] array = (from instanceName in performanceCounterCategory.GetInstanceNames()
					where !string.Equals(instanceName, "_Total", StringComparison.OrdinalIgnoreCase)
					select instanceName).OrderBy<string, string>((string instanceName) => instanceName, StringComparer.OrdinalIgnoreCase).ToArray();
				RemoveStaleCounters((IReadOnlyCollection<string>)(object)array);
				List<DiskPerformanceSnapshot> list = new List<DiskPerformanceSnapshot>();
				string[] array2 = array;
				foreach (string text in array2)
				{
					cancellationToken.ThrowIfCancellationRequested();
					try
					{
						DiskCounterSet orCreateCounterSet = GetOrCreateCounterSet(text);
						list.Add(orCreateCounterSet.Read());
					}
					catch (Exception ex) when (((ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException) ? 1 : 0) != 0)
					{
						AppLogger.LogError("Disk performance counter read failed for " + text + ".", ex, "disk-perf-read:" + text + ":" + ex.GetType().FullName, TimeSpan.FromMinutes(5.0));
					}
				}
				return list;
			}
			catch (Exception ex2) when (((ex2 is InvalidOperationException || ex2 is UnauthorizedAccessException || ex2 is PlatformNotSupportedException || ex2 is Win32Exception) ? 1 : 0) != 0)
			{
				AppLogger.LogError("Disk performance counters are unavailable.", ex2, "disk-perf-category:" + ex2.GetType().FullName, TimeSpan.FromMinutes(5.0));
				return Array.Empty<DiskPerformanceSnapshot>();
			}
		}
	}

	private DiskCounterSet GetOrCreateCounterSet(string instanceName)
	{
		if (counters.TryGetValue(instanceName, out DiskCounterSet value))
		{
			return value;
		}
		value = new DiskCounterSet(instanceName);
		counters[instanceName] = value;
		return value;
	}

	private void RemoveStaleCounters(IReadOnlyCollection<string> instanceNames)
	{
		IReadOnlyCollection<string> instanceNames2 = instanceNames;
		string[] array = counters.Keys.Where((string key) => !instanceNames2.Contains<string>(key, StringComparer.OrdinalIgnoreCase)).ToArray();
		string[] array2 = array;
		foreach (string key2 in array2)
		{
			counters[key2].Dispose();
			counters.Remove(key2);
		}
	}
}
