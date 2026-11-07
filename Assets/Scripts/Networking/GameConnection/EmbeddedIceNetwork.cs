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

namespace Unity.MP_FPS.EmbeddedNetworking
{
    internal static class EmbeddedIceNetwork
    {
        public static string PhysicalAddress(string configured = null)
        {
            var cards = NetworkInterface.GetAllNetworkInterfaces();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!IPAddress.TryParse(configured, out var requested) || IPAddress.IsLoopback(requested) ||
                    requested.AddressFamily != AddressFamily.InterNetwork ||
                    !cards.Any(n => n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(requested))))
                    throw new InvalidOperationException("配置的 P2P 网卡地址当前不可用。");
                return requested.ToString();
            }
            foreach (var card in cards.Where(n => n.OperationalStatus == OperationalStatus.Up &&
                         (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet || n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                     .Where(n => !new[] { "vpn", "virtual", "wireguard", "tailscale", "zerotier", "tap-", "tun", "hyper-v", "vmware" }
                         .Any(s => (n.Name + " " + n.Description).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                     .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork)))
            {
                var address = card.GetIPProperties().UnicastAddresses.Select(a => a.Address).FirstOrDefault(a =>
                    a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) &&
                    !a.ToString().StartsWith("169.254.", StringComparison.Ordinal));
                if (address != null) return address.ToString();
            }
            throw new InvalidOperationException("找不到可用的校园网或热点网卡，P2P 连接已停止。");
        }
        public static string HostId(string instance)
        {
            string id = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrWhiteSpace(id) || id == "n/a") return null;
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(instance + "|" + id))).Replace("-", "");
        }
        public static bool SameHost(LobbySnapshot snapshot)
        {
            string localId = HostId(snapshot.InstanceId);
            // Equal private addresses can exist on unrelated networks. Prefer the
            // per-instance device hash when both sides can provide one.
            if (!string.IsNullOrEmpty(snapshot.IceHostId) && !string.IsNullOrEmpty(localId))
                return snapshot.IceHostId == localId;
            return IPAddress.TryParse(snapshot.IceHostAddress, out var address) && NetworkInterface.GetAllNetworkInterfaces()
                .Any(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
        }

        // A host-PC client must attempt the public NAT path, even if both processes
        // happen to be on the same machine. Never publish loopback candidates.
        public static string Description(string sdp, bool publicOnly)
        {
            if (string.IsNullOrEmpty(sdp) || sdp.Length > 4095) throw new InvalidOperationException("P2P 连接描述无效。");
            var result = new StringBuilder(sdp.Length);
            int candidates = 0;
            foreach (var raw in sdp.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("a=candidate:", StringComparison.Ordinal))
                {
                    var fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length < 8 || !IPAddress.TryParse(fields[4], out var ip) || IPAddress.IsLoopback(ip) ||
                        publicOnly && !IsPublic(ip)) continue;
                    candidates++;
                }
                if (line.Length != 0) result.Append(line).Append("\r\n");
            }
            if (candidates == 0) throw new InvalidOperationException(publicOnly
                ? "本机公网 P2P 没有可用地址；不会改走本机直连。可在下一局选择 VPS 中转。"
                : "P2P 未获取到可用网络地址，请检查网络后重试。");
            return result.ToString();
        }
        static bool IsPublic(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            byte[] a = address.GetAddressBytes();
            if (a.Length == 16) return (a[0] & 0xe0) == 0x20;
            return a[0] != 0 && a[0] != 10 && a[0] != 127 && a[0] < 224 &&
                !(a[0] == 169 && a[1] == 254) && !(a[0] == 172 && a[1] >= 16 && a[1] <= 31) &&
                !(a[0] == 192 && a[1] == 168) && !(a[0] == 100 && a[1] >= 64 && a[1] <= 127);
        }
        public static async Task<string> ResolveStunAsync(string hostname, CancellationToken ct)
        {
            if (IPAddress.TryParse(hostname, out var literal)) return literal.ToString();
            var lookup = Dns.GetHostAddressesAsync(hostname);
            if (await Task.WhenAny(lookup, Task.Delay(2500, ct)) != lookup)
            {
                _ = Observe(lookup);
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException("P2P 地址服务解析超时。");
            }
            var ip = (await lookup).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            ct.ThrowIfCancellationRequested();
            return ip?.ToString() ?? throw new InvalidOperationException("P2P 地址服务没有 IPv4 地址。");
        }
        static async Task Observe(Task task) { try { await task; } catch (Exception) { } }
        public static async Task WaitAsync(Func<bool> ready, double seconds, CancellationToken ct)
        {
            double until = Time.realtimeSinceStartupAsDouble + seconds;
            while (!ready())
            {
                ct.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble >= until) throw new TimeoutException("P2P 直连超时，本局不会自动改走 VPS。请重试，或在下一局选择 VPS 中转。");
                await Task.Delay(20, ct);
            }
            ct.ThrowIfCancellationRequested();
        }
        public static bool Connected(EmbeddedIceState state) => state == EmbeddedIceState.Connected || state == EmbeddedIceState.Completed;
    }
}
