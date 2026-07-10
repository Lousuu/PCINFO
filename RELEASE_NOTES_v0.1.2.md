# HardwareVision v0.1.2

本版本修复 CPU 和 GPU 实时负载曲线缺少历史折线的问题。

## 修复内容

- CPU 和 GPU 历史采样从程序开始刷新时持续累计，不再等到用户进入对应页面后才开始。
- 页面未打开时仅记录轻量历史点，指标列表和设备明细仍只在页面打开时刷新。
- 消除 GPU 同一轮传感器更新中的重复曲线采样。
- 切换 GPU 时清理旧设备曲线，并从新设备读数重新开始记录。
- 保持现有硬件采集、0.5 秒前台刷新、页面导航和传感器读取逻辑不变。

## 发布方式

- Windows x64 Framework-dependent 单文件版本。
- Release 手动资产仅包含 `HardwareVision.exe`。
- 不捆绑完整 .NET Runtime。
- 保持默认管理员权限启动。
- PresentMon Console 2.5.1 继续在首次开始游戏性能采集时按需释放。

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

- 本版本尚未进行数字签名，Windows SmartScreen 可能显示未知发布者。
- 部分传感器取决于硬件、BIOS、驱动和厂商支持。
- 游戏性能采集组件释放失败时会降级显示状态，不影响其他硬件监控功能。

## SHA-256

`2DEE48063172631153E11640F92F76597F537CE1009D1A19BF645B4F5E753953`
