# 赏金速战服务端接口与验收记录

2026-10-21。规则契约见 `Docs/Planning/BountyClash-20261021.md`。本文件当前记录实现与验收进度，不代表完整模式已验收。

## 所有权与入口

- 服务端主组拥有 `BountyClashRules/Network/Damage`、`ServerGameSystem.BountyClash`、核心/原Raid适配、四条玩家伤害、Ghost生命/所有权同步。
- 地图拥有 `BountyClashArenaSettings.Active.Definition`，定义一份 `ClashArenaDefinition`：Center、Radius、Target、12个Spawns（team*3+slot）、2个Exits、ExitRadius8、BlockRadius15、BanishSeconds60、ReadySeconds10、ReconnectSeconds90。
- UI在World创建后、场景和Join前建立 `RaidModeState` 单例；`RaidGameMode.Expedition=0`/规则版本0，`BountyClash=1`/规则版本1。服务器校验 `ClientJoinRequestRpc.Mode/RulesVersion`，认证成功后发送 `ClashJoinAcceptedRpc`。连接方式与玩法模式分开。
- `BountyClashRuntime.Enabled(EntityManager)` 是共享模式查询。`InArena(manager,float3)` 用于权威互动边界；`CanAct(manager,Entity)` 用于等待、倒地、断线时禁止物品操作。原模式默认放行。

## 纯规则与网络

`BountyClashMatch` 只有服务器时间和纯数据，不访问数据库/场景。它拥有每局Guid `MatchId`/`TokenId`、递增Epoch、最多4队/12人、按账号索引的永久参与者身份。队伍由服务器生成不可猜的邀请字符串，客户端只发意图。

阶段为 Lobby → Preparing → Deploying → Active → Finished。至少两队全员ready，10秒准备后锁名单，生成每人的独立DeploymentId及开局TeamSize；全员真实部署成功后才开启共享900秒。Deploying最多120秒；失败/断线90秒超时终止准备，已扣装者无奖返还，未完成HTTP任务仍要等结果并耐久退款，不能直接丢弃。

- `ClashActionRpc`：MatchId/Epoch/RequestId，Intent，Value，InviteCode，TargetParticipantId。Intent为建队/邀请加入/离队/容量/ready/驱散/拾取/救援begin-renew-cancel/暗视flags/NextRound。
- `ClashSnapshotRpc`：共享时间/阶段、批准队伍/队长/邀请、人数、驱散与赏金状态、两撤离剩余及阻挡原因、越界/救援/暗视、部署取消与倒计时、允许下一局标记，以及权威目标/出口/边界。
- `ClashParticipantRpc`：同批Epoch/Sequence的名单行。只有自己/同队有姓名、健康、位置等数据；非获权行清空，避免泄露敌人精确信息。
- `ClashSenseRpc` + `ClashSenseContactRpc`：只给合法接收者的敌情，详细见下节。

所有动作按连接身份、Epoch/Match及单调RequestId验证；同tick重复Join和已有pending/ingame/reject连接早退，不重复添加拒绝组件。客户端不能传TeamId或奖金额决定服务端结果。

## 驱散、生命与撤离

驱散由近距、存活、视线有效的服务器交互开始，起手只恢复本队可恢复生命上限/伤势一次；倒地者不自动起身。60秒后生成唯一服务器赏金，倒地/死亡掉落，拾取仍需近距与视线。

Moonkov原身体总量440，按100%归一；每倒地降低25个百分点最大可恢复生命，不改变原武器伤害尺度。`PredictedPlayerGhost`新增ClashEnabled/ClashCanAct/ClashLife/ClashTeam/ClashMaxHealthFraction/ClashDarkSight。`RaidHealth.Maximum(state,part)`、Set和医疗统一夹紧上限，不能用生命针绕过。

