# 后端检查：隔离运行与验证边界

## 离线检查

在项目根目录运行（Python 3，无额外依赖）：

```powershell
python Backend/CheckHarnessStaticChecks.py
```

这是源码漂移检查，覆盖商店货单字段、隔离保护、清理 SQL 的外键顺序与限定范围、失败时不输出 PASS。
它不执行 C#、PowerShell 或 SQL，不能替代 .NET 编译或真实数据库验收。

## Smoke-Test 的运行方式变更

`Backend/Smoke-Test.ps1` 不再连接 `moon-server.local.json` 指向的现有服务，也不再清理 `moonkov` 项目库。
不带参数运行会拒绝执行。需要提供一个事先建立的、全新的空测试库：

```powershell
$testDb = 'moon_smoke_check_' + [Guid]::NewGuid().ToString('N')
$testDb
```

请由有建库权限的人在项目本机 PostgreSQL 实例上创建这个名称的数据库，所有者设为
`Backend/MoonPersistence/appsettings.local.json` 中的应用账号（默认 `moonkov_app`）。
使用空模板 `template0`；不要从真实项目库复制数据，不要覆盖已有库，不要给应用账号增加超级用户或建库权限。
建库示例模板，需由操作者核对实际库名、账号和实例后自行执行：

```sql
CREATE DATABASE "moon_smoke_check_这里替换为生成的32位小写十六进制字符串"
    OWNER "moonkov_app" TEMPLATE template0;
```

脚本本身不建库、不删库、不读取管理员密码。它使用已有本机配置中的应用账号，要求显式的
`Host`、`Port`、`Username`、`Password` 字段；Host 必须为 loopback，Port 必须与
`LocalData/database.local.json` 一致。连接字符串别名不会自动推断。为保证仅监听 loopback，脚本拒绝 appsettings 文件及继承环境中的 Kestrel 覆盖项，并拒绝环境中的 ContentRoot 重定向；应使用干净的测试 shell。需要 Windows PowerShell、
.NET 10 SDK、已运行的本机 PostgreSQL 和配置中的 `psql.exe`。

完成上述人工建库后，在同一个 PowerShell 会话中运行：

```powershell
powershell -NoProfile -File Backend/Smoke-Test.ps1 -IsolatedDatabase $testDb
```

脚本依次：

1. 拒绝默认项目库、不符合命名规则的库、非本机地址、非应用账号所有的库、已有用户关系或有其他连接的库。
2. 构建 MoonPersistence，在随机 loopback 端口启动自己的服务，注入测试库连接和本次独有的内部密钥。
3. 在该服务上检查注册、登录、退出、仓库持久化、幂等、冲突、并发结算和死亡不奖励。
4. 停止自己创建的服务，按本次生成的用户名及已知玩家 ID 清理该测试账号。注册成功而资料读取失败时，也可通过该用户名找到测试账号。
5. 仅在检查和清理都成功后输出 PASS。清理错误会使脚本失败；测试与清理都失败时同时报告。

清理在一个事务内按外键顺序删除八张依赖表及玩家行；`stashes` 是兼容视图，不执行 DELETE。
包括 `legacy_stashes_v2`、`inventory_stacks`、`inventory_profiles`、`inventory_trades` 和战局记录。
每条 DELETE 仅匹配经过身份检查的本次测试账号，不清空表，不修改生产 schema。

运行后保留测试库及空业务 schema、基础物品定义、schema 版本，供检查使用；不会删除整个数据库。
下一次运行需要新的空测试库。旧测试库由操作者确认名称和内容后自行处置。
进程被强制终止、电脑关机等情况不能保证 finally 执行；此时只检查本次明确命名的测试库。

## 合并前仍须完成的运行验收

以下是真实环境验收清单，不表示已经执行：

- .NET 10 编译 `Backend/ShopChecks` 和 `Backend/MoonPersistence`。
- 运行 ShopChecks，确认六项当前货单（含 sniper、medkit）的名称、买价、卖价一致；缺项、重复 code、错误价格应失败。
- 在按上述流程准备的空测试库运行 Smoke-Test，确认 PASS 后测试玩家及所有依赖记录为零。
- 验证缺少参数、`moonkov`、非本机配置、已有数据的库、不同所有者的库均被拒绝。
- 在独立测试库中准备目标及对照玩家，并给目标填充全部八张依赖表（含 settlement → deployment 外键）；运行清理 SQL 后目标被删除，对照玩家的所有记录不变。
- 测试身份不匹配、任意 DELETE 失败时事务整体回滚；注册成功但资料解析失败时按本次用户名回收。
- 模拟 psql 清理失败和后端启动失败，确认进程退出、没有误报 PASS，且无现有服务/项目库变化。

现有 ShopChecks 使用独立临时数据库并自行创建/删除它，仍沿用它原有的本地权限要求。
不要把 ShopChecks 当作无需数据库权限的离线检查。
