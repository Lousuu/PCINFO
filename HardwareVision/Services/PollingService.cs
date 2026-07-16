using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HardwareVision.Models;
using HardwareVision.Utilities;

namespace HardwareVision.Services;

public sealed class PollingService : IDisposable, IAsyncDisposable
{
	private readonly ISensorService sensorService;

	private readonly object intervalLock = new object();

	private readonly SemaphoreSlim lifecycleLock = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim scheduleChangedSignal = new SemaphoreSlim(0, 1);

	private readonly SemaphoreSlim pollExecutionLock = new SemaphoreSlim(1, 1);

	private TimeSpan foregroundInterval;

	private TimeSpan backgroundInterval;

	private CancellationTokenSource? pollingCancellation;

	private Task? pollingTask;

	private volatile bool isBackgroundMode;

	private int disposeStarted;

	private bool isDisposed;

	public IReadOnlyList<SensorReading> LatestReadings { get; private set; } = Array.Empty<SensorReading>();


	public event EventHandler<SensorReadingsUpdatedEventArgs>? ReadingsUpdated;

	public event EventHandler<Exception>? PollingFailed;

	public PollingService(ISensorService sensorService, AppSettings settings)
	{
		this.sensorService = sensorService;
		foregroundInterval = CreateForegroundInterval(settings.RefreshIntervalSeconds);
		backgroundInterval = CreateBackgroundInterval(settings.BackgroundRefreshIntervalSeconds);
	}

	public async Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		ThrowIfDisposed();
		await lifecycleLock.WaitAsync(cancellationToken);
		try
		{
			Task? task = pollingTask;
			if (task == null || task.IsCompleted)
			{
				pollingCancellation?.Dispose();
				pollingCancellation = new CancellationTokenSource();
				pollingTask = Task.Run(() => RunPollingAsync(pollingCancellation.Token), CancellationToken.None);
			}
		}
		finally
		{
			lifecycleLock.Release();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await lifecycleLock.WaitAsync(cancellationToken);
		CancellationTokenSource? cancellationToStop;
		Task? taskToStop;
		try
		{
			cancellationToStop = pollingCancellation;
			taskToStop = pollingTask;
			pollingCancellation = null;
			pollingTask = null;
			cancellationToStop?.Cancel();
		}
		finally
		{
			lifecycleLock.Release();
		}
		if (taskToStop != null)
		{
			try
			{
				await taskToStop.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToStop?.IsCancellationRequested ?? false)
			{
			}
		}
		cancellationToStop?.Dispose();
	}

