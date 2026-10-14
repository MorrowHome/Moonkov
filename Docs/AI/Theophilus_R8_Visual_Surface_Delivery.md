# R8 研究场景表面与渲染接入

2026-10-15，固定 R8 已接入并保存重开。目标为 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`；未下载同 ID 的后续版本。

本地源 Blend 为 82,515,104 字节，沿用交接核对的 SHA-256 `95df476a89f1bd8afb8d73106b1c7702b631afa55d134957632f98e01115a426`。读取内嵌 `R8_MINIMUM_IMPORT_ZH.txt` 与 `B_RING_R8_VISUAL_HANDOFF.json`，未保存源 Blend。

## 实际显示范围

1398 件清单对象更新 UV0、材质槽和面材质分配：B 前缀 1287、R2 25、R3 57、R6 10、R7 19。各显示 Mesh 从现有 Unity Mesh 复制，接入前校对来源坐标下的顶点对应及三角面索引成员；只替换 UV0/子网格分配并重算切线，保留原顶点、法线和其他 Mesh 数据。现有 GameObject、Transform、父链、组件与碰撞没有替换。未在清单中的字牌等对象保留旧显示。

R6 输入烘焙至其原模块局部空间；R7 输入烘焙至原站根或桌根的局部空间。更新只写 MeshFilter、Renderer，不套用云端预览的 R7 世界高度；一次原生接地后的桌根 +0.008037567 m、各脚/检修架/位置牌偏移均保留。

专属目录为 `DenseRingConceptB/R8VisualSurfaces/`：1398 个显示 Mesh 副本、7 个建筑材质、23 张内嵌贴图；6 个建筑材质使用 R8 底色/法线/MetallicGloss，另一个扫描支承材质复制 R5 材质并引用已有 R5 三图。清单中的旧 R4 接触尘、位置牌、R5 混凝土及 R6 玻璃材质继续复用，不修改原资产。

建筑材质为标准 URP Lit：底色 sRGB、白 Tint；NormalMap OpenGL +Y、Strength 1、不翻绿；MetallicGloss 线性、R 金属/A 光滑度、Metallic Alpha、Smoothness 1；UV0 不翻 V，Tiling 1/Offset 0。大橙面按清单改为灰白板面，小标识使用克制赭色，钢涂层和裸金属使用各自表面。

## 原生 Terrain 显示

| 项目 | 实际接入 |
| --- | --- |
| 全球控制图范围 | 世界 XZ `(0,0)–(4096,4096)`；PNG 上沿 Z4096、下沿 Z0 |
| 远景 Terrain | 世界 XZ `(0,0)–(4096,4096)`，完整采样控制图 |
| 近景 Terrain | 世界 XZ `(1536,1920)–(2048,2432)`，裁取同图对应范围 |
| 权重 | 原 512×512 alphamap 分辨率；按世界坐标双线性重采样 RG，归一化和为 1 |
| 图层 | 每块 Terrain 为 LowTone/HighTone 两层；各自两份专属 TerrainLayer，共四个资产以补偿不同原点 |
| 细节采样 | TileSize `(3.7,3.7)` m；近景 TileOffset `(1536,1920)`，远景 `(0,0)`，保持世界 XZ 相位 |
| 图输入 | 两张 sRGB 底色；共享 NormalMap；线性 TerrainMask：R=0、G=1、B=.5 不参与高度、A=光滑度 |
| 混合 | Height Blend 关闭；DiffuseRemapMax.w=1，Opacity as Density 关闭 |
| 远景 | basemapDistance 75 m，标记新 basemap 更新；细节图 Mipmap 和 8× anisotropic |

两块 TerrainData 通过原生复制建立专属显示副本，只设置图层、alphamap；没有调用高度或 holes 写入。heightmap 仍 2049、holes 仍 2048，原 TerrainCollider 数据引用保留。各有专属 URP Terrain Lit 材质。云端的两件地形参照 Mesh 完全未导入，没有替代原生 Terrain、TerrainCollider 或改变 DEM。

TerrainLayer 的贴图、图层采样与偏移采用现有原生能力；[Unity Terrain Layers 文档](https://docs.unity.com/en-us/engine/6000.7/manual/creating-environments/script-terrain/terrain-textures/class-terrain-layer)说明其图层与采样配置。现有 URP 包的 TerrainLitPasses 使用图层 ST 及 mask，Density 关闭参数采用该源码约定。

按交付清单声明，地表扫描为实验室月壤模拟物及作者宏观底色权重，不是新增月球实地摄影或测量地质。控制图不作为底色、高度或烘焙日照/AO。

## 研究场景渲染副本

- 专属深空材质复制现有 SpaceSky，关闭恒星、太阳盘和地球底色亮度，缩小地球盘；保留现有 Shader，未新增 Shader 文件。
- 场景环境补光设为中性 RGB `.015`、Flat，关闭雾；现有 MoonSceneLighting 实例使用该补光与专属天空，关闭自适应曝光。其他实例和代码不改。
- 专属 VolumeProfile 使用 Neutral Tonemapping、Post Exposure 0、Contrast/Saturation 0，挂在本场景的全局 Volume。没有修改项目全局 Profile 或管线资产。
- 原项目已经为 Linear；太阳强度 1.6 和方向 `(25,120,0)` 保留，没有把 Blender Sun 3.5、AgX Exposure .25 直接照搬到 Unity。

未照搬云端 Cycles 的 18–75 m 自定义细节渐退曲线，使用原生 Terrain basemap/Mipmap 能力；AgX/GI 与 Unity 不宣称等价。相机实际后处理观感、天空细小残余地球边缘光、法线观感、概念图逼真程度及性能均未做实机验收。

## 保存、回退与验证边界

`BeforeVisualR8.unity` 保存完整更新前研究场景；`BeforeVisualR8_DisplayReferences.tsv` 和 `BeforeVisualR8_TerrainReferences.tsv` 保存旧显示引用。旧资源保留，恢复前先保留后续编辑。

临时接入脚本编译无诊断，执行成功。保存重开后 1398 件专属 Mesh 引用和材质存在、两块原生 Terrain 为双层配置，场景 `dirty=false`、`playing=false`。只进行本次输入、范围、引用、明显几何对应及必要保存确认，没有截图、试走、整场保护扫描、性能验证或运行玩法验收。

项目共享素材、正式地图、原碰撞、DEM、R7 接地与并行改动保留。未 Git 提交或推送，没有新运行时代码、工具安装或模型配置变更。实际接入无剩余阻塞；上述渲染差异/观感为未验收部分。

正式地图接入时，用户再次要求小步 commit，本批按依赖单独纳入本地提交。上文未提交指最初 R8 接入阶段。
