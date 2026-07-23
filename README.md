# HardwareVision

HardwareVision 是一款面向 Windows 的轻量硬件与游戏性能监控工具，使用 WPF 和 .NET 8 构建。

- 最新公开 Release：**v2.0.1**
- 当前版本：**v2.0.1**

## 主要功能

- 查看 CPU、GPU、内存模组、存储设备、网络适配器、主板和高级传感器信息，默认每 0.5 秒采集一次。
- 使用统一的固定容量历史缓存绘制曲线；未打开的页面不会生成图表快照或执行渲染工作。
- 使用内嵌 PresentMon 2.5.1 采集 FPS、帧时间、1% Low、0.1% Low、CPU/GPU 帧耗时和显示延迟。
- 支持搜索/识别游戏进程、最近前台进程加权，以及启动器到实际渲染子进程的解析。
- 游戏捕获启动时先执行 warm-up，过滤异常首帧和异常启动样本，并识别主要渲染 SwapChain；稳定阶段继续以每条 SwapChain 独立的中位数/MAD 固定窗口过滤孤立超高 FPS 尖峰，真实持续高 FPS 与经连续确认的档位切换不设固定 FPS 上限。
- CaptureElapsed 与显式时间戳严格递增；重复或倒退样本不会进入实时统计、CSV 或摘要。辅助逐帧指标按字段独立清洗，单个异常字段不会丢弃整帧。
- 捕获期间默认自动记录完整游戏会话，应用进入托盘后记录仍会继续。
- 自动生成逐帧 CSV、限制事件 CSV、硬件时间线 CSV 与 summary schema v4 JSON 摘要；v1–v3 继续兼容，异常退出时保留并恢复 partial 数据。
- 估算游戏会话 CPU/GPU 能耗，记录明确上报的 Thermal、Power、Current/EDP 等性能限制原因。
- 最近记录可打开完整静态报告，查看逐帧性能、CPU/GPU 频率、温度、功耗、限制区间、硬件快照与事件详情。
- 会话报告会流式重新校验普通/GZip 历史 CSV；“最大 FPS”根据有效采样数据计算并排除孤立异常帧，内部诊断不会占用普通报告界面。
- 提供磁盘健康、温度、剩余寿命、累计读写、通电与可靠性指标；外接 USB/UASP 桥接器会在身份证据唯一且无冲突时与真实硬盘传感器合并，主名称显示真实硬盘型号，歧义场景保持分离；硬件不报告的值保持 `--`。
- 默认自动响应 GPU、网卡、USB 存储等设备变化并刷新硬件快照；设置页和托盘也提供“重新扫描硬件”手动入口。
- 支持导出当前统计窗口或最多 60,000 条内存缓存；最近记录默认显示 10 条，可每次继续加载 10 条直至访问全部历史记录。
- TRACEWORK 主题提供由真实初始化里程碑驱动的 `INITIAL TRACE` 启动序列；它复用现有服务图、轮询、历史缓存、页面路由和唯一 PageHost，不执行第二次硬件扫描，也不伪造百分比进度。
- v2.0.1 的冷启动由原生 First Frame Gate 保护：`HWND` 在 `Show()` 前使用 `#0B0E11` CompositionTarget 背景并保持不可见，首个 Render 提交后一次性显示；500 ms 有界 fail-open、DWM 深色标题栏失败安全和托盘恢复不重入保证窗口不会永久隐藏。
- INITIAL TRACE 的 COMMIT 授权在 Lock 后单调保持，视觉锁与文字同生共灭；Reveal 到达即原子提交 `05 / 05 REVEAL`，分别保持 Full/Standard/Reduced 100/80/40 ms，再以统一 90 ms 退出并与现有 Shell 重叠建立。`SYS/BOOT.00` 仅在稳定布局宽度上执行一次连续 Clip，并最多重试一个 Render。

## 下载与运行

从 [HardwareVision v2.0.1 Release](https://github.com/Lousuu/PCINFO/releases/tag/v2.0.1) 下载唯一的发布资产：

- `HardwareVision.exe`：Windows x64、.NET 8 WPF、framework-dependent 单文件，需要预先安装 [Microsoft .NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。

系统要求为 Windows 10/11 x64。程序清单默认请求管理员权限，PresentMon 的 ETW 游戏采集同样需要管理员权限。当前不提供自包含版本、ZIP 或单独的 `SHA256SUMS.txt`。如果工作流没有配置 Authenticode 证书，Windows SmartScreen 可能显示未知发布者。

## 游戏会话文件

自动记录默认开启，可在“游戏”页或“设置”页关闭。记录根目录为：

```text
%USERPROFILE%\Documents\HardwareVision\GameSessions
```

- 完整会话按 `yyyy-MM` 月份目录保存。逐帧数据默认直接流式写入 `.csv.gz`，不会先生成一份普通 CSV 再压缩；限制事件、硬件时间线和摘要分别使用 `.performance-limits.csv`、`.hardware-timeline.csv` 与 `.summary.json`。
- 正在写入的压缩逐帧文件使用 `.csv.gz.partial`；异常关闭后可恢复的数据标记为 `.csv.gz.incomplete`。普通 CSV 模式对应使用 `.csv.partial` 与 `.csv.incomplete`。
- 历史普通 `.csv` 会话继续兼容且不会被自动迁移或删除；报告详情可以把压缩记录导出为普通 `.export.csv`，手动导出保存在 `GameSessions\Exports`。
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

测试仍使用项目自带的控制台运行器。v2.0.1 最终候选包含 `2057` 项测试；原生首帧、首帧 fail-open、COMMIT 单调授权/视觉 latch/no-relight 退出、Reveal 原子底栏/Shell 重叠/迟到快照、稳定 Index Clip 和启动 Clock 清理均提供独立 20/20 组，同时保留 Advanced Sensors 15/15、SYSTEM REWIRE 20/20 及既有硬件、PresentMon、会话链路。正式发布门禁要求两轮独立 Release 进程总数一致、0 failed、stderr 为空，并由 PR #9、合并后 main CI 和正式 package workflow 继续验证。

## 许可与第三方组件

PresentMon 的许可证与第三方声明保存在 `HardwareVision/ThirdParty/PresentMon/2.5.1`，并在首次采集时随运行时组件一起校验和释放。
