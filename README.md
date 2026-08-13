# HardwareVision

HardwareVision 是面向 Windows 的本地硬件与游戏性能监控工具，使用 WPF 与 .NET 8 构建。当前正式版本为 **v2.0.4**。

## 功能

- 查看 CPU、GPU、内存模组、磁盘、网络适配器、主板和高级传感器；默认每 0.5 秒采样一次。
- 使用固定容量历史缓存绘制实时曲线；未激活页面不会持续创建图表快照。
- 使用内嵌 PresentMon 2.5.1 采集 FPS、帧时间、1% Low、0.1% Low、CPU/GPU 帧耗时与显示延迟。
- 识别游戏进程和启动器到渲染子进程的关系，过滤 warm-up 异常样本、重复时间戳和孤立 FPS 尖峰。
- 自动记录游戏会话，并生成逐帧 CSV/GZip、性能限制、硬件时间线和 summary schema v4；旧 schema 继续兼容。
- 从“最近游戏会话”打开独立静态报告，查看帧率、温度、频率、功耗、限制事件和硬件快照。关闭报告会返回同一个已缓存游戏页面，并保留列表、分页与滚动位置。
- 设备热插拔时自动刷新硬件快照，也可从设置或托盘手动重新扫描。

## 主题与动画

HardwareVision 提供 Classic 与 Tracework 两套主题。Tracework 使用同一个 Window、Shell、PageHost、CurrentPage 和 App-owned 服务图，不复制页面或硬件采集服务。

- 新安装、设置文件缺失或损坏恢复时，默认动画为 **Full**。
- 已有用户明确保存的 Full、Standard、Reduced 或 Off 不会被迁移或覆盖。
- Windows“减少动态效果”只会降低运行时 EffectiveLevel；RequestedLevel 仍保留用户选择。
- 页面切换在 Route 阶段提交目标业务状态，同时保留 outgoing/incoming 双层呈现；Full、Standard、Reduced 与 Off 分别使用完整、压缩、淡入淡出和即时路径。
- `INITIAL TRACE` 由真实启动里程碑驱动，不执行第二次硬件扫描，也不伪造百分比。
- SENSOR BUS 先稳定最终 Detail 文本，再显示输出/输入端口、提交端口可见帧、建立 Projection 线路并播放一次 Pulse；Pulse 完成后才进入 COMMIT，随后 Reveal。

## 下载与运行

从 [HardwareVision v2.0.4 Release](https://github.com/Lousuu/PCINFO/releases/tag/v2.0.4) 下载唯一公开资产 `HardwareVision.exe`。

支持边界：

- Windows 10/11 x64；不支持 x86、ARM、Linux 或 macOS。
- 需要预先安装 [Microsoft .NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。
- 发布物为 win-x64、framework-dependent、single-file、untrimmed。
- 清单请求管理员权限，用于硬件传感器、性能计数器、WMI/NVML 等能力，以及 PresentMon 的 ETW 游戏采集。
- 如果没有配置 Authenticode 证书，正式资产会明确标记为未签名，Windows SmartScreen 可能显示未知发布者。

可选 provider、性能计数器或 PresentMon 不可用时，应用会保留主界面并显示降级状态；这不意味着所有传感器在每种主板、驱动或虚拟化环境中都可用。

## 本地数据与隐私

HardwareVision 不提供云同步，也不会主动上传硬件、设置、日志或游戏会话。采集与报告均在本机完成。

```text
%APPDATA%\HardwareVision\settings.json
%APPDATA%\HardwareVision\logs
%USERPROFILE%\Documents\HardwareVision\GameSessions
```

- 游戏会话按 `yyyy-MM` 保存；压缩逐帧数据为 `.csv.gz`，写入中为 `.csv.gz.partial`，可恢复异常记录为 `.csv.gz.incomplete`。
- 历史普通 `.csv` 不会被自动迁移或删除。手动导出位于 `GameSessions\Exports`。
- CSV 使用 UTF-8 BOM、固定英文表头和 invariant-culture 数值。
- 日志用于本地诊断；日志目录不可写时采用有界 fail-open，不阻止主窗口使用。

## 游戏数据口径

- 当前 FPS：最近约 1 秒有效帧的平均帧时间倒数。
- 平均 FPS：当前统计窗口平均帧时间的倒数，不是瞬时 FPS 的算术平均。
- 1% Low / 0.1% Low：最慢 1% / 0.1% 帧的平均帧时间倒数，至少需要 100 / 1000 个有效样本。
- Primary cadence 顺序为 Display、Present、Application、legacy compatibility。
- 未报告、非有限、非正或时间戳未严格递增的样本不会进入统计。

与其他 Overlay 对比时应对齐游戏场景、统计窗口和起止时刻。不同工具的 Present/Display 分类和帧筛选规则可能产生差异。

## 构建与测试

```powershell
git clone https://github.com/Lousuu/PCINFO.git
cd PCINFO
dotnet restore .\HardwareVision\HardwareVision.csproj
dotnet build .\HardwareVision\HardwareVision.csproj -c Release
dotnet build .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
.\HardwareVision.Tests\bin\Release\net8.0-windows\win-x64\HardwareVision.Tests.exe
```

测试使用项目自带的控制台运行器。v2.0.4 的自动化基线为 `2648 passed / 0 failed / 2648 total`，覆盖真实 WPF Window/视觉树、页面切换、报告路由、启动 Projection、主题、DPI 布局、硬件/provider 降级、会话兼容、设置恢复、托盘、关闭与释放。正式发布还要求同一冻结 Release Tests 二进制连续运行两轮，总数一致、exit 0、stderr 为空。

自动化 DPI 和多显示器用例验证的是坐标与布局逻辑；并不等同于在所有真实显示器、RDP、软件渲染器、主板、GPU 或驱动组合上完成实机验证。

## 常见问题

**为什么必须以管理员身份运行？** 低层硬件访问、部分 WMI/性能计数器和 PresentMon ETW 采集需要提升权限。应用不会用管理员权限上传数据。

**为什么某些传感器显示 `--`、Unavailable 或 Unsupported？** 硬件、固件、驱动或 provider 可能不报告该指标。HardwareVision 会隔离部分失败，而不是伪造数值。

**PresentMon 无法启动怎么办？** 确认系统为 Windows x64、应用已提升权限、游戏确实在渲染，并检查本地日志。硬件页面仍可继续使用。

**设置文件损坏怎么办？** 应用会备份可识别的损坏文件并恢复安全默认值；动画默认回到 Full。已有可解析的显式动画选择不会被覆盖。

**报告文件被删除或损坏会怎样？** 当前记录显示可恢复错误，不会跳转到其他页面或清空游戏页缓存；可以关闭报告返回原列表。

## 第三方许可

PresentMon 2.5.1 的许可证和第三方声明位于 `HardwareVision/ThirdParty/PresentMon/2.5.1`，并作为资源嵌入发布物。直接 NuGet 依赖及其许可仍受各自条款约束。
