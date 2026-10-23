# Windows 玩家包渲染检查（2026-10-23）

## 静止太阳：正式地图漏接时钟表现组件

最新玩家包的阴影不动，源场景检查确认 `Theophilus Scene Lighting` 只有
`MoonSceneLighting`，缺少唯一调用其 `ApplyTimeOfDay` 的 `ExpeditionClockPresentation`。
服务器时间设置仍为启用、24 分钟对应 24 游戏小时；太阳引用有效且为 Realtime。
JSON 导出的场景对象 `instanceID: 0` 不能用于判定引用丢失，已用 SerializedObject
与实际 Light 引用核实。

已在正式地图补上时钟组件，并在 Windows 玩家包的场景处理阶段检查该连接和实时太阳。
Unity 编译通过；隔离预览场景检查通过；在预览里临时禁用时钟时，检查器按预期拒绝，
随后恢复并关闭预览，没有保存临时状态。没有运行 Play、客户端或重新构建。
保持 MeanLunarOrbit 和 astronomicalTimeScale=1；这仍是缓慢的月球太阳周期，
不会擅自换成快速地球昼夜。玩家包中的实际转动和外圈建筑可见性尚未验收。

## 构建耗时优化：未使用的 BRG 变体

2026-10-23 检查正在进行的构建，`Logs/Editor.log` 中已完成的一次
URP/Lit ForwardLit 片元编译为 1918.95 秒，缓存命中 929、新编译 5215 个变体；
后续另一组同名 Pass 裁剪后仍有 13824 个变体，尚未完成。这些记录不能当作
一次完整构建的前后对照，也不能据此认定每次构建的缓存都失效。

确认存在一项冗余：所有 20 份序列化 URP 资产的 `m_GPUResidentDrawerMode` 都为 0，
manifest/lock 中没有 Entities Graphics，项目运行时代码也没有自定义 BRG 创建路径，
但 Graphics 的 `m_BrgStripping: 2` 仍强制 Keep All。worker 0 的编译日志中已有
243 条启用 `DOTS_INSTANCING_ON` 的编译请求，说明这类变体确实参与了编译。

将 `m_BrgStripping` 改为 0（Strip if Entities Graphics Package is not installed）。
这是 Unity 原生的条件裁剪，不修改 Shader 源码，也不改普通 GPU Instancing、
运行时 Lit 保留材质、Lightmap Modes、画质或场景列表。
若将来启用 GPU Resident Drawer 或自定义 BRG，必须恢复 Keep All；现有
`MoonBaseApollo12Tools` 启用 GPU Resident Drawer 时已同时设置 Keep All。
安装 Entities Graphics 后，条件模式会自动保留 BRG 变体。

本次只做静态配置与依赖核对，未打断当前构建、调用繁忙 Editor 或另开实例。
修改供下一次构建使用；当前构建已载入的配置不会因此获得提速。
当前构建结束后，重新打开项目以确保 Graphics Settings 从磁盘重新载入，保留 Library。
下一次正常构建对照日志中的 Lit 变体数、缓存命中数和完整耗时，并检查正式地图
建筑与尸体显示。尚未实测节省时间，不保证该单项调整解决全部一小时耗时。

参考：[Unity 6000.5 BRG 裁剪模式](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityeditor/rendering/batchrenderergroupstrippingmode)。

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
