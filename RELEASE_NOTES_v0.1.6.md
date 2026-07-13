# HardwareVision v0.1.6

本版本集中优化长期运行开销，并增加默认开启的完整游戏会话自动记录。硬件采集仍保持 0.5 秒默认周期，PresentMon 版本与游戏统计口径保持不变。

## 性能优化

- WMI CPU 时钟改为按需回退：LibreHardwareMonitor 已有有效核心时钟时直接跳过；否则至少缓存 5 秒，失败采用最长 60 秒指数退避，并复用 WMI 查询对象。
- LibreHardwareMonitor 缓存传感器元数据与类型映射，每轮主要读取动态值；聚合器使用结构化键和单次合并，不再每周期排序/拼接字符串。
- CPU、GPU、磁盘和网络曲线共享固定 240 点环形历史；隐藏页面不再订阅 UI 更新、复制图表数组或执行渲染。
- 图表单次复制并在同一遍扫描中计算当前值、均值、极值与自适应上限，复用冻结的画笔资源。
- Dashboard 使用 250 ms 合并窗口与分类刷新；磁盘/网络前台约 1 秒、后台约 10 秒，并使用 single-flight 与 generation 防止重入和过期结果覆盖。
- 磁盘性能实例名缓存 15 秒，网络静态信息约 30 秒更新；各详情页复用 Dashboard 数据，不再创建重复采集服务。
- 游戏统计在样本锁外计算；相同窗口/版本复用结果，1% Low 与 0.1% Low 最多每秒重算一次。
- 页面改为惰性创建；托盘后台停止页面派生与图表工作，但硬件轮询、PresentMon 捕获和会话记录仍继续。

## 完整游戏会话自动记录

- 开始有效 PresentMon 捕获后自动创建会话，默认开启，可在“游戏”或“设置”页关闭。
- 使用容量 8,192 的有界 Channel，帧回调只执行非阻塞 `TryWrite`，单后台消费者顺序写盘。
- CSV 使用 UTF-8 BOM、固定英文表头、ISO 8601 时间和 invariant-culture 数值。
- 写入期间使用 `.csv.partial`；正常完成后原子重命名为 `.csv`，再原子写入 `.summary.json`。
- 摘要包含版本、进程与窗口信息、会话 ID/generation、起止时间、收/写/丢样本数、平均 FPS、Low FPS、帧时间、CPU/GPU 时间、显示延迟、结束原因和文件大小。
- 程序启动时扫描残留 partial：有数据文件恢复为 `.csv.incomplete`，空或仅表头文件删除，不会丢弃有效记录。
- 记录按 `Documents\HardwareVision\GameSessions\yyyy-MM` 保存；最近记录无需数据库，按摘要/恢复文件即时索引，最多显示 10 条。
- 关闭到托盘不会停止记录；用户停止、目标退出、PresentMon 失败、格式不兼容和应用退出均使用明确结束原因。

## 导出改进

- 自动记录关闭时，游戏页提供“导出当前窗口”和“保存当前缓存”。
- 当前窗口文件名包含窗口秒数；缓存导出最多包含现有 60,000 条会话隔离样本。
- 导出通过临时文件顺序写入，关闭文件句柄后再原子重命名，不再在内存中构造整个 CSV 字符串。
- 页面可打开记录目录、定位当前记录或最近导出文件；文件不存在时回退打开其目录。

## 测试与验证

- 自定义控制台测试从 32 项扩展到 73 项，全部通过。
- 新增覆盖：WMI 跳过/缓存/过期/退避，刷新合并，single-flight，240 点历史，统计窗口与缓存，CSV 转义/BOM/临时文件，会话隔离、恢复、摘要、Low FPS、最近记录，以及假 PresentMon 到自动会话文件的端到端链路。
- Release 构建：0 警告、0 错误。
- 5 分钟非管理员 Dashboard 进程级复测未使用 Windows 应用控制。相较修改前基线：
  - 每分钟 WMI 查询约 `98 -> 11`；
  - 磁盘/网络更新约 `98 -> 50`；
  - Dashboard 派生刷新约 `294 -> 100`；
  - 每分钟托管分配约 `258 MB -> 137 MB`；
  - 平均工作集约 `346.7 MiB -> 323.1 MiB`；
  - 启动到 `MainWindow.Show` 约 `1.70 s -> 1.01 s`。
- 本次环境中 LHM 单次更新约 93 ms，慢于修改前基线约 91 ms；5 分钟平均 CPU 为 0.279%，修改前为 0.211%，因此不宣称本机总 CPU 已下降。优化后的调用次数、分配量和工作集下降均由诊断计数确认。
- 按用户要求未使用 Windows 应用控制；CPU/GPU 页面切换、托盘交互和真实游戏实机捕获未在本轮自动复测。PresentMon 假进程端到端记录已通过，真实游戏仍建议在目标机器上人工确认。

## 兼容性与保持不变

- .NET 8、WPF、MVVM、LibreHardwareMonitor、WMI 回退、0.5 秒采集和 PresentMon 2.5.1 保持不变。
- 保留 PresentMon v2/旧 CSV 格式兼容、ETW 清理、目标解析、状态机、generation/session 隔离和 60,000 样本上限。
- 当前 FPS、平均 FPS、1% Low、0.1% Low、CPU/GPU 帧耗时与显示延迟口径保持不变。

## 下载

- `HardwareVision-v0.1.6-win-x64-lite.exe`：Framework-dependent 单文件，需要 .NET 8 Desktop Runtime x64。
- `HardwareVision-v0.1.6-win-x64-self-contained.exe`：自包含单文件，无需另装 .NET。
- `SHA256SUMS.txt`：两个可执行文件的 SHA-256。

两个可执行文件均为 Windows x64、非裁剪、未数字签名，并默认请求管理员权限。

## SHA-256

```text
0AE9ACC42E1839F96A0D82455C02AC2AC79E007E3E5A1D698C2EB0823994F648 *HardwareVision-v0.1.6-win-x64-lite.exe
2B52A2B6FF4C3BF30EA8432822AB37C5D5BB7856EF04FC45BFE53C29375BA497 *HardwareVision-v0.1.6-win-x64-self-contained.exe
```

## 已知限制

- PresentMon 数据仍受渲染 API、反作弊、驱动、权限和游戏呈现模式影响。
- 与 NVIDIA App 的短时差异通常来自窗口边界与帧筛选策略；比较时应对齐场景和统计区间。
- 会话记录队列在极端磁盘拥塞下可能丢弃“写盘副本”，摘要会记录数量；实时统计与捕获回调不会因此阻塞。
