// 给研究站的楼梯和内部区域补一块更细的导航网格。异步烘焙完后用连接把它和整张月面粗网格接起来。
// 这样 AI 能走细楼梯，也不用把整张大图重烘得很细。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.MP_FPS {
    public partial class DollSingerEnemySystem {
        // 大地形使用 75 厘米体素，MC00 楼梯附近另盖一块有限的 15 厘米导航。
        // 重叠的两张网格用区域号 30 区分，之后用明确的连接让它们可互相到达。
        private const int MissionNavigationArea = 30;
        private NavMeshData m_MissionNavData;
        private NavMeshDataInstance m_MissionNavInstance;
        private AsyncOperation m_MissionNavBuild;
        private readonly List<NavMeshLinkInstance> m_MissionNavLinks = new();
        private readonly List<NavMeshBuildSource> m_MissionNavSources = new();
        private Bounds m_MissionNavBounds;

        private bool MissionNavigationReady() {
            var site = RaidMissionSite.Active;
            if (!site || site.Anchors.Length == 0) return true;
            if (m_MissionNavInstance.valid) return true;
            // 异步构建在后台推进。只在准备完成时注册网格并接通到主地形。
            if (m_MissionNavBuild != null) {
                if (!m_MissionNavBuild.isDone) return false;
                m_MissionNavInstance = NavMesh.AddNavMeshData(m_MissionNavData);
                m_MissionNavSources.Clear();
                var settings = NavMesh.GetSettingsByIndex(0);
                var entry = site.Anchor("entry");
                if (entry != null)
                    foreach (var offset in new[] {
                                 Vector3.zero, Vector3.right * 4, Vector3.left * 4, Vector3.forward * 4,
                                 Vector3.back * 4
                             }) {
                        var point = entry.Approach + offset;
                        if (!NavMesh.SamplePosition(point, out var coarse, 3, 1) ||
                            !NavMesh.SamplePosition(point, out var detailed, 3, 1 << MissionNavigationArea))
                            continue;
                        // 在入口多个点尝试让粗网格与细网格连通。它们使用不同区域号，连接两端都需要能采样到。
                        var link = new NavMeshLinkData {
                            startPosition = coarse.position, endPosition = detailed.position,
                            width = 1, bidirectional = true, costModifier = -1, agentTypeID = settings.agentTypeID
                        };
                        var instance = NavMesh.AddLink(link);
                        if (NavMesh.IsLinkValid(instance)) m_MissionNavLinks.Add(instance);
                    }
                Debug.Log("[DollSinger AI] MC00 detailed navigation ready; boundary links=" + m_MissionNavLinks.Count);
                return m_MissionNavInstance.valid;
            }
            // 用设备锚点框住任务区，并扩出楼梯与平台，不用覆盖整张远征地图。
            m_MissionNavBounds = new Bounds(site.Anchors[0].Approach, Vector3.one);
            foreach (var anchor in site.Anchors) m_MissionNavBounds.Encapsulate(anchor.Approach);
            // 范围覆盖两条楼梯井及平台。设备室外沿也一起考虑。
            m_MissionNavBounds.Expand(new Vector3(100, 40, 100));
            UnityEngine.Physics.SyncTransforms();
            NavMeshBuilder.CollectSources(m_MissionNavBounds, s_WorldMask, NavMeshCollectGeometry.PhysicsColliders,
                MissionNavigationArea, new List<NavMeshBuildMarkup>(), m_MissionNavSources);
            if (m_MissionNavSources.Count == 0) return false;
            for (int i = 0; i < m_MissionNavSources.Count; i++) {
                var source = m_MissionNavSources[i];
                if (source.shape != NavMeshBuildSourceShape.Mesh || !(source.sourceObject is Mesh mesh) ||
                    mesh.isReadable)
                    continue;
                source.shape = NavMeshBuildSourceShape.Box;
                source.transform *= Matrix4x4.Translate(mesh.bounds.center);
                source.size = mesh.bounds.size;
                source.sourceObject = null;
                m_MissionNavSources[i] = source;
            }
            var build = NavMesh.GetSettingsByIndex(0);
            build.agentRadius = .35f;
            build.agentHeight = 1.8f;
            build.agentClimb = .35f;
            build.agentSlope = 45;
            // 仅任务区使用更精细的网格体素，解决粗网格会把窄梯踏面抹掉的问题。
            build.overrideVoxelSize = true;
            build.voxelSize = .15f;
            m_MissionNavData = new NavMeshData(build.agentTypeID) { name = "MC00 detailed mission navigation" };
            m_MissionNavBuild =
                NavMeshBuilder.UpdateNavMeshDataAsync(m_MissionNavData, build, m_MissionNavSources, m_MissionNavBounds);
            return false;
        }

        // 离场先取消未完成的构建，再移除连接和区域网格，旧地图的路线不会挂到下一局。
        private void ReleaseMissionNavigation() {
            if (m_MissionNavBuild != null && !m_MissionNavBuild.isDone && m_MissionNavData)
                NavMeshBuilder.Cancel(m_MissionNavData);
            foreach (var link in m_MissionNavLinks)
                if (NavMesh.IsLinkValid(link))
                    NavMesh.RemoveLink(link);
            m_MissionNavLinks.Clear();
            if (m_MissionNavInstance.valid) m_MissionNavInstance.Remove();
            if (m_MissionNavData) Object.Destroy(m_MissionNavData);
            m_MissionNavData = null;
            m_MissionNavBuild = null;
            m_MissionNavSources.Clear();
        }
    }
}