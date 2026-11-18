// 客户端按队友识别器的装备和输入判断是否显示标识，再从已同步角色里挑队友，处理距离、轮廓和跟踪对象的回收。
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TeamOutlineClientSystem : SystemBase {
        private EntityQuery m_Owner, m_Raid;
        private readonly Dictionary<Entity, Target> m_Targets = new();
        private readonly List<Entity> m_Removed = new();
        private readonly List<SkinnedMeshRenderer> m_Renderers = new(32);
        private readonly TeamOutlineRenderer m_Renderer = new();
        private InventoryGraph m_Graph;
        private int m_Version = -1;
        private bool m_Equipped;
        private Entity m_LastOwner;

        protected override void OnCreate() {
            RequireForUpdate<NetworkStreamInGame>();
            m_Owner = GetEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>(), ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadOnly<GhostGameObjectLink>());
            m_Raid = GetEntityQuery(ComponentType.ReadOnly<RaidClientState>(),
                ComponentType.ReadOnly<RaidInventoryClientState>());
        }

        protected override void OnStopRunning() { Clear(); }
        protected override void OnDestroy() { Clear(); }

        protected override void OnUpdate() {
            if (m_Raid.CalculateEntityCount() != 1) {
                Clear();
                return;
            }
            var raid = m_Raid.GetSingletonEntity();
            var snapshot = EntityManager.GetComponentData<RaidClientState>(raid).Snapshot;
            var inventory = EntityManager.GetComponentObject<RaidInventoryClientState>(raid);
            // 识别器效果依赖本局完整的服务器库存快照，菜单预览装备不会提前生效。
            if (snapshot.Sequence == 0 || snapshot.Phase != RaidPhase.Active || snapshot.DeployPending ||
                inventory.Sequence == 0 || inventory.RaidId != snapshot.RaidId || inventory.Graph == null) {
                Clear();
                return;
            }
            // 完整快照已经确认属于这一局后，只在图对象或版本改变时重查装备槽，免得每帧遍历整个库存。
            if (m_Graph != inventory.Graph || m_Version != inventory.Graph.Version) {
                m_Graph = inventory.Graph;
                m_Version = m_Graph.Version;
                m_Equipped = TeamOutlineRules.Equipped(m_Graph);
            }
            // GhostOwnerIsLocal 可以启用或禁用，这种查询不能直接用 GetSingletonEntity。
            // 只接受唯一的本地玩家实体。所有权切换或玩家暂时未出生时，把上一角色留下的轮廓释放掉。
            using var owners = m_Owner.ToEntityArray(Allocator.Temp);
            if (owners.Length != 1) {
                ReleaseTargets();
                return;
            }
            var owner = owners[0];
            if (m_LastOwner != owner) {
                ReleaseTargets();
                m_LastOwner = owner;
            }
            var viewer = EntityManager.GetComponentData<PredictedPlayerGhost>(owner);
            if (!m_Equipped && viewer.CompanionSquadId == 0) {
                ReleaseTargets();
                return;
            }
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
            var camera =
                link.LinkedInstance &&
                link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation)
                    ? presentation.OwnedCamera
                    : null;
            if (!TeamOutlineRules.CanView(viewer) || !camera) {
                ReleaseTargets();
                return;
            }
            int ownerId = EntityManager.GetComponentData<GhostOwner>(owner).NetworkId;
            foreach (var target in m_Targets.Values) target.Seen = false;
            m_Renderers.Clear();
            int count = 0;
            foreach (var (health, identity, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>()
                         .WithAll<GhostGameObjectLink>().WithEntityAccess()) {
                if (entity == owner || count >= TeamOutlineRules.MaxTeammates) continue;
                bool human = identity.ValueRO.NetworkId > 0;
                // 人机同伴用小队编号认，真人和其他队友用玩法的队伍规则认。有识别器和没识别器时显示资格不同。
                bool companion = TeamOutlineRules.Companion(viewer, health.ValueRO, human);
                if (!companion &&
                    !(m_Equipped && TeamOutlineRules.Teammate(viewer, health.ValueRO, ownerId > 0, human)))
                    continue;
                var other = EntityManager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                if (!other || !other.gameObject.activeInHierarchy) continue;
                // 没装备识别器时，同伴需要在近处、视线可达且没被烟挡住，才能保留提示。
                if (!m_Equipped && !CompanionVisible(camera, other.transform)) continue;
                // 同一个 ECS 实体可能换了关联模型。缓存绑定到具体模型，换了就释放旧缓存再取新渲染器。
                if (!m_Targets.TryGetValue(entity, out var target) || target.Root != other.gameObject) {
                    target?.Dispose();
                    m_Targets[entity] = target = new Target(other.gameObject);
                }
                target.Seen = true;
                count++;
                foreach (var renderer in target.Renderers)
                    if (renderer && renderer.enabled && !renderer.forceRenderingOff &&
                        renderer.gameObject.activeInHierarchy)
                        m_Renderers.Add(renderer);
            }
            m_Removed.Clear();
            foreach (var pair in m_Targets)
                if (!pair.Value.Seen)
                    m_Removed.Add(pair.Key);
            foreach (var entity in m_Removed) {
                m_Targets[entity].Dispose();
                m_Targets.Remove(entity);
            }
            // 收完当帧还有效的渲染器，才发布给本地拥有者的相机。这些列表不会通过网络同步。
            m_Renderer.Publish(camera, m_Renderers, UnityEngine.Time.realtimeSinceStartupAsDouble);
        }

        private static bool CompanionVisible(Camera camera, Transform companion) {
            var eye = camera.transform.position;
            var chest = companion.position + Vector3.up * 1.2f;
            return (chest - eye).sqrMagnitude < 50 * 50 &&
                   EnergyGrenadePhysics.ClearSight(companion.gameObject.scene.GetPhysicsScene(), eye, chest,
                       companion) && !TacticalItemsFeedback.SmokeObscures(eye, chest);
        }

        private void Clear() {
            ReleaseTargets();
            m_Graph = null;
            m_Version = -1;
            m_Equipped = false;
        }

        private void ReleaseTargets() {
            m_Renderer.Clear();
            foreach (var target in m_Targets.Values) target.Dispose();
            m_Targets.Clear();
            m_Renderers.Clear();
            m_Removed.Clear();
            m_LastOwner = Entity.Null;
        }

        private sealed class Target : IDisposable {
            internal readonly GameObject Root;
            internal readonly SkinnedMeshRenderer[] Renderers;
            internal bool Seen;
            private readonly bool[] m_Offscreen;
            private readonly Animator m_Animator;
            private readonly AnimatorCullingMode m_Culling;

            // 轮廓可能在角色普通画面被裁掉时仍要绘制。暂时调整动画与蒙皮更新设置，同时保存每个原值供退出时恢复。
            internal Target(GameObject root) {
                Root = root;
                Renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                m_Offscreen = new bool[Renderers.Length];
                for (int i = 0; i < Renderers.Length; i++) {
                    m_Offscreen[i] = Renderers[i].updateWhenOffscreen;
                    Renderers[i].updateWhenOffscreen = true;
                }
                m_Animator = root.GetComponentInChildren<Animator>();
                if (m_Animator) {
                    m_Culling = m_Animator.cullingMode;
                    m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
            }

            public void Dispose() {
                for (int i = 0; i < Renderers.Length; i++)
                    if (Renderers[i])
                        Renderers[i].updateWhenOffscreen = m_Offscreen[i];
                if (m_Animator) m_Animator.cullingMode = m_Culling;
            }
        }
    }
}