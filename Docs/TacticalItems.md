# 战术道具（Moonkov 适配 v1）

实现日期：2026-10-21。此文记录当前代码契约；运行验证见文末，不将静态测试视为联网验收。

## 来源与范围

Crytek 的[官方玩家指南](https://www.huntshowdown.com/player-guide/en_US)列出急救包、窒息弹、生命针等道具；[早期官方说明](https://www.huntshowdown.com/news/hunt-showdown-early-access-release-notes)确认炸药、破片、火与三类针剂；[官方现行怪物/状态指南](https://www.huntshowdown.com/player-guide/wiki)说明流血、解毒与窒息反制；[2.3 开发者说明](https://www.huntshowdown.com/news/developer-insight-update-23-changes-priorities)明确铁丝不能隔墙/天花板伤人；[库存改版说明](https://www.huntshowdown.com/news/inventory-slot-rework)包含警报绊雷、弹药箱等组合。

[Crytek官方2.3公告](https://store.steampowered.com/news/posts/?appids=594650&enddate=1743089498&feed=steam_community_announcements)已将普通耐力针从10分钟调整为7分钟，本批据此使用420秒；旧版600秒资料不作为本轮耐力针规则。

以上用于参考品类和玩法关系。以下价格、使用时长、伤害、半径、寿命全部是 Moonkov 自己的版本化适配，不宣称等同于 Hunt 当前平衡。原有 M-40 `medkit` / `med_gel` / `trauma_kit` 承担急救包功能，原 `energy_grenade` 保留；不注册换名字的重复系统。不复制 Hunt 美术或音频。

## 实际物品与效果

| Code | 名称 | 使用/部署 | 实际效果及反制 |
|---|---|---|---|
| medkit | M-40 急救包（已有） | 沿用地图医疗耗时，默认3秒 | 现有分部位恢复40；毒状态阻止治疗 |
| vitality_shot | 生命针 | 3秒 | 恢复全部可恢复的存活组织至当前合法上限；不复活、不修黑部位、不恢复赏金倒地损失上限；毒状态禁止 |
| stamina_shot | 耐力针 | 3秒 | 420秒防止独立冲刺疲劳；不补饥饿/水分/氧气、不治伤腿 |
| antidote_shot | 解毒针 | 3秒 | 清除当前毒状态，600秒免疫本组毒云；不是全面伤害无敌 |
| dynamite_bundle | 炸药包 | 蓄势1秒、释放后4秒引信 | 8m最大650全身爆炸伤害随距衰减；视线保护；破坏可见的铁丝/已布陷阱；窒息区熄引信 |
| frag_bomb | 破片雷 | 蓄势1秒、释放后4秒引信 | 8m最大320全身爆炸伤害，命中再流血8秒，每秒4；不清除铁丝 |
| fire_bomb | 燃烧瓶 | 蓄势1秒、接触爆开（最迟6秒） | 半径3m地面火30秒，接触点燃4秒，每秒8；窒息清除火场与身上燃烧；手动自救可灭火 |
| choke_bomb | 窒息弹 | 蓄势1秒、释放后3秒引信 | 半径4m持续20秒，消火消毒、抑制新点燃与炸药；进入者咳嗽，实际武器操作惩罚×1.5 |
| alert_trip_mine | 警报绊雷 | 布设3秒、1秒启用 | 1m近地触发，警报声音/可见脉冲8秒；自敌友皆可触发 |
| concertina_trip_mine | 铁丝绊雷 | 布设3秒、1秒启用 | 触发后45秒，接触减速至35%并施加8秒流血；可由炸药清除，破片不清除 |
| poison_trip_mine | 毒绊雷 | 布设3秒、1秒启用 | 触发后毒区30秒，毒状态2秒刷新、每秒6且阻止医疗；解毒针免疫，窒息消云消毒 |
| ammo_box | 电池补给箱 | 布设3秒；交互补给2秒 | 总计400能量、每次最多100；优先补真实现存partial cell，随后新增真实cells。口袋/胸挂容量或负重不足时不扣箱；120秒过期 |

独立冲刺疲劳：容量100，轻装时实际水平奔跑且Sprinting每秒消耗2（约50秒耗尽）；疲劳后仍能跑，Sprint约为原速度0.8且至少比Walk快5%，铁丝等更强限制优先。轻装时停步/不再奔跑1.5秒后每秒恢复20，达到25解除疲劳形成滞回。普通搜打撤超过18公斤后，消耗逐步增加，恢复逐步减慢；具体规则见[局内负重](RaidEncumbrance.md)。原地Shift、撞墙无水平位移不耗。以上为Moonkov适配常量，未实现独立近战/战斗耐力池。耐力针420秒防疲劳，解毒针600秒；重复针剂将对应buff刷新为各自时长，不叠乘；相同燃烧/流血/毒状态只保留单一状态实例，不因多场重叠无限加倍伤害。毒云接触伤害与短暂中毒合并为 Moonkov 版本；不声称 Hunt 的中毒状态本身持续扣血。火与其他战术伤害作用于Alive身体，断线但仍Alive的身体照常受伤；Downed/终死不继续燃烧伤害，不燃烧倒地尸体、不扣永久生命块、不引入额外死亡结算。完整燃尸、赏金复活、猎人特质/近战工具、甲虫等不在本批。

普通搜打撤中，同一次服务器更新内的子弹实际组织伤害达到 20 点，会造成 30 秒流血，每秒 4 点。多颗弹丸的真实伤害可合计。护盾完全吸收、致死命中及不足阈值的伤害不会产生枪伤流血；赏金模式保持原有枪伤规则。新枪伤刷新现有流血时间，不叠加每秒伤害。按 E 可以花 3 秒止血，期间仍失血；移动、开火或再次受击会打断。人形敌人也会在安全时止血，止血不消耗医疗物品。

枪伤检查覆盖实际服务端伤害、护盾吸收、弹丸合计、来源归属、计时刷新、止血和赏金模式隔离。人形敌人决策检查覆盖持续失血、治疗完成和再次受击中断。单人临时档案通过服务端伤害入口模拟一次 25 点枪伤；客户端显示「[E] 灭火 / 止血」，正常按 E 后服务器确认止血。该过程不是实弹交火或多人联机验收。

## 玩家流程与服务器约束

- 新道具均在月尘商店有真实库存条目；医疗补给表掉落三针，装备补给表小权重掉落投掷物、三种陷阱及电池箱。保留原保证医疗/电池和掷骰预算。拾取、主动丢弃、死亡袋及合法撤离返还使用既有库存树，不重新造物资副本。
- 快捷栏由 UI 组负责：1–9只选择，主使用键发服务器意图。新工具必须在口袋或已装备胸挂直接格内；背包内要先移到可操作位置。服务器不相信客户端持有量、伤害、位置或瞄准落点。
- 手柄 X 瞄准任务设备时只处理任务，不同时发出止血或其他战术请求。瞄准气闸或灯光按钮时，按钮优先于无目标的止血；明确瞄准的战术物件保留原优先级。键盘 E 与 F 仍各自处理。8 项输入规则与隔离编译通过；实体手柄和实际场景交互尚未验证。
- 布设须在服务器瞄准的2.2m近地稳定表面；同点0.65m内禁止叠放，四角支撑与净空检查阻止穿地板布设。陷阱垂直触发差<=0.65m，区域效果使用实际PhysicsScene/视线，不穿墙穿楼层。
- 所有新使用操作经过耗时/库存版本/连接/当前raid/存活可行动检查。正在装填或切换武器时不能开始；动作开始后移动超过0.55m、受伤、开火、换弹、换武器、断线、死亡、搜刮/医疗替换、库存变化或显式取消中断。完成时重新验证、同服务器线程原子消耗一次；丢失目标、无地面、超限或空间失败均不消耗。
- E交互由UI适配读取 `TacticalItemsFeedback.TryFindInteraction` 返回的TargetId和Disarm/Resupply。拆雷需3秒，销毁已部署陷阱，不返还全新物品，避免双返还或免费刷物。补给箱每次完成重新检查余量，已空箱不可被第二个同时完成者消费。
- 自救 Extinguish 需3秒，自身火、流血、饥渴和缺氧继续扣血，但不打断该动作。服务器只推进已知的火与流血伤害基线；新的外部伤害或护盾命中、移动及开火仍中断。反复开始取消不能免伤，完成后才清除状态，不消耗物品。
- `RaidInventoryState.TacticalPending` 与既有库存操作互斥。请求共享 `RequestId` 单调序列；同时绑定 `RaidId`、`ExpectedVersion`、真实 `ItemId`，旧请求不能跨局/重复扣物。
- 所有爆炸/火/毒/流血调用统一 `BountyClashDamage.Apply`，遵循其友伤、Downed、终死与击杀归属规则；道具组不直接调用Leaderboard或重复扣装备。
- 在线队伍的保护同时阻止友军火、毒和铁丝造成燃烧、中毒、流血或减速。投掷者仍受自己的效果影响。友军接触仍可触发陷阱；窒息弹仍对所有进入者灭火、解毒并造成咳嗽。来源记录投掷时的服务器角色和队伍，连接编号复用不改变保护对象。
- 服务端每World最多64对象、每玩家最多8个未触发陷阱；尸体、场景可见物件及状态不跨回合累积。回合终局/再开/地图变化清空对象、pending与状态，并发epoch reset。
- 选中投掷物有客户端瞄准轨迹，选中布设物有地面预览；预览只辅助操作，服务器仍重建真实释放/落点并拒绝非法位置。
- 客户端可靠低频RPC持续同步活动物件，晚加入者下一周期收到现状；移除消息、epoch与sequence拒绝旧状态复活。客户端只生成无碰撞表现，不能产生伤害。服务器物理推进有细分sweep，重用能量手雷的重力/弹跳。咳嗽通过既有同步ghost状态在25m内观察者生成有限3D噪声，不新增全局敌人坐标广播。

## 接入文件

- `Shared/TacticalItemsRules.cs`：品类、明确参数、真实库存消耗/电池补给规则；无Unity依赖。
- `Shared/TacticalItemsRpc.cs`：action/owner/world可靠RPC与客户端状态。
- `Shared/TacticalItemEffects.cs`：与现有冲刺/运动接入。
- `Server/ServerGameSystem.TacticalItems.cs`：服务器意图、耗时、物理、状态、清理；主组在survival后、death前每tick调用UpdateTacticalItems，在终局/再开局调用ClearTacticalItems。
- `Client/TacticalItemsClientSystem.cs`：唯一RPC消费者与owner singleton。
- `Gameplay/VisualEffects/TacticalItemsFeedback.cs`：程序生成可见投掷/绊线/电池箱/手持物；火/毒/窒息复用项目已有 Unity Particle Pack 的专属资源副本与URP材质，提示音为原创合成、客户端交互查找。Shared Ghost字段/health hooks由主组修改；HUD、输入和翻译由UI组修改。


透明粒子由专属 `TacticalItemsTransparentRenderer` 在事件601合成到已有场景颜色，使用当前相机resolved深度硬遮挡及可选soft fade；战术shader只提供独立LightMode，移除普通forward和SilverPrint保护mask，避免重复绘制与背景残圈。全局SilverPrint599、暗视600；受伤/黏液屏幕警示在602，确保仍覆盖场景。没有活跃火/毒/窒息root、组件停用或subject-only相机时不enqueue，不额外请求intermediate/depth；不新建RT，不改全局调色或第三方源材质。该结构已静态审查，最终实景效果仍以下述验证结果为准。

## 验证记录

- 队伍状态效果的 16 项生产规则检查和游戏、编辑器程序集隔离编译通过。实际状态、友军触发陷阱和窒息弹的单服务器夹具已补充，尚未在编辑器运行；未做联机验收。
- `dotnet run --project Tools/TacticalItemChecks/TacticalItemChecks.csproj`：1125项已通过，含1024份医疗/装备掉落保证项与全部新道具自然获取、11物品真实可持有/使用/单次消耗/重放拒绝、库存空间与partial cell电量守恒；另13项独立冲刺疲劳/停止恢复/针前后/帧率独立规则。
- `Assets/Editor/TacticalItemsAuthorityChecks.cs` 已于UTC 2026-10-21 11:19:50在单个隔离server ECS World + 本地PhysicsScene实际执行64项通过。覆盖真实RPC/耗时/移动受击中断、墙/楼层/投掷扫掠、反毒消火、陷阱、电池箱竞争、生命针全可恢复组织、420秒耐力/600秒解毒、真实移动耗尽/疲劳仍跑/停止恢复、自救开始取消不免伤/同tick外伤中断、固定伤害来源与ID复用/旧epoch/断线Alive身体受伤。Owner回执使用生产Apply的纯内存模型，没有client World。
- 统一候选已实际编译并能运行Editor helper；当前Assembly-CSharp.dll文件UTC为2026-10-21 12:13:44.775071，Editor程序集为12:14:19.611314。此前Editor归还基线为单一干净WeaponGestureLab、play=false、World.All=0。记录保存在 `Library/BuildValidation/TacticalItems-20261021.json`，区分已观察的权威结果与新跑的纯规则输出；它不是最终全任务验收。
- 首版引擎预览 `/tmp/moonkov-tactical-vfx-preview.png` 已经人工审查，不通过视觉质量验收；改进版粒子、实体外形和手持模型待实景复核，不能将首图当最终证据。
- 专属粒子资源已由主组运行 `TacticalItemsPresentationSetup.CreateResources()` 生成到 `Resources/Moonkov`，三个prefab的Resources.Load已检查存在；所有新增prefab/material/meta必须随任务提交，第三方源资产不改。UTC11:53的正式月面图仍因蓝色边缘/硬补丁未通过视觉验收；独立调色后透明合成代码已通过静态审查、统一编译与资源重生，并完成首轮小验，不能将weighted native mask或本轮图视为最终效果。
- UTC12:23:03首轮后合成小验完成，原始5图/report/复现脚本保存于 `Library/BuildValidation/TacticalItems/Composite-20261021/`：火/毒蓝边与硬遮罩块消失；完整遮挡墙中心120000像素对照差异0；顺序独立UI相机非黑像素0；finally恢复单一干净WeaponGestureLab/play=false。CLI外层30秒超时但Unity继续完整执行，不能将外层超时误当根因失败，也未叠加重复运行。协调组已认可首两图可读性，但本轮fixture关闭后新增13条MissingReference（6291–6303），故本轮不能整体放行。根因是手动EditMode Awake未保证成对native OnDestroy；已完成owner销毁自动退订、predicate fake-null、幂等清理与fixture显式finally清理；UTC12:52:42修后复验通过：cleanupDisposed=True、OnDestroy双调幂等、墙差异0/UI颜色0，UI owner观察Console cursor6326后新增错误0（无DestroyedFeedback/ParticleVelocity）。证据保存于 `Library/BuildValidation/TacticalItems/Composite-Lifecycle-20261021/`；旧错误轮次保留，不用新结果覆盖历史。UTC13:39:12完整7图复验已完成（run_script compile322ms/execute18253ms），保存于 `Library/BuildValidation/BountyClash/TacticalMoon-v2/`；capturedComplete/cleanupDisposed均True，finally恢复clean单Lab/play=false；Console since79372→79402新增error0、groundTruth error0（仅1条package Account API超时warning）。协调组已亲读全部7图，火/毒/窒息无旧蓝边或纸片底，近远危险区、铁丝/陷阱/箱体功能可读性门禁通过。后三者仍为程序构型低模，此结论不表示原版精细美术或AAA复刻验收。
- 用户因Mac内存限制已取消后续联机验收，不再创建多客户端World或额外Editor。实际场景/UI操作与视觉质量仍待单Editor验证；既有隔离规则/物理证据不表述为多端联网验收。


权威检查复现命令（须持有Editor令牌且编译成功；本轮64项已实际通过）：

```sh
unity command eval --project-path /Users/morrow/UnityProjects/Moonkov --caller plugin --skill unity-cli --timeout 20000 'TacticalItemsAuthorityChecks.Run(); return "completed";' --format json
```

针剂/其他工具的手持物按本地选择可见；本地第三人称可绑定Humanoid右手，第一人称摄像机随使用进度抬举。远端玩家选择持物姿态尚未新增专门同步协议，不将本地持物美术表述为已经完成全远端手部动画验收。使用项目自有角色素材和原创提示音，没有Hunt原版双手动画/音频，也不声称原版精细美术复刻。

暗视接入后的受影响顺序检查另见 `Library/BuildValidation/BountyClash/DarkSight-World-v2/` 两张叠加图：真实战术火与授权样本云叠加既有damage/mucus材质，世界保持冷黑白，火/橙云与602屏幕警示仍有各自颜色；这些是表现层注入，不是新的伤害或联网验收。
