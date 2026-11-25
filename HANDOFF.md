# 项目交接

初次阅读先走完一局标准远征。README 里的四个问题依次对应开局、移动、库存和结算。[运行和代码入口](README.md)

## 资源与构建

- 用 Unity `6000.5.10f1`、Git 和 Git LFS。日常分支为 `develop`。分支、提交与单 Editor 约定见 [AGENTS.md](AGENTS.md)。
- 主菜单场景是 `MainMenu`。标准远征和极速赏金使用 `TheophilusGameScene`。竞技场仍使用模板的 `GameScene`。删样例时要查构建场景的传递依赖、其他资源的直接引用、项目设置和代码按名称加载的路径。[场景选择](Assets/Scripts/Gameplay/GameManager/SceneLoader.cs)
- 经典爆破需要本地沙 2。迁移机器时备份 `Assets/LocalMaps/Dust2`、同名目录的 `.meta` 和目录内的 `.meta`。原始美术工作文件也可能只在 `LocalData/OriginalSources`。清理磁盘前单独核对本地资源和只有本机保存的 LFS 对象。[资源管理约定](Docs/GitAssetPolicy.md)
- 中文 UI 字体通过本地化表里的 GUID 加载。打包要让 Addressables 随 Player 构建，并把字体 GUID 写入 Catalog。构建前检查会核对两项设置。[构建检查](Assets/Editor/MoonkovPlayerBuildChecks.cs)
- `LocalData/UIReview` 存放本机新生成的界面验收图。UI Builder 会在 `UIElementsSchema` 生成自己的 XSD。`Docs/AI` 剩下的环形收边 JSON 是美术工具的输入。[收边清单](Docs/AI/Moonkov_Rebuild_R21_RingFinish_Delivery.json)

## 本机磁盘

- 关闭 Unity 后可以清理构建、导入、Burst 和着色器缓存。下次打开会重新导入。`Library/BuildValidation` 混有旧场景备份和脚本，按文件检查。
- `LocalData/SourceFidelity20261028/World` 存放 Blender 工作文件和烘焙素材。`LocalData/HuntGunshotReview/Sources` 是音效制作工具的输入。[音效制作脚本](Tools/Audio/build_hunt_weapon_reports.py)
- 清理 Git LFS 历史缓存时使用 `git lfs prune --verify-remote --verify-unreachable --when-unverified=continue`。这会保留远端尚未确认的对象。

## 改代码时容易漏的地方

- 切场景时让本轮 ECS 更新先结束，再释放旧 World。无画面的 Editor 和服务端也必须能完成这一步，使用下一游戏帧作为清理时机。[退局与销毁](Assets/Scripts/Gameplay/GameManager/GameFlowControl.cs)
- 输入先经 `ClientInputReaderSystem`，再由 `ClientInputSenderSystem` 合并到目标 tick。改短按要检查同一 tick 的多帧输入。角色重生时需要重新绑定输入。[按键处理](Assets/Scripts/Gameplay/Input/ClientInputSenderSystem.cs)
- 角色控制器的可回滚状态用于客户端预测和服务端模拟。同步字段或 RPC 的顺序和类型都属于网络协议。修改后要复查生成的序列化代码、实际消息长度和分片重组。[移动状态](Assets/Scripts/Gameplay/Player/Movement/FirstPersonController.State.cs) / [库存同步检查](Assets/Editor/MoonkovRaidInventoryChecks.cs)
- 网络角色先更新手部动作，再按手部是否被占用决定武器显隐。不同角色的 Animator 对 `MotionSpeed` 使用的数值类型可能不同。[角色表现](Assets/Scripts/Gameplay/Player/DollSingerNetworkPresentation.cs) / [步态检查](Assets/Editor/LocomotionCadenceChecks.cs)
- 本地档案按部署 ID 和结算回执处理重试。装备扣除、加载失败退款和局后结算要一起验证，测试时创建独立的临时档案。库存图、物品 ID 与版本号也由后端共用。[单机存档](Docs/SinglePlayer.md) / [结算检查](Assets/Editor/MoonkovInventoryLifecycleChecks.cs)

## 验证入口

以下命令在仓库根目录运行，使用 .NET 10 SDK。覆盖的流程依次是任务与背包规则、独立存档的结算和重试、服务编译。

```sh
dotnet run --project Backend/MissionRulesChecks
dotnet run --project Backend/BountyClashContextChecks
dotnet build Backend/MoonPersistence
```

Unity 的 `Tools/Moonkov` 菜单提供背包同步、局内动作、战术道具和角色步态检查。单机回归可以在一个 Editor 里从主菜单进入远征，再退出回仓库。打包、数据库集成和多人连接需要各自的环境。[构建入口](Assets/Editor/MoonkovPlayerBuildChecks.cs) / [后端说明](Docs/PostgreSQLPersistence.md)

## 查旧资料

迭代日志和验收截图保存在清理前的 Git 提交。可以按原目录搜索，也可以只查看一个文件。[旧交接记录](https://github.com/MorrowHome/Moonkov/blob/c28b20a7c75d47996f0942acd9de788514f0031c/HANDOFF.md) / [旧文档目录](https://github.com/MorrowHome/Moonkov/tree/c28b20a7c75d47996f0942acd9de788514f0031c/Docs)
