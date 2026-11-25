// 主菜单的入局按钮最终会到 GameManager。它串起档案保存、建连接、建 ECS World、地图和角色资源加载，以及正式开始战局。离场流程在 GameFlowControl。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    public partial class GameManager : MonoBehaviour {
        public static GameManager Instance { get; private set; }
        public const int MaxPlayer = 32;
        public static int RequestedMaxPlayers => LanRoomServer.CapacityFor(GameSettings.Instance.RequestedMode);
        public const string MainMenuSceneName = "MainMenu";
        public const string MenuPreviewSceneName = "GameScene";

        // 单人、Host、远程客户端和专用服务器共用。
        // Apollo12 与 MoonGameScene 仍可作为已有的备选场景。
        public const string GameSceneName = "TheophilusGameScene";
        public static GameConnection GameConnection { get; private set; }
        private Task m_LoadingGame;
        private CancellationTokenSource m_LoadingGameCancel;
        private Task m_LoadingMainMenu;
        private CancellationTokenSource m_LoadingMainMenuCancel;
        private bool m_StartGameRequested;
        public UnityEngine.Audio.AudioMixer AudioMixer;
        public int MaxSoundEmitters;
        public int MaxSoundGameObjects;
        [NonSerialized] public SoundGameObjectPool SoundGameObjects;
        private ISoundSystem m_SoundSystem;
        public ISoundSystem SoundSystem => m_SoundSystem;
        private bool m_IsHeadless = false;
        public bool IsHeadless => m_IsHeadless;
        public static bool CanUseMainMenu => SceneManager.GetActiveScene().name == MainMenuSceneName;

        private void Awake() {
            // 跨场景共用同一个 GameManager。碰到重复组件时销毁新来的那份，避免两套代码同时启动或关闭战局。
            if (Instance && Instance != this) {
                Debug.LogError($"Multiple instances of '{this}' violates the Singleton pattern!", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;
#if UNITY_STANDALONE_LINUX
            m_IsHeadless = true;
#else
            var commandLineArgs = new List<string>(Environment.GetCommandLineArgs());
            m_IsHeadless = commandLineArgs.Contains("-batchmode");
#endif
            m_IsHeadless |= GhostBridgeBootstrap.IsServerOnly;
            ConfigVar.Init();
            // 无画面的进程也有声音接口，但实际用空实现。带画面的进程才准备 AudioSource 池，以后播放时就可以反复借用。
            if (m_IsHeadless) { m_SoundSystem = new SoundSystemNull(); } else {
                m_SoundSystem = new SoundSystem();
                var audioListener = MainCameraSingleton.Instance.GetComponent<AudioListener>();
                SoundGameObjects = new SoundGameObjectPool("SoundSystemSources", MaxSoundGameObjects);
                m_SoundSystem.Init(audioListener.transform, MaxSoundEmitters, SoundGameObjects, AudioMixer);
            }
        }

        private async void Start() {
            if (!m_IsHeadless && GetComponent<MoonkovAudioEnvironment>() == null)
                gameObject.AddComponent<MoonkovAudioEnvironment>();
            // 为所有 UI Toolkit 画面接入按钮音效，包括未主动绑定的页面。
            if (!m_IsHeadless && GetComponent<MoonkovUIAudio>() == null) gameObject.AddComponent<MoonkovUIAudio>();
            Application.runInBackground = true; //多人模式把应用放到后台时，保持连接运行。

#if UNITY_SERVER || UNITY_EDITOR
            // 只运行服务端时，把启动交给 ServerBootstrap。普通客户端才需要继续打开菜单相机、等待菜单资源。
            if (GhostBridgeBootstrap.IsServerOnly) {
                if (SceneManager.GetActiveScene().name == MainMenuSceneName && GetComponent<ServerBootstrap>() == null)
                    gameObject.AddComponent<ServerBootstrap>();
                return;
            }
#endif
            MainCameraSingleton.Instance.GetComponent<Camera>().enabled = true;
            var audioListener = MainCameraSingleton.Instance.GetComponent<AudioListener>();
            if (audioListener != null) m_SoundSystem.SetListenerTransform(audioListener.transform);
            GameSettings.Instance.MainMenuSceneLoaded = false;
            if (SceneManager.GetActiveScene().name == "MainMenu") {
                m_LoadingMainMenuCancel = new CancellationTokenSource();
                try {
                    m_LoadingMainMenu = StartMainMenuAsync(m_LoadingMainMenuCancel.Token);
                    await m_LoadingMainMenu;
                } catch (OperationCanceledException) {
                    // 取消任务后直接结束这一条流程。
                } finally {
                    m_LoadingMainMenuCancel.Dispose();
                    m_LoadingMainMenuCancel = null;
                }
            }

            // 保证这组资源只加载一次。
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
                await SceneManager.LoadSceneAsync("Scenes/Persistents", LoadSceneMode.Additive);
        }

        public void Update() {
            if (m_SoundSystem != null) m_SoundSystem.UpdateSoundSystem(false);
        }

        private async Task StartMainMenuAsync(CancellationToken cancellationToken) {
            DestroyLocalSimulationWorld();
            // 回菜单时清掉上一局的角色网格拟合缓存。菜单用自己的角色预览，常驻场景到位后即可解除等待。
            EnergyShieldFittingData.Clear();
            // 全屏菜单有自己的角色和背景预览，
            // 此时加载旧样例地图与 NetCode 客户端 World 会推迟菜单可用的时刻。
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
                await SceneManager.LoadSceneAsync("Scenes/Persistents", LoadSceneMode.Additive);
            cancellationToken.ThrowIfCancellationRequested();
            GameSettings.Instance.MainMenuSceneLoaded = true;
        }

        /// <summary>
        /// 从这里开始游戏会话。
        /// </summary>
        /// <remarks>
        /// 这是异步方法，因为需要等待 <see cref="GameConnection"/> 和 <see cref="ScenesLoader"/> 等入口。
        /// </remarks>
        public void StartGameAsync(CreationType creationType) { RequestGameAsync(creationType, false); }

        public void ReconnectLastGameAsync() {
            if (SessionReconnect.Available) RequestGameAsync(SessionReconnect.Restore(), true);
        }

        private async void RequestGameAsync(CreationType creationType, bool resume) {
            // 进局和退局都要异步等许多步，这个标记防止连续点击再启动第二条流程。ResumeOnly 会跟着重连请求发送，后续可以据此只找原来的战局。
            if (m_StartGameRequested || m_ReturningToMainMenu) return;
            m_StartGameRequested = true;
            ConnectionSettings.Instance.ResumeOnly = resume;
            try { await StartRequestedGameAsync(creationType); } finally { m_StartGameRequested = false; }
        }

        private async Task StartRequestedGameAsync(CreationType creationType) {
            if (!IsHeadless && !PlayerProfileClient.IsLoggedIn) {
                Debug.LogWarning("Sign in before starting a raid.");
                return;
            }
            // 入局入口先与当前档案核对。好友房用房间档案和票据，单人用本机档案，账号联机用自己的线上档案。
            bool lan = creationType == CreationType.LanHost || creationType == CreationType.LanJoin;
            if (lan != PlayerProfileClient.IsLan ||
                (lan && (!LanRoomClient.Active || string.IsNullOrEmpty(LanRoomClient.Ticket)))) {
                Debug.LogWarning("Enter LAN raids through the room lobby.");
                return;
            }
            if (creationType == CreationType.Offline != PlayerProfileClient.IsOffline ||
                (creationType == CreationType.Offline && !PlayerProfileClient.SupportsSinglePlayer)) {
                Debug.LogWarning("Select the matching online or single-player profile before starting a raid.");
                return;
            }
            if (GameSettings.Instance.GameState != GlobalGameState.MainMenu) {
                Debug.Log("[StartGameAsync] Called but in-game, cannot start while in-game!");
                return;
            }
            var mode = GameSettings.Instance.RequestedMode;
            bool arena = ArenaMatch.UsesTemporaryKit(mode);
            if (arena && !lan && !(mode == RaidGameMode.Classic && creationType == CreationType.Offline)) {
                ConnectionSettings.Instance.ConnectionError = "竞技场请从好友房间进入。";
                return;
            }
            if (mode == RaidGameMode.Classic && !ClassicDust2Map.Available) {
                ConnectionSettings.Instance.ConnectionError = "本机缺少沙2地图。";
                if (lan) await LanRoomClient.ReturnedToMenuAsync();
                return;
            }
            if (!IsHeadless && !arena) {
                // 进入持久化战局前先把菜单里的装备变动交给档案。等待期间如果换了档案，就停下这次入场，避免混用两个档案的装备。
                string scope = PlayerProfileClient.LocalPreferenceScope;
                try {
                    using var sync = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await PlayerProfileClient.FlushMenuInventoryAsync(sync.Token);
                    if (scope != PlayerProfileClient.LocalPreferenceScope) return;
                } catch (Exception ex) {
                    ConnectionSettings.Instance.ConnectionError = ex.Message;
                    return;
                }
            }
            Debug.Log($"[{nameof(StartGameAsync)}] Called with creation type '{creationType}'");
            ConnectionSettings.Instance.ConnectionError = null;
            ConnectionSettings.Instance.ModeConfirmed = false;
            if (creationType == CreationType.JoinRelay && !ConnectionSettings.Instance.IsSessionCodeFormatValid) {
                ConnectionSettings.Instance.ConnectionError = "请输入六位英文字母或数字房间码。";
                return;
            }
            if ((creationType == CreationType.CreateOrJoin || creationType == CreationType.Host) &&
                !PlayerProfileClient.SupportsSinglePlayer) {
                ConnectionSettings.Instance.ConnectionError = "此客户端不能创建房间，请使用房间码或服务器地址加入。";
                return;
            }
            // 房主和手输地址进入时，这里先让菜单显示相应的弹窗。用户确认或取消会结束这次等待，界面随后切回正常菜单状态。
            if (creationType == CreationType.Host) {
                GameSettings.Instance.CancellableUserInputPopUp = new AwaitableCompletionSource();
                GameSettings.Instance.MainMenuState = MainMenuState.StartHostPopup;
                try { await GameSettings.Instance.CancellableUserInputPopUp.Awaitable; } catch
                    (OperationCanceledException) { return; } finally {
                    GameSettings.Instance.MainMenuState = MainMenuState.MainMenuScreen;
                }
            } else if (creationType == CreationType.ConnectAndJoin && !ConnectionSettings.Instance.ResumeOnly &&
                       !OnlineLobbyClient.InInstance) {
                GameSettings.Instance.CancellableUserInputPopUp = new AwaitableCompletionSource();
                GameSettings.Instance.MainMenuState = MainMenuState.DirectConnectPopUp;
                try { await GameSettings.Instance.CancellableUserInputPopUp.Awaitable; } catch
                    (OperationCanceledException) { return; } finally {
                    GameSettings.Instance.MainMenuState = MainMenuState.MainMenuScreen;
                }
            }
            // 离线远征加载中可能先锁定出战装备。这个检查点让加载失败时能恢复装备，真正进局后才提交。
            m_OfflineLoadingCheckpoint = creationType == CreationType.Offline && !arena
                ? PlayerProfileClient.OfflineStore.BeginLoadingCheckpoint()
                : null;
            BeginEnteringGame();
            m_LoadingGameCancel = new CancellationTokenSource();
            try {
                // 把加载任务和它的取消开关一起记下来。中途点返回时 GameFlowControl 可以先叫停任务，等它结束再拆场景。
                m_LoadingGame = StartGameAsync(creationType, m_LoadingGameCancel.Token);
                await m_LoadingGame;
            } catch (OperationCanceledException) {
                Debug.Log($"[{nameof(StartGameAsync)}] Loading has been cancelled.");
                // 取消加载和加载异常都会给大厅保留这次进房失败的信息。回到原房间后还能看到入口并重试。
                OnlineLobbyClient.PreserveFailedJoin();
                return;
            } catch (Exception e) {
                Debug.LogError($"[{nameof(StartGameAsync)}] Loading has failed, returning to main menu");
                Debug.LogException(e);
                OnlineLobbyClient.PreserveFailedJoin();
                ConnectionSettings.Instance.ConnectionError ??= e is TimeoutException || e is InvalidOperationException
                    ? e.Message
                    : "Unable to start this session. Please try again.";
                // 异常已经处理过，先释放取消令牌。之后的 ReturnToMainMenu 无需再看这个令牌。
                m_LoadingGameCancel.Dispose();
                m_LoadingGameCancel = null;
                ReturnToMainMenuAsync();
                return;
            } finally {
                m_LoadingGameCancel?.Dispose();
                m_LoadingGameCancel = null;
            }
            // 成功走完入场才记下这条连接方式作为可恢复入口。此时再切成 InGame，开始显示局内界面。
            SessionReconnect.Remember(creationType);
            FinishLoadingGame();
        }

        private void BeginEnteringGame() {
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.StartLoading);
        }

        private async Task StartGameAsync(CreationType creationType, CancellationToken cancellationToken) {
            if (creationType == CreationType.CreateOrJoin || creationType == CreationType.Host)
                OnlineTransportSecurity.RequireTrustedOnlineHost();
            // 新战局开始前先结束仍在加载的菜单，再把菜单可能留下的旧连接与 World 收掉。等这些操作结束后，才建立本次连接。
            if (m_LoadingMainMenuCancel != null || GameSettings.Instance.MainMenuSceneLoaded) {
                // 卸载之前已经创建的 MainMenu World。
                if (m_LoadingMainMenuCancel != null) {
                    m_LoadingMainMenuCancel.Cancel();
                    try { await m_LoadingMainMenu; } catch (OperationCanceledException) {
                        // 这里的取消是流程主动触发的，直接继续后续步骤。
                    }
                }

                //断开连接并离开原会话，从而卸载旧客户端 World。
                if (GameSettings.Instance.MainMenuSceneLoaded) await DisconnectAndUnloadWorlds();
                cancellationToken.ThrowIfCancellationRequested();
            }

            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);
            GameConnection = await GameConnection.OpenAsync(creationType, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (GameConnection.Session != null) {
                GameConnection.Session.RemovedFromSession += OnSessionLeft;
                ConnectionSettings.Instance.SessionCode = GameConnection.Session.Code;
            }
            var (server, client) = RaidWorldBootstrap.Create(GameConnection, creationType);
            if (server != null) {
                RaidWorldBootstrap.Listen(GameConnection, creationType, server, client);
                await ScenesLoader.LoadGameplayAsync(server, null);
                if (creationType == CreationType.LanHost)
                    await LanRoomClient.RequestAsync("listening", ct: cancellationToken);
            }

            if (client == null) {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            ConnectionSettings.Instance.GameJoinTicket = null;
            ConnectionSettings.Instance.ConnectionEndpoint = GameConnection.ConnectEndpoint;
            // 传输连通以后，客户端才能继续加载自己的 ECS 数据和表现资源。连通、收齐地图对象和服务端准许入场分别有各自的等待点。
            await WaitForPlayerConnectionAsync(cancellationToken);
            await ScenesLoader.LoadGameplayAsync(null, client);
            // 卸掉前一张图的场景只解开引用。这里额外释放不用的资源，再准备新一局会马上用到的资源，尽量把开销留在加载界面。
            await Resources.UnloadUnusedAssets();
            cancellationToken.ThrowIfCancellationRequested();
            // 地图加载完成后提前读角色素材、灯光和字体，让进入游戏时少碰到临时加载。
            await PrepareCharacterResourcesAsync(client, cancellationToken);
            foreach (var lighting in FindObjectsByType<Moon.SourceWorldLighting>())
                await lighting.PrepareResourcesAsync(cancellationToken);
            await MoonkovLocalization.PrepareGameplayFontAsync(cancellationToken);
            // 入局票有时效。地图加载可能花很久，所以靠近发送入局请求时再向账号服务取票。
            if (!PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan)
                ConnectionSettings.Instance.GameJoinTicket =
                    await AccountClient.CreateGameTicketAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // 本机读完资源后还要等服务器的 Ghost 陆续到齐。非普通远征玩法还需要拿到玩法批准，拒绝会带原因结束加载。
            await WaitForGhostReplicationAsync(client, cancellationToken);
            if (GameSettings.Instance.RequestedMode != RaidGameMode.Expedition)
                await WaitForModeApprovalAsync(client, cancellationToken);
        }

        public static void ConfigureRaidMode(World world) { RaidWorldBootstrap.ConfigureRaidMode(world); }

        private static async Task WaitForModeApprovalAsync(World world, CancellationToken cancellationToken) {
            using var query = world.EntityManager.CreateEntityQuery(typeof(BountyClashClientState));
            double started = Time.realtimeSinceStartupAsDouble;
            while (true) {
                CheckLoadingConnection(started, "Server did not confirm the selected game mode.");
                if (!query.IsEmptyIgnoreFilter) {
                    var state =
                        world.EntityManager.GetComponentObject<BountyClashClientState>(query.GetSingletonEntity());
                    // 客户端收到批准或拒绝都能结束等待。拒绝把理由上抛给入场流程，避免继续等一个永远不会开始的比赛。
                    if (state.ApprovalReceived) {
                        if (!state.Approved) throw new InvalidOperationException(state.ApprovalReason);
                        return;
                    }
                }
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        /// <summary>
        /// 等绝大多数 Ghost 实体已经创建后再进入场景。
        /// 否则加载完成时，会看到很多对象在眼前陆续出现。
        /// </summary>
        public static async Task WaitForGhostReplicationAsync(
            World world,
            CancellationToken cancellationToken = default) {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WorldReplication);
            using var ghostCountQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostCount>());
            double started = Time.realtimeSinceStartupAsDouble;
            while (true) {
                CheckLoadingConnection(started, "World synchronization timed out. Please reconnect.");
                ghostCountQuery.CompleteDependency();
                if (ghostCountQuery.TryGetSingleton<GhostCount>(out var ghostCount) && ghostCount.IsCreated) {
                    // 用服务器拥有的对象数和本地已经生成的对象数算同步进度。时间和断线检查仍在循环里，卡住时能结束等待。
                    float synchronizingPercentage = SessionConnectionPolicy.ReplicationProgress(
                        ghostCount.GhostCountOnServer, ghostCount.GhostCountInstantiatedOnClient,
                        Time.realtimeSinceStartupAsDouble - started);
                    LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WorldReplication,
                        synchronizingPercentage);
                    if (synchronizingPercentage > 0.99f) return;
                }
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        public static async Task WaitForPlayerConnectionAsync(CancellationToken cancellationToken = default) {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WaitingConnection);
            // 传输层会重试握手。会话管理器负责限制总等待时间。
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Connecting;
            double started = Time.realtimeSinceStartupAsDouble;
            // 这里只等连接状态变化。握手由网络系统推进，外层用现实时间限制等待，错误信息会指向此次实际使用的路线。
            while (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connecting) {
                if (Time.realtimeSinceStartupAsDouble - started >= SessionConnectionPolicy.TimeoutSeconds) {
                    ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
                    if (GameConnection?.PeerRoom != null) throw new TimeoutException("跨网战局连接超时，已返回房间。");
                    throw new TimeoutException(GameConnection?.IceSession != null
                        ? "P2P 游戏连接超时，本局已断开，不会改走 VPS。请重试或在下一局更换连接方式。"
                        : $"连接 {ConnectionSettings.Instance.IPAddress}:{ConnectionSettings.Instance.Port} 超时。请在队伍中重试，并确认公网 UDP 转发可用。");
                }
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Connected)
                throw new InvalidOperationException(ConnectionSettings.Instance.ConnectionError ??
                                                    "Connection lost. Return to the ship and reconnect.");
        }

        private static void CheckLoadingConnection(double started, string timeoutMessage) {
            if (ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Connected)
                throw new InvalidOperationException(ConnectionSettings.Instance.ConnectionError ??
                                                    "Connection lost. Return to the ship and reconnect.");
            if (Time.realtimeSinceStartupAsDouble - started >= SessionConnectionPolicy.TimeoutSeconds)
                throw new TimeoutException(timeoutMessage);
        }

        // 最后提交离线装备检查点并把进度拉到完成。下一次切场景或遇到退出时，这次检查点就已经完成。
        private void FinishLoadingGame() {
            m_OfflineLoadingCheckpoint?.Complete();
            m_OfflineLoadingCheckpoint = null;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            GameSettings.Instance.GameState = GlobalGameState.InGame;
        }

        /// <summary>
        /// 建立新的服务端与客户端 World 前，先清掉本地旧的模拟 World。
        /// </summary>
        public static void DestroyLocalSimulationWorld() { RaidWorldBootstrap.DestroyLocalSimulationWorld(); }

        public static void SetGameConnection(GameConnection gameConnection) { GameConnection = gameConnection; }
    }
}