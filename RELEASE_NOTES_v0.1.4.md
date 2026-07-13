# HardwareVision v0.1.4

本版本重点修复游戏性能页面无法稳定捕获首帧、PresentMon 提前退出、统计数据缺失以及瞬时 FPS 异常偏高的问题。

## 修复内容

- 完整适配 PresentMon 2.5.1 的 v2 CSV 字段，同时保留对旧格式字段的兼容。
- 增加明确的采集状态机，区分准备、等待首帧、采集中、目标退出、格式不兼容和采集失败等状态。
- 新增首行、CSV 表头、首个有效样本、丢弃原因和定期汇总诊断，便于定位权限、目标进程和输出格式问题。
- 自动从启动器进程解析到实际拥有窗口的渲染子进程，并在系统级采集结果中按目标 PID 过滤帧数据。
- 改为实时读取 PresentMon 标准输出，避免输出文件被独占锁定时无法消费帧数据。
- 启动采集前清理残留的 HardwareVision ETW 会话，并在停止时完整回收 PresentMon、读取任务和会话状态。
- 每次采集生成独立会话，清空旧样本和统计窗口，防止切换游戏后混入上一进程的数据。
- 保留 Swap Chain、帧类型、显示时间、Present 模式和运行时信息，为主渲染链和帧生成识别提供数据基础。
- 当前 FPS 改为最近约 1 秒有效帧时间的滚动统计，避免单个极短 Present 间隔显示为数千 FPS。
- 平均 FPS 使用平均帧时间换算；1% Low 和 0.1% Low 分别使用最慢 1% 与最慢 0.1% 帧的平均 FPS，样本不足时显示 `N/A`。
- CPU 忙碌时间、GPU 总时间和显示延迟按 PresentMon 原始语义分别统计，不再混用不兼容的延迟字段。
- 增加 16 项可重复逻辑测试，覆盖 v2/旧版 CSV、转义、缺失字段、数值过滤、会话隔离、低帧统计和滚动 FPS。

## 数据口径说明

- 当前 FPS：最近约 1 秒有效帧的平均帧时间换算值。
- 平均 FPS：当前统计窗口内平均帧时间的倒数。
- 1% Low / 0.1% Low：当前统计窗口内最慢 1% / 0.1% 帧的平均 FPS；分别至少需要 100 / 1000 个有效样本。
- CPU：`CPUBusy`。
- GPU：`GPUTime`，即 PresentMon 报告的 GPU Busy 与 GPU Wait 总时间。
- 延迟：`DisplayLatency`；目标或系统未报告时显示 `N/A`。
- 与 NVIDIA App 等工具对比时，因采样窗口边界、刷新周期和帧筛选策略不同，短时结果可能存在小幅差异。

## 发布方式

- Windows x64 Framework-dependent 单文件版本。
- Release 手动资产仅包含 `HardwareVision.exe`。
- 不捆绑完整 .NET Runtime。
- 保持默认管理员权限启动。
- PresentMon Console 2.5.1 在首次开始游戏性能采集时按需校验并释放。

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
- PresentMon 的可用数据受游戏渲染模式、权限、反作弊策略和驱动支持影响。
- `ClickToPhotonLatency`、帧类型等字段仅在系统与目标程序实际报告时可用。
- 部分传感器取决于硬件、BIOS、驱动和厂商支持。

## SHA-256

`4F477FA41DC2D1220D111CFE49045063C5963E60520768A3D45D444C5565F461`
