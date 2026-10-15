# R21 最终保留显示构件补齐

2026-10-15。直接补齐正式 `TheophilusGameScene.unity`，保留原 R21 及 R23 提交和全部历史回退。输入继续使用已固定的 R21 Blend、R23 映射与 UV 清单，不重新下载，不接 R22。

## 范围与结果

- 补入准确清单内的 9 件 R18 与 10 件 R19，共 19 件；跳过的请求目标为 0，拓扑／坐标不匹配为 0。源对象、所属集合、ViewLayer 均允许渲染，且均不在 R18／R19／R20／R21 隐藏回退清单中。它们属于最终候选保留件。
- 正式地图 canonical 覆盖由 19/38 达到 **38/38**：9 件 R18、10 件 R19、13 件 R20、6 件 R21。所有目标唯一、激活、Renderer 启用、Mesh／材质引用有效。三座原桥另计，未重复导入。
- 既有 19 件 R20/R21 的 Mesh、材质、Transform、激活及 Renderer 状态不变，原三桥同样保留。没有导入隐藏旧件或两件地形参考 Mesh。
- 原隐藏清单共 100 个名称，正式图精确匹配 64 个 Renderer：本次新关闭 40 个，原已关闭 24 个；其余 36 个不存在，不补建。40 个变更为 14 件原升降车／停靠平台显示、24 件 R10 中央旧显示与 2 件 R2 卷筒显示。仅关闭 Renderer，旧对象、Mesh、Transform、Collider、运行和交互组件保留。

完整新增对象、源集合、坐标哈希、UV 计数、旧 Renderer 原状态和缺席清单在 `Docs/AI/Theophilus_R21_Retained_Manifest.json`，不靠前缀猜测淘汰状态。

## 转换与资产

新增 19 份本批 Mesh 资产，位于 `Assets/MoonEnvironment/Variants/TheophilusBase/DisplayIncrements/R21Retained/Meshes/`。源顶点数／面数／loop 数及局部顶点 float32 小端 XYZ 哈希逐件匹配 R23 合同。沿用原 R21 导出器的三角化 loop 顺序 `[0,2,1]`，只对指定 loop 应用 R23 UV；134296 个源 loop 对应 211416 个展开顶点。未按 Unity 顶点索引直接套 Blender loop 数组。

相对源 `B_RING_CONCEPT_ROOT` 烘焙原世界变换，只做一次 Unity XYZ = Blender XZY 和一次绕序反转。正式新显示根位于 `(2015,0,2130)`、旋转为单位四元数、缩放为 1，子对象不再额外旋转或移动。

复用现有 4 个 R23 材质及原烘焙贴图，其他槽复用 6 个已有明确材质依赖；未增加材质或贴图，未安装未用的混凝土变体。保留此前的 Metallic/Smoothness 通道公式、混凝土线性混色、米制 UV tiling 和法线导入设置。

## 最小检查与边界

在正确正式场景、非 Play、非编译、场景干净状态下开始。依赖引用存在、复用 Shader 可用且没有 Shader 错误；辅助脚本编译诊断为空。保存重开后，新增 19 件绑定、旧显示覆盖及全部 38 个 canonical 目标读回通过，场景干净。

序列化 diff 只新增 20 个显示 GameObject（一个根和 19 件构件）、19 个 MeshFilter、19 个 MeshRenderer，并修改 40 个旧 Renderer 的启用状态（24 个直接序列化字段、16 个 Prefab 覆盖）。原对象／组件无删除，其他既有字段无变化。受保护的原网格、材质和四份 R21 TerrainData 文件哈希前后一致。

未改 holes、旧 DEM、桥面路径、出生／物资／撤离／联机入口、R7 接地或 R11；未新增 Collider、灯光、运行代码、动画或钻井玩法。源模型、R17 及历史预览／回退保留。新构件仅用于显示，不能据此认定通路或机械功能可用。

SubScene 断言沿用已核实结论：R21 阶段已有记录，不能确定是否由 R21 引入，不是 R23 首次出现；它未阻止实际完成的 Editor 场景打开、保存和引用读回。未清日志或展开包／运行代码修复。不截图、不全场试走、不巡检性能、不重复洞口射线；Host/Client 与最终画面未验收。

## 分阶段存档

- `c2cab374`：19 份源校验通过并带 R23 UV 的独立网格资产与清单；此阶段不改场景。
- 正式场景接入、旧 Renderer 覆盖和本记录另做独立本地 commit，不 push。

机器结果见 `Docs/AI/Theophilus_R21_Retained_Application.json`。本机接入前场景备份、原 Renderer 状态和辅助脚本位于忽略目录 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R23/`，其中 `BeforeRetained_Formal.unity` 对应补齐前的 `17e71aa1` 场景。
