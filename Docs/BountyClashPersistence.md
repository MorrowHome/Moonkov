# Bounty Clash 持久化接口与验证

2026-10-21。此说明记录后端与持久化桥接；编译/专项通过不代表多人场景、界面和完整玩法已验收。用户的Windows服务与数据库仍关闭，本轮未连接或迁移生产环境。

使用原账号、自备装备扣库、死亡损失、整树返还和待领取。单人存档不开放Bounty Clash，不改变离线Format2。客户端、Unity服务器和Backend需同步更新；内部health新增`bounty-clash-v1`，启动器已运行提前返回和新进程就绪都检查它。独立模式路径使旧后端返回404，不能因忽略新JSON字段而扣普通出战装备。新版普通端点也拒绝带模式绑定、或剥掉绑定后使用已登记的模式出战ID。

## 协议与事务

所有`/internal/bounty-clash/*`均需要服务器密钥，不接受玩家Bearer代替。客户端只发送操作意图；TeamId、完整名单、装备快照、撤离事件由可信游戏服务器生成。

| 端点 | 请求和行为 |
| --- | --- |
| `POST /matches` | `MatchId, RulesVersion=1, TokenId, Participants[{PlayerId,DeploymentId,TeamId}]`；锁定2–4队、2–12人、每队1–3人、TeamId0–3。同名单重试允许，重排不改变含义，改变名单/版本/Token冲突。 |
| `POST /deployments` | `PlayerId, DeploymentId, Cells, Mode="BountyClash", MatchId, RulesVersion, TeamId`；必须对应登记名单；原扣库事务保存真实自备装备，重试返回相同图，不再扣装。 |
| `POST /redemptions` | `MatchId,TokenId,ExtractionEventId,CarrierDeploymentId,ExtractedDeploymentIds[]`；携带者必须在实际同次撤离名单中，所有人同队且出战Open；每局唯一Token/事件，一次确认固定名单。金额由后端计算，不接受客户端金额。 |
| `POST /settlements` | 原每人独立`SettlementId=DeploymentId`及Outcome、真实携带图、资源合计，再带模式/战局/版本/TeamId和可空`BountyEventId`。获奖者必须Extracted且事件匹配；同事务保存收据、领取其一份赏金、返还装备/待领取、关闭出战。重复不发奖，冲突拒绝。 |
| `POST /deployments/refund` | 仅处理部署失败/中止；已扣但未生成角色时用后端保存的初始装备图返还，无赏金、无任务首次奖励；未扣时写Cancelled阻止迟到请求再扣装；重复退款不复制装备。 |
| `POST /deployments/abandon` | 已开始战斗且无成功结算的丢失恢复，沿用死亡损失；不能用于部署中止退款。 |

schema9仅增量添加：不可变match/participant名单、全局唯一redemption、每人award收据，以及原部署/结算的Mode、MatchId、RulesVersion、TeamId、BountyEventId元数据。旧记录默认Expedition；旧收据精确重试保留。不能将每人DeploymentId误用为全局MatchId，也不能只用玩家锁代替唯一赏金事务的match锁。

## 月尘与待领取

Moonkov货币适配v1：每位实际与赏金携带者同次有效撤离者基础20月尘；按**开局固定队伍人数**，单人额外60、双人额外15、三人无额外，即每人80/35/20。不按当前幸存人数冒领加成，不给其它撤离事件的同队者发奖。不是Hunt原货币/XP数值；此前200/600/150草案按本项目20/堆物理月尘与待领取容量缩放为1:10。

配置在`BountyClash:BaseDust/SoloBonusDust/DuoBonusDust`，默认20/60/15。登记match时冻结配置，之后重启或改配置不改变该局奖额。v1配置最高个人80，超出启动拒绝。赏金是服务端目标裁决，未注册为普通库存物品；拾取/救援/倒地不发奖。

返还调用`RecoveryInventory.Settle(...,missionRewards:false,bonusDust:...)`，不触发MC00首次搜刮/任务奖励；不接受该模式新生成的未回收MC00任务物品。原Normal `MaxItems=2048`、最多2批Recovery不变；满仓80月尘最多4堆，第二个合法奖励图可容纳，第一图保留原cargo。原领取/双版本/请求收据继续适用，未领取阻止下次出战。

