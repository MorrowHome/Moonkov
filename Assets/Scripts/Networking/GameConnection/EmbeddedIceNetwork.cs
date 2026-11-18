// 给 ICE 寻路准备地址信息。它选网卡、找 STUN 地址、过滤交换的候选地址，并比较房主标识以判断是否实际在同一台机器。
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.MP_FPS.Online;
using UnityEngine;

namespace Unity.MP_FPS.EmbeddedNetworking {
    internal static class EmbeddedIceNetwork {
        public static string PhysicalAddress(string configured = null) {
            var cards = NetworkInterface.GetAllNetworkInterfaces();
            // 手动指定的地址必须是本机正在使用的一张 IPv4 网卡的地址。它下线或地址写错时应提示，不能偷偷换一张卡。
            if (!string.IsNullOrWhiteSpace(configured)) {
                if (!IPAddress.TryParse(configured, out var requested) || IPAddress.IsLoopback(requested) ||
                    requested.AddressFamily != AddressFamily.InterNetwork || !cards.Any(n =>
                        n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().UnicastAddresses
                            .Any(a => a.Address.Equals(requested))))
                    throw new InvalidOperationException("配置的 P2P 网卡地址当前不可用。");
                return requested.ToString();
            }
            // 未指定时从实体 Wi-Fi 和网线里找地址，优先有 IPv4 网关的网卡。虚拟网卡、本机回环和自动私有地址被排除。
            foreach (var card in cards
                         .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                                     (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                      n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)).Where(n =>
                             !new[] {
                                 "vpn", "virtual", "wireguard", "tailscale", "zerotier", "tap-", "tun", "hyper-v",
                                 "vmware"
                             }.Any(s => (n.Name + " " + n.Description).IndexOf(s, StringComparison.OrdinalIgnoreCase) >=
                                        0)).OrderByDescending(n =>
                             n.GetIPProperties().GatewayAddresses
                                 .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))) {
                var address = card.GetIPProperties().UnicastAddresses.Select(a => a.Address).FirstOrDefault(a =>
                    a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) &&
                    !a.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                if (address != null) return address.ToString();
            }
            throw new InvalidOperationException("找不到可用的校园网或热点网卡，P2P 连接已停止。");
        }

        public static string HostId(string instance) {
            string id = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrWhiteSpace(id) || id == "n/a") return null;
            // 给机器身份加本场实例号再哈希。双方可以识别同一台机器，又不会把机器原始标识直接放进大厅。
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(instance + "|" + id)))
                .Replace("-", "");
        }

        public static bool SameHost(LobbySnapshot snapshot) {
            string localId = HostId(snapshot.InstanceId);
            // 相同的私有地址可以同时出现在互不相干的网络。只要两端都有，
            // 就优先用带实例号的设备哈希判断是否在同一台机器上。
            if (!string.IsNullOrEmpty(snapshot.IceHostId) && !string.IsNullOrEmpty(localId))
                return snapshot.IceHostId == localId;
            // 两端拿不到设备标识时才退回比较本机地址。不同内网网段可能用相同 IP，因此优先上面更可靠的机器标识。
            return IPAddress.TryParse(snapshot.IceHostAddress, out var address) && NetworkInterface
                .GetAllNetworkInterfaces()
                .Any(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
        }

        // 即使两个进程都在同一台电脑，房主电脑上的客户端也要
        // 尝试公网 NAT 路线。候选中不发布回环地址。
        public static string Description(string sdp, bool publicOnly, bool udpDirectOnly = false) {
            if (string.IsNullOrEmpty(sdp) || sdp.Length > 4095) throw new InvalidOperationException("P2P 连接描述无效。");
            var result = new StringBuilder(sdp.Length);
            // 逐行检查连接描述中的候选项。回环始终不交换，同机测试再去掉私有地址；好友直连还要求 IPv4 UDP 主机或映射候选。
            int candidates = 0;
            foreach (string raw in sdp.Split('\n')) {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("a=candidate:", StringComparison.Ordinal)) {
                    string[] fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 8 || !IPAddress.TryParse(fields[4], out var ip) || IPAddress.IsLoopback(ip) ||
                        (publicOnly && !IsPublic(ip)))
                        continue;
                    if (udpDirectOnly && (ip.AddressFamily != AddressFamily.InterNetwork || ip.Equals(IPAddress.Any) ||
                                          ip.GetAddressBytes()[0] >= 224 ||
                                          !string.Equals(fields[2], "UDP", StringComparison.OrdinalIgnoreCase) ||
                                          !ushort.TryParse(fields[5], out ushort port) || port == 0 ||
                                          fields[6] != "typ" || (fields[7] != "host" && fields[7] != "srflx")))
                        continue;
                    candidates++;
                }
                if (line.Length != 0) result.Append(line).Append("\r\n");
            }
            // 不合条件的候选过滤完若为空，信令到此结束。不能拿未过滤的描述碰碰运气。
            if (candidates == 0)
                throw new InvalidOperationException(publicOnly
                    ? "本机公网 P2P 没有可用地址；不会改走本机直连。可在下一局选择 VPS 中转。"
                    : "P2P 未获取到可用网络地址，请检查网络后重试。");
            return result.ToString();
        }

        private static bool IsPublic(IPAddress address) {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            byte[] a = address.GetAddressBytes();
            if (a.Length == 16) return (a[0] & 0xe0) == 0x20;
            return a[0] != 0 && a[0] != 10 && a[0] != 127 && a[0] < 224 && !(a[0] == 169 && a[1] == 254) &&
                   !(a[0] == 172 && a[1] >= 16 && a[1] <= 31) && !(a[0] == 192 && a[1] == 168) &&
                   !(a[0] == 100 && a[1] >= 64 && a[1] <= 127);
        }

        public static async Task<string> ResolveStunAsync(string hostname, CancellationToken ct) {
            if (IPAddress.TryParse(hostname, out var literal)) return literal.ToString();
            var lookup = Dns.GetHostAddressesAsync(hostname);
            // DNS 后台任务不一定响应取消，外层只等一小会。超时后观察迟到异常，不让解析拖住后续退出。
            if (await Task.WhenAny(lookup, Task.Delay(2500, ct)) != lookup) {
                _ = Observe(lookup);
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("P2P 地址服务解析超时。");
            }
            var ip = (await lookup).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            ct.ThrowIfCancellationRequested();
            return ip?.ToString() ?? throw new InvalidOperationException("P2P 地址服务没有 IPv4 地址。");
        }

        private static async Task Observe(Task task) {
            try { await task; } catch (Exception) { }
        }

        public static async Task WaitAsync(Func<bool> ready, double seconds, CancellationToken ct) {
            // 协商超时用真实时间，避免加载界面暂停或变更时间缩放后永远等不到截止。
            double until = Time.realtimeSinceStartupAsDouble + seconds;
            while (!ready()) {
                ct.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble >= until)
                    throw new TimeoutException("P2P 直连超时，本局不会自动改走 VPS。请重试，或在下一局选择 VPS 中转。");
                await Task.Delay(20, ct);
            }
            ct.ThrowIfCancellationRequested();
        }

        public static bool Connected(EmbeddedIceState state) {
            return state == EmbeddedIceState.Connected || state == EmbeddedIceState.Completed;
        }
    }
}