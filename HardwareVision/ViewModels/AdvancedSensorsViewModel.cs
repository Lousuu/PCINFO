using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Principal;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HardwareVision.Models;
using HardwareVision.Sensors;
using HardwareVision.Services;
using HardwareVision.Utilities;
using HardwareVision.Collections;
using static HardwareVision.ViewModels.ViewModelHelpers;

namespace HardwareVision.ViewModels;

public sealed class AdvancedSensorsViewModel : ObservableObject, IDisposable
{
    private const int MaxVisibleRows = 500;
    private static readonly TimeSpan RefreshThrottle = TimeSpan.FromSeconds(3);

    private readonly DashboardViewModel? dashboard;
    private readonly Dispatcher dispatcher;
    private CancellationTokenSource? refreshCancellation;
    private bool isActive;
    private bool isDisposed;
    private DateTime lastAppliedUtc = DateTime.MinValue;
    private readonly BulkObservableCollection<DetailSensorRowViewModel> sensorRows = new();
    private string statusText = "传感器列表仅在打开本页面时刷新";

    public AdvancedSensorsViewModel()
    {
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    public AdvancedSensorsViewModel(DashboardViewModel dashboard, Dispatcher dispatcher)
    {
        this.dashboard = dashboard;
        this.dispatcher = dispatcher;
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public ObservableCollection<DetailSensorRowViewModel> SensorRows => sensorRows;

    public void SetActive(bool active)
    {
        if (isDisposed || dashboard is null || isActive == active)
        {
            return;
        }

        isActive = active;
        if (active)
        {
            AppLogger.LogKeyEvent("StartupTiming | AdvancedSensorsView activated");
            AppLogger.LogMemoryCheckpoint("advanced sensors page activated");
            dashboard.PropertyChanged += OnDashboardPropertyChanged;
            QueueApplyReadings(dashboard.CurrentSensorReadings, force: true);
        }
        else
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
            CancelPendingRefresh();
            SensorRows.Clear();
            StatusText = "传感器列表仅在打开本页面时刷新";
            AppLogger.LogMemoryCheckpoint("advanced sensors page deactivated");
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        isActive = false;
        CancelPendingRefresh();
        if (dashboard is not null)
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isActive && e.PropertyName == nameof(DashboardViewModel.CurrentSensorReadings) && dashboard is not null)
        {
            QueueApplyReadings(dashboard.CurrentSensorReadings);
        }
    }

    private void QueueApplyReadings(IReadOnlyList<SensorReading> readings, bool force = false)
    {
        DateTime now = DateTime.UtcNow;
        if (!force && now - lastAppliedUtc < RefreshThrottle)
        {
            return;
        }

        lastAppliedUtc = now;
        SensorReading[] snapshot = readings.ToArray();

        CancellationTokenSource cancellation = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref refreshCancellation, cancellation);
        previous?.Cancel();

        if (SensorRows.Count == 0)
        {
            StatusText = snapshot.Length == 0
                ? "暂无可显示的传感器数据"
                : $"正在整理 {snapshot.Length} 个传感器读数...";
        }

        _ = ApplyReadingsAsync(snapshot, cancellation);
    }

    private async Task ApplyReadingsAsync(SensorReading[] readings, CancellationTokenSource owner)
    {
        CancellationToken cancellationToken = owner.Token;
        try
        {
            DetailSensorRowSnapshot[] rows = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return readings
                    .OrderBy(reading => reading.Category)
                    .ThenBy(reading => reading.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(reading => reading.SensorName, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxVisibleRows)
                    .Select(DetailSensorRowViewModel.CreateSnapshot)
                    .Where(row => row.IsVisible)
                    .ToArray();
            }, cancellationToken).ConfigureAwait(false);

            await dispatcher.InvokeAsync(() =>
            {
                if (!CanApply(owner, cancellationToken))
                {
                    return;
                }

                AdvancedSensorRowReconciler.Apply(sensorRows, rows);
                StatusText = BuildStatusText(readings.Length, rows.Length);
            }, DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "Advanced sensor list refresh failed.",
                exception,
                $"advanced-sensors-refresh:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));

            if (CanApply(owner, cancellationToken))
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (CanApply(owner, cancellationToken))
                    {
                        StatusText = "无法更新传感器列表，其他页面不受影响。";
                    }
                }, DispatcherPriority.Background);
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref refreshCancellation, null, owner);
            owner.Dispose();
        }
    }

    private void CancelPendingRefresh()
    {
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref refreshCancellation, null);
        cancellation?.Cancel();
    }

    private bool CanApply(CancellationTokenSource owner, CancellationToken cancellationToken) =>
        !isDisposed
        && isActive
        && !cancellationToken.IsCancellationRequested
        && ReferenceEquals(Volatile.Read(ref refreshCancellation), owner);

    private static string BuildStatusText(int totalCount, int visibleCount)
    {
        if (totalCount == 0)
        {
            return "暂无可显示的传感器数据";
        }

        return totalCount > MaxVisibleRows
            ? $"{visibleCount} / {totalCount} 个传感器读数正在显示，已限制列表规模以保持响应"
            : $"{visibleCount} / {totalCount} 个传感器读数正在显示";
    }
}

internal readonly record struct SensorRowReconciliationResult(
    int ReusedCount,
    int CreatedCount,
    int UpdatedCount,
    bool CollectionReset);

internal static class AdvancedSensorRowReconciler
{
    public static SensorRowReconciliationResult Apply(
        BulkObservableCollection<DetailSensorRowViewModel> target,
        IReadOnlyList<DetailSensorRowSnapshot> snapshots)
    {
        Dictionary<string, DetailSensorRowViewModel> existingById = new(StringComparer.Ordinal);
        foreach (DetailSensorRowViewModel row in target)
        {
            existingById.TryAdd(row.Id, row);
        }

        DetailSensorRowViewModel[] finalRows = new DetailSensorRowViewModel[snapshots.Count];
        HashSet<string> reusedIds = new(StringComparer.Ordinal);
        int reusedCount = 0;
        int createdCount = 0;
        int updatedCount = 0;

        for (int index = 0; index < snapshots.Count; index++)
        {
            DetailSensorRowSnapshot snapshot = snapshots[index];
            if (existingById.TryGetValue(snapshot.Id, out DetailSensorRowViewModel? existing)
                && reusedIds.Add(snapshot.Id))
            {
                if (!existing.HasSameValuesAs(snapshot))
                {
                    existing.ApplySnapshot(snapshot);
                    updatedCount++;
                }

                finalRows[index] = existing;
                reusedCount++;
            }
            else
            {
                finalRows[index] = DetailSensorRowViewModel.FromSnapshot(snapshot);
                createdCount++;
            }
        }

        bool collectionReset = target.ReplaceAll(finalRows);
        return new SensorRowReconciliationResult(reusedCount, createdCount, updatedCount, collectionReset);
    }
}
