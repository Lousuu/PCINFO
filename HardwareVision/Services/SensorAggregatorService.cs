using System.Diagnostics;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class SensorAggregatorService : ISensorService, IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<ISensorProvider> providers;
    private readonly SemaphoreSlim providerLock = new(1, 1);
    private readonly Dictionary<SensorIdentity, int> mergeIndices = new(SensorIdentityComparer.Instance);
    private bool isInitialized;
    private bool isDisposed;
    private int initializeDiagnosticsStarted;

    public SensorAggregatorService(IEnumerable<ISensorProvider> providers)
    {
        this.providers = providers.OrderByDescending(static provider => provider.Priority).ToArray();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        bool recordDiagnostics = Interlocked.CompareExchange(
            ref initializeDiagnosticsStarted,
            1,
            0) == 0;
        Stopwatch totalClock = Stopwatch.StartNew();
        if (recordDiagnostics)
        {
            LogStartupDiagnostic(
                "SensorAggregatorInitializeRequested",
                "FirstPollingInitialize",
                totalClock.Elapsed);
        }

        try
        {
            await providerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (recordDiagnostics)
            {
                LogStartupDiagnostic(
                    "SensorAggregatorInitializeCompleted",
                    "FirstPollingInitialize",
                    totalClock.Elapsed,
                    result: "LockWaitFailed");
            }

            throw;
        }

        try
        {
            if (recordDiagnostics)
            {
                LogStartupDiagnostic(
                    "SensorAggregatorInitializeLockAcquired",
                    "FirstPollingInitialize",
                    totalClock.Elapsed);
            }

            if (isInitialized)
            {
                return;
            }

            foreach (ISensorProvider provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch providerClock = Stopwatch.StartNew();
                string result = "Succeeded";
                if (recordDiagnostics)
                {
                    LogStartupDiagnostic(
                        "SensorProviderInitializeStarted",
                        "FirstPollingInitialize",
                        providerClock.Elapsed,
                        provider.Name);
                }

                try
                {
                    await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = "Cancelled";
                    throw;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = "Failed";
                    AppLogger.LogError(
                        $"Sensor provider initialization failed: {provider.Name}.",
                        exception,
                        $"sensor-provider-init:{provider.Name}:{exception.GetType().FullName}",
                        TimeSpan.FromMinutes(10));
                }
                finally
                {
                    if (recordDiagnostics)
                    {
                        LogStartupDiagnostic(
                            "SensorProviderInitializeCompleted",
                            "FirstPollingInitialize",
                            providerClock.Elapsed,
                            provider.Name,
                            result);
                    }
                }
            }

            isInitialized = true;
        }
        finally
        {
            if (recordDiagnostics)
            {
                LogStartupDiagnostic(
                    "SensorAggregatorInitializeCompleted",
                    "FirstPollingInitialize",
                    totalClock.Elapsed,
                    result: isInitialized ? "Succeeded" : "Cancelled");
            }

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
            mergeIndices.Clear();
            foreach (ISensorProvider provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    IReadOnlyList<SensorReading> providerReadings = provider is IConditionalSensorProvider conditional
                        ? await conditional.GetReadingsAsync(merged, cancellationToken).ConfigureAwait(false)
                        : await provider.GetReadingsAsync(cancellationToken).ConfigureAwait(false);
                    MergeProviderReadings(providerReadings, merged, mergeIndices);
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

    public Task<SensorProviderRefreshResult> RefreshDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        return RefreshDevicesAsync(recordStartupDiagnostics: false, cancellationToken);
    }

    internal Task<SensorProviderRefreshResult> RefreshDevicesAsync(
        HardwareRefreshReason reason,
        CancellationToken cancellationToken = default)
    {
        return RefreshDevicesAsync(
            recordStartupDiagnostics: reason == HardwareRefreshReason.Startup,
            cancellationToken);
    }

    private async Task<SensorProviderRefreshResult> RefreshDevicesAsync(
        bool recordStartupDiagnostics,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        Stopwatch totalClock = Stopwatch.StartNew();
        if (recordStartupDiagnostics)
        {
            LogStartupDiagnostic(
                "SensorAggregatorRefreshRequested",
                "StartupHardwareRefresh",
                totalClock.Elapsed);
        }

        try
        {
            await providerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (recordStartupDiagnostics)
            {
                LogStartupDiagnostic(
                    "SensorAggregatorRefreshCompleted",
                    "StartupHardwareRefresh",
                    totalClock.Elapsed,
                    result: "LockWaitFailed");
            }

            throw;
        }

        try
        {
            if (recordStartupDiagnostics)
            {
                LogStartupDiagnostic(
                    "SensorAggregatorRefreshLockAcquired",
                    "StartupHardwareRefresh",
                    totalClock.Elapsed);
            }

            List<string> failed = [];
            foreach (ISensorProvider provider in providers)
            {
                if (provider is not IRefreshableSensorProvider refreshable)
                {
                    continue;
                }

                Stopwatch providerClock = Stopwatch.StartNew();
                string result = "Succeeded";
                if (recordStartupDiagnostics)
                {
                    LogStartupDiagnostic(
                        "SensorProviderRefreshStarted",
                        "StartupHardwareRefresh",
                        providerClock.Elapsed,
                        provider.Name);
                }

                try
                {
                    await refreshable.RefreshDevicesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = "Cancelled";
                    throw;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = "Failed";
                    failed.Add(provider.Name);
                    AppLogger.LogError(
                        $"Sensor provider refresh failed: {provider.Name}.",
                        exception,
                        $"sensor-provider-refresh:{provider.Name}:{exception.GetType().FullName}",
                        TimeSpan.FromMinutes(5));
                }
                finally
                {
                    if (recordStartupDiagnostics)
                    {
                        LogStartupDiagnostic(
                            "SensorProviderRefreshCompleted",
                            "StartupHardwareRefresh",
                            providerClock.Elapsed,
                            provider.Name,
                            result);
                    }
                }
            }

            return new SensorProviderRefreshResult { FailedProviders = failed };
        }
        finally
        {
            if (recordStartupDiagnostics)
            {
                LogStartupDiagnostic(
                    "SensorAggregatorRefreshCompleted",
                    "StartupHardwareRefresh",
                    totalClock.Elapsed,
                    result: "Released");
            }

            providerLock.Release();
        }
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

    private static void LogStartupDiagnostic(
        string eventName,
        string startupPhase,
        TimeSpan elapsed,
        string? provider = null,
        string? result = null)
    {
        AppLogger.LogKeyEvent(
            $"{eventName} | startupPhase={startupPhase}; " +
            $"provider={provider ?? "SensorAggregator"}; elapsed={elapsed.TotalMilliseconds:0} ms; " +
            $"threadId={Environment.CurrentManagedThreadId}; result={result ?? "Pending"}");
    }
}
