# HardwareVision 开发交接

> 最后更新：2026-07-14（Asia/Shanghai）。公开发布基线仍为 HardwareVision v0.1.6；工作区包含下一版本未发布改动。

## 1. 仓库与发布状态

- 本地项目：`E:\Mine\PCINFO`
- GitHub：`Lousuu/PCINFO`
- 主分支：`main`
- 目标标签/Release：`v0.1.6`
- Release 渠道：Pre-release，非 Draft（与 v0.1.5 相同）
- 程序版本：`0.1.6`；程序集/文件版本：`0.1.6.0`
- 当前 README：仅根目录 `README.md` 一份
- 当前发布说明：`RELEASE_NOTES_v0.1.6.md`

继续开发前必须先执行：

```powershell
cd E:\Mine\PCINFO
git status --short --branch
git log -5 --oneline --decorate
git fetch origin main --tags
Get-Content .\HANDOFF.md -Raw
Get-Content .\README.md -Raw
Get-Content .\RELEASE_NOTES_v0.1.6.md -Raw
```

如果本机默认 GitHub DNS 失败，本轮曾使用 Git 的单次官方地址解析参数，不要修改仓库永久配置：

```powershell
git -c http.curloptResolve=github.com:443:140.82.112.3 fetch origin main --tags
```

## 2. 下一版本未发布变更（基于 v0.1.6）

本节内容尚未提交、推送或发布；版本号仍为 `0.1.6`，`RELEASE_NOTES_v0.1.6.md` 未修改。

### 2.1 游戏会话 CPU + GPU 估算能耗

- 新增应用生命周期级 `GameEnergyTracker`，直接订阅唯一的 `PollingService.ReadingsUpdated`，没有创建第二套硬件采集器，也不依赖游戏页是否打开。
- PresentMon 成功启动后使用同一 `GameSessionStartInfo` 启动能耗会话；停止、目标退出、PresentMon 退出和应用关闭均按同一 session ID + generation 幂等完成。旧会话或旧 generation 样本会被拒绝。
- 每个物理 CPU/GPU 只选择一个代表功耗：CPU 优先 Package/Total，GPU 优先 Board/Total/Package；过滤 Core/SoC/限制值、非有限值、负值、异常高值和非 W 单位，避免同一设备传感器相加造成重复计数。
- 使用 `Stopwatch` 单调时钟和梯形积分。首个有效样本只建立锚点；缺样、非递增时间或超过当前前台/后台轮询周期容差的间隔不跨缺口积分。默认后台 10 秒轮询可继续积分，页面隐藏和托盘状态不触发 UI 派生更新。
- 游戏页新增“估算能耗 / 当前估算功耗 / 平均估算功耗”，Tooltip 明示这是传感器估算、包含的 CPU/GPU 组件、有效积分覆盖率及“不等于整机墙上功耗”。清空图表不会清空会话能耗。
- 能耗格式：小于 1 Wh 保留三位，1–999 Wh 保留两位，达到 1000 Wh 显示 kWh；功耗显示两位小数。
- `GameSessionSummary` 新增估算能耗、平均估算功耗、覆盖率、有效积分时长和包含组件；旧 JSON 缺少字段时保持 `null`，最近记录新增紧凑能耗文本。

### 2.2 磁盘健康与可靠性详情

- 继续复用 `HardwareInfoService -> HardwareSnapshot -> DiskDeviceService -> DiskViewModel`，数据源优先级为 `MSFT_PhysicalDisk / MSFT_StorageReliabilityCounter`，其次 LibreHardwareMonitor，最后 Win32 状态。
- `HardwareInfoService` 在后台线程读取 Storage WMI，并对物理盘与可靠性结果缓存 45 秒；没有把 Storage WMI 放入 0.5 秒传感器热路径。
- 新增健康/运行状态、当前/最高温度、剩余寿命/磨损、累计读写、通电时间/次数、读写错误和最高读/写/刷新延迟。设备不支持、权限不足或驱动不报告时字段仍显示 `--`。
- Windows 官方文档确认 `Wear` 表示已消耗耐久度百分比，100% 表示达到估算磨损上限，因此仅对 0–100 的明确值显示 `Remaining Life = 100 - Wear`，不根据通电时间或写入量猜测健康度。
- 累计读写识别 Host/Total/Data Read/Data Written/Data Units 等常见名称；B/KB/MB/GB/TB 明确单位统一换算为字节，NVMe Data Units 等未知单位保留原值和原单位，不擅自套用换算系数。
- 合并优先精确 UniqueId/ObjectId/序列号，其次使用无冲突的设备编号、容量和型号组合；序列号冲突、容量冲突或同分歧义时保持独立设备。LHM 原始根标识也用于隔离相同型号的多块盘。
- 本机只读 CIM 检查确认 2 块 `MSFT_PhysicalDisk`，可靠性类包含温度、磨损、通电时间、错误和延迟等预期属性；非提升权限读取可靠性实例返回 PermissionDenied，因此该场景按设计显示 `--`。本轮未使用 Windows 应用控制。

