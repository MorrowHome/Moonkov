using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Networking.Transport;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>
    /// The optional <c>moon-client.local.json</c> that ships next to the executable. It points a
    /// build at an account service and a game server without recompiling, so a player only has to
    /// unzip the folder and run. Every field is optional and falls back to the built-in default.
    ///
    /// This is the only reader of that file: AccountServiceSettings, ConnectionSettings and
    /// GameSettings all go through it, so they can never disagree about what the file said.
    /// </summary>
    public static class ClientSettings
    {
        public const string FileName = "moon-client.local.json";

        sealed class Values
        {
            public string AccountServiceUrl;
            public bool AllowLanHttp;
            public string ServerAddress;
            public int ServerPort;
            // -1 means "leave whatever the player chose alone".
            public int ConnectionMode = -1;
        }

        static Values s_Values;

        // Not named Values: a member cannot share a name with the nested type above (CS0102).
        static Values Configuration
        {
            get
            {
                if (s_Values != null) return s_Values;
                try
                {
                    // Windows: next to the executable. macOS: next to the .app, outside its bundle.
                    string directory = Path.GetDirectoryName(Application.dataPath);
                    if (Application.platform == RuntimePlatform.OSXPlayer)
                        directory = Path.GetDirectoryName(directory);
                    string path = Path.Combine(directory, FileName);
                    s_Values = File.Exists(path)
                        ? JsonConvert.DeserializeObject<Values>(File.ReadAllText(path))
                        : new Values();
                    if (s_Values == null) s_Values = new Values();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Moonkov] ignoring {FileName}: {exception.Message}");
                    s_Values = new Values();
                }
                return s_Values;
            }
        }

        /// <summary>Editor play mode keeps statics alive between sessions, so re-read the file each run.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_Values = null;

        public static string AccountServiceUrl => Configuration.AccountServiceUrl?.Trim();
        public static bool AllowLanHttp => Configuration.AllowLanHttp;

        /// <summary>The game server a fresh install should point at, or null for the built-in default.</summary>
        public static string ServerAddress =>
            string.IsNullOrWhiteSpace(Configuration.ServerAddress) ? null : Configuration.ServerAddress.Trim();

        /// <summary>The game server port, or 0 for the built-in default.</summary>
        public static int ServerPort => Configuration.ServerPort is > 0 and <= 65535 ? Configuration.ServerPort : 0;

        /// <summary>The connection mode the main menu starts in, or -1 to keep the player's choice.</summary>
        public static int ConnectionMode => Configuration.ConnectionMode is 0 or 1 ? Configuration.ConnectionMode : -1;

        /// <summary>
        /// Unity Transport cannot hold a host name - NetworkEndpoint only stores an address - so a
        /// configured name is resolved here. An address literal comes back unchanged, which is the
        /// usual case and costs nothing.
        /// </summary>
        public static async Task<string> ResolveAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address) || IPAddress.TryParse(address, out _))
                return address;
            try
            {
                var candidates = await Dns.GetHostAddressesAsync(address);
                foreach (var candidate in candidates)
                    if (candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return candidate.ToString();
                Debug.LogWarning($"[Moonkov] {address} has no IPv4 address.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Moonkov] could not resolve {address}: {exception.Message}");
            }
            return address;
        }

        /// <summary>
        /// A connect field may hold an address or a name. Names are resolved at connect time, so they
        /// have to pass validation here or the join button would stay disabled.
        /// </summary>
        public static bool IsValidAddress(string address) =>
            !string.IsNullOrWhiteSpace(address) &&
            (NetworkEndpoint.TryParse(address, 0, out _) || Uri.CheckHostName(address) == UriHostNameType.Dns);
    }
}
