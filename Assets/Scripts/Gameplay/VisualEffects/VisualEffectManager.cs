// 射击表现的网络入口。服务器把枪口、弹着和标记消息按队列发出，客户端收到后生成特效，并让预测过的本机枪口特效免于重播。
using UnityEngine;
using System.Collections.Generic;
using System;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public struct ClientSpawnVfxRpc : IRpcCommand {
        public int OwnerNetworkId;
        public uint WeaponId;
        public uint ShotTick;
        public Mathematics.float3 AimPoint;
        public bool HaloDeployed;
        public Mathematics.float3 ShotOrigin;
    }

    public class VisualEffectManager : GhostSingleton<VisualEffectManager>,
        IUpdateServer,
        IUpdateClient,
        IGhostManager {
        private Queue<ClientSpawnVfxRpc> _vfxQueue = new();
        private readonly Queue<ClientRifleImpactRpc> m_RifleImpactQueue = new();
        private readonly Queue<ClientHaloMarkRpc> m_HaloMarkQueue = new();
        private readonly Dictionary<int, uint> m_LastMarkTicks = new();
        private HaloHitMarkerFeedback m_HaloMarks;
        private RifleFeedbackLibrary m_WeaponAudio;

        public void Server_RequestHaloMark(int owner, uint tick, Vector3 position) {
            // 服务器按射手编号记住最新已经生成的标记，重复或更早的命中 tick 不会再发一遍。
            if (owner <= 0 || (m_LastMarkTicks.TryGetValue(owner, out uint last) && (int)(tick - last) <= 0)) return;
            m_LastMarkTicks[owner] = tick;
            m_HaloMarkQueue.Enqueue(new ClientHaloMarkRpc
                { OwnerNetworkId = owner, ShotTick = tick, Position = position });
        }

        private RifleImpactFeedback m_RifleImpacts;

        public RifleImpactFeedback RifleImpacts {
            get {
                if (!m_RifleImpacts) {
                    var root = new GameObject("Rifle impact feedback");
                    root.transform.SetParent(transform, false);
                    m_RifleImpacts = root.AddComponent<RifleImpactFeedback>();
                    m_RifleImpacts.Warmup();
                }
                return m_RifleImpacts;
            }
        }

        public void Server_RequestRifleImpact(ClientRifleImpactRpc impact) { m_RifleImpactQueue.Enqueue(impact); }

        public void Server_RequestVfx(
            int ownerNetworkId,
            uint weaponId,
            Vector3 aimPoint,
            uint shotTick = 0,
            bool haloDeployed = false,
            Vector3 shotOrigin = default) {
            _vfxQueue.Enqueue(new ClientSpawnVfxRpc {
                OwnerNetworkId = ownerNetworkId, WeaponId = weaponId, AimPoint = aimPoint,
                ShotTick = shotTick, HaloDeployed = haloDeployed, ShotOrigin = shotOrigin
            });
        }

        public void UpdateServer(float deltaTime) {
            // 服务器玩法脚本可以先登记这一拍要发的表现。轮到管理器的更新再统一包装成 RPC 发送。
            while (m_HaloMarkQueue.Count > 0) GhostGameObject.BroadcastRPC(m_HaloMarkQueue.Dequeue());
            while (m_RifleImpactQueue.Count > 0) GhostGameObject.BroadcastRPC(m_RifleImpactQueue.Dequeue());
            while (_vfxQueue.Count > 0) {
                var vfxRequest = _vfxQueue.Dequeue();
                GhostGameObject.BroadcastRPC(vfxRequest);
            }
        }

        public void UpdateClient(float deltaTime) {
            while (GhostGameObject.ConsumeRPC(out ClientHaloMarkRpc mark)) {
                if (!m_HaloMarks) m_HaloMarks = gameObject.AddComponent<HaloHitMarkerFeedback>();
                m_HaloMarks.Present(mark);
            }
            // 弹着点会带发射者和命中标志。视觉层可以给所有接收到的人画效果，给射手的确认则按本机拥有者来决定。
            while (GhostGameObject.ConsumeRPC(out ClientRifleImpactRpc impact))
                RifleImpacts.Present(impact, true, IsOwnedShooter(impact.OwnerNetworkId));
            while (GhostGameObject.ConsumeRPC(out ClientSpawnVfxRpc rpc)) {
                int localPlayerNetworkId = -1;

                // Find the local player's network ID
                if (PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientOwned, out var playerManager) &&
                    playerManager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players) &&
                    players.Count > 0) {
                    var localPlayer = players[0];
                    if (localPlayer != null && localPlayer.GhostGameObject != null)
                        localPlayerNetworkId = localPlayer.GhostGameObject.Owner;
                }

                // 手持开火已在本机预测时显示。部署出去的光环自主开火由服务器驱动，所以连拥有者也需要收这条消息，才能在正确的世界位置看到特效。
                // Handheld shots are predicted locally. Autonomous shots are server-only,
                // so their owner also needs this event, including its original world position.
                if (!rpc.HaloDeployed && localPlayerNetworkId != -1 && localPlayerNetworkId == rpc.OwnerNetworkId)
                    continue;

                // This is for a remote player. Find them and spawn the effect.
                // 根据网络编号找到那一个角色，再从它当前的武器点或服务器给的部署位置生成效果。
                if (PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientAll, out var allPlayersManager) &&
                    allPlayersManager.TryGetPlayersByRole(MultiplayerRole.ClientAll, out var allPlayers) &&
                    allPlayers.Count > 0)
                    foreach (var player in allPlayers)
                        if (player.GhostGameObject.Owner == rpc.OwnerNetworkId) {
                            // Found the remote player. Spawn the effect.
                            SpawnMuzzleFlash(player, rpc.WeaponId, false, rpc.AimPoint, rpc.ShotTick, rpc.HaloDeployed,
                                rpc.ShotOrigin);
                            break;
                        }
            }
        }

        private static bool IsOwnedShooter(int owner) {
            if (!PlayerGhostManager.TryGetInstanceByRole(MultiplayerRole.ClientOwned, out var manager) ||
                !manager.TryGetPlayersByRole(MultiplayerRole.ClientOwned, out var players))
                return false;
            foreach (var player in players)
                if (player && player.GhostGameObject.Owner == owner)
                    return true;
            return false;
        }

        public async void SpawnMuzzleFlash(
            PlayerGhost player,
            uint weaponId,
            bool isFirstPerson,
            Vector3 aimPoint,
            uint shotTick = 0,
            bool haloDeployed = false,
            Vector3 shotOrigin = default) {
            try {
                if (player == null) {
                    Debug.Log("Cannot spawn muzzle flash: player is null");
                    return;
                }
                var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
                // 正式角色有自己的开枪反馈和枪位。支持的角色先走专用表现，传统预制件再使用模板的枪口特效。
                if (player.TryGetComponent<DollSingerNetworkPresentation>(out var dollSinger)) {
                    dollSinger.PlayShot(aimPoint, weaponId, shotTick, haloDeployed, shotOrigin);
                    return;
                }
                if (weaponData != null) {
                    if (!m_WeaponAudio) m_WeaponAudio = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
                    var profile = m_WeaponAudio?.ForWeapon(weaponId);
                    MoonkovAudio.PlayWeaponReport(profile?.FireBody ?? weaponData.WeaponFireSfx,
                        haloDeployed ? shotOrigin : player.transform.position, isFirstPerson && !haloDeployed);
                    MoonkovAudio.PlayWeaponLayer(profile != null ? profile.FireSnap : weaponData.WeaponFireLayerSfx,
                        player.transform.position, .85f, isFirstPerson ? 0 : 1);
                    MoonkovAudio.PlayWeaponLayer(profile?.FireTail, player.transform.position, .85f,
                        isFirstPerson ? .2f : 1);
                }
                if (weaponData == null) {
                    Debug.Log("Cannot spawn muzzle flash: weapon data is null");
                    return;
                }
                var spawnPoint = isFirstPerson ? player.VisualShotOrigin1P : player.VisualShotOrigin3P;
                if (spawnPoint == null) spawnPoint = player.transform; // Fallback
                try {
                    var vfxInstance = await weaponData.MuzzleFlashVfxPrefab.GhostPrefab.InstantiateAsync(
                        spawnPoint.position, spawnPoint.rotation, spawnPoint).Task;
                    // 异步加载期间角色可能已经离场或资源没生成。只有真正拿到实例才摆位置与方向，并给它一个短寿命。
                    if (vfxInstance == null) {
                        Debug.LogWarning("Cannot spawn muzzle flash: vfx instance is null");
                        return;
                    }
                    vfxInstance.AddComponent<DestroyAfterDelay>().Lifetime = 0.5f;
                    vfxInstance.SetActive(true);
                } catch (Exception e) { Debug.LogError($"Failed to spawn muzzle flash: {e.Message}"); }
            } catch (Exception e) { Debug.LogError("Error in SpawnMuzzleFlash: " + e.Message); }
        }

        private static void DrawGizmoAtPosition(Vector3 position) {
            var color = Color.red;
            const float duration = 4.0f; // How long the gizmo will be visible in seconds
            const float size = 0.5f; // The length of the lines for the cross marker
            Debug.DrawRay(position - Vector3.up * size, Vector3.up * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.right * size, Vector3.right * size * 2, color, duration);
            Debug.DrawRay(position - Vector3.forward * size, Vector3.forward * size * 2, color, duration);
        }
    }
}