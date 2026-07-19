# HardwareVision 开发交接

> 最后更新：2026-07-17（Asia/Shanghai）。HardwareVision v0.1.8 的最终发布范围经 PR #4 及其发布工作流修复收敛：帧校验、历史报告、项目可靠性、用户界面术语和外接存储身份合并均进入正式版本；发布只提供 framework-dependent 的 Windows x64 单文件 `HardwareVision.exe`。

## TRACEWORK UI Stage 5 handoff update

- Latest dedicated TRACEWORK document: [`docs/TRACEWORK_UI_HANDOFF.md`](docs/TRACEWORK_UI_HANDOFF.md)
- Branch / PR: `feature/tracework-ui`, Draft PR #7.
- Stage status: Stage 1, Stage 1.1, Stage 2, Stage 3A, Stage 3B, Stage 3C, Stage 3D-1, Stage 3D-2, Stage 4, and Stage 5 are complete. Stage 6 is not complete.
- Stage 5 scope: SYSTEM REWIRE theme transition layer, independent `IThemeTransitionService`, transition models/clock, `SystemRewireOverlay`, Settings async theme command persistence rules, MainViewModel snapshot exposure, and Rewire automated coverage.
- Validation: Debug build passed, Release build passed, custom test runner passed with `643 passed, 0 failed, 643 total`; GitHub Actions passed for run `29676111020` on `b87bd0ebd0754e9af16f662490703c51a736e6e9`. Manual visual validation was intentionally not performed; no formal admin EXE was launched; no screenshots were generated or committed.

## TRACEWORK UI 专项交接

- 最新专项交接文档：[`docs/TRACEWORK_UI_HANDOFF.md`](docs/TRACEWORK_UI_HANDOFF.md)
- 当前 TRACEWORK UI 改造状态：Stage 1、Stage 1.1、Stage 2、Stage 3A、Stage 3B、Stage 3C、Stage 3D-1、Stage 3D-2 已完成；Stage 4 已完成本地实现、自动测试并通过 GitHub Actions；Stage 5 已完成；Stage 6 未完成。
- Stage 4 范围：可持久化 Requested Motion、可降级 Effective Motion、系统环境检测、继承式 MotionContext、单一 PageHost 的轻量页面进入动效，以及 Settings 中的 Motion Profile 选择。
- 本阶段按用户要求未进行人工视觉验证，未启动正式管理员 EXE，未生成或提交截图。

## 1. 仓库与发布状态

- 本地项目：`E:\Mine\PCINFO`
- GitHub：`Lousuu/PCINFO`
- 公开主分支：`main`（已恢复完整源码，可作为后续常规开发基线；标签 `v0.1.7` 保持不变）
- `develop` 已从本地和远端删除，不再是当前开发或集成分支；`codex/session-startup-hotplug-storage` 作为功能开发分支继续保留
- 新功能应从最新 `origin/main` 创建分支，PR 的 base 应为 `main`
- 已推送优化分支：`codex/game-energy-performance-limits`
- 优化分支最新提交：`13bbbd7 perf: optimize game telemetry and runtime pipeline`；推送后本地与 `origin/codex/game-energy-performance-limits` 为 `0/0`
- PR #1：`feat: expand hardware and game telemetry`，已合并
- PR #2：`release: HardwareVision v0.1.7 session reports`，已合并
- 已发布标签/Release：`v0.1.7`
- Release 渠道：Pre-release，非 Draft（与 v0.1.5 相同）
- 当前 `main` 后续开发版本：`0.1.8-dev`；程序集/文件版本：`0.1.8.0`；最新公开 Release 仍为 `v0.1.7`
- 当前 README：根目录仅保留完整项目说明 `README.md`
- 根目录 `RELEASE_NOTES_v0.1.0.md` 至 `RELEASE_NOTES_v0.1.7.md` 已按用户明确要求全部删除；既有 `v0.1.7` tag 与 GitHub Release 未修改

继续开发前必须先执行：

```powershell
cd E:\Mine\PCINFO
git status --short --branch
git log -5 --oneline --decorate
git fetch origin main --tags
Get-Content .\HANDOFF.md -Raw
Get-Content .\README.md -Raw
```

## 1.1 v0.1.8 hardening 本地未提交开发状态（2026-07-15）

1. 本轮本地开发分支为 `codex/v0.1.8-hardening`。
2. 分支基于已发布的 v0.1.7 `main`（`origin/main` 基线提交 `933064b`）；本节描述的内容尚未发布。
3. 前台刷新间隔不再被 `App` 或 `SettingsViewModel` 强制改回 0.5 秒，用户修改会立即应用并持久化。
4. 前台间隔默认 0.5 秒、合法范围 0.5–30 秒、步长 0.5 秒；后台间隔仍独立配置为 5–120 秒。
5. `SessionFilePathResolver` 把历史会话文件视为不可信输入，拒绝绝对路径、UNC、分隔符、`.`、`..`、目录穿越和错误扩展名，并在 `Path.GetFullPath` 后使用 `OrdinalIgnoreCase` 验证目录边界。
6. Recorder 使用 `Idle / Recording / Finalizing / Completed / Failed` 状态机；收尾结果记录 frame CSV、hardware timeline、performance-limit CSV 和 summary JSON 的步骤状态。
7. 同一会话的 `CompleteAsync` 共享一个 CompletionTask；调用者 cancellation 只取消该调用者的等待，内部收尾使用独立的 30 秒超时并保留可恢复 partial。
8. NVML `State` 读数不再创建 timeline GPU；GPU 关联优先 UUID、PCI Bus ID、PNP ID，并用 NVML/LHM 设备序号作为低成本回退，同型号多 GPU 不按名称合并。
9. Polling 使用合并 `SemaphoreSlim` 调度信号；前后台切换或间隔修改会中断当前 delay、重新计算单调时钟周期，但不会重启服务或并发重入。
10. 帧图表横轴依次使用会话相对时间、`Timestamp - CaptureStartedAt`、旧格式累计 FrameTime，并记录 `FrameTimeAxisSource`；异常值会钳制并产生 warning。
11. 帧 CSV 在 IO/权限异常时保留已解析点，报告标记 partial 和失败行；其他文件仍可继续加载。
12. 降频统计从下采样前的原始时间线按时间加权计算；绘图仍最多保留 1,500 点，统计与显示数据职责分离。
13. 覆盖率使用中位采样间隔识别大于 2.5 倍周期的缺口；缺口不计入积分，覆盖率低于 50% 时不显示误导性的频率平均值。
14. 新 summary、performance-limit CSV 和 hardware timeline CSV 使用 `SessionSchemaVersion = 2`。
15. JSON 缺失版本按 v1；CSV 按规范化列名映射，允许重排、未知列和缺失可选列；旧 v1 继续读取，未来版本读取已知字段并给出 warning。
16. 会话索引为根目录下的 `session-index.jsonl`，每行一个只含最近列表所需字段和安全相对路径的 JSON 对象；损坏行会跳过，索引不可用时有界并行扫描旧 summary 并重建。
17. 会话目录大小由 `SessionDirectorySizeCache` 维护：启动后后台扫描一次，完成会话增量更新，设置页读取缓存并提供显式“重新计算”。
18. `GameIconService` 在后台提取本地 EXE 图标，默认跳过 UNC；缓存键含规范路径、大小和最后修改时间，使用固定容量 LRU、single-flight 和 Frozen `ImageSource`。
19. 性能限制事件改用有界高度 `ListBox`，启用 `VirtualizingStackPanel`、Recycling 和逻辑滚动；关闭详情时释放 ItemsSource/ToolTip 引用。
20. `SessionTelemetryChart` 按 Model 和尺寸复用 Frozen geometry，Brush/Model/尺寸变化显式失效；文本缓存有界，事件命中使用二分定位，鼠标移动不重建曲线。
21. 当前权限策略仍是 manifest 的 `requireAdministrator`。
22. 已移除设置页重复的“重新以管理员身份启动”入口及不可达的 `RestartAsAdministrator` 逻辑。未来若降权，应采用 asInvoker UI + 最小权限 Helper + Named Pipe/Pipe ACL，并只在采集需要时提权；本轮没有实现不完整 Helper。
23. `.github/workflows/ci.yml` 执行 restore、隔离 Release build、自定义控制台测试、`git diff --check`、依赖清单和日志归档；`package.yml` 生成 framework-dependent/self-contained win-x64 非裁剪单文件、版本校验、SHA256SUMS、依赖清单、artifact attestation 和可选 Authenticode。签名 Secrets 为 `WINDOWS_CERT_BASE64` / `WINDOWS_CERT_PASSWORD`，缺失时产物明确标记 `UNSIGNED`。
24. 新增 83 项测试，拆分到 11 个功能测试文件；最终为 `299 passed, 0 failed, 299 total`，隔离 Release build 为 0 warning / 0 error。
25. 仍需人工验证：设置重启持久化、托盘回前台立即刷新、真实会话四文件、停止/进程退出竞态、真实多 GPU 与同型号 GPU、真实长会话统计、三小时以上报告、5000 个真实会话、网络/失效 EXE 图标、事件列表滚动、真实证书签名、未签名标记和异常关闭后的 partial 恢复。
26. 本轮没有 commit、push、创建/更新 PR、merge、tag 或发布 Release；`RELEASE_NOTES_v0.1.7.md` 和正式版本号均未修改。

