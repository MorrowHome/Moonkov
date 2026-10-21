# 战术道具（Moonkov 适配 v1）

实现日期：2026-10-21。此文记录当前代码契约；运行验证见文末，不将静态测试视为联网验收。

## 来源与范围

Crytek 的[官方玩家指南](https://www.huntshowdown.com/player-guide/en_US)列出急救包、窒息弹、生命针等道具；[早期官方说明](https://www.huntshowdown.com/news/hunt-showdown-early-access-release-notes)确认炸药、破片、火与三类针剂；[官方现行怪物/状态指南](https://www.huntshowdown.com/player-guide/wiki)说明流血、解毒与窒息反制；[2.3 开发者说明](https://www.huntshowdown.com/news/developer-insight-update-23-changes-priorities)明确铁丝不能隔墙/天花板伤人；[库存改版说明](https://www.huntshowdown.com/news/inventory-slot-rework)包含警报绊雷、弹药箱等组合。

以上用于参考品类和玩法关系。以下价格、使用时长、伤害、半径、寿命全部是 Moonkov 自己的版本化适配，不宣称等同于 Hunt 当前平衡。原有 M-40 `medkit` / `med_gel` / `trauma_kit` 承担急救包功能，原 `energy_grenade` 保留；不注册换名字的重复系统。不复制 Hunt 美术或音频。

## 实际物品与效果

| Code | 名称 | 使用/部署 | 实际效果及反制 |
|---|---|---|---|
| medkit | M-40 急救包（已有） | 沿用地图医疗耗时，默认3秒 | 现有分部位恢复40；毒状态阻止治疗 |
| vitality_shot | 生命针 | 3秒 | 恢复全部可恢复的存活组织至当前合法上限；不复活、不修黑部位、不恢复赏金倒地损失上限；毒状态禁止 |
| stamina_shot | 耐力针 | 3秒 | 600秒保持现有 Energy/Hydration 至少80，实际解除此两项资源不足造成的冲刺禁用；不补氧、不治伤腿 |
| antidote_shot | 解毒针 | 3秒 | 清除当前毒状态，600秒免疫本组毒云；不是全面伤害无敌 |
| dynamite_bundle | 炸药包 | 蓄势1秒、释放后4秒引信 | 8m最大650全身爆炸伤害随距衰减；视线保护；破坏可见的铁丝/已布陷阱；窒息区熄引信 |
| frag_bomb | 破片雷 | 蓄势1秒、释放后4秒引信 | 8m最大320全身爆炸伤害，命中再流血8秒，每秒4；不清除铁丝 |
| fire_bomb | 燃烧瓶 | 蓄势1秒、接触爆开（最迟6秒） | 半径3m地面火30秒，接触点燃4秒，每秒8；窒息清除火场与身上燃烧；手动自救可灭火 |
| choke_bomb | 窒息弹 | 蓄势1秒、释放后3秒引信 | 半径4m持续20秒，消火消毒、抑制新点燃与炸药；进入者咳嗽，实际武器操作惩罚×1.5 |
| alert_trip_mine | 警报绊雷 | 布设3秒、1秒启用 | 1m近地触发，警报声音/可见脉冲8秒；自敌友皆可触发 |
| concertina_trip_mine | 铁丝绊雷 | 布设3秒、1秒启用 | 触发后45秒，接触减速至35%并施加8秒流血；可由炸药清除，破片不清除 |
| poison_trip_mine | 毒绊雷 | 布设3秒、1秒启用 | 触发后毒区30秒，毒状态2秒刷新、每秒6且阻止医疗；解毒针免疫，窒息消云消毒 |
| ammo_box | 电池补给箱 | 布设3秒；交互补给2秒 | 总计400能量、每次最多100；优先补真实现存partial cell，随后新增真实cells，口袋/胸挂容量不足不扣箱；120秒过期 |

耐力针对 Energy/Hydration 的适配基于项目当前没有独立的 Hunt 近战/奔跑耐力池。重复针剂将对应buff刷新为600秒，不叠乘；相同燃烧/流血/毒状态只保留单一状态实例，不因多场重叠无限加倍伤害。毒云接触伤害与短暂中毒合并为 Moonkov 版本；不声称 Hunt 的中毒状态本身持续扣血。火只作用于可行动的活体，不燃烧倒地尸体、不扣永久生命块、不引入额外死亡结算。完整燃尸、赏金复活、猎人特质/近战工具、甲虫等不在本批。

## 玩家流程与服务器约束

- 新道具均在月尘商店有真实库存条目；医疗补给表掉落三针，装备补给表小权重掉落投掷物、三种陷阱及电池箱。保留原保证医疗/电池和掷骰预算。拾取、主动丢弃、死亡袋及合法撤离返还使用既有库存树，不重新造物资副本。
- 快捷栏由 UI 组负责：1–9只选择，主使用键发服务器意图。新工具必须在口袋或已装备胸挂直接格内；背包内要先移到可操作位置。服务器不相信客户端持有量、伤害、位置或瞄准落点。
- 布设须在服务器瞄准的2.2m近地稳定表面；同点0.65m内禁止叠放，四角支撑与净空检查阻止穿地板布设。陷阱垂直触发差<=0.65m，区域效果使用实际PhysicsScene/视线，不穿墙穿楼层。
- 所有新使用操作经过耗时/库存版本/连接/当前raid/存活可行动检查。移动超过0.55m、受伤、开火、换弹、换武器、断线、死亡、搜刮/医疗替换、库存变化或显式取消中断。完成时重新验证、同服务器线程原子消耗一次；丢失目标、无地面、超限或空间失败均不消耗。
- E交互由UI适配读取 `TacticalItemsFeedback.TryFindInteraction` 返回的TargetId和Disarm/Resupply。拆雷需3秒，销毁已部署陷阱，不返还全新物品，避免双返还或免费刷物。补给箱每次完成重新检查余量，已空箱不可被第二个同时完成者消费。
- 自救 Extinguish 需3秒，自身火/流血继续按原节奏扣血但不打断该动作；服务器只推进这些已知持续伤害的操作基线，外部命中/移动/开火仍中断。反复开始取消不能免伤，完成后才清除状态，不消耗物品。
- `RaidInventoryState.TacticalPending` 与既有库存操作互斥。请求共享 `RequestId` 单调序列；同时绑定 `RaidId`、`ExpectedVersion`、真实 `ItemId`，旧请求不能跨局/重复扣物。
- 所有爆炸/火/毒/流血调用统一 `BountyClashDamage.Apply`，遵循其友伤、Downed、终死与击杀归属规则；道具组不直接调用Leaderboard或重复扣装备。
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

## 验证记录

- `dotnet run --project Tools/TacticalItemChecks/TacticalItemChecks.csproj`：1112项已通过，含1024份医疗/装备掉落保证项与全部新道具自然获取、11物品真实可持有/使用/单次消耗/重放拒绝、库存空间与partial cell电量守恒。
- `Assets/Editor/TacticalItemsAuthorityChecks.cs` 已在隔离 ECS World + 本地PhysicsScene实际执行36项通过，覆盖真实RPC、耗时/中断、楼层/墙、抗毒/消火/清毒、铁丝限速、两玩家箱体竞争和实际扫掠投掷/爆炸。
- 后续新增生命针全可恢复组织/600秒、连续开始取消自救不免伤、同tick外伤中断、真实客户端owner回执检查，45项脚本待新稳定assembly复跑，尚不列为已通过。
- 主组阶段全项目Unity编译曾通过；之后多组持续写入出现新增跨组编译错误。道具组已经归还Editor，最终代码统一编译尚待执行。
- 首版引擎预览 `/tmp/moonkov-tactical-vfx-preview.png` 已经人工审查，不通过视觉质量验收；改进版粒子、实体外形和手持模型待实景复核，不能将首图当最终证据。
- 专属粒子资源需要运行 `TacticalItemsPresentationSetup.CreateResources()` 一次生成到 `Resources/Moonkov`，所有prefab/material/meta必须随任务提交，第三方源资产不改。
- 真实Transport两客户端、实际场景/UI操作与视觉质量尚未验收，不能将以上规则通过表述成已完成联网可玩验收。


最终复跑命令（Editor令牌内，需先统一compile成功）：

```sh
unity command eval --project-path /Users/morrow/UnityProjects/Moonkov --caller plugin --skill unity-cli --timeout 20000 'TacticalItemsAuthorityChecks.Run(); return "completed";' --format json
```

生命针/耐力针的手持物按本地选择可见；本地第三人称可绑定Humanoid右手，第一人称摄像机随使用进度抬举。远端玩家选择持物姿态尚未新增专门同步协议，不将本地持物美术表述为已经完成全远端手部动画验收。
