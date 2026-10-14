# 西侧 R3 折返维护楼梯接入

2026-10-14，只接入已交付的 `B_09_WEST_PASSIVE_STAIR_ACCESS_R3` 增量。现有 R2 巨构展示没有重新导入，八栋外楼、配色、地形、原 A 路线、MC00/气闸及角色系统未编辑。目标场景保存重开后干净、不在 Play；没有截图或整段试走。

后续 R4 已更新西塔/桥头等 82 个显示对象的 UV0 与贴图材质，见 `Docs/AI/Theophilus_R4_Near_Material_Delivery.md`。R3 原对象及 53 个路径/桥盒碰撞保留，仍未整段实走。以下材质复用说明属于 R3 接入时状态。

## 实际安装

- 目标场景：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/Scenes/Theophilus_Shoulder_Return_Study.unity`。
- 独立场景根：`B_WEST_PASSIVE_STAIR_ACCESS_R3`，世界 `(2015,0,2130)`，旋转 identity、缩放 1。
- 专属 Prefab：`Assets/MoonEnvironment/Variants/TheophilusShoulderReturn/DenseRingConceptB/R3WestStairAccess/Prefabs/B_West_Passive_Stair_Access_R3.prefab`。
- 导入 57 个楼梯视觉网格，包括源合同中的 25 跑、275 级、平台及入口连接构型。每级升高约 0.16884 m，净宽 2 m；连接地面 `(1810,187.1683,2265)` 与上桥接口 `(1818,233.6,2265)`，高差约 46.4317 m。
- 源 `B_RING_CONCEPT_ROOT` 世界变换只去除/转换一次，Blender XYZ 按 XZY 转 Unity，几何烘焙至根局部。新独立根与现有巨构使用相同世界原点，不叠加 MC00 变换。

## 路径碰撞与桥接

`R3_PATH_SURFACE_GUIDES_NO_RUNTIME_COMPONENTS` 不作为可见网格导入。按源指南的上表面生成 52 个简单倾斜/平面盒：25 跑鼻线坡面、26 处休息平台和一个自然地面入口连接。每盒厚 0.12 m，上表面与指南对齐；只作路径代理，没有把每级格栅、栏杆、斜撑、薄板或装饰分别加 Collider。

另外在独立 R3 根下为原 `B_WEST_RING_ACCESS_BRIDGE_0526` 添加一个地板盒代理，宽约 7.0000 m、长约 18.9596 m，顶面 Y=233.6，与新楼梯上端接口相同。它只覆盖现有西侧接入桥这一段，没有改原桥 Mesh/Prefab，也没有给环形主平台、径向主桥或工业展示区增加碰撞。

共新增 53 个 BoxCollider。路径指南对象没有 Renderer，57 个视觉对象没有自动附加碰撞。装饰护栏没有逐网格碰撞；不能据此宣称全区防护、其他展示平台或完整探索路线已完成。

## 原对象与材质

下列三个现有场景实例对象仅设置 inactive，作为可回退覆盖；原素材和 R2 Prefab 中的对象均保留：

- `B_WEST_Access_Lift_Cladding_0524`
- `B_WEST_Access_Lift_Orange_Face_0525`
- `B_CONCEPT_PIER_0522`

原 `B_WEST_RING_ACCESS_BRIDGE_0526` 和 `B_UNSURVEYED_FOOT_PAD_0523` 保留。其他 R2 对象及独立本地接入组未删除。新碰撞和视觉全部在独立 R3 根下，可整体移除；撤回时同时重新启用上述三个旧展示对象。

严格复用已有七个粗糙 PBR 材质，没有新增材质、灯、贴图、Shader、电梯运行时代码或玩法组件。源 R3 的四个近景恒定材质按角色映射至既有材质：

| R3 来源 | 既有角色 |
| --- | --- |
| 镀锌格栅 | `Mat_01` 深灰结构钢 |
| 风化暖灰折板 | `Mat_00` 粗糙灰白包覆 |
| 受限维修赭色 | `Mat_02` 少量工程橙 |
| 下部入口月尘 | `Mat_04` 矿物灰 |

保留导出 UV 和法线，没有以本次接入宣称写实效果或概念图细节已达到。

## 最小检查与回退

只核对目标项目/场景、用户 Play/dirty 占用、导入坐标、原桥顶面和所供入口中心的明显高度错误，再保存重开。原生入口中心 Y=187.16835，与合同 Y=187.1683 相差约 0.00005 m。原桥顶面 Y=233.6 保持。临时接入脚本编译无诊断错误，重开结果 `dirty=false`、`playing=false`。

没有 Play 整段试走、胶囊矩阵、截图、性能测量、大范围重哈希、安装、提交或推送。入口边缘和塔脚地形接触不是新的工程测量；步行、转弯净空、扶手防护和角色脚部观感尚未实测。原环形主体及其他工业区仍为展示范围。

R3 前场景回退保存在 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R3/BeforeStairR3.unity`。它包含 R2 及八楼配色；恢复前先保留后续编辑，不直接覆盖新工作。

源文件保留 R1/R2，R3 文件只读导出：

| 文件 | 字节数 | SHA-256 |
| --- | --- | --- |
| `Moonkov_Theophilus_B_Dense_Ring_Concept_Increment_R3.blend` | 13,925,561 | `6413375c6ef2614b0aeae9a196915d6e48c58865482ed8adb2f3d33314595d93` |
| `B_RING_R3_STAIR_ACCESS.json` | 8,457 | `42dea612818a6ffe1497d51adeda80b51876a53f1c422c17fd7554a5f07861f7` |

输入各校验一次，无全场保护重哈希。原始导出和接入记录在持久目录 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R3/`；汇总见 `Docs/AI/Theophilus_West_Stair_R3_Import.json`。