## 1.2 启动样本稳定化、硬件热插拔与会话压缩（2026-07-16，已提交并推送）

1. 当前开发分支为 `codex/session-startup-hotplug-storage`，基于 `origin/codex/v0.1.8-hardening` 的 `007e438`；本节成果已经提交并推送至 `origin/codex/session-startup-hotplug-storage`。尚未创建或更新 PR，尚未合并到 `main`，尚未创建 tag 或发布新 Release；正式版本仍为 `0.1.7`，`RELEASE_NOTES_v0.1.7.md` 未修改。
2. 首秒极高 FPS 的根因不是全局高 FPS 上限，而是 PresentMon 启动阶段首帧/短间隔异常、多个 SwapChain 混流及时间戳未稳定。`GameFrameValidationPipeline` 现在位于解析器与所有消费者之间；不通过的帧不会进入内存 store、实时卡片/曲线、统计、CSV 或 summary。
3. 每次 capture 都从冷状态开始，状态为 `WaitingForHeader / WaitingForStableSwapChain / WarmingUp / Stable / Resetting / Completed`。默认最少候选帧 12、最短观察 250 ms、最长等待 3 s、稳定窗口 12 帧且至少 8 帧、稳定倍数 4、启动离群倍数 20；首个 SwapChain 样本直接丢弃。策略没有固定 FPS 上限，持续稳定的 1000 FPS 流可通过。
4. 主 SwapChain 按候选帧数和最近活跃度选择；最多跟踪 16 条链，主链无活动 750 ms 后才考虑切换，候选链至少 8 帧并持续确认 1 s。非主链不会写入后续链路；旧 PresentMon 无 SwapChain 字段时按单流兼容。
5. `CaptureElapsed`/显式时间戳必须单调递增；倒退或非递增帧被丢弃并计入诊断。FPS/FrameTime 结构字段异常会丢帧；CPU/GPU/延迟辅助字段的 `0` 为合法值，非有限、负值或大于 60,000 ms 时仅将该字段置空，不丢整帧。当前 FPS 需要至少 10 个有效样本，平均 FPS 至少 2 个。
6. `GameSessionSummary` schema 升至 v3，新增 warm-up、异常值、非主 SwapChain 丢弃、选中/观察到的 SwapChain 等质量字段。运行时诊断新增相应计数，状态栏在稳定前显示“正在稳定采样”或“正在识别主渲染 SwapChain”。
7. 新增 `IRefreshableSensorProvider`、`HardwareRefreshService` 与 `HardwareChangeMonitor`。主窗口通过 `HwndSource` 监听 `WM_DEVICECHANGE` 的到达、移除和 devnodes 变化；默认 debounce 2 s、cooldown 5 s，窗口回调只排队异步刷新，不做 WMI/LHM/NVML 重工作。
8. 硬件刷新使用 single-flight；并发手动刷新共享同一任务，刷新期间的自动通知最多合并为一次跟随刷新。自动热插拔默认开启，设置页可关闭并可手动“重新扫描硬件”，托盘同一入口显示为“重新扫描硬件”。只有完整刷新成功才发布新 `HardwareSnapshot`；失败保留旧快照和旧设备列表。
9. 刷新复用唯一的 provider/aggregator/polling 实例：LHM 在 provider 锁内安全 close/open，WMI CPU 清除 searcher/cache，HardwareInfo 清除 Storage WMI 缓存与退避状态；NVML 在游戏会话活跃时仅清读数缓存并延迟深度 unload/re-enumerate，避免打断正在记录的游戏会话。刷新成功后立即触发一次受轮询锁保护的 poll。
10. Dashboard、CPU/GPU、磁盘、网络和设置页都接收新快照；provider 单项失败被隔离并进入结果/诊断。设备变化不会停止 PresentMon、不会清空帧缓冲、不会结束或重建当前 recorder。应用退出时移除窗口 hook 并取消尚未执行的 debounce。
11. 会话帧文件默认直接流式写为 `FileStream -> GZipStream(CompressionLevel.Fastest) -> StreamWriter`，临时名 `.csv.gz.partial`，正常完成 `.csv.gz`，恢复文件 `.csv.gz.incomplete`；没有先落普通 CSV 再压缩的双份峰值。设置页可切回普通 CSV，新设置只影响下一次 capture。
12. `GameSessionFrameStreamFactory` 同时按实际 GZip signature 和文件名兼容 `.csv`、`.csv.gz`、`.csv.incomplete`、`.csv.gz.incomplete`、`.csv.partial`、`.csv.gz.partial`。报告、Low FPS 二次扫描、恢复、索引相关路径与导出均通过流工厂读取；现有普通 CSV 不迁移、不自动删除，历史 v1/v2 summary 继续兼容。
13. 设置页报告详情新增后台“导出普通 CSV”，先写唯一 `.partial`，完成后移动为 `.export.csv`；取消或失败会删除本次 partial。截断 GZip 会保留已成功解析的行并显示“压缩记录只能部分读取”。summary v3 记录格式、压缩算法、未压缩/压缩字节数和压缩率。
14. 新增 30 项定向测试，完整自定义 runner 结果为 `329 passed, 0 failed, 329 total`；隔离 Release 测试项目与应用项目均为 0 warning / 0 error。覆盖启动离群值、稳定 1000 FPS、多 SwapChain、旧单流、时间倒退、辅助零值、debounce/cooldown、single-flight、失败保留快照、立即 poll、GZip 往返/截断/恢复/导出/取消/路径安全/footer、设置默认值与持久化。
15. 合成写入基准使用真实 CSV 格式化和 `CompressionLevel.Fastest`：1 小时 60 FPS 为 53,818,473 -> 1,472,643 字节（减少 97.26%）；1 小时 120 FPS 为 106,455,873 -> 3,027,641（97.16%）；1 小时 240 FPS 为 214,754,673 -> 6,096,229（97.16%）；3 小时 120 FPS 为 319,729,473 -> 9,118,845（97.15%）；3 小时 240 FPS 为 644,988,273 -> 18,304,824（97.16%）。3 小时 240 FPS 的 wall/CPU 为普通 4482.7/4312.5 ms、GZip 4660.1/4562.5 ms；这是合成内存 sink 基准，不代表真实磁盘、游戏或整机 CPU 占用。
16. 修改前首次按指定 `--artifacts-path` 直接 `--no-restore` build 因该隔离目录没有 `project.assets.json` 而出现 NETSDK1004；对同一隔离目录 restore 后，基线 build 为 0 warning / 0 error。默认测试输出被用户正在运行的 `HardwareVision.exe` 锁定，因此没有停止该进程，改用隔离输出的 apphost 完成测试；不要把输出锁误写成源码失败。
17. 尚需人工实机验证：真实游戏首秒不再出现尖峰、真实 overlay/多 SwapChain 主链选择、高刷新率游戏、旧 PresentMon 单流、设备管理器启用/禁用 GPU/网卡、USB 存储插拔、磁盘/网络/CPU/GPU 页面实时变化、扫描失败时旧快照保留、游戏记录期间热插拔不打断、1–3 小时真实会话文件大小/CPU/内存、报告读取与普通 CSV 导出。用户明确禁止 Windows GUI 自动控制，本轮没有伪造这些实机结论。
18. 必须继续保留 `HardwareVision\Controls\RealtimeLineChart.cs.baiduyun.uploading.cfg`：不要读取、修改、删除、暂存或加入 Git。
19. 本次修复前按指定 `git grep -nI -E "[[:blank:]]+$"` 重新扫描 `HardwareVision/Services/DiskDeviceService.cs` 与 `HardwareVision/Services/GpuDeviceService.cs`，实际均为 0 处；直接检查 `origin/main` blob 与相关历史 diff 也未复现先前记录的 27+5 处，因此没有对这两个文件制造空白改动。后续以当前全树扫描结果为准。

