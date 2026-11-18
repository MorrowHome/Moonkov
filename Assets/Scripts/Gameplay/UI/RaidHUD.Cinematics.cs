// 用战局和本地角色状态触发进场、死亡镜头。把捕获的角色视角交给 RaidCameraTransition，并在过渡期间处理输入和遮罩。
using Unity.Entities;
using Unity.Transforms;
using UnityEngine.UIElements;
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private RaidCameraTransition m_Cinematic;
        private Entity m_CinematicOwner;
        private Camera m_CinematicSource;
        private int m_CinematicRaid;
        private VisualElement m_CinematicFade;
        private int m_ObservedRaid;
        private bool m_HadLivingCamera;
        public static bool CinematicViewActive => s_Active != null && s_Active.CinematicInputBlocked;
        private bool CinematicInputBlocked => m_Cinematic != null && (m_Cinematic.Deploying || m_Cinematic.DeathActive);

        private void UpdateRaidCinematics(bool clash) {
            if (m_Snapshot.RaidId <= 0) {
                ResetRaidCinematics();
                return;
            }
            // 换局重置记住的玩家镜头。旧局已经活过或死亡的状态不能影响新局开场。
            if (m_ObservedRaid != m_Snapshot.RaidId) {
                m_ObservedRaid = m_Snapshot.RaidId;
                m_Cinematic?.Stop();
                m_HadLivingCamera = false;
                m_CinematicOwner = Entity.Null;
                m_CinematicSource = null;
            }
            bool hasPlayer = TryGetLocalPlayerEntity(out var owner);
            var ghost = hasPlayer ? m_World.EntityManager.GetComponentData<PredictedPlayerGhost>(owner) : default;
            bool clashDead = clash && m_Clash is { HasSnapshot: true } && m_Clash.Snapshot.Life == ClashLife.Dead;
            bool dead = m_Snapshot.Phase == RaidPhase.Dead || clashDead || (!clash &&
                                                                            m_Snapshot.Phase == RaidPhase.Active &&
                                                                            m_HadLivingCamera && hasPlayer &&
                                                                            ghost.CurrentHealth <= 0);
            // 进入死亡段后继续把这段演出走完。首次触发时收掉会挡画面的地图、任务详情和背包。
            if (dead || m_Cinematic is { DeathActive: true }) {
                m_Cinematic ??= new RaidCameraTransition(transform);
                if (!m_Cinematic.DeathActive) {
                    SetRaidMapOpen(false);
                    SetMissionDetails(false);
                    if (m_InventoryVisible) SetInventory(false);
                    m_Cinematic.BeginDeath();
                }
                m_Cinematic.TickDeath(Time.unscaledDeltaTime);
                PresentCinematicCurtain();
                return;
            }
            bool playable = m_Snapshot.Phase == RaidPhase.Active && (!clash || (m_Clash is { HasSnapshot: true } &&
                m_Clash.Snapshot.Phase == ClashPhase.Active && m_Clash.Snapshot.Life == ClashLife.Alive));
            // 倒地玩家仍可能被救起。继续保留视角数据，等确认为死亡才进入死亡镜头。
            bool downed = clash && m_Clash is { HasSnapshot: true } && m_Clash.Snapshot.Life == ClashLife.Downed;
            if (!hasPlayer || (ghost.CurrentHealth <= 0 && !downed)) {
                m_Cinematic?.Stop();
                PresentCinematicCurtain();
                return;
            }
            // 新角色出现时通过 ECS 到 GameObject 的链接重新找相机。角色对象没准备好就先等。
            if (owner != m_CinematicOwner || !m_CinematicSource) {
                m_CinematicOwner = owner;
                m_CinematicSource = null;
                if (m_World.EntityManager.HasComponent<GhostGameObjectLink>(owner)) {
                    var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                    if (link.LinkedInstance && link.LinkedInstance.TryGetComponent<PlayerGhost>(out var player))
                        m_CinematicSource = player.GetPlayerCamera();
                }
            }
            if (!m_CinematicSource) return;
            m_Cinematic ??= new RaidCameraTransition(transform);
            if (!m_Cinematic.Deploying) m_Cinematic.Capture(m_CinematicSource);
            // 记录真实角色位置及其物理场景。后续镜头围绕正确的身体位置放置，场景遮挡也用同一物理场景判断。
            m_Cinematic.CaptureBody(m_World.EntityManager.GetComponentData<LocalTransform>(owner).Position,
                m_CinematicSource.gameObject.scene.GetPhysicsScene());
            m_HadLivingCamera |= ghost.CurrentHealth > 0;
            if (!playable) {
                // 倒地或趴下时继续记录当前镜头。被队友救援的状态要与死亡区分。
                m_Cinematic.Stop();
                PresentCinematicCurtain();
                return;
            }
            // 进场镜头每个战局触发一次。开始前把专用相机接到角色表现，过渡结束再恢复玩家输入。
            if (m_CinematicRaid != m_Snapshot.RaidId) {
                var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                if (link.LinkedInstance &&
                    link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                    presentation.PrepareDeploymentView(m_Cinematic.Camera);
                m_CinematicRaid = m_Snapshot.RaidId;
                m_Cinematic.BeginDeployment(DeploymentBounds(m_CinematicSource.transform.position));
            }
            bool wasPlaying = m_Cinematic.Deploying;
            m_Cinematic.TickDeployment(Time.unscaledDeltaTime);
            PresentCinematicCurtain();
            if (wasPlaying && !CinematicInputBlocked) Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
        }

        private void PresentCinematicCurtain() {
            bool hide = m_Cinematic != null && m_Cinematic.HidesHUD;
            m_Root.style.visibility = hide ? Visibility.Hidden : Visibility.Visible;
            float alpha = m_Cinematic?.BlackAlpha ?? 0;
            if (m_CinematicFade == null && alpha > 0) {
                m_CinematicFade = new VisualElement { name = "raidDeathFade", pickingMode = PickingMode.Ignore };
                m_CinematicFade.style.position = Position.Absolute;
                m_CinematicFade.style.left = m_CinematicFade.style.top = 0;
                m_CinematicFade.style.right = m_CinematicFade.style.bottom = 0;
                m_CinematicFade.style.backgroundColor = Color.black;
                m_CinematicFade.style.visibility = Visibility.Visible;
                m_Root.Add(m_CinematicFade);
            }
            if (m_CinematicFade == null) return;
            m_CinematicFade.style.display = alpha > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_CinematicFade.style.opacity = alpha;
            if (hide)
                m_CinematicFade.BringToFront();
            else
                m_CinematicFade.SendToBack(); // 失败页可操作时，世界画面仍保持黑色。
        }

        private Bounds DeploymentBounds(Vector3 eye) {
            var bounds = new Bounds(eye, Vector3.one * 200);
            var map = MoonRaidMap.Active;
            if (map) {
                for (int i = 0; i < map.ExtractionCount; i++)
                    if (map.TryExtractionPosition(i, out var exit))
                        bounds.Encapsulate(exit);
                foreach (var point in map.LootPositions) bounds.Encapsulate(point);
                foreach (var terrain in Terrain.activeTerrains) {
                    if (terrain.gameObject.scene != map.gameObject.scene || !terrain.terrainData) continue;
                    bounds.Encapsulate(terrain.transform.position);
                    bounds.Encapsulate(terrain.transform.position + terrain.terrainData.size);
                }
            }
            if (m_ClashMode && BountyClashArenaSettings.Active) {
                var arena = m_Clash.Snapshot;
                bounds = new Bounds(arena.ArenaCenter, new Vector3(arena.ArenaRadius * 2, 0, arena.ArenaRadius * 2));
                bounds.Encapsulate(eye);
            }
            return bounds;
        }

        private void ResetRaidCinematics() {
            m_Cinematic?.Dispose();
            m_Cinematic = null;
            m_CinematicOwner = Entity.Null;
            m_CinematicSource = null;
            m_CinematicRaid = m_ObservedRaid = 0;
            m_HadLivingCamera = false;
            m_CinematicFade?.RemoveFromHierarchy();
            m_CinematicFade = null;
            if (m_Root != null) m_Root.style.visibility = Visibility.Visible;
        }
    }
}