Moonkov：本地已安装 R21 + R23 + R24 → 最终 R29 显示增量

这是一套合并后的源资产交接，不需要按 R25、R26、R27、R28、R29 逐版安装。
以用户本地已有 R24 压缩材质槽版本为基线。原场景和既有回退均保留。
本包尚未上传、未交 DOT00、未在 Unity 编译/运行；不包含自动接入正式场景的 Unity 工具。

一、只传这三个 ZIP
1. geometry/Moonkov_R29_From_R24_Final_Geometry_And_Handoff.zip
2. industrial_patch/Moonkov_R29_Industrial_Final_For_R24.zip
3. local_ground/R29_Local_Ground_External_Materials_And_Textures.zip
三个 ZIP 均解压到同一个新目录。结果应包含本 README、geometry/、industrial_patch/、local_ground/。
所有贴图由两套材质 ZIP 各保存一份真实 PNG；不要另传云端 packed 旧库或完整 280 MB 场景。
文件校验及准确大小见 R29_Delivery_Files.json（单独小清单），依赖见 R29_R24_To_Final_Delivery_Index.json。

二、准确变化范围
34 个新增显示网格：
- R25 井下延伸导向/夹持模块 7 个
- R26 分层装甲模块 8 个
- R27 最终局部施工坡/断面/切岩/土块/覆板 6 个
- R29 I 形双梁 6 个、分层承座 6 个、原 R21_04 其余结构完整显示副本 1 个
基础网格合计 27,660 顶点、24,484 面；保留可编辑倒角等修改器，不做有损简化。
另有 19 个本地既有显示对象、4,639 面只更换最终材质和必要 UV。其名单及逐面 old/new 材质名都在工业包中。

唯一新增的旧显示隐藏：
R21_04_Rim_Foundations_And_Machine_Suspension_Grillage
只关闭 Renderer，不删除物体、Mesh、Collider、脚本或交互，不关闭整个功能 GameObject。
R29_00_R21_04_Display_Remainder 保留它的全部其它组件；两根白盒梁和一个暗柱由新结构替代。
原 13×13 m 混凝土基础仍保留。不能只放新梁而继续显示原梁，也不能把整个原物件和 Collider 一起删掉。
旧 R24 已隐藏的 R20_02 状态沿用，不恢复。不要安装 R22 或旧 R26S 地表再绕一遍隐藏。

三、建议接入顺序
先备份本地场景及相关显示资产，核对 R21 + R23 + R24 基线。遇到 hash、拓扑、逐面材质名不符应停止核对。
1. 载入工业最终 7 材质及 21 张唯一 PNG；对现有 19 对象执行精确材质/UV 改动。
2. 载入局部月壤 3 材质及其外部 PNG，保持 local_ground/Textures 相对目录。
3. 载入 geometry/ 的 34 网格。轻量几何库中的 H29_SLOT_* 是占位符，必须按 R29_Final_Geometry_Manifest.json 解析到规范材质名。
4. 保持原世界坐标，完成显示替换后只关闭上述原 R21_04 Renderer。
5. R28 局部作业遮罩位于 local_ground/Optional_R28/，默认关闭。如以后采用，只叠加到既有月面材质；mask 外输入完全保留。
Blender 源场景参考入口 APPLY_IN_BLENDER_SOURCE_ONLY.py 需显式调用；它不保存、不渲染，不是 Unity 导入器。
几何包已空场景重开，34 网格、原始 UV/拓扑 hash 与清单一致，18 个规范材质名均可从本地基线及两材质包解析。
工业包已在复制的 R21+R23 源对象与 R24 压缩槽上验证一次应用及重复跳过。此检查不等于本地项目接入通过。

四、UV、材质与坐标不能盲转
Blender 单位米，Z 向上；Unity 世界 XYZ = Blender XZY。沿用此前已验证的导入转换，仅转换一次。
每个新网格的全部源 UV 层顺序、名称、逐 loop hash 均在几何清单；工业补丁提供新增 UV loop 精确数据。
Blender loop 是面角，不是 Unity 顶点索引；顶点拆分后不可直接用同一个索引贴数组。
UVMap 保留原米制/工艺方向。R26_Finish_UV、R27_Industrial_Finish_UV、R28_Large_Surface_UV 是各代最终仍使用的图集层；R28_Metric_Detail_UV 是米制细节层。
应按材质清单把这些逻辑层显式映射到导出 UV 通道，保留既有 lightmap 通道；不要认定 Unity UV1 空闲。不同对象层顺序不同，不能全局盲指定同一个数字。
BaseColor RGB 使用 sRGB；MetallicGloss 为线性 R=金属度、G=粗糙度、B=尘覆盖、A=1-G；最终 smoothness 的细节公式见工业材质逐条记录。
Normal 是 OpenGL +Y 切线法线；按实际采样 UV 产生切线并沿用本地法线约定。R26 微细节、R28 双 UV 与月壤世界坐标组合需要兼容的 shader，不能把 Blender 节点直接当 URP Lit 自动导入。
局部月壤具独立 shader 重建契约。Unity 通道变体已经独立列明，没有声称节点图或 HLSL 辅助函数可以未经适配直接使用。

五、明确排除
绝不默认替换以下两份 Terrain 的本地材质或贴图：
R21_DISPLAY_TERRAIN_CUT_Theophilus_Background4km_HOLE_MATCH_512
R21_DISPLAY_TERRAIN_CUT_Theophilus_Shoulder512m_SERIALIZED_COMP
云端 R27/R28 全域地表 shader 仅为预览修复，不在本交付材质库中。
不含 DEM、孔形/井深变化、路线、碰撞、7 站/7 舱/2 鼓轮功能、灯光、相机、R22、完整历史场景。
预览专用井内补光不交付，也不代表实机光照效果。

六、剩余风险及回退
本包准确覆盖模型和贴图源数据，但 URP 多 UV/世界坐标材质、切线、导出坐标/法线、现有 lightmap 保留仍需本地既有流程适配。
较大坡面与本地主月壤纹理之间的色差需本地看图确认，不能用云端全域 shader 覆盖来掩盖。
只承诺云端源包可重开和精确依赖，不承诺 Unity 画面、性能、碰撞或正式场景验收。
保留本地接入前备份和原 Renderer/Collider 数据。云端 R24/R25/R26/R27/R28 全部完整回退、最终 R29 editable blend 均未改写。
