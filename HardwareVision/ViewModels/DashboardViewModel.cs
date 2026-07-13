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

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly AppSettings settings;
    private readonly IHardwareInfoService hardwareInfoService;
    private readonly PollingService pollingService;
    private readonly ISettingsService settingsService;
    private readonly Dispatcher dispatcher;
    private readonly ISensorHistoryService sensorHistoryService;
    private readonly GpuDeviceService gpuDeviceService = new();
    private readonly DiskDeviceService diskDeviceService = new();
    private readonly DiskPerformanceService diskPerformanceService = new();
    private readonly NetworkAdapterService networkAdapterService = new();
    private readonly DashboardRefreshCoordinator refreshCoordinator;
    private readonly SingleFlightGate diskRefreshGate = new();
    private readonly SingleFlightGate networkRefreshGate = new();
    private readonly CancellationTokenSource refreshCancellation = new();
    private IReadOnlyList<SensorReading> pendingSensorReadings = Array.Empty<SensorReading>();
    private DateTimeOffset pendingSensorTimestamp;
    private volatile bool pendingBackgroundMode;
    private long lastDiskRefreshTimestamp;
    private long lastNetworkRefreshTimestamp;
    private long lastNetworkStaticRefreshTimestamp;
    private int diskRefreshGeneration;
    private int networkRefreshGeneration;
    private bool summaryActive = true;
    private bool isDisposed;
    private bool isHardwareInfoLoading;
    private string deviceName = "--";
    private string? deviceNameToolTip;
    private string operatingSystem = "--";
    private string? operatingSystemToolTip;
    private string lastRefreshTime = "--";
    private string loadMessage = "正在读取硬件信息";
    private HardwareSnapshot? currentSnapshot;
    private IReadOnlyList<HardwareDevice> hardwareDevices = Array.Empty<HardwareDevice>();
    private IReadOnlyList<SensorReading> currentSensorReadings = Array.Empty<SensorReading>();
    private IReadOnlyList<DiskPerformanceSnapshot> diskPerformanceSnapshots = Array.Empty<DiskPerformanceSnapshot>();
    private IReadOnlyList<NetworkAdapterDevice> networkAdapters = Array.Empty<NetworkAdapterDevice>();
    private IReadOnlyList<GpuDevice> gpuDevices = Array.Empty<GpuDevice>();
    private IReadOnlyList<DiskDevice> diskDevices = Array.Empty<DiskDevice>();
    private GpuDevice? selectedGpu;
    private DiskDevice? selectedDisk;

    public DashboardViewModel(
        AppSettings settings,
        IHardwareInfoService hardwareInfoService,
        PollingService pollingService,
        ISettingsService settingsService,
        Dispatcher dispatcher,
        ISensorHistoryService sensorHistoryService)
    {
        this.settings = settings;
        this.hardwareInfoService = hardwareInfoService;
        this.pollingService = pollingService;
        this.settingsService = settingsService;
        this.dispatcher = dispatcher;
        this.sensorHistoryService = sensorHistoryService;
        refreshCoordinator = new DashboardRefreshCoordinator(ApplyCoalescedRefresh);

        OverviewCards.Add(CreateCard("CPU", "--"));
        OverviewCards.Add(CreateCard("GPU", "--"));
        OverviewCards.Add(CreateCard("内存", "--"));
        OverviewCards.Add(CreateCard("硬盘", "--"));
        OverviewCards.Add(CreateCard("网络", "--"));
        OverviewCards.Add(CreateCard("主板 / 系统", "--"));

        pollingService.ReadingsUpdated += OnReadingsUpdated;
        pollingService.PollingFailed += OnPollingFailed;
    }

    public string ApplicationName => "HardwareVision";

    public string PreferredGpuId
    {
        get => settings.PreferredGpuId ?? string.Empty;
        set
        {
            string? normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value;
            bool changed = !string.Equals(settings.PreferredGpuId, normalizedValue, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                settings.PreferredGpuId = normalizedValue;
                OnPropertyChanged();
                _ = settingsService.UpdateAsync(updated => updated.PreferredGpuId = settings.PreferredGpuId);
            }

            RefreshGpuDevices();
            RefreshSummaryCards(DashboardRefreshKind.Sensors);
        }
    }

    public string PreferredDiskId
    {
        get => settings.PreferredDiskId ?? string.Empty;
        set
        {
            string? normalizedValue = string.IsNullOrWhiteSpace(value) ? null : value;
            bool changed = !string.Equals(settings.PreferredDiskId, normalizedValue, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                settings.PreferredDiskId = normalizedValue;
                OnPropertyChanged();
                _ = settingsService.UpdateAsync(updated => updated.PreferredDiskId = settings.PreferredDiskId);
            }

            RefreshDiskDevices();
            RefreshSummaryCards(DashboardRefreshKind.Disk);
        }
    }

    public string DeviceName
    {
        get => deviceName;
        private set
        {
            if (SetProperty(ref deviceName, value))
            {
                DeviceNameToolTip = ViewModelHelpers.NullIfShortOrSame(deviceName, deviceName, 30);
            }
        }
    }

    public string? DeviceNameToolTip
    {
        get => deviceNameToolTip;
        private set => SetProperty(ref deviceNameToolTip, value);
    }

    public string OperatingSystem
    {
        get => operatingSystem;
        private set
        {
            if (SetProperty(ref operatingSystem, value))
            {
                OperatingSystemToolTip = ViewModelHelpers.NullIfShortOrSame(operatingSystem, operatingSystem, 36);
            }
        }
    }

    public string? OperatingSystemToolTip
    {
        get => operatingSystemToolTip;
        private set => SetProperty(ref operatingSystemToolTip, value);
    }

    public string LastRefreshTime
    {
        get => lastRefreshTime;
        private set => SetProperty(ref lastRefreshTime, value);
    }

    public string LoadMessage
    {
        get => loadMessage;
        private set => SetProperty(ref loadMessage, value);
    }

    public bool IsHardwareInfoLoading
    {
        get => isHardwareInfoLoading;
        private set => SetProperty(ref isHardwareInfoLoading, value);
    }

    public HardwareSnapshot? CurrentSnapshot
    {
        get => currentSnapshot;
        private set => SetProperty(ref currentSnapshot, value);
    }

    public IReadOnlyList<HardwareDevice> HardwareDevices
    {
        get => hardwareDevices;
        private set => SetProperty(ref hardwareDevices, value);
    }

    public IReadOnlyList<SensorReading> CurrentSensorReadings
    {
        get => currentSensorReadings;
        private set => SetProperty(ref currentSensorReadings, value);
    }

    public IReadOnlyList<GpuDevice> GpuDevices
    {
        get => gpuDevices;
        private set => SetProperty(ref gpuDevices, value);
    }

    public IReadOnlyList<DiskDevice> DiskDevices
    {
        get => diskDevices;
        private set => SetProperty(ref diskDevices, value);
    }

    public GpuDevice? SelectedGpu
    {
        get => selectedGpu;
        set
        {
            if (SetProperty(ref selectedGpu, value) && value is not null)
            {
                PreferredGpuId = value.Id;
            }
        }
    }

    public DiskDevice? SelectedDisk
    {
        get => selectedDisk;
        set
        {
            if (SetProperty(ref selectedDisk, value) && value is not null)
            {
                PreferredDiskId = value.Id;
            }
        }
    }

    public IReadOnlyList<NetworkAdapterDevice> NetworkAdapters => networkAdapters;

    public ObservableCollection<HardwareOverviewCardViewModel> OverviewCards { get; } = new();

    public ObservableCollection<RealtimeMetricChartViewModel> RealtimeCharts { get; } = new();

    public async Task RefreshHardwareInfoAsync()
    {
        if (isDisposed)
        {
            return;
        }

        IsHardwareInfoLoading = true;
        LoadMessage = "正在刷新硬件信息";

        try
        {
            HardwareSnapshot snapshot = await hardwareInfoService.GetHardwareSnapshotAsync();
            ViewModelHelpers.Dispatch(dispatcher, () =>
            {
                CurrentSnapshot = snapshot;
                HardwareDevices = snapshot.Devices;
                DeviceName = ViewModelHelpers.FirstAvailable(snapshot.ComputerName, snapshot.MotherboardName, Environment.MachineName, "--")!;
                OperatingSystem = ViewModelHelpers.FirstAvailable(snapshot.OperatingSystem, Environment.OSVersion.VersionString, "--")!;
                LastRefreshTime = snapshot.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
                LoadMessage = "硬件信息已更新";
                RefreshGpuDevices();
                RefreshDiskDevices();
                RefreshSummaryCards();
            });
        }
        catch (Exception exception)
        {
            LoadMessage = $"硬件信息读取失败：{exception.Message}";
            AppLogger.LogError("Hardware snapshot refresh failed.", exception, $"dashboard-refresh:{exception.GetType().FullName}", TimeSpan.FromMinutes(5));
        }
        finally
        {
            IsHardwareInfoLoading = false;
        }
    }

    public void SetSummaryActive(bool active)
    {
        summaryActive = active;
        if (active)
        {
            RefreshSummaryCards();
        }
    }

    public HardwareMetric ConfigureMetric(HardwareMetric metric)
    {
        return HardwareMetricService.ApplySettings(metric, settings);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        pollingService.ReadingsUpdated -= OnReadingsUpdated;
        pollingService.PollingFailed -= OnPollingFailed;
        refreshCancellation.Cancel();
        Interlocked.Increment(ref diskRefreshGeneration);
        Interlocked.Increment(ref networkRefreshGeneration);
        refreshCoordinator.Dispose();
        diskPerformanceService.Dispose();
        networkAdapterService.Dispose();
        refreshCancellation.Dispose();
        isDisposed = true;
    }

    private static HardwareOverviewCardViewModel CreateCard(string title, string hardwareName)
    {
        return new HardwareOverviewCardViewModel
        {
            Title = title,
            HardwareName = hardwareName
        };
    }

    private void OnReadingsUpdated(object? sender, SensorReadingsUpdatedEventArgs e)
    {
        if (isDisposed)
        {
            return;
        }

        pendingSensorReadings = e.Readings;
        pendingSensorTimestamp = e.Timestamp;
        pendingBackgroundMode = e.IsBackgroundMode;
        if (!e.IsBackgroundMode)
        {
            refreshCoordinator.Request(DashboardRefreshKind.Sensors);
        }

        ScheduleDiskRefresh(e.IsBackgroundMode);
        ScheduleNetworkRefresh(e.Readings, e.IsBackgroundMode);
    }

    private void OnPollingFailed(object? sender, Exception exception)
    {
        ViewModelHelpers.Dispatch(dispatcher, () => LoadMessage = $"传感器刷新失败：{exception.Message}");
    }

    private void ScheduleDiskRefresh(bool isBackgroundMode)
    {
        TimeSpan interval = isBackgroundMode ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1);
        long now = Stopwatch.GetTimestamp();
        if (lastDiskRefreshTimestamp != 0
            && Stopwatch.GetElapsedTime(lastDiskRefreshTimestamp, now) < interval)
        {
            return;
        }

        if (!diskRefreshGate.TryEnter())
        {
            RuntimePerformanceDiagnostics.RecordDiskRefreshSkip();
            return;
        }

        lastDiskRefreshTimestamp = now;
        int generation = Interlocked.Increment(ref diskRefreshGeneration);
        _ = RefreshDiskPerformanceAsync(generation, isBackgroundMode, refreshCancellation.Token);
    }

    private async Task RefreshDiskPerformanceAsync(
        int generation,
        bool requestedInBackground,
        CancellationToken cancellationToken)
    {
		Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            IReadOnlyList<DiskPerformanceSnapshot> snapshots = await diskPerformanceService
                .GetCurrentSnapshotsAsync(cancellationToken)
                .ConfigureAwait(false);
            if (isDisposed || cancellationToken.IsCancellationRequested || generation != Volatile.Read(ref diskRefreshGeneration))
            {
                return;
            }

            diskPerformanceSnapshots = snapshots;
            if (!requestedInBackground || !pendingBackgroundMode)
            {
                refreshCoordinator.Request(DashboardRefreshKind.Disk);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "Disk performance refresh failed.",
                exception,
                $"dashboard-disk-performance:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
		finally
		{
			stopwatch.Stop();
			RuntimePerformanceDiagnostics.RecordDiskRefresh(stopwatch.Elapsed);
			diskRefreshGate.Exit();
		}
    }

    private void ScheduleNetworkRefresh(IReadOnlyList<SensorReading> readings, bool isBackgroundMode)
    {
        TimeSpan interval = isBackgroundMode ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(1);
        long now = Stopwatch.GetTimestamp();
        if (lastNetworkRefreshTimestamp != 0
            && Stopwatch.GetElapsedTime(lastNetworkRefreshTimestamp, now) < interval)
        {
            return;
        }

        if (!networkRefreshGate.TryEnter())
        {
            RuntimePerformanceDiagnostics.RecordNetworkRefreshSkip();
            return;
        }

        lastNetworkRefreshTimestamp = now;
        bool includeStatic = lastNetworkStaticRefreshTimestamp == 0
            || Stopwatch.GetElapsedTime(lastNetworkStaticRefreshTimestamp, now) >= TimeSpan.FromSeconds(30);
        if (includeStatic)
        {
            lastNetworkStaticRefreshTimestamp = now;
        }

        int generation = Interlocked.Increment(ref networkRefreshGeneration);
        _ = RefreshNetworkAdaptersAsync(
            readings,
            includeStatic,
            generation,
            isBackgroundMode,
            refreshCancellation.Token);
    }

    private async Task RefreshNetworkAdaptersAsync(
        IReadOnlyList<SensorReading> readings,
        bool includeStatic,
        int generation,
        bool requestedInBackground,
        CancellationToken cancellationToken)
    {
		Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            IReadOnlyList<NetworkAdapterDevice> adapters = includeStatic
                ? await networkAdapterService.RefreshStaticDevicesAsync(readings, cancellationToken).ConfigureAwait(false)
                : await networkAdapterService.RefreshRealtimeDevicesAsync(readings, cancellationToken).ConfigureAwait(false);
            if (isDisposed || cancellationToken.IsCancellationRequested || generation != Volatile.Read(ref networkRefreshGeneration))
            {
                return;
            }

            networkAdapters = adapters;
            sensorHistoryService.RecordNetwork(SelectActiveNetworkAdapter(networkAdapters, settings.ShowVirtualNetworkAdapters));
            if (!requestedInBackground || !pendingBackgroundMode)
            {
                refreshCoordinator.Request(DashboardRefreshKind.Network);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AppLogger.LogError(
                "Network adapter refresh failed.",
                exception,
                $"dashboard-network-adapters:{exception.GetType().FullName}",
                TimeSpan.FromMinutes(5));
        }
		finally
		{
			stopwatch.Stop();
			RuntimePerformanceDiagnostics.RecordNetworkRefresh(stopwatch.Elapsed);
			networkRefreshGate.Exit();
		}
    }

    private void ApplyCoalescedRefresh(DashboardRefreshKind kinds)
    {
        ViewModelHelpers.Dispatch(dispatcher, () =>
        {
            if (isDisposed || pendingBackgroundMode)
            {
                return;
            }

            if ((kinds & DashboardRefreshKind.Sensors) != 0)
            {
                CurrentSensorReadings = pendingSensorReadings;
                LastRefreshTime = pendingSensorTimestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
                LoadMessage = "传感器读数已更新";
                RefreshGpuDevices();
            }

            if ((kinds & (DashboardRefreshKind.Sensors | DashboardRefreshKind.Disk)) != 0)
            {
                RefreshDiskDevices();
            }

            if ((kinds & DashboardRefreshKind.Network) != 0)
            {
                OnPropertyChanged(nameof(NetworkAdapters));
            }

            if (summaryActive)
            {
                RefreshSummaryCards(kinds);
            }
        });
    }

    private void RefreshGpuDevices()
    {
        GpuDevices = gpuDeviceService.BuildGpuDevices(CurrentSnapshot, CurrentSensorReadings, settings.PreferredGpuId);
        selectedGpu = gpuDeviceService.SelectPreferredGpu(GpuDevices, settings.PreferredGpuId);
        sensorHistoryService.RecordGpu(selectedGpu);
        OnPropertyChanged(nameof(SelectedGpu));
    }

    private void RefreshDiskDevices()
    {
        DiskDevices = diskDeviceService.BuildDiskDevices(CurrentSnapshot, CurrentSensorReadings, diskPerformanceSnapshots);
        selectedDisk = diskDeviceService.SelectPreferredDisk(DiskDevices, settings.PreferredDiskId);
        sensorHistoryService.RecordDisk(selectedDisk);
        OnPropertyChanged(nameof(SelectedDisk));
    }

    private void RefreshSummaryCards(DashboardRefreshKind kinds = DashboardRefreshKind.All)
    {
		RuntimePerformanceDiagnostics.RecordDashboardRefresh();
        if (OverviewCards.Count < 6)
        {
            return;
        }

        if ((kinds & (DashboardRefreshKind.Sensors | DashboardRefreshKind.Hardware)) != 0)
        {
            OverviewCards[0].HardwareName = ViewModelHelpers.FirstAvailable(CurrentSnapshot?.CpuName, FindDeviceName(SensorCategory.Cpu), "CPU")!;
            OverviewCards[0].HeaderNote = "LibreHardwareMonitor";
            OverviewCards[0].ClearHardwareOptions();
            OverviewCards[0].ReplaceMetrics(BuildCpuSummaryMetrics());

            OverviewCards[1].HardwareName = ViewModelHelpers.FirstAvailable(SelectedGpu?.Name, CurrentSnapshot?.GpuName, "GPU")!;
            OverviewCards[1].HeaderNote = SelectedGpu?.Source ?? "LibreHardwareMonitor / WMI";
            OverviewCards[1].UpdateHardwareOptions(
                GpuDevices.Select(gpu => (gpu.Id, gpu.Name)),
                SelectedGpu?.Id,
                id => PreferredGpuId = id ?? string.Empty);
            OverviewCards[1].ReplaceMetrics(BuildGpuSummaryMetrics());

            OverviewCards[2].HardwareName = "物理内存";
            OverviewCards[2].HeaderNote = "Windows API / WMI";
            OverviewCards[2].ClearHardwareOptions();
            OverviewCards[2].ReplaceMetrics(BuildMemorySummaryMetrics());
        }

        if ((kinds & (DashboardRefreshKind.Sensors | DashboardRefreshKind.Disk | DashboardRefreshKind.Hardware)) != 0)
        {
            OverviewCards[3].HardwareName = ViewModelHelpers.FirstAvailable(SelectedDisk?.Name, "系统盘与存储")!;
            OverviewCards[3].HeaderNote = "WMI / PerformanceCounter";
            OverviewCards[3].UpdateHardwareOptions(
                DiskDevices.Select(disk => (disk.Id, disk.Name)),
                SelectedDisk?.Id,
                id => PreferredDiskId = id ?? string.Empty);
            OverviewCards[3].ReplaceMetrics(BuildDiskSummaryMetrics());
        }

        if ((kinds & (DashboardRefreshKind.Network | DashboardRefreshKind.Hardware)) != 0)
        {
            NetworkAdapterDevice? adapter = SelectActiveNetworkAdapter(networkAdapters, settings.ShowVirtualNetworkAdapters);
            OverviewCards[4].HardwareName = ViewModelHelpers.FirstAvailable(adapter?.Name, "网络")!;
            OverviewCards[4].HeaderNote = adapter?.Source ?? "System.Net / WMI";
            OverviewCards[4].ClearHardwareOptions();
            OverviewCards[4].ReplaceMetrics(BuildNetworkSummaryMetrics(adapter));
        }

        if ((kinds & DashboardRefreshKind.Hardware) != 0)
        {
            OverviewCards[5].HardwareName = ViewModelHelpers.FirstAvailable(CurrentSnapshot?.MotherboardName, DeviceName, "系统")!;
            OverviewCards[5].HeaderNote = "WMI";
            OverviewCards[5].ClearHardwareOptions();
            OverviewCards[5].ReplaceMetrics(BuildSystemSummaryMetrics());
        }
    }

    private IEnumerable<HardwareMetric> BuildCpuSummaryMetrics()
    {
        SensorReading[] cpu = CurrentSensorReadings.Where(reading => reading.Category == SensorCategory.Cpu).ToArray();
        SensorReading? packageTemp = HardwareDetailReadingHelpers.FindPreferredReading(cpu, SensorType.Temperature, "Package", "CPU");
        SensorReading? totalLoad = HardwareDetailReadingHelpers.FindPreferredReading(cpu, SensorType.Load, "Total");
        SensorReading? packagePower = HardwareDetailReadingHelpers.FindPreferredReading(cpu, SensorType.Power, "Package", "CPU");
        double? averageClock = Average(cpu.Where(HardwareDetailReadingHelpers.IsCpuClockReadingUsableForFrequency).Select(reading => reading.Value));

        yield return SensorMetric("dashboard.cpu.temperature", HardwareMetricCategory.Cpu, "CPU Package Temperature", "CPU Package Temperature", packageTemp, "CPU 封装温度。", true, 0, "CPU");
        yield return SensorMetric("dashboard.cpu.load", HardwareMetricCategory.Cpu, "CPU Total Load", "CPU Total Load", totalLoad, "CPU 总负载。", true, 1, "CPU");
        yield return ValueMetric("dashboard.cpu.clock.average", "dashboard-cpu", HardwareMetricCategory.Cpu, "Average Core Clock", "Average Core Clock", averageClock, "MHz", "LibreHardwareMonitor", "可用核心频率读数的平均值。", true, 2, "CPU");
        yield return SensorMetric("dashboard.cpu.power", HardwareMetricCategory.Cpu, "CPU Package Power", "CPU Package Power", packagePower, "CPU 封装功耗。", true, 3, "CPU");
    }

    private IEnumerable<HardwareMetric> BuildGpuSummaryMetrics()
    {
        GpuDevice? gpu = SelectedGpu;
        yield return SensorMetric("dashboard.gpu.temperature", HardwareMetricCategory.Gpu, "GPU Core Temperature", "GPU Core Temperature", gpu?.TemperatureCore, "当前选中 GPU 核心温度。", true, 10, "GPU");
        yield return SensorMetric("dashboard.gpu.load", HardwareMetricCategory.Gpu, "GPU Core Load", "GPU Core Load", gpu?.CoreLoad, "当前选中 GPU 核心负载。", true, 11, "GPU");
        yield return SensorMetric("dashboard.gpu.clock", HardwareMetricCategory.Gpu, "GPU Core Clock", "GPU Core Clock", gpu?.CoreClock, "当前选中 GPU 核心频率。", true, 12, "GPU");
        yield return GpuMemoryMetric("dashboard.gpu.memory.usage", gpu, true, 13, "GPU");
        yield return SensorMetric("dashboard.gpu.power", HardwareMetricCategory.Gpu, "GPU Package Power", "GPU Package Power", gpu?.PowerPackage, "当前选中 GPU 功耗。", true, 14, "GPU");
    }

    private IEnumerable<HardwareMetric> BuildMemorySummaryMetrics()
    {
        MemoryStatusSnapshot? status = MemoryStatusService.GetCurrentStatus();
        ulong? used = status is null ? null : status.TotalPhysical - status.AvailablePhysical;
        string? configuredClock = FindMemoryConfiguredClock(CurrentSnapshot);

        yield return ValueMetric("dashboard.memory.load", "dashboard-memory", HardwareMetricCategory.Memory, "物理内存使用率", "Memory Load", status?.MemoryLoad, "%", "Windows GlobalMemoryStatusEx", "当前物理内存使用率。", true, 20, "内存");
        yield return ValueMetric("dashboard.memory.used.total", "dashboard-memory", HardwareMetricCategory.Memory, "已用 / 总量", "Memory Used / Total", FormatUsedTotal(used, status?.TotalPhysical), string.Empty, "Windows GlobalMemoryStatusEx", "已用与总物理内存。", true, 21, "内存");
        yield return ValueMetric("dashboard.memory.available", "dashboard-memory", HardwareMetricCategory.Memory, "可用内存", "Memory Available", status?.AvailablePhysical, "B", "Windows GlobalMemoryStatusEx", "当前可用物理内存。", true, 22, "内存");
        yield return ValueMetric("dashboard.memory.configured.clock", "dashboard-memory", HardwareMetricCategory.Memory, "ConfiguredClockSpeed", "ConfiguredClockSpeed", configuredClock, "MHz", "WMI Win32_PhysicalMemory", "内存配置频率，不是实时频率。", false, 23, "内存");
    }

    private IEnumerable<HardwareMetric> BuildDiskSummaryMetrics()
    {
        DiskDevice? disk = SelectedDisk ?? diskDeviceService.SelectPreferredDisk(DiskDevices, settings.PreferredDiskId);
        string? health = ViewModelHelpers.FirstAvailable(disk?.SmartStatus, disk?.NvmeHealthStatus, disk?.HealthStatus?.Value?.ToString(CultureInfo.InvariantCulture));

        yield return ValueMetric("dashboard.disk.system.usage", disk?.Id ?? "dashboard-disk", HardwareMetricCategory.Disk, "硬盘使用率", "Disk Usage", disk?.UsagePercent, "%", "WMI", "选中硬盘已用空间比例。", true, 30, "硬盘");
        yield return ValueMetric("dashboard.disk.read.speed", disk?.Id ?? "dashboard-disk", HardwareMetricCategory.Disk, "当前读速率", "Disk Read Speed", disk?.ReadSpeed, "B/s", "PerformanceCounter", "选中硬盘读取吞吐。", true, 31, "硬盘");
        yield return ValueMetric("dashboard.disk.write.speed", disk?.Id ?? "dashboard-disk", HardwareMetricCategory.Disk, "当前写速率", "Disk Write Speed", disk?.WriteSpeed, "B/s", "PerformanceCounter", "选中硬盘写入吞吐。", true, 32, "硬盘");
        yield return ValueMetric("dashboard.disk.temperature.max", disk?.Id ?? "dashboard-disk", HardwareMetricCategory.Disk, "硬盘温度", "Storage Temperature", disk?.Temperature?.Value, "C", "LibreHardwareMonitor", "选中硬盘温度。", true, 33, "硬盘");
        yield return ValueMetric("dashboard.disk.health", "dashboard-disk", HardwareMetricCategory.Disk, "健康状态", "Health Status", health, string.Empty, "MSFT_PhysicalDisk / SMART", "硬盘健康状态。", false, 34, "硬盘");
    }

    private IEnumerable<HardwareMetric> BuildNetworkSummaryMetrics(NetworkAdapterDevice? adapter)
    {
        yield return ValueMetric("dashboard.network.active.adapter", "dashboard-network", HardwareMetricCategory.Network, "当前活动网卡", "Active Adapter", adapter?.Name, string.Empty, adapter?.Source ?? "System.Net.NetworkInformation", "当前优先显示的活动网卡。", true, 40, "网络");
        yield return ValueMetric("dashboard.network.download.speed", "dashboard-network", HardwareMetricCategory.Network, "下载速度", "Download Speed", adapter?.DownloadSpeed, "B/s", adapter?.Source ?? "System.Net.NetworkInformation", "当前下载速度。", true, 41, "网络");
        yield return ValueMetric("dashboard.network.upload.speed", "dashboard-network", HardwareMetricCategory.Network, "上传速度", "Upload Speed", adapter?.UploadSpeed, "B/s", adapter?.Source ?? "System.Net.NetworkInformation", "当前上传速度。", true, 42, "网络");
        yield return ValueMetric("dashboard.network.ipv4", "dashboard-network", HardwareMetricCategory.Network, "IPv4 地址", "IPv4Address", adapter is null ? null : JoinOrNull(adapter.IPv4Addresses), string.Empty, adapter?.Source ?? "System.Net.NetworkInformation", "当前活动网卡 IPv4 地址。", true, 43, "网络");
    }

    private IEnumerable<HardwareMetric> BuildSystemSummaryMetrics()
    {
        HardwareDevice? system = FindDevice("Win32_ComputerSystem", SensorCategory.Unknown);
        HardwareDevice? board = FindDevice("Win32_BaseBoard", SensorCategory.Motherboard);
        HardwareDevice? bios = FindDevice("Win32_BIOS", SensorCategory.Unknown);

        yield return ValueMetric("dashboard.system.device.model", "dashboard-system", HardwareMetricCategory.System, "设备型号", "ComputerSystem Model", ViewModelHelpers.FirstAvailable(Prop(system, "Model"), CurrentSnapshot?.ComputerName), string.Empty, "WMI Win32_ComputerSystem", "设备型号。", true, 50, "系统");
        yield return ValueMetric("dashboard.system.motherboard.model", "dashboard-system", HardwareMetricCategory.System, "主板型号", "BaseBoard Product", ViewModelHelpers.FirstAvailable(Prop(board, "Product"), board?.Model, CurrentSnapshot?.MotherboardName), string.Empty, "WMI Win32_BaseBoard", "主板型号。", true, 51, "系统");
        yield return ValueMetric("dashboard.system.bios.version", "dashboard-system", HardwareMetricCategory.System, "BIOS 版本", "BIOS Version", ViewModelHelpers.FirstAvailable(Prop(bios, "SMBIOSBIOSVersion"), bios?.Model, CurrentSnapshot?.BiosInfo), string.Empty, "WMI Win32_BIOS", "BIOS/UEFI 版本。", true, 52, "系统");
        yield return ValueMetric("dashboard.system.permission", "dashboard-system", HardwareMetricCategory.System, "当前权限状态", "Process Elevation", SensorRuntimeDiagnostics.IsAdministrator() ? "管理员" : "普通用户", string.Empty, "WindowsIdentity", "HardwareVision 当前进程权限。", true, 53, "系统");
    }

    private string? FindDeviceName(SensorCategory category)
    {
        return CurrentSensorReadings.FirstOrDefault(reading => reading.Category == category)?.DeviceName;
    }

    private HardwareDevice? FindDevice(string hardwareSource, SensorCategory fallbackCategory)
    {
        return CurrentSnapshot?.Devices.FirstOrDefault(device => string.Equals(Prop(device, "HardwareSource"), hardwareSource, StringComparison.OrdinalIgnoreCase))
            ?? CurrentSnapshot?.Devices.FirstOrDefault(device => fallbackCategory != SensorCategory.Unknown && device.Category == fallbackCategory);
    }

    private static NetworkAdapterDevice? SelectActiveNetworkAdapter(IEnumerable<NetworkAdapterDevice> adapters, bool includeVirtual)
    {
        return adapters
            .Where(adapter => includeVirtual || !adapter.IsVirtual)
            .OrderByDescending(adapter => adapter.IsUp)
            .ThenByDescending(adapter => adapter.IPv4Addresses.Count > 0)
            .ThenByDescending(adapter => !string.IsNullOrWhiteSpace(adapter.Gateway))
            .ThenBy(adapter => adapter.IsVirtual)
            .FirstOrDefault();
    }

    private HardwareMetric SensorMetric(
        string id,
        HardwareMetricCategory category,
        string displayName,
        string technicalName,
        SensorReading? reading,
        string description,
        bool important,
        int order,
        string groupName)
    {
        return ConfigureMetric(HardwareMetricService.FromSensorReading(
            id,
            reading?.DeviceName ?? "dashboard",
            category,
            displayName,
            technicalName,
            reading,
            description,
            important,
            true,
            order,
            groupName,
            settings,
            reading?.Source ?? "LibreHardwareMonitor"));
    }

    private HardwareMetric ValueMetric(
        string id,
        string hardwareId,
        HardwareMetricCategory category,
        string displayName,
        string technicalName,
        object? value,
        string unit,
        string source,
        string description,
        bool important,
        int order,
        string groupName)
    {
        string? textValue = ViewModelHelpers.ToMetricValue(value);
        return ConfigureMetric(HardwareMetricService.FromValue(
            id,
            hardwareId,
            category,
            displayName,
            technicalName,
            textValue,
            unit,
            source,
            string.IsNullOrWhiteSpace(textValue) ? MetricAvailability.NotReported : MetricAvailability.Available,
            description,
            important,
            true,
            order,
            groupName,
            settings));
    }

    private HardwareMetric GpuMemoryMetric(string id, GpuDevice? gpu, bool important, int order, string groupName)
    {
        double? usedBytes = ViewModelHelpers.SensorValueToBytes(gpu?.MemoryUsed);
        double? totalBytes = ViewModelHelpers.SensorValueToBytes(gpu?.MemoryTotal)
            ?? gpu?.AdapterRam;

        string? value = usedBytes.HasValue && totalBytes.HasValue
            ? $"{MetricFormatService.FormatBytesAuto((ulong)usedBytes.Value)} / {MetricFormatService.FormatBytesAuto((ulong)totalBytes.Value)}"
            : null;

        return ValueMetric(id, gpu?.Id ?? "gpu", HardwareMetricCategory.Gpu, "GPU Memory Used / Total", "GPU Memory Used / Total", value, string.Empty, gpu?.Source ?? "LibreHardwareMonitor / WMI", "当前选中 GPU 显存使用量。", important, order, groupName);
    }

    private static string? FindMemoryConfiguredClock(HardwareSnapshot? snapshot)
    {
        return snapshot?.Devices
            .Where(device => device.Category == SensorCategory.Memory)
            .Select(device => ViewModelHelpers.FirstAvailable(Prop(device, "ConfiguredClockSpeedMHz"), Prop(device, "ConfiguredClockSpeed"), Prop(device, "SpeedMHz"), Prop(device, "Speed")))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        double[] numericValues = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return numericValues.Length == 0 ? null : numericValues.Average();
    }

    private static string? FormatUsedTotal(ulong? used, ulong? total)
    {
        return used.HasValue && total.HasValue
            ? $"{MetricFormatService.FormatBytesAuto(used)} / {MetricFormatService.FormatBytesAuto(total)}"
            : null;
    }

    private static string? JoinOrNull(IEnumerable<string> values)
    {
        string[] filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return filtered.Length == 0 ? null : string.Join(", ", filtered);
    }

    private static string? Prop(HardwareDevice? device, string key)
    {
        return device?.Properties.TryGetValue(key, out string? value) == true ? value : null;
    }
}
