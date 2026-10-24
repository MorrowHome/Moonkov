using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.GhostBridge;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    public partial class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public const int MaxPlayer = 32;
        public static int RequestedMaxPlayers => GameSettings.Instance.RequestedMode == RaidGameMode.BountyClash
            ? BountyClashRules.MaxPlayers : MaxPlayer;
        public const string MainMenuSceneName = "MainMenu";
        public const string MenuPreviewSceneName = "GameScene";
        // Shared by offline/Host, remote clients and the dedicated server.
        // Apollo12 and MoonGameScene remain available as authored fallbacks.
        public const string GameSceneName = "TheophilusGameScene";
        static public GameConnection GameConnection { get; private set; }

        Task m_LoadingGame;
        CancellationTokenSource m_LoadingGameCancel;
        Task m_LoadingMainMenu;
        CancellationTokenSource m_LoadingMainMenuCancel;
        bool m_StartGameRequested;

        public UnityEngine.Audio.AudioMixer AudioMixer;
        public int MaxSoundEmitters;
        public int MaxSoundGameObjects;
        [System.NonSerialized]
        public SoundGameObjectPool SoundGameObjects;

        ISoundSystem m_SoundSystem;
        public ISoundSystem SoundSystem => m_SoundSystem;

        bool m_IsHeadless = false;
        public bool IsHeadless => m_IsHeadless;

        public static bool CanUseMainMenu => SceneManager.GetActiveScene().name == MainMenuSceneName;

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogError($"Multiple instances of '{this}' violates the Singleton pattern!", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

#if UNITY_STANDALONE_LINUX
            m_IsHeadless = true;
#else
            var commandLineArgs = new List<string>(System.Environment.GetCommandLineArgs());
            m_IsHeadless = commandLineArgs.Contains("-batchmode");
#endif
            m_IsHeadless |= GhostBridgeBootstrap.IsServerOnly;
            ConfigVar.Init();

            if (m_IsHeadless)
            {
                m_SoundSystem = new SoundSystemNull();
            }
            else
            {
                m_SoundSystem = new SoundSystem();
                AudioListener audioListener = MainCameraSingleton.Instance.GetComponent<AudioListener>();
                SoundGameObjects = new SoundGameObjectPool("SoundSystemSources", MaxSoundGameObjects);
                m_SoundSystem.Init(audioListener.transform, MaxSoundEmitters, SoundGameObjects, AudioMixer);
            }
        }
        
        async void Start()
        {
            if (!m_IsHeadless && GetComponent<MoonkovAudioEnvironment>() == null)
                gameObject.AddComponent<MoonkovAudioEnvironment>();
            // Keeps every UI Toolkit screen audible, including ones that never opt in.
            if (!m_IsHeadless && GetComponent<MoonkovUIAudio>() == null)
                gameObject.AddComponent<MoonkovUIAudio>();
            Application.runInBackground = true; //Prevents dropped connections during multiplayer gameplay

            // The Editor's Server play type must use the same startup path as
            // the dedicated server, rather than creating a main-menu client world.
#if UNITY_SERVER || UNITY_EDITOR
            if (GhostBridgeBootstrap.IsServerOnly)
            {
                if (SceneManager.GetActiveScene().name == MainMenuSceneName &&
                    GetComponent<ServerBootstrap>() == null)
                {
                    gameObject.AddComponent<ServerBootstrap>();
                }
                return;
            }
#endif

            MainCameraSingleton.Instance.GetComponent<Camera>().enabled = true;
            var audioListener = MainCameraSingleton.Instance.GetComponent<AudioListener>();
            if (audioListener != null)
            {
                m_SoundSystem.SetListenerTransform(audioListener.transform);
            }

            GameSettings.Instance.MainMenuSceneLoaded = false;
            if (SceneManager.GetActiveScene().name == "MainMenu")
            {
                m_LoadingMainMenuCancel = new CancellationTokenSource();
                try
                {
                    m_LoadingMainMenu = StartMainMenuAsync(m_LoadingMainMenuCancel.Token);
                    await m_LoadingMainMenu;
                }
                catch (OperationCanceledException)
                {
                    // Nothing to do when the task is cancelled.
                }
                finally
                {
                    m_LoadingMainMenuCancel.Dispose();
                    m_LoadingMainMenuCancel = null;
                }
            }

            // Ensures it only ever loads once
            if (!SceneManager.GetSceneByName("Persistents").isLoaded)
            {
                SceneManager.LoadScene("Scenes/Persistents", LoadSceneMode.Additive);
            }
        }

        public void Update()
        {
            if (m_SoundSystem != null)
            {
                m_SoundSystem.UpdateSoundSystem(false);
            }
        }

        async Task StartMainMenuAsync(CancellationToken cancellationToken)
        {
            DestroyLocalSimulationWorld();
            var clientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            await ScenesLoader.LoadMenuPreviewAsync(clientWorld);
            GameSettings.Instance.MainMenuSceneLoaded = true;
            cancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// This method start the Gameplay session.
        /// </summary>
        /// <remarks>
        /// It is an asynchronous method because it is waiting on several API like <see cref="GameConnection"/> and <see cref="ScenesLoader"/>.
        /// </remarks>
        public void StartGameAsync(CreationType creationType) => RequestGameAsync(creationType, false);
        public void ReconnectLastGameAsync()
        {
            if (SessionReconnect.Available) RequestGameAsync(SessionReconnect.Restore(), true);
        }
        async void RequestGameAsync(CreationType creationType, bool resume)
        {
            if (m_StartGameRequested || m_ReturningToMainMenu) return;
            m_StartGameRequested = true;
            ConnectionSettings.Instance.ResumeOnly = resume;
            try { await StartRequestedGameAsync(creationType); }
            finally { m_StartGameRequested = false; }
        }

        async Task StartRequestedGameAsync(CreationType creationType)
        {
            if (!IsHeadless && !PlayerProfileClient.IsLoggedIn)
            {
                Debug.LogWarning("Sign in before starting a raid.");
                return;
            }
            bool lan = creationType == CreationType.LanHost || creationType == CreationType.LanJoin;
            if (lan != PlayerProfileClient.IsLan || (lan && (!LanRoomClient.Active || string.IsNullOrEmpty(LanRoomClient.Ticket))))
            {
                Debug.LogWarning("Enter LAN raids through the room lobby.");
                return;
            }
            if ((creationType == CreationType.Offline) != PlayerProfileClient.IsOffline ||
                (creationType == CreationType.Offline && !PlayerProfileClient.SupportsSinglePlayer))
            {
                Debug.LogWarning("Select the matching online or single-player profile before starting a raid.");
                return;
            }
            if (GameSettings.Instance.GameState != GlobalGameState.MainMenu)
            {
                Debug.Log("[StartGameAsync] Called but in-game, cannot start while in-game!");
                return;
            }

            Debug.Log($"[{nameof(StartGameAsync)}] Called with creation type '{creationType}'");
            ConnectionSettings.Instance.ConnectionError = null;
            ConnectionSettings.Instance.ModeConfirmed = false;
            if (creationType == CreationType.JoinRelay && !ConnectionSettings.Instance.IsSessionCodeFormatValid)
            {
                ConnectionSettings.Instance.ConnectionError = "请输入六位英文字母或数字房间码。";
                return;
            }
            if ((creationType == CreationType.CreateOrJoin || creationType == CreationType.Host) && !PlayerProfileClient.SupportsSinglePlayer)
            {
                ConnectionSettings.Instance.ConnectionError = "此客户端不能创建房间，请使用房间码或服务器地址加入。";
                return;
            }

            if (creationType == CreationType.Host)
            {
                GameSettings.Instance.CancellableUserInputPopUp = new AwaitableCompletionSource();
                GameSettings.Instance.MainMenuState = MainMenuState.StartHostPopup;
                try
                {
                    await GameSettings.Instance.CancellableUserInputPopUp.Awaitable;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    GameSettings.Instance.MainMenuState = MainMenuState.MainMenuScreen;
                }
            }
            else if (creationType == CreationType.ConnectAndJoin && !ConnectionSettings.Instance.ResumeOnly)
            {
                GameSettings.Instance.CancellableUserInputPopUp = new AwaitableCompletionSource();
                GameSettings.Instance.MainMenuState = MainMenuState.DirectConnectPopUp;
                try
                {
                    await GameSettings.Instance.CancellableUserInputPopUp.Awaitable;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    GameSettings.Instance.MainMenuState = MainMenuState.MainMenuScreen;
                }
            }

            m_OfflineLoadingCheckpoint = creationType == CreationType.Offline
                ? PlayerProfileClient.OfflineStore.BeginLoadingCheckpoint() : null;
            BeginEnteringGame();

            m_LoadingGameCancel = new CancellationTokenSource();
            try
            {
                m_LoadingGame = StartGameAsync(creationType, m_LoadingGameCancel.Token);
                await m_LoadingGame;
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[{nameof(StartGameAsync)}] Loading has been cancelled.");
                return;
            }
            catch (Exception e)
            {
                Debug.LogError($"[{nameof(StartGameAsync)}] Loading has failed, returning to main menu");
                Debug.LogException(e);
                ConnectionSettings.Instance.ConnectionError ??= e is TimeoutException || e is InvalidOperationException
                    ? e.Message : "Unable to start this session. Please try again.";
                // Disposing the token here because the error has been handled and ReturnToMainMenu should not check it.
                m_LoadingGameCancel.Dispose();
                m_LoadingGameCancel = null;
                ReturnToMainMenuAsync();
                return;
            }
            finally
            {
                m_LoadingGameCancel?.Dispose();
                m_LoadingGameCancel = null;
            }

            SessionReconnect.Remember(creationType);
            FinishLoadingGame();
        }

        void BeginEnteringGame()
        {
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.StartLoading);
        }

        async Task StartGameAsync(CreationType creationType, CancellationToken cancellationToken)
        {
            if (creationType == CreationType.CreateOrJoin || creationType == CreationType.Host)
                OnlineTransportSecurity.RequireTrustedOnlineHost();
            if (m_LoadingMainMenuCancel != null || GameSettings.Instance.MainMenuSceneLoaded)
            {
                // Unload already created MainMenu world 
                if (m_LoadingMainMenuCancel != null)
                {
                    m_LoadingMainMenuCancel.Cancel();
                    try
                    {
                        await m_LoadingMainMenu;
                    }
                    catch (OperationCanceledException)
                    {
                        // We are ignoring the cancelled exception as it is expected.
                    }
                }

                //Unload created client world by disconnecting and leaving the current session.
                if (GameSettings.Instance.MainMenuSceneLoaded)
                {
                    await DisconnectAndUnloadWorlds();
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            // Connecting to a Multiplayer Session.
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.InitializeConnection);
            switch (creationType)
            {
                case (CreationType.CreateOrJoin): //Relay
                    {
                        GameConnection = await GameConnection.CreateorJoinGameAsync(cancellationToken);
                        break;
                    }
                case CreationType.JoinRelay:
                    GameConnection = await GameConnection.JoinGameAsync(cancellationToken);
                    break;
                case CreationType.LanHost:
                case (CreationType.Host): //Direct connection - host
                    {
                        GameConnection = await GameConnection.HostGameAsync();
                        break;
                    }
                case CreationType.Offline:
                    GameConnection = await GameConnection.OfflineGameAsync();
                    break;
                case CreationType.LanJoin:
                case (CreationType.ConnectAndJoin): //Direct connection - client
                    {
                        GameConnection = await GameConnection.ConnectGameAsync();
                        break;
                    }
            }

            cancellationToken.ThrowIfCancellationRequested();

            World server = null, client = null;
            if (GameConnection.Session != null)
            {
                GameConnection.Session.RemovedFromSession += OnSessionLeft;
                ConnectionSettings.Instance.SessionCode = GameConnection.Session.Code;
                CreateEntityWorlds(GameConnection.Session, GameConnection.SessionConnectionType, out server, out client);
            }
            else
            {
                CreateEntityWorlds(creationType == CreationType.Host || creationType == CreationType.LanHost || creationType == CreationType.Offline, out server, out client);
                if (creationType == CreationType.LanHost) server.EntityManager.CreateEntity(typeof(LanRaidMode));
                if (creationType == CreationType.LanHost || creationType == CreationType.LanJoin) client.EntityManager.CreateEntity(typeof(LanRaidMode));
                if (creationType == CreationType.Offline)
                {
                    server.EntityManager.CreateEntity(typeof(OfflineRaidMode));
                    client.EntityManager.CreateEntity(typeof(OfflineRaidMode));
                    if (GameSettings.Instance.RequestedMode == RaidGameMode.BountyClash)
                    {
                        server.EntityManager.CreateEntity(typeof(ClashSoloMode));
                        client.EntityManager.CreateEntity(typeof(ClashSoloMode));
                    }
                }
            }

            // If the server was created successfully, start listening.
            if (server != null)
            {
                using var drvQuery = server.EntityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkStreamDriver>());
                drvQuery.CompleteDependency();
                var serverDriver = drvQuery.GetSingletonRW<NetworkStreamDriver>();
                if (!serverDriver.ValueRW.Listen(GameConnection.ListenEndpoint))
                    throw new InvalidOperationException("Cannot start server. The port may be in use.");
                if (creationType == CreationType.Offline)
                {
                    // NetCode may choose IPC or UDP for the local client (e.g. the
                    // Editor network simulator forces UDP). Port 0 gives each server
                    // driver a different port, so select the matching transport.
                    using var clientDriverQuery = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamDriver>());
                    var clientDrivers = clientDriverQuery.GetSingleton<NetworkStreamDriver>().DriverStore;
                    var transport = clientDrivers.GetDriverType(clientDrivers.FirstDriver);
                    var serverDrivers = serverDriver.ValueRO.DriverStore;
                    bool matched = false;
                    for (int id = serverDrivers.FirstDriver; id < serverDrivers.LastDriver; id++)
                        if (serverDrivers.GetDriverType(id) == transport)
                        {
                            GameConnection.UseLocalServerEndpoint(serverDriver.ValueRW.GetLocalEndPoint(id));
                            matched = true; break;
                        }
                    if (!matched) throw new InvalidOperationException("No matching local server transport.");
                }
                await ScenesLoader.LoadGameplayAsync(server, null);
                if (creationType == CreationType.LanHost) await LanRoomClient.RequestAsync("listening", ct:cancellationToken);
            }

            //The the client world was created, connect to the server
            if (client != null)
            {
                ConnectionSettings.Instance.GameJoinTicket = PlayerProfileClient.IsOffline || PlayerProfileClient.IsLan
                    ? null : await AccountClient.CreateGameTicketAsync(cancellationToken);
                ConnectionSettings.Instance.ConnectionEndpoint = GameConnection.ConnectEndpoint;
                await WaitForPlayerConnectionAsync(cancellationToken);
                await ScenesLoader.LoadGameplayAsync(null, client);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (client != null)
            {
                await WaitForGhostReplicationAsync(client, cancellationToken);
                if (GameSettings.Instance.RequestedMode == RaidGameMode.BountyClash)
                    await WaitForModeApprovalAsync(client, cancellationToken);
            }
        }

        public static void ConfigureRaidMode(World world)
        {
            if (world == null || !world.IsCreated) return;
            using var query = world.EntityManager.CreateEntityQuery(typeof(RaidModeState));
            if (!query.IsEmptyIgnoreFilter) return;
            var entity = world.EntityManager.CreateEntity(typeof(RaidModeState));
            world.EntityManager.SetComponentData(entity, new RaidModeState
            { Mode = GameSettings.Instance.RequestedMode, RulesVersion = GameSettings.Instance.RequestedRulesVersion });
        }

        private static async Task WaitForModeApprovalAsync(World world, CancellationToken cancellationToken)
        {
            using var query = world.EntityManager.CreateEntityQuery(typeof(BountyClashClientState));
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                CheckLoadingConnection(started, "Server did not confirm Bounty Clash rules. Check the server mode and version.");
                if (!query.IsEmptyIgnoreFilter)
                {
                    var state = world.EntityManager.GetComponentObject<BountyClashClientState>(query.GetSingletonEntity());
                    if (state.ApprovalReceived)
                    {
                        if (!state.Approved) throw new InvalidOperationException(state.ApprovalReason);
                        return;
                    }
                }
                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Wait until the vast majority of ghosts have been spawned.
        /// If we don't do this, we'll see a bunch of ghosts pop in as the scene loads.
        /// </summary>
        /// <param name="world"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task WaitForGhostReplicationAsync(World world, CancellationToken cancellationToken = default)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WorldReplication);
            using var ghostCountQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GhostCount>());
            double started = Time.realtimeSinceStartupAsDouble;
            while (true)
            {
                CheckLoadingConnection(started, "World synchronization timed out. Please reconnect.");
                ghostCountQuery.CompleteDependency();
                if (ghostCountQuery.TryGetSingleton<GhostCount>(out var ghostCount) && ghostCount.IsCreated)
                {
                    var synchronizingPercentage = SessionConnectionPolicy.ReplicationProgress(
                        ghostCount.GhostCountOnServer, ghostCount.GhostCountInstantiatedOnClient,
                        Time.realtimeSinceStartupAsDouble - started);

                    LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WorldReplication, synchronizingPercentage);
                    if (synchronizingPercentage > 0.99f)
                    {
                        return;
                    }
                }

                await Awaitable.NextFrameAsync(cancellationToken);
            }
        }

        static async Task WaitForAttachedCameraAsync(World world, CancellationToken cancellationToken = default)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WaitingOnPlayer);
            using var mainEntityCameraQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MainCamera>());
            double started = Time.realtimeSinceStartupAsDouble;
            while (!mainEntityCameraQuery.HasSingleton<MainCamera>())
            {
                CheckLoadingConnection(started, "Player spawn timed out. Please reconnect.");
                await Awaitable.NextFrameAsync(cancellationToken);
            }

            // Waiting an extra frame so that the player position is properly synced with the server.
            await Awaitable.NextFrameAsync(cancellationToken);
        }

        static void CreateEntityWorlds(ISession session, NetworkType connectionType,
            out World serverWorld, out World clientWorld)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            DestroyLocalSimulationWorld();

