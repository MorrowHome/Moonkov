using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private void UpdateSurvival(ref SystemState state)
        {
            var map = MoonRaidMap.Active;
            if (map == null) return;
            uint tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick.TickIndexForValidTick;
            // Once per authoritative simulation update, outside prediction replay.
            // Settled players and PMCs do not consume the player's oxygen supply.
            foreach (var (session, joined) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>())
            {
                var player = joined.ValueRO.PlayerEntity;
                if (session.ValueRO.Phase != RaidPhase.Active || !SystemAPI.Exists(player) ||
                    !SystemAPI.HasComponent<PredictedPlayerGhost>(player) || !SystemAPI.HasComponent<LocalTransform>(player)) continue;
                var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                if (health.ValueRO.CurrentHealth <= 0) continue;
                var position = (Vector3)SystemAPI.GetComponent<LocalTransform>(player).Position;
                // Check torso rather than the feet to avoid treating a doorway's
                // floor contact as breathable before the character has entered.
                bool air = BreathableZone.Contains(position + Vector3.up);
                RaidHealth.TickOxygen(ref health.ValueRW, air, SystemAPI.Time.DeltaTime,
                    map.OutdoorOxygenSeconds, map.OxygenRecoveryPerSecond,
                    map.HypoxiaGraceSeconds, map.HypoxiaDamagePerSecond, tick);
            }
        }
    }
}
