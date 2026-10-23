# Windows 玩家包渲染检查（2026-10-23）

## 问题与当前结论

报告症状：正式地图除中央大楼外的建筑只剩阴影，怪物尸体不可见。
目前没有新包画面对照，原始症状的最终根因仍未确认。先前关闭 URP 的
`Strip Unused Variants` 导致粒子 Lit 单个 ForwardLit 阶段出现 442,368 个片元变体，
该措施范围过大，现已撤销。必须保持 URP 裁剪开启，并包含场景引用的管线。

## 本次修复

- `ProjectSettings/GraphicsSettings.asset`：Lightmap Modes 使用 Custom，
  保留 Baked Non-Directional、Baked Directional 和 Shadowmask；关闭未使用的实时 GI
  与 Subtractive 组合。`BuildingBakedLighting.Apply`
  在运行时设置 `LightmapSettings.lightmapsMode/lightmaps` 与 Renderer 的 lightmapIndex；
  构建时仅分析场景不能可靠覆盖这一运行时状态。
- 同文件：Instancing Variants 恢复 Strip Unused。`Resources/Moonkov/RuntimeLitInstanced`
  与 `RuntimeLitInstancedEmission` 两份材质专门保留 `RaidSupplyCacheVisuals.Finish`
  在运行时创建的 Lit 普通和发光实例化组合，不再对所有 Shader 使用 Keep All。
- `Assets/Resources/Moonkov/AlienRemains.mat`：提供不启用局部关键词的 opaque Lit
  材质资源；`AlienLootVisual` 克隆该资源替代 `Shader.Find` 临时构造，并明确报告资源异常。
  这保留了尸体的材质组合；不代表旧包中 Lit Shader 本身没有打包。
- `Assets/Editor/MoonkovPlayerBuildChecks.cs`：Windows 玩家包构建前自动检查上述设置、
  场景引用管线开关、尸体材质、主菜单与正式地图，以及构建场景材质的 Shader 错误。
  也可执行 `Tools > Moonkov > Check Player Build Rendering`。检查失败会阻止构建。

## 已核实与排除

- 当前目标 StandaloneWindows64 / Player；保存的 Windows Profile 的 subtarget=2
  在本项目 Unity 6000.5.10f1 中确实是 Player，不是 Dedicated Server。
- Unity 构建 API 返回 5 个管线资产：PC、Moon、Apollo12Laptop、MC00Baked、MC00Interior。
  场景引用的管线已经自动纳入，不存在“仅包含菜单管线”的问题。
- 旧包 `Build/Moonkov20261023_1/FPS_Template.exe` 的 BuildReport 包含 Lit、Unlit、
  HaloLine、RifleHaloLine、TacticalVolume。Shader 入包不等于所有运行时变体都保留。
- 当前正式地图依赖包含 337 个材质，221 个使用 URP/Lit。
- 当前 Lit、Unlit、Industrial Dust 和两个 MoonCutout Shader 均无缓存编译错误。
  更早构建中的 CBUFFER_START 错误已有源代码修复，不作为本次新发现。
- 最新旧包 BuildReport 的 1 个 error 是 `/api/exec` 主线程超时，非 Shader 编译失败。
- `No RuntimePipelineConfig ... Pipeline will be disabled` 来自 `com.unity.pipeline`
  的代码热重载功能，不是 URP 被禁用。

## 验证边界

Unity 编译通过；构建前检查通过；尸体材质克隆具有可用的 ForwardLit pass、无局部关键词，
Shader 在当前 Editor 支持。检查器现要求 URP 裁剪开启、Instancing 使用 Strip Unused，
并验证两份实例化保留材质。先前要求全局保留的规则已移除。
通过 URP 原生 ShaderBuildPreprocessor 重新生成管线的预过滤配置。
没有另开 Editor、游戏客户端或做联机验收。

尚未重新生成 Windows 包或确认建筑/尸体的新包画面。下一次构建后须检查同一地图和尸体；
若仍有问题，应记录新包 Player.log 与消失物体所用材质/Shader，进一步定位，不能继续以
“Build Succeeded”代替视觉验收。尚未测量修正后新包的编译耗时和变体数量。
