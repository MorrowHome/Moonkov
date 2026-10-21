# 1–9 快捷栏

2026-10-21。接入真实 RaidHUD / DollSingerInput / ContainerInventoryView；沿用现有 UI Toolkit，不保存或更改场景。

## 玩家路径

首次有效配置只将当前真实装备武器设为 1/2/3；保存或主动清除后不再自动填充，后续换装/新 ID 需要显式绑定。

1. 在战局打开背包，悬停或选择已装备武器、按原规则可访问的可用物品（食物饮水可在随身背包，医疗/氧气/战术道具须口袋或胸挂），按 1–9 绑定；也可将实际物品拖入下方格子。右键槽位清除。不可从搜刮容器、仓库或未装备武器绑定。
2. 关闭背包，数字键只选中。武器按该物品当前真实装备槽切换；道具显示名字、数量，并拦截旧武器 Fire/Aim/Reload。松开当前主键后重新按主键才使用，数字键本身不消耗。
3. 急救包/医疗凝胶/创伤包、食物饮水氧气、能量雷分别走原服务器 RPC；新战术道具只发 RaidTacticalItemActionRpc，没有客户端扣物或指定落点。
4. 新道具请求发出后、Started 回包前，以及服务器报告耗时动作期间，X、菜单或数字切换意图均可请求取消；该数字不会同时选择下一物品。同一次按住 X 不会在取消回包后转成踮脚，松开后的下一次独立 X 恢复踮脚。移动/受伤等中断仍由服务器裁决。取消反馈只提示检查背包，不声称服务器必然未扣物。

## 身份、生命周期和边界

- 绑定保存真实 ItemId + Code；不按 code 寻找替代物。失去访问权限/医疗或战术道具转入背包/丢弃/耗尽后槽位保留图标并标红 ×，当前选择解除。同一实际物品重新进入可用位置后可再次选择；同 code 新 ID 必须重新绑定。
- 使用 PlayerProfileClient.LocalPreferenceScope，存储键 Moonkov.QuickSlots.v2.<scope>。账号验证身份/服务或离线实际文件路径隔离由现有 Scope 提供；不保存 token。无有效 scope 不写入。旧未集成基础版本 v1 偏好不迁移。
- 场景销毁/死亡/回合变化解除当前选择，偏好仍保留。新回合真实物品 ID 不同则保持不可用；不会凭空装备。
- 不对 WeaponGestureLab 安装委托，它继续原 1–3 行为。赏金速战中 V 留给暗视、B 切视角；原搜打撤/实验室仍为 V，中键不变。文本输入、菜单、倒地/死亡、耗时动作和服务器等待互斥。
- 九格按可用宽度伸缩，格内名称省略，选中完整名称在下方换行展示。图标、数量、既有背包蓝色选中状态、红色不可用加叉及拖入有效/无效边框可见。旧医疗进度面板与快捷栏使用同一像素布局根，避免窄屏缩放重叠。
- 栏内反馈显示服务器报告的冲刺体力百分比、疲劳与恢复提示、体力针/解毒剂剩余时间；不在客户端硬编码效果时长。赏金暗视期间栏位变暗，提示松开 V 后使用物品。

## 集成契约

- Presentation：RaidQuickSlotInput.Bind(input) / Unbind(input)。DollSingerInput 两个可选委托 QuickSlotSelection(int number)->真实武器槽 或 0，QuickSlotPrimaryUse(bool pressed)->是否抑制武器。只在拥有者实际战局安装。
- RaidHUD：InitializeQuickSlots() / PresentQuickSlots(bool active) / DisposeQuickSlots()，由基础生命周期调用；不在独立 Update 中重复执行。
- ContainerInventoryView：QuickSlotCandidate（悬停优先、选中后备）、QuickSlotDrop(item,panelPoint)、QuickSlotDragPreview。Drop 被消费后不发送 Move，不改变库存。
- 统一 NextManualActionRequest / m_InventoryRequestId；owner 的同 RequestId Started→Completed/Interrupted 仍更新反馈，不能只观察 RequestId。
- TryQuickTacticalInteraction(bool active) 在旧 E 交互前执行；命中陷阱/弹药箱或自身灭火止血后返回本帧已消费，不再同时打开旧容器。
- E 查询使用实际 Ghost root 的 capsule 眼高（max(.3,height-.2)，默认高度1.8）和 PredictedPlayerGhost.ControllerState 的 Pitch/Yaw，完全与服务器一致。第三人称 orbit 相机距离不改变可达性；隔墙与跨楼层仍由客户端辅助查询和服务器复核。
- 图形预览调用 TacticalItemsFeedback.SetSelectedItem(code/null)，服务器仍独立验明物品、距离、地面与消耗。SetPresentationBlocked 在菜单/文本/暗视/死亡隐藏；pending 保留手持并隐藏投掷轨迹。
- 手持模型目前覆盖本地第一人称/本地第三人称，远端选择持物未新增 Ghost 协议；世界投掷物/陷阱/箱体的联网表现由道具组提供。