如果本机默认 GitHub DNS 失败，本轮曾使用 Git 的单次官方地址解析参数，不要修改仓库永久配置：

```powershell
git -c http.curloptResolve=github.com:443:140.82.112.3 fetch origin main --tags
```

## 1.3 main ancestry 与交付加固修复（2026-07-16）

1. 独立修复分支为 `codex/reconcile-main-history-ci-docs`，基于 `origin/main` 的 `747acfd` 创建；`main` 仍是完整源码默认分支，`develop` 已删除，旧功能分支保持原样。
2. 首个提交 `97e4a02136b381b15295439e8f642d2c19b0de96` 使用 `-s ours --no-ff` 记录 `origin/codex/session-startup-hotplug-storage` ancestry，两个 parent 为 `747acfd38045d29db8311200a393ff529335c8a5` 与 `8e7a496ec3fa09c52c0d24a3e03a156bd8ed7cae`。该 merge 前后文件树无差异，功能分支现已成为修复分支祖先。
3. 当前开发版本改为 `0.1.8-dev`，`AssemblyVersion`/`FileVersion` 为 `0.1.8.0`；最新公开 Release 仍为 `v0.1.7`，不得把 v0.1.8 描述为已发布。
4. 当前指定两文件扫描结果为 0 处尾随空格，因此没有空白清理 diff。CI 现在对完整已跟踪文本树扫描 `*.cs`、`*.xaml`、`*.xml`、`*.csproj`、`*.props`、`*.targets`、`*.yml`、`*.yaml`、`*.json`、`*.md`、`*.txt`、`*.ps1`，并正确区分 `git grep` 的 0、1 与异常退出码。
5. CI 保留 restore、Release build、自定义测试、依赖清单和日志上传；同时运行 `git diff --check`，并在构建或测试使工作区变脏时明确失败。
6. package workflow 在 publish 前解析项目版本。`v*` tag push 只接受无 `dev`、`preview`、`alpha`、`beta`、`rc` 等后缀的稳定版本，且 tag 必须严格等于 `v` + `Version`；因此 `0.1.8-dev` 的任何 tag 构建都会失败。
7. `workflow_dispatch` 开发包使用产品版本加 7 位短 SHA，例如 `HardwareVision-0.1.8-dev-747acfd-win-x64-framework-dependent.zip`；稳定 tag 包不加 SHA，例如 `HardwareVision-0.1.8-win-x64-framework-dependent.zip`。
8. `WINDOWS_CERT_BASE64` 与 `WINDOWS_CERT_PASSWORD` 只对单个“Sign or mark executables”步骤可见。该步骤在有证书时签名、时间戳并验证，在缺少证书时写明确的 `SIGNING_STATUS.txt`，临时 PFX 始终在 `finally` 中删除。
9. 每种 ZIP 都包含 `BUILD_INFO.txt`；上传 artifact 同时包含构建信息、实际 ZIP、按实际 ZIP 文件名生成的 `SHA256SUMS.txt` 与依赖清单。workflow 不创建 tag，也不发布 GitHub Release。
10. 根目录 README/Release Notes 相关文件仍严格只有 `README.md`，不存在任何 `RELEASE_NOTES_*.md`。本轮未创建或修改 tag/Release，v0.1.8 仍未发布。
11. 本次隔离 Release 应用构建与测试项目构建均为 0 warning / 0 error；自定义 runner 为 `329 passed, 0 failed, 329 total`。人工实机验证仍包括真实游戏首秒、overlay/多 SwapChain、高刷新率、旧 PresentMon 单流、设备启用/禁用与 USB 插拔、热插拔期间持续记录、1–3 小时真实会话、报告导出以及真实/缺失证书路径。
12. 未来合并本修复 PR 必须使用 **Create a merge commit**；不得使用 Squash and merge 或 Rebase and merge，否则 ancestry 修复会丢失。

## 1.4 稳定阶段帧校验与会话统计（2026-07-16）

1. 独立修复分支为 `codex/harden-frame-validation-robust-stats`，基于已合并 PR #3 的 `origin/main` merge commit `072a84c62a1250f8cc3d3e2b14c8fe7ed8c789b9` 创建；版本继续保持 `0.1.8-dev`，未创建 tag 或 Release。
2. 根因已从代码确认：旧 pipeline 在 `Stable` 后不再检查孤立 FrameTime 尖峰；所有 SwapChain 共用一个稳定窗口；时间戳相等会通过；主链主要按累计帧数选择；十个辅助字段共用 60 秒上限；历史报告绕过实时验证并直接采用单帧最大 FPS。
3. `GameFrameValidationPipeline` 现在为每条 SwapChain 独立维护固定帧时间窗口、最近活动、稳定计数、中位数/MAD、档位候选与确认、最近接受/异常时间及各辅助指标窗口。热路径使用复用数组和小型固定窗口，不执行 LINQ 排序或逐帧大数组分配。
4. 稳定阶段只把显著更短的孤立 FrameTime 视为超高 FPS 异常；慢帧继续保留，避免抹除真实卡顿。持续更快档位需要连续 6 帧确认后接纳；稳定 1000 FPS 可直接通过，1000 FPS 中的单帧 10000 FPS 会被过滤，没有固定 FPS 上限。
5. CaptureElapsed 和显式时间戳均严格要求 `current > previous`，重复与倒退分别计数；两类时间戳都缺失时进入明确的兼容回退模式。时间戳异常帧在进入 SwapChain 计数、窗口、store、CSV、summary 前拒绝。
6. 主链评分综合最近活动、连续稳定性、当前稳定窗口、主链持续性，并把 `Independent Flip`/`Application` 仅作为软置信度；不靠这些字段硬拒绝其他模式。短暂中断不切换，长期失活后候选链仍需稳定帧与持续时间确认，切换会写诊断日志。
7. CPU/GPU busy/wait、GPU latency/time、render/display/displayed/click-to-photon 分别使用独立结构上限和固定窗口稳健清洗；0 保留，非有限/负数/结构异常或孤立高尖峰只清空该字段，不丢整帧。parser 补齐自身 `DisplayLatencyMs`/`DisplayedTimeMs` 表头别名。
8. FPS 始终由已验证 `FrameTimeMs` 重算；平均 FPS 继续使用平均帧时间倒数。用户界面的“最大 FPS”定义为最近最多 8 个已接受帧的平均帧时间倒数峰值，至少 4 帧才产生；内部 `RawMaximumFps` 仅用于兼容与日志诊断。
9. `GameSessionReportService` 以历史重放模式复用同一验证 pipeline，普通 CSV 与 GZip 都流式处理，不修改原文件；重复/倒退时间戳、FrameTime 尖峰和辅助字段异常都会重新校验。无时间戳旧文件使用累计帧时间降级并显示警告，截断文件继续保留已解析有效行。
10. `GameSessionSummary` 升至 schema v4，新增独立时间戳、FrameTime、档位切换、每字段清洗、兼容模式以及内部 `RawMaximumFps` / `SustainedMaximumFps` 字段；v1、v2、v3 仍可读取，未来版本继续警告。
11. 报告主要指标改用离线重校验结果；内部逐帧诊断继续保留在模型、日志和兼容字段中，普通用户只看到需要处理的文件损坏、兼容解析或部分指标不可用提示。
12. 新增 28 项生产路径测试，覆盖稳定 60 FPS 微帧、60→240 档位、稳定/尖峰 1000 FPS、严格时间戳、每链隔离、overlay 选择、短/长中断、辅助字段、最大 FPS、历史 CSV、schema v3/v4、普通/GZip、截断 GZip 和 parser→validation→recorder→report 一致性。当前隔离测试项目构建为 0 warning / 0 error，完整 runner 为 `357 passed, 0 failed, 357 total`。
13. 仍需人工实机验证：真实游戏首秒与稳定阶段是否不再出现虚高最大 FPS、overlay/多 SwapChain 主链选择、60/120/240/高刷新率与真实持续极高 FPS、长会话辅助字段、1–3 小时 CSV/GZip/报告、托盘持续记录。未使用 Windows GUI 自动控制，不得把合成测试写成实机结论。

