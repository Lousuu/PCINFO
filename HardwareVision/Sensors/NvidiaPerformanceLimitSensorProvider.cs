using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.Utilities;

namespace HardwareVision.Sensors;

public sealed class NvidiaPerformanceLimitSensorProvider : ISensorProvider, IDisposable
{
    private const int NvmlSuccess = 0;
    private const ulong KnownReasonMask = 0x1FF;
    private readonly List<DeviceState> devices = [];
    private nint libraryHandle;
    private NvmlShutdownDelegate? shutdown;
    private NvmlGetThrottleReasonsDelegate? getThrottleReasons;
    private bool nvmlInitialized;
    private bool isDisposed;

    public string Name => "NVIDIA NVML";

    public bool IsAvailable { get; private set; }

    public int Priority => 40;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (nvmlInitialized || libraryHandle != 0)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!TryLoadNvml(out libraryHandle))
            {
                IsAvailable = false;
                return Task.CompletedTask;
            }

            NvmlInitDelegate? initialize = GetDelegate<NvmlInitDelegate>("nvmlInit_v2", "nvmlInit");
            shutdown = GetDelegate<NvmlShutdownDelegate>("nvmlShutdown");
            NvmlGetDeviceCountDelegate? getDeviceCount = GetDelegate<NvmlGetDeviceCountDelegate>("nvmlDeviceGetCount_v2", "nvmlDeviceGetCount");
            NvmlGetDeviceHandleDelegate? getDeviceHandle = GetDelegate<NvmlGetDeviceHandleDelegate>("nvmlDeviceGetHandleByIndex_v2", "nvmlDeviceGetHandleByIndex");
            NvmlGetDeviceNameDelegate? getDeviceName = GetDelegate<NvmlGetDeviceNameDelegate>("nvmlDeviceGetName");
            getThrottleReasons = GetDelegate<NvmlGetThrottleReasonsDelegate>(
                "nvmlDeviceGetCurrentClocksEventReasons",
                "nvmlDeviceGetCurrentClocksThrottleReasons");
            if (initialize is null
                || shutdown is null
                || getDeviceCount is null
                || getDeviceHandle is null
                || getThrottleReasons is null
                || initialize() != NvmlSuccess)
            {
                ReleaseLibrary();
                IsAvailable = false;
                return Task.CompletedTask;
            }

            nvmlInitialized = true;
            if (getDeviceCount(out uint count) != NvmlSuccess)
            {
                ReleaseLibrary();
                IsAvailable = false;
                return Task.CompletedTask;
            }

            for (uint index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (getDeviceHandle(index, out nint handle) != NvmlSuccess || handle == 0)
                {
                    continue;
                }

                string name = $"NVIDIA GPU {index + 1}";
                if (getDeviceName is not null)
                {
                    StringBuilder buffer = new(96);
                    if (getDeviceName(handle, buffer, (uint)buffer.Capacity) == NvmlSuccess
                        && !string.IsNullOrWhiteSpace(buffer.ToString()))
                    {
                        name = buffer.ToString().Trim();
                    }
                }

                devices.Add(new DeviceState(index, handle, name));
            }

            IsAvailable = devices.Count > 0;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppLogger.LogError(
                "NVIDIA NVML performance limit provider initialization failed.",
                exception,
                $"nvml-performance-limit-init:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(10));
            ReleaseLibrary();
            IsAvailable = false;
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SensorReading>> GetReadingsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!IsAvailable || getThrottleReasons is null)
        {
            return [];
        }

        List<SensorReading> readings = [];
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (DeviceState device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (getThrottleReasons(device.Handle, out ulong reasons) == NvmlSuccess)
            {
                readings.AddRange(CreateReadings(device.Name, device.Index, reasons, now));
            }
        }

        return readings;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        ReleaseLibrary();
    }

    internal static IReadOnlyList<SensorReading> CreateReadings(
        string deviceName,
        uint deviceIndex,
        ulong reasonMask,
        DateTimeOffset timestamp)
    {
        if (reasonMask == 0)
        {
            return [];
        }

        (ulong Mask, string Label)[] knownReasons =
        [
            (0x001, "GPU Performance Limit - Utilization / Idle"),
            (0x002, "GPU Performance Limit - Application Clock Setting"),
            (0x004, "GPU Performance Limit - Software Power Cap"),
            (0x008, "GPU Performance Limit - Hardware Slowdown"),
            (0x010, "GPU Performance Limit - Sync Boost"),
            (0x020, "GPU Performance Limit - Software Thermal Slowdown"),
            (0x040, "GPU Performance Limit - Hardware Thermal Slowdown"),
            (0x080, "GPU Performance Limit - Hardware Power Brake"),
            (0x100, "GPU Performance Limit - Display Clock Setting")
        ];
        List<SensorReading> readings = [];
        foreach ((ulong mask, string label) in knownReasons)
        {
            if ((reasonMask & mask) != 0)
            {
                readings.Add(CreateReading(deviceName, deviceIndex, mask, label, reasonMask, timestamp));
            }
        }

        ulong unknown = reasonMask & ~KnownReasonMask;
        for (int bitIndex = 9; bitIndex < 64 && unknown != 0; bitIndex++)
        {
            ulong bit = 1UL << bitIndex;
            if ((unknown & bit) != 0)
            {
                readings.Add(CreateReading(
                    deviceName,
                    deviceIndex,
                    bit,
                    $"GPU Performance Limit - NVML Flag 0x{bit:X}",
                    reasonMask,
                    timestamp));
                unknown &= ~bit;
            }
        }

        return readings;
    }

    private static SensorReading CreateReading(
        string deviceName,
        uint deviceIndex,
        ulong bit,
        string label,
        ulong allReasons,
        DateTimeOffset timestamp)
    {
        return new SensorReading
        {
            DeviceName = deviceName,
            SensorName = label,
            Category = SensorCategory.Gpu,
            Type = SensorType.State,
            Value = 1d,
            Unit = "state",
            Status = HardwareStatus.Warning,
            Timestamp = timestamp,
            IsAvailable = true,
            Source = "NVIDIA NVML",
            Availability = SensorAvailability.Available,
            RawIdentifier = $"/nvml/{deviceIndex}/performance-limit/0x{bit:X}",
            LastUpdated = timestamp,
            ErrorMessage = $"NVML clocks event reason mask=0x{allReasons:X}"
        };
    }

    private static bool TryLoadNvml(out nint handle)
    {
        Assembly assembly = typeof(NvidiaPerformanceLimitSensorProvider).Assembly;
        if (NativeLibrary.TryLoad("nvml.dll", assembly, DllImportSearchPath.SafeDirectories, out handle))
        {
            return true;
        }

        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "nvml.dll")
        ];
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out handle))
            {
                return true;
            }
        }

        handle = 0;
        return false;
    }

    private T? GetDelegate<T>(params string[] exportNames)
        where T : Delegate
    {
        foreach (string exportName in exportNames)
        {
            if (NativeLibrary.TryGetExport(libraryHandle, exportName, out nint address))
            {
                return Marshal.GetDelegateForFunctionPointer<T>(address);
            }
        }

        return null;
    }

    private void ReleaseLibrary()
    {
        devices.Clear();
        if (nvmlInitialized)
        {
            try
            {
                shutdown?.Invoke();
            }
            catch
            {
            }
        }

        nvmlInitialized = false;
        shutdown = null;
        getThrottleReasons = null;
        if (libraryHandle != 0)
        {
            NativeLibrary.Free(libraryHandle);
            libraryHandle = 0;
        }
    }

    private sealed record DeviceState(uint Index, nint Handle, string Name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetDeviceCountDelegate(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetDeviceHandleDelegate(uint index, out nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int NvmlGetDeviceNameDelegate(nint device, StringBuilder name, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlGetThrottleReasonsDelegate(nint device, out ulong reasons);
}