### 2.3 游戏页 CPU/GPU 性能限制日志

- 游戏页在“实时曲线”之后、“最近记录”之前新增“性能限制日志 / CPU / GPU 降频与限制事件”。界面只复用现有 `MetricCard`、`SensorRow`、`StatusBadge`、字体、颜色和边框，没有照搬参考截图配色，也没有新增 UI/NuGet 体系。
- 新增应用生命周期级 `GamePerformanceLimitTracker`，订阅同一个 `PollingService.ReadingsUpdated`，并与 PresentMon 共用 session ID + generation。页面隐藏/托盘状态继续记录；切换会话清空；旧 generation 拒绝；停止后冻结；内存历史上限 200 条，页面显示最近 50 条。
- 同一次轮询内同一处理器的多个原因合并为一条；CPU 与 GPU 分开记录。原因集合不变时只延长持续时间，原因变化或清除时结束旧记录并创建/冻结记录。每条显示发生时间、CPU/GPU、持续时间、原因数量和具体标签。
- 不根据低频率、高温或功耗值推测降频。仅识别明确的 Throttling、Thermal Event、Performance Limit、EDP、PerfCap 等状态；数值型 W/A/V/MHz 限制配置不会被当成活动事件。
- LibreHardwareMonitor 0.9.6 不直接公开此类状态，因此在唯一传感器聚合链中增加两个只读来源：Windows 自带 `Processor Information\\Performance Limit Flags`（1 秒缓存，保留每个原始位标志，不猜未公开的位语义）和 NVIDIA NVML clocks event reasons（利用率/空闲、应用时钟、软件功耗、硬件减速、Sync Boost、软/硬件温度、Power Brake、显示时钟及未知原始位）。NVML 不存在或不支持时静默降级为空列表。
- 限制状态使用独立 `SensorType.State`，不会混入 CPU/GPU Load、图表或 Dashboard 负载计算。Windows 性能计数器和 NVML 都在现有轮询中读取，没有启动第二套定时器、外部命令或采集进程。

### 2.4 当前验证状态

- 隔离构建：0 warning / 0 error。
- 自定义控制台测试：`129 passed, 0 failed, 129 total`；其中新增 19 项能耗/摘要覆盖、25 项磁盘选择/匹配/优先级/单位/默认显示/降级/缓存/可靠性字段覆盖，以及 12 项性能限制原因合并、CPU/GPU 隔离、噪声拒绝、持续时间、原因切换、结束、会话隔离、容量和来源映射覆盖。
- 常规 Debug/Release `obj` 当时被另一进程占用，因此验证使用 `dotnet build ... --artifacts-path E:\Mine\PCINFO\.codex-artifacts`；这是输出文件锁，不是源码编译错误。
- 未执行 GUI、托盘或真实游戏交互验证；用户明确要求不要使用 Windows 应用控制。
- 必须保留用户未跟踪文件 `HardwareVision\Controls\RealtimeLineChart.cs.baiduyun.uploading.cfg`，不要读取、删除、覆盖或暂存。

## 3. 不得破坏的基线

- .NET 8、WPF、MVVM、LibreHardwareMonitor、WMI 回退和默认 0.5 秒采集周期。
- 导航结构、托盘、设置持久化、指标可见性和现有硬件页面。
- PresentMon Console 2.5.1 内嵌资源、SHA-256 校验、按需释放和 ETW 残留会话清理。
- `GameCaptureState` 状态机、capture generation、session ID、目标解析和样本会话隔离。
- PresentMon v2/旧格式解析、NA/非有限值过滤、CSV 引号处理和进程过滤。
- 当前 FPS 为最近约 1 秒平均帧时间的倒数；平均 FPS 为统计窗口平均帧时间的倒数。
- 1% Low / 0.1% Low 为最慢 1% / 0.1% 帧平均帧时间的倒数，至少 100 / 1000 个有效样本。
- CPU/GPU/延迟分别使用 `CPUBusy`、`GPUTime`、`DisplayLatency`，不得混用 ClickToPhoton 或 RenderLatency。
- 内存缓存上限仍为 60,000 条，切换会话时清空且拒绝旧 session 样本。

