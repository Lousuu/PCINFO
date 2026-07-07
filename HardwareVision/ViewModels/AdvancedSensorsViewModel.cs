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
    private string statusText = "传感器列表仅在打开本页面时刷新";
    private IReadOnlyList<DetailSensorRowViewModel> sensorRows = Array.Empty<DetailSensorRowViewModel>();

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

    public IReadOnlyList<DetailSensorRowViewModel> SensorRows
    {
        get => sensorRows;
        private set => SetProperty(ref sensorRows, value);
    }

    public void SetActive(bool active)
    {
        if (isDisposed || dashboard is null || isActive == active)
        {
            return;
        }

        isActive = active;
        if (active)
        {
            dashboard.PropertyChanged += OnDashboardPropertyChanged;
            QueueApplyReadings(dashboard.CurrentSensorReadings, force: true);
        }
        else
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
            CancelPendingRefresh();
        }
    }

    public void Dispose()
    {
        CancelPendingRefresh();
        refreshCancellation?.Dispose();
        if (dashboard is not null)
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }

        isDisposed = true;
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

        CancelPendingRefresh();
        refreshCancellation = new CancellationTokenSource();
        CancellationToken token = refreshCancellation.Token;

        StatusText = snapshot.Length == 0
            ? "当前传感器源未返回可显示数据"
            : $"正在整理 {snapshot.Length} 个传感器读数...";

        _ = ApplyReadingsAsync(snapshot, token);
    }

    private async Task ApplyReadingsAsync(SensorReading[] readings, CancellationToken cancellationToken)
    {
        try
        {
            DetailSensorRowViewModel[] rows = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return readings
                    .OrderBy(reading => reading.Category)
                    .ThenBy(reading => reading.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(reading => reading.SensorName, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxVisibleRows)
                    .Select(DetailSensorRowViewModel.FromReading)
                    .ToArray();
            }, cancellationToken).ConfigureAwait(false);

            await dispatcher.InvokeAsync(() =>
            {
                if (isDisposed || !isActive || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!HardwareDetailReadingHelpers.DetailRowsEqual(SensorRows, rows))
                {
                    SensorRows = rows;
                }

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

            if (!isDisposed && isActive)
            {
                await dispatcher.InvokeAsync(() => StatusText = "传感器列表整理失败，其他页面读取不受影响。", DispatcherPriority.Background);
            }
        }
    }

    private void CancelPendingRefresh()
    {
        refreshCancellation?.Cancel();
    }

    private static string BuildStatusText(int totalCount, int visibleCount)
    {
        if (totalCount == 0)
        {
            return "当前传感器源未返回可显示数据";
        }

        return totalCount > MaxVisibleRows
            ? $"{visibleCount} / {totalCount} 个传感器读数正在显示，已限制列表规模以保持响应"
            : $"{visibleCount} / {totalCount} 个传感器读数正在显示";
    }
}
