# 用 VPS 中继自建服务器

让玩家连你 **家里那台机器**上跑的游戏服务器，而你家里没有公网 IP、不想动路由器端口映射时，
用一台有公网 IP 的 VPS 做中继。玩家连 VPS 的域名，流量经隧道回到你家。

本文对应 Debian / Ubuntu 的 VPS + Windows 的家用机。

## 为什么是这套结构

两个服务要暴露，协议和约束不一样：

| 服务 | 协议 | 端口 | 约束 |
| --- | --- | --- | --- |
| 游戏服务器 | **UDP** | 7979 | Unity Transport（`com.unity.netcode` 6.5.0 = Netcode for Entities）走 UDP，中继必须支持 UDP |
| 账号 / 持久化 | **TCP + TLS** | 443 → 5080 | `AccountServiceSettings.ValidateAddress` **强制 HTTPS**，见下 |

两条代码事实决定了方案：

- `Assets/Scripts/Networking/GameConnection/GameConnection.cs:109` 里服务器监听
  `NetworkEndpoint.AnyIpv4`，也就是 `0.0.0.0:7979`。**不用改代码**，隧道一连通就能收到包。
- `Assets/Scripts/Networking/Client/AccountServiceSettings.cs:41` 只接受 `https://`，
  或者 `http://` 且**回环地址**，或者显式打开 `AllowLanHttp` 后的**私有网段 IP**。
  DNS 域名走 HTTP 被专门挡掉（注释写明"域名不能把 HTTP 例外变成公网访问"）。
  所以对远程玩家，**TLS 反代是客户端的硬性要求**，不是优化项。

```
玩家
 ├─ UDP 7979 ───────────────► VPS 公网 IP ──┐
 │                                          │ wg0 隧道
 └─ HTTPS 443 (你的域名) ──► VPS 上的 Caddy ─┘
                                            │
                            你家机器 10.8.0.2 ├─ 游戏服务器 UDP 0.0.0.0:7979
                                             └─ MoonPersistence HTTP 10.8.0.2:5080
```

## 约定

下文用这些占位值，替换成你自己的：

| 占位 | 含义 | 示例 |
| --- | --- | --- |
| `203.0.113.10` | VPS 公网 IP | 你的 VPS |
| `moon.example.com` | 指向 VPS 的域名 | 你的域名 |
| `10.8.0.1` | VPS 在隧道里的地址 | 固定 |
| `10.8.0.2` | 你家机器在隧道里的地址 | 固定 |
| `ens3` | VPS 的公网网卡名 | **先查，不是 eth0** |
| `51820` | WireGuard 端口 | 可改 |

先确认网卡名：

```bash
ip -br a        # 找到有公网 IP 的那个，下面所有 ens3 都换成它
```

## 1. VPS：装 WireGuard 并生成密钥

```bash
apt update && apt install -y wireguard iptables-persistent conntrack
cd /etc/wireguard
umask 077
wg genkey | tee vps.key | wg pubkey > vps.pub
wg genkey | tee home.key | wg pubkey > home.pub
cat vps.pub home.pub     # 两个公钥，下面要用
```

`home.key` 是**你家机器**的私钥，待会要拷过去；拷完就从 VPS 上删掉。

`/etc/wireguard/wg0.conf`：

```ini
[Interface]
Address    = 10.8.0.1/24
ListenPort = 51820
PrivateKey = <vps.key 的内容>

[Peer]
# 你家机器
PublicKey  = <home.pub 的内容>
AllowedIPs = 10.8.0.2/32
```

```bash
sysctl -w net.ipv4.ip_forward=1
echo 'net.ipv4.ip_forward = 1' > /etc/sysctl.d/99-wg-forward.conf
systemctl enable --now wg-quick@wg0
wg show                       # 应看到 interface wg0 与一个 peer
```

## 2. VPS：转发 UDP 7979 到你家

`ip_forward` 不开的话 DNAT 会静默失败，这是最常见的坑。

