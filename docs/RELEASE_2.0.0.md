## 主要更新

- HardwareVision 2.0.0 完成 TRACEWORK 全新视觉系统与 Stage 6；Classic 主题完整保留，可继续随时切换。
- 12 个正式页面均采用完整静态视觉语言与响应式构图，包括 Dashboard、CPU、GPU、Memory、Disk、Network、Motherboard、Advanced Sensors、Game Performance、Session Report、Settings 和 Metric Visibility。
- 引入由真实初始化里程碑驱动的 `INITIAL TRACE` 开机启动序列；Full、Standard、Reduced、Off 与 Classic 均有明确且有界的行为。
- FLOW RELAY 提供普通页面的单 PageHost 路由过渡；SYSTEM REWIRE 提供主题切换状态层，两者的提交点、优先级和清理边界保持确定。
- 保持唯一 MainWindow、MainShellHost、PageHost、CurrentPage 绑定和现有服务图；启动序列不会重复扫描硬件、重复轮询或伪造进度。
- 高级传感器使用有界投影、稳定行复用、字典协调、虚拟化和节流刷新，降低大规模传感器矩阵的 UI 分配与重排成本。
- 修复高级传感器状态每三秒闪回、会话诊断摘要被最小高度撑大、GPU 选择器未满宽/未对齐、Dashboard CPU 主仪表与末行拉伸四项最终问题。
- 游戏性能监控继续通过内嵌 PresentMon 采集 FPS、帧时间、1% Low、0.1% Low、CPU/GPU 帧耗时和显示延迟，并保留 warm-up、SwapChain 选择与异常帧过滤。
- 会话报告继续支持逐帧数据、硬件时间线、性能限制、频率/温度/功耗、能耗、历史分页、GZip 流式记录、partial 恢复和旧 schema 兼容。
- 硬件遥测继续覆盖 CPU、GPU、内存、磁盘、网络、主板和高级传感器，并保留共享轮询、固定容量历史、设备变化刷新与磁盘桥接身份合并。
- 将报告时间对齐测试改为固定单调时间，消除 `Report accuracy 01` 对系统时钟分辨率的偶发依赖；生产解析、过滤、容差和会话格式不变。
- 完成全项目性能、生命周期、逻辑、安全和依赖复核；无未解决的 Critical / High 发现。

## 运行要求

- Windows 10/11 x64。
- 需要 Microsoft .NET 8 Desktop Runtime x64。
- 程序清单请求管理员权限；PresentMon ETW 游戏采集同样需要管理员权限。
- 发布物为 framework-dependent、win-x64、单文件、未裁剪的 `HardwareVision.exe`。

## 验证

- 版本：`2.0.0`；Assembly/File：`2.0.0.0`；Informational：`2.0.0`。
- 自动测试：`1366 passed, 0 failed, 1366 total`，高于 1235 基线。
- Release / Debug 构建、两次独立完整 Release 测试、依赖漏洞/弃用审计、单文件打包、PE x64、版本、清单、PresentMon/第三方声明、签名状态和 SHA-256 均纳入发布验证。
- GitHub Release 只包含一个资产：`HardwareVision.exe`；不提供 ZIP、校验和、build-info 或 signing-status 附件。
- 唯一下载资产为 `HardwareVision.exe`；GitHub 自动提供的源码归档不属于手动 Release 资产。

## 已知限制

- 本轮按验证边界未启动需要管理员权限的正式 EXE，也未进行截图/人工像素检查。
- 不同 DPI、高对比度、远程桌面、低渲染等级和真实管理员硬件传感器负载仍建议进行人工环境验证。
- 若发布工作流未配置 Authenticode 证书，EXE 会明确记录为未签名，Windows SmartScreen 可能显示未知发布者。
