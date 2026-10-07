using System;
using System.Runtime.CompilerServices;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Relay or Direct connection type, set by <see cref="ServicesSettings"/>.
    /// </summary>
     public enum ConnectionType
     {
         Relay = 0,
         Direct = 1,
     }

     /// <summary>
     /// P2P (peer to peer) or Dgs (dedicated game server) matchmaker type, set by <see cref="ServicesSettings"/>.
     /// </summary>
     public enum MatchmakerType
     {
         P2P = 0,
         Dgs = 1,
     }

     public enum CreationType
     {
         CreateOrJoin = 0,
         Host = 1,
         ConnectAndJoin = 2
     }

     public class ConnectionSettings : INotifyBindablePropertyChanged
     {
         public static ConnectionSettings Instance { get; private set; } = null!;

         /// <summary>
         /// This initialization is required in the Editor to avoid the instance from a previous Playmode to stay alive in the next session.
         /// </summary>
         [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
         static void RuntimeInitializeOnLoad() => Instance = new ConnectionSettings();

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

         const string k_IPAddressKey = "IPAddress";
         const string k_PortKey = "Port";
         // Remember what the shipped settings file asked for, so changing that file moves existing
         // installs to the new server while a player's own edit still wins in between.
         const string k_SeededAddressKey = "SeededServerAddress";
         const string k_SeededPortKey = "SeededServerPort";

         public NetworkEndpoint ConnectionEndpoint;
         public string ConnectionError { get; set; }

         ConnectionSettings()
         {
             IPAddress = Seeded(k_IPAddressKey, k_SeededAddressKey, ClientSettings.ServerAddress, ConfiguredAddress);
             if (!ClientSettings.IsValidAddress(IPAddress))
                 IPAddress = ConfiguredAddress;

             string configuredPort = ClientSettings.ServerPort > 0 ? ClientSettings.ServerPort.ToString() : null;
             Port = Seeded(k_PortKey, k_SeededPortKey, configuredPort, ConfiguredPort);
             if (!ushort.TryParse(Port, out _))
                 Port = ConfiguredPort;
         }

         /// <summary>
         /// A value the shipped settings file supplies is re-applied whenever that value changes, so
         /// a new server address reaches existing installs; otherwise the player's own choice is kept.
         /// </summary>
         static string Seeded(string valueKey, string seedKey, string configured, string fallback)
         {
             if (string.IsNullOrWhiteSpace(configured))
                 return PlayerPrefs.GetString(valueKey, fallback);
             if (PlayerPrefs.GetString(seedKey, string.Empty) == configured)
                 return PlayerPrefs.GetString(valueKey, configured);
             PlayerPrefs.SetString(seedKey, configured);
             return configured;
         }

         public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;
         void Notify([CallerMemberName] string property = "") =>
             propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));

         ConnectionState.State m_ConnectionState;
         public ConnectionState.State GameConnectionState
         {
             get => m_ConnectionState;
             set
             {
                 if (m_ConnectionState == value)
                     return;
                 m_ConnectionState = value;
                 Notify(ConnectionStatusStylePropertyName);
             }
         }
         public static readonly string ConnectionStatusStylePropertyName = nameof(ConnectionStatusStyle);
         [CreateProperty]
         DisplayStyle ConnectionStatusStyle =>
             m_ConnectionState == ConnectionState.State.Connecting
                 ? DisplayStyle.Flex
                 : DisplayStyle.None;

         bool m_IsNetworkEndpointFormatValid;
         [CreateProperty]
         public bool IsNetworkEndpointValid
         {
             get => m_IsNetworkEndpointFormatValid;
             set
             {
                 if (m_IsNetworkEndpointFormatValid == value)
                     return;
                 m_IsNetworkEndpointFormatValid = value;
                 Notify();
             }
         }
         string m_IPAddress;
         [CreateProperty]
         public string IPAddress
         {
             get => m_IPAddress;
             set
             {
                 if (m_IPAddress == value)
                     return;

                 m_IPAddress = value;
                 PlayerPrefs.SetString(k_IPAddressKey, value);
                 IsNetworkEndpointValid = ClientSettings.IsValidAddress(m_IPAddress) && ushort.TryParse(m_Port, out _);
                 Notify();
             }
         }
         string m_Port;
         [CreateProperty]
         public string Port
         {
             get => m_Port;
             set
             {
                 if (m_Port == value)
                     return;

                 m_Port = value;
                 PlayerPrefs.SetString(k_PortKey, value);
                 IsNetworkEndpointValid = ClientSettings.IsValidAddress(m_IPAddress) && ushort.TryParse(m_Port, out _);
                 Notify();
             }
         }

         bool m_IsSessionCodeFormatValid;
         [CreateProperty]
         public bool IsSessionCodeFormatValid
         {
             get => m_IsSessionCodeFormatValid;
             private set
             {
                 if (m_IsSessionCodeFormatValid == value)
                     return;
                 m_IsSessionCodeFormatValid = value;
                 Notify();
             }
         }
         string m_SessionCode;
         [CreateProperty]
         public string SessionCode
         {
             get => m_SessionCode;
             set
             {
                 if(m_SessionCode == value)
                     return;

                 m_SessionCode = value;
                 IsSessionCodeFormatValid = CheckIsSessionCodeFormatValid(m_SessionCode);
                 Notify();
             }
         }
         static bool CheckIsSessionCodeFormatValid(string str)
         {
             if (string.IsNullOrEmpty(str) || str.Length != 6)
                 return false;

             foreach (var c in str)
             {
                 if (!char.IsLetter(c) && !char.IsNumber(c))
                     return false;
             }
             return true;
         }
     }
}
