using System.Reflection;
using System.Windows.Threading;
using HardwareVision.Models;
using HardwareVision.Services;
using HardwareVision.ViewModels;

namespace HardwareVision.Tests;

internal static class GpuLoadMappingTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("GPU load mapping 01 Intel iGPU preserves zero and nonzero load", IntelGpuPreservesZeroAndNonzeroLoad),
        ("GPU load mapping 02 Intel and NVIDIA identities stay isolated", IntelAndNvidiaStayIsolated),
        ("GPU load mapping 03 canonical load outranks engine channels", CanonicalLoadOutranksEngineChannels),
        ("GPU load mapping 04 missing canonical load does not cross adapters", MissingCanonicalLoadDoesNotCrossAdapters),
        ("GPU load mapping 05 Dashboard and detail use the same normalized load", DashboardAndDetailUseSameNormalizedLoad)
    ];

    private static void IntelGpuPreservesZeroAndNonzeroLoad()
    {
        GpuDevice zero = SingleDevice(
            Reading("Intel UHD Graphics", "D3D 3D", 0d, "/gpu-intel/0/load/0"));
        TestSupport.Equal("/gpu-intel/0", zero.Id, "Intel identity");
        TestSupport.Nearly(0d, zero.CoreLoad?.Value, "zero Intel load");

        GpuDevice active = SingleDevice(
            Reading("Intel UHD Graphics", "D3D 3D", 17d, "/gpu-intel/0/load/0"));
        TestSupport.Nearly(17d, active.CoreLoad?.Value, "active Intel load");
        HardwareMetric projection = HardwareMetricService.FromSensorReading(
            "gpu.load.core", active.Id, HardwareMetricCategory.Gpu, "核心负载", "GPU Core Load",
            active.CoreLoad, "GPU load", true, true, 0, "GPU");
        TestSupport.Equal("17", projection.Value, "Intel load projection");
        TestSupport.Equal(MetricAvailability.Available, projection.Availability, "Intel load availability");
    }

    private static void IntelAndNvidiaStayIsolated()
    {
        IReadOnlyList<GpuDevice> devices = Build(
            Reading("Intel UHD Graphics", "D3D 3D", 17d, "/gpu-intel/0/load/0"),
            Reading("NVIDIA GeForce RTX", "GPU Core", 63d, "/gpu-nvidia/0/load/0"));
        GpuDevice intel = devices.Single(device => device.HardwareType == "GpuIntel");
        GpuDevice nvidia = devices.Single(device => device.HardwareType == "GpuNvidia");

        TestSupport.False(string.Equals(intel.Id, nvidia.Id, StringComparison.OrdinalIgnoreCase), "GPU identities differ");
        TestSupport.Nearly(17d, intel.CoreLoad?.Value, "Intel load");
        TestSupport.Nearly(63d, nvidia.CoreLoad?.Value, "NVIDIA load");
        TestSupport.True(intel.Sensors.All(sensor => sensor.RawIdentifier.StartsWith("/gpu-intel/0/", StringComparison.Ordinal)), "Intel sensor ownership");
        TestSupport.True(nvidia.Sensors.All(sensor => sensor.RawIdentifier.StartsWith("/gpu-nvidia/0/", StringComparison.Ordinal)), "NVIDIA sensor ownership");

        GpuViewModel details = new();
        foreach (GpuDevice device in devices) details.GpuDevices.Add(device);
        details.SelectedGpu = intel;
        TestSupport.Equal(intel.Name, details.GpuName, "Intel selector name");
        TestSupport.Equal("17 %", MetricValue(details, "gpu.load.core"), "Intel selector load");
        details.SelectedGpu = nvidia;
        TestSupport.Equal(nvidia.Name, details.GpuName, "NVIDIA selector name");
        TestSupport.Equal("63 %", MetricValue(details, "gpu.load.core"), "NVIDIA selector load");
    }

    private static void CanonicalLoadOutranksEngineChannels()
    {
        GpuDevice canonical = SingleDevice(
            Reading("Intel Arc Graphics", "D3D Video Decode", 91d, "/gpu-intel/0/load/2"),
            Reading("Intel Arc Graphics", "D3D 3D", 17d, "/gpu-intel/0/load/1"),
            Reading("Intel Arc Graphics", "GPU Core", 23d, "/gpu-intel/0/load/0"));
        TestSupport.Nearly(23d, canonical.CoreLoad?.Value, "canonical GPU Core load");

        GpuDevice d3dFallback = SingleDevice(
            Reading("Intel UHD Graphics", "D3D Copy", 88d, "/gpu-intel/0/load/1"),
            Reading("Intel UHD Graphics", "D3D 3D", 19d, "/gpu-intel/0/load/0"));
        TestSupport.Nearly(19d, d3dFallback.CoreLoad?.Value, "D3D 3D fallback");
    }

    private static void MissingCanonicalLoadDoesNotCrossAdapters()
    {
        IReadOnlyList<GpuDevice> devices = Build(
            Reading("Intel UHD Graphics", "D3D Video Decode", 46d, "/gpu-intel/0/load/0"),
            Reading("NVIDIA GeForce RTX", "GPU Core", 63d, "/gpu-nvidia/0/load/0"));
        GpuDevice intel = devices.Single(device => device.HardwareType == "GpuIntel");
        GpuDevice nvidia = devices.Single(device => device.HardwareType == "GpuNvidia");

        TestSupport.True(intel.CoreLoad is null, "Intel canonical load remains missing");
        TestSupport.Nearly(63d, nvidia.CoreLoad?.Value, "NVIDIA load remains local");
        HardwareMetric projection = HardwareMetricService.FromSensorReading(
            "gpu.load.core", intel.Id, HardwareMetricCategory.Gpu, "核心负载", "GPU Core Load",
            intel.CoreLoad, "GPU load", true, true, 0, "GPU");
        TestSupport.Equal(HardwareMetricService.EmptyValue, projection.Value, "missing load placeholder");
        TestSupport.Equal(MetricAvailability.NotReported, projection.Availability, "missing load availability");
    }

    private static void DashboardAndDetailUseSameNormalizedLoad()
    {
        AppSettings settings = new() { PreferredGpuId = "/gpu-intel/0" };
        using PollingService polling = new(new CountingSensorService(), settings);
        using SensorHistoryService history = new(polling);
        using DashboardViewModel dashboard = new(
            settings,
            new EmptyHardwareInfoService(),
            polling,
            new CountingSettingsService(settings),
            Dispatcher.CurrentDispatcher,
            history);

        SetPrivateProperty(dashboard, nameof(DashboardViewModel.CurrentSensorReadings), new[]
        {
            Reading("Intel UHD Graphics", "D3D 3D", 17d, "/gpu-intel/0/load/0"),
            Reading("NVIDIA GeForce RTX", "GPU Core", 63d, "/gpu-nvidia/0/load/0")
        });
        InvokePrivate(dashboard, "RefreshGpuDevices");
        InvokePrivate(dashboard, "RefreshSummaryCards", DashboardRefreshKind.Sensors);

        using GpuViewModel details = new(dashboard, settings, new CountingSettingsService(settings), history);
        details.SetActive(true);
        DetailMetricViewModel dashboardLoad = dashboard.GpuOverviewCard.Metrics.Single(metric => metric.Id == "dashboard.gpu.load");
        DetailMetricViewModel detailLoad = details.Metrics.Single(metric => metric.Id == "gpu.load.core");
        TestSupport.Equal("17 %", dashboardLoad.Value, "Dashboard normalized Intel load");
        TestSupport.Equal(dashboardLoad.Value, detailLoad.Value, "Dashboard/detail load consistency");
        TestSupport.Equal("/gpu-intel/0", dashboardLoad.Metric?.HardwareId, "Dashboard metric device identity");
        TestSupport.Equal(dashboardLoad.Metric?.HardwareId, detailLoad.Metric?.HardwareId, "Dashboard/detail metric identity consistency");
        TestSupport.True(ReferenceEquals(dashboard.SelectedGpu?.CoreLoad, details.SelectedGpu?.CoreLoad), "shared normalized reading");
    }

    private static IReadOnlyList<GpuDevice> Build(params SensorReading[] readings) =>
        new GpuDeviceService().BuildGpuDevices(null, readings, null);

    private static GpuDevice SingleDevice(params SensorReading[] readings) => Build(readings).Single();

    private static SensorReading Reading(string device, string sensor, double value, string identifier) => new()
    {
        DeviceName = device,
        SensorName = sensor,
        Category = SensorCategory.Gpu,
        Type = SensorType.Load,
        Value = value,
        Unit = "%",
        IsAvailable = true,
        Availability = SensorAvailability.Available,
        Source = "Synthetic LHM",
        RawIdentifier = identifier
    };

    private static string MetricValue(GpuViewModel viewModel, string id) =>
        viewModel.Metrics.Single(metric => metric.Id == id).Value;

    private static void SetPrivateProperty<T>(object target, string name, T value)
    {
        PropertyInfo property = TestSupport.NotNull(target.GetType().GetProperty(name), name);
        property.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name, params object[] arguments)
    {
        MethodInfo method = TestSupport.NotNull(
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic),
            name);
        method.Invoke(target, arguments);
    }

    private sealed class EmptyHardwareInfoService : IHardwareInfoService
    {
        public Task<HardwareSnapshot> GetHardwareSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSnapshot());

        public Task<IReadOnlyList<HardwareDevice>> GetHardwareDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<HardwareDevice>>([]);

        public Task<HardwareSummary> GetHardwareSummaryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSummary("test", "test", null, null, null, null));
    }
}
