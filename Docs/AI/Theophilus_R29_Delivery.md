# R29 正式地图显示接入

2026-10-15，直接接入当前 `TheophilusGameScene.unity`。以整栋 MC00 烘焙提交 `5b44f34f` 及用户已有未提交场景内容为基线，没有打开旧地图或回写旧快照。

## 实际接入

- **34 件新增显示网格**，位于 `R29_FROM_R24_FINAL_DISPLAY_ROOT`：R25 导向／夹持 7 件、R26 装甲 8 件、R27 局部地表／覆板 6 件、R29 双梁／承座及原结构保留副本 13 件。求值后共 176165 个三角形，沿既有 XZY 转轴和一次绕序反转，根位置 `(2015,0,2130)`，不额外旋转或位移。
- **19 件既有显示对象的材质／UV 补丁**，覆盖源 4639 面；为其创建独立网格副本，保留原顶点、法线、UV0、光图 UV1、Transform 和 Renderer 光图绑定。所有原网格资产保留，MeshCollider 继续引用原网格。
- **10 个专用材质、43 张 PNG、2 个本批专用 Shader**：7 个工业材质采用精确图集／米制细节通道，3 个局部地表材质按交接组合世界颜色／高度／宏观变化及局部纹理。sRGB、线性数据、OpenGL 法线、粗糙度／光滑度语义、Wrap 与 Mipmap 显式配置。工业纹理保留源分辨率，局部地表按交接设为 2048；16 位高度保留 R16 精度。
- 唯一额外关闭的旧 Renderer 为 `R21_04_Rim_Foundations_And_Machine_Suspension_Grillage`。其 GameObject、Transform、碰撞和功能保留；旧 R24 已关闭的 R20_02 不恢复。没有按前缀批量隐藏。

新根只有显示组件，没有 Collider、Light、Camera 或运行时代码。不导入 R22、全局 Terrain、DEM、孔形、路线或后处理。R28 可选作业遮罩未安装、未开启。

## 严格来源与 UV

原三包保持不动，另用 37897 B 修复包，SHA-256 `fe77db609b6f1d7cb46172a02edb32382d816af62911dcafc918a84f729d7040`。先调用 `preflight_r29_source_baseline`，真实本地 R23 JSON 指纹、19 件对象、16 个修正 UV guard 与 9 件精确 R13 别名的权威内容指纹均通过，零应用前后状态一致；随后才在离线副本调用 `apply_r29_with_r23_serialized_uv_guard`。未继续旧顶层入口，不通用去后缀、不重命名共享素材、不保存源 blend。

UV0 保留原工艺米制数据，UV1 保留现有光图；R26、R27、R28 图集分别使用 UV2／UV3／UV4，R28 细节使用 UV5，两个源地表辅助通道使用 UV6／UV7。切线按各材质实际法线采样通道生成。R24_01、R24_02 加入新 UV 层后，Blender 的倒角求值会重插部分 UV0；已核对其 Unity 原 UV0 与原 R24 导出字节完全相同，导入副本保留这些本地原值，只添加新通道。没有放宽原始源 UV guard 或改写现有 UV0。

## 已保留

保存前确认全部 **15322 个原 Collider 对象、启用状态和 MeshCollider 网格引用**不变；Terrain／TerrainCollider 数据及 Terrain 主材质引用不变。原桥、外圈、R7 与现有游戏／交互对象没有修改。

MC00 **118 灯、135 按钮**及 274 个现有 MonoBehaviour 序列化数据保持；光图、探针、回路 owner、Mixed／Baked Indirect、静态反射、关键灯和角色受影修复沿用优化结果，没有恢复全实时照明。当前管线、天空及后处理不改。

用户已有的场景改动和 `R24_Slate_Gray_Industrial_Enamel.mat` 修改保留。提交场景时按 YAML 对象分离：只提交 138 个新文档及 40 个原文档（19 MeshFilter、20 Renderer、1 SceneRoots）；用户原有 9 个修改文档、3 个新增文档继续留在工作区，不混入本批提交。

## 必要检查与边界

- Unity 中 19 件原顶点／法线／UV0 与来源精确匹配，材质顺序和光图通道检查通过；53 个显示网格及材质引用完整。
- 两个 Shader 的 Forward、ShadowCaster、DepthOnly pass 完成同步编译，无 Shader 错误。工业 Shader 的 SRP Batcher 检查返回兼容；地表检查返回 `Not initialized`，未为初始化诊断而增加渲染测试，不宣称其运行批处理或性能已验收。
- 正式场景已保存、无缺失引用且不脏。没有 Play／Host、截图、性能测试、安装、账号／接口／部署／SubScene 排查，也没有 R30 或后处理改动。Shader 编译不等于与云端图像等价；局部地表与本地 Terrain 的视觉接缝仍未作图像验收。

完整 34／19 清单、实际资产路径与保留检查见 `Docs/AI/Theophilus_R29_Validation.json`，资产检查见 `Docs/AI/Theophilus_R29_AssetValidation.json`，提交范围见 `Docs/AI/Theophilus_R29_CommitScope.json`。源契约、修复和导出记录在 `SourceData/Models/TheophilusR29/`；修改前完整地图保留于 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R29/Prepared/BeforeR29_Formal.unity`。

资产阶段提交 `458860a5`；场景与本记录另作独立本地提交，未 push。回退时移除本批新显示、恢复 19 件原网格／材质绑定与旧 R21_04 Renderer；不回滚 MC00 优化或用户其他工作。