#if UNITY_EDITOR
            if (connectionType == NetworkType.Relay && MultiplayerPlayModePreferences.RequestedNumThinClients > 0)
            {
                Debug.Log($"[{nameof(CreateEntityWorlds)}] ThinClient is disabled when Relay connection is used.");
                MultiplayerPlayModePreferences.RequestedNumThinClients = 0;
            }
#endif

            serverWorld = null;
            clientWorld = null;
            if (session.IsHost)
            {
                serverWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
                ConfigureRaidMode(serverWorld);
                GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            }

            if (!session.IsServer())
            {
                clientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
                ConfigureRaidMode(clientWorld);
                GhostGameObjectUpdateSystem.GatherWorldSystems(clientWorld);
            }
        }

        static void CreateEntityWorlds(bool isHost, out World serverWorld, out World clientWorld)
        {
            NetworkStreamReceiveSystem.DriverConstructor = new EntityDriverConstructor(
                isHost ? NetworkRole.Host : NetworkRole.Client, !PlayerProfileClient.IsOffline && !PlayerProfileClient.IsLan);
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.CreateWorld);
            DestroyLocalSimulationWorld();

            serverWorld = null;
            clientWorld = null;
            if (isHost)
            {
                serverWorld = ClientServerBootstrap.CreateServerWorld("ServerWorld");
                ConfigureRaidMode(serverWorld);
                GhostGameObjectUpdateSystem.GatherWorldSystems(serverWorld);
            }

            clientWorld = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            ConfigureRaidMode(clientWorld);
            GhostGameObjectUpdateSystem.GatherWorldSystems(clientWorld);
