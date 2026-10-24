using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private RaidCameraTransition m_Cinematic;
        private Entity m_CinematicOwner;
        private Camera m_CinematicSource;
        private int m_CinematicRaid;
        private bool CinematicInputBlocked => m_Cinematic != null && m_Cinematic.Deploying;

        private void UpdateRaidCinematics(bool clash)
        {
            if (m_Snapshot.RaidId <= 0) return;
            bool playable = m_Snapshot.Phase == RaidPhase.Active &&
                (!clash || m_Clash is { HasSnapshot: true } && m_Clash.Snapshot.Phase == ClashPhase.Active &&
                    m_Clash.Snapshot.Life == ClashLife.Alive);
            if (!playable)
            {
                m_Cinematic?.Stop();
                m_Root.style.visibility = UnityEngine.UIElements.Visibility.Visible;
                return;
            }
            if (!TryGetLocalPlayerEntity(out var owner)) return;
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
            if (m_CinematicRaid != m_Snapshot.RaidId)
            {
                var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                if (link.LinkedInstance && link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                    presentation.PrepareDeploymentView();
                m_CinematicRaid = m_Snapshot.RaidId;
                m_Cinematic.BeginDeployment(DeploymentBounds(m_CinematicSource.transform.position));
            }
            bool wasPlaying = m_Cinematic.Deploying;
            m_Cinematic.TickDeployment(Time.unscaledDeltaTime);
            m_Root.style.visibility = CinematicInputBlocked ? UnityEngine.UIElements.Visibility.Hidden : UnityEngine.UIElements.Visibility.Visible;
            if (wasPlaying && !CinematicInputBlocked) Utils.SetCursorVisible(GameSettings.Instance.IsPauseMenuOpen);
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
            m_CinematicOwner = Entity.Null; m_CinematicSource = null; m_CinematicRaid = 0;
            if (m_Root != null) m_Root.style.visibility = UnityEngine.UIElements.Visibility.Visible;
        }
    }
}
