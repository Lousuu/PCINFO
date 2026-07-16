namespace HardwareVision.Services;

public sealed record MemoryStatusSnapshot(ulong TotalPhysical, ulong AvailablePhysical, ulong TotalPageFile, ulong AvailablePageFile, uint MemoryLoad);
