namespace HardwareVision.Models;

public sealed record HardwareSummary(string ComputerName, string OperatingSystem, string? CpuName, string? GpuName, string? MotherboardName, string? MemorySummary);
