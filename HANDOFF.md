# HardwareVision 开发交接

## 当前正式线

- 当前版本：HardwareVision **2.0.4**。
- 集成边界：PR #12，`fix/2.0.4-igpu-dashboard-layout` 以 merge commit 合并到 `main`。
- 正式发布时，`main` 的 PR #12 merge commit、annotated `v2.0.4` tag 解引用后的提交和 GitHub Release 源提交必须完全一致；精确 SHA、CI run、tag workflow 和资产摘要记录在 PR #12 与最终发布报告中。
- v2.0.0、v2.0.1、v2.0.2、v2.0.3 的 tag、Release 元数据和资产为只读历史，不得移动、覆盖或删除。
- 自动化正式基线为 `2648 passed / 0 failed / 2648 total`。正式门禁还要求 Release/Debug/Test 零警告零错误、定向矩阵、2400+ 导航压力、包审计、两轮冻结二进制完整测试、PR/main/package CI 和正式资产复核。

## 架构不变量

- 只有一个 `MainWindow`、一个 `MainShellHost`、一个名为 `PageHost` 的 `MotionTransitionHost` 和一个 `MainViewModel.CurrentPage` 绑定。
- App 创建并拥有唯一服务图：settings、theme/motion、navigation、startup、polling、history、hardware refresh、foreground tracking、PresentMon、recording、report、tray 与 diagnostics。
- 页面 ViewModel 按需创建并缓存在 `NavigationItemViewModel`；普通导航不得清空缓存、复制全局服务或创建第二个 Shell/PageHost。
- `PollingService` 是单飞循环。页面激活只控制 UI 消费与局部刷新，不创建额外硬件采集循环。Dashboard 初始 Projection 复用现有首轮数据源生命周期。
- SYSTEM REWIRE 优先于 FLOW RELAY；关闭、隐藏、最小化、主题 takeover 与 generation replacement 必须收敛到一个确定终态。

## GPU 与 Dashboard 不变量

- 每个 GPU telemetry 必须以稳定 GPU identity 为边界；每块 adapter 只从自己的传感器集合计算 `CoreLoad`。
- Primary GPU load 只允许精确匹配 `GPU Core`、`GPU Core Load`、`GPU Load`；Intel 可受控回退到精确 `D3D 3D` 或 `3D`。Video Decode/Encode、Copy、Overlay 和其他 D3D engine 不得伪装成总 GPU load。
- 真实 0% 必须保持 available/reported；没有 canonical load 时保持 missing，不跨 adapter、CPU 或全局最大值回退。Dashboard、GPU 详情和 history 必须复用相同的规范化 `GpuDevice.CoreLoad` 与设备 ID。
- Dashboard Wide 首行固定为独立 CPU 7/12 与 GPU 5/12；Memory、Disk、Network、System 是后续四个独立 3/12 responsive children，DataRail 再占全宽。不得把整列 secondary modules 重新塞入 Row0 单一 StackPanel。
- Standard、Compact、Narrow 必须保持 8/4/1 列合同与无重叠、无水平溢出；Classic Dashboard 和详情页响应式合同不随首页 Wide 调整而改变。

## 页面与报告路由

普通页面切换在 Route 阶段提交目标业务状态：`CurrentPage`、当前导航项、标题、副标题、route、持久化页面 key、旧页停用和新页激活在同一提交中改变。视觉层保留旧 presenter，并在新 presenter 已加载、安排且完成真实 Render 后继续 Shift/Relay/Settle。

当前计划：

| Motion | Route | Shift | visual Relay | Settle | Total |
|---|---:|---:|---:|---:|---:|
| Full | 70 ms | 120 ms | 190 ms | 220 ms | 420 ms |
| Standard | 50 ms | 90 ms | 140 ms | 160 ms | 320 ms |
| Reduced | 0 ms | 50 ms | 50 ms | 100 ms | 150 ms |
| Off | 0 | 0 | immediate | 0 | 0 |

`CachedPagePresenter` 维护一个缓存 presenter 集合和一个短生命周期 outgoing/incoming 双层。旧 presenter 只在至少一个真实重叠帧之后清理；清理由 navigation version、content generation、unload 与 dispose 防护，不能删除当前页面。

