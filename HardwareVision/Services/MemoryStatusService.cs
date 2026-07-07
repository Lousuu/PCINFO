using System;
using System.Runtime.InteropServices;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public static class MemoryStatusService
{
	private struct MemoryStatusEx
	{
		public uint Length;

		public uint MemoryLoad;

		public ulong TotalPhysical;

		public ulong AvailablePhysical;

		public ulong TotalPageFile;

		public ulong AvailablePageFile;

		public ulong TotalVirtual;

		public ulong AvailableVirtual;

		public ulong AvailableExtendedVirtual;
	}

	public static MemoryStatusSnapshot? GetCurrentStatus()
	{
		MemoryStatusEx memoryStatusEx = default(MemoryStatusEx);
		memoryStatusEx.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
		MemoryStatusEx buffer = memoryStatusEx;
		try
		{
			if (!GlobalMemoryStatusEx(ref buffer))
			{
				AppLogger.LogError("GlobalMemoryStatusEx failed.", null, "global-memory-status-failed", TimeSpan.FromMinutes(10.0));
				return null;
			}
			return new MemoryStatusSnapshot(buffer.TotalPhysical, buffer.AvailablePhysical, buffer.TotalPageFile, buffer.AvailablePageFile, buffer.MemoryLoad);
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			AppLogger.LogError("Global memory status read failed.", ex, "global-memory-status:" + ex.GetType().FullName, TimeSpan.FromMinutes(10.0));
			return null;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
