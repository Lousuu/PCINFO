# PCINFO / HardwareVision 项目交接文档

> 供新的 Codex 对话直接读取。开始任何修改前，请完整阅读本文，再检查 Git 状态和当前源码。
> 最后更新：2026-07-13（Asia/Shanghai），对应 HardwareVision v0.1.5。

## 1. 当前发布基线

- 工作目录：`E:\Mine\PCINFO`
- GitHub：<https://github.com/Lousuu/PCINFO>
- 当前分支：`main`
- 当前标签：`v0.1.5`
- 当前 Release：<https://github.com/Lousuu/PCINFO/releases/tag/v0.1.5>
- Release 状态：预发布（prerelease），非草稿。
- Release 资产：仅 `HardwareVision.exe`
- 资产大小：`8,592,580` 字节
- 资产 SHA-256：`63E4415D39CDFA1BCDCD28037CE4B8E8B073DDDCD709EEF7E087258B2A3BF59E`
- 程序版本：`0.1.5`，文件版本：`0.1.5.0`
- 发布格式：Windows x64、Framework-dependent、非裁剪单文件。
- 运行依赖：Microsoft .NET 8 Desktop Runtime x64。
- 应用和游戏采集需要管理员权限。
- 本文件随 v0.1.5 发布提交一起维护；最终提交以 `git rev-parse v0.1.5^{}` 为准。

新对话开始时先执行：

```powershell
Set-Location E:\Mine\PCINFO
git status --short --branch
git fetch origin main --tags
git rev-list --left-right --count main...origin/main
git log -5 --oneline --decorate
```

不要在未检查工作区时直接重置、覆盖或拉取。工作区可能包含用户的新修改。

## 2. 用户长期要求

1. 不要重写项目，不要大规模改架构。
2. 保持现有 WPF、MVVM、类名、命名空间和绑定路径。
3. 不得删除或削弱硬件采集、刷新、导航、ViewModel 绑定、传感器读取和 PresentMon 采集逻辑。
4. UI 保持紧凑硬件监控面板和原子朋克/复古未来主义风格，减少大卡片、空白和单列堆叠。
5. 前台不要展示 WMI、LibreHardwareMonitor、PerformanceCounter 等来源文字；底层来源字段和 Provider 必须保留。
6. 普通页面以核心指标为主，高级传感器页保留完整明细。
7. “显示项管理”不进入导航或主界面；兼容用 View/ViewModel 可保留。
8. 前台刷新默认固定 `0.5` 秒，步进 `0.5` 秒，最小不得低于 `0.5` 秒。
9. 空值统一显示 `--`；统计学上不可用的 Low FPS 显示 `N/A`。
10. 不引入新 NuGet 包，除非确有必要并明确说明。
11. 修改应小步、可验证，优先复用已有服务和样式。
12. 只有用户明确要求时才提交、推送或发布。
13. 受 Git 管理的项目 README 只保留根目录 `README.md` 一份；版本发布说明使用 `RELEASE_NOTES_vX.Y.Z.md`，交接信息使用本文件。

## 3. 技术栈与生命周期

- WPF，目标框架 `net8.0-windows`
- x64，应用清单为 `requireAdministrator`
- CommunityToolkit.Mvvm `8.4.2`
- LibreHardwareMonitorLib `0.9.6`
- System.Diagnostics.PerformanceCounter `10.0.9`
- System.Management `10.0.9`
- PresentMon Console `2.5.1` 作为程序集嵌入资源

应用组合根位于 `HardwareVision/App.xaml.cs`：

- 加载并规范化设置。
- 构造硬件信息、传感器、轮询、前台进程追踪、主窗口和托盘服务。
- `ForegroundProcessTracker` 全局只创建一次，随 App 生命周期释放。
- 主窗口关闭时释放页面 ViewModel；App 退出时释放轮询、传感器、前台 Hook 和托盘服务。

运行数据路径：

- 设置：`%APPDATA%\HardwareVision\settings.json`
- 日志：`%APPDATA%\HardwareVision\logs\hardwarevision-YYYYMMDD.log`
- PresentMon 运行时：`%LOCALAPPDATA%\HardwareVision\Runtime\PresentMon\2.5.1`

## 4. 游戏性能采集架构（v0.1.4 基线）

核心调用链：

```text
GamePerformanceViewModel
→ IGamePerformanceService
→ PresentMonGamePerformanceService
→ GameCaptureTargetResolver
→ PresentMon 2.5.1 stdout CSV
→ PresentMonCsvParser
→ GameFrameSampleStore
→ GameFrameStatisticsCalculator
→ GamePerformanceSnapshot / 图表 / 指标
```

必须保留：

