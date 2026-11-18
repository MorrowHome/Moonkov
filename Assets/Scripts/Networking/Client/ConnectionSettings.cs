// 加载页、高级连接和 GameConnection 共用的连接状态。端口、会话码、最终连接端点以及握手报错放这里，界面通过属性通知得到更新。
using System;
using System.Runtime.CompilerServices;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    /// <summary>
    /// Relay or Direct connection type, set by <see cref="ServicesSettings"/>.
    /// </summary>
    public enum ConnectionType {
        Relay = 0,
        Direct = 1
    }

    /// <summary>
    /// P2P (peer to peer) or Dgs (dedicated game server) matchmaker type, set by <see cref="ServicesSettings"/>.
    /// </summary>
    public enum MatchmakerType {
        P2P = 0,
        Dgs = 1
    }

    // GameManager 根据这个枚举区分要创建本机两个 World、只去远端加入，还是从大厅或好友房接手连接。
    public enum CreationType {
        CreateOrJoin = 0,
        Host = 1,
        ConnectAndJoin = 2,
        Offline = 3,
        LanHost = 4,
        LanJoin = 5,
        JoinRelay = 6
    }

    public class ConnectionSettings : INotifyBindablePropertyChanged {
        public static ConnectionSettings Instance { get; private set; } = null!;

        /// <summary>
        /// This initialization is required in the Editor to avoid the instance from a previous Playmode to stay alive in the next session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitializeOnLoad() { Instance = new ConnectionSettings(); }

        public const string DefaultServerAddress = "127.0.0.1";
        public const ushort DefaultServerPort = 7979;

        /// <summary>
        /// The address a fresh install should use: what the shipped settings file asked for, or the
        /// built-in default. Cancelling a popup restores this rather than the built-in default, so
        /// a player cannot accidentally replace the server they were given.
        /// </summary>
        public static string ConfiguredAddress => ClientSettings.ServerAddress ?? DefaultServerAddress;

        /// <summary>The port a fresh install should use, on the same terms as <see cref="ConfiguredAddress"/>.</summary>
        public static string ConfiguredPort =>
            ClientSettings.ServerPort > 0 ? ClientSettings.ServerPort.ToString() : DefaultServerPort.ToString();

        private const string k_IPAddressKey = "IPAddress";
        private const string k_PortKey = "Port";

        // Remember what the shipped settings file asked for, so changing that file moves existing
        // installs to the new server while a player's own edit still wins in between.
        private const string k_SeededAddressKey = "SeededServerAddress";
        private const string k_SeededPortKey = "SeededServerPort";
        // 页面输入可以是地址文本或会话码。真正交给 NetCode 的是已解析端点，领票和重连标记也只用于当次加载。
        public NetworkEndpoint ConnectionEndpoint;
        public string GameJoinTicket { get; set; }
        public bool ResumeOnly { get; set; }
        public string ConnectionError { get; set; }
        public bool ModeConfirmed { get; set; }
        public RaidGameMode ApprovedMode { get; set; }
        public int ApprovedRulesVersion { get; set; }

        private ConnectionSettings() {
            // 刚安装先用随包默认值，之后保留玩家的修改。发布方改了随包地址才重新覆盖一次，端口同理。
            IPAddress = Seeded(k_IPAddressKey, k_SeededAddressKey, ClientSettings.ServerAddress, ConfiguredAddress);
            if (!ClientSettings.IsValidAddress(IPAddress)) IPAddress = ConfiguredAddress;
            string configuredPort = ClientSettings.ServerPort > 0 ? ClientSettings.ServerPort.ToString() : null;
            Port = Seeded(k_PortKey, k_SeededPortKey, configuredPort, ConfiguredPort);
            if (!ushort.TryParse(Port, out _)) Port = ConfiguredPort;
        }

        /// <summary>
        /// A value the shipped settings file supplies is re-applied whenever that value changes, so
        /// a new server address reaches existing installs; otherwise the player's own choice is kept.
        /// </summary>
        private static string Seeded(string valueKey, string seedKey, string configured, string fallback) {
            if (string.IsNullOrWhiteSpace(configured)) return PlayerPrefs.GetString(valueKey, fallback);
            // 保存上次已经套用的随包值。只有新的随包值和它不同时，才用新配置替换之前的连接目标。
            if (PlayerPrefs.GetString(seedKey, string.Empty) == configured)
                return PlayerPrefs.GetString(valueKey, configured);
            PlayerPrefs.SetString(seedKey, configured);
            return configured;
        }

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        private void Notify([CallerMemberName] string property = "") {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }

        private ConnectionState.State m_ConnectionState;

        public ConnectionState.State GameConnectionState {
            get => m_ConnectionState;
            set {
                if (m_ConnectionState == value) return;
                m_ConnectionState = value;
                // 状态值有变化时通知 UI Toolkit 重新取连接提示的 DisplayStyle，连接中才显示提示。
                Notify(ConnectionStatusStylePropertyName);
            }
        }

        public static readonly string ConnectionStatusStylePropertyName = nameof(ConnectionStatusStyle);

        [CreateProperty]
        private DisplayStyle ConnectionStatusStyle =>
            m_ConnectionState == ConnectionState.State.Connecting ? DisplayStyle.Flex : DisplayStyle.None;

        private bool m_IsNetworkEndpointFormatValid;

        [CreateProperty]
        public bool IsNetworkEndpointValid {
            get => m_IsNetworkEndpointFormatValid;
            set {
                if (m_IsNetworkEndpointFormatValid == value) return;
                m_IsNetworkEndpointFormatValid = value;
                Notify();
            }
        }

        private string m_IPAddress;

        [CreateProperty]
        public string IPAddress {
            get => m_IPAddress;
            set {
                if (m_IPAddress == value) return;
                m_IPAddress = value;
                // 地址和端口任一修改后都重算整组端点格式。满足格式才解锁连接按钮，域名是否真能解析留给加载处理。
                PlayerPrefs.SetString(k_IPAddressKey, value);
                IsNetworkEndpointValid = ClientSettings.IsValidAddress(m_IPAddress) && ushort.TryParse(m_Port, out _);
                Notify();
            }
        }

        private string m_Port;

        [CreateProperty]
        public string Port {
            get => m_Port;
            set {
                if (m_Port == value) return;
                m_Port = value;
                PlayerPrefs.SetString(k_PortKey, value);
                IsNetworkEndpointValid = ClientSettings.IsValidAddress(m_IPAddress) && ushort.TryParse(m_Port, out _);
                Notify();
            }
        }

        private bool m_IsSessionCodeFormatValid;

        [CreateProperty]
        public bool IsSessionCodeFormatValid {
            get => m_IsSessionCodeFormatValid;
            private set {
                if (m_IsSessionCodeFormatValid == value) return;
                m_IsSessionCodeFormatValid = value;
                Notify();
            }
        }

        private string m_SessionCode;

        [CreateProperty]
        public string SessionCode {
            get => m_SessionCode;
            set {
                if (m_SessionCode == value) return;
                // 玩家粘贴的会话码先去前后空格并统一大写。这里检查的是六位字母数字格式，真实房间由服务查询。
                m_SessionCode = value?.Trim().ToUpperInvariant();
                IsSessionCodeFormatValid = CheckIsSessionCodeFormatValid(m_SessionCode);
                Notify();
            }
        }

        private static bool CheckIsSessionCodeFormatValid(string str) {
            if (string.IsNullOrEmpty(str) || str.Length != 6) return false;
            foreach (char c in str)
                if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9'))
                    return false;
            return true;
        }
    }
}