## 验证记录

- `dotnet run --project Tools/QuickSlotChecks/QuickSlotChecks.csproj`：真实规则与内存 store，167 项（无真实 Prefs/账号读写）。覆盖三真实武器槽、全部战术物品、三种医疗、食品/水/氧气、选择不消耗、访问失效、同名新 ID 不替代、重绑/清除/账户隔离/损坏存储。
- Unity 单角色 fixture 已完成真实 UITK 路径：医疗包4/解毒针5/破片雷6/窒息弹7/弹药箱8键绑定，耐力针9拖入→右键清除→重新拖入，库存 GraphVersion=9 保持不变。关闭菜单首帧保护继续保留；等待正常游戏帧后独立数字9选中成功，数量2、使用意图0。
- `QuickSlotCancelInputChecks.Run(9)` 首轮通过27项，随后同World追加已有pending动作时的真实TextField+X检查，实际共31项通过；确切物品 Use RequestId=1、未注入 Started 时 Cancel RequestId=2，同次按住X不转踮脚，松开重按恢复踮脚。菜单和真实 TextField 焦点阻止误用、重绑与选择，数量2/版本9全程不变。UI确认是显式 fixture 注入，未执行服务器消耗。追加阶段真实帧28184–28254，输入X未生成Cancel，数量/版本不变。最终证据 `Library/BuildValidation/BountyClash/UI/cancel-input-31.json`，原始输入另有 `quick-input.jsonl`。
- 用户否决的初版视觉验收已撤销；业务输入证据继续有效。新版复用 `Components/InventoryItem.uxml` 的 Button/stateFx/itemLabel/quantity、`InventoryTheme` 的 `inventory-selected` 和共享 `MoonkovControls.uss`；使用深黑、白字与既有蓝强调，取消金色框及游戏常驻双行说明。新版已由持有独占 Editor 的 UI 组在安全点合入，统一编译已通过（0错误、0警告），新版真实 Bind4、Drag9、Clear9、重新Drag9及Hit断言通过。模板标签定位已归一化（重置定位/高度、透明背景、空子元素隐藏、空底保持不透明）。协调方已逐张检查 `archive-final-active-800.png`、最终新样式覆盖的 `final-inventory-800.png`、`final-inventory-1280.png`、`final-tool-fatigue-800.png`、`final-tool-fatigue-1280.png`：空条/黑块消失，蓝色选中、数量、图标和长中文反馈可读，504px九格与原健康/弹药仪表无重叠。本轮UI视觉门禁通过；库存/疲劳与原PlayerHUD角落数值为明确注入样本，未冒称新的服务器状态验证。
- 单角色 fixture 与随后 JUI/文本X保护合入后的统一编译均通过，后者0错误、0警告。文本焦点且已有pending动作时，按键X有Typing保护；菜单/非活动自动取消保留。此边界已由上述31项实际输入回归覆盖。
- 用户明确取消联机验收与多开，只使用单 Unity 的轻量验证，不启动额外 Editor/MPPM/多 client World。网络传输未测不再作为本任务阻断；服务器效果由道具组的独立权威检查覆盖。

## 集中翻译增量清单

由原 UI 组维护 MoonkovUI en/zh 与 SharedData。以下源字符串仅作增量清单，不另建本地字库。

- `Equip weapons first. Keep usable supplies in your pockets or rig.`
- `Binding saved.`
- `Binding cleared.`
- `Item unavailable. Equip it or bind another item.`
- `Empty slot. Bind an item in your inventory.`
- `Hover an item in your inventory and press this number, or drag it here.`
- `Right click to clear binding.`
- `WEAPON SELECTED`
- `LEFT CLICK TO USE`
- `Hover an item + 1–9 / Drag to bind / Right click to clear`
- `1–9 SELECT / LEFT CLICK USE SELECTED ITEM`
- `Item is cooling down.`
- `Not connected. Item remains in its container.`
- `Cancellation requested.`
- `Use requested. Waiting for server.`
- `Waiting for the server. Retry after the connection recovers.`
- `Item use unavailable while inactive or downed.`
- `Action in progress. X cancels timed actions.`
- `Item action started. Keep still; X cancels.`
- `Item action completed.`
- `Cancellation processed. Check inventory.`
- `Item action interrupted. Nothing consumed.`
- `Aim at clear ground within reach.`
- `Too many active objects. Remove a trap first.`
- `No room for supplies.`
- `No wound needs treatment.`
- `No usable target within reach.`
- `Inventory changed. Try again.`
- `Poison exposure detected.`
- `Item action rejected. Check access and placement.`
- `USING ITEM / {0:0.0}s · X CANCEL`
- `SPRINT {0:0}% / FATIGUED / REST TO RECOVER`
- `SPRINT {0:0}% / 1–9 SELECT`
- `STAMINA BOOST {0:0}s`
- `ANTIDOTE {0:0}s`
- `DARK SIGHT / RELEASE V TO USE ITEMS`
- `[E] RESUPPLY FROM AMMO BOX`
- `[E] DISARM TRAP`
- `[E] STOP BURNING / BLEEDING`

