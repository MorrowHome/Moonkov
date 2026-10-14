# R6 西侧 TRANSFER 06 外观增量

2026-10-14，已将 R6 清单中的 13 件新增显示网格接入独立 Theophilus 场景。更新限既有 28×15×14 m 建筑外观，不提供进入室内或开门玩法。

场景：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。新资产目录：`DenseRingConceptB/R6Transfer06/`，13 个显示 Mesh、2 个常量 URP Lit 材质，无新增贴图。

新增显示对象：

- `R6_T06_THICK_WALL_AND_ROOF_BACKING`
- `R6_T06_LAYERED_CLADDING_PANELS`
- `R6_T06_FIXED_INSPECTION_GLAZING`
- `R6_T06_WINDOW_FRAMES_AND_SILLS`
- `R6_T06_PORTAL_FRAME_AND_BEARING_SHOES`
- `R6_T06_ACCESS_REVEALS_AND_CANOPY`
- `R6_T06_PERSONNEL_DOOR_STATIC_CLOSED`
- `R6_T06_CARGO_SHUTTER_STATIC_CLOSED`
- `R6_T06_RECESSED_SEALED_THERMAL_SERVICE`
- `R6_T06_LAYERED_EAVES_AND_ROOF_SERVICE`
- `R6_T06_MAIN_FUNCTION_LABEL`
- `R6_T06_PERSONNEL_LABEL`
- `R6_T06_CARGO_LABEL`

仅关闭以下旧对象的 MeshRenderer，并记录场景实例覆盖：`B_RING_Transfer_Pod_0729`、`B_RING_Transfer_Pod_Orange_0730`、`R2_MODULE_TRANSFER_POD_05`。旧 GameObject、Transform、父链、组件和显示源资源保留；没有用禁用 GameObject 影响其碰撞。

新独立组 `B_WEST_TRANSFER_06_DETAIL_R6` 位于 Unity `(2015,0,2130)`；其内部 `R6_WEST_TRANSFER_06_ROOT` 位于世界 `(1873.58667,233,2298.529785)`，Unity Yaw 140°，按源矩阵换算，无额外 37°。新增子对象几何烘焙至该模块局部坐标；顶点和法线采用一次 XZY 映射、绕序换一次，UV V 不翻转。没有重装原概念根或增加 Collider、灯、运行时代码。

复用已有 R4 灰白涂层、深色涂层钢、粗糙裸金属、安全赭色和防滑甲板材质；按已有交接表映射至 `R4_Mat_03/00/01/02/05`，不修改这些资产。新增 `R6_Dark_Inspection_Glazing`、`R6_Label_Dark` 为不透明标准 URP Lit 常量材质，读取作者 Principled 的线性底色并转换为 Unity 颜色输入，金属度 0，光滑度分别 0.72、0.20，无新增纹理或 Shader。

保留原桥面、外侧走带、已有碰撞、R3 路径、其他 R2/R4、R5 两件支承、八楼、DEM、118 灯／135 按钮、正式和并行地图；未改照明、曝光或运行逻辑。新增人行门和货运门保持静态关闭显示，不能视为可操作气闸。

源：`LocalData/AI/AssetHandoffs/TheophilusDenseRing/R6/Moonkov_Theophilus_B_Dense_Ring_Concept_Increment_R6.blend`，45,902,403 字节，沿用交接已核对 SHA-256 `036cd0473be5f2d8efbce3abb23ee62ea1076d792cfa4fa53392e4dad30916df`。只提取内嵌 `R6_MINIMUM_IMPORT_ZH.txt`、`B_RING_R6_TRANSFER06_DELTA.json` 和指定集合；未保存源 Blend，未重下载、安装工具或找其他素材。

回退：同一持久 R6 目录中的 `BeforeTransferR6.unity` 和 `BeforeTransferR6_Renderers.tsv` 保存接入前场景及三个旧显示状态/引用。恢复前保留后续编辑；也可移除新独立组并恢复记录的旧 Renderer 状态。

必要确认仅覆盖正确目标、用户 dirty/Play 占用、本次绑定及保存重开。首轮在写入前因 Unity 材质资产名与 Blender 名不同的校验失败，修正交接表名称匹配后成功，未重导 R4。临时脚本编译无诊断；重开后 13 件显示引用存在、3 个旧 Renderer 关闭、新组无碰撞，`dirty=false`、`playing=false`。Git diff 出现 Unity 序列化 ID 重排，局部核对差异涉及的 11 个旧命名对象、48 个序列化块，除 ID 映射外内容相同，没有扩大为全场保护扫描。

未截图、试走、全场测试、保护扫描或性能测量；没有达到概念图的实机画面验收声明。按用户约定本批做限定本地 commit，不推送，不夹带并行改动。
