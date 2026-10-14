# R9/R10 研究场景承托与下降塔显示增量

2026-10-15，固定 R9/R10 文件已接入 `Theophilus_Shoulder_Return_Study.unity`。新增 37 件显示网格：R9 支承 13 件、R10 下降塔 24 件；仅关闭清单中的 23 个旧 MeshRenderer。旧对象、Transform、组件、碰撞和原显示源资源保留，没有重装巨构根。

两组专属根位于 Unity `(2015,0,2130)`，几何采用一次 Blender XZY 映射，绕序只换一次，UV V 不翻转；37 件显示对象各自保留，复用 7 个已有 R8/R6 材质，无新材质、贴图、灯光、碰撞、运行代码或电梯玩法。

原 7 个层站顶面、7 个独立厢体、2 个独立卷筒及地面领圈/密封井盖保留。新增 7 个固定接收框各自独立，没有挂到轿厢上，没有创建控制 ID、运动轴或可进入井下路线。原桥面、R3 路径、DEM 高度/孔洞、R7 原生接地偏移与 R8 Terrain/渲染资产不改。

新增专属资源目录为 `Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/R9R10TowerDetails/`。源文件位于 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R9R10/Moonkov_Theophilus_B_Dense_Ring_Concept_Increment_R9R10.blend`，82,588,826 字节，沿用交接核对的 SHA-256 `de927fea6d1cd71742c2a5eb2f88842c7b5cdc0d1c0f48a058012b6d381269a7`；读取内嵌合并清单及两份子清单，没有重下载或保存源 Blend。

回退为同一持久目录中的 `BeforeTowerR9R10.unity`、`BeforeTowerR9R10_Renderers.tsv`。临时脚本编译无诊断，保存重开后 37 件材质/网格绑定、23 个 Renderer 禁用、指定独立构件及 R7 桌根/原生 Terrain 引用成立，`dirty=false`、`playing=false`。未截图、试走、整场保护扫描、性能或视觉验收。

本批最初只写研究场景。随后用户明确要求直接使用正式地图，37 件显示增量与 23 个 Renderer 禁用已同步到 `TheophilusGameScene`，保留原正式出生/搜打撤入口。后续增量只修改正式场景，不再维护研究副本。

本地用户在本批执行期间再次要求“小步 commit”，因此该研究增量单独提交；没有推送，没有夹带正式入口或其他并行改动。
