# Unity 项目上下文

分析日期：2026-10-02。基于实际项目配置、代表代码和已连接 Editor。

- 项目根：`E:/UnityProjects/FPS_Template`。
- Unity 6000.5.10f1，URP 17.5.0；GraphicsSettings 指向现有 URP Pipeline。
- Input System 1.20.0，activeInputHandler=1；保留已存在的 Starter Assets。
- 模板使用 Entities／NetCode 与 `GhostBridgeBootstrap`、GhostBridge 连接游戏对象和网络实体；原有脚本主要为 MonoBehaviour 加 ECS 系统。
- 默认 Build Settings：MainMenu、GameScene、GameResourcesSubScene、Persistents、SpawnPointsSubScene。`GameManager`／`SceneLoader.cs` 负责模板游戏场景加载。
- 2026-10-15 正式默认地图为 `MoonEnvironment/Variants/TheophilusBase/Scenes/TheophilusGameScene.unity`，配套 TheophilusSpawnPointsSubScene（8 个出生点）。已包含 Theophilus R7 接地、R8 表面/渲染与 R9/R10 塔显示增量，配置 12 个物资点和撤离区，复用原搜打撤、Ghost 资源与服务端敌人，移除本地验证角色驱动。用户明确要求后续直接修改正式地图、不再维护研究副本，并小步 commit 存档。Apollo12、MoonGameScene 与原 GameScene 主菜单预览保留；离线／Host／Client／独立 Server 使用同一入口，详见 `Docs/TheophilusGameMap.md`。
- 2026-10-15 R21 已直接安装在正式地图：13 件 R20 机械与 6 件 R21 井体显示构件；四份正式专属 TerrainData 分别保留近／远景显示、碰撞职责，并同步切出 112 m、64 边形的保守栅格洞口。共享原 TerrainData、DEM、旧场景不改写；原三座径向桥、R7 接地与物资／撤离设置保留。新构件没有 Collider／导航，尚不代表可行走深井。保存重开和五条射线已检查，Console 存在 SubScene.OnEnable 的重复加入断言；未确认产生时点，未做联机／画面验收。详见 `Docs/AI/Theophilus_R21_Delivery.md`。
- 2026-10-15 R23 材质／UV 修正直接应用于正式地图现有 19 件 R20/R21：76 个材质槽、4 个独立材质、6 张贴图、17 份 UV 网格副本。源拓扑／坐标哈希和原导出顺序匹配；未安装的另 19 个 R18/R19 目标不补导入。几何、Transform、碰撞和 Terrain/holes 未改，原资产保留；保存重开、Shader 和引用检查通过。Console 仍有前述 SubScene 断言；不做截图／走图／性能或重复物理检查，不称最终画面验收。详见 `Docs/AI/Theophilus_R23_Delivery.md`。
- 2026-10-15 后续明确补齐授权完成：按固定 R21 最终可见清单新增 9 件 R18、10 件 R19 保留显示件，并应用各自 R23 材质／UV，最终 canonical 覆盖 38/38。原清单匹配到的 40 个旧 Renderer 关闭，24 个此前已关闭；原对象、碰撞和交互组件不变。此前 19 件 R20/R21 及三桥未重复导入或改动，四份 TerrainData/holes、出生／物资／撤离入口、R7、R11 和历史回退均保留。新增 19 份 Mesh，复用现有材质，无新 Collider／灯光／运行代码。保存重开与引用检查通过；SubScene 断言沿用原结论，未做 Host/Client 或画面验收。详见 `Docs/AI/Theophilus_R21_Retained_Delivery.md`。上条“未补导入”是此阶段之前的记录。
- 正式月球地图的 `Moon Raid` 配置最小搜打撤：服务端拾取验证、12 格背包、死亡／超时损失、8 秒撤离、连接内存仓库、结算后再次出战。复用现有光环战斗，未接数据库或敌人 AI；断线清空仓库。详见 `Docs/MinimumRaidLoop.md`。
- 原有功能根为 `Assets/Scripts`、`Prefabs`、`Scenes`、`Data`、`InputSystems`、`Settings`；第三方／Unity 示例目录保持原样。
- 月球功能集中在 `Assets/MoonEnvironment`，分 Runtime、Demo、Editor 三个程序集；Runtime 不依赖模板业务类或 Starter Assets。Demo 复用 Starter Assets 与 Input System。
- 正式 MoonGameScene 使用 `MoonEnvironment/SpaceSky` 与 `LunarSpaceSky.mat`：NASA HDR 星图与银河、Blue Marble 地球、与 Lunar Sun 在编辑及运行模式同步的太阳圆盘。Moon Space Volume / MoonSpacePostProcessing 提供 Bloom 眩光，MoonRenderer 引用 URP PostProcessData。MoonSceneLighting 管理运行时材质及设置恢复；无图形设备的服务器跳过天空实例化。当前是固定天空与游戏曝光近似，未接天文星历或昼夜循环。详见 `Docs/MoonSpaceEnvironment.md`，素材署名见 `Assets/MoonEnvironment/Art/Space/Sources.md`。
- DollSinger 本地角色集中在 `Assets/DollSinger`，分 Runtime／Editor 程序集；本地演示和操作见 `Docs/DollSinger.md`。独立网络预制体 `DollSingerNetworkPlayer.prefab` 已通过模板 Ghost 接入 MainMenu 的第三项角色选择，复用模板预测移动和服务器武器判定；适配器位于 `Assets/Scripts/Gameplay/Player/DollSingerNetworkPresentation.cs`。联机结构、操作与限制见 `Docs/DollSingerNetwork.md`。
- DollSinger 地面动画使用 MoveX / MoveZ 的身体坐标方向树，AimBlend 混合 Locomotion Pack 与 Pistol 包；非瞄准前向 Walk / Run、Halo Aim 手势层、空中护裙保持原资源。网络方向复用 ControllerState.AnimatorMotion，无新增 Ghost 字段。原地副本及重建工具见 `Docs/DollSingerLocomotion.md`。
- DollSinger 原地转身使用腿部 Turn In Place 层；本地身体 yaw／已有网络 CurrentRotation 驱动左右挪步和播放速度。镜头独立旋转不触发，移动和空中关闭；Root 与上身不参与此层，未添加脚部 IK。最终观感由用户 Play 确认。
- Unity Test Framework 已安装；本次迁移使用针对性编译、资产引用与 Play Mode 冒烟验证，没有引入通用测试架构。
- 官方 AI Assistant 和 Pipeline 包已存在。当前可通过 Unity CLI 的本地 Pipeline 连接 Editor；操作必须显式指定 FPS_Template。
- 原有格式以 C# 标准花括号和私有 `m_` 字段为主；月球新代码使用明确序列化私有字段与 `Unity.MP_FPS.Moon` 命名空间。

