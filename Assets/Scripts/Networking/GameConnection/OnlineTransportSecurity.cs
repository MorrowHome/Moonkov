using System;
using System.IO;
using Newtonsoft.Json;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;
using UnityEngine;

namespace Unity.MP_FPS
{
    // PEM private keys stay outside Assets and are never copied into a client build.
    public static class OnlineTransportSecurity
    {
        private sealed class ServerOptions
        {
            public string GameCertificateFile = "LocalData/game-tls/server.pem";
            public string GamePrivateKeyFile = "LocalData/game-tls/server-key.pem";
        }

        public static void ConfigureClient(ref NetworkSettings settings)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            if (Application.platform == RuntimePlatform.OSXPlayer) root = Path.GetDirectoryName(root);
            string ca = Path.Combine(root, "moon-game-ca.pem");
            if (!File.Exists(ca)) throw new InvalidOperationException("Missing moon-game-ca.pem. Install the server's public game certificate beside the client.");
            settings.WithSecureClientParameters(caCertificate: File.ReadAllText(ca),
                serverName: ClientSettings.GameServerName ?? ConnectionSettings.Instance.IPAddress);
        }

        public static void ConfigureServer(ref NetworkSettings settings)
        {
            string config = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
            if (string.IsNullOrEmpty(config)) config = Path.Combine(Path.GetDirectoryName(Application.dataPath), "moon-server.local.json");
            if (!File.Exists(config)) throw new InvalidOperationException("Online server requires moon-server.local.json.");
            var options = JsonConvert.DeserializeObject<ServerOptions>(File.ReadAllText(config));
            if (options == null || string.IsNullOrWhiteSpace(options.GameCertificateFile) || string.IsNullOrWhiteSpace(options.GamePrivateKeyFile))
                throw new InvalidOperationException("Configure the online game certificate and private key paths.");
            string root = Path.GetDirectoryName(Path.GetFullPath(config));
            string certificate = Path.Combine(root, options.GameCertificateFile);
            string key = Path.Combine(root, options.GamePrivateKeyFile);
            if (!File.Exists(certificate) || !File.Exists(key)) throw new InvalidOperationException("Game DTLS certificate or private key is missing. Run Backend/Setup-GameTls.ps1 on the server.");
            settings.WithSecureServerParameters(certificate: File.ReadAllText(certificate), privateKey: File.ReadAllText(key));
        }
    }
}
