using HardwareVision.Models;

namespace HardwareVision.Services;

internal sealed class GameFrameSampleStore
{
    private readonly object syncRoot = new();
    private readonly GameFrameSample?[] samples;
    private Guid captureSessionId;
    private int startIndex;
    private int sampleCount;

    public GameFrameSampleStore(int maximumSampleCount)
    {
        samples = new GameFrameSample[Math.Max(1, maximumSampleCount)];
    }

    public void StartSession(Guid sessionId)
    {
        lock (syncRoot)
        {
            captureSessionId = sessionId;
            Array.Clear(samples);
            startIndex = 0;
            sampleCount = 0;
        }
    }

    public bool TryAdd(GameFrameSample sample)
    {
        lock (syncRoot)
        {
            if (sample.CaptureSessionId == Guid.Empty || sample.CaptureSessionId != captureSessionId)
            {
                return false;
            }

            if (sampleCount < samples.Length)
            {
                int writeIndex = (startIndex + sampleCount) % samples.Length;
                samples[writeIndex] = sample;
                sampleCount++;
            }
            else
            {
                samples[startIndex] = sample;
                startIndex = (startIndex + 1) % samples.Length;
            }

            return true;
        }
    }

    public IReadOnlyList<GameFrameSample> Snapshot()
    {
        lock (syncRoot)
        {
            GameFrameSample[] snapshot = new GameFrameSample[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                snapshot[index] = samples[(startIndex + index) % samples.Length]!;
            }

            return snapshot;
        }
    }

    public GamePerformanceSnapshot Calculate(TimeSpan window)
    {
        lock (syncRoot)
        {
            CircularSampleList currentSamples = new(samples, startIndex, sampleCount);
            return GameFrameStatisticsCalculator.Calculate(currentSamples, window, captureSessionId);
        }
    }

    private sealed class CircularSampleList : IReadOnlyList<GameFrameSample>
    {
        private readonly GameFrameSample?[] samples;
        private readonly int startIndex;

        public CircularSampleList(GameFrameSample?[] samples, int startIndex, int count)
        {
            this.samples = samples;
            this.startIndex = startIndex;
            Count = count;
        }

        public int Count { get; }

        public GameFrameSample this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                return samples[(startIndex + index) % samples.Length]!;
            }
        }

        public IEnumerator<GameFrameSample> GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
