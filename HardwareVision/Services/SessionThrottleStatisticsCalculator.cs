using HardwareVision.Models;

namespace HardwareVision.Services;

public static class SessionThrottleStatisticsCalculator
{
    private const double MinimumCoverageRatio = 0.5d;

    public static SessionThrottleStatistics Calculate(
        SessionChartSeries? frequencySeries,
        IReadOnlyList<SessionLimitInterval> intervals,
        double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        double limitedDuration = CalculateUnionDuration(intervals, durationSeconds);
        double? ratio = durationSeconds > 0d
            ? Math.Clamp(limitedDuration / durationSeconds * 100d, 0d, 100d)
            : null;

        IReadOnlyList<SessionChartPoint> points = frequencySeries?.Points ?? [];
        double coverage = CalculateCoverage(points, durationSeconds);
        bool sufficient = points.Count >= 2 && coverage >= MinimumCoverageRatio * 100d;
        double limitedSum = 0d;
        double normalSum = 0d;
        int limitedCount = 0;
        int normalCount = 0;
        for (int index = 0; index < points.Count; index++)
        {
            SessionChartPoint point = points[index];
            if (!double.IsFinite(point.Value)) continue;
            if (IsLimited(point.ElapsedSeconds, intervals))
            {
                limitedSum += point.Value;
                limitedCount++;
            }
            else
            {
                normalSum += point.Value;
                normalCount++;
            }
        }

        return new SessionThrottleStatistics
        {
            EventCount = intervals.Count,
            LimitedDurationSeconds = intervals.Count > 0 ? limitedDuration : 0d,
            LimitedRatioPercent = ratio,
            MostCommonReason = FindMostCommonReason(intervals),
            AverageFrequencyMHz = sufficient ? frequencySeries?.Average : null,
            MinimumFrequencyMHz = sufficient ? frequencySeries?.Minimum : null,
            MaximumFrequencyMHz = sufficient ? frequencySeries?.Maximum : null,
            LimitedAverageFrequencyMHz = sufficient && limitedCount > 0 ? limitedSum / limitedCount : null,
            NormalAverageFrequencyMHz = sufficient && normalCount > 0 ? normalSum / normalCount : null,
            DataCoveragePercent = points.Count > 0 ? coverage : null,
            HasSufficientFrequencyCoverage = sufficient
        };
    }

    private static double CalculateCoverage(IReadOnlyList<SessionChartPoint> points, double durationSeconds)
    {
        if (points.Count < 2 || durationSeconds <= 0d) return 0d;
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        for (int index = 0; index < points.Count; index++)
        {
            minimum = Math.Min(minimum, points[index].ElapsedSeconds);
            maximum = Math.Max(maximum, points[index].ElapsedSeconds);
        }
        return Math.Clamp((maximum - minimum) / durationSeconds * 100d, 0d, 100d);
    }

    private static bool IsLimited(double elapsedSeconds, IReadOnlyList<SessionLimitInterval> intervals)
    {
        for (int index = 0; index < intervals.Count; index++)
        {
            if (elapsedSeconds >= intervals[index].StartSeconds && elapsedSeconds <= intervals[index].EndSeconds) return true;
        }
        return false;
    }

    private static double CalculateUnionDuration(IReadOnlyList<SessionLimitInterval> intervals, double durationSeconds)
    {
        if (intervals.Count == 0) return 0d;
        List<(double Start, double End)> ranges = new(intervals.Count);
        for (int index = 0; index < intervals.Count; index++)
        {
            double start = Math.Clamp(intervals[index].StartSeconds, 0d, Math.Max(0d, durationSeconds));
            double end = Math.Clamp(intervals[index].EndSeconds, start, Math.Max(start, durationSeconds));
            ranges.Add((start, end));
        }
        ranges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        double total = 0d;
        double currentStart = ranges[0].Start;
        double currentEnd = ranges[0].End;
        for (int index = 1; index < ranges.Count; index++)
        {
            if (ranges[index].Start <= currentEnd)
            {
                currentEnd = Math.Max(currentEnd, ranges[index].End);
                continue;
            }
            total += currentEnd - currentStart;
            currentStart = ranges[index].Start;
            currentEnd = ranges[index].End;
        }
        return total + currentEnd - currentStart;
    }

    private static string? FindMostCommonReason(IReadOnlyList<SessionLimitInterval> intervals)
    {
        Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
        string? best = null;
        int bestCount = 0;
        for (int intervalIndex = 0; intervalIndex < intervals.Count; intervalIndex++)
        {
            IReadOnlyList<string> reasons = intervals[intervalIndex].Reasons;
            for (int reasonIndex = 0; reasonIndex < reasons.Count; reasonIndex++)
            {
                string reason = reasons[reasonIndex];
                if (string.IsNullOrWhiteSpace(reason)) continue;
                int count = counts.TryGetValue(reason, out int existing) ? existing + 1 : 1;
                counts[reason] = count;
                if (count > bestCount)
                {
                    best = reason;
                    bestCount = count;
                }
            }
        }
        return best;
    }
}