## 1.5 会话历史分页与报告展示完善（2026-07-16）

1. 本轮继续使用 `codex/harden-frame-validation-robust-stats` 和现有草稿 PR #4；没有创建新分支或新 PR，版本继续为 `0.1.8-dev`，未创建 tag 或 Release。
2. 最近记录默认加载 10 条，可每次继续加载 10 条直至全部历史记录，并可收起到前 10 条。分页使用稳定 snapshot token、总数和 `HasMore`；索引或 incomplete 文件发生变化时重建轻量快照，未变化时不重复全目录扫描。
3. 历史加载使用独立 cancellation、generation 和 single-flight 门控。页面停用或释放会取消等待；旧请求、错位页或 snapshot 变化不会覆盖新状态；加载失败保留当前列表并给出可操作提示。
4. 最近记录改用有界高度 `ListBox`，启用 WPF `VirtualizingStackPanel`、Recycling 与逻辑滚动；完成记录优先于同一会话的 incomplete 记录，顺序按开始时间和稳定键确定。
5. 短会话与长会话曲线都先过滤无效点、排序并合并相同横坐标。每个时间桶只保留一个代表点；FPS 按桶内平均 FrameTime 的倒数换算，CPU/GPU/延迟使用算术平均，首尾点与限制事件边界继续保留。
6. 曲线横轴按 5/15/30/60/120 秒和更长时长自适应格式化，避免相邻标签重复；纵轴加入单位相关留白并对非负指标钳制下界。1–3 个点显示数据量提示，单点和极短序列使用标记；检测到采样缺口时折线断开，不跨空白连线。
7. 普通用户界面统一显示“最大 FPS”，不再展示内部 `RawMaximumFps`、帧校验详情面板、session/generation 或原始 elapsed 字段；schema v4、内部诊断模型、日志和兼容字段保持不变。报告分区与指标名称改为面向用户的中文描述。
8. 历史 GZip 在 `InvalidDataException`、IO 或权限异常时保留已成功解析的有效行并标记部分报告；通过可注入 frame stream factory 对损坏尾部进行确定性测试，源文件不被修改。
9. 采集阶段诊断与历史重放诊断分别存储，不再把两套计数相加；报告统计采用重放后的有效数据，摘要诊断仍保留用于兼容和日志定位。
10. 新增 60 项测试，覆盖分页边界、快照、去重、排序、single-flight、取消、失败保留、WPF 虚拟化、短会话曲线、聚合口径、坐标轴、缺口、单点、损坏 GZip、诊断分离和展示文案。隔离 Release 应用/测试构建均为 0 warning / 0 error，完整 runner 为 `417 passed, 0 failed, 417 total`。
11. 仍需人工实机验证：真实大量历史记录的滚动与展开/收起、持续录制时 snapshot 更新、5–120 秒真实会话曲线视觉、系统缩放/窗口尺寸、真实截断 GZip、真实游戏最大 FPS 与 CPU/GPU/延迟指标。未使用 Windows GUI 自动控制，不得把自动化结果写成实机结论。

## 1.6 项目可靠性与用户界面术语收敛（2026-07-16）

1. 本轮继续使用 `codex/harden-frame-validation-robust-stats` 和现有草稿 PR #4；修改前本地 HEAD、远端分支引用和 PR head 均为 `e08da68564e1a69ead3921152c8fd36e10a6e233`。没有创建新分支或新 PR，版本仍为 `0.1.8-dev`。
2. 已审查全部生产源码、页面 XAML、用户可见 ViewModel 文案、README 和本交接文档。界面把“网络细节”改为“网络适配器信息”，导航统一为“容量与内存模组”“网络适配器与流量”“应用设置与诊断”；合理的“查看详情”“会话详情”“详细信息”等按语境保留，没有机械全文替换。
3. `HardwareChangeMonitor` 原先由替换请求和异步 owner 同时释放同一个 `CancellationTokenSource`，可能在冷却等待或刷新服务仍使用 token 时触发 `ObjectDisposedException`。现在外部只负责取消，异步 owner 在 `finally` 中唯一释放；回归测试会在替换通知后继续访问旧 token，验证生命周期所有权。
4. `PollingService.PollingFailed` 原先用空 `catch` 包裹一次多播调用，一个异常订阅者会阻止后续订阅者且不留诊断。现在逐个隔离调用并限频记录订阅者异常，与 `ReadingsUpdated` 的策略一致。
5. `AdvancedSensorsViewModel` 的每次整理任务现在拥有并最终释放自己的 cancellation；停用和 `Dispose` 先改变生命周期状态，再取消当前任务。所有成功、失败和 Dispatcher 落地路径都会复查 active/disposed/token，旧结果不会覆盖新页面状态。
6. `DashboardViewModel` 在直接硬件快照 await 后、失败/完成路径及所有排队事件回调内复查 `isDisposed`；`Dispose` 在取消和解除订阅前先标记已释放。`MainViewModel` 与游戏进程刷新失败的 Dispatcher 回调也加入落地时复查。
7. `SettingsViewModel` 的目录大小读取增加 active/disposed 状态和单 owner cancellation；页面停用或释放后取消等待，只有当前 owner 可以更新 UI。硬件扫描和保存状态文案改为可操作的“无法读取/无法刷新/无法保存”表达。
8. 会话报告加载不再由替换请求提前释放前一个 token；导出命令在报告关闭时取消，导出和加载的完成/失败状态不会写入已释放 ViewModel。
9. 会话恢复、summary/incomplete 扫描和目录大小统计改用 `IgnoreInaccessible` 且跳过 reparse point 的递归枚举；根枚举失败会限频记录并保留现有 partial，不会因一个不可访问目录中断整个恢复链路。单文件检查失败同样保留文件并记录诊断。
10. 修改前同机 Release 合成基准：轮询切换 11.177 ms；3 小时 60/120/240 FPS 分别 576.667/1116.207/2279.568 ms；3 小时时间线读取 186.595 ms；5000 条索引读取 88.301 ms；5000 条旧 summary 扫描 3633.683 ms。修改后分别为 4.523 ms、452.560/817.778/1575.796 ms、133.075 ms、44.690 ms、1685.252 ms。两次都保持相同数据规模和有界结果，但本轮没有改写统计热路径，因此这些差异只作为无回退信号，不宣称为本轮算法提速。
11. 工作区清理前逐项盘点了 ignored/untracked 文件。已删除可再生成的 `HardwareVision/obj`、`HardwareVision.Tests/bin`、`HardwareVision.Tests/obj`、`decompiled_pdb` 和 `decompiled_verify`；运行中的 HardwareVision 锁定了应用 `bin`，因此按约束保留。`.codex-artifacts` 可能含会话恢复资料，`.tools` 含工具与重建来源，`artifacts` 含历史发布/验证资产，均因用途明确或存在歧义而保留。
12. 受保护的 `HardwareVision\Controls\RealtimeLineChart.cs.baiduyun.uploading.cfg` 没有读取、创建、修改、删除、移动、暂存或提交；所有搜索和文件清单均显式排除该路径。
13. 新增 5 项回归覆盖：轮询失败订阅者隔离、取消令牌 owner 生命周期、Dashboard 延迟快照在释放后不落地、专业导航术语和网络标题；乱码检查扩展到全部 Views XAML 与 ViewModels。完整 runner 为 `422 passed, 0 failed, 422 total`。
14. 应用与测试项目隔离 Release 构建均为 `0 warning / 0 error`。正在运行的 `HardwareVision.exe` 锁定默认输出目录，因此没有停止用户程序，而是把输出定向到仓库外目录。仍需人工验证真实设备热插拔、扫描失败、页面快速切换/关闭、真实受限目录、系统缩放和完整视觉效果。
15. 本轮没有 Windows GUI 自动化，没有停止 HardwareVision，没有删除用户会话或用途不明文件，没有修改统计口径、Schema 或版本，没有创建/修改 tag 或 Release，也没有合并 PR 或发布 v0.1.8。

