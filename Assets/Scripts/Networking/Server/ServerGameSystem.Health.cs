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
            // Ordinary PMCs have no connection/RaidSession. Only equipped shields
            // need recovery here; their life support is updated separately below.
            foreach (var (health, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithNone<ClashBot>().WithEntityAccess())
            {
                if (!health.ValueRO.ShieldEquipped || !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity)) continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                EnergyShieldEquipment.Tick(ref health.ValueRW, brain.Inventory, SystemAPI.Time.DeltaTime);
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
            // Settled players do not consume oxygen.
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                var player = joined.ValueRO.PlayerEntity;
                if (session.ValueRO.Phase != RaidPhase.Active || !SystemAPI.Exists(player) ||
                    !SystemAPI.HasComponent<PredictedPlayerGhost>(player) || !SystemAPI.HasComponent<LocalTransform>(player)) continue;
                var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                if (health.ValueRO.CurrentHealth <= 0) continue;
                var position = (Vector3)SystemAPI.GetComponent<LocalTransform>(player).Position;
                RaidInventoryState inventory=null;bool? moving=null;
                if(state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    inventory=GetRaidInventory(ref state,connection);
                    bool actualMove=inventory.SurvivalMotion.Observe(position,SystemAPI.Time.DeltaTime);
                    moving=health.ValueRO.ControllerState.MovementSpeed>.1f && actualMove;
                }
                // Check torso rather than the feet to avoid treating a doorway's
                // floor contact as breathable before the character has entered.
                var samplePosition = position + Vector3.up;
                BreathableZone.TrySample(samplePosition, out var atmosphere);
                RaidOxygen.Tick(ref health.ValueRW, atmosphere, map.OxygenParameters, SystemAPI.Time.DeltaTime,
                    tick, OxygenRefillZone.SampleSupply(samplePosition, atmosphere.Breathable),health.ValueRO.HeadDomeEquipped,moving);
                RaidNutrition.Tick(ref health.ValueRW, SystemAPI.Time.DeltaTime,
                    map.EnergyReserveSeconds, map.HydrationReserveSeconds, map.DigestionSeconds,
                    map.NutritionGraceSeconds, map.StarvationDamagePerSecond, map.DehydrationDamagePerSecond, tick,moving);
                if(inventory!=null)EnergyShieldEquipment.Flush(ref health.ValueRW,inventory.Graph);
            }
            if (SystemAPI.HasSingleton<TutorialRaidMode>()) return;
            var tactical = TacticalWorld(ref state);
            double now = SystemAPI.Time.ElapsedTime;
            float dt = SystemAPI.Time.DeltaTime;
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithNone<ClashBot>().WithEntityAccess())
            {
                if (health.ValueRO.CurrentHealth <= 0 || !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity)) continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                Vector3 position = pose.ValueRO.Position;
                bool moved = brain.OxygenMotion.Observe(position, dt);
                bool sample = now >= brain.NextAtmosphereSample;
                if (sample)
                {
                    var samplePosition = position + Vector3.up;
                    BreathableZone.TrySample(samplePosition, out brain.CachedAtmosphere);
                    brain.CachedOxygenSupply = OxygenRefillZone.SampleSupply(samplePosition, brain.CachedAtmosphere.Breathable);
                    brain.NextAtmosphereSample = now + .5;
                }
                var status = TacticalStatus(tactical, entity);
                PmcLifeSupport.Tick(ref health.ValueRW, brain.Inventory, status,
                    brain.CachedAtmosphere, map.OxygenParameters, dt, tick, brain.CachedOxygenSupply,
                    health.ValueRO.ControllerState.MovementSpeed > .1f && moved, flush: sample);
                var beforeNutrition = health.ValueRO;
                RaidNutrition.Tick(ref health.ValueRW, dt, map.EnergyReserveSeconds, map.HydrationReserveSeconds,
                    map.DigestionSeconds, map.NutritionGraceSeconds, map.StarvationDamagePerSecond,
                    map.DehydrationDamagePerSecond, tick, health.ValueRO.ControllerState.MovementSpeed > .1f && moved);
                PmcStatusDamage.Record(status, beforeNutrition, health.ValueRO);
                if (!sample && health.ValueRO.CurrentHealth <= 0) EnergyShieldEquipment.Flush(ref health.ValueRW, brain.Inventory);
            }
        }
    }
}