Downed保留原实体与装备，健康0且禁止移动/开火/物品操作；服务端ControllerState、输入门禁和实际CharacterController胶囊统一为Prone，复活后由既有输入/净空姿态解算恢复；同队5秒持续救援，每0.25秒RPC续租，0.6秒无续租、受伤、离开/遮挡中断。团灭或恢复比例耗尽才终死结算。身体实际姿态与武器表现由DollSinger现有表现路径适配，不生成第二个可复制装备的角色。

- 同队普通子弹伤害0.1、爆炸/火/毒等1.0，为明确的Moonkov v1适配。
- `BountyClashDamage.Capture(World, actorEntity)` 在发射时生成服务器专用 `ClashDamageSource{NetworkId,ParticipantId,Epoch}`；延迟攻击使用 `Apply(World,victim,source,BodyPart,amount,tick,kind)`，按永久参与者识别队伍，旧epoch拒绝。原int overload仅兼容即时伤害。Auto按组织比例系统分配；投掷者后来倒地/重连不改变已释放攻击归属。Projectile服务器ResolveShooter和self过滤同样按participant+epoch，避免NetCode复用NetworkId导致错认。
- 原模式终死统一记一次击杀；Clash倒地不记，最终死亡由共享生命转换记，排除同队。
- 两撤离各30秒；活敌或同撤离阻挡区内倒地队友暂停进度，解除后继续；全队离开该出口才重置。敌方倒地不阻挡是当前实现推断，终死尸体不阻挡。
- 赏金随同一次实际撤离名单固定。带出后Deadline=min(oldDeadline,now+90)，不延长快到期的局，也不因只剩一队免费发奖。
- 越界20秒宽限后淘汰。普通箱/地面包的玩家和目标都必须在圆内，宽限不等于可搜刮圈外物资。

## 暗视权限与预算

当前参考为官方 Player Guide Step5：基础Instinct75m、增强125m、累计5秒。早期150m及token全局预算方案已撤回。

- `SenseHeld.Value` 为flags：View=1；View+Boost=3；释放=0。V进入基础暗视，V+RMB增强，0.25秒续租，服务器View/Boost分别0.6秒租约。
- 所有活人可进入基础暗视看目标，但仅当前赏金携带者有75m存在感；非携带者Presence=false、ContactCount=0。基础不消耗增强预算，耗尽后携带者仍可用75m存在感。
- 增强必须携带者+View+Boost+剩余>0，仅该连接得到125m内最多11个橙色云contact；2m量化网格加稳定小幅抖动、不确定半径3m，不含敌人身份或精确hitbox。
- 每位参与者对本局token首获有自身5秒；自己掉落重拾/重连保留剩余，新敌人首次取得有自己的5秒；新Match重置。
- contact MatchId/Epoch/Sequence与同批快照一致，ValidFor0.35秒。松开、耗尽、换持有者、过期或换局即清空；旧序号不能复活云点。SectorMask为辅助8方向×3距离档，bit=band*8+sector，北+Z为0顺时针；档位<50、<100、<=125。
- 暗视期间Ghost.ClashDarkSight使服务器和客户端抑制主枪Aim/Fire，松开恢复，不能同时右键开镜。

## 持久化、断线与再开

认证后先进Lobby，不自动扣装。名单锁定时先RegisterBountyMatchAsync，再每人BeginBountyDeploy；同区赏金提取时先BeginBountyRedemption固定整组名单，再逐人FinishRaid/BeginSave。持久化组的整组.bounty日志包含所有人的装备快照，防止已兑token却丢部分队员装备记录。月尘v1固定每合法同事件撤离者20+开局solo60/duo15/trio0=80/35/20；客户端/地图不能指定金额。