## 1.7 外接存储桥接器与真实硬盘身份合并（2026-07-16）

1. 本轮继续使用 `codex/harden-frame-validation-robust-stats` 和草稿 PR #4；修改前本地 HEAD、远端分支引用和 PR head 均为 `7540a1c41a07bd9385b805b0f9c424682d3c8c48`。版本保持 `0.1.8-dev`，summary schema 保持 v4。
2. 只读 CIM 现场确认 Windows 把外接盘报告为 Index/DeviceId 2：`Win32_DiskDrive` 名称为 Realtek RTL9210 NVME SCSI 设备、容量 512,105,932,800 字节、介质为外接；`MSFT_PhysicalDisk` 名称为 Realtek RTL9210 NVME、BusType 为 USB、容量 512,110,190,592 字节。两者的序列身份一致；交接与源码未记录实际序列号、PNP ID、UniqueId 或 ObjectId。
3. 在不停止正在运行的 HardwareVision、不提权且不使用 GUI 自动化的约束下，独立 LHM 探针没有枚举到存储根设备，因此没有取得可验证的 `/hdd/N` 现场根索引。用户界面已提供真实盘型号 `KXG6AZNV512G TOSHIBA` 及温度/寿命读写数据；实现没有假定 `/hdd/0 == PhysicalDrive0`，根索引只作为中等证据。
4. `DiskDeviceService` 现在集中执行分层身份匹配。强证据包括清洗后的序列号、UniqueId、ObjectId、PNP 稳定片段和 Win32 Index ↔ Storage DeviceId；序列号、容量、物理索引或唯一 ID 明确冲突时拒绝合并。名称、兼容容量、LHM 根索引、性能实例、外接传输和唯一候选关系只作为中等证据。
5. 外接桥接器识别基于 USB/UASP/外接介质与 SCSI/SATA/NVMe 传输组合，RTL9210、JMS578、JMS583、ASM2362 等名称仅是弱提示，不写死单一控制器。桥接器与 LHM 真实盘仅在没有强冲突、具有 SMART/温度类传感器证据、候选唯一且领先明显时合并；歧义继续保留独立设备，并以不含名称和标识符的计数诊断限频记录。
6. 合并后的主 `Id` 保持 Windows WMI 稳定 ID，旧 Win32、Storage WMI 与 LHM 根 ID 保存在 `IdentityAliases`，首选磁盘选择和热插拔重建同时匹配主 ID 与别名。Windows 容量、分区、卷、空间、健康、可靠性和性能字段保留；LHM 补充温度、SMART、寿命、累计读写和计数器，不用空值覆盖 Windows 数据。
7. 桥接场景主名称与型号改为真实硬盘型号，Windows 报告的传输设备名和桥接控制器分别保存在 `TransportDeviceName` / `BridgeControllerName`；磁盘详情新增默认隐藏的桥接控制器指标。Dashboard、磁盘页总数/总容量和新会话硬件元数据都复用合并后的设备列表，因此只计算一次并显示真实盘型号。
8. 新增 27 项生产路径回归，覆盖真实桥接组合、字段优先级、显示名、计数/容量、索引证据、歧义、双桥双盘、序列/容量/索引/唯一 ID 冲突、内外盘隔离、通用 USB-SATA、WMI-only、LHM-only、无 SMART、性能、旧首选 ID、热插拔重建、Dashboard 汇总、会话元数据、容量舍入和 PNP 稳定片段；既有保守歧义用例继续通过。完整 runner 为 `449 passed, 0 failed, 449 total`，应用与测试工程隔离 Release 构建均为 `0 warning / 0 error`。
9. 仍需人工确认真实 HardwareVision 刷新后外接盘只显示一次、主名称为 `KXG6AZNV512G TOSHIBA`、容量/分区/温度/寿命/累计读写/性能均归属同一设备，以及拔插后的首选项保持。未停止程序、未执行设备禁用/弹出/格式化、未写入或删除 F: 内容，也未伪造 LHM 根索引现场结论。
10. 受保护的 `HardwareVision\Controls\RealtimeLineChart.cs.baiduyun.uploading.cfg` 未读取、创建、修改、删除、移动、暂存或提交；没有创建/删除分支，没有 force push、rebase、amend、reset 或 clean，没有创建/修改 tag 和 Release，也没有发布 v0.1.8。

## 1.8 HardwareVision v0.1.8 最终发布准备（2026-07-17）

1. 发布审查基于 `codex/harden-frame-validation-robust-stats`、PR #4 和已推送提交 `c5bfc43592bed8a31366b51b78655f2428b4b0b2` 开始；PR #4 的最终发布准备提交是包含本节的 `release: prepare HardwareVision v0.1.8` 普通追加提交。审查覆盖崩溃/数据损坏、会话隔离与路径安全、SwapChain/generation/GPU 串线、Dispose 后 UI 更新、磁盘误合并、统计口径、历史 schema、启动/构建/记录/读取和发布依赖；没有发现未修复的明确 P0/P1 生产代码阻断问题，也没有进行推测性架构重写。
2. 原 `package.yml` 同时 restore/publish framework-dependent 与 self-contained 两套包，并生成 ZIP、`SHA256SUMS.txt`、`BUILD_INFO.txt`、依赖清单和签名状态旁文件作为打包资产，不符合 v0.1.8 的唯一 EXE 要求。仅设置 `PublishSingleFile=true` 的首次本地验证还会留下两个 MonoPosix 原生 DLL；最终 publish 增加 `IncludeNativeLibrariesForSelfExtract=true`，将它们纳入单文件。
3. `package.yml` 现在只执行 framework-dependent、`self-contained=false`、win-x64、非裁剪单文件 publish。工作流保留 workflow_dispatch、`v*` tag、.NET 8、restore、应用/测试 Release build、449 项测试、严格版本/PE x64/资源/管理员清单/单文件验证、可选 Authenticode、provenance attestation 和独立日志 artifact。Release executable artifact 只包含 `HardwareVision.exe`；内部 build info、SHA-256、依赖清单、签名状态和构建日志只进入日志 artifact，不得附加到 GitHub Release。
4. 项目版本从 `0.1.8-dev` 正式转为 `Version=0.1.8`、`InformationalVersion=0.1.8`；`AssemblyVersion` 与 `FileVersion` 保持 `0.1.8.0`。summary schema 保持 v4，v1–v3 历史会话兼容路径未修改。
5. 用户已人工确认外接 USB/NVMe 桥接盘合并结果正常；最终实现继续保留真实盘主名称、Windows 稳定 ID/容量/盘符/健康/性能和 LHM 温度/寿命/累计读写，强冲突与歧义仍拒绝误合并。
6. 最终本地 restore 成功；使用 `E:\Mine\PCINFO-build\v0.1.8-verify` 隔离资产路径的应用与测试 Release build 均为 `0 warning / 0 error`，自定义 runner 为 `449 passed, 0 failed, 449 total`。默认 `dotnet run` 只因用户正在运行的 `HardwareVision.exe` 锁定默认 apphost 而无法重建；没有停止该进程，完整测试改由同一隔离构建的 runner 执行。
7. 本地最终 publish 目录 `E:\Mine\PCINFO-build\v0.1.8-publish` 只有 `HardwareVision.exe`：framework-dependent、win-x64 PE、单文件、非裁剪、9,137,348 字节，ProductName `HardwareVision`、ProductVersion `0.1.8`、FileVersion `0.1.8.0`。本地文件未签名；SHA-256 只作内部核验，不生成或上传 SHA 文件。
8. `package.yml` 已通过 YAML 解析，全部 PowerShell `run` 块通过语法解析；PR CI、main CI、main workflow_dispatch 和 tag workflow 仍必须按发布门禁依次成功。workflow_dispatch artifact 必须在创建 tag 前下载复核；tag workflow artifact 必须在创建 Release 前再次复核。
9. PR #4 合并后的首次 main `workflow_dispatch` 打包运行在测试通过后，于 publish 阶段报告 NETSDK1112：此前只 restore 测试项目，没有为应用显式下载 `win-x64` runtime pack。发布工作流的最小修复是在同一隔离 artifacts path 先执行 `dotnet restore HardwareVision.csproj -r win-x64`，再 restore 测试项目。第二次运行已通过 restore、build、449 项测试、publish、版本/PE 和签名状态校验，但发现 publish 到仓库内未跟踪 `dist/` 会触发工作区干净门禁；最终把唯一 EXE 输出改到 runner temp，并让 attestation 和 artifact upload 直接使用该仓库外路径。两次失败运行均没有创建 tag 或 Release。
10. 发布准备提交进入 PR 时，`v0.1.8` tag 与 Release 按门禁仍不存在。最终发布状态必须是：annotated tag `v0.1.8` 指向包含 PR #4 及上述工作流修复的最终 main merge commit；GitHub `HardwareVision v0.1.8` 为非 Draft 的 Pre-release；自定义资产严格只有一个 `HardwareVision.exe`。GitHub 自动生成的 source zip/tar 不计入自定义资产。
11. 仍建议后续继续人工验证更多真实游戏、Overlay/PresentMode、同型号多 GPU、1–3 小时长会话、托盘持续记录、真实截断 GZip、更多 USB/SATA/NVMe 桥接组合和有真实证书时的签名路径；这些项目不阻断已通过自动化与现有人工确认的 v0.1.8 发布。
12. 本轮不创建根目录 `RELEASE_NOTES_*.md`，不停止 HardwareVision，不使用 Windows GUI 自动化，不删除开发分支，不启动 `0.1.9-dev`，不移动或覆盖旧 tag，不修改 v0.1.7 Release。受保护 cfg 继续完全不触碰。

