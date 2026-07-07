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

public sealed class MotherboardViewModel : ObservableObject, IDisposable
{
    private const string NoSensorDataMessage = "当前设备未向此传感器源提供主板传感器数据";
    private readonly DashboardViewModel? dashboard;
    private bool isActive;
    private bool isDisposed;
    private string motherboardName = "--";
    private string deviceSummary = "--";
    private bool noSensorData = true;
    private string sensorStatusMessage = NoSensorDataMessage;
    private IReadOnlyList<DetailSensorRowViewModel> sensorRows = Array.Empty<DetailSensorRowViewModel>();

    public MotherboardViewModel()
    {
    }

    public MotherboardViewModel(DashboardViewModel dashboard)
    {
        this.dashboard = dashboard;
    }

    public string MotherboardName
    {
        get => motherboardName;
        private set => SetProperty(ref motherboardName, value);
    }

    public string DeviceSummary
    {
        get => deviceSummary;
        private set => SetProperty(ref deviceSummary, value);
    }

    public bool NoSensorData
    {
        get => noSensorData;
        private set => SetProperty(ref noSensorData, value);
    }

    public string SensorStatusMessage
    {
        get => sensorStatusMessage;
        private set => SetProperty(ref sensorStatusMessage, value);
    }

    public ObservableCollection<DetailMetricViewModel> BoardMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> BiosMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> DeviceMetrics { get; } = new();

