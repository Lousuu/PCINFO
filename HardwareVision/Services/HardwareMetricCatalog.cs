using HardwareVision.Models;

namespace HardwareVision.Services;

public sealed class HardwareMetricCatalogItem
{
    public HardwareMetricCatalogItem(string pageKey, string pageTitle, HardwareMetric metric)
    {
        PageKey = pageKey;
        PageTitle = pageTitle;
        Metric = metric;
    }

    public string PageKey { get; }

    public string PageTitle { get; }

    public HardwareMetric Metric { get; }
}

public static class HardwareMetricCatalog
{
    public static IReadOnlyList<HardwareMetricCatalogItem> GetDefaultMetrics()
    {
        List<HardwareMetricCatalogItem> items =
        [
            Dashboard("dashboard.cpu.temperature", "CPU Package Temperature", "CPU Package Temperature", "C", "LibreHardwareMonitor", "CPU package temperature.", true, true, 0),
            Dashboard("dashboard.cpu.load", "CPU Total Load", "CPU Total Load", "%", "LibreHardwareMonitor", "Total CPU load.", true, true, 1),
            Dashboard("dashboard.cpu.clock.average", "Average Core Clock", "Average Core Clock", "MHz", "LibreHardwareMonitor", "Average of available CPU core clock readings.", true, true, 2),
            Dashboard("dashboard.cpu.power", "CPU Package Power", "CPU Package Power", "W", "LibreHardwareMonitor", "CPU package power, when available.", true, true, 3),
            Dashboard("dashboard.gpu.temperature", "GPU Core Temperature", "GPU Core Temperature", "C", "LibreHardwareMonitor", "Selected GPU core temperature.", true, true, 10),
            Dashboard("dashboard.gpu.load", "GPU Core Load", "GPU Core Load", "%", "LibreHardwareMonitor", "Selected GPU core load.", true, true, 11),
            Dashboard("dashboard.gpu.clock", "GPU Core Clock", "GPU Core Clock", "MHz", "LibreHardwareMonitor", "Selected GPU core clock.", true, true, 12),
            Dashboard("dashboard.gpu.memory.usage", "GPU Memory Used / Total", "GPU Memory Used / Total", "GB", "LibreHardwareMonitor / WMI", "Selected GPU memory usage.", true, true, 13),
            Dashboard("dashboard.gpu.power", "GPU Package Power", "GPU Package Power", "W", "LibreHardwareMonitor", "Selected GPU package or board power.", true, true, 14),
            Dashboard("dashboard.memory.load", "物理内存使用率", "Memory Load", "%", "LibreHardwareMonitor", "Physical memory usage.", true, true, 20),
            Dashboard("dashboard.memory.used.total", "已用 / 总量", "Memory Used / Total", "GB", "LibreHardwareMonitor / WMI", "Used and total physical memory.", true, true, 21),
            Dashboard("dashboard.memory.available", "可用内存", "Memory Available", "GB", "LibreHardwareMonitor", "Available physical memory.", true, true, 22),
            Dashboard("dashboard.memory.configured.clock", "ConfiguredClockSpeed", "ConfiguredClockSpeed", "MHz", "WMI Win32_PhysicalMemory", "Configured memory clock, not realtime frequency.", false, true, 23),
            Dashboard("dashboard.disk.system.usage", "系统盘使用率", "System Disk Usage", "%", "WMI", "Used space ratio of the system disk.", true, true, 30),
            Dashboard("dashboard.disk.read.speed", "当前读速率", "Disk Read Speed", "B/s", "PerformanceCounter", "Current read throughput of the system disk.", true, true, 31),
            Dashboard("dashboard.disk.write.speed", "当前写速率", "Disk Write Speed", "B/s", "PerformanceCounter", "Current write throughput of the system disk.", true, true, 32),
            Dashboard("dashboard.disk.temperature.max", "最高硬盘温度", "Max Storage Temperature", "C", "LibreHardwareMonitor", "Highest available storage temperature.", true, true, 33),
            Dashboard("dashboard.disk.health", "健康状态", "Health Status", "", "MSFT_PhysicalDisk / SMART / LibreHardwareMonitor", "Storage health summary.", false, true, 34),
            Dashboard("dashboard.network.active.adapter", "当前活动网卡", "Active Adapter", "", "System.Net.NetworkInformation", "Current active network adapter.", true, true, 40),
            Dashboard("dashboard.network.download.speed", "下载速度", "Download Speed", "B/s", "System.Net.NetworkInformation", "Current download speed.", true, true, 41),
            Dashboard("dashboard.network.upload.speed", "上传速度", "Upload Speed", "B/s", "System.Net.NetworkInformation", "Current upload speed.", true, true, 42),
            Dashboard("dashboard.network.ipv4", "IPv4 地址", "IPv4Address", "", "System.Net.NetworkInformation / WMI", "IPv4 address of the current active adapter.", true, true, 43),
            Dashboard("dashboard.system.device.model", "设备型号", "ComputerSystem Model", "", "WMI Win32_ComputerSystem", "Computer system model.", true, true, 50),
            Dashboard("dashboard.system.motherboard.model", "主板型号", "BaseBoard Product", "", "WMI Win32_BaseBoard", "Motherboard model.", true, true, 51),
            Dashboard("dashboard.system.bios.version", "BIOS 版本", "BIOS Version", "", "WMI Win32_BIOS", "BIOS/UEFI version.", true, true, 52),
            Dashboard("dashboard.system.permission", "当前权限状态", "Process Elevation", "", "WindowsIdentity", "Current HardwareVision process permission.", true, true, 53),

            Cpu("cpu.temperature.current", "当前温度", "CPU Temperature", "C", "LibreHardwareMonitor", "Current available CPU temperature reading.", true, true, 0),
            Cpu("cpu.load.total", "总负载", "CPU Total Load", "%", "LibreHardwareMonitor", "Total CPU load.", true, true, 1),
            Cpu("cpu.clock.current", "当前频率", "CPU Clock", "MHz", "LibreHardwareMonitor / WMI", "Current CPU clock.", true, true, 2),
            Cpu("cpu.power.package", "当前功耗", "CPU Package Power", "W", "LibreHardwareMonitor", "CPU package power.", true, true, 3),
            Cpu("cpu.core.count", "核心数量", "NumberOfCores", "", "WMI", "Win32_Processor.NumberOfCores.", false, true, 10),
            Cpu("cpu.thread.count", "线程数量", "NumberOfLogicalProcessors", "", "WMI", "Win32_Processor.NumberOfLogicalProcessors.", false, true, 11),
            Cpu("cpu.per.core.load", "每核心负载", "Per-Core Load", "%", "LibreHardwareMonitor", "Per-core CPU load readings.", false, true, 20),
            Cpu("cpu.per.core.clock", "每核心频率", "Per-Core Clock", "MHz", "LibreHardwareMonitor", "Per-core CPU clock readings.", false, true, 21),

            Gpu("gpu.hardware.type", "硬件类型", "HardwareType", "", "LibreHardwareMonitor / WMI", "GPU hardware type.", false, true, 0),
            Gpu("gpu.driver.version", "驱动版本", "DriverVersion", "", "WMI", "Win32_VideoController.DriverVersion.", false, true, 1),
            Gpu("gpu.adapter.ram", "适配器显存", "AdapterRAM", "", "WMI", "Adapter memory reported by the driver.", false, true, 2),
            Gpu("gpu.source", "数据来源", "Source", "", "HardwareVision", "Merged GPU data source.", false, true, 3),
            Gpu("gpu.availability", "可用性", "Availability", "", "HardwareVision", "Whether GPU sensor data is available.", false, true, 4),
            Gpu("gpu.temperature.core", "核心温度", "GPU Core Temperature", "C", "LibreHardwareMonitor", "GPU core temperature.", true, true, 10),
            Gpu("gpu.temperature.hotspot", "热点温度", "GPU Hot Spot Temperature", "C", "LibreHardwareMonitor", "GPU hot spot temperature.", false, false, 11),
            Gpu("gpu.temperature.memory.junction", "显存结温", "GPU Memory Junction Temperature", "C", "LibreHardwareMonitor", "GPU memory junction temperature.", false, false, 12),
            Gpu("gpu.load.core", "核心负载", "GPU Core Load", "%", "LibreHardwareMonitor", "GPU core load.", true, true, 13),
            Gpu("gpu.load.memory", "显存负载", "GPU Memory Load", "%", "LibreHardwareMonitor", "GPU memory or controller load.", false, false, 14),
            Gpu("gpu.memory.used", "显存使用", "GPU Memory Used", "GB", "LibreHardwareMonitor", "Used GPU memory.", true, true, 15),
            Gpu("gpu.memory.total", "显存总量", "GPU Memory Total", "GB", "LibreHardwareMonitor / WMI", "Total GPU memory.", false, true, 16),
            Gpu("gpu.clock.core", "核心频率", "GPU Core Clock", "MHz", "LibreHardwareMonitor", "GPU core clock.", true, true, 17),
            Gpu("gpu.clock.memory", "显存频率", "GPU Memory Clock", "MHz", "LibreHardwareMonitor", "GPU memory clock.", false, false, 18),
            Gpu("gpu.power.package", "当前功耗", "GPU Power", "W", "LibreHardwareMonitor", "GPU power.", true, true, 19),
            Gpu("gpu.voltage.core", "核心电压", "GPU Core Voltage", "V", "LibreHardwareMonitor", "GPU core voltage.", false, false, 20),
            Gpu("gpu.fan.speed", "风扇转速", "GPU Fan Speed", "RPM", "LibreHardwareMonitor", "GPU fan speed.", true, true, 21),
            Gpu("gpu.pcie.rx", "PCIe Rx", "PCIe Receive Throughput", "KB/s", "LibreHardwareMonitor", "PCIe receive throughput.", false, false, 22),
            Gpu("gpu.pcie.tx", "PCIe Tx", "PCIe Transmit Throughput", "KB/s", "LibreHardwareMonitor", "PCIe transmit throughput.", false, false, 23),

            Memory("memory.physical.total", "总物理内存", "TotalPhysicalMemory", "GB", "WMI", "Win32_ComputerSystem.TotalPhysicalMemory.", true, true, 0),
            Memory("memory.physical.used", "已用物理内存", "Memory Used", "GB", "LibreHardwareMonitor", "Used physical memory.", true, true, 1),
            Memory("memory.physical.available", "可用物理内存", "Memory Available", "GB", "LibreHardwareMonitor", "Available physical memory.", true, true, 2),
            Memory("memory.physical.load", "物理内存使用率", "Memory Load", "%", "LibreHardwareMonitor", "Physical memory usage.", true, true, 3),
            Memory("memory.virtual.used", "虚拟内存已用", "Virtual Memory Used", "GB", "PerformanceCounter / Windows API", "Used virtual memory.", false, true, 10),
            Memory("memory.virtual.available", "虚拟内存可用", "Virtual Memory Available", "GB", "PerformanceCounter / Windows API", "Available virtual memory.", false, true, 11),
            Memory("memory.virtual.load", "虚拟内存使用率", "Virtual Memory Load", "%", "PerformanceCounter / Windows API", "Virtual memory usage.", false, true, 12),
            Memory("memory.module.bank", "插槽位置", "BankLabel / DeviceLocator", "", "WMI", "Memory module slot or bank label.", true, true, 20),
            Memory("memory.module.capacity", "容量", "Capacity", "GB", "WMI", "Single module capacity.", true, true, 21),
            Memory("memory.module.manufacturer", "厂商", "Manufacturer", "", "WMI", "Memory module manufacturer.", false, true, 22),
            Memory("memory.module.part.number", "PartNumber", "PartNumber", "", "WMI", "Memory module part number.", false, true, 23),
            Memory("memory.module.serial", "SerialNumber", "SerialNumber", "", "WMI", "Memory module serial number, hidden by default.", false, false, 24),
            Memory("memory.module.speed", "Speed", "Speed", "MHz", "WMI", "Nominal or module speed, not realtime frequency.", true, true, 25),
            Memory("memory.module.configured.clock", "ConfiguredClockSpeed", "ConfiguredClockSpeed", "MHz", "WMI", "Configured memory clock, not realtime frequency.", true, true, 26),
            Memory("memory.module.form.factor", "FormFactor", "FormFactor", "", "WMI", "Memory module form factor.", false, true, 27),
            Memory("memory.module.memory.type", "MemoryType / SMBIOSMemoryType", "MemoryType / SMBIOSMemoryType", "", "WMI", "Memory type reported by WMI.", false, true, 28),
            Memory("memory.module.data.width", "DataWidth", "DataWidth", "bit", "WMI", "Memory module data width.", false, false, 29),
            Memory("memory.module.total.width", "TotalWidth", "TotalWidth", "bit", "WMI", "Total width including ECC bits.", false, false, 30),
            Memory("memory.module.count", "已安装内存条数量", "InstalledModuleCount", "", "WMI", "Installed physical memory module count.", true, true, 40),
            Memory("memory.slot.count", "插槽数量", "MemoryDevices", "", "WMI", "Win32_PhysicalMemoryArray.MemoryDevices.", false, true, 41),
            Memory("memory.channel.count", "通道数", "InferredChannelCount", "", "WMI", "Shown only when it can be inferred reasonably.", false, true, 42),
            Memory("memory.capacity.distribution", "单条容量分布", "ModuleCapacityDistribution", "", "WMI", "Memory module capacity distribution.", false, true, 43),
            Memory("memory.frequency.note", "内存频率说明", "MemoryFrequencyNote", "", "HardwareVision", "Speed is nominal; ConfiguredClockSpeed is configured clock; neither is realtime frequency.", false, true, 44),
            Memory("memory.power", "内存功耗", "Memory Power", "W", "LibreHardwareMonitor", "Memory power when exposed by the platform.", false, false, 50),

            Disk("disk.count", "硬盘数量", "Disk Count", "", "WMI", "Detected physical disk count.", true, true, 0),
            Disk("disk.system", "系统盘", "System Disk", "", "WMI", "Current system disk.", true, true, 1),
            Disk("disk.capacity.total", "总容量", "Total Disk Size", "GB", "WMI", "Total capacity of all disks.", true, true, 2),
            Disk("disk.capacity.used", "已用容量", "Used Space", "GB", "WMI", "Used volume space.", true, true, 3),
            Disk("disk.capacity.free", "可用容量", "Free Space", "GB", "WMI", "Free volume space.", true, true, 4),
            Disk("disk.temperature.max", "最高硬盘温度", "Max Storage Temperature", "C", "LibreHardwareMonitor", "Highest current storage temperature.", true, true, 5),
            Disk("disk.health", "健康状态", "Health Status", "", "LibreHardwareMonitor / MSFT_PhysicalDisk", "Disk health status.", true, true, 6),
            Disk("disk.model", "型号", "Model", "", "WMI / LibreHardwareMonitor", "Disk model.", true, true, 10),
            Disk("disk.interface.type", "接口类型", "InterfaceType / BusType", "", "WMI / MSFT_PhysicalDisk", "Disk interface or bus type.", true, true, 11),
            Disk("disk.volume.letters", "分区盘符", "Volumes", "", "WMI", "Associated volumes and drive letters.", true, true, 12),
            Disk("disk.temperature.current", "温度", "Storage Temperature", "C", "LibreHardwareMonitor", "Current disk temperature.", true, true, 13),
            Disk("disk.read.speed", "读取速率", "Disk Read Speed", "B/s", "PerformanceCounter", "Current disk read throughput.", true, true, 14),
            Disk("disk.write.speed", "写入速率", "Disk Write Speed", "B/s", "PerformanceCounter", "Current disk write throughput.", true, true, 15),
            Disk("disk.usage.percent", "使用率", "Usage Percent", "%", "WMI", "Usage ratio calculated from associated volume space.", true, true, 16),
            Disk("disk.firmware", "固件版本", "FirmwareRevision", "", "WMI / MSFT_PhysicalDisk", "Disk firmware revision.", false, true, 20),
            Disk("disk.serial", "序列号", "SerialNumber", "", "WMI", "Disk serial number, hidden by default.", false, false, 30),
            Disk("disk.power.on.hours", "PowerOnHours", "Power On Hours", "h", "LibreHardwareMonitor / SMART", "Power-on hours when available.", false, true, 31),
            Disk("disk.power.cycle.count", "PowerCycleCount", "Power Cycle Count", "", "LibreHardwareMonitor / SMART", "Power cycle count when available.", false, true, 32),
            Disk("disk.smart.status", "SMART 状态", "SMART Status", "", "WMI / MSFT_PhysicalDisk", "SMART or storage subsystem status.", false, true, 33),
            Disk("disk.nvme.health", "NVMe 健康状态", "NVMe Health Status", "", "MSFT_PhysicalDisk", "NVMe or Storage Spaces health status.", false, true, 34),
            Disk("disk.read.total", "读取总量", "Read Total", "", "LibreHardwareMonitor / SMART", "Cumulative read amount when available.", false, true, 35),
            Disk("disk.write.total", "写入总量", "Write Total", "", "LibreHardwareMonitor / SMART", "Cumulative write amount when available.", false, true, 36),
            Disk("disk.power", "硬盘功耗", "Storage Power", "W", "LibreHardwareMonitor", "Storage power when available.", false, false, 40),

            Network("network.active.adapter", "当前活动网卡", "Active Adapter", "", "System.Net.NetworkInformation", "Preferred current network adapter.", true, true, 0),
            Network("network.ipv4", "IPv4 地址", "IPv4Address", "", "System.Net.NetworkInformation / WMI", "Selected adapter IPv4 addresses.", true, true, 1),
            Network("network.gateway", "网关", "Gateway", "", "System.Net.NetworkInformation / WMI", "Selected adapter default gateway.", true, true, 2),
            Network("network.download.speed", "下载速度", "Download Speed", "B/s", "System.Net.NetworkInformation / LibreHardwareMonitor / PerformanceCounter", "Current download speed.", true, true, 3),
            Network("network.upload.speed", "上传速度", "Upload Speed", "B/s", "System.Net.NetworkInformation / LibreHardwareMonitor / PerformanceCounter", "Current upload speed.", true, true, 4),
            Network("network.link.speed", "连接速度", "Link Speed", "bps", "System.Net.NetworkInformation / WMI", "Adapter link speed.", true, true, 5),
            Network("network.utilization", "网络利用率", "Network Utilization", "%", "System.Net.NetworkInformation / LibreHardwareMonitor", "Network utilization calculated from realtime throughput and link speed.", true, true, 6),
            Network("network.adapter.type", "类型", "Interface Type", "", "System.Net.NetworkInformation / WMI", "Wired, wireless, virtual, or VPN.", true, true, 10),
            Network("network.adapter.status", "状态", "Operational Status", "", "System.Net.NetworkInformation / WMI", "Adapter operational status.", true, true, 11),
            Network("network.adapter.ipv4", "IPv4", "IPv4Address", "", "System.Net.NetworkInformation / WMI", "IPv4 addresses in the adapter list.", true, true, 12),
            Network("network.mac", "MAC 地址", "MACAddress", "", "WMI / System.Net.NetworkInformation", "MAC address, hidden by default.", false, false, 13),
            Network("network.adapter.link.speed", "链路速度", "Link Speed", "bps", "System.Net.NetworkInformation / WMI", "Adapter list link speed.", true, true, 14),
            Network("network.adapter.download.speed", "下载", "Download Speed", "B/s", "System.Net.NetworkInformation / LibreHardwareMonitor / PerformanceCounter", "Adapter realtime download speed.", true, true, 15),
            Network("network.adapter.upload.speed", "上传", "Upload Speed", "B/s", "System.Net.NetworkInformation / LibreHardwareMonitor / PerformanceCounter", "Adapter realtime upload speed.", true, true, 16),
            Network("network.dns", "DNS", "DNSServerSearchOrder", "", "System.Net.NetworkInformation / WMI", "DNS server list.", false, true, 30),
            Network("network.dhcp", "DHCP", "DHCPEnabled", "", "System.Net.NetworkInformation / WMI", "Whether DHCP is enabled on the selected adapter.", false, true, 31),
            Network("network.ipv6", "IPv6", "IPv6Address", "", "System.Net.NetworkInformation / WMI", "IPv6 addresses.", false, false, 32),
            Network("network.interface.description", "接口描述", "Interface Description", "", "System.Net.NetworkInformation / WMI", "Interface description reported by the driver or system.", false, true, 33),
            Network("network.total.uploaded", "总上传", "Total Uploaded", "B", "System.Net.NetworkInformation / LibreHardwareMonitor", "Cumulative uploaded bytes.", false, true, 34),
            Network("network.total.downloaded", "总下载", "Total Downloaded", "B", "System.Net.NetworkInformation / LibreHardwareMonitor", "Cumulative downloaded bytes.", false, true, 35),
            Network("network.selected.utilization", "网络利用率", "Network Utilization", "%", "System.Net.NetworkInformation / LibreHardwareMonitor", "Selected adapter realtime utilization.", false, true, 36),
            Network("network.signal.quality", "信号质量", "Signal Quality", "%", "WMI", "Wireless signal quality when available.", false, false, 37),
            Network("network.source", "数据来源", "Source", "", "HardwareVision", "Merged network adapter data source.", false, true, 38),
            Network("network.availability", "可用性", "Availability", "", "HardwareVision", "Whether adapter data is available.", false, true, 39),

            Motherboard("motherboard.vendor", "主板厂商", "BaseBoard Manufacturer", "", "WMI Win32_BaseBoard", "Motherboard manufacturer.", true, true, 0),
            Motherboard("motherboard.name", "主板型号", "BaseBoard Product", "", "WMI Win32_BaseBoard", "Motherboard product/model.", true, true, 1),
            Motherboard("motherboard.version", "主板版本", "BaseBoard Version", "", "WMI Win32_BaseBoard", "Motherboard version.", false, true, 2),
            Motherboard("motherboard.serial", "主板序列号", "BaseBoard SerialNumber", "", "WMI Win32_BaseBoard", "Motherboard serial number, hidden by default.", false, false, 3),
            Motherboard("motherboard.system.vendor", "系统厂商", "ComputerSystem Manufacturer", "", "WMI Win32_ComputerSystem", "System manufacturer.", true, true, 4),
            Motherboard("motherboard.device.model", "设备型号", "ComputerSystem Model", "", "WMI Win32_ComputerSystem / ComputerSystemProduct", "Device model.", true, true, 5),
            Motherboard("motherboard.device.sku", "设备 SKU", "SystemSKUNumber / SKUNumber", "", "WMI Win32_ComputerSystem / ComputerSystemProduct", "Device SKU when available.", false, true, 6),
            Motherboard("motherboard.bios.vendor", "BIOS 厂商", "BIOS Manufacturer", "", "WMI Win32_BIOS", "BIOS vendor.", true, true, 10),
            Motherboard("motherboard.bios.version", "BIOS 版本", "SMBIOSBIOSVersion / Version", "", "WMI Win32_BIOS", "BIOS/UEFI version.", true, true, 11),
            Motherboard("motherboard.bios.release.date", "BIOS 发布日期", "ReleaseDate", "", "WMI Win32_BIOS", "BIOS/UEFI release date.", true, true, 12),
            Motherboard("motherboard.smbios.version", "SMBIOS 版本", "SMBIOSMajorVersion / SMBIOSMinorVersion", "", "WMI Win32_BIOS", "SMBIOS version.", false, true, 13),
            Motherboard("motherboard.bios.mode", "BIOS 模式", "Firmware Type", "", "Windows Firmware API", "UEFI or Legacy BIOS mode when available.", false, true, 14),
            Motherboard("motherboard.secure.boot", "Secure Boot", "SecureBoot", "", "Windows SecureBoot State", "Secure Boot state when available.", false, true, 15),
            Motherboard("motherboard.device.type", "设备类型", "PCSystemType / PCSystemTypeEx", "", "WMI Win32_ComputerSystem", "Device type reported by Windows.", true, true, 20),
            Motherboard("motherboard.chassis.type", "机箱类型", "ChassisTypes", "", "WMI Win32_SystemEnclosure", "Chassis type.", true, true, 21),
            Motherboard("motherboard.form.factor", "笔记本 / 台式机判断", "Inferred Form Factor", "", "HardwareVision", "Form factor inferred from WMI device and chassis data.", true, true, 22),
            Motherboard("motherboard.product.name", "产品名称", "Product Name", "", "WMI Win32_ComputerSystemProduct / ComputerSystem", "SMBIOS product name.", false, true, 23),
            Motherboard("motherboard.product.version", "产品版本", "Product Version", "", "WMI Win32_ComputerSystemProduct / SystemEnclosure", "SMBIOS product or chassis version.", false, true, 24),
            Motherboard("motherboard.temperature", "主板温度", "Motherboard Temperature", "C", "LibreHardwareMonitor Motherboard/SuperIO", "Motherboard temperature sensor.", true, true, 30),
            Motherboard("motherboard.chipset.temperature", "芯片组温度", "Chipset / PCH Temperature", "C", "LibreHardwareMonitor Motherboard/SuperIO", "Chipset, PCH, or VRM temperature.", false, true, 31),
            Motherboard("motherboard.fan.speed", "风扇转速", "Fan Speed", "RPM", "LibreHardwareMonitor Motherboard/SuperIO", "Fan speed exposed by the motherboard or Super I/O.", true, true, 32),
            Motherboard("motherboard.voltage", "电压", "Voltage", "V", "LibreHardwareMonitor Motherboard/SuperIO", "Voltage reading exposed by the motherboard or Super I/O.", false, true, 33),
            Motherboard("motherboard.ec.sensor", "EC 传感器", "Embedded Controller Sensors", "", "LibreHardwareMonitor EmbeddedController", "Embedded Controller sensor device names.", false, true, 34),
            Motherboard("motherboard.superio.chip", "Super I/O 芯片", "Super I/O Chip", "", "LibreHardwareMonitor SuperIO", "Super I/O chip names when available.", false, true, 35),

            Battery("battery.status", "电池状态", "Battery Status", "", "LibreHardwareMonitor / WMI", "Battery status.", true, true, 0),
            Battery("battery.charge", "电量", "Battery Charge", "%", "LibreHardwareMonitor / WMI", "Battery charge.", true, true, 1),
            Battery("battery.power", "电池功率", "Battery Power", "W", "LibreHardwareMonitor", "Battery charge or discharge power.", false, true, 2),
            System("system.computer.name", "计算机名称", "ComputerName", "", "Environment / WMI", "Current computer name.", true, true, 0),
            System("system.os.version", "系统版本", "OperatingSystem", "", "WMI", "Windows operating system version.", true, true, 1),
            System("system.current.user", "当前用户", "UserName", "", "Environment / WMI", "Current logged-in user.", false, true, 2)
        ];

        return items;
    }