断线90秒身体留场可攻击，不能断线无敌。`BountyClashDisconnectSystem` 在NetworkStreamReceiveSystem之后、NetworkGroupCommandBufferSystem之前完成接收job，读取公开NetworkStreamConnection.CurrentState，在NetCode销毁连接的LinkedEntityGroup前把Session/Inventory/Profile/JoinedClient转到服务器专用detached holder并移除身体关联。身体GhostOwner和参与者当前NetworkId置0，销毁旧输入，避免新陌生连接复用netid暂得身体。原账号重连后移回新连接，换GhostOwner与新命令输入，不新生成装备或出战。普通模式不进入该前置迁移。连接迁移调用 `RaidPersistenceContext.RebindBountyOwner(old,new,authenticatedPlayerId,settlementId)`，只允许同玩家、同结算记录；重连认证仍保留旧回执等待门禁，先更新detached holder并重新获取已提交仓库，再完成Join。正在进行的HTTP Save.Task/Payload保留原引用，回执写入当前owner，已完成部署优先保留刚认证的fresh profile，不能用旧缓存覆盖仓库。GhostGameObject.Owner实时从有效ECS所有权读取，Role随GhostOwnerIsLocal变化，避免缓存旧owner。完整独立进程owner/observer仍须专门验证。

同连接下一共享局保留个人RaidSession，BeginNext递增RaidId/序列基线，旧局库存RPC不匹配。NextRound等待保存/部署/退款稳定，清掉模式临时物体，重新roll基础补给并提高版本、清refill及个人搜索/交互缓存，不迁移/封锁生产地图。

## 可复跑验证与当前状态

```sh
dotnet run --project Tools/Validation/BountyClashChecks/BountyClashChecks.csproj
# 仅允许专属临时loopback API配置，脚本自行创建临时账号与私有outbox，不读真实Prefs。
# 需协调Editor独占后运行；不得把同进程World检查称为独立客户端。
unity command eval 'return BountyClashTransportChecks.RunAndRecord("<temporary-server-config>");' 120000 --caller plugin --skill unity-build-validation --timeout 150 --result-only
```

- 纯规则已通过：人数/队伍准备、锁名单、epoch/request去重、共享截止、部署有界失败、账号重连、起手恢复、持续救援、近远倒地阻挡、唯一赏金、个人暗视预算、暂停/恢复/离开重置、越界/超时。
- 已通过新鲜全项目编译并加载真实协议。2026-10-21已实际运行同进程Server+3客户端生成RPC、真实ServerGameSystem、临时HTTP+PostgreSQL链路，73个检查节点通过（包含持续救援的重复续租节点，并非73个独立场景）。
- 实测覆盖：认证/邀请/准备→三人独立扣装→队伍出生→倒地不结算→驱散起手恢复→持续救援→唯一赏金/获权暗视→断线同身体可伤→真实NetworkId复用防错认→原账号同实体/新输入/旧输入销毁→永久source友伤与自过滤→阻挡暂停/恢复撤离→不可变赏金名单→延迟成功HTTP回执期间断线/新认证等待→holder Saved/fresh仓库→重连与auth/me仓库相等、solo恰好80且仅一个个人settlement POST→90秒收尾→下一局RaidId递增/旧库存RPC及旧epoch伤害拒绝。
- 结果为 `Library/BuildValidation/BountyClash/adapter-udp-result.txt`，逐节点记录为 `adapter-udp.txt`；最后一轮finally后实际确认World.All=0、原WeaponGestureLab单场景、dirty=false、Play=false，未写真实Prefs。
- 最后一轮Console无error，fixture保留RPC数据曾产生3086条MaxRpcAgeFrames测试警告；脚本现仅对客户端移除ReceiveRpcCommandRequest以消费通知、保留payload断言，服务端动作不变。这项消噪只做新鲜编译，按用户要求未再UDP复跑；上述PASS73对应消噪前的运行。未清用户Console。
- 用户因Mac内存明确取消后续UDP多客户端/MPPM验收；最后一轮已在运行中，自然通过后停止，不再重启。未进行独立进程owner/observer的真实移动射击与画面验证，不能将上述同进程适配检查称为完整联机验收。UI/效果交由后续单Editor检查。临时API/PG已释放给持久化组关闭，Windows生产后台/DB不连接、不部署。

差异必须保留：Moonkov地图/身体/武器/物品与货币适配；驱散60秒的项目选值；原版赏金救援、燃烧尸体、全部特质、猎人招募/血脉XP尚未实现。未完成项不能用“完全复刻”概括为已完成。
