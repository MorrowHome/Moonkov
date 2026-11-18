// 离开战局、加载失败和退出程序都在这里收尾。先断开会话和 World，等存档处理完成，再退回大厅或关闭程序。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial class GameManager : MonoBehaviour
    {
        private bool m_ReturningToMainMenu;
        private OfflineProfileStore.LoadingCheckpoint m_OfflineLoadingCheckpoint;

        async Task RefundFailedOfflineLoadingAsync()
        {
            if (m_OfflineLoadingCheckpoint == null) return;
            m_OfflineLoadingCheckpoint.Refund();
            m_OfflineLoadingCheckpoint = null;
            if (PlayerProfileClient.IsOffline) await PlayerProfileClient.ValidateAsync(null, default);
            Debug.Log("[Raid] Failed single-player loading refunded the original equipment.");
        }
        /// <summary>
        /// Safe return to main menu, can be called by the pause menu button.
        /// </summary>
        public async void ReturnToMainMenuAsync()
        {
            if (m_ReturningToMainMenu) return;
            m_ReturningToMainMenu = true;
            try { await ReturnToMainMenuCoreAsync(); }
            catch (Exception e) { Debug.LogException(e); }
            finally { m_ReturningToMainMenu = false; }
        }

        async Task ReturnToMainMenuCoreAsync()
        {
            Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Called.");
            if (!CanUseMainMenu)
            {
                QuitAsync();
                return;
            }
            
            if (m_LoadingGameCancel != null)
            {
                Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Cancelling loading game.");
                m_LoadingGameCancel.Cancel();
                try
                {
                    await m_LoadingGame;
                }
                catch (OperationCanceledException)
                {
                    // Discarding this exception because we're the one asking for it.
                }
                Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Loading Cancelled, start returning to main menu.");
            }
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.UnloadingGame);
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            
            GameSettings.Instance.IsPauseMenuOpen = false;
            await DisconnectAndUnloadWorlds();
            await RefundFailedOfflineLoadingAsync();
            if (LanRoomClient.Active) await LanRoomClient.ReturnedToMenuAsync();
            else await OnlineLobbyClient.ReturnedAsync();
            
            // Restart the main menu scene.
            Start();
            
            Utils.SetCursorVisible(true);
            
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.BackToMainMenu);
            GameSettings.Instance.GameState = GlobalGameState.MainMenu;
        }
        
        /// <summary>
        /// Safe shutdown of the game. Saves everything that needs to be saved.
        /// </summary>
        public async void QuitAsync()
        {
            try
            {
                using var sync = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                await PlayerProfileClient.FlushMenuInventoryAsync(sync.Token);
            }
            catch (Exception ex) { ConnectionSettings.Instance.ConnectionError = ex.Message; return; }
            await DisconnectAndUnloadWorlds();
            await RefundFailedOfflineLoadingAsync();
            await LanRoomClient.LeaveAsync(afterWorldShutdown:true);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    
        async Task LeaveSessionAsync()
        {
            if (GameConnection == null || GameConnection.Session == null)
            {
                return;
            }

            GameConnection.Session.RemovedFromSession -= OnSessionLeft;
            if (GameConnection.Session.IsHost || GameConnection.Session.IsServer())
            {
                //ConnectionSettings.Instance.SessionCode = null;
            }

            if (GameConnection.Session.IsHost)
            {
                await GameConnection.Session.AsHost().DeleteAsync();
            }
            else
            {
                await GameConnection.Session.LeaveAsync();
            }

            GameConnection = null;
        }
        
        void OnSessionLeft()
        {
            GameConnection = null;
            ReturnToMainMenuAsync();
        }
        
        static async Task DestroyGameSessionWorlds()
        {
            // This prevents the "Cannot dispose world while updating it" error,
            // allowing us to call this from anywhere.
            // 等这一帧的 ECS 系统跑完再释放 World，避免从系统更新里拆掉正在被使用的对象。
            await Awaitable.EndOfFrameAsync();

            // Destroy netcode worlds:
            var shutdowns = new List<Task>();
            for (var i = World.All.Count - 1; i >= 0; i--)
            {
                var world = World.All[i];
                if (world.IsServer() || world.IsClient())
                {
                    RaidPersistenceContext persistence = null;
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(RaidPersistenceContext)))
                        if (!query.IsEmptyIgnoreFilter) persistence = world.EntityManager.GetComponentObject<RaidPersistenceContext>(query.GetSingletonEntity());
                    world.Dispose();
                    if (persistence != null) shutdowns.Add(persistence.ShutdownTask);
                }
            }
            // Do not expose the account equipment screen while its last raid still owns the kit.
            // 每个 World 的存档上下文仍可能在结束战局。等它们真正交还出战装备，再让玩家看见可操作的账号页面。
            await Task.WhenAll(shutdowns);
        }
        
        async Task DisconnectAndUnloadWorlds()
        {
            NetworkStreamReceiveSystem.DriverConstructor = null;
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;

            bool requestedDisconnect = false;
            foreach (var world in World.All)
            {
                if (world.IsClient())
                {
                    using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                    if (query.TryGetSingletonEntity<NetworkId>(out var networkId))
                    {
                        requestedDisconnect = true;
                        world.EntityManager.AddComponentData(networkId, new NetworkStreamRequestDisconnect());
                    }
                }
            }

            if (requestedDisconnect)
            {
                await Awaitable.NextFrameAsync();
            }

            var previousConnection = GameConnection;
            try { await LeaveSessionAsync(); }
            catch (Exception e) { Debug.LogWarning($"[Session] Could not leave the service session: {e.Message}"); }
            finally { GameConnection = null; }
            try { await DestroyGameSessionWorlds(); }
            finally { previousConnection?.ReleaseIce(); }
            
            if (GhostBridgeBootstrap.Instance != null)
            {
                // Destroy all "Server" and "Client" GameObjects and all their children.
                Debug.Log($"[{nameof(DisconnectAndUnloadWorlds)}] Destroying multiplayer worlds via GhostBridgeBootstrap.");
                GhostBridgeBootstrap.Instance.DestroyMultiplayerWorlds();
            }
            else
            {
                // Fallback in case bootstrap instance is lost
                Debug.Log($"[{nameof(DisconnectAndUnloadWorlds)}] InvokeAndClearStaticCallbacks");
                GhostSingleton.InvokeAndClearStaticCallbacks();
            }
            
            await ScenesLoader.UnloadGameplayScenesAsync();
        }
    }
}
