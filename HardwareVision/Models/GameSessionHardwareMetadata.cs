namespace HardwareVision.Models;

public sealed class GameSessionHardwareMetadata
{
    public string? OperatingSystem { get; init; }

    public string? CpuName { get; init; }

    public string? GpuName { get; init; }

    public string? GpuDriverVersion { get; init; }

    public string? MotherboardName { get; init; }

    public string? MemoryDescription { get; init; }

    public string? DiskDescription { get; init; }

    public string? DisplayDescription { get; init; }
}
