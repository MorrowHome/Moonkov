# 正式地图碰撞与石头渲染开销修复

2026-10-15，针对用户“模型没碰撞、Play 帧数低”的直接反馈。

## 碰撞

此前 R21/R23 按显示接入，38 件新增构件确实没有 Collider；巨构原始 B_RING 基础、桥和部分细节也没有碰撞。本次添加 1349 个静态非凸 MeshCollider，覆盖当前启用的巨构、桥、R6/R7/R9 与 38 件新构件。原 R3 阶梯已有 53 个简化 BoxCollider，保留它们，不叠加三角网格碰撞。

禁用的封盖和历史 Renderer 不补碰撞，不重新挡住已开井口。井底原始面存在向下绕序：第一次从上方射线未命中、从下方命中，确认源面朝向问题；使用专用双面碰撞 Mesh 后向下命中，显示 Mesh 不改。三座桥面定点向下射线、井底向下射线、井壁从内部向外射线通过。保存重开后所列活动构件均有碰撞引用。

碰撞提交 `01f05cca`，机器记录 `Docs/AI/Theophilus_Gameplay_Collision.json`。未运行角色穿行、楼梯手感、完整通路或 AI 导航验收，不新增 Rigidbody 或钻井玩法。

## 渲染

正式地图静态盘点发现 6000 块石头全部使用原最高精度 Mesh，总三角形数 46654920；对比新增 38 件构件合计 236124 三角形。正式场景中没有导入相机。保留现有灯光和阴影，不用全局画质降低来处理。

两种石头新增共享网格，中精度分别为 1193／671 三角形，远精度为 199／111；原始分别为 9958／5598。`MoonRockLod` 仅在 Play 且有图形设备时工作，使用 `Camera.main`，每 0.25 s 检查一次缓存的世界包围球；18 m 内保留原网格，18–80 m 用中精度，80–600 m 用远精度，之后关闭显示。仅状态改变时写 Mesh／Renderer。没有每石头 Update 或新增上万个 LOD 子物体；不改 MeshCollider、材质、Transform 或原始 Mesh 资产，关闭组件时恢复。

## 必要验证

- 新运行脚本已由 Unity 编译并挂入正式场景，四份 Mesh 引用和两个源 Mesh 引用存在。
- 对实际 6000 块石头调用同一缓存／分级逻辑，检查近、中、远、关闭四档及恢复。原网格与 Renderer 启用状态全部恢复，6000 份原碰撞网格引用保持不变，场景未变脏。
- 固定 Scene 相机位置下，石头几何量由 46654920 降为 527800，下降约 98.9%。该视角位于场景高处，不能替代玩家出生点、室内或所有视角。
- 首次全局 `UnityStats` 在编辑状态未刷新，前后返回同一组旧统计，明确不用于帧率或渲染改善声明。随后只做一次实际 SRP Scene 相机 begin/end 回调计时，每阶段 6 帧：CPU 渲染提交中位数 5.5776→4.9936 ms。它不测 GPU 时间，不等于 Play FPS。
- 性能阶段的场景 diff 仅新增一个 `MoonRockLod` 组件及其引用；6000 块石头的序列化 Mesh／Collider 绑定、原资产和地形数据没有改写。临时验证只使用 Editor 回调，已取消订阅并恢复显示状态。

未启动 Host／Play、未触发账号或部署请求，未截图、巡图、构建或做多端／性能矩阵。实际 Play FPS 和目标硬件性能仍需实测；本次已移除“全部小石头始终最高精度”的明确几何开销。记录见 `Docs/AI/Theophilus_RockLOD_Validation.json`。原基线、输入、碰撞接入前场景和临时辅助代码保留在忽略的 `LocalData/AI/AssetHandoffs/TheophilusDenseRing/R23/` 下。

## 2026-10-15 后续：正式 Play 的实例化与骨骼开销

在用户交出 Unity 独占控制后，从 MainMenu 启动正式 TheophilusGameScene 离线对局。使用独立临时离线档案，不修改用户进度。本轮机器为 i5-14600KF / RTX 4070 SUPER，Unity 6000.5.10f1，D3D12，Game 视图 1365×670。沿用 MoonPipeline 的 Render Scale 1、4× MSAA、8192 主光阴影和 1800 m 阴影距离。

### 岩石 GPU 实例化