	public async Task RestartAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await StopAsync(cancellationToken);
		await StartAsync(cancellationToken);
	}

	public void SetBackgroundMode(bool enabled)
	{
		if (isBackgroundMode != enabled)
		{
			isBackgroundMode = enabled;
			SignalScheduleChanged();
		}
	}

	public async Task PollNowAsync(CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		await PollOnceAsync(cancellationToken).ConfigureAwait(false);
		SignalScheduleChanged();
	}

	public void UpdateIntervals(double foregroundSeconds, int backgroundSeconds)
	{
		lock (intervalLock)
		{
			foregroundInterval = TimeSpan.FromSeconds(
				SettingsService.NormalizeForegroundRefreshInterval(foregroundSeconds));
			backgroundInterval = CreateBackgroundInterval(backgroundSeconds);
		}

		SignalScheduleChanged();
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
		{
			isDisposed = true;
			CancellationTokenSource? cancellationTokenSource = pollingCancellation;
			Task? taskToObserve = pollingTask;
			pollingCancellation = null;
			pollingTask = null;
			cancellationTokenSource?.Cancel();
			_ = DisposeAfterPollingStopsAsync(taskToObserve, cancellationTokenSource);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
		{
			isDisposed = true;
			try
			{
				await StopAsync();
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				AppLogger.LogError(
					"Polling service async dispose failed.",
					ex,
					$"polling-dispose:{ex.GetType().FullName}",
					TimeSpan.FromMinutes(5));
			}
			scheduleChangedSignal.Dispose();
			pollExecutionLock.Dispose();
			lifecycleLock.Dispose();
		}
	}

	private async Task RunPollingAsync(CancellationToken cancellationToken)
	{
		try
		{
			await InitializeSensorServiceAsync(cancellationToken);
			long nextPollTimestamp = Stopwatch.GetTimestamp();
			while (!cancellationToken.IsCancellationRequested)
			{
				await PollOnceAsync(cancellationToken);
				TimeSpan interval = GetCurrentInterval();
				long intervalTicks = Math.Max(1L, (long)Math.Ceiling(interval.TotalSeconds * Stopwatch.Frequency));
				nextPollTimestamp += intervalTicks;
				long now = Stopwatch.GetTimestamp();
				if (nextPollTimestamp <= now)
				{
					long missedIntervals = ((now - nextPollTimestamp) / intervalTicks) + 1L;
					nextPollTimestamp += missedIntervals * intervalTicks;
				}

				TimeSpan delay = Stopwatch.GetElapsedTime(now, nextPollTimestamp);
				if (delay > TimeSpan.Zero)
				{
					bool scheduleChanged = await WaitForDelayOrScheduleChangeAsync(delay, cancellationToken);
					if (scheduleChanged)
					{
						nextPollTimestamp = Stopwatch.GetTimestamp();
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
	}

	private async Task InitializeSensorServiceAsync(CancellationToken cancellationToken)
	{
		try
		{
			await sensorService.InitializeAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
	}

	private async Task PollOnceAsync(CancellationToken cancellationToken)
	{
		await pollExecutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		Stopwatch stopwatch = Stopwatch.StartNew();
		try
		{
			IReadOnlyList<SensorReading> readings = (LatestReadings = await sensorService.GetCurrentReadingsAsync(cancellationToken));
			SensorReadingsUpdatedEventArgs args = new(readings, DateTimeOffset.Now, isBackgroundMode);
			EventHandler<SensorReadingsUpdatedEventArgs>? handlers = ReadingsUpdated;
			if (handlers is not null)
			{
				foreach (EventHandler<SensorReadingsUpdatedEventArgs> handler in handlers.GetInvocationList())
				{
					try
					{
						handler(this, args);
					}
					catch (Exception exception)
					{
						AppLogger.LogError(
							"Sensor readings subscriber failed.",
							exception,
							$"polling-subscriber:{handler.Method.DeclaringType?.FullName}:{handler.Method.Name}",
							TimeSpan.FromMinutes(5));
					}
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
		finally
		{
			stopwatch.Stop();
			RuntimePerformanceDiagnostics.RecordPolling(stopwatch.Elapsed);
			RuntimePerformanceDiagnostics.TryLogSummary(isBackgroundMode);
			pollExecutionLock.Release();
		}
	}

	private TimeSpan GetCurrentInterval()
	{
		lock (intervalLock)
		{
			return isBackgroundMode ? backgroundInterval : foregroundInterval;
		}
	}

	private void OnPollingFailed(Exception exception)
	{
		AppLogger.LogError("Sensor polling failed.", exception, "polling:" + exception.GetType().FullName);
		try
		{
			this.PollingFailed?.Invoke(this, exception);
		}
		catch
		{
		}
	}

	private static TimeSpan CreateForegroundInterval(double seconds)
	{
		return TimeSpan.FromSeconds(SettingsService.NormalizeForegroundRefreshInterval(seconds));
	}

	private static TimeSpan CreateBackgroundInterval(int seconds)
	{
		return CreateInterval(seconds, 10, 5, 120);
	}

	private static TimeSpan CreateInterval(double seconds, double fallbackSeconds, double minimumSeconds, double maximumSeconds)
	{
		double value = ((seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds)) ? seconds : fallbackSeconds);
		value = Math.Clamp(value, minimumSeconds, maximumSeconds);
		return TimeSpan.FromSeconds(value);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(isDisposed, this);
	}

	internal TimeSpan ForegroundIntervalForDiagnostics => foregroundInterval;

	internal TimeSpan BackgroundIntervalForDiagnostics => backgroundInterval;

	private void SignalScheduleChanged()
	{
		if (pollingTask is not { IsCompleted: false })
		{
			return;
		}

		try
		{
			scheduleChangedSignal.Release();
		}
		catch (SemaphoreFullException)
		{
			// Multiple rapid changes coalesce into one scheduler wake-up.
		}
		catch (ObjectDisposedException)
		{
		}
	}

	private async Task<bool> WaitForDelayOrScheduleChangeAsync(TimeSpan delay, CancellationToken cancellationToken)
	{
		using CancellationTokenSource waitCancellation =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task delayTask = Task.Delay(delay, waitCancellation.Token);
		Task signalTask = scheduleChangedSignal.WaitAsync(waitCancellation.Token);
		Task completed = await Task.WhenAny(delayTask, signalTask).ConfigureAwait(false);
		cancellationToken.ThrowIfCancellationRequested();
		bool scheduleChanged = completed == signalTask;
		waitCancellation.Cancel();
		try
		{
			await completed.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested)
		{
			cancellationToken.ThrowIfCancellationRequested();
		}

		if (scheduleChanged)
		{
			while (scheduleChangedSignal.Wait(0))
			{
			}

			return true;
		}

		return false;
	}

	private async Task DisposeAfterPollingStopsAsync(Task? taskToObserve, CancellationTokenSource? cancellationToDispose)
	{
		try
		{
			if (taskToObserve != null)
			{
				await taskToObserve.ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Exception exception = ex2;
			OnPollingFailed(exception);
		}
		finally
		{
			cancellationToDispose?.Dispose();
			scheduleChangedSignal.Dispose();
			pollExecutionLock.Dispose();
			lifecycleLock.Dispose();
		}
	}
}
