// 读客户端随包配置和本机覆盖配置，提供账号地址、战局地址、证书名及 ICE 网卡选项。建普通 IPv4 连接前，GameConnection 还会请它把域名解析成地址。
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>
    /// 可选的 <c>moon-client.local.json</c> 放在可执行文件旁。
    /// 它可以直接指定账号服务和战局服务器，无需重新编译，
    /// 玩家解压就能启动。所有字段可省略，缺省时使用程序内默认值。
    ///
    /// 只有这里读取该文件。AccountServiceSettings、ConnectionSettings
    /// 和 GameSettings 都使用它提供的同一份配置。
    /// </summary>
    public static class ClientSettings {
        public const string FileName = "moon-client.local.json";
        public const string EditorFileName = "moon-client.editor.local.json";

        private sealed class Values {
            public string GameServerName;
            public string IceBindAddress;
            public string AccountServiceUrl;
            public string FriendSignalingUrl;
            public bool? AllowLanHttp;
            public string ServerAddress;
            public int ServerPort;

            // -1 表示沿用玩家原先的选择。
            public int ConnectionMode = -1;
        }

        private static Values s_Values;

        // 成员不能和前面那个嵌套类型都叫 Values，否则报 CS0102。
        private static Values Configuration {
            get {
                // 几个菜单和网络入口会反复取配置。同一次运行缓存第一次读到的值，避免每次打开页面都读磁盘。
                if (s_Values != null) return s_Values;
                try {
                    // Windows 放在可执行文件旁。macOS 放在 .app 旁、应用包外。
                    // 配置和可执行文件一起发。macOS Player 的数据路径在应用包里，所以还要上一层找应用包旁的文件。
                    string directory = Path.GetDirectoryName(Application.dataPath);
                    if (Application.platform == RuntimePlatform.OSXPlayer) directory = Path.GetDirectoryName(directory);
                    string path = Path.Combine(directory, FileName);
                    s_Values = File.Exists(path)
                        ? JsonConvert.DeserializeObject<Values>(File.ReadAllText(path))
                        : new Values();
                    if (s_Values == null) s_Values = new Values();
                    // 可根据 HTTPS 账号服务推出同域的好友信令地址。显式提供信令地址时，以随包配置为准。
                    if (string.IsNullOrWhiteSpace(s_Values.FriendSignalingUrl) &&
                        Uri.TryCreate(s_Values.AccountServiceUrl, UriKind.Absolute, out var publicUrl) &&
                        publicUrl.Scheme == Uri.UriSchemeHttps)
                        s_Values.FriendSignalingUrl = new Uri(publicUrl, "/friend-signaling/v1/").AbsoluteUri;
                } catch (Exception exception) {
                    Debug.LogWarning($"[Moonkov] ignoring {FileName}: {exception.Message}");
                    s_Values = new Values();
                }
#if UNITY_EDITOR
                // 本地开发应使用独立覆盖配置，随包设置继续提供给玩家。
                try {
                    // 编辑器开发配置只覆盖明确填写过的字段。空字段继续用随包值，因此本机测试不用改发给玩家的那份配置。
                    string editorPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), EditorFileName);
                    if (File.Exists(editorPath)) {
                        var overrides = JsonConvert.DeserializeObject<Values>(File.ReadAllText(editorPath));
                        if (overrides != null) {
                            if (overrides.AccountServiceUrl != null)
                                s_Values.AccountServiceUrl = overrides.AccountServiceUrl;
                            if (overrides.FriendSignalingUrl != null)
                                s_Values.FriendSignalingUrl = overrides.FriendSignalingUrl;
                            if (overrides.ServerAddress != null) s_Values.ServerAddress = overrides.ServerAddress;
                            if (overrides.GameServerName != null) s_Values.GameServerName = overrides.GameServerName;
                            if (overrides.ServerPort != 0) s_Values.ServerPort = overrides.ServerPort;
                            if (overrides.ConnectionMode != -1) s_Values.ConnectionMode = overrides.ConnectionMode;
                            s_Values.AllowLanHttp = overrides.AllowLanHttp ?? s_Values.AllowLanHttp;
                        }
                    }
                } catch (Exception exception) {
                    Debug.LogWarning($"[Moonkov] ignoring {EditorFileName}: {exception.Message}");
                }
#endif
                return s_Values;
            }
        }

        /// <summary>编辑器退出 Play 后可能保留静态字段，所以每次启动都重新读取文件。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { s_Values = null; }

        public static string AccountServiceUrl => Configuration.AccountServiceUrl?.Trim();
        public static string FriendSignalingUrl => Configuration.FriendSignalingUrl?.Trim();

        public static string GameServerName =>
            string.IsNullOrWhiteSpace(Configuration.GameServerName) ? null : Configuration.GameServerName.Trim();

        public static string IceBindAddress => Configuration.IceBindAddress?.Trim();
        public static bool AllowLanHttp => Configuration.AllowLanHttp ?? false;

        /// <summary>首次安装时默认使用的游戏服务器。返回 null 时采用程序内默认值。</summary>
        public static string ServerAddress =>
            string.IsNullOrWhiteSpace(Configuration.ServerAddress) ? null : Configuration.ServerAddress.Trim();

        /// <summary>游戏服务器端口。0 表示使用程序内默认端口。</summary>
        public static int ServerPort => Configuration.ServerPort is > 0 and <= 65535 ? Configuration.ServerPort : 0;

        /// <summary>主菜单的初始连接模式。-1 表示沿用玩家选择。</summary>
        public static int ConnectionMode => Configuration.ConnectionMode is 0 or 1 ? Configuration.ConnectionMode : -1;

        /// <summary>
        /// Unity Transport 的 NetworkEndpoint 只存具体地址，所以配置域名时
        /// 在这里做解析。常见的字面 IP 直接返回，
        /// 无需等待 DNS。
        /// </summary>
        public static async Task<string> ResolveAsync(string address) {
            // 输入已经是 IP 时直接返回，连接不必等待 DNS。域名则在真正进场前查地址。
            if (string.IsNullOrWhiteSpace(address) || IPAddress.TryParse(address, out _)) return address;
            try {
                // 这个入口挑 IPv4 地址。解析异常或没找到时记下原因，后续端点解析和连接等待还会处理失败。
                var candidates = await Dns.GetHostAddressesAsync(address);
                foreach (var candidate in candidates)
                    if (candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return candidate.ToString();
                Debug.LogWarning($"[Moonkov] {address} has no IPv4 address.");
            } catch (Exception exception) {
                Debug.LogWarning($"[Moonkov] could not resolve {address}: {exception.Message}");
            }
            return address;
        }

        /// <summary>
        /// 连接输入栏可以填写 IP 或域名，域名在发起连接时才解析。
        /// 这里先认可域名格式，否则加入按钮会一直禁用。
        /// </summary>
        public static bool IsValidAddress(string address) {
            return !string.IsNullOrWhiteSpace(address) && (NetworkEndpoint.TryParse(address, 0, out _) ||
                                                           Uri.CheckHostName(address) == UriHostNameType.Dns);
        }
    }
}