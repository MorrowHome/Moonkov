// 把 ClientSettings 里的账号服务地址交给 AccountClient。公网地址要求 HTTPS。本机和明确允许的私有 IPv4 地址才接受 HTTP。
using System;
using System.Net;
using System.Net.Sockets;

namespace Unity.MP_FPS {
    /// <summary>客户端公开连接配置。数据库和服务器凭据另行保存。</summary>
    public static class AccountServiceSettings {
        public static string Configure(string fallbackUrl) {
            // 只有 ClientSettings 直接读此文件。ConnectionSettings 和 GameSettings
            // 都使用它提供的值，因此各处能得到同一套随包配置。
            // 配置文件有值就覆盖场景给出的默认账号地址。UI、连接页和实际 HTTP 请求因此读到同一个站点。
            string url = string.IsNullOrWhiteSpace(ClientSettings.AccountServiceUrl)
                ? fallbackUrl
                : ClientSettings.AccountServiceUrl;
            ValidateAddress(url, ClientSettings.AllowLanHttp);
            AccountClient.AllowLanHttp = ClientSettings.AllowLanHttp;
            // 统一在末尾保留一个斜杠。后面拼上 auth 等相对路径时，URL 的最后一段才能保留下来。
            return url.TrimEnd('/') + "/";
        }

        public static Uri ValidateAddress(string baseUrl, bool allowLanHttp) {
            // 服务地址只接受完整 URL。用户名、查询参数和片段容易把凭据写进配置或影响请求路径，先在这里拒绝。
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var url) || !string.IsNullOrEmpty(url.UserInfo) ||
                !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
                throw new InvalidOperationException("Enter a valid account service URL.");
            if (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp &&
                                                     (url.IsLoopback ||
                                                      (allowLanHttp && IsPrivateLanAddress(url.Host)))))
                return url;
            throw new InvalidOperationException(
                "Account service requires HTTPS. For LAN testing, set AllowLanHttp in moon-client.local.json and use a private LAN IP.");
        }

        private static bool IsPrivateLanAddress(string host) {
            // 局域网 HTTP 例外只认明确的 IP，防止域名把例外带到公网。
            // 局域网免 HTTPS 只认具体的私有 IPv4。域名当前解析在内网，也可能以后被改去公网，不能使用这条例外。
            if (!IPAddress.TryParse(host.Trim('[', ']'), out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork)
                return false;
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}