```bash
IFACE=ens3          # ← 换成第 0 步查到的网卡名
HOME=10.8.0.2

# 进站 7979/UDP 改写到隧道里的你家机器
iptables -t nat -A PREROUTING -i $IFACE -p udp --dport 7979 -j DNAT --to-destination $HOME:7979

# 你家机器的回包要原路回 VPS，所以做源地址伪装。
# 副作用：游戏服务器看到的所有玩家都是 10.8.0.1，拿不到真实玩家 IP。
iptables -t nat -A POSTROUTING -o wg0 -j MASQUERADE

iptables -A FORWARD -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
iptables -A FORWARD -i $IFACE -o wg0 -p udp --dport 7979 -d $HOME -j ACCEPT

netfilter-persistent save     # 重启后仍在
```

放行端口（还有云厂商控制台里的安全组，两处都要）：

```bash
ufw allow 51820/udp     # WireGuard
ufw allow 7979/udp      # 游戏
ufw allow 80/tcp        # Caddy 申请证书
ufw allow 443/tcp       # 账号服务
```

## 3. 你家机器：接上隧道

Windows 装 WireGuard 官方客户端，新建隧道 `moon-home`：

```ini
[Interface]
Address    = 10.8.0.2/24
PrivateKey = <home.key 的内容>

[Peer]
PublicKey  = <vps.pub 的内容>
Endpoint   = 203.0.113.10:51820
AllowedIPs = 10.8.0.0/24
PersistentKeepalive = 25
```

两个必须注意的点：

- **`AllowedIPs` 只能是 `10.8.0.0/24`**。写成 `0.0.0.0/0` 会把你家机器的**全部**上网流量
  绕去 VPS，等于自毁。你还要在这台机器上玩游戏，这条尤其致命。
- `PersistentKeepalive = 25` 用来把家里 NAT 的映射保持打开，否则对端会连不上你。

然后放行 Windows 防火墙的入站（WireGuard 虚拟网卡通常被归为 Public 配置文件，只开内网
规则往往不够，直接放行全部配置文件）：

```powershell
New-NetFirewallRule -DisplayName "Moonkov game UDP 7979" -Direction Inbound -Protocol UDP -LocalPort 7979 -Action Allow
New-NetFirewallRule -DisplayName "Moonkov account TCP 5080" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow
```

验证隧道：

```powershell
ping 10.8.0.1
```

## 4. 账号后端：绑到隧道地址

`Backend/MoonPersistence/Program.cs:10` 从配置读监听地址，默认 `http://127.0.0.1:5080`，
**只绑回环，VPS 连不到**。改 `Backend/MoonPersistence/appsettings.local.json`：

```json
{
  "Urls": "http://10.8.0.2:5080"
}
```

改完重启后端。可以从 VPS 验证：

```bash
curl -sv http://10.8.0.2:5080/ -o /dev/null
```

有 TCP 响应（哪怕是 404）就说明隧道和绑定都对了。

## 5. VPS：Caddy 做 TLS 反代

```bash
apt install -y caddy
```

`/etc/caddy/Caddyfile`：

```
moon.example.com {
    reverse_proxy 10.8.0.2:5080
}
```

```bash
systemctl reload caddy
```

Caddy 会自动申请并续期 Let's Encrypt 证书，前提是**域名的 A 记录已指向 VPS**、
且 80/443 对外可达。它会以 `https://moon.example.com` 对外提供服务，
满足 `ValidateAddress` 的 HTTPS 要求。

## 6. 客户端配置

玩家端的账号地址从**可执行文件旁边的** `moon-client.local.json` 读
（`AccountServiceSettings.Configure`：路径是 `Application.dataPath` 的上一级）。
在编辑器里 `Application.dataPath` 是 `<项目>/Assets`，所以要放**项目根目录**；
打包后放在 exe 旁边。

```json
{
  "AccountServiceUrl": "https://moon.example.com/",
  "AllowLanHttp": false,
  "ServerAddress": "moon.example.com",
  "ServerPort": 7979,
  "ConnectionMode": 1
}
```

全部字段都是可选的，缺什么就用内置默认值：

