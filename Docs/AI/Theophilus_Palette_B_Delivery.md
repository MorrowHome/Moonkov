# Theophilus 专属 B 配色

2026-10-14，已在 Theophilus 独立场景实施灰白外壳、深灰外部钢构和少量工程橙，并对月面材质及现有太阳/场景填光作小范围调整。场景已保存重开，保持干净、可编辑、不在 Play。没有截图，也不宣称实机画面已经达到概念图观感。

## 实际修改

- 建筑新增十个专属材质副本：840 个已识别外壳/屋顶材质槽改灰白，222 个外部支承/屋顶钢构槽改深灰，两处安全角带槽改工程橙。重开后共 1064 个槽引用专属副本。
- 外壳颜色为 `(0.78, 0.79, 0.77)`，钢构为 `(0.19, 0.205, 0.215)`，工程橙为 `(0.69, 0.285, 0.075)`。原透明度、纹理、法线、UV、金属度、光滑度及其他 PBR 参数保留，不把所有材质统一涂灰。
- 来源包括 `LD2_Hull_CeramicPaint`、`LD2_Painted_Metal_Grey`、`LD2_Raw_StainlessSteel`、`LD2_Brushed_Aluminium`、原支承及防滑材质。只替换已识别外部 Renderer 的对应槽；排除室内房间、数据架、楼层组织、按钮、标识、发光指示及玻璃类材质。原内部材质、灯控状态与 UI 标识没有重新配色。
- 月面新增一个 `LunarRegolith_Theophilus_B.mat`，只将已识别 `_DustTint`、`_BasaltTint` 向各自灰度混合 85%，并将现有 `_IndirectStrength` 调为 0.03。原基础灰度、纹理、法线、地形、粗糙度及其他光照模型参数保留。两块 Terrain 和场景 `MoonEnvironment.regolith` 同时引用该专属副本，保证运行时仍按现有环境组件创建材质实例。
- 现有太阳由 Soft 改为 Hard 阴影，原强度 1.6、角度 `(25°,120°,0°)` 保留；现有 `MoonSceneLighting.ambientFill` 由黑调为 `(0.015,0.016,0.018)`，仅少量填光。未新增灯，未改相机曝光，也未改全项目后处理、URP、Quality 或 RenderPipeline 资产。

全部新材质在 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/VisualPaletteB/Materials/`，只有独立场景材质槽和现有照明组件参数发生场景覆盖。没有更改源材质、原 Prefab 或正式地图。原 118 灯控制链的映射未编辑，135 个按钮和单个气闸保留。

## 必要保存核对

只做一次保存重开后的必要引用读取：1064 个建筑槽持久引用新副本，两块 Terrain 材质与环境组件引用一致，太阳为 Hard，原曝光开关仍为 true，场景 `dirty=false`、`playing=false`。编译此次临时实施脚本没有诊断错误。没有在配色后重启 Play、逐项测试或读取重复日志。

只对本次涉及的六个源材质所在文件（含嵌入材质的源 FBX）保存修改前后哈希，六个均一致；没有对整项目或此前数百个保护文件重新哈希。没有提交、推送、安装或下载。

配色前场景备份为 `LocalData/AI/AssetHandoffs/TheophilusShoulder/BeforePaletteB.unity`。它包含八栋外楼；如需回退，须先保留后续编辑，再用备份恢复本场景或撤销专属材质覆盖，不能直接覆盖新工作。

## 当前交付边界

八栋入口和首组三栋两段室外连接的实际角色试走记录见 `Docs/AI/Theophilus_Outer_Buildings_Actual_Player_Walk.md`，该结果发生在此次配色之前。没有因配色重复通行。

画面颜色、日照和阴影可读性仍待用户实际查看；当前结论仅是具体材质/参数已实施并持久保存。PH01/RB00 仍未落位。后续云端已交付的 B 加密断环及工业区灰盒已按指定集合追加为独立展示根，见 `Docs/AI/Theophilus_Dense_Ring_Concept_B_Delivery.md`；它没有碰撞，入口尚未接地，不提升为可游玩或视觉验收结论。性能、声音、网络及手动交互也不扩大验收。

原始配置、槽位修改、源材质保护哈希及保存读回记录在持久交接目录 `LocalData/AI/AssetHandoffs/TheophilusShoulder/PaletteB_*.json`，汇总见 `Docs/AI/Theophilus_Palette_B_Validation.json`。