## 4. v0.1.6 性能结构

### 4.1 传感器采集

- `LibreHardwareMonitorProvider` 缓存传感器引用、设备/传感器名、类型、单位、分类和原始 ID；每轮主要更新硬件并读取动态值。
- 类型映射使用 enum switch，不在热路径反复处理字符串。
- `SensorAggregatorService` 在构造时按优先级排 provider，使用 `SensorIdentity` 结构化键单遍合并，不在每轮排序或拼接复合字符串。
- `IConditionalSensorProvider` 允许低优先级 provider 查看已合并的高优先级读数。
- `WmiCpuClockSensorProvider`：
  - LHM 已有有效 CPU 非 Bus/BCLK 时钟时直接跳过；
  - 无有效时钟时查询 WMI；结果至少缓存 5 秒；
  - 失败退避 5、10、20、40、60 秒；
  - 复用 `ManagementObjectSearcher`，并用 `SemaphoreSlim` 防止并发查询。

### 4.2 历史与图表

- `SensorHistoryService` 是中央历史源，所有序列使用固定 240 点环形缓冲。
- CPU：Load/Temperature/Power/Clock；GPU：Load/Temperature/Power/Clock/MemoryUsed；磁盘：Read/Write；网络：Upload/Download。
- CPU/GPU 页面仅在激活时订阅 Dashboard；激活时从中央历史装载，隐藏时不生成图表快照。
- `RealtimeMetricChartViewModel` 每次刷新只复制一次环形数据，并在同一遍扫描中计算 current/avg/min/max。
- `RealtimeLineChart` 直接消费 `IReadOnlyList<double>`，不再在 `OnRender` 内 `ToArray`/`ToList`；不可见时不渲染，画笔被冻结并复用。

### 4.3 Dashboard 与 I/O

- `DashboardRefreshCoordinator` 默认 250 ms 合并 Sensors/Disk/Network/Hardware 刷新请求。
- UI 只更新相关分类；摘要卡片仅在 Dashboard 激活时重建。
- 磁盘与网络前台约 1 秒、后台约 10 秒；`SingleFlightGate` 阻止重入，generation/取消令牌拒绝过期结果。
- 磁盘性能实例名缓存 15 秒；网络静态信息约 30 秒刷新。
- Disk/Network 详情页复用 Dashboard 服务结果，不再重复创建采集服务。
- 托盘后台仍轮询并保持捕获/记录，但不执行页面派生、图表快照和 UI 刷新。

### 4.4 游戏统计

- `GameFrameSampleStore` 在锁内只截取所需窗口和版本，统计在锁外执行。
- 同一窗口和 sample version 返回精确缓存；新样本到达后 current/average 立即更新，Low FPS 最多每秒重算一次。
- `RuntimePerformanceDiagnostics` 每分钟记录 CPU、工作集、分配、GC、各服务计数/耗时、Dashboard 刷新和游戏统计锁耗时。

## 5. 完整会话记录

核心接口/实现：

- `IGameSessionRecorder`
- `CsvGameSessionRecorder`
- `GameSessionStartInfo`
- `GameSessionSummary`
- `GameSessionRecordInfo`
- `GameCsvFormatting` / `GameCsvExporter` / `GameSessionFileNaming`

默认根目录：

```text
%USERPROFILE%\Documents\HardwareVision\GameSessions
```

文件约定：

```text
GameSessions\yyyy-MM\<Game>-yyyyMMdd-HHmmss-<pid>.csv
GameSessions\yyyy-MM\<Game>-yyyyMMdd-HHmmss-<pid>.summary.json
GameSessions\Exports\<Game>-last-60s-yyyyMMdd-HHmmss.csv
GameSessions\Exports\<Game>-cache-yyyyMMdd-HHmmss.csv
```

写入流程：

