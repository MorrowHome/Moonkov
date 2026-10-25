# 项目交接

## 光环改装系统

2026-10-25 接手。代码基线：`28373b62`。

本聊天负责光环改装系统，包括改装规则、库存和保存链路、参数比较、安装与拆卸交互、改装 UI。跨系统共用代码只按具体需求做最小修改。

### 当前实现

- 只有步枪 `rifle`、紧凑步枪 `compact`、霰弹 `shotgun` 支持实体模块改装。不要把所有光环默认视为兼容。
- 每件光环有储能、整形、功能三个槽。同槽互斥。模块挂在具体武器的物品树下。
- 基地可以安装、替换和拆卸。战局禁止拆装，但允许转移带模块的完整武器。
- 替换时，旧模块返回新模块原来的位置。拆卸需要仓库空位。出售武器前必须先拆下模块。
- 改装不补弹。缩容不删除已有的已充能弹量。部署、撤离、死亡掉落和搜刮沿用完整物品树。
- 原六种模块可购买或搜刮。第七种月核回流器必须研究后制造。

### 关键入口

| 职责 | 文件 |
|---|---|
| 兼容关系、槽位、模块掩码 | `Assets/Scripts/Networking/Shared/HaloModules.cs` |
| 物品树和库存事务 | `Assets/Scripts/Networking/Shared/InventoryModel.cs` |
| 参数计算和装备同步 | `Assets/Scripts/Gameplay/Weapon/WeaponRegistry.cs`、`DollSingerWeapons.cs` |
| 改装窗口 | `Assets/Scripts/UI/Game/ContainerInventoryView.Modules.cs` |
| 光环总览和改装入口 | `Assets/Scripts/UI/Game/MoonkovTerminal.cs`、`StashScreen.Containers.cs` |
| 页面和模块控件 | `Assets/Resources/Moonkov/UI/Halo.uxml`、`Components/ModuleSlot.uxml`、`Components/ModuleOption.uxml` |
| 客户端库存和离线保存 | `Assets/Scripts/Networking/Client/PlayerProfileClient.cs`、`OfflineProfileStore.cs` |
| 在线保存 | `Backend/MoonPersistence/InventoryRepository.cs` |

### 后续修改注意点

- UI 使用原生 UI Toolkit。保留真实光环外观、中英文绑定、忙碌和只读状态。操作仍提交库存事务，不能只改显示。
- 光环总览目前是固定步枪与原六模块的说明图，不代表玩家当前构筑。总览没有月核回流器，实际改装窗口已支持它。
- `HaloModules.Codes` 同时用于原六模块掉落池。不要直接加入需要研究制造的模块。
- `HaloModule` 的 16 位掩码还承载生物研究和活动效果。新增模块前先检查位占用，并保留现有参数叠加顺序。
- `HaloLightPanel.cs` 管理局内照明，与模块改装独立。
- `Docs/MoonkovUI.md` 中“配件更换尚无后端”的旧描述已过时。改装现状优先查实际代码、[光环模块](Docs/HaloModules.md)和[月核成长状态](Docs/Planning/Missions/Mooncore-Progression-Status.md)。

### 验证边界

本次只读检查实现并补充交接，没有修改运行代码、启动 Unity 或重跑历史测试。

后续按改动范围使用 `HaloModuleChecks.cs`、`MoonkovUIRuntimeReview.cs` 的 `halo` / `halo-compact` / `workshop` 用例，以及 `Backend/HaloWeaponChecks`。数据库检查只用隔离测试数据。遵守项目单 Editor 和不做联机验收的约定。

月核中文研究布局与回流光环效果仍有历史视觉待验收记录。既有编译、截图或数据库检查不能替代当前版本的实际验收。

## UI 模块与层级交接

2026-10-25。中英文策划案位于 `Docs/UI/ArtBrief-20261025/`。两份 Word 共用 `brief-content.json`，由 `build_brief.py` 生成。

- 本案只交代功能模块、子项和共享关系。主界面计划重做。风格、构图、布局和页面组合由美术决定，不要求沿用旧导航、三栏库存或五步出发。
- M01 至 M50 为玩家功能，M51 为开发工具范围说明。编号用于核对覆盖，不要求每项单独成页。基地、通讯和出发保险要保留未开放边界。
- 文档按 `develop@11c245a3` 静态核对。旧六槽、常驻小地图和改装无后端描述不能作为需求。当前是十二个装备目标、七种实体模块；旧模板排行榜不列为正式玩家模块。
- 本次未修改游戏代码或运行 Unity。中英文模块记录逐项对应，Word 已逐页渲染检查。
- 本机打包 LibreOffice 不一定能读取系统中文字体。中文渲染需用临时 `FONTCONFIG_FILE` 指向项目 `Assets/Localization/Fonts` 和运行时字体目录，使用 `Noto Sans CJK SC`。不需要安装字体或修改系统配置。
