namespace HardwareVision.Models;

public enum SessionAuxiliaryFileStatus
{
    NotRecorded,
    RecordedNoData,
    Recorded,
    Unavailable
}

public readonly record struct SessionChartPoint(double ElapsedSeconds, double Value);

public sealed class SessionChartSeries
{
    public required string Name { get; init; }

    public required string Unit { get; init; }

    public IReadOnlyList<SessionChartPoint> Points { get; init; } = [];

    public double? Average { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }
}

public sealed class SessionLimitInterval
{
    public long EventId { get; init; }

    public PerformanceLimitProcessorType ProcessorType { get; init; }

    public double StartSeconds { get; init; }

    public double EndSeconds { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public IReadOnlyList<string> RawReasons { get; init; } = [];

    public IReadOnlyList<string> RawIdentifiers { get; init; } = [];

    public int TriggerCount { get; init; }

    public string ToolTip { get; init; } = string.Empty;
}

public sealed class SessionChartModel
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public IReadOnlyList<SessionChartSeries> Series { get; init; } = [];

    public IReadOnlyList<SessionLimitInterval> LimitIntervals { get; init; } = [];

    public double DurationSeconds { get; init; }

    public string EmptyText { get; init; } = "--";

    public bool HasData => Series.Any(series => series.Points.Count > 0) || LimitIntervals.Count > 0;
}

public sealed class GameSessionReport
{
    public required GameSessionRecordInfo Record { get; init; }

    public GameSessionSummary? Summary { get; init; }

    public IReadOnlyList<SessionChartModel> Charts { get; init; } = [];

    public IReadOnlyList<GamePerformanceLimitEvent> PerformanceLimitEvents { get; init; } = [];

    public bool PerformanceLimitEventsLoadedFromLegacySummary { get; init; }

    public SessionAuxiliaryFileStatus PerformanceLimitFileStatus { get; init; }

    public SessionAuxiliaryFileStatus HardwareTimelineFileStatus { get; init; }

    public long ParsedFrameCount { get; init; }

    public double? MinimumFps { get; init; }

    public double? MaximumFps { get; init; }

    public double? LastFps { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsPartial => Warnings.Count > 0 || Summary is null || !Record.IsComplete;
}

public sealed class GameSessionReportMetric
{
    public required string Label { get; init; }

    public required string Value { get; init; }

    public string? ToolTip { get; init; }
}

public sealed class SessionThrottleStatistics
{
    public int EventCount { get; init; }

    public double? LimitedDurationSeconds { get; init; }

    public double? LimitedRatioPercent { get; init; }

    public string? MostCommonReason { get; init; }

    public double? AverageFrequencyMHz { get; init; }

    public double? MinimumFrequencyMHz { get; init; }

    public double? MaximumFrequencyMHz { get; init; }

    public double? LimitedAverageFrequencyMHz { get; init; }

    public double? NormalAverageFrequencyMHz { get; init; }

    public double? DataCoveragePercent { get; init; }

    public bool HasSufficientFrequencyCoverage { get; init; }
}