1. PresentMon 进程成功启动后，如果 `AppSettings.RecordGameSessions` 为 true，创建 recorder 会话。
2. 使用容量 8,192 的有界 `Channel<GameFrameSample>`，多写单读；捕获回调只调用 `TryWrite`，绝不等待磁盘。
3. 第一个样本到达时才创建 `.csv.partial`，UTF-8 BOM、固定英文表头、64 KiB 缓冲，单消费者顺序写入。
4. 每 256 行 flush；完成时先 drain/关闭 writer，再将 partial 原子移动到 `.csv`。
5. 第二遍流式扫描 CSV，仅保留最慢 1% 所需的优先队列，计算 1%/0.1% Low；然后通过 `.tmp` 原子写 `.summary.json`。
6. 没有样本的会话不留下文件；写盘失败保留已有数据并记录错误。
7. 启动恢复：有数据 partial 改名为 `.csv.incomplete`；空或仅表头 partial 删除；无法处理的文件保持原位。

摘要字段包括：HardwareVision/PresentMon 版本、session/generation、PID/进程/窗口/路径、起止/时长、received/written/dropped、平均 FPS、Low FPS、帧时间、CPU/GPU 时间、显示延迟、正常完成标记、明确结束原因、CSV 名称和大小。

结束原因枚举：`UserStopped`、`TargetProcessExited`、`CaptureFailed`、`PermissionDenied`、`ToolUnavailable`、`SchemaMismatch`、`ApplicationShutdown`、`RecorderFailed`、`Unknown`。

注意：设置在捕获开始时读取。捕获过程中关闭自动记录，不会截断已经开始的当前文件；下一会话不再创建记录。

## 6. 页面与设置

- `MainViewModel` 仅立即创建 Dashboard，其余详情页在首次导航时创建。
- 游戏页离开或窗口进托盘不会停止 PresentMon；仅停止该页 UI 图表工作。
- 游戏页显示自动记录开关、状态、当前路径、打开目录/定位文件和最近 10 条记录。
- 自动记录关闭时显示“导出当前窗口”和“保存当前缓存”。
- 设置页显示同一开关、记录根目录、目录占用和打开目录命令。
- 不使用 MessageBox 报告导出；状态与完整路径显示在页面，长路径使用 ToolTip。

## 7. 测试

测试项目继续使用自定义控制台运行器，不迁移 xUnit/NUnit/MSTest。

```powershell
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
```

v0.1.6 预发布结果：`73 passed, 0 failed, 73 total`。

其中 41 项为 v0.1.6 新增/扩展覆盖：

- single-flight 与 Dashboard 合并刷新；
- 中央历史容量、顺序、尾部快照、无效值和 CPU/GPU/磁盘/网络映射；
- 样本 store 会话隔离、环形容量、版本、窗口和统计缓存；
- WMI 主时钟跳过、Bus Clock 识别、5 秒缓存、过期和失败退避；
- 文件名、CSV 引号/数字/BOM/表头/空导出；
- recorder partial、空会话、CSV/摘要、计数、generation、恢复、最近记录、目录大小、月份目录和 Low FPS；
- 假 PresentMon stdout 到自动 CSV/摘要的完整端到端链路。

## 8. 性能基线与复测

修改前基线（2026-07-13，同机 Release 诊断实例）：

- Dashboard 5 分钟：平均 CPU 0.211%，峰值 0.312%，平均工作集 346.7 MiB。
- 每分钟约 98 次 polling/LHM/WMI/disk/network，约 294 次 Dashboard 刷新。
- 每分钟托管分配约 257–259 MB，Gen0 约 16–18 次。
- 启动到 `MainWindow.Show` 约 1.700 秒；首批 Dashboard 数据约 3.684 秒；首批传感器约 3.729 秒；硬件快照约 4.843 秒。
- CPU 页面 5 分钟：平均 CPU 3.726%，平均工作集约 349.7 MiB。
- GPU 页面 5 分钟：平均 CPU 3.735%，平均工作集约 360.0 MiB。

修改后替代复测使用 `dotnet HardwareVision.dll` 非管理员启动，仅做进程/日志观测，没有 Windows 应用控制：

- Dashboard 5 分钟：平均 CPU 0.279%，峰值 1.520%，平均工作集 323.1 MiB，结束工作集 330.3 MiB。
- 稳态每分钟：WMI 查询约 11 次、命中约 89 次；disk/network 约 50 次；Dashboard 派生刷新约 100 次。
- 每分钟托管分配约 136.7–138.4 MB，Gen0 约 8–10 次。
- 启动到 `MainWindow.Show` 1.014 秒；首批 Dashboard 数据 4.278 秒；首批传感器 4.016 秒；硬件快照 4.079 秒。
- WMI、I/O、UI 刷新、分配量、工作集和主窗口显示时间均下降。
- 本次 LHM 平均约 93 ms，修改前约 91 ms，导致本机总 CPU 没有下降；不得把 v0.1.6 描述成“所有场景 CPU 都更低”。