- `GameCaptureState` 明确状态机与 `CaptureStateChanged` 事件。
- 只有收到首个有效 `GameFrameSample` 后才进入 `Capturing`。
- PresentMon 2.x 和旧版 CSV 字段兼容，必须识别 `FrameTime`。
- 每轮采集具有独立 generation 和 `CaptureSessionId`，旧任务不能写入新会话。
- `GameFrameSampleStore` 是 60000 条容量的会话隔离环形存储。
- `GameCaptureTargetResolver` 在开始采集时把启动器解析到实际有窗口的渲染子进程。
- PresentMon 使用系统级采集，在应用内部按目标 PID 过滤。
- 使用 stdout 实时消费 CSV，避免输出文件独占锁导致无法读取。
- 采集前清理残留的 `HardwareVision-*` ETW 会话，停止时完整回收。
- 当前 FPS 使用最近约 1 秒有效帧时间均值换算，避免极短 Present 间隔显示数千 FPS。
- 平均 FPS 使用平均帧时间换算。
- 1% Low / 0.1% Low 为最慢 1% / 0.1% 帧的平均 FPS，分别至少需要 100 / 1000 个有效样本。
- CPU 指标为 `CPUBusy`；GPU 指标为 `GPUTime`；延迟为 `DisplayLatency`，不可混用其他延迟字段。

## 5. 游戏进程发现与选择（v0.1.5）

职责划分：

- `PresentMonGamePerformanceService.GetCandidateProcessesAsync()`：只枚举候选并提供 PID、名称、标题、路径、启动时间和可见窗口元数据。
- `GameProcessFilter`：纯逻辑本地搜索，不触发系统进程重新枚举。
- `ForegroundProcessTracker`：记录最近一个非 HardwareVision 前台窗口快照。
- `GameProcessScorer`：纯逻辑评分、分组排序和高置信度决策。
- `GameProcessSelectionPolicy`：采集状态锁定、手动选择保护和 PID 重用判断。
- `GamePerformanceViewModel`：管理完整候选、过滤列表、选择来源、刷新 generation 和命令状态。

### 搜索

搜索字段：

- `DisplayName`
- `ProcessName`
- `WindowTitle`
- `FilePath`
- `Path.GetFileName(FilePath)`
- `Path.GetFileNameWithoutExtension(FilePath)`

规则：大小写不敏感，搜索词匹配时去除首尾空格，支持中文和带/不带 `.exe`。过滤不会破坏完整候选列表；被过滤隐藏的选择会在清空搜索后恢复。

### 最近前台追踪

