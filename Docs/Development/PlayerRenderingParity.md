# Editor Play 与 Player 渲染一致性

2026-10-23 修复打包路径中的两处缺陷，未调整模型、材质风格或场景光照。

- `MoonSceneLighting` 在 Play 时切换到场景引用的管线。正式 Theophilus 地图使用 `MC00BakedPipeline`，其传统 Light Probe 配置与 Graphics/Quality 中的 PC 管线不同。URP Global Settings 的 `IncludeAdditionalRPAssets.m_IncludeReferencedInScenes` 必须开启，让构建时的 Shader 变体收集包含这些管线。
- `MoonToonCutout`、`MoonToonCutoutOutline` 复用 lilToon Pass。发布版优化器会向其附带的 `ReferenceUVs` 空名 Pass 注入 `CBUFFER_START(UnityPerMaterial)`，但该 Pass 的原管线 include 被注释掉了。两个 wrapper 提前通过 `HLSLINCLUDE` 加载 URP Core，补齐宏定义；无需改动 PackageCache 或关闭 lilToon 优化。

针对性验证：

- 实际调用 `BuildTargetExtensions.TryGetRenderPipelineAssets`：修复前仅返回 PC 管线，修复后同时返回 Moon、Apollo12、MC00Baked、MC00Interior 四个场景管线。
- 调用 lilToon 的发布版 `UnpackContainer` 优化路径，并同步编译两个 wrapper 的最终 UV Pass：均为 0 错误。移除新 include 的负例复现旧构建报告中的 `unrecognized identifier 'CBUFFER_START'`。
- 验证后恢复内存优化标记，`lilToonSetting.json` 内容哈希不变。

旧 Windows 构建虽报告 Succeeded，仍含上述两个 Shader 错误及一次 `lilToonSetting.json` 文件占用错误；文件占用本轮未复现。定点验证不等于完整 Player 构建或视觉验收，已有 exe 不会自动获得修复，需重新打包检查同一地图。

## 构建变体成本

场景管线纳入收集后，默认 `PC_RPAsset` 的 GPU Resident Drawer 开启，而四个地图管线均关闭。当前 URP 的构建预处理据此选择 `USE_LEGACY_LIGHTMAPS`；混用两种配置会保留该关键字的两组状态。将默认 PC 的 GPU Resident Drawer 也关闭，保持正式地图原有设置，去掉这条不一致的变体维度。不要为减少构建时间再次关闭场景管线收集，否则会重新丢失运行时所需变体。

这个调整影响默认 PC 管线的 GPU Resident Drawer 路径；正式 Theophilus、Moon 与 Apollo12 地图原本已关闭。使用默认管线的菜单、过渡阶段或旧场景的性能没有实测，不能据此承诺帧率不变。不同 Renderer 的 Decal 等真实功能差异仍保留。

2026-10-23 的一次实际构建中，URP Lit / ForwardLit 的片元阶段保留9216个变体，4845个命中本地缓存，4371个新编译，用时1714.74秒。此记录属于优化前构建；减少一个变体维度不等于整体构建时间减半，也不保证后续构建不再编译。保留 Shader 缓存，按后续构建日志中的实际 cache hits / compiled 数量判断。

该轮 Windows 构建在19:06完成，结果 Succeeded，总计46分钟。最新 BuildReport 中唯一 Error 消息是构建期间外部 `/api/exec` 请求的主线程超时，没有旧版的 Shader 宏编译错误。本轮没有启动独立客户端，Player 视觉差异仍待实际运行验收。配置统一后再次调用管线收集器，五套管线均返回 `GPUResidentDrawer=Disabled`；没有为计时再触发整包构建。
