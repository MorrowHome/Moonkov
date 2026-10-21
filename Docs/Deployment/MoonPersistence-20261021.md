# MoonPersistence 线上同步准备（2026-10-21）

状态：**发布准备完成，尚未上线**。没有连接生产数据库、执行生产迁移、重启生产服务或使用真实玩家账户做写入测试。

## 已确认信息与当前登录阻碍

- 用户已明确确认线上后端运行在自己的Windows PC，并授权备份、schema8增量迁移和服务重启，无需再次确认主机归属或发布权限。
- 现有公开客户端配置指向`https://moon.morrowhome.site/`，游戏地址同域名、UDP7979。域名经过Cloudflare；无凭证GET `/auth/me`和`/internal/health`返回Cloudflare403，仅作为边缘响应记录，不用于判断源站鉴权或健康，也不阻碍通过PC管理连接部署。
- 保存的Windows SSH连接使用当前默认路由时超时；只为该连接绑定校园网`en0`接口后，TCP连接成功、SSH严格主机密钥校验通过。没有关闭VPN或修改全局路由，不能仅凭此前超时认定VPN是唯一原因。
- 当前阻碍已缩小为SSH认证：服务器拒绝本机现有公钥，SSH agent无可用身份，主机专属钥匙串条目未找到。没有猜测密码、修改Windows权限或远程写入。继续需要可用的既有SSH身份或已认证管理会话；不要在Git或聊天中提交密码/私钥。
- 仓库记录的运行方案是Windows上的MoonPersistence/.NET10与PostgreSQL，经VPS反代/WireGuard暴露HTTPS。登录后读取实际服务目录、运行参数和受保护配置即可继续备份与发布；文档占位路径不作为实际部署路径。

## 已完成的发布准备

- 修改`Backend/Start-Backend.ps1`：已有健康进程的提前返回分支，以及新启动进程的就绪分支，都必须包含`species-harvest-v2`、`missions-v1`、`recovery-inbox-v1`。旧服务不能仅凭ready状态被报告为已同步。校验位于仅处理网络探测的catch之外，缺能力会明确报错。
- .NET10 Release发布成功。使用`-p:UseAppHost=false --no-self-contained`输出可移植DLL，避免把Mac ARM64 apphost误部署到Windows/Linux；目标仍须已安装兼容的ASP.NET Core/.NET10运行时。
- 准备包位于`/tmp/moonkov-backend-release-9105d91f-portable`，`release-manifest.json`记录来源提交、构建、要求能力和逐文件SHA256。不含生产`appsettings.local.json`、数据库配置或服务器密钥。
- 应用构建：`1.0.0+9105d91fd60e329cbfe8ce43016462df9e412982`。这是本次部署准备提交之前的develop源码；之后新增的准备改动是启动门禁与文档，不代表该包已上传。真正发布前应从届时develop HEAD重建最终包并重新记录其构建号。
- `MoonPersistence.dll`：177664字节，SHA256 `5672df4b82fb4b278db14dad87a1a07d743d957ceb7898c33348de6b5368c8dc`。
- `schema.sql`：6760字节，SHA256 `492844e68fbc80681efa0e3d13fa12c195ee54526e38537e881d6996bf55dcd8`，包含增量schema8。
- 静态核对要求能力与应用health清单一致，检查两个就绪分支且错误不被探测catch吞掉；当前Mac没有PowerShell，未声称运行过Windows启动器。本次未启动任何本机替代后端或测试数据库，也未操作Unity。

## 获得实际管理连接后的发布步骤

1. 在用户确认的Windows PC上只读核对后端进程/服务和配置路径、数据库身份、已有构建与schema，按实际运行参数部署，不使用文档占位路径。
2. 先在生产主机受保护目录保留旧程序、配置和运行参数，再对正确生产数据库做一致性备份；检查备份非空、校验值和`pg_restore --list`可读。备份、密码和服务器密钥不进入Git或发布包。
3. 从当前develop构建最终Release包，暂存至新目录、保留生产配置；在维护或受控切换期间启动验证。应用启动通过既有事务和迁移锁执行仅新增待领取表的schema8，不删除或重置玩家表。
4. 验证origin带服务器密钥的`/internal/health`为ready、构建号匹配、包含`recovery-inbox-v1`，并直接只读核对`schema_version`含8与`inventory_recovery`表存在。无凭证的受保护health与`/auth/me`、`POST /auth/inventory/recovery/claim`应拒绝访问；不要用真实玩家账号验证领取。
5. 确认健康后恢复流量，记录结果。失败时优先恢复旧程序/运行参数并保留所有备份；若新版本已经写入Recovery数据，不得降级到忽略待领取区的旧后端或盲目恢复旧数据库快照，应保留新数据和部署门禁并修复服务。未完成这些检查不能把状态改为“线上同步完成”。
