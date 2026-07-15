namespace HardwareVision.Models;

public sealed class GameEnergySnapshot
{
    public static GameEnergySnapshot Empty { get; } = new();

    public Guid CaptureSessionId { get; init; }

    public int Generation { get; init; }

    public bool IsTracking { get; init; }

    public double? EstimatedEnergyWh { get; init; }

    public double? CurrentEstimatedPowerWatts { get; init; }

    public double? AverageEstimatedPowerWatts { get; init; }

    public TimeSpan SessionDuration { get; init; }

    public TimeSpan ValidIntegrationDuration { get; init; }

    public double? CoveragePercent { get; init; }

    public string? IncludedComponents { get; init; }
}