## 2. v0.1.7 已发布变更（基于 v0.1.6）

本节 2.1–2.5 经 PR #1 合并；2.6 的完整会话报告、限制 CSV、硬件时间线和旧记录兼容经 PR #2 合并。版本号为 `0.1.7`；发布时使用过的根目录发布说明现已按用户要求删除，tag 与 GitHub Release 保持不变。

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

### 2.4 `7a591d0` 提交时的验证状态

- 隔离构建：0 warning / 0 error。
- 自定义控制台测试：`129 passed, 0 failed, 129 total`；其中新增 19 项能耗/摘要覆盖、25 项磁盘选择/匹配/优先级/单位/默认显示/降级/缓存/可靠性字段覆盖，以及 12 项性能限制原因合并、CPU/GPU 隔离、噪声拒绝、持续时间、原因切换、结束、会话隔离、容量和来源映射覆盖。
- 常规 Debug/Release `obj` 当时被另一进程占用，因此验证使用 `dotnet build ... --artifacts-path E:\Mine\PCINFO\.codex-artifacts`；这是输出文件锁，不是源码编译错误。
- 未执行 GUI、托盘或真实游戏交互验证；用户明确要求不要使用 Windows 应用控制。
- 必须保留用户未跟踪文件 `HardwareVision\Controls\RealtimeLineChart.cs.baiduyun.uploading.cfg`，不要读取、删除、覆盖或暂存。

### 2.5 已验收的性能与正确性优化（当前开发分支最新提交）

- PresentMon 2.5.1 自带 `--help` 已确认支持 `--process_id id`；启动参数现在将目标 PID 下推到 PresentMon，同时应用层解析器保留目标 PID 过滤作为正确性兜底。诊断日志同时记录 source filter 与 application filter，后续若出现 PID 重用、子进程切换或来源差异可定位过滤层。
- CSV 表头只解析一次并缓存列位置；数据行使用 `ReadOnlySpan<char>` 单遍扫描和栈上字段范围，不再为非目标行解析数值或创建 `GameFrameSample`。目标行继续覆盖 PresentMon v2/旧表头、引号/逗号、`NA/N/A`、帧时间和 CPU/GPU/延迟既有语义。
- `GameFrameSample.RawLine` 已移除，内存只保留结构化字段；CSV 由结构化样本重新格式化。样本 store 的时间窗口使用二分定位，统计缓存限制为 8 项。
- 游戏页不再订阅每帧 `FrameReceived`；改为页面可见时唯一的 500 ms UI 定时器拉取 store/version，隐藏或释放时停止。性能限制集合按 EventId 增量插入、移动、替换和删除，不再每次 `Clear()` 导致整表 Reset。
- CPU/GPU 限制状态各自使用 `NotStarted / SupportedNormal / ActiveLimit / Unsupported / TemporarilyUnavailable`。开始事件需连续 2 次或 1 秒确认，结束需连续 3 次或 2 秒确认；相同原因 5 秒内合并，活动事件最多每 1 秒更新一次。短刺、采集临时失败和利用率/空闲、应用时钟、Sync Boost、显示时钟、Low Utilization、Idle P-state、未知位不会误报；明确的 thermal、power/current/EDP、software power cap、hardware slowdown/power brake 等状态才进入事件。历史达到容量时 `EventsTruncated=true` 会进入快照和摘要。
- Windows CPU 与 NVIDIA NVML provider 仅在游戏会话活动时读取；NVML 延迟初始化并锁定生命周期/原生调用，CPU 计数器正常零值也会报告“支持且正常”。每次会话边界清空短缓存，防止快速重启沿用旧限制事件。
- `GameEnergyTracker` 改为每个物理 CPU/GPU 独立维护功率锚点、积分时长和可用性。一类组件缺样只打断自身连续性，其他组件继续积分；组件返回后不跨缺口补算。热路径复用列表/字典并限制原始标识元数据缓存为 512 项。
- Polling 使用 `Stopwatch` 固定周期计划，慢采集跳过已错过周期且不重入、不忙循环；单个订阅者异常不会阻断其他订阅者。聚合器复用合并字典。
- Storage WMI 使用 `SemaphoreSlim` single-flight 和 45 秒成功缓存/10 秒失败重试；可靠性实例一次批量查询，失败不覆盖已有好缓存，也不把权限拒绝误缓存为“确实无数据”。基础 Win32 磁盘仍可显示。
- 启动时 partial 恢复由单一后台任务执行，窗口显示不等待目录扫描；会话启动、最近记录/目录大小读取和关闭会等待同一恢复任务，避免恢复与新写入竞争。完成与释放保持幂等。
- 100,000 行合成 PresentMon 压测（95% 非目标、60/120/240/500 FPS、v2/旧表头、引号逗号、NA/N/A）基线为 764,532,456 B 分配、约 832–873 ms、95,000 个非目标样本对象；优化后 3 次为 1,647,024 B、143.17–143.95 ms、0 个非目标样本对象，即分配下降 99.78%、吞吐约提高 5.8 倍。强制 GC 后 60,000 条结构化样本保留量从 53,721,808 B 降到 20,115,704 B（下降 62.56%），其中 `RawLine` 保留从约 17,436,096 B 降为 0。
- 当前隔离 Release 构建为 0 warning / 0 error，自定义控制台测试为 `152 passed, 0 failed, 152 total`。新增覆盖早期 PID 过滤、引号目标行、无 RawLine、UI 定时器/增量集合、polling 重入/异常隔离、恢复 single-flight/幂等、CPU/GPU 独立能耗、限制抗抖/暂时失败/合并/状态/摘要兼容等。
- 用户已经完成人工验收并确认表现正常。本轮没有由 Codex 自动执行 Windows GUI 控制；真实长时间稳态、NVIDIA App 同窗口对比、多 GPU 和托盘长会话数据仍以用户后续实测为准。历史 v0.1.6 进程观测基线仍见第 8 节，不得把合成 CSV 压测外推成整机 CPU/工作集结论。

### 2.6 本地未提交的完整会话报告、限制事件与硬件时间线