| 字段 | 作用 | 缺省时 |
| --- | --- | --- |
| `AccountServiceUrl` | 账号服务地址，**非回环必须 HTTPS** | Inspector 上的 `m_AccountServiceUrl`（`http://127.0.0.1:5080/`） |
| `AllowLanHttp` | 允许局域网私有 IP 走明文 HTTP | `false` |
| `ServerAddress` | 游戏服务器地址，**可以填域名** | `ConnectionSettings.DefaultServerAddress`（`127.0.0.1`） |
| `ServerPort` | 游戏服务器端口 | `7979` |
| `ConnectionMode` | 主菜单初始连接模式，`1` 才会显示 HOST / JOIN EXPEDITION | PlayerPrefs，全新安装为 `0` |

`ServerAddress` 在连接时才做 DNS 解析（`ClientSettings.ResolveAsync`），因为
`NetworkEndpoint` 结构体只能存 IP、存不了域名。填 IP 字面量时这一步直接跳过，零开销。

`ServerAddress` 写入后会被记成"已下发的值"（PlayerPrefs `SeededServerAddress`）：
**这个值一改，老玩家下次启动会自动跟到新地址**；值没变时则尊重玩家自己手填的地址。
换 VPS 时不用让所有人重装。

**打包时这个文件会自动拷到 exe 所在目录。** `Assets/Editor/ClientSettingsPostprocessor.cs`
是一个 `IPostprocessBuildWithReport`，**任何方式触发的构建都会执行**（所以故意没做成菜单项——
从 Build Settings 构建会绕过菜单项）。项目根目录里没有这个文件时，构建会打印一条明确的警告：
那样打出来的包会回退到 `http://127.0.0.1:5080/` 和 `127.0.0.1`，指向玩家自己的机器，
**登录和连接都会失败**。

`ConnectionMode` 只决定全新安装的初始值。玩家自己切换过之后以他的选择为准，不会被每次启动改回去。


## 7. 验证顺序

1. `wg show` 有握手；你家机器 `ping 10.8.0.1` 通
2. 你家机器：`netstat -an | findstr 7979` 看到 `0.0.0.0:7979` LISTENING
3. VPS：`curl -sv http://10.8.0.2:5080/` 有响应
4. VPS：`curl -sv https://moon.example.com/` 有响应
5. **你自己**用 `127.0.0.1:7979` 进游戏（别走中继，省一圈）
6. 让朋友连 `moon.example.com:7979` + 账号服务
7. 有玩家时在 VPS 上看转发是否生效：`conntrack -L | grep 7979`

## 8. 你会踩到的坑

| 现象 | 原因 |
| --- | --- |
| 隧道通了但玩家连不上 | `net.ipv4.ip_forward` 没开，DNAT 静默失效 |
| 只有第一个玩家能进 | 忘了 `POSTROUTING MASQUERADE`，回包走了你家宽带出口，源地址不对 |
| 你自己上网全断了 | 家里那端 `AllowedIPs` 写成了 `0.0.0.0/0` |
| 隧道过一会儿掉线 | 家里那端没设 `PersistentKeepalive` |
| 客户端报 "Account service requires HTTPS" | 用了 `http://` + 域名，被 `ValidateAddress` 挡掉 |
| VPS 连不上 5080 | 后端还绑在 `127.0.0.1`，没改成隧道地址 |
| 登录超时但游戏能进 | Caddy 没起到 5080，或安全组没放 443 |
| iptables 规则重启就没了 | 没跑 `netfilter-persistent save` |

## 9. 两个现实约束

- **路径变成双跳**：玩家 → VPS → 你家 → VPS → 玩家。延迟约等于
  (你家到 VPS 的 RTT) + (玩家到 VPS 的 RTT)。VPS 离你远的话，**你自己玩也会变卡**。
- **所有玩家的流量都吃你家上行**，而且每一份都传两遍。ECS ghost 单人上行不大，
  但按人数乘一下；家用宽带的**上行**通常才是真正的瓶颈。

如果哪天人多到家里宽带上行顶不住，正路是把专用服务器搬到 VPS 上跑
（仓库里已有 `GhostBridgeBootstrap.IsServerOnly` 和
`Assets/Scripts/DedicatedServer/ServerBootstrap.cs`），那就没有双跳了，
代价是要出 Linux headless 构建、账号后端也得一起搬。
