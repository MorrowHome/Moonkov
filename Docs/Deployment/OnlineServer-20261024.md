# 本机公网服务器更新记录（2026-10-24）

服务器是本机 `E:/UnityProjects/FPS_Template`；VPS 只提供已有公网转发入口。

- 项目 PostgreSQL 已启动，继续仅监听本机 5433。
- 更新前已完成数据库 custom archive 及私有配置备份：`LocalData/backups/online-20261024-123218/`。`pg_restore --list` 可读，dump 非空，SHA-256：`60375337672AFC3C740C08423ABCB4169BEA8C67A792871A564018B022818EC8`。备份目录 ACL 仅当前用户、SYSTEM 和管理员可访问；没有重建或清空玩家库。
- MoonPersistence 已通过原启动器重建并后台启动，监听原配置的 `127.0.0.1:5080` 和 `10.8.0.2:5080`。受服务器密钥保护的 health 包含 `game-join-tickets-v1` 及既有业务能力。此次票据不新增 schema。
- 公网账号 HTTPS `/auth/me` 无凭证从 504 恢复为预期的 401；没有使用真实玩家账号做注册、交易或领取操作。
- 本机 DTLS 身份已生成在 `LocalData/game-tls/`，公开信任文件为根目录 `moon-game-ca.pem`；信任链、域名、有效期和私钥匹配检查通过。私钥未进入 Git，也不复制给玩家。
- Unity 6000.5.10f1 Windows Dedicated Server 模块已安装。Unity CLI 位于 `D:/Tools/UnityCLI/bin/unity.exe`。只复用原有 Editor。
- 游戏服务器构建与启动状态以 `Library/BuildValidation/OnlineServer/build-report.json`、`LocalData/game-server.pid.json` 和最后的 `LocalData/online-preflight.json` 为准；账号接口正常不代表游戏 UDP 或联机对局已验收。
- 当前构建已进入 Unity 原生 `Scene(s) Have Been Modified` 保存确认。正式场景含并行任务未保存修改；已保存构建副本，但 Unity 仍要求决定是否保存原场景。在用户确认之前没有自动保存原场景，游戏服务器尚未启动。

代码检查已覆盖 7 项单次票据、10 项无网络驱动的 ECS 角色恢复、44 项持久化上下文检查及其既有离线恢复检查。未进行 Play、独立游戏客户端、跨设备公网联机、Relay 云房间实测或丢包验收。客户端和游戏服务器必须重新构建为同一版本。