    public ObservableCollection<DetailMetricViewModel> SensorMetrics { get; } = new();

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
            Refresh();
        }
        else
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }
    }

    public void Dispose()
    {
        if (dashboard is not null)
        {
            dashboard.PropertyChanged -= OnDashboardPropertyChanged;
        }

        isDisposed = true;
    }

    private void OnDashboardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (isActive && e.PropertyName is nameof(DashboardViewModel.CurrentSnapshot) or nameof(DashboardViewModel.CurrentSensorReadings))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (dashboard is null)
        {
            return;
        }

        HardwareSnapshot? snapshot = dashboard.CurrentSnapshot;
        HardwareDevice? board = FindDevice(snapshot, "Win32_BaseBoard", SensorCategory.Motherboard);
        HardwareDevice? system = FindDevice(snapshot, "Win32_ComputerSystem", SensorCategory.Unknown);
        HardwareDevice? product = FindDevice(snapshot, "Win32_ComputerSystemProduct", SensorCategory.Unknown);
        HardwareDevice? enclosure = FindDevice(snapshot, "Win32_SystemEnclosure", SensorCategory.Unknown);
        HardwareDevice? bios = FindDevice(snapshot, "Win32_BIOS", SensorCategory.Unknown);
        SensorReading[] sensors = dashboard.CurrentSensorReadings.Where(reading => reading.Category == SensorCategory.Motherboard).ToArray();

        MotherboardName = ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(board, "Manufacturer"), ViewModelHelpers.Prop(board, "Product"), board?.Name, snapshot?.MotherboardName, "Motherboard")!;
        DeviceSummary = ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(system, "Manufacturer"), ViewModelHelpers.Prop(system, "Model"), ViewModelHelpers.Prop(product, "Name"), "--")!;

        ReplaceMetricCollection(BoardMetrics, BuildBoardMetrics(board, system, product).Select(dashboard.ConfigureMetric));
        ReplaceMetricCollection(BiosMetrics, BuildBiosMetrics(bios).Select(dashboard.ConfigureMetric));
        ReplaceMetricCollection(DeviceMetrics, BuildDeviceMetrics(system, product, enclosure).Select(dashboard.ConfigureMetric));
        ReplaceMetricCollection(SensorMetrics, BuildSensorMetrics(sensors).Select(dashboard.ConfigureMetric));
        SensorRows = sensors.Where(reading => reading.IsAvailable).Select(DetailSensorRowViewModel.FromReading).ToArray();
    }

    private IEnumerable<HardwareMetric> BuildBoardMetrics(HardwareDevice? board, HardwareDevice? system, HardwareDevice? product)
    {
        yield return Metric("motherboard.vendor", "主板厂商", "BaseBoard Manufacturer", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(board, "Manufacturer"), board?.Vendor), "WMI Win32_BaseBoard", "主板厂商。", true, true, 0, "主板");
        yield return Metric("motherboard.name", "主板型号", "BaseBoard Product", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(board, "Product"), board?.Model, board?.Name), "WMI Win32_BaseBoard", "主板型号。", true, true, 1, "主板");
        yield return Metric("motherboard.version", "主板版本", "BaseBoard Version", ViewModelHelpers.Prop(board, "Version"), "WMI Win32_BaseBoard", "主板版本。", false, true, 2, "主板");
        yield return Metric("motherboard.serial", "主板序列号", "BaseBoard SerialNumber", ViewModelHelpers.Prop(board, "SerialNumber"), "WMI Win32_BaseBoard", "主板序列号，默认隐藏。", false, false, 3, "主板");
        yield return Metric("motherboard.system.vendor", "系统厂商", "ComputerSystem Manufacturer", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(system, "Manufacturer"), system?.Vendor, ViewModelHelpers.Prop(product, "Vendor")), "WMI Win32_ComputerSystem", "整机系统厂商。", true, true, 4, "主板");
        yield return Metric("motherboard.device.model", "设备型号", "ComputerSystem Model", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(system, "Model"), system?.Model, ViewModelHelpers.Prop(product, "Name"), product?.Name), "WMI Win32_ComputerSystem / ComputerSystemProduct", "整机设备型号。", true, true, 5, "主板");
        yield return Metric("motherboard.device.sku", "设备 SKU", "SystemSKUNumber / SKUNumber", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(system, "SystemSKUNumber"), ViewModelHelpers.Prop(product, "SKUNumber")), "WMI Win32_ComputerSystem / ComputerSystemProduct", "设备 SKU。", false, true, 6, "主板");
    }

    private IEnumerable<HardwareMetric> BuildBiosMetrics(HardwareDevice? bios)
    {
        yield return Metric("motherboard.bios.vendor", "BIOS 厂商", "BIOS Manufacturer", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(bios, "Manufacturer"), bios?.Vendor), "WMI Win32_BIOS", "BIOS 厂商。", true, true, 10, "BIOS/UEFI");
        yield return Metric("motherboard.bios.version", "BIOS 版本", "SMBIOSBIOSVersion / Version", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(bios, "SMBIOSBIOSVersion"), bios?.Model, ViewModelHelpers.Prop(bios, "Version")), "WMI Win32_BIOS", "BIOS/UEFI 版本。", true, true, 11, "BIOS/UEFI");
        yield return Metric("motherboard.bios.release.date", "BIOS 发布日期", "ReleaseDate", ViewModelHelpers.Prop(bios, "ReleaseDate"), "WMI Win32_BIOS", "BIOS/UEFI 发布日期。", true, true, 12, "BIOS/UEFI");
        yield return Metric("motherboard.smbios.version", "SMBIOS 版本", "SMBIOSMajorVersion / SMBIOSMinorVersion", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(bios, "SMBIOSVersion"), JoinNonEmpty(".", ViewModelHelpers.Prop(bios, "SMBIOSMajorVersion"), ViewModelHelpers.Prop(bios, "SMBIOSMinorVersion"))), "WMI Win32_BIOS", "SMBIOS 版本。", false, true, 13, "BIOS/UEFI");
        yield return Metric("motherboard.bios.mode", "BIOS 模式", "Firmware Type", ViewModelHelpers.Prop(bios, "BiosMode"), "Windows Firmware API", "UEFI 或 Legacy BIOS 模式。", false, true, 14, "BIOS/UEFI");
        yield return Metric("motherboard.secure.boot", "Secure Boot", "SecureBoot", FormatSecureBoot(ViewModelHelpers.Prop(bios, "SecureBoot")), "Windows SecureBoot State", "Secure Boot 状态。", false, true, 15, "BIOS/UEFI");
    }

    private IEnumerable<HardwareMetric> BuildDeviceMetrics(HardwareDevice? system, HardwareDevice? product, HardwareDevice? enclosure)
    {
        string? deviceType = ResolveDeviceType(ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(system, "PCSystemTypeEx"), ViewModelHelpers.Prop(system, "PCSystemType")));
        string? chassisType = ViewModelHelpers.Prop(enclosure, "ChassisTypeNames");

        yield return Metric("motherboard.device.type", "设备类型", "PCSystemType / PCSystemTypeEx", deviceType, "WMI Win32_ComputerSystem", "Windows 报告的设备类型。", true, true, 20, "设备");
        yield return Metric("motherboard.chassis.type", "机箱类型", "ChassisTypes", chassisType, "WMI Win32_SystemEnclosure", "机箱类型。", true, true, 21, "设备");
        yield return Metric("motherboard.form.factor", "笔记本 / 台式机判断", "Inferred Form Factor", ResolveFormFactor(deviceType, chassisType), "HardwareVision", "根据设备类型和机箱类型推断。", true, true, 22, "设备");
        yield return Metric("motherboard.product.name", "产品名称", "Product Name", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(product, "Name"), product?.Name, ViewModelHelpers.Prop(system, "Model"), system?.Model), "WMI Win32_ComputerSystemProduct / ComputerSystem", "SMBIOS 产品名称。", false, true, 23, "设备");
        yield return Metric("motherboard.product.version", "产品版本", "Product Version", ViewModelHelpers.FirstAvailable(ViewModelHelpers.Prop(product, "Version"), ViewModelHelpers.Prop(enclosure, "Version")), "WMI Win32_ComputerSystemProduct / SystemEnclosure", "产品或机箱版本。", false, true, 24, "设备");
    }

    private IEnumerable<HardwareMetric> BuildSensorMetrics(IReadOnlyList<SensorReading> sensors)
    {
        NoSensorData = sensors.Count == 0;
        SensorStatusMessage = NoSensorData ? NoSensorDataMessage : $"{sensors.Count(reading => reading.IsAvailable)} 个可用主板传感器读数";

        yield return SensorMetric("motherboard.temperature", "主板温度", "Motherboard Temperature", HardwareDetailReadingHelpers.FindPreferredReading(sensors, SensorType.Temperature, "Motherboard", "Mainboard", "System", "Board"), "主板温度传感器。", true, true, 30);
        yield return SensorMetric("motherboard.chipset.temperature", "芯片组温度", "Chipset / PCH Temperature", HardwareDetailReadingHelpers.FindPreferredReading(sensors, SensorType.Temperature, "Chipset", "PCH", "VRM"), "芯片组、PCH 或 VRM 温度。", false, true, 31);
        yield return SensorMetric("motherboard.fan.speed", "风扇转速", "Fan Speed", HardwareDetailReadingHelpers.FindPreferredReading(sensors, SensorType.Fan, "Fan", "CPU", "Chassis", "System"), "主板或 Super I/O 暴露的风扇转速。", true, true, 32);
        yield return SensorMetric("motherboard.voltage", "电压", "Voltage", HardwareDetailReadingHelpers.FindPreferredReading(sensors, SensorType.Voltage, "Voltage", "VCore", "+12V", "+5V", "+3.3V"), "主板或 Super I/O 暴露的电压读数。", false, true, 33);
        yield return Metric("motherboard.ec.sensor", "EC 传感器", "Embedded Controller Sensors", JoinDistinct(sensors.Where(IsEcSensor).Select(reading => reading.DeviceName)), "LibreHardwareMonitor EmbeddedController", "Embedded Controller 传感器设备名。", false, true, 34, "传感器");
        yield return Metric("motherboard.superio.chip", "Super I/O 芯片", "Super I/O Chip", JoinDistinct(sensors.Where(IsSuperIoSensor).Select(reading => reading.DeviceName)), "LibreHardwareMonitor SuperIO", "Super I/O 芯片名称。", false, true, 35, "传感器");
    }

    private static HardwareDevice? FindDevice(HardwareSnapshot? snapshot, string source, SensorCategory fallbackCategory)
    {
        return snapshot?.Devices.FirstOrDefault(device => string.Equals(ViewModelHelpers.Prop(device, "HardwareSource"), source, StringComparison.OrdinalIgnoreCase))
            ?? snapshot?.Devices.FirstOrDefault(device => fallbackCategory != SensorCategory.Unknown && device.Category == fallbackCategory);
    }

    private static HardwareMetric SensorMetric(string id, string displayName, string technicalName, SensorReading? reading, string description, bool important, bool visible, int order)
    {
        return HardwareMetricService.FromSensorReading(id, "motherboard-sensors", HardwareMetricCategory.Motherboard, displayName, technicalName, reading, description, important, visible, order, "传感器", fallbackSource: reading?.Source ?? "LibreHardwareMonitor");
    }

    private static HardwareMetric Metric(string id, string displayName, string technicalName, string? value, string source, string description, bool important, bool visible, int order, string group)
    {
        return HardwareMetricService.FromValue(id, "motherboard", HardwareMetricCategory.Motherboard, displayName, technicalName, value, string.Empty, source, string.IsNullOrWhiteSpace(value) ? MetricAvailability.NotReported : MetricAvailability.Available, description, important, visible, order, group);
    }

    private static bool IsEcSensor(SensorReading reading)
    {
        string text = $"{reading.DeviceName} {reading.SensorName} {reading.RawIdentifier}";
        return text.Contains("EmbeddedController", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Embedded Controller", StringComparison.OrdinalIgnoreCase)
            || text.Contains("EC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSuperIoSensor(SensorReading reading)
    {
        string text = $"{reading.DeviceName} {reading.SensorName} {reading.RawIdentifier}";
        return text.Contains("SuperIO", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Super I/O", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Nuvoton", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ITE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Fintek", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Winbond", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDeviceType(string? value)
    {
        return value switch
        {
            "1" => "台式机",
            "2" => "移动设备",
            "3" => "工作站",
            "4" => "企业服务器",
            "5" => "SOHO 服务器",
            "6" => "Appliance PC",
            "7" => "性能服务器",
            "8" => "平板",
            _ => value
        };
    }

    private static string? ResolveFormFactor(string? deviceType, string? chassisType)
    {
        string text = $"{deviceType} {chassisType}";
        if (ViewModelHelpers.ContainsAny(text, "移动", "Laptop", "Notebook", "Portable", "Tablet", "Convertible", "Detachable", "平板"))
        {
            return "笔记本 / 移动设备";
        }

        if (ViewModelHelpers.ContainsAny(text, "Desktop", "Tower", "All-in-One", "台式", "工作站"))
        {
            return "台式机 / 一体机";
        }

        return null;
    }

    private static string? FormatSecureBoot(string? value)
    {
        return value switch
        {
            "Enabled" => "已启用",
            "Disabled" => "未启用",
            _ => value
        };
    }

    private static string? JoinDistinct(IEnumerable<string?> values)
    {
        string[] filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return filtered.Length == 0 ? null : string.Join(", ", filtered);
    }

    private static string? JoinNonEmpty(string separator, params string?[] values)
    {
        string[] filtered = values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).ToArray();
        return filtered.Length == 0 ? null : string.Join(separator, filtered);
    }
}