GamePerformance 与 GameSessionReport 的关系：

- `GamePerformanceViewModel` 始终是“游戏”导航项缓存的实例，持有会话列表、分页、滚动位置和报告创建入口。
- 打开报告时创建独立 `GameSessionReportViewModel`，并把它设置为唯一 `CurrentPage`；Shell route 为 `GameSessionReport`，标题为“会话报告”，但“游戏”导航项继续选中。
- 报告加载使用自己的 generation/取消所有权。旧报告不得回写后续报告；离开报告或关闭窗口会使旧加载失去写权限并释放句柄。
- 关闭报告把 `CurrentPage` 恢复为同一个缓存的 `GamePerformanceViewModel`，恢复 `GamePerformance` route、标题和实时 UI timer，不重新创建游戏页。
- 文件缺失、损坏、无权限或目录错误显示为当前记录的有界错误；不得跳到 Dashboard/Settings 或其他缓存页。

## 启动顺序

App 创建服务图并启动唯一 Polling 后，`INITIAL TRACE` 使用真实里程碑：

```text
theme/resources + service graph + page router + history
-> first polling/source lifecycles
-> first-frame gate + measured Shell surface
-> Index -> Route -> Bind -> Lock
-> final SENSOR BUS Detail
-> source port -> target port -> committed port frame
-> Projection route -> one Pulse
-> COMMIT -> Reveal -> Complete
```

- Dashboard 的 CPU、GPU、Memory、Disk、Network、System 各自等待首个数据源完成或明确失败；普通后续刷新不能重开启动序列。
- SENSOR BUS 的最终 Detail 必须先完成文字呈现并经过最终布局确认。端口随后按顺序进入，端口可见帧提交后才允许线路与 Pulse。
- 首次合法 6/6 终态最多授权一次 Pulse。PollingVersion、重复 snapshot、主题切换、窗口恢复、Reveal 后更新和 stale callback 都不能重播。
- Projection 请求可跨 Bind 保留到 Lock。Pulse 完成后通过独立 Render turn 授权 COMMIT；若无法呈现，Lock 时启动的新 700 ms fail-open 保证不会永久停留。已提交可见帧但未完成的 Pulse 由 1500 ms guard 收敛。
- `CompositionTarget.Rendering` 只允许由启动 Projection 和 presenter 重叠帧拥有的短生命周期、generation-guarded 订阅；所有终态都必须解除。不得扩展为常驻生产循环。

## Motion 默认值与兼容

- `new AppSettings()`、缺失设置文件、空文件或损坏恢复的 RequestedLevel 默认 **Full**。
- 已有合法 Full、Standard、Reduced、Off 原样保留，不做强制迁移，不在启动时覆盖。
- OS 减少动态效果可把 EffectiveLevel 降低，但不能改写 RequestedLevel 或保存的用户选择。
- Classic 与 Tracework 共用业务路由、页面缓存和服务图；主题只改变资源、版式与有限视觉时钟。

## 生命周期与鲁棒性结论

本次逐项审查覆盖 App/Window/tray、服务图、导航/缓存、INITIAL TRACE、Dashboard、所有硬件页、Advanced Sensors、PresentMon/foreground tracking、录制/目录/报告、settings、logging、provider、XAML/resources、dispose/cancellation、CI/package 和测试夹具。

已修复：

- 最近会话错误路由：报告改为独立 CurrentPage 内容对象，route/content/title/selection 一致，返回复用游戏页。
- Theme 与 ViewModel background producer 不再使用同步 Dispatcher 等待；UI 关闭期间的投递有 shutdown guard。
- Settings 自动启动查询/写入改为异步、串行、可取消、latest-wins；`schtasks` stdout/stderr 并发排空，取消终止准确子进程。
- 游戏会话与报告目录准备失败进入有界错误状态，不再从异步命令逃逸。
- Projection 在 Lock 获得完整 fail-open 预算，避免较早 Bind 请求耗尽预算后立即跳过视觉呈现。
- 测试等待改为观察合法生命周期终态，而不是依赖极窄调度边界；生产时序未因此放宽。

