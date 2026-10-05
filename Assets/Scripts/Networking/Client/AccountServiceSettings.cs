using System;
using System.Net;
using System.Net.Sockets;

namespace Unity.MP_FPS
{
    /// <summary>Public client connection settings; never contains database or server credentials.</summary>
    public static class AccountServiceSettings
    {
        public static string Configure(string fallbackUrl)
        {
            // The file itself is read by ClientSettings, which ConnectionSettings and GameSettings
            // share, so all three agree on what the shipped configuration said.
            string url = string.IsNullOrWhiteSpace(ClientSettings.AccountServiceUrl)
                ? fallbackUrl
                : ClientSettings.AccountServiceUrl;
            ValidateAddress(url, ClientSettings.AllowLanHttp);
            AccountClient.AllowLanHttp = ClientSettings.AllowLanHttp;
            return url.TrimEnd('/') + "/";
        }

        public static Uri ValidateAddress(string baseUrl, bool allowLanHttp)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var url) ||
                !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
                throw new InvalidOperationException("Enter a valid account service URL.");
            if (url.Scheme == Uri.UriSchemeHttps ||
                (url.Scheme == Uri.UriSchemeHttp && (url.IsLoopback || (allowLanHttp && IsPrivateLanAddress(url.Host)))))
                return url;
            throw new InvalidOperationException("Account service requires HTTPS. For LAN testing, set AllowLanHttp in moon-client.local.json and use a private LAN IP.");
        }

        private static bool IsPrivateLanAddress(string host)
        {
            // Explicit IPs only: a DNS name must not turn an HTTP exception into public access.
            if (!IPAddress.TryParse(host.Trim('[', ']'), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                return false;
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }
    }
}
