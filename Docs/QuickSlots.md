# 1–9 快捷栏

2026-10-21。接入真实 RaidHUD / DollSingerInput / ContainerInventoryView；沿用现有 UI Toolkit，不保存或更改场景。

## 玩家路径

首次有效配置只将当前真实装备武器设为 1/2/3；保存或主动清除后不再自动填充，后续换装/新 ID 需要显式绑定。

1. 在战局打开背包，悬停或选择已装备武器、按原规则可访问的可用物品（食物饮水可在随身背包，医疗/氧气/战术道具须口袋或胸挂），按 1–9 绑定；也可将实际物品拖入下方格子。右键槽位清除。不可从搜刮容器、仓库或未装备武器绑定。
2. 关闭背包，数字键只选中。武器按该物品当前真实装备槽切换；道具显示名字、数量，并拦截旧武器 Fire/Aim/Reload。松开当前主键后重新按主键才使用，数字键本身不消耗。
3. 急救包/医疗凝胶/创伤包、食物饮水氧气、能量雷分别走原服务器 RPC；新战术道具只发 RaidTacticalItemActionRpc，没有客户端扣物或指定落点。
4. 新道具耗时期间 X、菜单或数字切换意图请求取消；该数字不会同时选择下一物品。移动/受伤等中断仍由服务器裁决。

## 身份、生命周期和边界

- 绑定保存真实 ItemId + Code；不按 code 寻找替代物。失去访问权限/医疗或战术道具转入背包/丢弃/耗尽后槽位保留图标并标红 ×，当前选择解除。同一实际物品重新进入可用位置后可再次选择；同 code 新 ID 必须重新绑定。
- 使用 PlayerProfileClient.LocalPreferenceScope，存储键 Moonkov.QuickSlots.v2.<scope>。账号验证身份/服务或离线实际文件路径隔离由现有 Scope 提供；不保存 token。无有效 scope 不写入。旧未集成基础版本 v1 偏好不迁移。
- 场景销毁/死亡/回合变化解除当前选择，偏好仍保留。新回合真实物品 ID 不同则保持不可用；不会凭空装备。
- 不对 WeaponGestureLab 安装委托，它继续原 1–3 行为。赏金速战中 V 留给暗视、B 切视角；原搜打撤/实验室仍为 V，中键不变。文本输入、菜单、倒地/死亡、耗时动作和服务器等待互斥。
- 九格按可用宽度伸缩，格内名称省略，选中完整名称在下方换行展示。图标、数量、金色选中、红色不可用及拖入有效/无效边框可见。

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
- Unity 编译、真实背包输入/拖入、Transport 使用和视觉验收尚待共享 Editor 令牌；纯规则通过不代表这些路径已验收。

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
- `Item action cancelled. Nothing consumed.`
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
- `[E] RESUPPLY FROM AMMO BOX`
- `[E] DISARM TRAP`
- `[E] STOP BURNING / BLEEDING`

## 真实 Play 验收入口（原 UI 组持 Editor 令牌运行）

- 进入 Play 菜单、打开任何 fixture profile 或首次初始化槽位前，先 `QuickSlotPlayChecks.InstallMemoryPreferences()`；Editor-only `QuickSlotPreferenceStore.ValidationStore` 会拦截 Load/Save，整个 fixture 不读写真实 PlayerPrefs。若已加载有效 scope，入口拒绝继续，需重启隔离 fixture 按序安装。
- `QuickSlotPlayChecks.Begin()`：查找实际 RaidHUD，注入内存偏好存储，返回临时 JSONL 证据文件。已有账号绑定模型在 End 恢复。要求实际 Play、有效 scope；不会伪造 HUD 快照或库存。
- `Items()`：列实际客户端可访问物品 ID/code/数量。打开真实背包及对应胸挂窗口后，`BindViaKey(itemId,4)` 发真实元素悬停和 UITK 数字事件；`BindViaDrag(itemId,5)` 驱动现有 Drag 操纵器并断言库存版本不变；`ClearViaRightClick(5)` 走实际右键回调。
- 关闭背包、锁定 Game 视图，`PressNumber(4)` 通过 InputSystem 设备排队并跨实际游戏帧释放。几帧后 `ExpectSelected(4)` / `ExpectQuantity(itemId,N)` 检查选中且未消耗；`Record(label)` 输出状态。
- `PressPrimary(aim:true)` 同时按主键/瞄准，`ExpectWeaponSuppressed()` 验证旧 Fire/Aim/Reload 全部被拦截；主键通过实际输入/实际 Transport 使用；只允许正在使用的离线 profile 处于系统临时目录，拒绝线上账号和真实本地存档。等待真实服务器 UseSeconds 后 `ExpectQuantity(itemId,N-1)`。该入口不直接调用服务器、不假造扣物成功。
- `PressKey(Key.X)` 检查取消；`HoldInput(3f,true,Key.V)` 在在线隔离赏金速战中保持 V+RMB 三秒，不生成左键或 Use；`PressKey(Key.V)` 可作短按。E/G/F 等可能改变世界的输入同样要求临时离线档案。
- `End()`：恢复原 binding model 和此前 ValidationStore override、移除虚拟键鼠与 Editor 回调、隐藏持物；退出 Play 自动调用。临时离线 profile 由启动该 fixture 的 UI 组关闭并删除，先关闭 World 和 profile lease；此 helper 不删除任何账号存档。
- 必须记录：真实 Frame 持续递增，选中前后数量/版本不变，主使用服务器完成后只减少一次；取消不扣物；道具选择无 Fire/Aim，切回武器恢复；失去权限/死亡后选择清空；文本输入、菜单、倒地、暗视防误触。
- 必须截图：正式战局选道具的手持/数量/选中，背包九格及拖入反馈，1280×720 与 800×600 中文长反馈。未取得这些证据前不把 model 或 UI 事件注入称为实际玩家全路径验收。

真实对照还需包括：角色贴箱第一人称可交互；保持同一角色位置切第三人称并后移 orbit 相机超过2.2m仍可交互；隔墙或不在同一可达楼层则不得交互。新 World 重用 RaidId、切换 profile scope 均清除 pending/当前选择/旧反馈；不能因普通移物递增 RequestId 重播旧 Completed。
