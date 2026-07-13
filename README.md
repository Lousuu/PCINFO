# HardwareVision

HardwareVision 是一款面向 Windows 的轻量硬件与游戏性能监控工具，使用 WPF 和 .NET 8 构建。

## 主要功能

- 查看 CPU、GPU、内存、硬盘、网络、主板和高级传感器信息。
- 以 0.5 秒默认刷新周期展示实时指标与历史曲线。
- 使用内嵌的 PresentMon 2.5.1 采集游戏 FPS、帧时间、1% Low、0.1% Low、CPU/GPU 帧耗时和显示延迟。
- 支持搜索进程、识别最近使用的游戏，并自动解析启动器对应的实际渲染子进程。
- 按游戏特征、窗口、安装目录和最近前台记录排序候选，同时隔离不同游戏采集会话。
- 支持托盘运行和设置持久化。

## 系统要求

- Windows 10 或 Windows 11 64 位。
- x64 处理器。
- [Microsoft .NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。
- 管理员权限；游戏性能采集依赖 ETW 和 PresentMon。

## 下载与运行

从 [GitHub Releases](https://github.com/Lousuu/PCINFO/releases) 下载最新的 `HardwareVision.exe`，确认已安装 .NET 8 Desktop Runtime x64 后运行，并在 UAC 提示中选择“是”。

发布文件为 Framework-dependent、非裁剪的 Windows x64 单文件程序。当前版本未进行数字签名，Windows SmartScreen 可能显示未知发布者。

## 本地开发

```powershell
git clone https://github.com/Lousuu/PCINFO.git
cd PCINFO
dotnet restore .\HardwareVision\HardwareVision.csproj
dotnet build .\HardwareVision\HardwareVision.csproj
dotnet run --project .\HardwareVision\HardwareVision.csproj
```

运行游戏性能逻辑测试：

```powershell
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
```

## 游戏性能数据口径

- 当前 FPS：最近约 1 秒有效帧的平均帧时间换算值。
- 平均 FPS：当前统计窗口内平均帧时间的倒数。
- 1% Low / 0.1% Low：最慢 1% / 0.1% 帧的平均 FPS，分别至少需要 100 / 1000 个有效样本。
- CPU：PresentMon `CPUBusy`。
- GPU：PresentMon `GPUTime`。
- 延迟：PresentMon `DisplayLatency`，目标或系统未报告时显示 `N/A`。

与 NVIDIA App 等工具对比时，采样窗口边界、刷新周期和帧筛选策略不同可能造成短时结果略有差异。

## 许可与第三方组件

PresentMon 的许可证与第三方声明随源码保存在 `HardwareVision/ThirdParty/PresentMon/2.5.1`，并在首次采集时随运行时组件一起释放。
