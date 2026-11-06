using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial class ServerPlayerMovementSystem
    {
        private sealed class ScanSweep
        {
            internal readonly HaloScanContactRpc[] Contacts = new HaloScanContactRpc[HaloScanRules.MaxContacts];
            internal readonly Entity[] Targets = new Entity[HaloScanRules.MaxContacts];
            internal readonly float[] Arrival = new float[HaloScanRules.MaxContacts];
            internal int Count, Sent;
            internal float Elapsed;
            internal Entity Connection;
        }
        private readonly Dictionary<Entity, ScanSweep> m_HaloScans = new();
        private readonly List<Entity> m_ExpiredScans = new();

        private void UpdateHaloScans(uint tick, float dt, ref EntityCommandBuffer ecb)
        {
            if (!float.IsFinite(dt) || dt <= 0) return;
            m_ExpiredScans.Clear();
            foreach (var pair in m_HaloScans)
                if (!EntityManager.Exists(pair.Key) || !ActiveTransferActor(pair.Key, false)) m_ExpiredScans.Add(pair.Key);
            foreach (var actor in m_ExpiredScans) m_HaloScans.Remove(actor);

            foreach (var actor in m_TransferActors)
            {
                if (!IsHuman(actor)) continue;
                var state = EntityManager.GetComponentData<PredictedPlayerGhost>(actor);
                bool fresh = TryReadTransferInput(actor, tick, out var input);
                bool press = fresh && input.HaloScan && !state.HaloScanLatched;
                bool changed = fresh && state.HaloScanLatched != input.HaloScan || state.HaloScanCooldown > 0;
                if (fresh) state.HaloScanLatched = input.HaloScan;
                state.HaloScanCooldown = Mathf.Max(0, state.HaloScanCooldown - dt);
                if (press && HaloScanRules.CanScan(state) && ActiveTransferActor(actor, false))
                {
                    HaloEnergyRules.Spend(ref state, HaloScanRules.Cost);
                    state.HaloScanSequence++;
                    state.HaloScanCooldown = HaloScanRules.Cooldown;
                    BeginHaloScan(actor, state, ref ecb);
                    changed = true;
                }
                if (changed) EntityManager.SetComponentData(actor, state);
                if (!m_HaloScans.TryGetValue(actor, out var sweep)) continue;
                if (!EnergyShieldRules.Alive(state) || !state.HaloEquipped || !ActiveTransferActor(actor, false))
                { sweep.Count = sweep.Sent = 0; continue; }
                sweep.Elapsed += dt;
                while (sweep.Sent < sweep.Count && sweep.Arrival[sweep.Sent] <= sweep.Elapsed)
                {
                    int index = sweep.Sent++;
                    Entity target = sweep.Targets[index];
                    if (EntityManager.HasComponent<PredictedPlayerGhost>(target) &&
                        HostileHaloTarget(actor, state, target, EntityManager.GetComponentData<PredictedPlayerGhost>(target)))
                        SendHaloRpc(sweep.Contacts[index], sweep.Connection, ref ecb);
                }
            }
        }

        private void BeginHaloScan(Entity actor, in PredictedPlayerGhost state, ref EntityCommandBuffer ecb)
        {
            if (!SystemAPI.TryGetSingletonBuffer<ClientsMap>(out var clients)) return;
            var identity = EntityManager.GetComponentData<GhostInstance>(actor);
            int owner = EntityManager.GetComponentData<GhostOwner>(actor).NetworkId;
            if (owner <= 0 || owner >= clients.Length) return;
            if (!m_HaloScans.TryGetValue(actor, out var sweep)) m_HaloScans[actor] = sweep = new ScanSweep();
            sweep.Count = sweep.Sent = 0; sweep.Elapsed = 0; sweep.Connection = clients[owner].ConnectionEntity;
            float3 origin = HaloScanRules.Origin(state);
            foreach (var (other, pose, ghost, target) in SystemAPI.Query<RefRO<PredictedPlayerGhost>, RefRO<LocalTransform>, RefRO<GhostInstance>>().WithEntityAccess())
            {
                if (!HostileHaloTarget(actor, state, target, other.ValueRO) || ghost.ValueRO.ghostId <= 0 ||
                    IsHuman(target) && !ActiveTransferActor(target, false)) continue;
                float3 point = pose.ValueRO.Position + new float3(0, .9f, 0);
                if (!HaloSupportRules.Within(origin, point, HaloScanRules.Range)) continue;
                float arrival = math.distance(origin, point) / HaloScanRules.Speed;
                int index = sweep.Count;
                while (index > 0 && sweep.Arrival[index - 1] > arrival) index--;
                if (index >= HaloScanRules.MaxContacts) continue;
                int last = Mathf.Min(sweep.Count, HaloScanRules.MaxContacts - 1);
                for (int i = last; i > index; i--)
                { sweep.Contacts[i] = sweep.Contacts[i - 1]; sweep.Arrival[i] = sweep.Arrival[i - 1]; sweep.Targets[i] = sweep.Targets[i - 1]; }
                sweep.Contacts[index] = new HaloScanContactRpc { OwnerGhostId = identity.ghostId,
                    OwnerSpawnTick = identity.spawnTick.SerializedData, Sequence = state.HaloScanSequence,
                    GhostId = ghost.ValueRO.ghostId, SpawnTick = ghost.ValueRO.spawnTick.SerializedData, Position = point };
                sweep.Arrival[index] = arrival;
                sweep.Targets[index] = target;
                sweep.Count = Mathf.Min(sweep.Count + 1, HaloScanRules.MaxContacts);
            }
            for (int i = 0; i < sweep.Count; i++) sweep.Contacts[i].Index = i;
            SendHaloRpc(new HaloScanPulseRpc { OwnerGhostId = identity.ghostId, OwnerSpawnTick = identity.spawnTick.SerializedData,
                Sequence = state.HaloScanSequence, Weapon = state.EquippedWeaponID, Origin = origin }, sweep.Connection, ref ecb);
        }

        private static void SendHaloRpc<T>(T value, Entity connection, ref EntityCommandBuffer ecb) where T : unmanaged, IRpcCommand
        {
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, value);
            ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }
}
