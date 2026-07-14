using HardwareVision.Services;

namespace HardwareVision.Models;

public sealed class GameSessionRecordInfo
{
    public string GameName { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public long FileSize { get; init; }

    public bool IsComplete { get; init; }

    public string CsvPath { get; init; } = string.Empty;

    public string? SummaryPath { get; init; }

    public GameSessionEndReason EndReason { get; init; }

    public double? EstimatedEnergyWh { get; init; }

    public double? AverageEstimatedPowerWatts { get; init; }

    public double? EnergyCoveragePercent { get; init; }

    public string? EnergyIncludedComponents { get; init; }

    public string StatusText => IsComplete ? "完整" : "未完成";

    public string DurationText => Duration.TotalHours >= 1d
        ? Duration.ToString(@"hh\:mm\:ss")
        : Duration.ToString(@"mm\:ss");

    public string EnergyText => GameEnergyFormatting.FormatEnergy(EstimatedEnergyWh);
}

public sealed class GameSessionRecorderStateChangedEventArgs : EventArgs
{
    public GameSessionRecorderStateChangedEventArgs(
        string statusText,
        bool isRecording,
        string? currentPath,
        long droppedSamples,
        GameSessionRecordInfo? completedRecord = null)
    {
        StatusText = statusText;
        IsRecording = isRecording;
        CurrentPath = currentPath;
        DroppedSamples = droppedSamples;
        CompletedRecord = completedRecord;
    }

    public string StatusText { get; }

    public bool IsRecording { get; }

    public string? CurrentPath { get; }

    public long DroppedSamples { get; }

    public GameSessionRecordInfo? CompletedRecord { get; }
}
