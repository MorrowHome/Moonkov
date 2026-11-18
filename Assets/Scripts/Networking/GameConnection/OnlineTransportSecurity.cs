// 配置在线战局的 DTLS。客户端用随包的 CA 和期望服务器名核对对端，服务端从本地配置读取证书与私钥。公网连接地址和证书名可以不同。
using System;
using System.IO;
using Newtonsoft.Json;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;
using UnityEngine;

namespace Unity.MP_FPS {
    // PEM 私钥放在 Assets 外，不打到客户端包里。
    public static class OnlineTransportSecurity {
        private sealed class ServerOptions {
            public string BackendUrl, ServerKey;
            public string GameCertificateFile = "LocalData/game-tls/server.pem";
            public string GamePrivateKeyFile = "LocalData/game-tls/server-key.pem";
        }

        public static void RequireTrustedOnlineHost() {
            string config = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
            if (string.IsNullOrEmpty(config))
                config = Path.Combine(Path.GetDirectoryName(Application.dataPath), "moon-server.local.json");
            // 启动在线房主要有服务端身份配置。个人临时开好友房由 LanPeerIdentity 创建另一套证书。
            if (!File.Exists(config))
                throw new InvalidOperationException("创建在线房间需要本机受信服务器配置。普通客户端请通过房间码或服务器地址加入；自建本地房间请使用 LAN。");
            var options = JsonConvert.DeserializeObject<ServerOptions>(File.ReadAllText(config));
            // 在线房主既得有后端认证身份，也要用 HTTPS 或本机回环访问后端，账号服务才会接受独立服内部请求。
            if (options?.ServerKey?.Length < 32 || options?.ServerKey == null ||
                !Uri.TryCreate(options.BackendUrl, UriKind.Absolute, out var backend) ||
                (backend.Scheme != "https" && !(backend.Scheme == "http" && backend.IsLoopback)))
                throw new InvalidOperationException("在线房主的后端地址或服务器身份配置无效。");
        }

        public static void ConfigureClient(ref NetworkSettings settings) {
            string root = Path.GetDirectoryName(Application.dataPath);
            if (Application.platform == RuntimePlatform.OSXPlayer) root = Path.GetDirectoryName(root);
            // 客户端只随包取得公开 CA，借此验证对面是否为认可的战局服务器。连接地址可以是 IP，证书检查用预期的服务器名称。
            string ca = Path.Combine(root, "moon-game-ca.pem");
            if (!File.Exists(ca))
                throw new InvalidOperationException(
                    "Missing moon-game-ca.pem. Install the server's public game certificate beside the client.");
            // 证书名优先采用当前实例发下的值，其次用本机设置。最后才用玩家在连接页填写的地址。
            settings.WithSecureClientParameters(File.ReadAllText(ca),
                (OnlineLobbyClient.InInstance ? OnlineLobbyClient.ServerName : null) ??
                ClientSettings.GameServerName ?? ConnectionSettings.Instance.IPAddress);
        }

        public static void ConfigureServer(ref NetworkSettings settings) {
            string config = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
            if (string.IsNullOrEmpty(config))
                config = Path.Combine(Path.GetDirectoryName(Application.dataPath), "moon-server.local.json");
            if (!File.Exists(config))
                throw new InvalidOperationException("Online server requires moon-server.local.json.");
            var options = JsonConvert.DeserializeObject<ServerOptions>(File.ReadAllText(config));
            if (options == null || string.IsNullOrWhiteSpace(options.GameCertificateFile) ||
                string.IsNullOrWhiteSpace(options.GamePrivateKeyFile))
                throw new InvalidOperationException("Configure the online game certificate and private key paths.");
            // 服务端证书和私钥路径相对配置文件定位。只给 driver 使用，客户端只需要公开 CA。
            string root = Path.GetDirectoryName(Path.GetFullPath(config));
            string certificate = Path.Combine(root, options.GameCertificateFile);
            string key = Path.Combine(root, options.GamePrivateKeyFile);
            if (!File.Exists(certificate) || !File.Exists(key))
                throw new InvalidOperationException(
                    "Game DTLS certificate or private key is missing. Run Backend/Setup-GameTls.ps1 on the server.");
            settings.WithSecureServerParameters(File.ReadAllText(certificate), File.ReadAllText(key));
        }
    }
}