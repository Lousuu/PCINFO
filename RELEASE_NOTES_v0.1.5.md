# HardwareVision v0.1.5

本版本改进游戏性能页面的进程发现与选择体验，减少在大量系统进程中手动查找游戏的操作。

## 新增与改进

- 增加进程搜索框，可按显示名称、进程名、中文窗口标题、完整路径、EXE 文件名及无扩展名文件名进行不区分大小写的本地过滤。
- 搜索只处理已经获取的完整候选列表，不会在每次输入字符时重新枚举系统进程。
- 清空搜索后恢复完整列表；当前选择暂时被过滤时不会自动切换到其他进程，清空搜索后恢复原选择。
- 增加“识别游戏”按钮，只选择高置信度候选，不会自动开始 PresentMon 采集。
- 增加全局最近外部前台窗口追踪，使用 WinEvent Hook 保存最近外部前台 PID、窗口标题、句柄和时间。
- WinEvent Hook 初始化失败时自动降级为每秒一次的低频前台窗口轮询，并在应用退出时完整释放。
- 增加可测试的游戏进程评分：最近前台、可见主窗口、`Win64-Shipping` / `Win32-Shipping`、Steam/Epic/Xbox/GOG/Games 目录和近期启动会获得加分。
- launcher、bootstrap、updater、helper、crash reporter、overlay 等辅助进程降权；浏览器、IDE、Shell 和常见非游戏程序大幅降权但仍可搜索。
- 进程列表按最近外部前台、高置信度游戏、有主窗口进程和其他可搜索进程分组，再按评分、名称和 PID 排序。
- 使用 PID、进程名、路径和启动时间共同保留选择，降低 PID 重用造成的错误匹配。
- 区分自动选择、手动选择和搜索后选择；有效的用户选择不会被普通刷新或自动识别覆盖。
- `Preparing`、等待首帧、采集中和停止中均锁定实际采集目标，不允许刷新或搜索切换目标。
- 多次刷新使用 generation 与取消令牌隔离，过期结果不会覆盖较新的进程列表。
- 游戏性能顶部调整为两行/自动换行布局，在较窄窗口下搜索框、进程下拉框和操作按钮不会严重挤压。
- 高置信度候选显示“可能是游戏”标记，进程路径继续通过 ToolTip 展示。
- 自定义测试从 16 项扩展到 32 项，覆盖搜索、评分、降权、模糊候选、前台 PID、手动选择保护和采集状态锁定，同时保留全部 PresentMon 原有测试。

## 保持不变

- PresentMon 2.5.1 v2 与旧格式 CSV 兼容。
- `GameCaptureState` 状态机、采集 generation、session ID 和样本会话隔离。
- `GameCaptureTargetResolver` 启动器子进程解析。
- 当前 FPS、平均 FPS、1% Low、0.1% Low、CPU/GPU 帧耗时和显示延迟统计口径。
- PresentMon 内嵌资源提取、SHA-256 校验、ETW 会话清理和实时 stdout 消费。

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

## 注意事项

- 本版本尚未进行数字签名，Windows SmartScreen 可能显示未知发布者。
- 自动识别依赖进程路径、窗口、最近前台记录和命名特征；低置信度时会要求搜索或手动选择。
- 浏览器云游戏默认不会作为高置信度本地游戏自动选择，但仍可通过搜索找到。
- PresentMon 的可用数据受游戏渲染模式、权限、反作弊策略和驱动支持影响。

## SHA-256

`63E4415D39CDFA1BCDCD28037CE4B8E8B073DDDCD709EEF7E087258B2A3BF59E`