## Unity桥接与恢复

`RaidPersistenceContext`提供`ClashSupported`、`RegisterBountyMatchAsync`、显式DeploymentId的`BeginBountyDeploy`和`BeginBountyRedemption`。模式Join只认证并进入服务端lobby，不立刻扣装。部署在登记完成后才写`.deploy`并请求；成功后主组`ClashDeploymentAllowed`复核，不允许再生成角色时按无奖Extracted返还。连接消失和关闭World时都等待已发起的扣库结果，使用`.refund`耐久退款意图，避免迟到成功被遗忘。

赏金撤离先在所有Profiles/Inventories仍存在时捕获整组不可变快照，原子写`.bounty`（含整组每人的完整Settlement），之后才发兑付/个人结算。个人`.json`也保留到确认；重启先重放组日志，再个人收据，再退款/丢失出战。即使崩溃时只有组日志，也能恢复所有队员；不是对尚未持久化整张战局的恢复承诺。模式日志不能交给旧版服务器处理，含新模式数据后不得盲目回滚服务器/后端。

`RebindBountyOwner`仅供服务端认证后或已认证连接断线时调用；按PlayerId、同SettlementId和旧owner校验后，迁移Profiles/Inventories与pending Save.Connection，不改变或重发原Task/payload。Detached与重连接入由主组调用，真实ECS延迟ack回归另行验收。

普通模式新增字段均省略默认值，保持旧离线收据JSON哈希兼容；未更改已有真实存档/PlayerPrefs。`PlayerProfileClient.LocalPreferenceScope`供快捷栏使用：在线由已验证PlayerId与服务URL哈希，离线由实际档案路径哈希；未验证/切换服务/登出为空，不用DisplayName或Token，也不是所有权凭证。

## 可复现验证

- `dotnet run --project Backend/BountyClashPersistenceChecks/BountyClashPersistenceChecks.csproj -- "Host=127.0.0.1;Port=临时端口;Username=moon_clash_test;Database=moon_clash_check_唯一后缀"`：只接受显式loopback专属临时库，创建并保留它供检查，操作者负责销毁临时集群。57项：schema8→9保留库存、登记/模式剥除/跨账号/跨回合/跨队拒绝、并发唯一Token、solo/duo/trio开局人数、满仓80+cargo两批与Claim幂等、退款/迟到扣装拒绝。其中14项启动真实独立HTTP进程，覆盖注册→session→登记→扣装→兑付→提取/死亡→重试→账号刷新，以及所有新入口鉴权/health。HTTP进程在finally停止。
- `dotnet run --project Backend/BountyClashContextChecks/BountyClashContextChecks.csproj`：43项，包括实际源文件的普通收据字节、旧Format1/2离线14项、故障留日志/只剩组日志整组重放、旧后端能力拒绝、未完成扣库关闭后退款、真实AccountClient/PlayerProfileClient账号/服务/离线scope隔离，以及结算HTTP延迟ack时旧连接→holder→重连的同任务/同payload迁移、错误身份/结算/目标账号拒绝、完成后优先最新认证仓库。最小Unity类型与**内存PlayerPrefs**桩仅用于.NET测试，不代表真实Unity/Transport；DTO反序列化字段会有CS0649警告，未抑制它们。
- `dotnet run --project Backend/RecoveryChecks/RecoveryChecks.csproj`：原Recovery纯规则23项通过，普通上限/原首次奖励行为回归。

本轮数据库使用第三方Zonky Maven `embedded-postgres-binaries-darwin-arm64v8:17.6.0`，临时目录、随机loopback端口、专属测试身份，不安装全局服务。另保留一套临时API/独立数据库给主组真实多人Transport验收；配置与密钥仅在0600临时文件，不入Git，验收结束后停止并删除。具体多人/界面验收由主组及协调记录，不能据本专项宣称完整玩法完成。
