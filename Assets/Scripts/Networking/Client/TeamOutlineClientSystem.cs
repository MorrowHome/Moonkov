using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TeamOutlineClientSystem : SystemBase
    {
        private EntityQuery m_Owner, m_Raid;
        private readonly Dictionary<Entity, Target> m_Targets = new();
        private readonly List<Entity> m_Removed = new();
        private readonly List<SkinnedMeshRenderer> m_Renderers = new(32);
        private readonly TeamOutlineRenderer m_Renderer = new();
        private InventoryGraph m_Graph;
        private int m_Version = -1;
        private bool m_Equipped;
        private Entity m_LastOwner;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            m_Owner = GetEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<GhostGameObjectLink>());
            m_Raid = GetEntityQuery(ComponentType.ReadOnly<RaidClientState>(), ComponentType.ReadOnly<RaidInventoryClientState>());
        }
        protected override void OnStopRunning() => Clear();
        protected override void OnDestroy() => Clear();

        protected override void OnUpdate()
        {
            if (m_Raid.CalculateEntityCount() != 1) { Clear(); return; }
            Entity raid = m_Raid.GetSingletonEntity();
            var snapshot = EntityManager.GetComponentData<RaidClientState>(raid).Snapshot;
            var inventory = EntityManager.GetComponentObject<RaidInventoryClientState>(raid);
            // Only a complete server inventory for this raid grants the effect. No optimistic UI state.
            if (snapshot.Sequence == 0 || snapshot.Phase != RaidPhase.Active || snapshot.DeployPending ||
                inventory.Sequence == 0 || inventory.RaidId != snapshot.RaidId || inventory.Graph == null)
            { Clear(); return; }
            if (m_Graph != inventory.Graph || m_Version != inventory.Graph.Version)
            {
                m_Graph = inventory.Graph; m_Version = m_Graph.Version;
                m_Equipped = TeamOutlineRules.Equipped(m_Graph);
            }
            if (!m_Equipped) { ReleaseTargets(); return; }
            // GhostOwnerIsLocal is enableable: GetSingletonEntity is not valid for this query.
            using var owners = m_Owner.ToEntityArray(Allocator.Temp);
            if (owners.Length != 1) { ReleaseTargets(); return; }
            Entity owner = owners[0];
            if (m_LastOwner != owner) { ReleaseTargets(); m_LastOwner = owner; }
            var viewer = EntityManager.GetComponentData<PredictedPlayerGhost>(owner);
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
            var camera = link.LinkedInstance && link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation)
                ? presentation.OwnedCamera : null;
            if (!TeamOutlineRules.CanView(viewer) || !camera) { ReleaseTargets(); return; }
            int ownerId = EntityManager.GetComponentData<GhostOwner>(owner).NetworkId;
            foreach (var target in m_Targets.Values) target.Seen = false;
            m_Renderers.Clear();
            int count = 0;
            foreach (var (health, identity, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<GhostOwner>>()
                         .WithAll<GhostGameObjectLink>().WithEntityAccess())
            {
                if (entity == owner || count >= TeamOutlineRules.MaxTeammates ||
                    !TeamOutlineRules.Teammate(viewer, health.ValueRO, ownerId > 0, identity.ValueRO.NetworkId > 0)) continue;
                var other = EntityManager.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
                if (!other || !other.gameObject.activeInHierarchy) continue;
                if (!m_Targets.TryGetValue(entity, out var target) || target.Root != other.gameObject)
                {
                    target?.Dispose();
                    m_Targets[entity] = target = new Target(other.gameObject);
                }
                target.Seen = true; count++;
                foreach (var renderer in target.Renderers)
                    if (renderer && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
                        m_Renderers.Add(renderer);
            }
            m_Removed.Clear();
            foreach (var pair in m_Targets) if (!pair.Value.Seen) m_Removed.Add(pair.Key);
            foreach (var entity in m_Removed) { m_Targets[entity].Dispose(); m_Targets.Remove(entity); }
            m_Renderer.Publish(camera, m_Renderers, UnityEngine.Time.realtimeSinceStartupAsDouble);
        }

        private void Clear()
        { ReleaseTargets(); m_Graph = null; m_Version = -1; m_Equipped = false; }
        private void ReleaseTargets()
        {
            m_Renderer.Clear();
            foreach (var target in m_Targets.Values) target.Dispose();
            m_Targets.Clear(); m_Renderers.Clear(); m_Removed.Clear(); m_LastOwner = Entity.Null;
        }

        private sealed class Target : IDisposable
        {
            internal readonly GameObject Root;
            internal readonly SkinnedMeshRenderer[] Renderers;
            internal bool Seen;
            private readonly bool[] m_Offscreen;
            private readonly Animator m_Animator;
            private readonly AnimatorCullingMode m_Culling;
            internal Target(GameObject root)
            {
                Root = root;
                Renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                m_Offscreen = new bool[Renderers.Length];
                for (int i = 0; i < Renderers.Length; i++)
                {
                    m_Offscreen[i] = Renderers[i].updateWhenOffscreen;
                    Renderers[i].updateWhenOffscreen = true;
                }
                m_Animator = root.GetComponentInChildren<Animator>();
                if (m_Animator) { m_Culling = m_Animator.cullingMode; m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            }
            public void Dispose()
            {
                for (int i = 0; i < Renderers.Length; i++) if (Renderers[i]) Renderers[i].updateWhenOffscreen = m_Offscreen[i];
                if (m_Animator) m_Animator.cullingMode = m_Culling;
            }
        }
    }
}
