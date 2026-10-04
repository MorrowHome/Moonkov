using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Public client connection settings; never contains database or server credentials.</summary>
    public static class AccountServiceSettings
    {
        private sealed class Settings
        {
            public string AccountServiceUrl;
            public bool AllowLanHttp;
        }

        public static string Configure(string fallbackUrl)
        {
            // Windows: next to the executable. macOS: next to the .app, outside its bundle.
            string directory = Path.GetDirectoryName(Application.dataPath);
            if (Application.platform == RuntimePlatform.OSXPlayer)
                directory = Path.GetDirectoryName(directory);
            string path = Path.Combine(directory, "moon-client.local.json");
            var settings = File.Exists(path)
                ? JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path))
                : new Settings { AccountServiceUrl = fallbackUrl };
            if (settings == null) throw new InvalidOperationException("Client server settings are empty.");
            string url = string.IsNullOrWhiteSpace(settings.AccountServiceUrl) ? fallbackUrl : settings.AccountServiceUrl;
            ValidateAddress(url, settings.AllowLanHttp);
            AccountClient.AllowLanHttp = settings.AllowLanHttp;
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
