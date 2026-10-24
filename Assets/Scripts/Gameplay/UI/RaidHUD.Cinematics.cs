using Unity.Entities;
using Unity.Transforms;
using UnityEngine.UIElements;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private RaidCameraTransition m_Cinematic;
        private Entity m_CinematicOwner;
        private Camera m_CinematicSource;
        private int m_CinematicRaid;
        private VisualElement m_CinematicFade;
        private int m_ObservedRaid;
        private bool m_HadLivingCamera;
        public static bool CinematicViewActive => s_Active != null && s_Active.CinematicInputBlocked;
        private bool CinematicInputBlocked => m_Cinematic != null && (m_Cinematic.Deploying || m_Cinematic.DeathActive);

        private void UpdateRaidCinematics(bool clash)
        {
            if (m_Snapshot.RaidId <= 0) { ResetRaidCinematics(); return; }
            if (m_ObservedRaid != m_Snapshot.RaidId)
            {
                m_ObservedRaid = m_Snapshot.RaidId;
                m_Cinematic?.Stop(); m_HadLivingCamera = false;
                m_CinematicOwner = Entity.Null; m_CinematicSource = null;
            }
            bool hasPlayer = TryGetLocalPlayerEntity(out var owner);
            var ghost = hasPlayer ? m_World.EntityManager.GetComponentData<PredictedPlayerGhost>(owner) : default;
            bool clashDead = clash && m_Clash is { HasSnapshot: true } && m_Clash.Snapshot.Life == ClashLife.Dead;
            bool dead = m_Snapshot.Phase == RaidPhase.Dead || clashDead ||
                !clash && m_Snapshot.Phase == RaidPhase.Active && m_HadLivingCamera && hasPlayer && ghost.CurrentHealth <= 0;
            if (dead || m_Cinematic is { DeathActive: true })
            {
                m_Cinematic ??= new RaidCameraTransition(transform);
                if (!m_Cinematic.DeathActive)
                {
                    SetRaidMapOpen(false);
                    SetMissionDetails(false);
                    if (m_InventoryVisible) SetInventory(false);
                    m_Cinematic.BeginDeath();
                }
                m_Cinematic.TickDeath(Time.unscaledDeltaTime);
                PresentCinematicCurtain();
                return;
            }
            bool playable = m_Snapshot.Phase == RaidPhase.Active &&
                (!clash || m_Clash is { HasSnapshot: true } && m_Clash.Snapshot.Phase == ClashPhase.Active &&
                    m_Clash.Snapshot.Life == ClashLife.Alive);
            bool downed = clash && m_Clash is { HasSnapshot: true } && m_Clash.Snapshot.Life == ClashLife.Downed;
            if (!hasPlayer || ghost.CurrentHealth <= 0 && !downed) { m_Cinematic?.Stop(); PresentCinematicCurtain(); return; }
            if (owner != m_CinematicOwner || !m_CinematicSource)
            {
                m_CinematicOwner = owner;
                m_CinematicSource = null;
                if (m_World.EntityManager.HasComponent<GhostGameObjectLink>(owner))
                {
                    var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                    if (link.LinkedInstance && link.LinkedInstance.TryGetComponent<PlayerGhost>(out var player))
                        m_CinematicSource = player.GetPlayerCamera();
                }
            }
            if (!m_CinematicSource) return;
            m_Cinematic ??= new RaidCameraTransition(transform);
            if (!m_Cinematic.Deploying) m_Cinematic.Capture(m_CinematicSource);
            m_Cinematic.CaptureBody(m_World.EntityManager.GetComponentData<LocalTransform>(owner).Position,
                m_CinematicSource.gameObject.scene.GetPhysicsScene());
            m_HadLivingCamera |= ghost.CurrentHealth > 0;
            if (!playable)
            {
                // Keep the prone/downed eye current without turning a rescue state into death.
                m_Cinematic.Stop(); PresentCinematicCurtain(); return;
            }
            if (m_CinematicRaid != m_Snapshot.RaidId)
            {
                var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                if (link.LinkedInstance && link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                    presentation.PrepareDeploymentView(m_Cinematic.Camera);
                m_CinematicRaid = m_Snapshot.RaidId;
                m_Cinematic.BeginDeployment(DeploymentBounds(m_CinematicSource.transform.position));
            }
            bool wasPlaying = m_Cinematic.Deploying;
            m_Cinematic.TickDeployment(Time.unscaledDeltaTime);
            PresentCinematicCurtain();
            if (wasPlaying && !CinematicInputBlocked) Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
        }

        private void PresentCinematicCurtain()
        {
            bool hide = m_Cinematic != null && m_Cinematic.HidesHUD;
            m_Root.style.visibility = hide ? Visibility.Hidden : Visibility.Visible;
            float alpha = m_Cinematic?.BlackAlpha ?? 0;
            if (m_CinematicFade == null && alpha > 0)
            {
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
            if (hide) m_CinematicFade.BringToFront();
            else m_CinematicFade.SendToBack(); // Keep a black world behind the interactive failure page.
        }

        private Bounds DeploymentBounds(Vector3 eye)
        {
            var bounds = new Bounds(eye, Vector3.one * 200);
            var map = MoonRaidMap.Active;
            if (map)
            {
                bounds.Encapsulate(map.ExtractionPosition);
                foreach (var point in map.LootPositions) bounds.Encapsulate(point);
                foreach (var terrain in Terrain.activeTerrains)
                {
                    if (terrain.gameObject.scene != map.gameObject.scene || !terrain.terrainData) continue;
                    bounds.Encapsulate(terrain.transform.position);
                    bounds.Encapsulate(terrain.transform.position + terrain.terrainData.size);
                }
            }
            if (m_ClashMode && BountyClashArenaSettings.Active)
            {
                var arena = m_Clash.Snapshot;
                bounds = new Bounds(arena.ArenaCenter, new Vector3(arena.ArenaRadius * 2, 0, arena.ArenaRadius * 2));
                bounds.Encapsulate(eye);
            }
            return bounds;
        }

        private void ResetRaidCinematics()
        {
            m_Cinematic?.Dispose(); m_Cinematic = null;
            m_CinematicOwner = Entity.Null; m_CinematicSource = null; m_CinematicRaid = m_ObservedRaid = 0;
            m_HadLivingCamera = false;
            m_CinematicFade?.RemoveFromHierarchy(); m_CinematicFade = null;
            if (m_Root != null) m_Root.style.visibility = UnityEngine.UIElements.Visibility.Visible;
        }
    }
}
