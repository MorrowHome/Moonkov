// 服务端每帧更新保护装备和远征生存数值。这里把人物位置与移动状态代入供氧、饥渴等共用规则，玩家与普通 PMC 均有各自更新。
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS {
    public partial struct ServerGameSystem {
        private double m_NextProtectionSnapshot;

        private void UpdateProtectionGear(ref SystemState state) {
            // 每帧可以计算角色能量，库存重发的检查单独限频。换装备或耗氧改变了物品详情，才标记需发新快照。
            double now = SystemAPI.Time.ElapsedTime;
            bool send = now >= m_NextProtectionSnapshot;
            if (send) m_NextProtectionSnapshot = now + .5;
            foreach (var (session, joined, entity) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>()
                         .WithEntityAccess()) {
                var player = joined.ValueRO.PlayerEntity;
                if (!SystemAPI.Exists(player) || !SystemAPI.HasComponent<PredictedPlayerGhost>(player) ||
                    !state.EntityManager.HasComponent<RaidInventoryState>(entity))
                    continue;
                var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                var inventory = GetRaidInventory(ref state, entity);
                // 只有本局仍在进行且角色在当前模式可行动时允许回充，免得结算或倒地时继续生成资源。
                bool recover = session.ValueRO.Phase == RaidPhase.Active &&
                               (!health.ValueRO.ClashEnabled || health.ValueRO.ClashCanAct);
                EnergyShieldEquipment.Tick(ref health.ValueRW, inventory.Graph, SystemAPI.Time.DeltaTime, recover);
                // Halo energy already travels in the Ghost. Inventory only needs
                // another snapshot when the collar's displayed oxygen changes.
                // 光环能量已经跟角色 Ghost 同步。这里只在项圈显示的氧气或装备身份有变化时让库存重发，避免把整包库存当作能量计时器。
                if (send && inventory.ProtectionDisplayChanged) inventory.LastSentVersion = -1;
            }
            // NPCs have no connection/RaidSession. Their halos share the player
            // recovery rule; ordinary PMC life support is updated separately below.
            foreach (var (health, entity) in SystemAPI.Query<RefRW<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithEntityAccess()) {
                if (!state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity)) continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                EnergyShieldEquipment.Tick(ref health.ValueRW, brain.Inventory, SystemAPI.Time.DeltaTime);
            }
        }

        private void UpdateSurvival(ref SystemState state) {
            UpdateProtectionGear(ref state);
            if (ClashEnabled(ref state) || SystemAPI.ManagedAPI.HasSingleton<ArenaMatch>()) return;
            var map = MoonRaidMap.Active;
            if (map == null) return;
            // 供氧、饥渴和缺氧伤害按服务器当前 tick 推进。客户端只读同步数值，预测重放不会再次扣氧。
            uint tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick.TickIndexForValidTick;
            // Once per authoritative simulation update, outside prediction replay.
            // Settled players do not consume oxygen.
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>()
                         .WithEntityAccess()) {
                var player = joined.ValueRO.PlayerEntity;
                if (session.ValueRO.Phase != RaidPhase.Active || !SystemAPI.Exists(player) ||
                    !SystemAPI.HasComponent<PredictedPlayerGhost>(player) ||
                    !SystemAPI.HasComponent<LocalTransform>(player))
                    continue;
                var health = SystemAPI.GetComponentRW<PredictedPlayerGhost>(player);
                if (health.ValueRO.CurrentHealth <= 0) continue;
                var position = (Vector3)SystemAPI.GetComponent<LocalTransform>(player).Position;
                RaidInventoryState inventory = null;
                bool? moving = null;
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection)) {
                    inventory = GetRaidInventory(ref state, connection);
                    // 拿真实位置变化和控制器速度一起判断移动。被墙卡住时速度字段可能还显示在走，这时氧气不用按跑动消耗。
                    bool actualMove = inventory.SurvivalMotion.Observe(position, SystemAPI.Time.DeltaTime);
                    moving = health.ValueRO.ControllerState.MovementSpeed > .1f && actualMove;
                }
                // 从身体位置采样气体。脚先跨过门槛不等于面罩已经进入有氧区。
                // Check torso rather than the feet to avoid treating a doorway's
                // floor contact as breathable before the character has entered.
                var samplePosition = position + Vector3.up;
                BreathableZone.TrySample(samplePosition, out var atmosphere);
                RaidOxygen.Tick(ref health.ValueRW, atmosphere, map.OxygenParameters, SystemAPI.Time.DeltaTime, tick,
                    OxygenRefillZone.SampleSupply(samplePosition, atmosphere.Breathable),
                    health.ValueRO.HeadDomeEquipped, moving,
                    Inventory.SpecialEquipmentRules.OxygenDrainMultiplier(inventory?.Graph));
                RaidNutrition.Tick(ref health.ValueRW, SystemAPI.Time.DeltaTime, map.EnergyReserveSeconds,
                    map.HydrationReserveSeconds, map.DigestionSeconds, map.NutritionGraceSeconds,
                    map.StarvationDamagePerSecond, map.DehydrationDamagePerSecond, tick, moving);
                if (inventory != null) EnergyShieldEquipment.Flush(ref health.ValueRW, inventory.Graph);
            }
            // 教学只走上面的玩家生存流程。普通远征中的敌人也会耗氧和补氧，教学这里跳过敌人的生存更新。
            if (SystemAPI.HasSingleton<TutorialRaidMode>()) return;
            var tactical = TacticalWorld(ref state);
            double now = SystemAPI.Time.ElapsedTime;
            float dt = SystemAPI.Time.DeltaTime;
            foreach (var (pose, health, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<PredictedPlayerGhost>>()
                         .WithAll<DollSingerEnemy, Simulate>().WithNone<ClashBot>().WithEntityAccess()) {
                if (health.ValueRO.CurrentHealth <= 0 ||
                    !state.EntityManager.HasComponent<DollSingerEnemyBrain>(entity))
                    continue;
                var brain = state.EntityManager.GetComponentObject<DollSingerEnemyBrain>(entity);
                Vector3 position = pose.ValueRO.Position;
                bool moved = brain.OxygenMotion.Observe(position, dt);
                bool sample = now >= brain.NextAtmosphereSample;
                if (sample) {
                    var samplePosition = position + Vector3.up;
                    BreathableZone.TrySample(samplePosition, out brain.CachedAtmosphere);
                    brain.CachedOxygenSupply =
                        OxygenRefillZone.SampleSupply(samplePosition, brain.CachedAtmosphere.Breathable);
                    // 敌人仍按 tick 计算耗氧，但环境与供氧点约半秒采样一次，用上一结果填中间帧，减少查区域的次数。
                    brain.NextAtmosphereSample = now + .5;
                }
                var status = TacticalStatus(tactical, entity);
                PmcLifeSupport.Tick(ref health.ValueRW, brain.Inventory, status, brain.CachedAtmosphere,
                    map.OxygenParameters, dt, tick, brain.CachedOxygenSupply,
                    health.ValueRO.ControllerState.MovementSpeed > .1f && moved, sample);
                var beforeNutrition = health.ValueRO;
                RaidNutrition.Tick(ref health.ValueRW, dt, map.EnergyReserveSeconds, map.HydrationReserveSeconds,
                    map.DigestionSeconds, map.NutritionGraceSeconds, map.StarvationDamagePerSecond,
                    map.DehydrationDamagePerSecond, tick, health.ValueRO.ControllerState.MovementSpeed > .1f && moved);
                // 将营养损伤前后状态交给 PMC 的状态记录。敌人当帧死亡时也立即把装备余量写回，供随后掉落使用。
                PmcStatusDamage.Record(status, beforeNutrition, health.ValueRO);
                if (!sample && health.ValueRO.CurrentHealth <= 0)
                    EnergyShieldEquipment.Flush(ref health.ValueRW, brain.Inventory);
            }
        }
    }
}