约束：Markdown 图使用 Mermaid；Unity 验证遵循最小充分原则。不要把月球环境迁移扩大成联机玩法重构，不修改源 MMORPG。

2026-10-15 用户实测反馈穿模后，正式地图巨构及 R6/R7/R9、38 件 R18–R21 补静态非凸 MeshCollider，R3 原有 53 个 BoxCollider 保留；隐藏旧封盖排除，井底另用双面碰撞网格处理源面向下绕序。三处桥面／井底／井壁射线与保存重开通过；地形与玩法入口未改，未作角色走图或导航验收。当前碰撞状态以 `Docs/AI/Theophilus_Gameplay_Collision.json` 为准，之前“仅显示”记录是历史阶段。

同日针对 Play 帧率，正式地图 6000 块原始小石头从始终最高精度（总计约 4665 万三角形）改为 `MoonRockLod` 距离分级，近／中／远边界 18／80／600 m，原碰撞和源资产不变，R3 现有简化碰撞也保留。新增四份共享低精度网格，一个场景组件每 0.25 s 批量检查，无每石头 Update。固定 Scene 视角石头几何量降至约 52.8 万；同视角实际 Scene 渲染回调 CPU 提交中位数 5.58→4.99 ms（每阶段 6 帧）。未测 GPU 时间或真实 Play FPS，不能据此称已稳定目标帧率；未启动 Host 或账号部署。详见 `Docs/AI/Theophilus_Collision_Performance_Fix.md`。

已查阅：`Packages/manifest.json`、`ProjectVersion.txt`、`GraphicsSettings.asset`、`EditorBuildSettings.asset`、`ProjectSettings.asset`、`GhostBridgeBootstrap.cs`、`GameManager.cs`、`SceneLoader.cs`、Starter Assets 控制器与输入脚本、源月球地形／材质／着色器／接地实现。

未覆盖：模板完整联机端到端测试、构建平台差异、正式月球网络地图的出生点和 Ghost 资源配置、性能测量。
