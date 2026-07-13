using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class SensorAggregatorService : ISensorService, IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<ISensorProvider> providers;
    private readonly SemaphoreSlim providerLock = new(1, 1);
    private bool isInitialized;
    private bool isDisposed;

    public SensorAggregatorService(IEnumerable<ISensorProvider> providers)
    {
        this.providers = providers.OrderByDescending(static provider => provider.Priority).ToArray();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await providerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (isInitialized)
            {
                return;
            }

            foreach (ISensorProvider provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AppLogger.LogError(
                        $"Sensor provider initialization failed: {provider.Name}.",
                        exception,
                        $"sensor-provider-init:{provider.Name}:{exception.GetType().FullName}",
                        TimeSpan.FromMinutes(10));
                }
            }

            isInitialized = true;
        }
        finally
        {
            providerLock.Release();
        }
    }

    public async Task<IReadOnlyList<SensorReading>> GetCurrentReadingsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await providerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<SensorReading> merged = new(256);
            Dictionary<SensorIdentity, int> indices = new(SensorIdentityComparer.Instance);
            foreach (ISensorProvider provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    IReadOnlyList<SensorReading> providerReadings = provider is IConditionalSensorProvider conditional
                        ? await conditional.GetReadingsAsync(merged, cancellationToken).ConfigureAwait(false)
                        : await provider.GetReadingsAsync(cancellationToken).ConfigureAwait(false);
                    MergeProviderReadings(providerReadings, merged, indices);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    AppLogger.LogError(
                        $"Sensor provider read failed: {provider.Name}.",
                        exception,
                        $"sensor-provider-read:{provider.Name}:{exception.GetType().FullName}");
                }
            }

            return merged;
        }
        finally
        {
            providerLock.Release();
        }
    }

    public Task<IReadOnlyList<SensorReading>> GetSensorReadingsAsync(CancellationToken cancellationToken = default)
    {
        return GetCurrentReadingsAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        foreach (ISensorProvider provider in providers)
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        providerLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        foreach (ISensorProvider provider in providers)
        {
            if (provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        providerLock.Dispose();
    }

    private static void MergeProviderReadings(
        IReadOnlyList<SensorReading> providerReadings,
        List<SensorReading> merged,
        Dictionary<SensorIdentity, int> indices)
    {
        for (int index = 0; index < providerReadings.Count; index++)
        {
            SensorReading reading = providerReadings[index];
            SensorIdentity identity = new(
                reading.Category,
                reading.DeviceName.Trim(),
                reading.Type,
                reading.SensorName.Trim());
            if (!indices.TryGetValue(identity, out int existingIndex))
            {
                indices.Add(identity, merged.Count);
                merged.Add(reading);
            }
            else if (!merged[existingIndex].IsAvailable && reading.IsAvailable)
            {
                merged[existingIndex] = reading;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}
