// 接收光环扫描的脉冲和目标消息，按扫描编号和有效期决定哪些提示该显示，交给 HaloScanRenderer。服务端负责选择扫描结果。
using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Exact contacts only arrive after the authoritative pulse reaches their sampled position.
    public sealed class HaloScanClientState
    {
        public HaloScanPulseRpc Pulse;
        public double StartedAt;
        public bool Active;
        public readonly HaloScanContactRpc[] Contacts = new HaloScanContactRpc[HaloScanRules.MaxContacts];
        public readonly double[] ReceivedAt = new double[HaloScanRules.MaxContacts];
        public readonly Renderer[][] Outlines = new Renderer[HaloScanRules.MaxContacts][];

        public bool Receive(HaloScanPulseRpc pulse, double now)
        {
            if (!math.all(math.isfinite(pulse.Origin)) || pulse.Sequence == 0 ||
                pulse.OwnerGhostId == Pulse.OwnerGhostId && pulse.OwnerSpawnTick == Pulse.OwnerSpawnTick && pulse.Sequence <= Pulse.Sequence) return false;
            Clear(); Pulse = pulse; StartedAt = now; Active = true; return true;
        }

        // 目标和脉冲用创建者的 Ghost ID、出生 tick 与扫描序号绑定。重新出生或下一次扫描后，旧目标不能混到这次提示里。
        public bool Receive(HaloScanContactRpc contact, double now)
        {
            int i = contact.Index;
            if (!Active || i < 0 || i >= Contacts.Length || contact.Sequence != Pulse.Sequence ||
                contact.OwnerGhostId != Pulse.OwnerGhostId || contact.OwnerSpawnTick != Pulse.OwnerSpawnTick ||
                contact.GhostId <= 0 || Contacts[i].GhostId != 0 || now - StartedAt > HaloScanRules.Duration + 2 ||
                !HaloSupportRules.Within(Pulse.Origin, contact.Position, HaloScanRules.Range + .01f)) return false;
            Contacts[i] = contact; ReceivedAt[i] = now; return true;
        }

        public bool Visible(int index, double now) => Active && Contacts[index].GhostId > 0 &&
            now - ReceivedAt[index] < HaloScanRules.RevealSeconds;
        public void Clear()
        { Active = false; Array.Clear(Contacts, 0, Contacts.Length); Array.Clear(ReceivedAt, 0, ReceivedAt.Length); Array.Clear(Outlines, 0, Outlines.Length); }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HaloScanClientSystem : SystemBase
    {
        private readonly HaloScanClientState m_State = new();
        private EntityQuery m_Owner;
        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            m_Owner = GetEntityQuery(ComponentType.ReadOnly<PredictedPlayerGhost>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<GhostInstance>(), ComponentType.ReadOnly<GhostGameObjectLink>());
        }
        protected override void OnStopRunning() { m_State.Clear(); HaloScanRenderer.Clear(); }
        protected override void OnDestroy() { m_State.Clear(); HaloScanRenderer.Clear(); }

        protected override void OnUpdate()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            using var owners = m_Owner.ToEntityArray(Allocator.Temp);
            Entity owner = owners.Length == 1 ? owners[0] : Entity.Null;
            var identity = owner != Entity.Null ? EntityManager.GetComponentData<GhostInstance>(owner) : default;
            bool alive = owner != Entity.Null && HaloSupportRules.CanProtect(EntityManager.GetComponentData<PredictedPlayerGhost>(owner));
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, entity) in SystemAPI.Query<RefRO<HaloScanPulseRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (alive && rpc.ValueRO.OwnerGhostId == identity.ghostId && rpc.ValueRO.OwnerSpawnTick == identity.spawnTick.SerializedData)
                    m_State.Receive(rpc.ValueRO, now);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, entity) in SystemAPI.Query<RefRO<HaloScanContactRpc>>().WithAll<ReceiveRpcCommandRequest>().WithEntityAccess())
            {
                if (alive && m_State.Receive(rpc.ValueRO, now)) FindOutline(rpc.ValueRO);
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager); ecb.Dispose();
            if (!alive || !m_State.Active || identity.ghostId != m_State.Pulse.OwnerGhostId ||
                identity.spawnTick.SerializedData != m_State.Pulse.OwnerSpawnTick || now - m_State.StartedAt > HaloScanRules.Duration + HaloScanRules.RevealSeconds + 2)
            { m_State.Clear(); HaloScanRenderer.Clear(); return; }
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
            Camera camera = link.LinkedInstance && link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation)
                ? presentation.OwnedCamera : null;
            HaloScanRenderer.Publish(camera, m_State, now);
        }

        private void FindOutline(in HaloScanContactRpc contact)
        {
            foreach (var (ghost, entity) in SystemAPI.Query<RefRO<GhostInstance>>().WithAll<GhostGameObjectLink>().WithEntityAccess())
            {
                if (ghost.ValueRO.ghostId != contact.GhostId || ghost.ValueRO.spawnTick.SerializedData != contact.SpawnTick) continue;
                var link = EntityManager.GetComponentObject<GhostGameObjectLink>(entity);
                if (link.LinkedInstance) m_State.Outlines[contact.Index] = link.LinkedInstance.GetComponentsInChildren<Renderer>(false);
                break;
            }
        }
    }
}
