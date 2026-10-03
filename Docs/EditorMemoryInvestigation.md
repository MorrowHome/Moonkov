# 编辑器内存耗尽排查（2026-10-03）

用户反馈未进入 Play 也会黑屏、显示器丢失信号，需要长按电源重启。

## 已确认的证据

- Windows System 日志在 18:00、21:31、21:38、21:43 记录 Resource-Exhaustion-Detector 事件 2004：虚拟内存不足。21:31 的一个 Unity 进程提交内存约 140.3 GiB，21:43 约 138.7 GiB；这不是物理内存或显存读数。
- 后续 Kernel-Power 41 表示未正常关机后的重启，不能单独证明电源故障。
- 重启后的 FPS_Template 主 Editor 使用 DX12，MainMenu 场景，未 Play；内存继续增长，托管堆使用约 9 GiB。Profiler 没有录制，Console 只有少量消息。此时不能归因于新搜尸代码的 Play 逻辑。
- `MoonkovUIPreview` 仍然打开，角色、月球及矢量界面通过调度器持续刷新。关闭此窗口并进行一次诊断性 GC 后，托管使用从 9038 MiB 降到 1362 MiB，随后约 1365 MiB。进程提交内存仍约 29 GiB：回收对象不会保证保留堆及驱动内存马上归还操作系统。

事件 2004 的含义见 [Microsoft 的内存泄漏排查文档](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-memory-leaks)。当前证据确认了 Unity 导致的内存耗尽，但关闭窗口与 GC 同时进行，尚不能区分预览分配、编辑器 GC 调度、URP／DX12 原生资源等具体根因，也没有证明黑屏完全由该问题造成。

## 本轮控制措施

- UI 编辑器预览默认静态；角色仅在初次显示或尺寸变化时渲染，月球只响应必要的重绘；持续矢量背景与跑马灯停止动画。
- 窗口上方 `ANIMATE PREVIEW / 30 SECONDS` 可手动开启 30 秒动画，随后自动回到静态。Play 中的游戏界面继续正常动画。
- 没有添加定时强制 GC、扩大分页文件、升级 Unity、改变图形 API 或停止服务器。该措施用于消除一个确认存在的持续预览入口，不宣称已彻底修复引擎／驱动层泄漏。

针对性检查通过：静态预览的月球时间不会自行推进；手动开启动画有效；模拟到期后恢复静态。编译无错误，未启动 Play 或截图。重启后项目专用数据库与后端没有自动恢复，已使用现有 `Start-Backend.ps1 -Background` 恢复；另一个无窗口 Unity 进程确认是资源导入 worker，而不是游戏独立服务器。

若关闭预览后仍持续快速增长，先保存并重启 Editor 释放保留内存；后续再对关闭预览的空闲 Editor、短时预览、实际 Play 分别做有界比较，必要时检查 DX11 对照和内存分配来源，避免长时间压力测试再次耗尽内存。
