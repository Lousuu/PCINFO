using HardwareVision.Models;

namespace HardwareVision.Services;

public static class SessionThrottleStatisticsCalculator
{
    private const double MinimumCoverageRatio = 0.5d;

    public static SessionThrottleStatistics Calculate(
        SessionChartSeries? frequencySeries,
        IReadOnlyList<SessionLimitInterval> intervals,
        double durationSeconds) =>
        CalculateRaw(frequencySeries?.Points ?? [], intervals, durationSeconds);

    public static SessionThrottleStatistics CalculateRaw(
        IReadOnlyList<SessionChartPoint> rawPoints,
        IReadOnlyList<SessionLimitInterval> intervals,
        double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(rawPoints);
        ArgumentNullException.ThrowIfNull(intervals);
        double boundedDuration = Math.Max(0d, durationSeconds);
        double limitedDuration = CalculateUnionDuration(intervals, boundedDuration);
        double? limitedRatio = boundedDuration > 0d
            ? Math.Clamp(limitedDuration / boundedDuration * 100d, 0d, 100d)
            : null;

        List<SessionChartPoint> points = rawPoints
            .Where(static point => double.IsFinite(point.ElapsedSeconds) && double.IsFinite(point.Value))
            .OrderBy(static point => point.ElapsedSeconds)
            .ToList();
        double? expectedInterval = EstimateExpectedInterval(points);
        double gapThreshold = expectedInterval.HasValue
            ? Math.Max(expectedInterval.Value * 2.5d, expectedInterval.Value + 0.25d)
            : 0d;
        double coverage = 0d;
        double limitedCoverage = 0d;
        double normalCoverage = 0d;
        double integral = 0d;
        double limitedIntegral = 0d;
        double normalIntegral = 0d;
        double largestGap = 0d;
        int gapCount = 0;
        for (int index = 1; index < points.Count; index++)
        {
            SessionChartPoint left = points[index - 1];
            SessionChartPoint right = points[index];
            double delta = right.ElapsedSeconds - left.ElapsedSeconds;
            if (delta <= 0d) continue;
            largestGap = Math.Max(largestGap, delta);
            if (gapThreshold <= 0d || delta > gapThreshold)
            {
                gapCount++;
                continue;
            }

            coverage += delta;
            integral += (left.Value + right.Value) * 0.5d * delta;
            IntegrateClassifiedSegment(
                left,
                right,
                intervals,
                ref limitedCoverage,
                ref normalCoverage,
                ref limitedIntegral,
                ref normalIntegral);
        }

        double coveragePercent = boundedDuration > 0d
            ? Math.Clamp(coverage / boundedDuration * 100d, 0d, 100d)
            : 0d;
        bool sufficient = points.Count >= 2 && coveragePercent >= MinimumCoverageRatio * 100d;
        double? minimum = points.Count > 0 ? points.Min(static point => point.Value) : null;
        double? maximum = points.Count > 0 ? points.Max(static point => point.Value) : null;
        return new SessionThrottleStatistics
        {
            EventCount = intervals.Count,
            LimitedDurationSeconds = intervals.Count > 0 ? limitedDuration : 0d,
            LimitedRatioPercent = limitedRatio,
            MostCommonReason = FindMostCommonReason(intervals),
            AverageFrequencyMHz = sufficient && coverage > 0d ? integral / coverage : null,
            MinimumFrequencyMHz = sufficient ? minimum : null,
            MaximumFrequencyMHz = sufficient ? maximum : null,
            LimitedAverageFrequencyMHz = sufficient && limitedCoverage > 0d
                ? limitedIntegral / limitedCoverage
                : null,
            NormalAverageFrequencyMHz = sufficient && normalCoverage > 0d
                ? normalIntegral / normalCoverage
                : null,
            DataCoveragePercent = points.Count > 0 ? coveragePercent : null,
            HasSufficientFrequencyCoverage = sufficient,
            ExpectedSamplingIntervalSeconds = expectedInterval,
            RawCoverageSeconds = points.Count > 0 ? coverage : null,
            LargestGapSeconds = points.Count > 1 ? largestGap : null,
            GapCount = gapCount,
            LimitedCoverageSeconds = points.Count > 0 ? limitedCoverage : null,
            NormalCoverageSeconds = points.Count > 0 ? normalCoverage : null,
            CoverageMode = expectedInterval.HasValue ? "MedianIntervalGapAware" : "InsufficientSamples"
        };
    }