用户明确要求不要使用 Windows 应用控制，因此以下本轮跳过：CPU/GPU 页面自动切换复测、托盘交互复测、真实游戏 35244 实机捕获/首帧/NVIDIA App 对比。不要伪造结果。已有假 PresentMon 端到端测试覆盖捕获、状态、样本、自动 CSV 与摘要。

## 9. 构建与发布

恢复、构建、测试：

```powershell
dotnet restore .\HardwareVision\HardwareVision.csproj
dotnet build .\HardwareVision\HardwareVision.csproj -c Release --no-restore
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release --no-restore
```

发布两个隔离目录：

```powershell
dotnet publish .\HardwareVision\HardwareVision.csproj -c Release --no-restore -p:PublishProfile=win-x64-light-single-file -o .\artifacts\publish-lite
dotnet publish .\HardwareVision\HardwareVision.csproj -c Release --no-restore -p:PublishProfile=win-x64-self-contained-single-file -o .\artifacts\publish-self-contained
```

最终资产名必须是：

```text
HardwareVision-v0.1.6-win-x64-lite.exe
HardwareVision-v0.1.6-win-x64-self-contained.exe
SHA256SUMS.txt
```

两个 exe 均须确认：x64、单文件、非裁剪、文件版本 0.1.6.0、产品版本 0.1.6、PresentMon 资源仍内嵌。Lite 依赖 .NET 8 Desktop Runtime；self-contained 包含运行时。

本轮本地资产：Lite 8,666,308 bytes；self-contained 73,880,515 bytes。SHA-256 分别为 `0AE9ACC42E1839F96A0D82455C02AC2AC79E007E3E5A1D698C2EB0823994F648` 与 `2B52A2B6FF4C3BF30EA8432822AB37C5D5BB7856EF04FC45BFE53C29375BA497`。使用 ILSpy bundle dump 解包 self-contained 后，确认 `HardwareVision.dll` 内有 956,768-byte PresentMon 资源，SHA-256 与源码资源一致。

发布前检查：

```powershell
git diff --check
git status --short
git diff --stat
git diff -- README.md HANDOFF.md RELEASE_NOTES_v0.1.6.md
```

提交消息：

```text
feat: optimize runtime and record game sessions
```

发布顺序：commit -> push `main` -> annotated tag `v0.1.6` -> push tag -> 创建 Pre-release -> 上传 3 个资产 -> 下载 Release 资产复算 SHA-256 -> 核对非 Draft/Pre-release/资产数量。

## 10. 已知限制与后续建议

- 本版本未数字签名，SmartScreen 可能显示未知发布者。
- PresentMon 可用性受权限、渲染模式、反作弊、驱动和游戏实现影响。
- NVIDIA App 等工具使用不同窗口边界/帧筛选时会出现合理的短时偏差。
- 极端磁盘拥塞可能使有界记录队列丢弃写盘副本；摘要有 dropped 数量，实时统计不被阻塞。
- `RuntimePerformanceDiagnostics` 的调用计数/平均耗时是进程启动以来累计值；allocated/GC/CPU 为每个约 60 秒区间。
- 下一轮可在用户允许人工配合时补做真实游戏：等待首帧、长会话、托盘继续写入、目标退出自动完成、NVIDIA App 同窗口对比和两种发布资产启动冒烟。

## 11. 给下一位开发者的简版提示词

```text
先完整阅读 E:\Mine\PCINFO\HANDOFF.md、README.md、RELEASE_NOTES_v0.1.6.md，并检查 git status、最近提交、远端 main 和标签。基于 v0.1.6 继续，不要破坏 .NET 8 WPF/MVVM、LHM/WMI、0.5 秒采集、PresentMon 2.5.1、状态机、generation/session 隔离、目标解析、统计口径、中央历史、合并刷新、惰性页面和完整会话记录。用户本轮明确禁止 Windows 应用控制；若仍有效，使用自动化测试、日志和进程观测，无法替代的 GUI/真实游戏步骤明确跳过。修改后先隔离构建并运行全部自定义测试；未经明确授权不要提交、推送或发布。
```