    private static HardwareMetricCatalogItem Dashboard(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Dashboard", "首页摘要", HardwareMetricCategory.System, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Cpu(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Cpu", "CPU", HardwareMetricCategory.Cpu, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Gpu(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Gpu", "GPU", HardwareMetricCategory.Gpu, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Memory(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Memory", "内存", HardwareMetricCategory.Memory, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Disk(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Disk", "硬盘", HardwareMetricCategory.Disk, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Network(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Network", "网络", HardwareMetricCategory.Network, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Motherboard(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Motherboard", "主板", HardwareMetricCategory.Motherboard, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Battery(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("Battery", "电池", HardwareMetricCategory.Battery, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem System(string id, string displayName, string technicalName, string unit, string source, string description, bool isImportant, bool isVisible, int displayOrder)
    {
        return Create("System", "系统", HardwareMetricCategory.System, id, displayName, technicalName, unit, source, description, isImportant, isVisible, displayOrder);
    }

    private static HardwareMetricCatalogItem Create(
        string pageKey,
        string pageTitle,
        HardwareMetricCategory category,
        string id,
        string displayName,
        string technicalName,
        string unit,
        string source,
        string description,
        bool isImportant,
        bool isVisible,
        int displayOrder)
    {
        return new HardwareMetricCatalogItem(
            pageKey,
            pageTitle,
            new HardwareMetric
            {
                Id = HardwareMetricService.NormalizeMetricId(id),
                HardwareId = pageKey,
                Category = category,
                DisplayName = displayName,
                TechnicalName = technicalName,
                Value = HardwareMetricService.EmptyValue,
                Unit = unit,
                Source = source,
                Availability = MetricAvailability.Loading,
                Description = description,
                IsImportant = isImportant,
                IsVisible = isVisible,
                DisplayOrder = displayOrder,
                GroupName = pageTitle
            });
    }
}
