using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private double m_NextProtectionSnapshot;
        private void UpdateProtectionGear(ref SystemState state)
        {
            double now=SystemAPI.Time.ElapsedTime;bool send=now>=m_NextProtectionSnapshot;
            if(send)m_NextProtectionSnapshot=now+.5;
            foreach(var (session,joined,entity) in SystemAPI.Query<RefRO<RaidSession>,RefRO<JoinedClient>>().WithEntityAccess())
            {
                var player=joined.ValueRO.PlayerEntity;
                if(!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player) ||
                    !state.EntityManager.HasComponent<RaidInventoryState>(entity))continue;
                var health=SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                var inventory=GetRaidInventory(ref state,entity);
                bool recover=session.ValueRO.Phase==RaidPhase.Active && (!health.ValueRO.ClashEnabled || health.ValueRO.ClashCanAct);
                EnergyShieldEquipment.Tick(ref health.ValueRW,inventory.Graph,SystemAPI.Time.DeltaTime,recover);
                if(send && (health.ValueRO.ShieldEquipped || health.ValueRO.HeadDomeEquipped))inventory.LastSentVersion=-1;
            }
        }
        private void UpdateSurvival(ref SystemState state)
        {
            UpdateProtectionGear(ref state);
            if(ClashEnabled(ref state))return;
            var map = MoonRaidMap.Active;
            if (map == null) return;
            uint tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick.TickIndexForValidTick;
            // Once per authoritative simulation update, outside prediction replay.
            // Settled players and PMCs do not consume the player's oxygen supply.
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                var player = joined.ValueRO.PlayerEntity;
                if (session.ValueRO.Phase != RaidPhase.Active || !SystemAPI.Exists(player) ||
                    !SystemAPI.HasComponent<PredictedPlayerGhost>(player) || !SystemAPI.HasComponent<LocalTransform>(player)) continue;
                var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                if (health.ValueRO.CurrentHealth <= 0) continue;
                var position = (Vector3)SystemAPI.GetComponent<LocalTransform>(player).Position;
                // Check torso rather than the feet to avoid treating a doorway's
                // floor contact as breathable before the character has entered.
                var samplePosition = position + Vector3.up;
                BreathableZone.TrySample(samplePosition, out var atmosphere);
                RaidOxygen.Tick(ref health.ValueRW, atmosphere, map.OxygenParameters, SystemAPI.Time.DeltaTime,
                    tick, OxygenRefillZone.SampleSupply(samplePosition, atmosphere.Breathable),health.ValueRO.HeadDomeEquipped);
                RaidNutrition.Tick(ref health.ValueRW, SystemAPI.Time.DeltaTime,
                    map.EnergyReserveSeconds, map.HydrationReserveSeconds, map.DigestionSeconds,
                    map.NutritionGraceSeconds, map.StarvationDamagePerSecond, map.DehydrationDamagePerSecond, tick);
                if(state.EntityManager.HasComponent<RaidInventoryState>(connection))
                    EnergyShieldEquipment.Flush(ref health.ValueRW,GetRaidInventory(ref state,connection).Graph);
            }
        }
    }
}
