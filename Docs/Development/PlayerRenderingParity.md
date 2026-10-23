# Editor Play 与 Player 渲染一致性

2026-10-23 修复打包路径中的两处缺陷，未调整模型、材质风格或场景光照。

- `MoonSceneLighting` 在 Play 时切换到场景引用的管线。正式 Theophilus 地图使用 `MC00BakedPipeline`，其传统 Light Probe 配置与 Graphics/Quality 中的 PC 管线不同。URP Global Settings 的 `IncludeAdditionalRPAssets.m_IncludeReferencedInScenes` 必须开启，让构建时的 Shader 变体收集包含这些管线。
- `MoonToonCutout`、`MoonToonCutoutOutline` 复用 lilToon Pass。发布版优化器会向其附带的 `ReferenceUVs` 空名 Pass 注入 `CBUFFER_START(UnityPerMaterial)`，但该 Pass 的原管线 include 被注释掉了。两个 wrapper 提前通过 `HLSLINCLUDE` 加载 URP Core，补齐宏定义；无需改动 PackageCache 或关闭 lilToon 优化。

针对性验证：

- 实际调用 `BuildTargetExtensions.TryGetRenderPipelineAssets`：修复前仅返回 PC 管线，修复后同时返回 Moon、Apollo12、MC00Baked、MC00Interior 四个场景管线。
- 调用 lilToon 的发布版 `UnpackContainer` 优化路径，并同步编译两个 wrapper 的最终 UV Pass：均为 0 错误。移除新 include 的负例复现旧构建报告中的 `unrecognized identifier 'CBUFFER_START'`。
- 验证后恢复内存优化标记，`lilToonSetting.json` 内容哈希不变。

旧 Windows 构建虽报告 Succeeded，仍含上述两个 Shader 错误及一次 `lilToonSetting.json` 文件占用错误；文件占用本轮未复现。定点验证不等于完整 Player 构建或视觉验收，已有 exe 不会自动获得修复，需重新打包检查同一地图。
