using HardwareVision.Models;

namespace HardwareVision.Services;

internal static class SessionChartDownsampler
{
    public const int DefaultMaximumPoints = 1_500;

    public static IReadOnlyList<SessionChartPoint> Downsample(
        IReadOnlyList<SessionChartPoint> points,
        IReadOnlyList<SessionLimitInterval> intervals,
        int maximumPoints = DefaultMaximumPoints)
    {
        maximumPoints = Math.Max(8, maximumPoints);
        List<SessionChartPoint> normalized = Normalize(points);
        if (normalized.Count <= maximumPoints) return normalized;

        HashSet<int> preserved = new() { 0, normalized.Count - 1 };
        for (int index = 0; index < intervals.Count && preserved.Count + 2 < maximumPoints; index++)
        {
            preserved.Add(FindNearestIndex(normalized, intervals[index].StartSeconds));
            preserved.Add(FindNearestIndex(normalized, intervals[index].EndSeconds));
        }

        int representativeSlots = Math.Max(1, maximumPoints - preserved.Count);
        double firstElapsed = normalized[0].ElapsedSeconds;
        double lastElapsed = normalized[^1].ElapsedSeconds;
        double duration = Math.Max(0.000_001d, lastElapsed - firstElapsed);
        double bucketWidth = duration / representativeSlots;
        List<SessionChartPoint> result = new(maximumPoints + preserved.Count);
        int cursor = 0;
        for (int bucketIndex = 0; bucketIndex < representativeSlots && cursor < normalized.Count; bucketIndex++)
        {
            double bucketEnd = bucketIndex == representativeSlots - 1
                ? double.PositiveInfinity
                : firstElapsed + (bucketIndex + 1) * bucketWidth;
            double elapsedSum = 0d;
            double valueSum = 0d;
            int count = 0;
            bool breakBefore = false;
            while (cursor < normalized.Count && normalized[cursor].ElapsedSeconds < bucketEnd)
            {
                SessionChartPoint point = normalized[cursor++];
                elapsedSum += point.ElapsedSeconds;
                valueSum += point.Value;
                breakBefore |= point.BreakBefore;
                count++;
            }
            if (count > 0)
                result.Add(new SessionChartPoint(elapsedSum / count, valueSum / count, breakBefore));
        }

        foreach (int index in preserved) result.Add(normalized[index]);
        result = Normalize(result, inferGaps: false);
        if (result.Count <= maximumPoints) return result;

        HashSet<double> preservedElapsed = preserved
            .Select(index => normalized[index].ElapsedSeconds)
            .ToHashSet();
        List<SessionChartPoint> capped = result
            .Where(point => preservedElapsed.Contains(point.ElapsedSeconds))
            .ToList();
        int optionalSlots = Math.Max(0, maximumPoints - capped.Count);
        List<SessionChartPoint> optional = result
            .Where(point => !preservedElapsed.Contains(point.ElapsedSeconds))
            .ToList();
        if (optionalSlots > 0 && optional.Count > 0)
        {
            double stride = optional.Count / (double)optionalSlots;
            for (int index = 0; index < optionalSlots; index++)
            {
                int selected = Math.Min(optional.Count - 1, (int)Math.Floor((index + 0.5d) * stride));
                capped.Add(optional[selected]);
            }
        }
        return Normalize(capped, inferGaps: false);
    }

    internal static List<SessionChartPoint> Normalize(
        IReadOnlyList<SessionChartPoint> points,
        bool inferGaps = true)
    {
        List<SessionChartPoint> ordered = new(points.Count);
        for (int index = 0; index < points.Count; index++)
        {
            SessionChartPoint point = points[index];
            if (!double.IsFinite(point.ElapsedSeconds)
                || !double.IsFinite(point.Value)
                || point.ElapsedSeconds < 0d)
            {
                continue;
            }
            ordered.Add(point);
        }
        ordered.Sort(static (left, right) => left.ElapsedSeconds.CompareTo(right.ElapsedSeconds));
        if (ordered.Count == 0) return ordered;

        List<SessionChartPoint> unique = new(ordered.Count);
        int cursor = 0;
        while (cursor < ordered.Count)
        {
            double elapsed = ordered[cursor].ElapsedSeconds;
            double sum = 0d;
            int count = 0;
            bool breakBefore = false;
            do
            {
                sum += ordered[cursor].Value;
                breakBefore |= ordered[cursor].BreakBefore;
                count++;
                cursor++;
            }
            while (cursor < ordered.Count && ordered[cursor].ElapsedSeconds.Equals(elapsed));
            unique.Add(new SessionChartPoint(elapsed, sum / count, breakBefore));
        }

        if (!inferGaps || unique.Count < 3) return unique;
        double[] deltas = new double[unique.Count - 1];
        int deltaCount = 0;
        for (int index = 1; index < unique.Count; index++)
        {
            double delta = unique[index].ElapsedSeconds - unique[index - 1].ElapsedSeconds;
            if (delta > 0d && double.IsFinite(delta)) deltas[deltaCount++] = delta;
        }
        if (deltaCount == 0) return unique;
        Array.Sort(deltas, 0, deltaCount);
        double median = deltas[deltaCount / 2];
        double gapThreshold = Math.Max(median * 3d, median + 0.001d);
        for (int index = 1; index < unique.Count; index++)
        {
            if (unique[index].ElapsedSeconds - unique[index - 1].ElapsedSeconds > gapThreshold)
                unique[index] = unique[index] with { BreakBefore = true };
        }
        return unique;
    }

    private static int FindNearestIndex(IReadOnlyList<SessionChartPoint> points, double elapsedSeconds)
    {
        int low = 0;
        int high = points.Count - 1;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (points[middle].ElapsedSeconds < elapsedSeconds) low = middle + 1;
            else high = middle;
        }
        if (low > 0
            && Math.Abs(points[low - 1].ElapsedSeconds - elapsedSeconds)
                <= Math.Abs(points[low].ElapsedSeconds - elapsedSeconds))
        {
            return low - 1;
        }
        return low;
    }
}
