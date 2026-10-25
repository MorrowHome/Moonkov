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
- 用户明确要求模块是克制的小点缀。保留原光环的主体轮廓和识别度；安装后要有真实可辨的外观变化，不能让组件喧宾夺主。多模块组合也按此标准验收。
- 正式战局已将库存模块掩码接入 `HaloModuleVisual`，通过平面细线、弧段和节点改变外观。改装台大图目前仅创建 `StashItemArt(weaponCode)`，仍是静态 HaloIcons，没有装后外观预览。参数预览通过不等于外观预览完成。
- 部分组件当前使用大范围分段环，组合后的克制程度尚未目视验收。独立本地武器演示的模块掩码为 0，不能用它判断正式战局的改装效果。
- 光环页已改为直接操作的改装台。首页、顶部导航、仓库按钮和物品操作都进入同一套 UI。左侧是实际光环与槽位，中央展示全部兼容模块，右侧比较用途、代价和真实参数。
- 固定布局在 `Components/HaloWorkshop.uxml`，局部样式在 `HaloWorkshop.uss`。动态控件由 `ContainerInventoryView.Modules.cs` 绑定。槽位玩家文案为供能、射击、辅助，底层 ID 不变。
- 未持有模块可以预览，不能安装。获取入口定位补给商品，或直接选中月核研究与制造。浏览和比较不创建库存物品。
- 参数预览只替换当前槽的实体模块位，保留其他槽与永久研究。伤害显示每发全部弹丸全中的总量。不要把扇形阵列的单粒伤害降低误报为总伤害降低。
- `HaloModules.Codes` 同时用于原六模块掉落池。不要直接加入需要研究制造的模块。
- `HaloModule` 的 16 位掩码还承载生物研究和活动效果。新增模块前先检查位占用，并保留现有参数叠加顺序。
- `HaloLightPanel.cs` 管理局内照明，与模块改装独立。
- `Docs/MoonkovUI.md` 中“配件更换尚无后端”的旧描述已过时。改装现状优先查实际代码、[光环模块](Docs/HaloModules.md)和[月核成长状态](Docs/Planning/Missions/Mooncore-Progression-Status.md)。

### 验证边界

改装台使用 `HaloWorkshopReview.cs` 做单 Editor、编辑模式下的原生布局和真实物品树事务检查。覆盖只预览、安装、同槽替换、拆卸、失败重试、忙碌防重复、只读、未持有和无兼容武器状态。测试使用隔离库存，不写玩家档案。

2026-10-25：Unity C# 编译、UXML/USS 导入和 16 项定向检查通过。已目视检查中文原生终端模板和紧凑正文。实际逻辑尺寸为 1280×900、1280×720，另检查 420px 高正文；DPI 为 2。没有做真实账号完整操作或联机验收。批处理另有未修改的材质预览 Metal 编译诊断，本次不据此宣称全项目构建通过。

原生截图按实际 panel 尺寸和 DPI 捕获。不要强制根画布超出 panel；这会使画面放大并被裁掉，几何检查仍可能误报通过。`native` 后缀是修正后的截图，日志记录实际尺寸。终端模板截图验证样式继承，不代表真实账号和完整导航操作已验收。

后续按改动范围使用 `HaloWorkshopReview.cs`、`HaloModuleChecks.cs` 和 `Backend/HaloWeaponChecks`。数据库检查只用隔离测试数据。遵守项目单 Editor 和不做联机验收的约定。旧 `MoonkovUIRuntimeReview` 的光环检查已适配新控件；其其他流程仍有历史环境限制。

月核中文研究布局与回流光环效果仍有历史视觉待验收记录。既有编译、截图或数据库检查不能替代当前版本的实际验收。

## UI 模块与层级交接

2026-10-25。中英文策划案位于 `Docs/UI/ArtBrief-20261025/`。两份 Word 共用 `brief-content.json`，由 `build_brief.py` 生成。

- 本案主体交代功能模块、子项和共享关系。主界面计划重做。风格、构图、布局和页面组合由美术决定，不要求沿用旧导航、三栏库存或五步出发。
- v1.1 增加一页最低素材交付。九宫格只用于需拉伸且要保留边角的底板或边框；交一张完整 PNG，标四边像素和独立内容边距。纯色形状可由程序绘制。Unity 切片、动态文字和接入由程序负责。
- M01 至 M50 为玩家功能，M51 为开发工具范围说明。编号用于核对覆盖，不要求每项单独成页。基地、通讯和出发保险要保留未开放边界。
- 文档按 `develop@11c245a3` 静态核对。旧六槽、常驻小地图和改装无后端描述不能作为需求。当前是十二个装备目标、七种实体模块；旧模板排行榜不列为正式玩家模块。
- 本次未修改游戏代码或运行 Unity。中英文模块记录逐项对应，Word 已逐页渲染检查。
- 本机打包 LibreOffice 不一定能读取系统中文字体。中文渲染需用临时 `FONTCONFIG_FILE` 指向项目 `Assets/Localization/Fonts` 和运行时字体目录，使用 `Noto Sans CJK SC`。不需要安装字体或修改系统配置。
