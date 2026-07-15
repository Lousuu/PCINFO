# HardwareVision v0.1.7

本版本扩展游戏会话硬件遥测与静态报告，增加 CPU/GPU 估算能耗、明确性能限制事件、磁盘健康可靠性详情，并继续降低长期运行和游戏采集热路径开销。

## 完整游戏会话报告

- 最近记录新增“查看详情”，在游戏页内加载静态完整报告，不影响后台采集和自动记录。
- 报告包含 FPS、Frame Time、CPU Busy、GPU Time、Display Latency、CPU/GPU 频率、温度、估算功率、限制区间、事件列表、关键统计与捕获时硬件快照。
- 静态图表支持多序列、时间/数值轴、事件区间、悬停详情和事件感知下采样；长会话默认限制为 1,500 个绘制点。
- CPU 普通核心频率、Effective Clock 与最大核心频率分开记录；GPU Core/Memory Clock 分列，多 GPU 独立保存和选择。
- 报告服务在后台流式读取文件，逐帧 CSV 不绑定全量 UI 集合，并固定缓存最近 4 份报告。

## 能耗与性能限制记录

- 新增游戏会话 CPU/GPU 估算能耗、当前/平均估算功率、有效积分覆盖率和包含组件说明；不把估算值描述为整机墙上功耗。
- 每个物理 CPU/GPU 只选择一个代表功率传感器，使用单调时钟和梯形积分；组件缺样时只打断自身连续性，不跨缺口补算。
- 新增 CPU/GPU 性能限制日志，只记录硬件或驱动明确上报的 Thermal、Power、Current/EDP、Hardware Slowdown/Power Brake 等原因，不根据频率、温度或负载猜测降频。
- 限制事件按会话 ID 与 generation 隔离，包含去抖、结束确认、相同原因短窗口合并和固定容量历史。
- 完成会话后写入独立 `.performance-limits.csv`；硬件传感器时间线以约 1 秒间隔写入 `.hardware-timeline.csv`，写入回调使用有界非阻塞队列。

## 磁盘健康与可靠性

- 新增健康/运行状态、当前/最高温度、剩余寿命/磨损、累计读写、通电时间/次数、错误计数及最高延迟。
- 数据源优先使用 Windows Storage WMI，其次 LibreHardwareMonitor，最后 Win32 状态；不支持、权限不足或驱动未报告时显示 `--`。
- 设备合并优先使用 UniqueId/ObjectId/序列号，并拒绝序列号冲突、容量冲突和同分歧义，避免同型号多盘误合并。
- Storage WMI 使用 single-flight、成功缓存和失败退避，不进入 0.5 秒硬件采集热路径。

## 性能与稳定性

- PresentMon 目标 PID 同时下推到采集源并保留应用层过滤；CSV 表头只解析一次，非目标行不再创建样本对象。
- 移除帧样本中的原始 CSV 行保留；游戏页使用可见时 500 ms UI 拉取，不再订阅每一帧事件。
- Polling 使用单调固定周期计划，慢采集跳过已错过周期并避免重入；单个订阅者异常不会阻断其他订阅者。
- Dashboard 刷新合并、硬件历史环形缓冲、Storage WMI single-flight、partial 恢复 single-flight 与报告固定缓存共同限制长期运行开销。
- 应用级 UI 异常日志按异常类型与消息节流，避免重复 XAML 错误形成日志风暴。

## 兼容性与修复

- 修复游戏页详情 XAML 资源缺失导致页面空白、加载缓慢的问题，并延迟构造详情视觉树。
- 旧版会话若只在 `summary.json` 保存限制事件、没有独立限制 CSV，报告会严格校验会话标识后兼容加载事件列表和图表区间。
- 旧事件缺少 `TriggerCount`/`WasMerged` 字段时显示“确认次数未记录/合并状态未记录”，不再误显示为 `0/False`；新记录显示实际确认采样次数和合并状态。
- 缺少硬件时间线的历史记录仍可查看逐帧性能；缺失或损坏的单个辅助文件返回带说明的部分报告。

## 测试与验证

- 自定义控制台测试共 216 项，覆盖 PresentMon、会话记录、能耗积分、限制事件、磁盘可靠性、硬件时间线、静态报告、事件感知下采样、旧记录兼容与视图资源加载。
- Release 构建：0 警告、0 错误。
- 本机真实旧记录已确认可从摘要兼容加载 69 个同会话 GPU 限制事件。
- 未使用 Windows GUI 自动控制；真实长时间游戏、多 GPU、托盘持续记录和实际 Thermal/Power/EDP 触发仍建议在目标机器上继续验证。

## 下载

- `HardwareVision-v0.1.7-win-x64-lite.exe`：Framework-dependent 单文件，需要 .NET 8 Desktop Runtime x64。
- `HardwareVision-v0.1.7-win-x64-self-contained.exe`：自包含单文件，无需另装 .NET。
- `SHA256SUMS.txt`：两个可执行文件的 SHA-256。

两个可执行文件均为 Windows x64、非裁剪、未数字签名，并默认请求管理员权限。

## SHA-256

```text
E6EEDFA7A077F48666EDE0E1C4CD636F036F9D8209CFCB1C40C0D6FF6AE3156F *HardwareVision-v0.1.7-win-x64-lite.exe
EA05359DA1CC6A8FEE72E8A027E52731582A04E7CA9A8418679B32366ABD9C4B *HardwareVision-v0.1.7-win-x64-self-contained.exe
```

## 已知限制

- PresentMon 数据受渲染 API、反作弊、驱动、权限和游戏呈现模式影响。
- 与 NVIDIA App 等工具比较时应对齐统计窗口、场景与开始/停止时刻；不同帧筛选和窗口边界会导致短时差异。
- 极端磁盘拥塞可能导致有界记录队列丢弃写盘副本，摘要会记录丢样数量，实时捕获回调不会因此阻塞。
- 估算能耗仅来自可用 CPU/GPU 功率传感器，不等于整机插座功耗。
