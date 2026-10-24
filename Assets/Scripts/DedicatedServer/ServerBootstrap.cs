#if UNITY_SERVER || UNITY_EDITOR

using System;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public class ServerBootstrap : MonoBehaviour
    {
        private float m_NextShutdownCheck;
        private bool m_Stopping;
        private void Update()
        {
            if (m_Stopping || Time.unscaledTime < m_NextShutdownCheck) return;
            m_NextShutdownCheck = Time.unscaledTime + 1;
            string signal = Environment.GetEnvironmentVariable("MOON_SERVER_SHUTDOWN_FILE");
            if (string.IsNullOrEmpty(signal) || !System.IO.File.Exists(signal)) return;
            m_Stopping = true;
            System.IO.File.Delete(signal);
            Debug.Log("[Server] Graceful shutdown requested; flushing pending raid receipts.");
            if (GameManager.Instance != null) GameManager.Instance.QuitAsync(); else Application.Quit();
        }
        private void Awake()
        {
            if (!GhostBridgeBootstrap.IsServerOnly)
            {
                enabled = false;
                return;
            }

            Debug.Log($"FPS2 server -> Awaking.");
            Application.runInBackground = true; 
            Debug.Log($"FPS2 server -> Loading the Persistent scene ...");
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
            {
                SceneManager.LoadScene("Scenes/Persistents", LoadSceneMode.Additive);
            }
            Debug.Log($"FPS2 server -> Persistent is loaded.");
        }

        async void Start()
        {
            Debug.Log($"FPS2 server -> Starting.");
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);
            ushort port = GetPortFromArgument();
            GameConnection gameConnection = GameConnection.GetServerConnectionSettings(port);
            GameManager.SetGameConnection(gameConnection);
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            GameManager.DestroyLocalSimulationWorld();

            var serverWorld = GhostBridgeBootstrap.Instance.ServerWorld;
            GameManager.ConfigureRaidMode(serverWorld);
            GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            Debug.Log($"FPS2 server -> ServerWorld is available.");
            
            using var drvQuery = serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.CompleteDependency();
            var serverDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>();
            if (!serverDriver.ValueRW.Listen(gameConnection.ListenEndpoint))
                throw new InvalidOperationException($"Unable to listen on port {port}.");
            Debug.Log($"FPS2 server -> Listening on port {port}.");
            
            Debug.Log($"FPS2 server -> Loading the Game Scenes.");
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadGameScene);
            await ScenesLoader.LoadGameplayAsync(serverWorld, null);
            Debug.Log($"FPS2 server -> The Game Scene is loaded.");
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            GameSettings.Instance.GameState = GlobalGameState.InGame;
        }
        
        ushort GetPortFromArgument()
        {
            ushort port = 7979;
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (arg.StartsWith("port="))
                {
                    string portValue = arg.Substring("port=".Length);
                    Debug.Log("Port argument found: " + portValue);
                    if (!ushort.TryParse(portValue, out port))
                    {
                        Debug.LogWarning("Invalid port argument. The default port number 7979 is used.");
                    }
                }
            }
            return port;
        }
    }
}

#endif
