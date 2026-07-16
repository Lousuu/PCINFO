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
        if (points.Count <= maximumPoints) return points.ToArray();

        HashSet<int> preserved = new() { 0, points.Count - 1 };
        int minimumIndex = 0;
        int maximumIndex = 0;
        for (int index = 1; index < points.Count; index++)
        {
            if (points[index].Value < points[minimumIndex].Value) minimumIndex = index;
            if (points[index].Value > points[maximumIndex].Value) maximumIndex = index;
        }
        preserved.Add(minimumIndex);
        preserved.Add(maximumIndex);
        for (int index = 0; index < intervals.Count && preserved.Count + 2 < maximumPoints; index++)
        {
            preserved.Add(FindNearestIndex(points, intervals[index].StartSeconds));
            preserved.Add(FindNearestIndex(points, intervals[index].EndSeconds));
        }

        int bucketCount = Math.Max(1, (maximumPoints - preserved.Count) / 3);
        int bucketSize = Math.Max(1, (int)Math.Ceiling(points.Count / (double)bucketCount));
        List<SessionChartPoint> result = new(maximumPoints);
        for (int bucketStart = 0; bucketStart < points.Count; bucketStart += bucketSize)
        {
            int bucketEnd = Math.Min(points.Count, bucketStart + bucketSize);
            int minIndex = bucketStart;
            int maxIndex = bucketStart;
            double elapsedSum = 0d;
            double valueSum = 0d;
            for (int index = bucketStart; index < bucketEnd; index++)
            {
                SessionChartPoint point = points[index];
                if (point.Value < points[minIndex].Value) minIndex = index;
                if (point.Value > points[maxIndex].Value) maxIndex = index;
                elapsedSum += point.ElapsedSeconds;
                valueSum += point.Value;
            }

            result.Add(points[minIndex]);
            if (maxIndex != minIndex) result.Add(points[maxIndex]);
            int count = bucketEnd - bucketStart;
            if (count > 2)
            {
                result.Add(new SessionChartPoint(elapsedSum / count, valueSum / count));
            }
        }

        foreach (int index in preserved) result.Add(points[index]);
        result.Sort(static (left, right) => left.ElapsedSeconds.CompareTo(right.ElapsedSeconds));
        Deduplicate(result);
        if (result.Count <= maximumPoints) return result;

        // Bucket sizing normally keeps the result below the cap. If many preserved event
        // boundaries cause an overflow, retain those boundaries and evenly thin only the rest.
        List<SessionChartPoint> capped = new(maximumPoints);
        HashSet<SessionChartPoint> preservedPoints = new();
        foreach (int index in preserved) preservedPoints.Add(points[index]);
        capped.AddRange(preservedPoints);
        int optionalSlots = Math.Max(0, maximumPoints - capped.Count);
        int optionalCount = 0;
        for (int index = 0; index < result.Count; index++)
        {
            if (!preservedPoints.Contains(result[index])) optionalCount++;
        }
        if (optionalSlots > 0 && optionalCount > 0)
        {
            double stride = optionalCount / (double)optionalSlots;
            double next = stride / 2d;
            int optionalIndex = 0;
            for (int index = 0; index < result.Count && capped.Count < maximumPoints; index++)
            {
                SessionChartPoint point = result[index];
                if (preservedPoints.Contains(point)) continue;
                if (optionalIndex >= next)
                {
                    capped.Add(point);
                    next += stride;
                }
                optionalIndex++;
            }
        }

        capped.Sort(static (left, right) => left.ElapsedSeconds.CompareTo(right.ElapsedSeconds));
        Deduplicate(capped);
        return capped;
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

    private static void Deduplicate(List<SessionChartPoint> points)
    {
        int write = 0;
        for (int read = 0; read < points.Count; read++)
        {
            if (write > 0
                && points[read].ElapsedSeconds.Equals(points[write - 1].ElapsedSeconds)
                && points[read].Value.Equals(points[write - 1].Value))
            {
                continue;
            }

            points[write++] = points[read];
        }
        if (write < points.Count) points.RemoveRange(write, points.Count - write);
    }
}