    private static void IntegrateClassifiedSegment(
        SessionChartPoint left,
        SessionChartPoint right,
        IReadOnlyList<SessionLimitInterval> intervals,
        ref double limitedCoverage,
        ref double normalCoverage,
        ref double limitedIntegral,
        ref double normalIntegral)
    {
        List<double> boundaries = [left.ElapsedSeconds, right.ElapsedSeconds];
        for (int index = 0; index < intervals.Count; index++)
        {
            if (intervals[index].StartSeconds > left.ElapsedSeconds && intervals[index].StartSeconds < right.ElapsedSeconds)
                boundaries.Add(intervals[index].StartSeconds);
            if (intervals[index].EndSeconds > left.ElapsedSeconds && intervals[index].EndSeconds < right.ElapsedSeconds)
                boundaries.Add(intervals[index].EndSeconds);
        }

        boundaries.Sort();
        for (int index = 1; index < boundaries.Count; index++)
        {
            double start = boundaries[index - 1];
            double end = boundaries[index];
            double delta = end - start;
            if (delta <= 0d) continue;
            double startValue = Interpolate(left, right, start);
            double endValue = Interpolate(left, right, end);
            double segmentIntegral = (startValue + endValue) * 0.5d * delta;
            if (IsLimited((start + end) * 0.5d, intervals))
            {
                limitedCoverage += delta;
                limitedIntegral += segmentIntegral;
            }
            else
            {
                normalCoverage += delta;
                normalIntegral += segmentIntegral;
            }
        }
    }

    private static double Interpolate(SessionChartPoint left, SessionChartPoint right, double elapsed)
    {
        double width = right.ElapsedSeconds - left.ElapsedSeconds;
        if (width <= 0d) return left.Value;
        double ratio = Math.Clamp((elapsed - left.ElapsedSeconds) / width, 0d, 1d);
        return left.Value + ((right.Value - left.Value) * ratio);
    }

    private static double? EstimateExpectedInterval(IReadOnlyList<SessionChartPoint> points)
    {
        if (points.Count < 2) return null;
        List<double> deltas = new(points.Count - 1);
        for (int index = 1; index < points.Count; index++)
        {
            double delta = points[index].ElapsedSeconds - points[index - 1].ElapsedSeconds;
            if (delta > 0d && double.IsFinite(delta)) deltas.Add(delta);
        }

        if (deltas.Count == 0) return null;
        deltas.Sort();
        int middle = deltas.Count / 2;
        return deltas.Count % 2 == 0
            ? (deltas[middle - 1] + deltas[middle]) * 0.5d
            : deltas[middle];
    }

    private static bool IsLimited(double elapsedSeconds, IReadOnlyList<SessionLimitInterval> intervals)
    {
        for (int index = 0; index < intervals.Count; index++)
        {
            if (elapsedSeconds >= intervals[index].StartSeconds && elapsedSeconds <= intervals[index].EndSeconds)
                return true;
        }
        return false;
    }

    private static double CalculateUnionDuration(IReadOnlyList<SessionLimitInterval> intervals, double durationSeconds)
    {
        if (intervals.Count == 0) return 0d;
        List<(double Start, double End)> ranges = new(intervals.Count);
        for (int index = 0; index < intervals.Count; index++)
        {
            double start = Math.Clamp(intervals[index].StartSeconds, 0d, durationSeconds);
            double end = Math.Clamp(intervals[index].EndSeconds, start, durationSeconds);
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
            }
            else
            {
                total += currentEnd - currentStart;
                currentStart = ranges[index].Start;
                currentEnd = ranges[index].End;
            }
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
            for (int reasonIndex = 0; reasonIndex < intervals[intervalIndex].Reasons.Count; reasonIndex++)
            {
                string reason = intervals[intervalIndex].Reasons[reasonIndex];
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