`MoonRockGrounding` 用 MaterialPropertyBlock 写入每块岩石的 `_GroundPlane`、`_BlendHeight`、`_RockVariation`、`_RockFreshness`，但原 shader 只有材质常量声明。材质的 Enable GPU Instancing 开关本身不能使这些逐实例覆盖兼容合批。`LunarRock.shader` 与 `MoonInput.hlsl` 现在在实例化变体中从实例缓冲区读取这四项，保留原 UnityPerMaterial 布局和非实例化路径；地形 shader 不启用此分支。机制参考 [Unity 逐实例属性文档](https://docs.unity3d.com/cn/2021.3/Manual/gpu-instancing-shader.html)。

同一对局、同一相机位置和方向，冻结玩法时间以隔离渲染，分别运行原 shader 和修复 shader，每段 8 秒：

| 指标 | 原 shader | 实例化修复 |
| --- | ---: | ---: |
| 样本帧数 | 505 | 1111 |
| 帧耗时中位数 | 15.563 ms | 6.972 ms |
| P95 | 18.870 ms | 8.566 ms |
| Main Thread 中位数 | 15.597 ms | 6.934 ms |
| GPU 中位数 | 7.556 ms | 5.553 ms |
| Draw Calls | 17610 | 14301 |
| 三角形 | 6855244 | 6855244 |
| Shadow Casters | 34702 | 34702 |
| 相机位移 | 0 m | 0 m |

这是冻结玩法的渲染 A/B，帧耗时下降约 55.2%，Draw Calls 下降约 18.8%；不能当作正常对局 FPS。前后截图检查未见画面变化。Forward 和 ShadowCaster 实例化变体预热通过，没有 shader 编译错误；原版和修复版均有同一个既有 MoonLocalBounce 未初始化警告。

### 角色次级骨骼

恢复玩法后，岩石修复版一段固定视角样本仍为 19.201 ms 中位数（约 52 FPS），GPU 约 5.027 ms，说明还有 CPU 开销。短 CPU Profiler 捕获中，代表帧 66 的单次 `SecondaryBoneSpring.LateUpdate` 为 6.476 ms。

`SecondaryBoneSpring` 本轮改动：

- 用父级变换和骨骼局部缩放直接计算 rest direction，省去正常骨骼每子步的中间旋转写入；保留最终旋转。绑定时检查碰撞查询是否依赖该骨后代，仅存在依赖的骨保留原中间写入，零长度分支也维持原恢复行为。
- 在绑定时缓存外裙链拓扑；碰撞仍读取求解当时的实时世界位置，保持原链顺序和逐链退出规则。
- 同一次胶囊／外裙段查询复用端点位置，把同一轮不变的 CoverBlend 移出骨循环。

不改变弹簧参数、子步次数、碰撞规则、模拟顺序或角色资产。对运行中的实际导入骨架逐骨比较原 `bone.TransformVector` 与新公式：492 根骨，最大世界方向误差 1.016124e-7 m。新捕获代表帧 235 的单次 LateUpdate 为 2.292 ms。两段 Profiler 的对局敌人状态不同，且 Profiler 本身有额外开销，以上仅作为热点降低证据，不当作整局严格 A/B。

新代表帧仍有 SimulationSystemGroup 11.112 ms、相机渲染 4.059 ms，不能宣称所有主线程瓶颈均已消除。没有用降低阴影／分辨率或停掉 AI 来掩盖该开销。

最终重新进入正常离线对局，在另一出生位进行固定视角 8 秒采样（`06-final-gameplay.json`）：395 帧，中位数 19.828 ms（约 50 FPS），P95 23.886 ms，P99 25.406 ms，相机位移 0 m。本段 GPU 计时约 0.128 ms，明显不足以代表该场景完整渲染，视为不可用。因出生位与敌人状态不同，不与前一对局的 52 FPS 作严格前后比较。第二次实际骨架方向检查仍通过，492 根骨、最大误差 7.300049e-8 m。

验证限于 Unity 编译、shader 变体预热、正式离线 Play、固定视角渲染对照、骨骼方向等价检查和两段短 CPU Profiler。没有验证打包游戏、目标笔记本、多客户端或所有运动姿态。临时诊断脚本与基线 shader 已移出 Assets，输入／时间／启动场景已恢复，退出 Play。

原始 JSON、截图、改动前骨骼脚本及临时诊断脚本备份位于忽略目录 `Library/BuildValidation/FormalPerformance-20261015/`。`invalid-moving-*` 不用于结果；`05-spring-optimized` 在设置新朝向前记录了起始相机位置，位移字段包含视角切换，也不用于严格帧率对比。
