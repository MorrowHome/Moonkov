// ServerScene 的独立服入口。它准备常驻资源、取出服务端 World、开始监听和加载地图，最后向战局管理服务报就绪。
#if UNITY_SERVER || UNITY_EDITOR
using System;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public class ServerBootstrap : MonoBehaviour {
        private float m_NextShutdownCheck;
        private bool m_Stopping;

        private void Update() {
            // 部署脚本可以用一个信号文件要求停服。这里只接收一次，进入 GameManager 的退出流程后会等尚未完成的结算落盘。
            if (m_Stopping || Time.unscaledTime < m_NextShutdownCheck) return;
            m_NextShutdownCheck = Time.unscaledTime + 1;
            string signal = Environment.GetEnvironmentVariable("MOON_SERVER_SHUTDOWN_FILE");
            if (string.IsNullOrEmpty(signal) || !System.IO.File.Exists(signal)) return;
            m_Stopping = true;
            System.IO.File.Delete(signal);
            Debug.Log("[Server] Graceful shutdown requested; flushing pending raid receipts.");
            if (GameManager.Instance != null)
                GameManager.Instance.QuitAsync();
            else
                Application.Quit();
        }

        private void Awake() {
            // 只有装载成专用服务端才继续。普通带界面的客户端即使同场景看到这个组件，也会在这里把它停用。
            if (!GhostBridgeBootstrap.IsServerOnly) {
                enabled = false;
                return;
            }
            Debug.Log($"FPS2 server -> Awaking.");
            Application.runInBackground = true;
            Debug.Log($"FPS2 server -> Loading the Persistent scene ...");
            // 服务器启动时先补载常驻场景，后面创建战局要用到已经初始化的管理器。同一个场景只载一次。
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
                SceneManager.LoadScene("Scenes/Persistents", LoadSceneMode.Additive);
            Debug.Log($"FPS2 server -> Persistent is loaded.");
        }

        private async void Start() {
            Debug.Log($"FPS2 server -> Starting.");
            // 大厅分配的合作与对抗战局使用远征规则，队伍由服务端分配。
            // 从大厅分配出来的合作或对抗实例都先用远征的基础规则。参与者和分队再由服务端根据入场资料安排。
            if (OnlineInstanceServer.Managed) GameSettings.Instance.RequestedMode = RaidGameMode.Expedition;
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);
            // 部署给出的端口交给 GameConnection 生成监听配置。监听地址由 NetCode 驱动真正打开，端口占用会使启动失败。
            ushort port = GetPortFromArgument();
            var gameConnection = GameConnection.GetServerConnectionSettings(port);
            GameManager.SetGameConnection(gameConnection);
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            GameManager.DestroyLocalSimulationWorld();
            // 专用服的 World 在 GhostBridgeBootstrap 启动时已经建好。这里拿来装入比赛模式，再登记 Ghost 上的服务器更新回调。
            var serverWorld = GhostBridgeBootstrap.Instance.ServerWorld;
            GameManager.ConfigureRaidMode(serverWorld);
            GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            Debug.Log($"FPS2 server -> ServerWorld is available.");
            using var drvQuery =
                serverWorld.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
            drvQuery.CompleteDependency();
            var serverDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>();
            if (!serverDriver.ValueRW.Listen(gameConnection.ListenEndpoint))
                throw new InvalidOperationException($"Unable to listen on port {port}.");
            Debug.Log($"FPS2 server -> Listening on port {port}.");
            Debug.Log($"FPS2 server -> Loading the Game Scenes.");
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadGameScene);
            // 服务端只需要自己的场景和子场景，所以第二个 World 参数留空。地图读完才会报就绪，分配器可以据此放玩家进来。
            await ScenesLoader.LoadGameplayAsync(serverWorld, null);
            Debug.Log($"FPS2 server -> The Game Scene is loaded.");
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            GameSettings.Instance.GameState = GlobalGameState.InGame;
            await OnlineInstanceServer.AnnounceReadyAsync();
        }

        private ushort GetPortFromArgument() {
            ushort port = 7979;
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
                if (arg.StartsWith("port=")) {
                    string portValue = arg.Substring("port=".Length);
                    Debug.Log("Port argument found: " + portValue);
                    if (!ushort.TryParse(portValue, out port))
                        Debug.LogWarning("Invalid port argument. The default port number 7979 is used.");
                }
            return port;
        }
    }
}

#endif