// 离开战局、加载失败和退出程序都在这里收尾。先断开会话和 World，等存档处理完成，再退回大厅或关闭程序。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS {
    public partial class GameManager : MonoBehaviour {
        private bool m_ReturningToMainMenu;
        private OfflineProfileStore.LoadingCheckpoint m_OfflineLoadingCheckpoint;

        private async Task RefundFailedOfflineLoadingAsync() {
            if (m_OfflineLoadingCheckpoint == null) return;
            // 只在还有未完成的离线加载检查点时退回装备。退款后刷新正在显示的本机档案，菜单能立刻读到恢复结果。
            m_OfflineLoadingCheckpoint.Refund();
            m_OfflineLoadingCheckpoint = null;
            if (PlayerProfileClient.IsOffline) await PlayerProfileClient.ValidateAsync(null, default);
            Debug.Log("[Raid] Failed single-player loading refunded the original equipment.");
        }

        /// <summary>
        /// Safe return to main menu, can be called by the pause menu button.
        /// </summary>
        public async void ReturnToMainMenuAsync() {
            // 退出也可能同时由按钮、掉线和会话事件触发。这个标记让几个来源共用一次清理。
            if (m_ReturningToMainMenu) return;
            m_ReturningToMainMenu = true;
            try { await ReturnToMainMenuCoreAsync(); } catch (Exception e) { Debug.LogException(e); } finally {
                m_ReturningToMainMenu = false;
            }
        }

        private async Task ReturnToMainMenuCoreAsync() {
            Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Called.");
            if (!CanUseMainMenu) {
                QuitAsync();
                return;
            }
            if (m_LoadingGameCancel != null) {
                Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Cancelling loading game.");
                // 如果还在入局，先让该任务停下来并等待它退出。这能防止一边释放 World、另一边又继续加载或获取入场票。
                m_LoadingGameCancel.Cancel();
                try { await m_LoadingGame; } catch (OperationCanceledException) {
                    // Discarding this exception because we're the one asking for it.
                }
                Debug.Log($"[{nameof(ReturnToMainMenuAsync)}] Loading Cancelled, start returning to main menu.");
            }
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.UnloadingGame);
            GameSettings.Instance.GameState = GlobalGameState.Loading;
            GameSettings.Instance.IsPauseMenuOpen = false;
            await DisconnectAndUnloadWorlds();
            await RefundFailedOfflineLoadingAsync();
            // 地图和存档收完后，再通知对应的房间返回菜单。房间能恢复准备状态，玩家还能在同一个房间发起下一局。
            if (LanRoomClient.Active)
                await LanRoomClient.ReturnedToMenuAsync();
            else
                await OnlineLobbyClient.ReturnedAsync();

            // Restart the main menu scene.
            Start();
            Utils.SetCursorVisible(true);
            LoadingData.Instance.UpdateLoading(LoadingData.LoadingSteps.BackToMainMenu);
            GameSettings.Instance.GameState = GlobalGameState.MainMenu;
        }

        /// <summary>
        /// Safe shutdown of the game. Saves everything that needs to be saved.
        /// </summary>
        public async void QuitAsync() {
            try {
                using var sync = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                // 正常退出前给仓库待发队列一次保存机会。写入失败时把原因放到界面，继续保留当前会话供玩家处理。
                await PlayerProfileClient.FlushMenuInventoryAsync(sync.Token);
            } catch (Exception ex) {
                ConnectionSettings.Instance.ConnectionError = ex.Message;
                return;
            }
            await DisconnectAndUnloadWorlds();
            await RefundFailedOfflineLoadingAsync();
            await LanRoomClient.LeaveAsync(true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private async Task LeaveSessionAsync() {
            if (GameConnection == null || GameConnection.Session == null) return;
            // 主动离开前去掉会话的回调。接着由房主删除会话、其他人退出，避免自己触发的离开再重复执行一次返回菜单。
            GameConnection.Session.RemovedFromSession -= OnSessionLeft;
            if (GameConnection.Session.IsHost || GameConnection.Session.IsServer()) {
                //ConnectionSettings.Instance.SessionCode = null;
            }
            if (GameConnection.Session.IsHost)
                await GameConnection.Session.AsHost().DeleteAsync();
            else
                await GameConnection.Session.LeaveAsync();
            GameConnection = null;
        }

        private void OnSessionLeft() {
            GameConnection = null;
            ReturnToMainMenuAsync();
        }

        private static async Task DestroyGameSessionWorlds() {
            // This prevents the "Cannot dispose world while updating it" error,
            // allowing us to call this from anywhere.
            // 等这一帧的 ECS 系统跑完再释放 World，避免从系统更新里拆掉正在被使用的对象。
            await Awaitable.EndOfFrameAsync();

            // Destroy netcode worlds:
            var shutdowns = new List<Task>();
            for (int i = World.All.Count - 1; i >= 0; i--) {
                var world = World.All[i];
                if (world.IsServer() || world.IsClient()) {
                    RaidPersistenceContext persistence = null;
                    using (var query = world.EntityManager.CreateEntityQuery(typeof(RaidPersistenceContext))) {
                        if (!query.IsEmptyIgnoreFilter)
                            persistence =
                                world.EntityManager.GetComponentObject<RaidPersistenceContext>(
                                    query.GetSingletonEntity());
                    }
                    // World 释放会启动服务端存档上下文的清理。先留下它提供的任务，再等待所有清理任务一起完成。
                    world.Dispose();
                    if (persistence != null) shutdowns.Add(persistence.ShutdownTask);
                }
            }
            // Do not expose the account equipment screen while its last raid still owns the kit.
            // 每个 World 的存档上下文仍可能在结束战局。等它们真正交还出战装备，再让玩家看见可操作的账号页面。
            await Task.WhenAll(shutdowns);
        }

        private async Task DisconnectAndUnloadWorlds() {
            NetworkStreamReceiveSystem.DriverConstructor = null;
            ConnectionSettings.Instance.GameConnectionState = ConnectionState.State.Disconnected;
            bool requestedDisconnect = false;
            foreach (var world in World.All)
                if (world.IsClient()) {
                    using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>());
                    if (query.TryGetSingletonEntity<NetworkId>(out var networkId)) {
                        requestedDisconnect = true;
                        // 只有客户端 World 有已登记的连接实体才排入断开请求。下一帧让网络系统处理完这个请求，再销毁 World。
                        world.EntityManager.AddComponentData(networkId, new NetworkStreamRequestDisconnect());
                    }
                }
            if (requestedDisconnect) await Awaitable.NextFrameAsync();
            var previousConnection = GameConnection;
            try { await LeaveSessionAsync(); } catch (Exception e) {
                Debug.LogWarning($"[Session] Could not leave the service session: {e.Message}");
            } finally { GameConnection = null; }
            // 先关依赖传输的 World，再释放当前连接握着的 ICE 会话。即使拆 World 中途报错，原 ICE 资源也会被收回。
            try { await DestroyGameSessionWorlds(); } finally { previousConnection?.ReleaseIce(); }
            if (GhostBridgeBootstrap.Instance != null) {
                // Destroy all "Server" and "Client" GameObjects and all their children.
                Debug.Log(
                    $"[{nameof(DisconnectAndUnloadWorlds)}] Destroying multiplayer worlds via GhostBridgeBootstrap.");
                GhostBridgeBootstrap.Instance.DestroyMultiplayerWorlds();
            } else {
                // Fallback in case bootstrap instance is lost
                Debug.Log($"[{nameof(DisconnectAndUnloadWorlds)}] InvokeAndClearStaticCallbacks");
                GhostSingleton.InvokeAndClearStaticCallbacks();
            }
            await ScenesLoader.UnloadGameplayScenesAsync();
        }
    }
}