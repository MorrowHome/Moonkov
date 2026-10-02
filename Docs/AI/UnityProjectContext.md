# Unity 项目上下文

分析日期：2026-10-02。基于实际项目配置、代表代码和已连接 Editor。

- 项目根：`E:/UnityProjects/FPS_Template`。
- Unity 6000.5.10f1，URP 17.5.0；GraphicsSettings 指向现有 URP Pipeline。
- Input System 1.20.0，activeInputHandler=1；保留已存在的 Starter Assets。
- 模板使用 Entities／NetCode 与 `GhostBridgeBootstrap`、GhostBridge 连接游戏对象和网络实体；原有脚本主要为 MonoBehaviour 加 ECS 系统。
- 默认 Build Settings：MainMenu、GameScene、GameResourcesSubScene、Persistents、SpawnPointsSubScene。`GameManager`／`SceneLoader.cs` 负责模板游戏场景加载。
- 正式联机地图改为 `MoonEnvironment/Scenes/MoonGameScene.unity`，另加 MoonSpawnPointsSubScene（8 个出生点）；原 GameScene 保留为主菜单预览。Host／Client／独立 Server 使用同一正式地图入口，菜单预览退出后才加载月球地图，详见 `Docs/MoonNetworkMap.md`。
- 原有功能根为 `Assets/Scripts`、`Prefabs`、`Scenes`、`Data`、`InputSystems`、`Settings`；第三方／Unity 示例目录保持原样。
- 月球功能集中在 `Assets/MoonEnvironment`，分 Runtime、Demo、Editor 三个程序集；Runtime 不依赖模板业务类或 Starter Assets。Demo 复用 Starter Assets 与 Input System。
- DollSinger 本地角色集中在 `Assets/DollSinger`，分 Runtime／Editor 程序集；本地演示和操作见 `Docs/DollSinger.md`。独立网络预制体 `DollSingerNetworkPlayer.prefab` 已通过模板 Ghost 接入 MainMenu 的第三项角色选择，复用模板预测移动和服务器武器判定；适配器位于 `Assets/Scripts/Gameplay/Player/DollSingerNetworkPresentation.cs`。联机结构、操作与限制见 `Docs/DollSingerNetwork.md`。
- Unity Test Framework 已安装；本次迁移使用针对性编译、资产引用与 Play Mode 冒烟验证，没有引入通用测试架构。
- 官方 AI Assistant 和 Pipeline 包已存在。当前可通过 Unity CLI 的本地 Pipeline 连接 Editor；操作必须显式指定 FPS_Template。
- 原有格式以 C# 标准花括号和私有 `m_` 字段为主；月球新代码使用明确序列化私有字段与 `Unity.MP_FPS.Moon` 命名空间。

约束：Markdown 图使用 Mermaid；Unity 验证遵循最小充分原则。不要把月球环境迁移扩大成联机玩法重构，不修改源 MMORPG。

已查阅：`Packages/manifest.json`、`ProjectVersion.txt`、`GraphicsSettings.asset`、`EditorBuildSettings.asset`、`ProjectSettings.asset`、`GhostBridgeBootstrap.cs`、`GameManager.cs`、`SceneLoader.cs`、Starter Assets 控制器与输入脚本、源月球地形／材质／着色器／接地实现。

未覆盖：模板完整联机端到端测试、构建平台差异、正式月球网络地图的出生点和 Ghost 资源配置、性能测量。
