# MC00 Full v02 灯光性能回归修复

2026-10-11。在 Full v02 候选的 L22 操作台原站位执行自动 Play 采样，修复批量设置 Inner 120° / Outer 179° 后的严重性能回归。旧正式地图、源模型和测试场景的 Git 忽略规则保留。

## 保存的配置

- 118 盏 `Runtime Light`：Inner **120°**、Outer **150°**。保留宽内角，降低近 180° 投影开销；角度边缘照明会有变化，并非画面完全不变。
- 候选独用 `Assets/MoonEnvironment/Variants/MC00LightControlsFullV02/Settings/MC00InteriorPipeline.asset`，由候选 Prefab 的 `MoonSceneLighting.pipeline` 引用。
- 实时阴影距离 **30 米**，GPU Resident Drawer **Disabled**；Forward+、SRP Batcher、2560×1440 输出下的 0.75 Render Scale、光强、Light.range、灯具开关和近处软阴影保留。
- 原共享 `Apollo12LaptopPipeline.asset` 不变。30 米属于本次室内性能配置：远处阴影会淡出，不能未经外景验证直接推广到整张正式月面地图；关闭 GRD 也只是这份资产组合的实测选择。

这是资产配置修复，不添加运行时扫描、按距离开关灯或物理查询。临时剔除矩阵、包围球和阴影合并实验全部恢复，没有留下额外运行组件或生成网格。

重建 Full v02 后，可执行 **Tools → Moon Environment → MC00 Full Lights v02 → 5 - Apply measured interior lighting preset**。工具检查 118 灯和唯一环境控制器，备份候选，另建独立配置并保存 Prefab。未来模型版本必须先适配自己的路径和灯位数量，不能盲用本版 118 灯断言。

## 实测及原因

硬件：**RTX 4070 SUPER、i5-14600KF**。Unity 6000.5.10f1 编辑器 Game 视图，2560×1440，内部缩放 0.75。摄像机原地，未注入移动输入。UnityStats 可能包含编辑器其他视图，因此提交计数作为同条件归因，不当作独立 Player 的精确 draw-call 报告。

第一轮在所有阶段开启相同 Profiler 设置：

| 同一站位设置 | 帧耗时中位数 | 阴影投射统计中位数 |
| --- | ---: | ---: |
| 179° 外角、原阴影 | 177.2 ms | 370,723 |
| 179°、临时关闭 MC00 阴影（仅诊断） | 25.4 ms | 35,887 |
| 150° 外角、原阴影距离 | 40.7 ms | 66,261 |
| 130° 外角、120° 内角 | 33.3 ms | 43,730 |

代表性坏帧的渲染线程 `Draw Additional Lights Shadowmap` 约 152.7 ms，伴随 D3D12 scratch buffer 回收开销。CPU/GPU 帧计时可能含等待，不能把全部时间归为纯 CPU 计算或纯 GPU 运算。证据支持“大外角放大额外灯阴影提交”是此次回归的主要原因。

后续关闭 Profiler 的短采样中，150° 外角配合 60 米阴影约 28.1 ms，30 米约 20.9 ms。在相同 **150° / 30 米** 条件下，仅比较 GRD：

| GRD | 中位数 | P95 |
| --- | ---: | ---: |
| Disabled | 13.77 ms | 16.46 ms |
| InstancedDrawing | 19.31 ms | 23.42 ms |

因此最终保留 GRD Disabled。其 UnityStats 阴影对象计数反而更多，但帧耗时更低；不能只根据对象数或 draw-call 数判断性能。

## 保存后重新 Play 的最终检查

独立重新进入 Play，确认实际运行的是新候选 Pipeline，118 灯都为 120° / 150°，阴影仍开启。Profiler 关闭，稳定等待后采集 561 帧：

| 指标 | 结果 |
| --- | ---: |
| 帧耗时中位数 | **13.90 ms（约 72 FPS）** |
| P95 | 17.21 ms |
| P99 | 29.19 ms |
| 超过 16.667 ms 的帧比例 | 7.49% |
| 位置 / 朝向漂移 | 0.004 米 / 0° |

自动调用最近的 ALL MC00 实体按钮 `Press()` 玩法入口：全关时 118 Light 全部 disabled、组状态 AllOff；再次按下后全部恢复 enabled、非零强度、组状态 AllOn。检查开关截图，指示灯和室内照明有对应变化；不是模拟 F 键射线命中，也不是 135 键逐键验收。最终恢复全开。

当前严重回归已明显缓解，**仍不宣称稳定 60 FPS**，也未进行 4060 Laptop、独立构建、跨房间行走或外景性能验收。后续优先处理余下帧尖峰和可见物件提交，避免继续扩大灯锥或无依据增加实时灯。

本地原始证据在 `Library/BuildValidation`：`MC00AngleCost.phases.json`、`MC00ShadowDistance.csv`、`MC00Drawer.csv`、`MC00Final.json`、`MC00Final-on.png`、`MC00Final-off.png`。Library 文件不提交；最终摘要随本文保留。修改前候选备份：`Library/BuildValidation/MC00LightingBefore-20261011-140952947`。
