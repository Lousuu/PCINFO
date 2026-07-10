# HardwareVision v0.1.1

本版本将 Windows 发布包调整为轻量单文件版本。

## 主要变化

- 使用单个 `HardwareVision.exe` 分发。
- 显著降低 Release 下载体积。
- 不再捆绑完整 .NET Runtime。
- 保持默认管理员权限启动。
- 保持全部硬件监控和游戏性能功能。
- 保留现有硬件采集依赖，不通过裁剪削弱功能。
- PresentMon Console 2.5.1 在首次开始游戏性能采集时按需释放，不在应用启动时加载。

## 系统要求

- Windows 10 或 Windows 11 64 位。
- x64 处理器。
- Microsoft .NET 8 Desktop Runtime x64。
- 需要管理员权限运行。

Microsoft 官方下载页面：<https://dotnet.microsoft.com/en-us/download/dotnet/8.0>

## 运行方式

1. 下载 `HardwareVision.exe`。
2. 确认已安装 Microsoft .NET 8 Desktop Runtime x64。
3. 双击 `HardwareVision.exe`。
4. 在 Windows UAC 窗口中选择“是”。

## 注意事项

- 本版本默认请求管理员权限。
- 部分传感器取决于硬件、BIOS、驱动和厂商支持。
- 首次开始游戏性能采集时，应用会把已校验的 PresentMon 组件、许可证和第三方声明释放到 `%LOCALAPPDATA%\HardwareVision\Runtime\PresentMon\2.5.1`。
- PresentMon 释放失败时，游戏性能页面会降级显示状态，其他硬件监控功能不受影响。
- 本版本尚未进行数字签名，Windows SmartScreen 可能显示未知发布者。

PresentMon 许可证：<https://github.com/GameTechDev/PresentMon/blob/v2.5.1/LICENSE.txt>

## SHA-256

`EB8D67A494FA31FB5C95B54B1C53ADA0CC00E8D21E3EAF28237D4722F83E00D1`