- 当前实现已由 PR #2 合并到 `main` 并随 v0.1.7 发布；开发分支 `codex/session-report-throttle-timeline` 保留对应发布提交。
- 自动记录会话新增 `<SessionBaseName>.performance-limits.csv`。它只在会话完成后保存 `GamePerformanceLimitTracker` 已去抖、合并、会话隔离并冻结的事件，不重复保存每次轮询状态。字段固定为：`CaptureSessionId,CaptureGeneration,EventId,ProcessorType,StartedAt,EndedAt,ElapsedStartSeconds,ElapsedEndSeconds,DurationSeconds,ReasonCount,Reasons,RawReasonNames,Scopes,TriggerCount,WasMerged,SupportStatus,IsActiveFinalState,Source,RawIdentifiers,WasTruncatedSource,Notes`。时间使用 ISO 8601，数字使用 InvariantCulture；多值字段以 `;` 分隔，反斜杠转义 `;` 和 `\`；整个字段仍按 RFC 风格 CSV 引号规则转义。无事件时写稳定表头的空事件文件，旧记录没有文件时显示“未记录”。
- 自动记录会话新增 `<SessionBaseName>.hardware-timeline.csv`，写入期间使用 `<SessionBaseName>.hardware-timeline.partial.csv`，完整关闭后安全移动为正式文件。每个时间点、每个设备一行，字段固定为：`CaptureSessionId,CaptureGeneration,Timestamp,ElapsedSeconds,DeviceType,DeviceId,DeviceName,CpuAverageCoreClockMHz,CpuEffectiveClockMHz,CpuMaximumCoreClockMHz,CpuLoadPercent,CpuTemperatureCelsius,CpuPackagePowerWatts,CpuLimitActive,CpuLimitReasonCount,CpuLimitReasons,CpuLimitSupportStatus,GpuCoreClockMHz,GpuMemoryClockMHz,GpuLoadPercent,GpuTemperatureCelsius,GpuHotSpotTemperatureCelsius,GpuBoardPowerWatts,GpuLimitActive,GpuLimitReasonCount,GpuLimitReasons,GpuLimitSupportStatus,MemoryUsedBytes,MemoryLoadPercent`。不可用值写空字段，不写伪造的 0。
- 时间线只复用唯一的 `PollingService.ReadingsUpdated`，不创建第二套 LHM/NVML/WMI/PerformanceCounter 或硬件 Timer。默认每 1 秒最多产生一次批次；容量 512 的有界 Channel 在回调中只 `TryWrite`，队列满时不阻塞轮询并累计丢样，`TimelineWrittenSampleCount` 和 `TimelineDroppedSampleCount` 进入 summary。无活动 Recorder 会话时只做一次空状态判断。partial 有数据时恢复为 `.hardware-timeline.incomplete.csv`，只有表头时删除。
- 所有文件以 `CaptureSessionId + CaptureGeneration + CaptureStartedAt` 隔离；CSV 横轴统一使用相对会话开始时间的 `ElapsedSeconds`。单调时钟只负责 1 秒节流，不另建持久化时间基准。限制事件使用真实起止时间覆盖曲线；事件落在两个硬件采样点之间时不插值或伪造传感器值。
- CPU 普通频率口径是所有有效、正数、非 Bus/BCLK、非 Effective 的 `Core Clock` 算术平均；同时记录这些普通 Core Clock 的当前最大值。明确的 `Effective Clock` 单独算术平均，绝不与普通 Clock 混合。Bus/BCLK、额定最大睿频、负数、NaN、Infinity 不作为当前主频。
- GPU `Core/Graphics Clock` 与 `Memory/VRAM Clock` 分列。每个 GPU 使用稳定设备 ID 单独写行；核显与独显不合并。报告中的 GPU 图表按会话最大负载优先，无负载依据时给 NVIDIA/GeForce/Radeon/AMD 独显轻量优先级，并可通过图表选择器手动切换具体 GPU。
- 最近记录每行新增“查看详情”。详情仍位于游戏页现有导航上下文中，打开后停止该页 500 ms 实时 UI 定时器，后台解析静态历史文件；关闭时取消读取、清空报告/曲线/图标引用，并在页面仍激活时恢复实时 UI。自动采集、Recorder 和应用生命周期 Tracker 不依赖该详情页，因此不受打开/关闭影响。
- 详情页使用现有 `MetricCard`、`SensorRow`、`StatusBadge`、`FutureButton`、字体、颜色和间距，包含会话信息/游戏图标降级、性能统计、会话性能曲线、CPU/GPU 性能限制统计、限制事件列表、会话硬件信息及数据完整性提示。硬件型号来自捕获开始时复制到 `GameSessionStartInfo` 的历史元数据；不会读取当前机器状态冒充旧会话。
- `GameSessionReportService` 在后台流式读取 summary、逐帧 CSV、限制 CSV 和时间线 CSV，固定缓存 4 份报告。逐帧 CSV 不绑定全量集合；页面事件最多保留 200 条。旧记录仍可显示 FPS/Frame Time；缺时间线时频率区显示“该会话未记录硬件频率时间序列”；只有事件时显示事件时间轴而不伪造曲线；只有频率时保留曲线并把原因标为未记录；单个文件损坏返回部分报告和具体警告。
- 旧版会话可能已把限制事件完整写入 `summary.json`，但没有后续新增的独立 `.performance-limits.csv`。报告服务现在仅在独立 CSV 不存在且摘要事件数组确实存在时兼容回退，严格校验 `CaptureSessionId + CaptureGeneration`，按发生时间排序后同时用于事件列表和图表区间；详情明确标注“兼容读取旧版 summary.json（未记录独立 CSV）”。摘要没有事件数组的更老记录仍显示“未记录”，不会误报为“未检测到性能限制事件”。本机 `SB-Win64-Shipping-20260714-233449-38960.summary.json` 已确认包含 69 个同会话、同 generation 的 GPU 事件。
- 上述真实旧记录的 69 个事件均缺少后来新增的 `TriggerCount` 与 `WasMerged` JSON 字段；旧实现反序列化后误显示为 `触发 0 次 / False`。事件模型现在记录这两个字段是否实际存在：旧记录显示“确认次数未记录 / 合并状态未记录”，不根据持续时间猜测次数；新记录显示“确认 N 次 / 发生过合并 / 未合并”。其中 `TriggerCount` 的实际语义是限制信号被轮询确认的采样次数，不是独立事件数量。
- 游戏页详情视图改为仅在 `SessionReport` 已加载时通过 `DataTemplate` 延迟实例化，避免切换游戏页时提前构造整棵详情视觉树。详情 XAML 中不存在的 `SettingsComboBox` 资源引用已改为现有 `DashboardHardwareComboBox`；应用级 Dispatcher 异常按“异常类型 + 消息”做一分钟节流，避免同一 XAML 资源错误形成日志风暴。
- 静态曲线使用独立 `SessionTelemetryChart`，支持最多三条曲线、时间/数值轴、图例、限制区间、悬停详情和点击选中区间；`OnRender` 不执行 LINQ、排序或 `ToArray`，画笔/透明填充冻结并复用，不可见时直接返回。限制 tooltip 包含真实起止/持续时间、规范化与原始原因、RawIdentifier，以及区间内实际采样得到的频率/温度/功耗/负载均值；没有区间内样本时显示 `--`。
- 图表下采样为事件感知的 Min/Max/Average 分桶，默认最多 1,500 点。全局最小值、最大值、首尾点、每个事件开始/结束最近的原始采样点会被强制保留，限制区间本身始终使用精确事件时间绘制。报告缓存固定为 4 项。
- 不根据频率下降、高温、低负载或 P-State 推断限制。只有 Tracker 明确识别的 Thermal、Power/Current/EDP、Hardware Slowdown/Power Brake 等状态会生成覆盖区间；Utilization/Idle、Application Clock Setting、Sync Boost、Display Clock Setting 和普通动态调频继续排除。
- 当前隔离 Release 构建为 `0 warning / 0 error`。干净环境此前的自定义 runner 为 `216 passed, 0 failed, 216 total`；本次旧版摘要回退用例及其余 212 项通过，但在 HardwareVision 与 NVIDIA `PresentMon_x64` 同时运行时，3 个既有 PresentMon 端到端用例连续超时，因此本轮完整结果为 `213 passed, 3 failed, 216 total`，失败不在报告/摘要代码路径。仍需在关闭运行实例、释放 PresentMon 环境后复跑全套，并人工确认该 69 事件真实记录的详情列表。其他待人工验证项：真实游戏长会话、托盘持续记录、实际 Thermal/Power/EDP 触发、多 GPU 实机设备选择、与 NVIDIA App 同一窗口对比、详情页视觉/缩放/悬停，以及一小时级文件大小与内存稳态。未使用 Windows GUI 自动控制，也不得把自动测试写成这些实机结论。

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
GameSessions\yyyy-MM\<Game>-yyyyMMdd-HHmmss-<pid>.performance-limits.csv
GameSessions\yyyy-MM\<Game>-yyyyMMdd-HHmmss-<pid>.hardware-timeline.csv
GameSessions\Exports\<Game>-last-60s-yyyyMMdd-HHmmss.csv
GameSessions\Exports\<Game>-cache-yyyyMMdd-HHmmss.csv
```

