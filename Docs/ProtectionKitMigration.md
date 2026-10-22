# 生命维持头罩的一次性迁移

2026-10-22。共用纯逻辑`ProtectionKitMigration.TryApply`由线上和离线库存入口调用，保证旧档和新profile能获得一次生命维持装备，不按登录、出战或死亡循环免费补发。未连接Windows或生产数据库。

- `ProtectionKitVersion>=1`不再发放。已装备兼容`helmet`或`energy_head_dome`时只记录版本，原物品ID、剩余氧气/护盾/能源不替换、不补满；0保留为0，只有物品自身的缺省-1由装备系统首次初始化。
- 否则生成一件`energy_head_dome`：优先空Helmet槽且不超携带重量，然后仓库；没有普通容量时尝试现有Recovery空Helmet/可容纳容器，再尝试一个新批。已有装备和物品不被覆盖，不提高普通MaxItems或Recovery最多2批。
- 如果两处库存全部无法容纳，输入完全不变、版本保持0，待领取/出售整理后再尝试。只有物品实际落地或已装备兼容Helmet时，才记录版本1。
- 任何Open出战都暂缓迁移，避免出战暂时从账号树移走头盔就触发额外赠送；原装备返还或损失结算关闭出战后再处理。

线上`ReadGraphAsync`已持有玩家行锁，新增窄钩子在同一原事务读取Recovery、迁移并写回graph和inbox。并发请求最终只有一次已提交的迁移版本；异常沿原事务回滚。没有增加数据库列或版本，没有修改HTTP客户端写入协议。

离线在读取/原Commit中处理同一纯helper，装备、Recovery和迁移版本由原文件Flush+Replace一起落盘；同时使用已有`Receipts["migration/protection-kit-v1"]="1"`。旧writer即使丢掉新Graph字段，仍保留这个既有收据字典，新reader只恢复版本1，不再给已经丢失的装备。无需Format4；原Format2和离线Clash Format3规则保持。

`InventoryItem`三项资源字段随既有MemberwiseClone与JSON保存；Graph.Clone复制版本由装备组负责并已静态核对。运行时移出/掉落/死亡/整组赏金快照前的Ghost资源Flush由服务器与装备组接入，本迁移不代替运行时资源验收。

## 必要验证

`dotnet run --project Backend/BountyClashContextChecks/BountyClashContextChecks.csproj --no-restore -- --protection-migration-only`

22项实际通过：新profile只发一次、耗尽旧Helmet保持ID/氧气0、ShieldCharge0/ShieldEnergy17经Clone和JSON保留、超重落仓库、Max2048转待领取、两批全满不消耗版本/不变输入、腾空后同两批续迁且原160dust不丢、离线原子收据、重开不重写/重复赠送、出战期间不赠、死亡与重开不再赠、旧writer丢Graph版本后仍由原收据阻止重复。

文件夹具只在`Library/BuildValidation/ProtectionMigration/kit-唯一GUID`创建并finally清除，Prefs为内存桩；真实用户档和两处已有字体修改未动。迁移组只运行这套必要专项和Backend编译，不重跑40项离线赏金检查或全库回归。昨日专属临时PG已清，本轮没有为此重建数据库；线上原锁事务路径为静态核对，真实SQL迁移未在本轮运行。Unity后续统一编译与装备画面/资源实际行为由根串行排期，不能把这22项写成运行时护盾/氧气已验收。
