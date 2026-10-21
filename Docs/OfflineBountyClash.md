# 正式离线赏金速战持久化

2026-10-22。单人赏金速战使用现有独立离线仓库与真实离线PlayerId，不是临时练习。人类自备装备出战会实际扣除，死亡/超时损失携带装备，合法撤离返还战利品；人类实际携标撤离获得80月尘。AI没有profile、假账号或真实connection，不写入离线存档，也不领取赏金。AI带标离场后，人类无标撤离只保留自己的实际战利品。

## 门禁和调用

服务器World必须同时具备唯一`OfflineRaidMode`、`ClashSoloMode`以及BountyClash模式。`ServerGameSystem.Persistence`使用`ClashSoloRuntime.Enabled`驱动`RaidPersistenceContext.SetClashSoloEnabled(bool)`；只有真实OfflineProfileStore存在时可启用本地能力。移除本地门禁后禁止新登记、出战和新提取，原线上HTTP能力检查、真实多人名单/两队要求不变，未改Backend服务。

主组沿用`RegisterBountyMatchAsync`、`BeginBountyDeploy`、`BeginBountyRedemption`和`BeginSave`。本地登记仅接受当前离线档的一个真实GUID，机器人不在Participants内。真人固定单人队，主组拒绝单人模式的建队/换队/扩队意图。机器人携标时主组不调用真人兑奖；真人携标时只有该真人DeploymentId进入兑奖名单。

出战与结算仍使用同一个个人DeploymentId/SettlementId，MatchId和TokenId独立。真人实例化之前等待持久化扣装成功；部署中止使用原始装备快照退款并留下取消记录，迟到出战不能再次扣装。终局沿用原库存树、弹药/电量、Recovery容量和请求收据；模式返还禁用MC00首次奖励，不从客户端金额或普通赏金物品铸造收益。

## 档案兼容和幂等

- 新reader读取Format1/2/3。没有使用过离线赏金速战的档案继续写Format2；首次登记本地Clash才升级Format3，原库存和Recovery不改格式、不提高容量。旧程序拒读Format3，避免忽略唯一赏金收据后覆盖文件。
- Format3新增紧凑本地Clash ledger：只记录真人的不可变Match/Token/规则版本/Team/Deployment绑定、Registered/Open/Closed/Cancelled状态、唯一兑奖事件和是否已随结算领取。机器人身份及负载不进入ledger。
- 重复登记/扣装返回原绑定/装备，改变绑定拒绝。新回合不能复用旧Token或Deployment；被新登记取代的未出战旧回合取消。Mode被剥除、旧epoch、跨账号、不同Carrier/Event或重复奖额均不能重新扣装或发奖。
- 赏金事件登记与最终入库分开，但最终领取标记、装备/Recovery、收据和关闭出战在同一次原子档案替换中提交。只有同事件的Extracted能领取一次80；Dead、TimedOut、无标提取不发赏金。
- 原`.bounty`整组快照和`.json`个人收据仍先落盘后提交。普通离线加载器在选择新模式前，可以恢复**已登记且归属当前真人**的旧Clash收据；这不启用新Clash玩法，也不允许凭日志新建虚假名单。未记录成功提取的中断出战按原损失恢复；未知版本/损坏文件原样保留，不重置真实档案。

## 验证证据与边界

`dotnet run --project Backend/BountyClashContextChecks/BountyClashContextChecks.csproj --no-restore -- --offline-solo-only`

40项实际隔离文件检查通过：真实离线GUID、人类唯一名单、Format2→3、真实手枪/电池扣除与原树返回、死亡含口袋战利品损失、提取80仅一次、无标/AI获标不奖、重复/旧epoch/假身份拒绝、已扣和未扣部署退款、显式本地Context能力开关及完整登记/出战/兑奖链路、关闭重开、只有`.bounty`日志的无marker恢复、未完成出战损失、未知格式原文件保留。夹具位于`Library/BuildValidation/OfflineClash/solo-唯一GUID`，finally删除自身目录；没有读取真实离线档、真实PlayerPrefs或连接线上数据库。普通Context检查段43项在初次进入新专项前均已走过；首轮新专项死亡断言误把口袋1dust算成仓库资产，仅修正测试预期，生产损失规则未改。

首轮全项目Unity编译由服务器主组通过。以上40项是实际源文件在.NET下的持久化/桥接检查，使用最小Unity类型和内存Prefs桩；不据此宣称已完成正式地图导航、聪明AI实战或真实入口/画面验收。后续只允许根排期的一个Editor、本地Server和一个真实Client验收，不启动多实例或线上服务。两处用户已有字体修改不属于本任务。
