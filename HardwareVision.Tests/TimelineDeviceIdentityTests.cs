using HardwareVision.Models;
using HardwareVision.Services;

namespace HardwareVision.Tests;

internal static class TimelineDeviceIdentityTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Timeline identity 01 NVML state does not create GPU", NvmlStateDoesNotCreateGpu),
        ("Timeline identity 02 LHM plus NVML creates one GPU", LhmPlusNvmlCreatesOneGpu),
        ("Timeline identity 03 different GPU roots stay separate", DifferentRootsStaySeparate),
        ("Timeline identity 04 same model GPUs stay separate", SameModelGpusStaySeparate),
        ("Timeline identity 05 stable UUID PCI and PNP keys", StableKeysAreExtracted),
        ("Timeline identity 06 scoped event applies to one GPU", ScopedEventAppliesToOneGpu),
        ("Timeline identity 07 unscoped legacy event is conservative", UnscopedLegacyEventIsConservative)
    ];

    private static void NvmlStateDoesNotCreateGpu()
    {
        IReadOnlyList<GameHardwareTimelineSample> samples = Create([Reading("RTX", "GPU Performance Limit", SensorType.State, "/nvml/gpu/0/state")]);
        TestSupport.False(samples.Any(sample => sample.DeviceType == GameTimelineDeviceType.Gpu), "state-only GPU was created");
    }

    private static void LhmPlusNvmlCreatesOneGpu()
    {
        IReadOnlyList<GameHardwareTimelineSample> samples = Create(
        [
            Reading("RTX", "GPU Core", SensorType.Clock, "/gpu/0/clock/0"),
            Reading("RTX", "GPU Performance Limit", SensorType.State, "/nvml/gpu/0/state")
        ]);
        TestSupport.Equal(1, samples.Count(sample => sample.DeviceType == GameTimelineDeviceType.Gpu), "GPU device count");
    }

    private static void DifferentRootsStaySeparate()
    {
        IReadOnlyList<GameHardwareTimelineSample> samples = Create(
        [
            Reading("GPU A", "GPU Core", SensorType.Clock, "/gpu/0/clock/0"),
            Reading("GPU B", "GPU Core", SensorType.Clock, "/gpu/1/clock/0")
        ]);
        TestSupport.Equal(2, samples.Count(sample => sample.DeviceType == GameTimelineDeviceType.Gpu), "different GPU count");
    }

    private static void SameModelGpusStaySeparate()
    {
        GameHardwareTimelineSample[] gpus = Create(
        [
            Reading("RTX 4060", "GPU Core", SensorType.Clock, "/gpu/0/clock/0"),
            Reading("RTX 4060", "GPU Core", SensorType.Clock, "/gpu/1/clock/0")
        ]).Where(sample => sample.DeviceType == GameTimelineDeviceType.Gpu).ToArray();
        TestSupport.Equal(2, gpus.Length, "same-model GPU count");
        TestSupport.False(string.Equals(gpus[0].DeviceId, gpus[1].DeviceId, StringComparison.OrdinalIgnoreCase), "same-model IDs merged");
    }

    private static void StableKeysAreExtracted()
    {
        TestSupport.Equal("uuid:GPU-12345678-ABCD", GpuDeviceIdentity.TryExtractStableKey("nvml/GPU-12345678-abcd/state"), "UUID key");
        TestSupport.Equal("pci:0000:01:00.0", GpuDeviceIdentity.TryExtractStableKey("pci 0000:01:00.0"), "PCI key");
        TestSupport.True(GpuDeviceIdentity.TryExtractStableKey("PCI\\VEN_10DE&DEV_1234")?.StartsWith("pnp:", StringComparison.Ordinal) == true, "PNP key");
        TestSupport.Equal("gpu-index:1", GpuDeviceIdentity.TryExtractStableKey("/nvml/1/performance-limit/0x4"), "NVML index fallback");
        TestSupport.Equal("gpu-index:1", GpuDeviceIdentity.TryExtractStableKey("/gpu-nvidia/1/clock/0"), "LHM index fallback");
    }

    private static void ScopedEventAppliesToOneGpu()
    {
        Guid sessionId = Guid.NewGuid();
        GameSessionStartInfo start = TestSupport.StartInfo(sessionId);
        GamePerformanceLimitSnapshot limits = new()
        {
            CaptureSessionId = sessionId,
            Generation = start.Generation,
            GpuSupportStatus = PerformanceLimitSupportStatus.ActiveLimit,
            Events =
            [
                new GamePerformanceLimitEvent
                {
                    CaptureSessionId = sessionId,
                    Generation = start.Generation,
                    ProcessorType = PerformanceLimitProcessorType.Gpu,
                    DeviceId = "gpu-index:1",
                    IsActive = true,
                    Reasons = ["Power Limit"]
                }
            ]
        };
        GameHardwareTimelineSample[] gpus = GameHardwareTimelineSampler.CreateSamples(
            start,
            [
                Reading("RTX", "GPU Core", SensorType.Clock, "/gpu/0/clock/0"),
                Reading("RTX", "GPU Core", SensorType.Clock, "/gpu/1/clock/0")
            ],
            limits,
            DateTimeOffset.UtcNow,
            1d).Where(sample => sample.DeviceType == GameTimelineDeviceType.Gpu).ToArray();
        TestSupport.Equal(1, gpus.Count(sample => sample.GpuLimitActive == true), "scoped event target count");
        TestSupport.Equal("gpu-index:1", gpus.Single(sample => sample.GpuLimitActive == true).DeviceId, "wrong GPU received event");
    }

    private static void UnscopedLegacyEventIsConservative()
    {
        Guid sessionId = Guid.NewGuid();
        GameSessionStartInfo start = TestSupport.StartInfo(sessionId);
        GamePerformanceLimitSnapshot limits = new()
        {
            CaptureSessionId = sessionId,
            Generation = start.Generation,
            GpuSupportStatus = PerformanceLimitSupportStatus.ActiveLimit,
            Events =
            [
                new GamePerformanceLimitEvent
                {
                    CaptureSessionId = sessionId,
                    Generation = start.Generation,
                    ProcessorType = PerformanceLimitProcessorType.Gpu,
                    IsActive = true,
                    Reasons = ["Power Limit"]
                }
            ]
        };
        GameHardwareTimelineSample[] gpus = GameHardwareTimelineSampler.CreateSamples(
            start,
            [
                Reading("RTX", "GPU Core", SensorType.Clock, "/gpu/0/clock/0"),
                Reading("RTX", "GPU Core", SensorType.Clock, "/gpu/1/clock/0")
            ],
            limits,
            DateTimeOffset.UtcNow,
            1d).Where(sample => sample.DeviceType == GameTimelineDeviceType.Gpu).ToArray();
        TestSupport.Equal(0, gpus.Count(sample => sample.GpuLimitActive == true), "legacy event covered every GPU");
    }

    private static IReadOnlyList<GameHardwareTimelineSample> Create(IReadOnlyList<SensorReading> readings) =>
        GameHardwareTimelineSampler.CreateSamples(
            TestSupport.StartInfo(),
            readings,
            GamePerformanceLimitSnapshot.Empty,
            DateTimeOffset.UtcNow,
            1d);

    private static SensorReading Reading(string device, string sensor, SensorType type, string id) => new()
    {
        DeviceName = device,
        SensorName = sensor,
        Category = SensorCategory.Gpu,
        Type = type,
        Value = type == SensorType.State ? 1d : 1500d,
        IsAvailable = true,
        Availability = SensorAvailability.Available,
        RawIdentifier = id,
        Source = "synthetic"
    };
}
