using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        // The large terrain uses 75 cm voxels. MC00 stairs need a bounded 15 cm overlay.
        // Area 30 distinguishes the two overlapping meshes so explicit links can join them.
        private const int MissionNavigationArea = 30;
        private NavMeshData m_MissionNavData;
        private NavMeshDataInstance m_MissionNavInstance;
        private AsyncOperation m_MissionNavBuild;
        private readonly List<NavMeshLinkInstance> m_MissionNavLinks = new List<NavMeshLinkInstance>();
        private readonly List<NavMeshBuildSource> m_MissionNavSources = new List<NavMeshBuildSource>();
        private Bounds m_MissionNavBounds;

        private bool MissionNavigationReady()
        {
            var site = RaidMissionSite.Active;
            if (!site || site.Anchors.Length == 0) return true;
            if (m_MissionNavInstance.valid) return true;
            if (m_MissionNavBuild != null)
            {
                if (!m_MissionNavBuild.isDone) return false;
                m_MissionNavInstance = NavMesh.AddNavMeshData(m_MissionNavData);
                m_MissionNavSources.Clear();
                var settings = NavMesh.GetSettingsByIndex(0);
                var entry = site.Anchor("entry");
                if (entry != null)
                {
                    foreach (var offset in new[] { Vector3.zero, Vector3.right * 4, Vector3.left * 4, Vector3.forward * 4, Vector3.back * 4 })
                    {
                        var point = entry.Approach + offset;
                        if (!NavMesh.SamplePosition(point, out var coarse, 3, 1) ||
                            !NavMesh.SamplePosition(point, out var detailed, 3, 1 << MissionNavigationArea)) continue;
                        var link = new NavMeshLinkData { startPosition = coarse.position, endPosition = detailed.position,
                            width = 1, bidirectional = true, costModifier = -1, agentTypeID = settings.agentTypeID };
                        var instance = NavMesh.AddLink(link);
                        if (instance.valid) m_MissionNavLinks.Add(instance);
                    }
                }
                Debug.Log("[DollSinger AI] MC00 detailed navigation ready; boundary links=" + m_MissionNavLinks.Count);
                return m_MissionNavInstance.valid;
            }
            m_MissionNavBounds = new Bounds(site.Anchors[0].Approach, Vector3.one);
            foreach (var anchor in site.Anchors) m_MissionNavBounds.Encapsulate(anchor.Approach);
            // Include both stair towers and their landings, beyond the equipment-room footprint.
            m_MissionNavBounds.Expand(new Vector3(100, 40, 100));
            UnityEngine.Physics.SyncTransforms();
            NavMeshBuilder.CollectSources(m_MissionNavBounds, s_WorldMask, NavMeshCollectGeometry.PhysicsColliders,
                MissionNavigationArea, new List<NavMeshBuildMarkup>(), m_MissionNavSources);
            if (m_MissionNavSources.Count == 0) return false;
            for (int i = 0; i < m_MissionNavSources.Count; i++)
            {
                var source = m_MissionNavSources[i];
                if (source.shape != NavMeshBuildSourceShape.Mesh || !(source.sourceObject is Mesh mesh) || mesh.isReadable) continue;
                source.shape = NavMeshBuildSourceShape.Box; source.transform *= Matrix4x4.Translate(mesh.bounds.center);
                source.size = mesh.bounds.size; source.sourceObject = null; m_MissionNavSources[i] = source;
            }
            var build = NavMesh.GetSettingsByIndex(0);
            build.agentRadius = .35f; build.agentHeight = 1.8f; build.agentClimb = .35f; build.agentSlope = 45;
            build.overrideVoxelSize = true; build.voxelSize = .15f;
            m_MissionNavData = new NavMeshData(build.agentTypeID) { name = "MC00 detailed mission navigation" };
            m_MissionNavBuild = NavMeshBuilder.UpdateNavMeshDataAsync(m_MissionNavData, build, m_MissionNavSources, m_MissionNavBounds);
            return false;
        }
        private void ReleaseMissionNavigation()
        {
            if (m_MissionNavBuild != null && !m_MissionNavBuild.isDone && m_MissionNavData) NavMeshBuilder.Cancel(m_MissionNavData);
            foreach (var link in m_MissionNavLinks) if (link.valid) link.Remove();
            m_MissionNavLinks.Clear();
            if (m_MissionNavInstance.valid) m_MissionNavInstance.Remove();
            if (m_MissionNavData) Object.Destroy(m_MissionNavData);
            m_MissionNavData = null; m_MissionNavBuild = null; m_MissionNavSources.Clear();
        }
    }
}
