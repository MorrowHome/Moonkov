# R23 正式地图材质与 UV 补丁

2026-10-15。处理用户反馈的明显纹理重复，直接修改 `TheophilusGameScene.unity`。保留 R21 四阶段提交 `7579bbd6`、`e527888f`、`9c621300`、`ab2a5fd0`，不重装几何，不接入 R22，不增加写入者。

## 实际应用

- 精确匹配并校验 19 件现有 R20/R21 源对象的顶点数、面数、loop 数与局部顶点 float32 XYZ 小端 SHA-256，全部通过。再按原 R21 导出器的 XZY、反绕序、三角化展开顺序，对照原二进制导出的坐标、法线、UV 和各 submesh 索引，全部逐字节匹配；Unity 原网格数组也与该布局一致。
- 更新 19 件对象的 76 个指定材质槽，其他材质槽保持原引用。新增 4 个独立 URP Lit 材质，未安装未用的 `R23_Concrete_Course_Variant`。
- 17 份原 Mesh 的独立副本仅更新 UV0 并重算切线；位置、法线、拓扑、Bounds 保持相同。159240 个源 Blender loop 更新映射到 271920 个 Unity 展开顶点。米制 UV 保留交付的岛相位与四分之一圈旋转，采样 tiling 为 `1 / image_repeat_metres`，offset 为零，不再除以 1.2。
- `R21_01_Excavated_Rock_Wall_And_Uneven_Rim` 与 `R21_06_Visible_Rock_Bottom_And_Rubble` 的 UV 清单为空，保留原 Mesh；只按交付清单更新材质槽，不计入 17 份 UV 更新。
- 新增 6 张本批贴图：4 张 Metallic/Smoothness、1 张混凝土混色 BaseColor、1 张独立混凝土法线副本。6 张原图片和原共享材质／网格不改写。

## URP 翻译

原 MetallicGloss 的 Alpha 以线性数据读取，忽略 R：`smoothness = (1 - roughness_max) + (roughness_max - roughness_min) * A`。新图 R 为指定 metallic 常量，A 为计算后的 smoothness；PNG 为 8 位无损，量化误差上限 0.5/255。导入关闭 sRGB、保持 Alpha、无压缩；材质 Metallic 工作流、Metallic Alpha 平滑度来源、Metallic／Smoothness 标量均为 1。保留公式中的偏移项。

三种金属使用指定恒定线性底色，经 Gamma 编码写入材质颜色，移除 BaseColor 图和 Normal 图，只保留低反差细微粗糙度。混凝土源 BaseColor 先解码 sRGB，再在线性空间混合 `0.12 * 原图 + 0.88 * 常量底色`，编码回 sRGB 作为新 BaseColor。混凝土法线以 NormalMap 导入、保持现有 OpenGL 的 Y 方向（不翻绿通道）、强度 0.12，材质采样为 2 m 尺度。未加入噪声、Shader 或包。

## 未应用目标

以下 19 件 R18/R19 精确名称在当前正式图中未安装，未猜名称映射、未新建替代对象、未导入旧几何；已安装的 19 件没有拓扑不匹配阻塞。

- R18_09_Pipe_Handling_Terrace
- R18_10_Horizontal_Tubular_Storage_Racks
- R18_11_Pipe_Rack_Gantry_And_Handling_Sled
- R18_13_Process_Equipment_Deck
- R18_14_Return_Separation_Vessel_Skids
- R18_15_High_Pressure_Pump_And_Compressor_Skids
- R18_16_Flanged_Process_Headers_And_Valve_Manifolds
- R18_19_Machinery_House_Foundation
- R18_20_Hoist_Power_House_Panels_And_Service_Vents
- R19_02_Sealed_Wellhead_Rotary_Table_And_BOP
- R19_04_Equipment_Transfer_Beams_And_Winch_Cradles
- R19_05_Twin_Large_Drawworks_Drums_And_Brakes
- R19_06_Winch_Reduction_Gearboxes_And_Drive_Motors
- R19_07_Paired_Feed_Rams_And_Axial_Guides
- R19_08_Travelling_Head_Topdrive_And_Clamp_Assembly
- R19_10_Tubular_Magazine_And_Handling_Rail
- R19_11_Machine_Service_Walkways_And_Rails
- R19_12_Wellhead_Process_Return_Pipework
- R19_13_Tubular_Feed_Catwalk_To_Central_Axis

## 保存与必要检查

正确正式场景、非 Play、非编译、干净场景下开始。URP Lit 可用且没有 Shader 错误；6 张贴图无缺失。保存重开后 19 个目标的材质与网格引用全部符合映射，场景干净。场景序列化差异严格限于 19 个 MeshRenderer 的材质引用和 17 个 MeshFilter 的网格引用：零对象／组件增删，其他字段不变。没有编辑 Transform、几何、桥通道、Collider、运行组件或地形引用；原网格、材质和四份 R21 TerrainData 文件哈希前后一致，holes 未重写。

辅助脚本最终编译诊断为空。Console 一次审阅仍有 R21 时已记录的 `SubScene.AddSceneEntities:379` / `OnEnable:359` 断言，未改包代码，也不声称整个 Console 无错误。不截图、不试走、不巡检性能、不重放 R21 物理测试；这次交付完成资产转换和正式引用接入，不代表最终画面验收。

资产阶段提交 `91d24723`；场景引用和本记录另做独立本地 commit，不 push。`Docs/AI/Theophilus_R23_Asset_Manifest.json` 记录固定输入哈希、19 个源校验、材质数值和烘焙规则；`Docs/AI/Theophilus_R23_Application.json` 记录保存与未应用目标。原正式场景备份、映射和辅助脚本保留于忽略目录 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R23/`，原输入不重复下载或改写。
