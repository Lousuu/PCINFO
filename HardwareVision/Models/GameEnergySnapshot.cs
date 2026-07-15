namespace HardwareVision.Models;

public sealed class GameEnergySnapshot
{
    public static GameEnergySnapshot Empty { get; } = new();

    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public bool IsTracking { get; init; }

    public double? EstimatedEnergyWh { get; init; }

    public double? CpuEstimatedEnergyWh { get; init; }

    public double? GpuEstimatedEnergyWh { get; init; }

    public double? CurrentEstimatedPowerWatts { get; init; }

    public double? AverageEstimatedPowerWatts { get; init; }

    public double? CpuAverageEstimatedPowerWatts { get; init; }

    public double? GpuAverageEstimatedPowerWatts { get; init; }

    public TimeSpan SessionDuration { get; init; }

    public TimeSpan ValidIntegrationDuration { get; init; }

    public double? CoveragePercent { get; init; }

    public string? IncludedComponents { get; init; }

    public double? AverageCpuLoadPercent { get; init; }

    public double? AverageCpuTemperatureCelsius { get; init; }

    public double? AverageGpuLoadPercent { get; init; }

    public double? AverageGpuTemperatureCelsius { get; init; }

    public double? AverageMemoryLoadPercent { get; init; }
}