- 使用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`。
- 标志为 `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`。
- 回调只保存 PID、时间、句柄和标题，不更新 WPF 集合、不执行耗时工作。
- 排除自身、桌面、任务栏和 Shell 表面。
- 委托引用由追踪器持有，避免 GC。
- Hook 失败时使用 `PeriodicTimer` 每秒轮询一次。
- `Dispose()` 调用 `UnhookWinEvent` 并停止降级任务。

### 评分与自动选择

当前主要分值：

- 最近外部前台 PID：`+100`
- 非空主窗口标题：`+25`
- 可见主窗口：`+15`
- `Win64-Shipping` / `Win32-Shipping`：`+30`
- Steam/Epic/Xbox/GOG/Games 路径：`+25`
- 最近 6 小时启动：`+10`
- 进程仍存在：`+5`
- launcher/helper/updater/crash/overlay 等：`-35`
- 浏览器、IDE、Shell、ChatGPT/Codex 等常见非游戏程序：`-140`

自动选择条件：

- 游戏候选阈值：`60`
- 高置信度阈值：`75`
- 第一、第二名至少相差：`18`
- 最近前台候选仍必须不是强降权非游戏程序。
- 搜索框非空、有效用户选择存在或采集目标锁定时，不自动覆盖。
- “识别游戏”只更新选择，不调用 `StartCaptureAsync()`。

选择来源通过 `GameProcessSelectionSource` 区分 `Automatic`、`Manual` 和 `Search`。刷新优先按 PID 保留，再校验名称、路径和启动时间；`Preparing`、`WaitingForFirstFrame`、`Capturing`、`Stopping` 均锁定目标。

## 6. 关键文件地图

- 应用启动与全局服务：`HardwareVision/App.xaml.cs`
- 主窗口与组合注入：`HardwareVision/MainWindow.xaml.cs`
- 主导航：`HardwareVision/ViewModels/MainViewModel.cs`
- 游戏页面：`HardwareVision/Views/GamePerformanceView.xaml`
- 游戏页面状态：`HardwareVision/ViewModels/GamePerformanceViewModel.cs`
- 游戏候选模型：`HardwareVision/Models/GameProcessInfo.cs`
- 检测结果：`HardwareVision/Models/GameProcessDetectionResult.cs`
- 选择来源：`HardwareVision/Models/GameProcessSelectionSource.cs`
- 本地过滤：`HardwareVision/Services/GameProcessFilter.cs`
- 评分与选择策略：`HardwareVision/Services/GameProcessScorer.cs`
- 最近前台追踪：`HardwareVision/Services/ForegroundProcessTracker.cs`
- PresentMon 服务：`HardwareVision/Services/PresentMonGamePerformanceService.cs`
- 启动器解析：`HardwareVision/Services/GameCaptureTargetResolver.cs`
- CSV 解析：`HardwareVision/Services/PresentMonCsvParser.cs`
- 样本存储：`HardwareVision/Services/GameFrameSampleStore.cs`
- 统计：`HardwareVision/Services/GameFrameStatisticsCalculator.cs`
- 自定义测试：`HardwareVision.Tests/Program.cs`
- 发布配置：`HardwareVision/Properties/PublishProfiles/win-x64-light-single-file.pubxml`
- 当前发布说明：`RELEASE_NOTES_v0.1.5.md`

## 7. 自动测试与 GUI 验证

测试项目是自定义 Console 测试程序，不使用 xUnit/NUnit/MSTest：

```powershell
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
```

v0.1.5 发布前结果：

- `32 passed`
- `0 failed`
- 原有 16 项 PresentMon、CSV、状态机、会话隔离、统计和启动器解析测试全部保留。
- 新增 16 项搜索、评分、降权、模糊候选、手动选择和采集目标锁定测试。

GUI 已使用非管理员诊断构建实际操作验证：

- 约 690px 宽窗口下顶部控件能自然换行，无重叠。
- `CHROME.EXE` 可匹配 Chrome 主窗口和后台进程。
- 被其他搜索条件隐藏的手动选择不会替换，清空搜索后恢复。
- “识别游戏”按钮可执行。

限制：该 GUI 验证环境没有正在运行的真实游戏，并且诊断实例非管理员，因此没有在 v0.1.5 这轮重新完成真实游戏前台识别和 PresentMon 首帧采集。v0.1.4 曾使用真实游戏验证 PresentMon 帧数据和统计链路。

## 8. 构建、运行与发布

开发运行：

```powershell
dotnet run --project E:\Mine\PCINFO\HardwareVision\HardwareVision.csproj -c Release
```

程序会请求管理员权限；关闭窗口可能进入托盘。构建前检查但不要擅自结束用户进程：

```powershell
Get-Process HardwareVision -ErrorAction SilentlyContinue
```

正式测试和构建应使用隔离目录，避免百度网盘或运行实例锁定 `bin/obj`：

```powershell
$root = Join-Path $env:TEMP ('HardwareVision-build-' + [guid]::NewGuid().ToString('N'))
dotnet restore .\HardwareVision.Tests\HardwareVision.Tests.csproj --artifacts-path $root
dotnet build .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release --no-restore --artifacts-path $root
```

发布使用 `win-x64-light-single-file.pubxml`，正式资产目录只允许有 `HardwareVision.exe`。校验项目：

- 文件版本与产品版本
- Windows x64 单文件
- `requireAdministrator` 清单
- PresentMon 嵌入资源
- 未签名状态（当前预期）
- SHA-256 和文件大小
- GitHub 上传后资产名称、大小和摘要

不要提交或上传 `artifacts`、`bin`、`obj`、临时构建目录或 GUI 验证产物。

## 9. 已知限制与后续注意

- 自动识别基于启发式评分，不是完整游戏数据库。
- 浏览器云游戏默认被强降权，需通过搜索手动选择。
- 无法读取路径或启动时间时，自动识别置信度会降低。
- 名称和安装路径都缺乏游戏特征的游戏，可能需要手动选择。
- 反作弊、游戏渲染模式、权限和驱动可能影响 PresentMon 数据，但不代表进程搜索失效。
- NVIDIA App 与 HardwareVision 的短时 FPS 可能因采样窗口边界和帧筛选策略存在小幅差异。
- 不要把中文状态字符串重新作为采集状态判断依据。
- 不要把搜索和评分逻辑塞回 PresentMon 采集生命周期。
- 不要删除低分候选；用户仍需通过搜索选择浏览器或通用程序。

## 10. 新对话建议开场指令

```text
请先完整读取 E:\Mine\PCINFO\HANDOFF.md，然后检查当前 Git 状态、远端 main、README 和最近提交。基于 v0.1.5 源码继续，不要破坏硬件采集、0.5 秒刷新、导航、传感器、PresentMon 状态机、会话隔离、CSV 解析、统计口径、进程搜索、前台追踪、游戏评分和 GameCaptureTargetResolver。完成修改后先执行隔离构建和 HardwareVision.Tests；未经我明确要求不要提交、推送或发布。
```
