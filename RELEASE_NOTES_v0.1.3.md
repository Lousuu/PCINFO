# HardwareVision v0.1.3

本版本修复 CPU 和 GPU 在低负载时曲线难以看见的问题。

## 修复内容

- CPU 和 GPU 负载曲线采用自适应纵坐标，低负载时从 `0–10%` 开始展示。
- 负载升高时纵坐标按 `10%` 逐级扩展，最高保持为 `100%`。
- 修正曲线控件固定测量高度与页面实际高度不一致造成的底部裁剪。
- 保持现有硬件采集、历史采样、0.5 秒前台刷新、页面导航和传感器读取逻辑不变。

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

`88745FC5906CDDA4031E30640C3850D09DDDB9FEBE987BC549286C6C9FC4F2C8`