写入流程：

1. PresentMon 进程成功启动后，如果 `AppSettings.RecordGameSessions` 为 true，创建 recorder 会话。
2. 使用容量 8,192 的有界 `Channel<GameFrameSample>`，多写单读；捕获回调只调用 `TryWrite`，绝不等待磁盘。
3. 第一个样本到达时才创建 `.csv.partial`，UTF-8 BOM、固定英文表头、64 KiB 缓冲，单消费者顺序写入。
4. 每 256 行 flush；完成时先 drain/关闭 writer，再将 partial 原子移动到 `.csv`。
5. 同一 Recorder 会话约每 1 秒把现有 Polling 读数规范化为 CPU/每个 GPU/Memory 行，通过独立容量 512 的后台 Channel 写 hardware timeline；结束时关闭并移动 partial。
6. 第二遍流式扫描 CSV，仅保留最慢 1% 所需的优先队列，计算 1%/0.1% Low；写入冻结后的 performance-limit CSV，然后通过 `.tmp` 原子写 `.summary.json`。
7. 没有帧样本的会话不留下上述会话文件；写盘失败保留可恢复数据并记录错误。
8. 启动恢复：帧 partial 有数据时改名为 `.csv.incomplete`；timeline partial 有数据时改名为 `.hardware-timeline.incomplete.csv`；空或仅表头 partial 删除；无法处理的文件保持原位。

摘要字段包括：HardwareVision/PresentMon 版本、session/generation、PID/进程/窗口/路径、起止/时长、received/written/dropped、平均 FPS、Low FPS、帧时间、CPU/GPU 时间、显示延迟、CPU/GPU 分项能耗/功率、会话平均 CPU/GPU/内存指标、捕获时硬件元数据、限制统计、timeline written/dropped、各文件名、正常完成标记、明确结束原因和 CSV 大小。

结束原因枚举：`UserStopped`、`TargetProcessExited`、`CaptureFailed`、`PermissionDenied`、`ToolUnavailable`、`SchemaMismatch`、`ApplicationShutdown`、`RecorderFailed`、`Unknown`。

注意：设置在捕获开始时读取。捕获过程中关闭自动记录，不会截断已经开始的当前文件；下一会话不再创建记录。

## 6. 页面与设置

- `MainViewModel` 仅立即创建 Dashboard，其余详情页在首次导航时创建。
- 游戏页离开或窗口进托盘不会停止 PresentMon；仅停止该页 UI 图表工作。
- 游戏页显示自动记录开关、状态、当前路径、打开目录/定位文件和历史记录；默认显示 10 条，可每次加载 10 条直至全部记录，也可收起到前 10 条。每条记录可直接打开静态完整会话报告。
- 自动记录关闭时显示“导出当前窗口”和“保存当前缓存”。
- 设置页显示同一开关、记录根目录、目录占用和打开目录命令。
- 不使用 MessageBox 报告导出；状态与完整路径显示在页面，长路径使用 ToolTip。

## 7. 测试

测试项目继续使用自定义控制台运行器，不迁移 xUnit/NUnit/MSTest。

```powershell
dotnet run --project .\HardwareVision.Tests\HardwareVision.Tests.csproj -c Release
```

公开 v0.1.6 预发布当时结果：`73 passed, 0 failed, 73 total`。v0.1.7 发布验证结果：`216 passed, 0 failed, 216 total`；其中阶段一优化基线为 152 项，会话报告新增 64 项。v0.1.8 最终发布验证为 `449 passed, 0 failed, 449 total`。

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

v0.1.8 只发布一个 framework-dependent Windows x64 单文件：

```powershell
dotnet publish .\HardwareVision\HardwareVision.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -p:UseSharedCompilation=false `
  -o .\dist
```

最终 GitHub Release 自定义资产严格只有：

```text
HardwareVision.exe
```

该 EXE 必须是 framework-dependent、win-x64、单文件、非裁剪，ProductVersion `0.1.8`、FileVersion `0.1.8.0`；用户需要预装 Microsoft .NET 8 Desktop Runtime x64。当前不提供 ZIP、自包含版本、PDB 或单独 SHA 文件。工作流可生成内部日志、依赖清单、build info、签名状态和 SHA-256，但它们只能进入 Actions 日志 artifact，不能成为 Release 资产。

发布前检查：

```powershell
git diff --check
git status --short
git diff --stat
git diff -- README.md HANDOFF.md
```

发布准备提交消息：

```text
release: prepare HardwareVision v0.1.8
```

发布顺序：PR #4 merge commit 合并 -> main CI 成功 -> 若发布门禁暴露明确问题则以独立 merge commit 修复并重跑 main CI -> main workflow_dispatch 包验证成功 -> annotated tag `v0.1.8` -> tag package workflow 成功 -> 创建 Pre-release -> 只上传 tag workflow 的 `HardwareVision.exe` -> 下载 Release 资产复核版本/哈希 -> 核对非 Draft/Pre-release/唯一资产。

## 10. 已知限制与后续建议

- package workflow 支持可选 Authenticode；未配置证书的构建会在内部日志中明确标记 `UNSIGNED`，SmartScreen 可能显示未知发布者。
- PresentMon 可用性受权限、渲染模式、反作弊、驱动和游戏实现影响。
- NVIDIA App 等工具使用不同窗口边界/帧筛选时会出现合理的短时偏差。
- 极端磁盘拥塞可能使有界记录队列丢弃写盘副本；摘要有 dropped 数量，实时统计不被阻塞。
- `RuntimePerformanceDiagnostics` 的调用计数/平均耗时是进程启动以来累计值；allocated/GC/CPU 为每个约 60 秒区间。
- 下一轮可在用户允许人工配合时补做真实游戏：等待首帧、长会话、托盘继续写入、目标退出自动完成、NVIDIA App 同窗口对比、多 GPU、更多外接桥接盘和真实证书签名验证。

## 11. 给下一位开发者的简版提示词

```text
先完整阅读 E:\Mine\PCINFO\HANDOFF.md 和 README.md，并检查 git status、最近提交、远端 main、PR、标签与 Release。v0.1.8 的发布资产只有 framework-dependent win-x64 单文件 `HardwareVision.exe`，需要 .NET 8 Desktop Runtime x64；不得重新加入 ZIP、自包含版本、PDB、SHA/build-info/dependency 旁文件作为 Release 资产。不要破坏 .NET 8 WPF/MVVM、唯一 PollingService、PresentMon、状态机、generation/session 隔离、每链稳健帧校验、严格时间戳、CPU/GPU 频率口径、事件去抖、磁盘强冲突拒绝与保守歧义策略、summary schema v4 与 v1–v3 兼容、会话报告旧记录兼容、异步 owner 生命周期和既有性能优化。用户禁止 Windows 应用自动控制；无法替代的真实游戏、多 GPU、overlay、托盘长会话和更多外接盘组合留给人工验证。修改后运行全部 449 项测试和隔离 Release 构建；不得停止用户正在运行的 HardwareVision，受保护 cfg 完全不触碰。
```
