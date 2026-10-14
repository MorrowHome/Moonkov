# R4 西侧近景材质接入

2026-10-14，已按内嵌 `B_RING_R4_MATERIAL_HANDOFF.json` 更新 82 个既有显示对象的 UV0、材质槽及必要显示 Mesh。没有重装巨构、创建新根、移动对象或修改通行几何。目标场景保存重开后干净、不在 Play。

## 实际范围

- 场景：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。
- 专属新资产目录：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/R4NearMaterials/`，82 个显示 Mesh、9 个 URP Lit 材质、27 张 PNG。
- 只匹配合同中的 82 个原对象，包括 R3 西塔视觉、西侧桥头、邻近西北环端和承载结构。保留 GameObject、父链、Transform、原组件及 Collider，只修改 MeshFilter 和 Renderer 的显示引用。
- 更新为场景实例覆盖；原 R2/R3 Prefab 资产、旧 Mesh/材质保留，未替换 `R3_PATH_*`、52 个楼梯路径盒和一个原西侧接入桥地板盒。两组根继续位于 `(2015,0,2130)`。
- 八楼、配色、原 A 路线、DEM、正式地图、角色系统及其他显示对象不在此次更新范围。没有修改灯光、曝光、全项目渲染或模型配置，没有运行时代码或新工具安装。

新材质包含粗糙涂层、外露金属、防滑甲板、灰白包覆、克制工程色、矿物支承及标牌等近景纹理。板缝、微表面和粉尘按作者纹理呈现，不是新增实体分缝、雨水锈迹或全场磨损处理。

## 导入约定

| 数据 | 实际设置 |
| --- | --- |
| BaseColor | Base Map，sRGB 开，白色 Tint |
| Normal_OpenGL | TextureImporter NormalMap，OpenGL +Y，Flip Green 关闭，非灰度生成，Strength 1 |
| MetallicGloss | 线性，R 金属度、A 光滑度；未按 ORM 转换、未反转 Alpha、未用作粗糙度 |
| URP 工作流 | 标准 URP Lit，Metallic；Smoothness Source=Metallic Alpha，乘数 1 |
| 纹理采样 | Tiling 1、Offset 0；W-01 牌三张图 Clamp，其余 Repeat |
| UV/切线 | 作者单 UVMap 保留为 UV0，未翻 V；顶点/法线按一次 XZY 坐标映射，Unity 按保留 UV0 重算显示切线 |
| 其他 | 未绑定伪 AO，未加 emission、displacement 或自定义 Shader |

27 张 PNG 均从 Blend packed images 提取，逐文件与该合同中的 SHA-256 对应，未下载其他包、生成新图或改变纹理内容。按源尺寸导入，Standalone 底色/金属图用 BC7，法线用 BC5，保留 Mipmap。

## 必要保存核对

仅确认正确项目与干净/未占用场景、必要贴图输入、材料引用及保存结果。临时接入脚本编译无诊断错误；重开后 82 个目标引用专属显示 Mesh，各材质的三张图均存在。路径/桥 Collider 仍为 53 个，场景 `dirty=false`、`playing=false`。

没有截图、完整试走、测试矩阵、保护扫描、性能测量、提交或推送。实际画面、法线朝向的主观效果和概念图写实程度未做实机验收；导入成功不能等同于已经达到参考图完成度。

## 回退与来源

回退文件在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R4/`：`BeforeMaterialR4.unity` 保存更新前场景，`BeforeMaterialR4_References.json` 保存 82 个对象的旧 Mesh/材质引用。旧显示资产均保留；恢复前先保留后续编辑，不直接覆盖新工作。

源 `Moonkov_Theophilus_B_Dense_Ring_Concept_Increment_R4.blend` 为 25,901,725 字节，读取时一次校验 SHA-256：`8936a7cdd59b6bc5996f9dbdc04f4db86bbc5cd05b773caf819a0e020d28a4d6`。R1/R2/R3 保留，源 Blend 未保存修改，也未改变共享权限。

原始合同、导出清单和 Unity 接入记录在该持久交接目录；汇总为 `Docs/AI/Theophilus_R4_Near_Material_Import.json`。