明确保留：

- App、recorder、timeline、foreground tracker 与 PresentMon 的同步 Dispose 入口保留，因为它们同时提供异步/取消路径或处于进程退出边界；没有证据证明重写能降低风险而不改变 shutdown 顺序。
- provider fallback、旧 session schema、兼容构造器、绑定名、资源 key 和 bounded startup `UpdateLayout` 保留。它们具有 XAML、兼容或 fail-open 责任，不能按“零静态引用”删除。
- 未证明无用的 helper、测试夹具和历史格式读取器全部保留。没有为整洁进行无关大重构。
- v2.0.4 性能审查保留 polling 单飞、采样频率、固定容量 history、页面激活策略和动画时序。没有测量证据支持的 LINQ 微优化、绝对布局、额外 Dispatcher 投递或 provider 缓存不得以“性能”名义引入。

## 支持与测试边界

正式支持 Windows 10/11 x64、net8.0-windows WPF、.NET 8 Desktop Runtime x64。自动化覆盖：

- 100/125/150/175/200% DPI 坐标与响应式布局逻辑；普通/最大化和负坐标/多显示器放置模型；
- provider 正常、空值、部分 WMI/PerformanceCounter/LibreHardwareMonitor/NVML 失败；
- PresentMon 不可启动、无候选游戏、session/settings/log 目录缺失或不可写、session/settings 损坏；
- GPU/磁盘/网络/传感器部分缺失；托盘、关闭、shutdown 中异步工作、OS 减少动态效果；
- Full/Standard/Reduced/Off 与 Classic/Tracework 的外部路由一致性。

Intel iGPU 的 0%、非零负载、多 GPU identity 隔离和 Dashboard/详情一致性，以及 Dashboard 全屏 Wide 布局已在真实 Intel 多 GPU 设备完成 v2.0.4 目标场景验证。该验证不扩展为对所有 GPU、驱动、主板、DPI、RDP 或软件渲染组合的覆盖声明。

DPI、多显示器、provider 与权限异常中的一部分使用 stub、fault injection 或 WPF runtime fixture。未声称在所有真实主板/GPU/驱动、RDP、软件渲染器、刷新率或显示器排列上完成实机验证。可选能力失败必须保留主窗口、去重状态/日志、释放资源，并在数据源恢复后允许刷新。

## 构建与发布

发布顺序固定为：

1. 分支上完成零警告 Release App、Debug App、Release Tests、定向矩阵、包审计与 `git diff --check`。
2. 冻结一个 Release Tests EXE/DLL，连续运行两轮；中间不构建、不修改，记录 SHA-256、总数、exit code 与 stderr。
3. PR #12 保持 Draft 直至最终 review 与 CI 全绿，然后转 Ready。
4. 使用 merge commit 合并；禁止 squash/rebase merge。
5. `main` CI 全绿后，在 merge commit 创建 annotated `v2.0.4`。
6. tag package workflow 必须生成 win-x64、framework-dependent、single-file、untrimmed 的唯一 `HardwareVision.exe`，验证 AMD64、版本、requireAdministrator、PresentMon/notice、签名状态和 attestation。
7. GitHub Release 不是 Draft/prerelease，公开资产恰好一个，不上传 ZIP、DLL、PDB、checksum 或内部日志。

## 维护保护边界

- `HardwareVision/Controls/RealtimeLineChart.cs.baiduyun.uploading.cfg` 是永久保护文件：不得读取、修改、移动、删除、暂存、提交或哈希。任何递归扫描必须显式排除它。
- 如果 `git status` 显示该路径 modified/deleted/staged/untracked，立即停止，不做 restore/checkout/reset。
- 禁止 `git reset`、`git rebase`、amend、force push、`git clean`、`git add .`、`git add -A`、squash merge、rebase merge 和直接修改 main。
- 不启动 requireAdministrator 的正式 EXE；测试使用 WPF fixture 与控制台 runner。
- 不用 `Task.Delay` 掩盖竞态，不引入同步 `Dispatcher.Invoke`、未观察 Task、非事件 async void、常驻 Rendering 订阅或普通页面 `UpdateLayout` 修复。