#if UNITY_EDITOR
            Debug.Log($"[{nameof(CreateEntityWorlds)}] ThinClient number is {MultiplayerPlayModePreferences.RequestedNumThinClients}");
#endif
        }

        public static async Task WaitForPlayerConnectionAsync(CancellationToken cancellationToken = default)
        {
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.WaitingConnection);
            // The transport retries its handshake; the session owner bounds the wait.
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Connecting;
            double started = Time.realtimeSinceStartupAsDouble;
            while (ConnectionSettings.Instance.GameConnectionState == ConnectionState.State.Connecting)
            {
                if (Time.realtimeSinceStartupAsDouble - started >= SessionConnectionPolicy.TimeoutSeconds)
                {
                    ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
                    throw new TimeoutException("Server connection timed out. Check the address and try again.");
                }
                await Awaitable.NextFrameAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Connected)
                throw new InvalidOperationException(ConnectionSettings.Instance.ConnectionError ?? "Connection lost. Return to the ship and reconnect.");
        }

        static void CheckLoadingConnection(double started, string timeoutMessage)
        {
            if (ConnectionSettings.Instance.GameConnectionState != ConnectionState.State.Connected)
                throw new InvalidOperationException(ConnectionSettings.Instance.ConnectionError ?? "Connection lost. Return to the ship and reconnect.");
            if (Time.realtimeSinceStartupAsDouble - started >= SessionConnectionPolicy.TimeoutSeconds)
                throw new TimeoutException(timeoutMessage);
        }

        void FinishLoadingGame()
        {
            m_OfflineLoadingCheckpoint?.Complete();
            m_OfflineLoadingCheckpoint = null;
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.LoadingDone);
            GameSettings.Instance.GameState = GlobalGameState.InGame;
        }

        /// <summary>
        /// Destroy all local game simulation worlds if any before creating new server/client worlds.
        /// </summary>
        public static void DestroyLocalSimulationWorld()
        {
            foreach (var world in World.All)
            {
                if (world.Flags == WorldFlags.Game)
                {
                    world.Dispose();
                    break;
                }
            }
        }

        public static void SetGameConnection(GameConnection gameConnection)
        {
            GameConnection = gameConnection;
        }

    }
}
