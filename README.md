# HardwareVision

HardwareVision 是一款面向 Windows 的轻量硬件与游戏性能监控工具，使用 WPF 和 .NET 8 构建。当前版本为 v0.1.6。

## 主要功能

- 查看 CPU、GPU、内存、硬盘、网络、主板和高级传感器信息，默认每 0.5 秒采集一次。
- 使用统一的固定容量历史缓存绘制曲线；未打开的页面不会生成图表快照或执行渲染工作。
- 使用内嵌 PresentMon 2.5.1 采集 FPS、帧时间、1% Low、0.1% Low、CPU/GPU 帧耗时和显示延迟。
- 支持搜索/识别游戏进程、最近前台进程加权，以及启动器到实际渲染子进程的解析。
- 捕获期间默认自动记录完整游戏会话，应用进入托盘后记录仍会继续。
- 自动生成逐帧 CSV 与 JSON 摘要；异常退出时保留并恢复 `.csv.partial` 数据。
- 支持导出当前统计窗口或最多 60,000 条内存缓存，并可查看最近 10 条记录。

## 下载与运行

从 [GitHub Releases](https://github.com/Lousuu/PCINFO/releases) 下载以下任一 Windows x64 单文件版本：

- `HardwareVision-v0.1.6-win-x64-lite.exe`：体积较小，需要预先安装 [Microsoft .NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。
- `HardwareVision-v0.1.6-win-x64-self-contained.exe`：包含 .NET 运行时，无需另行安装，文件更大。

可使用同一 Release 中的 `SHA256SUMS.txt` 校验文件。两个版本均未数字签名，Windows SmartScreen 可能显示未知发布者。程序清单默认请求管理员权限；PresentMon 的 ETW 游戏采集需要该权限。

系统要求：Windows 10/11 64 位与 x64 处理器。

## 游戏会话文件

自动记录默认开启，可在“游戏”页或“设置”页关闭。记录根目录为：

```text
%USERPROFILE%\Documents\HardwareVision\GameSessions
```

- 完整会话按 `yyyy-MM` 月份目录保存为 `.csv` 和 `.summary.json`。
- 写入中的文件使用 `.csv.partial`；下次启动时，有数据的残留文件恢复为 `.csv.incomplete`，空文件安全删除。
- 手动导出保存在 `GameSessions\Exports`。
- CSV 使用 UTF-8 BOM、固定英文表头和 invariant-culture 数值，便于 Excel、脚本与分析工具读取。

记录器使用有界队列与单后台写入器；PresentMon 帧回调只尝试非阻塞入队。极端磁盘拥塞时，摘要会记录被丢弃的“记录样本”数量，而实时内存统计链路不受阻塞。

## 游戏性能数据口径

- 当前 FPS：最近约 1 秒有效帧的平均帧时间换算值。
- 平均 FPS：当前统计窗口内平均帧时间的倒数，不是瞬时 FPS 的算术平均。
- 1% Low / 0.1% Low：最慢 1% / 0.1% 帧的平均帧时间换算值，分别至少需要 100 / 1000 个有效样本。
- CPU：PresentMon `CPUBusy`；GPU：`GPUTime`；延迟：`DisplayLatency`。
- 未报告、非有限值和非正帧时间不会进入统计。

与 NVIDIA App 等工具对比时，请对齐统计窗口、游戏场景与开始/停止时刻。不同工具的帧筛选、Overlay/Present 分类和窗口边界不同，短时结果可能略有偏差。

## 本地开发与测试

```powershell
git clone https://github.com/Lousuu/PCINFO.git
cd PCINFO
dotnet restore .\HardwareVision\HardwareVision.csproj
dotnet build .\HardwareVision\HardwareVision.csproj -c Release
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
```

测试仍使用项目自带的控制台运行器；v0.1.6 共 73 项，覆盖 PresentMon 解析/状态机、进程选择、缓存与退避、刷新合并、固定历史、统计缓存、CSV、崩溃恢复和自动记录端到端链路。

## 许可与第三方组件

PresentMon 的许可证与第三方声明保存在 `HardwareVision/ThirdParty/PresentMon/2.5.1`，并在首次采集时随运行时组件一起校验和释放。
