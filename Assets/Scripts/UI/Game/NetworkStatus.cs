using System;
using System.Collections;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    [RequireComponent(typeof(UIDocument))]
    public class NetworkStatus : MonoBehaviour
    {
        static class UIElementNames
        {
            public const string ConnectionStatus = "ConnectionStatus";
            public const string NetworkingRole = "NetworkingRole";
            public const string SessionName = "SessionName";
        }

        VisualElement m_Root;
        Label m_ConnectionStatus;
        Label m_NetworkingRole;
        Label m_SessionName;

        void OnEnable()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;

            m_ConnectionStatus = m_Root.Q<Label>(UIElementNames.ConnectionStatus);
            m_NetworkingRole = m_Root.Q<Label>(UIElementNames.NetworkingRole);
            m_SessionName = m_Root.Q<Label>(UIElementNames.SessionName);

            StartCoroutine(UpdateNetworkStatusInfo());
        }

        void OnDisable()
        {
            StopCoroutine(UpdateNetworkStatusInfo());
        }


        IEnumerator UpdateNetworkStatusInfo()
        {
            while (true)
            {
                switch(ConnectionSettings.Instance.GameConnectionState)
                {
                    case ConnectionState.State.Disconnected:
                        MoonkovLocalization.Set(m_ConnectionStatus, "Connection: {0}", "Offline");
                        MoonkovLocalization.Set(m_NetworkingRole, string.Empty);
                        MoonkovLocalization.Set(m_SessionName, string.Empty);
                        break;
                    case ConnectionState.State.Connecting:
                        MoonkovLocalization.Set(m_ConnectionStatus, "Connection: {0}", "Connecting ...");
                        MoonkovLocalization.Set(m_NetworkingRole, string.Empty);
                        MoonkovLocalization.Set(m_SessionName, string.Empty);
                        break;
                    case ConnectionState.State.Connected:
                        MoonkovLocalization.Set(m_ConnectionStatus, "Connection: {0}", "Connected");
                        MoonkovLocalization.Set(m_NetworkingRole, "Role: {0}", ClientServerBootstrap.HasServerWorld ? "Server" : "Client");

                        if (GameManager.GameConnection != null &&
                            GameManager.GameConnection.Session != null)
                        {
                            MoonkovLocalization.Set(m_SessionName, "Session: {0}" , GameManager.GameConnection.Session.Name);
                        }
                        else
                        {
                            MoonkovLocalization.Set(m_SessionName, string.Empty);
                        }


                        break;
                }
                yield return (new WaitForSeconds(1.0f));
            }
        }
    }
}
