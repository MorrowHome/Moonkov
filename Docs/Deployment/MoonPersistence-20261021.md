# MoonPersistence 代码同步与发布准备（2026-10-21）

状态：**代码已同步，发布准备完成，实际PC尚未重建或迁移**。用户确认线上后端在自己的Windows PC，服务器与数据库当前都未开启；本次不再进行远程连接、认证或启动服务。

## 已完成与验证

- `Backend/Start-Backend.ps1`在已有健康进程的提前返回分支、新进程就绪分支都检查`species-harvest-v2`、`missions-v1`、`recovery-inbox-v1`。旧后端缺少能力时明确报错，不会只凭ready响应报告同步完成；能力检查位于网络探测catch之外。
- .NET10 Release可移植发布成功：`dotnet publish Backend/MoonPersistence/MoonPersistence.csproj -c Release --no-self-contained -p:UseAppHost=false`。产物不包含Mac apphost、生产配置或密钥，Windows需已有兼容的ASP.NET Core/.NET10运行时。
- 准备包：`/tmp/moonkov-backend-release-747d3be2-portable`。`release-manifest.json`记录来源提交、构建、schema8、要求能力及逐文件SHA256；临时目录不是长期发布仓库。
- 来源提交：`747d3be255ba3d1850809258f3cd18f75af4d1c5`；应用构建：`1.0.0+747d3be255ba3d1850809258f3cd18f75af4d1c5`。本记录之后的修改仅为文档，不改变应用代码。
- `MoonPersistence.dll`：177664字节，SHA256 `da327ff43444ebcc741ed049c0a41b6624a1f020eb20be4b8fef0b40a95fd471`。
- `schema.sql`：6760字节，SHA256 `492844e68fbc80681efa0e3d13fa12c195ee54526e38537e881d6996bf55dcd8`，包含增量schema8。
- 静态核对两个启动门禁与health能力列表一致，`git diff --check`通过。当前Mac没有PowerShell，未运行Windows启动器。没有连接生产数据库、执行生产迁移、重启服务、使用真实玩家账户写入或操作Unity。

## 后续在Windows PC启动

1. 同步`develop`，保留已有`moon-server.local.json`、`Backend/MoonPersistence/appsettings.local.json`和数据库配置。在更新前备份正确生产数据库、旧程序、配置与运行参数；验证备份非空、校验值及`pg_restore --list`可读，备份与密钥不进入Git。
2. 使用现有启动器：`powershell -NoProfile -ExecutionPolicy Bypass -File Backend/Start-Backend.ps1 -Background -Update`。原服务使用`-Lan`时仍需保留该参数。启动器启动数据库并重建后端；应用初始化通过既有事务和迁移锁执行schema8，新增待领取表，不删除或重置玩家数据。
3. 启动后核对带服务器密钥的`/internal/health`为ready、构建号匹配且包含`recovery-inbox-v1`，只读确认schema8与`inventory_recovery`表存在；无凭证的受保护接口应拒绝访问，不使用真实玩家账号试领。
4. 若启动失败，保留备份与新数据后排查。新版本一旦写入Recovery数据，不得降级到忽略待领取区的旧后端或盲目恢复旧数据库快照。实际PC完成启动与验证之前，不能标记为运行态上线完成。