## 单角色 Play 验收入口（原 UI 组持 Editor 令牌运行）

- 在首次创建 HUD 前调用 `QuickSlotPlayChecks.InstallMemoryPreferences()`，同时安装内存 store 和随机 ValidationScope。`BountyClashUIFixture` 只创建一个无连接、无 Transport 的 ClientWorld、一个正式角色与真实 RaidHUD；不登录、不读写账号存档，快捷栏偏好不读写真实 PlayerPrefs，不创建 ServerWorld 或第二个客户端。
- `QuickSlotPlayChecks.BeginIsolated(hud,input)` 使用 fixture 的真实输入组件，安装实际 InputSystem 虚拟键鼠，并保存原委托/绑定模型，返回临时 JSONL 路径。fixture 的客户端库存和 owner 回包是显式注入的 UI 状态；它不执行服务器消耗、不模拟网络传输。
- `Items()` 列真实图中的物品 ID/code/数量。打开真实背包及对应胸挂后，`BindViaKey(itemId,4)` 向实际 tile 发带目标及指针源的 UITK 悬停、数字事件；`BindViaDrag(itemId,5)` 驱动现有 Drag 操纵器并断言库存版本不变；`ClearViaRightClick(5)` 发成对右键按下/抬起，走实际右键回调。
- 关闭背包并捕获输入后，`PressNumber(4)` 通过 InputSystem 排队且跨实际游戏帧释放；`ExpectSelected(4)` / `ExpectQuantity(itemId,N)` 检查选中且未消耗。`PressPrimary(aim:true)` 通过生产输入生成请求，`ExpectUseIntent(itemId,1)` 检查确切 ID 的单次实际 RPC 意图，`ExpectWeaponSuppressed()` 检查 Fire/Aim/Reload 被拦截。没有 Transport 时不宣称服务器已经扣物或完成动作。
- `QuickSlotCancelInputChecks.Run(9)` 在同一现有 fixture 内运行：Use 尚无 Started 回包时按住 X，仅生成递增一次的 Cancel；显式注入 UI 确认后，原先按住的 X 仍不转成踮脚，松开再按 X 恢复正常踮脚。随后检查菜单和有实际焦点的 TextField 阻止重绑、选择与使用。每阶段都检查客户端数量/版本不变。输出独立 JSON，并明确标记 LocalUIOnly / NoTransport / ServerConsumptionTested=false。
- 暗视使用 `HoldInput(3f,true,Key.V)`，只保持 V+RMB，不自动加入主键。E 使用角色 Ghost 的实际眼高/朝向，需另行验证第三人称相机后移仍可达，以及遮挡/楼层拒绝。
- `BountyClashUIFixture.End()` 先禁用 fixture HUD，再由 `QuickSlotPlayChecks.End()` 恢复原委托、绑定模型、此前 ValidationStore/Scope，移除虚拟键鼠和更新回调；然后销毁 fixture World/角色并恢复原场景运行状态。不会删除任何用户存档。
- 服务器扣物、取消和效果由同一 Editor 内顺序运行的独立权威/Physics 检查覆盖。用户明确取消联机验收与多开，不以未运行 Transport、多 Editor 或多 client World 阻断完成。
- 必须记录：实际 Frame 递增、真实 UITK 绑定/拖放/右键清除、选中前后数量/版本不变、主使用只发正确请求、工具抑制武器且切回恢复、失去访问/死亡清空选择、文本/菜单/倒地/暗视防误触。
- 必须截图并人工检查：战局持物/数量/选中、背包九格及拖入反馈、1280×720 与 800×600 中文长反馈。规则模型、UI 事件或注入 owner 状态均不等于网络全路径验收。

真实对照还需包括：角色贴箱第一人称可交互；保持同一角色位置切第三人称并后移 orbit 相机超过2.2m仍可交互；隔墙或不在同一可达楼层则不得交互。新 World 重用 RaidId、切换 profile scope 均清除 pending/当前选择/旧反馈；不能因普通移物递增 RequestId 重播旧 Completed。

用户范围更新：取消联机验收是内存约束下的明确决定，不以“未跑多客户端”延长本任务；仍完成单角色输入、真实 UITK 事件、可见布局与实际窄屏截图。
