# 本机公网服务器更新记录（2026-10-24）

服务器是本机 `E:/UnityProjects/FPS_Template`；VPS 只提供已有公网转发入口。

- 项目 PostgreSQL 已启动，继续仅监听本机 5433。
- 更新前已完成数据库 custom archive 及私有配置备份：`LocalData/backups/online-20261024-123218/`。`pg_restore --list` 可读，dump 非空，SHA-256：`60375337672AFC3C740C08423ABCB4169BEA8C67A792871A564018B022818EC8`。备份目录 ACL 仅当前用户、SYSTEM 和管理员可访问；没有重建或清空玩家库。
- MoonPersistence 已通过原启动器重建并后台启动，监听原配置的 `127.0.0.1:5080` 和 `10.8.0.2:5080`。受服务器密钥保护的 health 包含 `game-join-tickets-v1` 及既有业务能力。此次票据不新增 schema。
- 公网账号 HTTPS `/auth/me` 无凭证从 504 恢复为预期的 401；没有使用真实玩家账号做注册、交易或领取操作。
- 本机 DTLS 身份已生成在 `LocalData/game-tls/`，公开信任文件为根目录 `moon-game-ca.pem`；信任链、域名、有效期和私钥匹配检查通过。私钥未进入 Git，也不复制给玩家。
- Unity 6000.5.10f1 Windows Dedicated Server 模块已安装。Unity CLI 位于 `D:/Tools/UnityCLI/bin/unity.exe`。只复用原有 Editor。
- 游戏服务器构建与启动状态以 `Library/BuildValidation/OnlineServer/build-report.json`、`LocalData/game-server.pid.json` 和最后的 `LocalData/online-preflight.json` 为准；账号接口正常不代表游戏 UDP 或联机对局已验收。
- 用户授权继续时，Editor 已切到没有未保存修改的 MainMenu，因此直接使用磁盘上的正式地图构建，没有覆盖并行任务的场景修改。磁盘场景另备份在 `LocalData/backups/approved-scene-20261024-133147/`。
- 修复旧 Server 配置中的 `UGS_SERVER` 触发已不存在的 `IMultiplaySessionManager` 编译错误：本机构建器为 Server 目标和 Player 构建同时选择 `MOON_LOCAL_SERVER`，Addressables/SBP 与 Player 使用同一本地启动入口；旧云专用启动脚本不在 Editor 或本机构建中编译。客户端目标配置没有修改。
- Windows Dedicated Server 构建成功，`Build/MoonServer/MoonServer.exe`，报告 `Succeeded`、0 个错误，总大小 1,135,031,258 字节。使用正式 TheophilusGameScene 与共享网络子场景，未启动额外 Editor。
- 本机 PostgreSQL 与账号后端已恢复；本轮后端 PID 为 45964。专用游戏服务器 PID 为 40880，已确认日志中的正式地图加载完成消息及该进程的 UDP 7979 监听。服务器保持后台运行，未启动任何游戏客户端。
- `Backend/Test-OnlineDeployment.ps1` 的 9 项检查全部通过：包括本机后端能力、DTLS 信任/域名/有效期/私钥匹配、公共账号 HTTPS 401，以及游戏 UDP 监听。本机监听不证明外部 UDP 转发或完整对局已验收。

代码检查已覆盖 7 项单次票据、10 项无网络驱动的 ECS 角色恢复、44 项持久化上下文检查及其既有离线恢复检查。本轮另完成专用服务器构建及无客户端的地图加载/监听检查。未进行 Play、独立游戏客户端、跨设备公网联机、Relay 云房间实测或丢包验收。旧客户端构建不能与新协议混用，仍需重新打包客户端。
