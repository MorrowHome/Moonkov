// 每个人形 AI 的记忆和行动状态。记录它确实观察到的线索、正在干的事、库存和小队身份。
// 文件前面是共用规则，比如枪声线索、手雷避险、补氧、装填和体力。主循环在 DollSingerEnemySystem。
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 行动是这段时间想完成的事。Approaching 等掩体子状态记录的是这件事在执行到哪一步。
    public enum PmcAction {
        Scavenge,
        Extract,
        Investigate,
        Search,
        Engage,
        Cover,
        Flank,
        Retreat,
        Reload,
        HoldAngle,
        Heal,
        ClashObjective,
        EvadeGrenade,
        EvadeHazard,
        FollowSquad
    }

    public enum PmcCoverPhase {
        Approaching,
        Settling,
        Advancing,
        Holding,
        Returning
    }

    // 声音只说明大概哪里有动静，不能据此确定敌人或开火线路。
    // 机器、枪声之类只说明哪里有动静。更新线索可打断搜刮，不凭它获得精确枪线。
    public static class PmcSoundClue {
        public static bool Hear(DollSingerEnemyBrain brain, string id, Vector3 position, double now) {
            if (brain == null || string.IsNullOrEmpty(id) || brain.IsClashBot || brain.Leaving ||
                brain.Target != Entity.Null || brain.MissionAlarmId == id)
                return false;
            brain.MissionAlarmId = id;
            brain.MissionAlarmPosition = position;
            brain.MissionAlarmHeardAt = now;
            brain.MissionAlarmUntil = now + 30;
            brain.NextDecision = 0;
            if (brain.Action == PmcAction.Scavenge) brain.CommitUntil = 0;
            return true;
        }
    }

    // 观察者只记住自己看到时的位置。
    // 转头后会把线索保留到爆炸，但不会透墙追踪手雷。
    // 记忆保存最后一处真正看见的手雷位置和时效。转开视线不立刻忘掉，手雷也不会隔墙自动跟踪。
    public struct PmcGrenadeAwareness {
        private ulong m_ObservedId, m_DangerId;
        private double m_ObservedSince, m_LastObservation, m_RecognizedAt;
        public Vector3 Position;
        public float Radius;
        public double Until;
        public bool Active(double now) { return double.IsFinite(now) && now >= m_RecognizedAt && now < Until; }
        public void Miss() { m_ObservedId = 0; }

        public bool Observe(ulong id, Vector3 position, float radius, double fuseUntil, double now) {
            if (id == 0 || !PmcGrenadeRules.Finite(position) || !float.IsFinite(radius) || radius <= 0 ||
                !double.IsFinite(fuseUntil) || !double.IsFinite(now) || fuseUntil <= now) {
                Miss();
                return false;
            }
            if (m_ObservedId != id || now < m_LastObservation || now - m_LastObservation > .25) {
                m_ObservedId = id;
                m_ObservedSince = m_LastObservation = now;
                return false;
            }
            m_LastObservation = now;
            if (now - m_ObservedSince < .25) return false;
            bool changed = !Active(now) || m_DangerId != id || (position - Position).sqrMagnitude > 1;
            m_DangerId = id;
            m_RecognizedAt = now;
            Position = position;
            Radius = radius;
            Until = System.Math.Min(fuseUntil, now + 4);
            return changed;
        }
    }

    // 逃离方向不能在起点先靠近爆炸点。几何检查对每条候选路线单独做。
    public static class PmcGrenadeRules {
        public static bool Finite(Vector3 point) {
            return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }

        public static ulong EnergyId(uint id) { return id == 0 ? 0 : (1UL << 32) | id; }

        public static bool Candidate(
            bool clash,
            TacticalItemKind kind,
            TacticalWorldPhase phase,
            float distance,
            float radius,
            float angle,
            float cone) {
            return phase == TacticalWorldPhase.Flying &&
                   (kind == TacticalItemKind.Frag || kind == TacticalItemKind.Dynamite) &&
                   VisibleDanger(clash, distance, radius, angle, cone);
        }

        public static bool VisibleDanger(bool clash, float distance, float radius, float angle, float cone) {
            return !clash && float.IsFinite(distance) && distance >= 0 && float.IsFinite(radius) && radius > 0 &&
                   distance <= radius + 2 && float.IsFinite(angle) && float.IsFinite(cone) && angle >= 0 &&
                   angle <= cone;
        }

        // 如果路线第一段先靠近最后看到的手雷，就拒绝它。
        // 逃跑受阻后也不会因此掉头跑进爆点。
        public static bool SegmentSafe(Vector3 from, Vector3 to, Vector3 grenade, float minimum) {
            if (!Finite(from) || !Finite(to) || !Finite(grenade) || !float.IsFinite(minimum) || minimum < 0)
                return false;
            double ax = from.x, az = from.z, dx = (double)to.x - ax, dz = (double)to.z - az;
            double length = dx * dx + dz * dz;
            double t = length > 0
                ? System.Math.Max(0,
                    System.Math.Min(1, (((double)grenade.x - ax) * dx + ((double)grenade.z - az) * dz) / length))
                : 0;
            double x = ax + t * dx - grenade.x, z = az + t * dz - grenade.z;
            return x * x + z * z >= (double)minimum * minimum;
        }
    }

    // 以实际走过的地面距离更新体力。卡在墙前或原地走动作，不按请求速度来记。
    public static class PmcSprintRules {
        // 体力耗完时改为走路恢复，但见到爆炸威胁时仍可尝试紧急冲刺。
        public static bool Allows(bool clash, PmcAction action, bool exhausted) {
            return clash || !exhausted || action == PmcAction.EvadeGrenade || action == PmcAction.EvadeHazard;
        }

        public static void Tick(
            ref TacticalSprintState stamina,
            ref float3 previous,
            ref bool known,
            float3 position,
            float dt,
            bool alive,
            bool sprinting,
            float speed,
            float weight,
            bool stimulated) {
            bool valid = math.all(math.isfinite(position));
            var travel = valid && known ? position - previous : float3.zero;
            float threshold = math.max(.005f, float.IsFinite(dt) ? dt * .15f : 0);
            float maximum = math.max(2, float.IsFinite(dt) ? dt * 12 : 0);
            float distance = travel.x * travel.x + travel.z * travel.z;
            bool moved = valid && known && distance > threshold * threshold && distance <= maximum * maximum;
            if (valid) previous = position;
            known = alive && valid && float.IsFinite(dt) && dt > 0;
            TacticalSprintRules.Tick(ref stamina, dt, alive && sprinting && speed > .1f && moved, stimulated, alive,
                weight);
        }
    }

    // 备用入口并不是正式撤离点。
    // 附近有真人可能看到人消失时，让 AI 继续留在场景里。
    // 找不到正式出口时可回入口离场，但如果附近真人会目击到角色消失，要等可安全退出的窗口。
    public static class PmcLocalExitRules {
        public const float NearbyRange = 30, SightRange = 100, AimedSightRange = 1000;
        private const float AimedConeDot = .990268f; // 以当前视角方向为中心，容许 8 度偏角。
        private static int s_WorldMask = int.MinValue;

        public static bool Nearby(Vector3 player, Vector3 actor) {
            return !PmcGrenadeRules.Finite(player) || !PmcGrenadeRules.Finite(actor) ||
                   (player - actor).sqrMagnitude <= NearbyRange * NearbyRange;
        }

        public static bool InSightCone(Vector3 eye, Vector3 forward, Vector3 actor, bool aiming = false) {
            if (!PmcGrenadeRules.Finite(eye) || !PmcGrenadeRules.Finite(actor)) return true;
            var delta = actor + Vector3.up - eye;
            if (!PmcGrenadeRules.Finite(delta)) return true;
            bool distant = delta.sqrMagnitude > SightRange * SightRange;
            if (distant && (!aiming || delta.sqrMagnitude > AimedSightRange * AimedSightRange)) return false;
            if (!PmcGrenadeRules.Finite(forward) || forward.sqrMagnitude < .01f || delta.sqrMagnitude < .001f)
                return true;
            return Vector3.Dot(forward.normalized, delta.normalized) >= (distant ? AimedConeDot : .5f);
        }

        public static bool Observed(PhysicsScene scene, Vector3 actor, IReadOnlyList<RaidSpawnObserver> observers) {
            if (!scene.IsValid() || observers == null || !PmcGrenadeRules.Finite(actor)) return true;
            if (s_WorldMask == int.MinValue) s_WorldMask = LayerMask.GetMask("Default", "Ground");
            foreach (var observer in observers) {
                if (!observer.CheckVisibility) continue;
                if (Nearby(observer.Position, actor)) return true;
                var eye = observer.Position + Vector3.up * 1.6f;
                if (!InSightCone(eye, observer.Forward, actor, observer.Aiming)) continue;

                bool Clear(Vector3 point) {
                    var delta = point - eye;
                    return delta.sqrMagnitude < .001f || !scene.Raycast(eye, delta.normalized, out _, delta.magnitude,
                        s_WorldMask, QueryTriggerInteraction.Ignore);
                }

                if (Clear(actor + Vector3.up * .7f) || Clear(actor + Vector3.up * 1.5f)) return true;
            }
            return false;
        }

        public static bool Advance(
            ref float progress,
            bool inExit,
            bool localExit,
            bool observed,
            float dt,
            float duration) {
            if (!inExit || (localExit && observed)) {
                progress = 0;
                return false;
            }
            if (float.IsFinite(dt) && dt > 0) progress += dt;
            return float.IsFinite(duration) && duration > 0 && progress >= duration;
        }
    }

    // 持续区域风险沿观察过的位置和结束时间保存，方便 AI 选择必须中断当前动作的避险行为。
    public static class PmcHazardRules {
        public static bool Active(
            bool clash,
            TacticalServerWorld.Status status,
            TacticalWorldPhase phase,
            double areaUntil,
            double now,
            bool inside) {
            if (clash || status == null || !inside || !double.IsFinite(now) || !double.IsFinite(areaUntil) ||
                areaUntil <= now)
                return false;
            double until = phase == TacticalWorldPhase.Fire ? status.Fire :
                phase == TacticalWorldPhase.Wire ? status.Wire :
                phase == TacticalWorldPhase.Poison && status.Antidote <= now ? status.Poison : 0;
            return double.IsFinite(until) && until > now;
        }
    }

    // 本次更新已被共用的状态处理动作消耗时返回 true。
    // 由调用方做真实的安全与位置检查，并处理其它用药逻辑。
    // 止血或灭火能获得独立的治疗阶段。等待安全窗口并持续验证条件，真正完成才更新状态。
    public static class PmcSelfTreatment {
        public static bool Advance(DollSingerEnemyBrain brain, bool eligible, bool bleeding, bool burning, double now) {
            if (!eligible) {
                if (brain.HealUntil > 0) brain.NextHeal = now + .75;
                brain.HealUntil = 0;
                brain.HealItemId = null;
                brain.HealStatus = false;
                return true;
            }
            if (brain.HealUntil > 0 && !brain.HealStatus && (bleeding || burning)) {
                brain.HealUntil = 0;
                brain.HealItemId = null;
                brain.NextHeal = now + .75;
                brain.CommitUntil = brain.NextDecision = 0;
                return true;
            }
            if (brain.HealUntil <= 0 && (bleeding || burning)) {
                if (now < brain.NextHeal) return true;
                brain.HealStatus = true;
                brain.HealItemId = null;
                brain.HealUntil = now + TacticalItemRules.StopBleedSeconds;
                brain.CommitUntil = brain.HealUntil;
                return true;
            }
            if (!brain.HealStatus) return false;
            if ((bleeding || burning) && now < brain.HealUntil) return true;
            if (bleeding) brain.SelfStatus.Bleed = 0;
            if (burning) brain.SelfStatus.Fire = 0;
            brain.HealUntil = 0;
            brain.HealStatus = false;
            brain.NextHeal = now + .75;
            brain.CommitUntil = brain.NextDecision = 0;
            return true;
        }
    }

    // 少数普通远征角色带着与玩家相同的实体护盾物品。
    // 教学与悬赏仍沿用原来的装备。
    // 普通远征 AI 有机会穿真实的护具。扣掉的充能或损耗留在物品上，死亡时可由尸体继承。
    public static class PmcProtectionLoadout {
        public const float ShieldChance = 1f / 3f;

        // 旧档案可保留旧版发生器，但不再生成或装备这种东西。
        public static bool ShouldEquip(bool clash, bool tutorial, float roll) { return false; }
        public static bool TryEquip(InventoryGraph inventory, bool clash, bool tutorial, float roll) { return false; }

        public static void Initialize(ref PredictedPlayerGhost health, InventoryGraph inventory) {
            // 没有生命维持设备的装备方案沿用已有储备，教学也包括在内。
            // 仅绑定护盾不应该改变这份储备。
            float oxygen = health.Oxygen;
            EnergyShieldEquipment.Synchronize(ref health, inventory);
            if (!health.HeadDomeEquipped) health.Oxygen = oxygen;
        }

        public static bool CanUseLootedShield(InventoryItem item) { return false; }
        public static bool BindLootedShield(ref PredictedPlayerGhost health, InventoryGraph inventory) { return false; }
    }

    // 即时的库存操作也要等安全时机。
    // 角色自己在搜刮、治疗、撤离或避险时不能顺便做这些操作。
    // 换电池、穿装备等短操作也要查手头有没有正在搜刮、治疗或读条。用同一条件避免重叠做两件事。
    public static class PmcResourceActions {
        public static bool SafeWindow(
            DollSingerEnemyBrain brain,
            in PredictedPlayerGhost health,
            bool tutorial,
            bool safe,
            double now,
            bool allowCriticalOxygen = false,
            bool continuingNutrition = false,
            bool allowCriticalNutrition = false) {
            bool oxygenUrgent = allowCriticalOxygen && PmcLifeSupport.Low(health, PmcLifeSupport.DepartureThreshold);
            bool nutritionUrgent = allowCriticalNutrition && PmcNutrition.Low(health, PmcNutrition.DepartureThreshold);
            if (brain == null || brain.IsClashBot || tutorial || !safe || !double.IsFinite(now) ||
                health.CurrentHealth <= 0 || RaidHealth.HandsBusy(health) || brain.Visible || brain.HazardDanger ||
                brain.Grenade.Active(now) || brain.HealUntil > 0 || brain.Interacting || brain.LootUntil > 0 ||
                (brain.NutritionUntil > 0 && !continuingNutrition) || (brain.ExtractionProgress > 0 && !oxygenUrgent) ||
                brain.Extracted)
                return false;
            bool passive = brain.Action == PmcAction.Scavenge || brain.Action == PmcAction.Extract;
            bool recovering = (oxygenUrgent || nutritionUrgent) && !brain.IsPeeking &&
                              (brain.Action == PmcAction.Retreat || brain.Action == PmcAction.Cover ||
                               brain.Action == PmcAction.Search || brain.Action == PmcAction.Investigate ||
                               brain.Action == PmcAction.HoldAngle);
            if (!passive && !recovering) return false;
            var status = brain.SelfStatus;
            return status == null || (status.Bleed <= now && status.Fire <= now &&
                                      (status.Poison <= now || status.Antidote > now));
        }
    }

    // 护盾和光环消耗同一批实体电池。
    // 先留够一仓弹药所需的可用能量，再拿电池给护盾用。
    // 护盾与枪用同一类电池。优先留出可访问的一仓火力所需能量，再考虑给护盾充。
    public static class PmcShieldSupply {
        public const float RechargeThreshold = 25;

        public static bool MayRecharge(
            DollSingerEnemyBrain brain,
            in PredictedPlayerGhost health,
            bool tutorial,
            bool safe,
            double now) {
            return PmcResourceActions.SafeWindow(brain, health, tutorial, safe, now) && health.ShieldEquipped &&
                   float.IsFinite(health.ShieldEnergy) && health.ShieldEnergy >= 0 &&
                   health.ShieldEnergy <= RechargeThreshold;
        }

        public static bool TryRecharge(
            ref PredictedPlayerGhost health,
            InventoryGraph inventory,
            int roundCost,
            int magazineSize,
            WeaponData weapon = null) {
            if (inventory == null || !health.HaloEquipped || HaloEnergyRules.Energy(health) > RechargeThreshold)
                return false;
            return HaloEnergyInventory.RechargeFirst(ref health, inventory, weapon);
        }
    }

    // 服装和补氧道具确实在 AI 身上。
    // AI 和真人共用空气检测、耗氧和一次性补氧事务。
    // 生存所需时间、氧气区与补氧物品复用玩家规则。AI 也会因为缺氧受伤或考虑离场。
    public static class PmcLifeSupport {
        public const float RefillThreshold = 40, DepartureThreshold = 20;

        public static bool TryEquip(InventoryGraph inventory, bool clash, bool tutorial) {
            if (inventory == null || clash || tutorial || inventory.Equipped("Helmet") != null) return false;
            var suit = new InventoryItem {
                Id = System.Guid.NewGuid().ToString("D"), Code = EnergyShieldEquipment.HeadDomeCode,
                Parent = InventoryCatalog.Equipment, Region = "Helmet", FoundInRaid = true
            };
            if (inventory.Items.Count >= InventoryCatalog.MaxItems ||
                inventory.CarriedWeight + InventoryCatalog.Get(suit.Code).Weight > InventoryCatalog.CarryWeightLimit ||
                inventory.CanPlace(suit, InventoryCatalog.Equipment, "Helmet", 0, 0, false) != InventoryError.None)
                return false;
            inventory.Items.Add(suit);
            inventory.Version++;
            return true;
        }

        public static bool Low(in PredictedPlayerGhost health, float threshold) {
            return health.ProtectionInitialized && health.CurrentHealth > 0 && float.IsFinite(health.Oxygen) &&
                   health.Oxygen <= threshold && RaidOxygen.NeedsSuitAir(health);
        }

        public static void Tick(
            ref PredictedPlayerGhost health,
            InventoryGraph inventory,
            TacticalServerWorld.Status status,
            in AtmosphereSample atmosphere,
            in OxygenParameters settings,
            float dt,
            uint tick,
            float supply,
            bool moving,
            bool flush = true) {
            var before = health;
            RaidOxygen.Tick(ref health, atmosphere, settings, dt, tick, supply, health.HeadDomeEquipped, moving,
                SpecialEquipmentRules.OxygenDrainMultiplier(inventory));
            PmcStatusDamage.Record(status, before, health);
            if (flush || health.CurrentHealth <= 0) EnergyShieldEquipment.Flush(ref health, inventory);
        }

        private static bool IsOxygen(InventoryItem item) {
            return item != null && item.Quantity > 0 && (ConsumableCatalog.Get(item.Code)?.Oxygen ?? 0) > 0;
        }

        public static bool HasCarried(InventoryGraph inventory) {
            return inventory != null && inventory.Items.Any(item => IsOxygen(item) && inventory.Carried(item));
        }

        public static InventoryItem FindAccessible(InventoryGraph inventory) {
            return inventory?.Items.FirstOrDefault(item => IsOxygen(item) && inventory.ConsumableAccessible(item));
        }

        public static InventoryItem Prepare(InventoryGraph inventory) {
            var ready = FindAccessible(inventory);
            if (ready != null || inventory == null) return ready;
            string rig = inventory.Equipped("ChestRig")?.Id;
            string[] ids = inventory.Items.Where(item => IsOxygen(item) && inventory.Carried(item))
                .Select(item => item.Id).ToArray();
            foreach (string id in ids) {
                var item = inventory.Find(id);
                foreach (string parent in new[] { InventoryCatalog.Pockets, rig }) {
                    if (parent == null) continue;
                    var def = InventoryCatalog.Get(item.Code);
                    for (int orientation = 0; orientation < (def.Width == def.Height ? 1 : 2); orientation++) {
                        var probe = item.Clone();
                        if (orientation == 1) probe.Rotated = !probe.Rotated;
                        if (!inventory.FindSpace(probe, parent, out string region, out int x, out int y)) continue;
                        var command = new InventoryCommand {
                            Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                            ItemId = id, Parent = parent, Region = region, X = x, Y = y, Rotated = probe.Rotated
                        };
                        if (inventory.TryApply(command) == InventoryError.None) return inventory.Find(id);
                    }
                }
            }
            return null;
        }

        public static bool TryRefill(ref PredictedPlayerGhost health, InventoryGraph inventory, InventoryItem item) {
            return Low(health, RefillThreshold) && inventory != null && item != null &&
                   RaidOxygenConsumables.Consume(ref health, inventory, item.Id, inventory.Version) ==
                   InventoryError.None;
        }
    }

    // 一次补氧失败按库存版本记录。物资没变之前无需每帧尝试同一件拿不出的东西。
    public struct PmcOxygenFailure {
        private bool m_Failed;
        private int m_Version;
        public bool Matches(int version) { return m_Failed && m_Version == version; }

        public void Record(int version) {
            m_Failed = true;
            m_Version = version;
        }

        public void Clear() { m_Failed = false; }
    }

    // 食物和水使用真人同款的物品、计时和库存版本规则。
    // 动作进行时，饥渴和缺氧也继续。
    // 开吃后记下所选食物和库存版本。时间走完仍要确认物品有效，饥饿与缺水在动作中照常变化。
    public static class PmcNutrition {
        public const float UseThreshold = 35, DangerThreshold = 20, DepartureThreshold = 10;

        public static bool HasCarried(InventoryGraph inventory, bool food) {
            return inventory != null && inventory.Items.Any(item => {
                var definition = ConsumableCatalog.Get(item.Code);
                return item.Quantity > 0 && definition != null && definition.Oxygen <= 0 && inventory.Carried(item) &&
                       (food ? definition.Food : !definition.Food && definition.Hydration > 0);
            });
        }

        public static bool Low(in PredictedPlayerGhost health, float threshold) {
            return health.ProtectionInitialized && health.BodyHealthInitialized && health.CurrentHealth > 0 &&
                   (health.Energy <= threshold || health.Hydration <= threshold);
        }

        public static InventoryItem Find(in PredictedPlayerGhost health, InventoryGraph inventory, bool food) {
            if (inventory == null) return null;
            foreach (var item in inventory.Items) {
                var definition = ConsumableCatalog.Get(item.Code);
                if (item.Quantity <= 0 || definition == null || definition.Oxygen > 0 ||
                    !inventory.ConsumableAccessible(item) ||
                    (food ? !definition.Food : definition.Food || definition.Hydration <= 0) ||
                    !RaidNutrition.CanConsume(health, definition))
                    continue;
                return item;
            }
            return null;
        }

        public static InventoryItem Choose(in PredictedPlayerGhost health, InventoryGraph inventory) {
            if (health.Hydration <= UseThreshold) {
                var drink = Find(health, inventory, false);
                if (drink != null) return drink;
            }
            return health.Energy <= UseThreshold ? Find(health, inventory, true) : null;
        }

        public static bool NeedsDeparture(in PredictedPlayerGhost health, InventoryGraph inventory) {
            return health.ProtectionInitialized && health.BodyHealthInitialized && health.CurrentHealth > 0 &&
                   (health.Energy <= DepartureThreshold && Find(health, inventory, true) == null ||
                    health.Hydration <= DepartureThreshold && Find(health, inventory, false) == null);
        }

        public static void Advance(
            DollSingerEnemyBrain brain,
            ref PredictedPlayerGhost health,
            Vector3 position,
            bool eligible,
            double now) {
            if (brain == null || !double.IsFinite(now)) return;
            if (brain.NutritionUntil > 0) {
                bool unchanged = brain.Inventory != null && brain.Inventory.Version == brain.NutritionVersion &&
                                 RaidHealth.SelfTreatmentDamageUnchanged(health, brain.NutritionDamageSequence,
                                     brain.NutritionShieldSequence) && health.LastShotTick == brain.NutritionShot &&
                                 health.LastGrenadeShotTick == brain.NutritionGrenade &&
                                 health.LastReloadTick == brain.NutritionReload &&
                                 health.EquippedWeaponID == brain.NutritionWeapon &&
                                 (position - brain.NutritionOrigin).sqrMagnitude <=
                                 RaidInventoryState.ItemActionMovementLimit *
                                 RaidInventoryState.ItemActionMovementLimit;
                if (!eligible || !unchanged) {
                    Cancel(brain, now);
                    return;
                }
                if (now < brain.NutritionUntil) return;
                RaidNutrition.Consume(ref health, brain.Inventory, brain.NutritionItemId, brain.NutritionVersion);
                Cancel(brain, now);
                return;
            }
            if (!eligible || now < brain.NextNutrition) return;
            var item = Choose(health, brain.Inventory);
            if (item == null) {
                brain.NextNutrition = now + 1;
                return;
            }
            brain.NutritionItemId = item.Id;
            brain.NutritionVersion = brain.Inventory.Version;
            brain.NutritionOrigin = position;
            brain.NutritionDamageSequence = health.NonPhysiologicalDamageSequence;
            brain.NutritionShieldSequence = health.ShieldHitSequence;
            brain.NutritionShot = health.LastShotTick;
            brain.NutritionGrenade = health.LastGrenadeShotTick;
            brain.NutritionReload = health.LastReloadTick;
            brain.NutritionWeapon = health.EquippedWeaponID;
            brain.NutritionUntil = now + ConsumableCatalog.UseSeconds(item.Code);
            RaidConsumableAcoustics.Mark(ref health);
        }

        private static void Cancel(DollSingerEnemyBrain brain, double now) {
            brain.NutritionUntil = 0;
            brain.NutritionItemId = null;
            brain.NextNutrition = now + 1;
            brain.NextDecision = 0;
        }
    }

    // 够不着的背包电池，不会每帧都重试同一次操作。
    // 实际库存有变化或武器能耗改变后，才值得重试。
    // 装填失败的条件不只与库存有关。换武器或配件使每发能耗改变后，也有理由重新尝试。
    public struct PmcReloadFailure {
        private bool m_Failed;
        private int m_Version, m_Cost;
        private uint m_Weapon;

        public bool Matches(int version, uint weapon, int cost) {
            return m_Failed && m_Version == version && m_Weapon == weapon && m_Cost == cost;
        }

        public void Record(int version, uint weapon, int cost) {
            m_Failed = true;
            m_Version = version;
            m_Weapon = weapon;
            m_Cost = cost;
        }

        public void Clear() { m_Failed = false; }
    }

    // 电池在背包里不等于手能用到。需要先通过库存移动放到口袋或可用装备槽。
    public static class PmcReloadInventory {
        public static bool Prepare(InventoryGraph inventory, int energyPerRound) {
            string rig = inventory.Equipped("ChestRig")?.Id;
            if (inventory.CellEnergy(accessibleOnly: true) >= energyPerRound) return true;
            // 存放的电池要先搬到口袋或当前穿戴的装备中。
            foreach (var source in inventory.Items.Where(i => i.Code == "cells" && inventory.Carried(i) &&
                                                              i.Parent != InventoryCatalog.Pockets && i.Parent != rig)
                         .ToArray()) {
                var item = inventory.Find(source.Id);
                foreach (string parent in new[] { InventoryCatalog.Pockets, rig })
                    if (parent != null && inventory.FindSpace(item, parent, out string region, out int x, out int y) &&
                        inventory.TryApply(new InventoryCommand {
                            Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                            ItemId = item.Id, Parent = parent, Region = region, X = x, Y = y
                        }) == InventoryError.None) {
                        if (inventory.CellEnergy(accessibleOnly: true) >= energyPerRound) return true;
                        break;
                    }
            }
            return false;
        }
    }

    public struct PmcMedicalFailure {
        private bool m_Failed;
        private int m_Version;
        public bool Matches(int version) { return m_Failed && m_Version == version; }

        public void Record(int version) {
            m_Failed = true;
            m_Version = version;
        }

        public void Clear() { m_Failed = false; }
    }

    // 药品必须在可使用区域才能开始治疗。AI 身上有药但拿不到时也要遵守库存移动限制。
    public static class PmcMedicalInventory {
        public static InventoryItem FindAccessible(InventoryGraph inventory) {
            return inventory?.Items.FirstOrDefault(item => item.Quantity > 0 && inventory.MedicalAccessible(item));
        }

        public static bool HasCarried(InventoryGraph inventory) {
            return inventory != null && inventory.Items.Any(item =>
                item.Quantity > 0 && ConsumableCatalog.MedicalRestore(item.Code) > 0 && inventory.Carried(item));
        }

        // AI 使用和玩家相同的随身容器与带版本的物品移动规则。
        public static InventoryItem Prepare(InventoryGraph inventory) {
            var available = FindAccessible(inventory);
            if (available != null || inventory == null) return available;
            string rig = inventory.Equipped("ChestRig")?.Id;
            string[] sources = inventory.Items
                .Where(item => item.Quantity > 0 && ConsumableCatalog.MedicalRestore(item.Code) > 0 &&
                               inventory.Carried(item)).Select(item => item.Id).ToArray();
            foreach (string id in sources) {
                var item = inventory.Find(id);
                foreach (string parent in new[] { InventoryCatalog.Pockets, rig }) {
                    if (parent == null) continue;
                    var def = InventoryCatalog.Get(item.Code);
                    for (int orientation = 0; orientation < (def.Width == def.Height ? 1 : 2); orientation++) {
                        var probe = item.Clone();
                        if (orientation == 1) probe.Rotated = !probe.Rotated;
                        if (!inventory.FindSpace(probe, parent, out string region, out int x, out int y)) continue;
                        var command = new InventoryCommand {
                            Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                            ItemId = id, Parent = parent, Region = region, X = x, Y = y, Rotated = probe.Rotated
                        };
                        if (inventory.TryApply(command) == InventoryError.None) return inventory.Find(id);
                    }
                }
            }
            return null;
        }
    }

    // 优先适合当前距离且仍有可用能量的枪。切换有分开的阈值，在边界附近不反复横跳。
    public static class PmcWeaponTactics {
        internal static bool PrefersCloseRange(uint weapon) {
            return weapon is DollSingerWeapons.Shotgun or DollSingerWeapons.Thorn or DollSingerWeapons.Cicada;
        }

        // 切入与切出条件使用不同阈值，目标在边界附近时不会反复换枪。
        public static int PreferredSlot(in PredictedPlayerGhost health, float distance) {
            return SelectSlot(health, distance, true);
        }

        internal static int LoadedAlternate(in PredictedPlayerGhost health, float distance) {
            return SelectSlot(health, distance, false);
        }

        private static int SelectSlot(in PredictedPlayerGhost health, float distance, bool holdTransitionBand) {
            if (!health.InventoryWeapons || health.CurrentHealth <= 0 || RaidHealth.HandsBusy(health) ||
                !float.IsFinite(distance) || distance < 0 || (holdTransitionBand && distance > 11 && distance < 18))
                return 0;
            int best = health.CurrentAmmo > 0 ? Suitability(health.EquippedWeaponID, distance) : -1;
            int preferred = 0;
            for (int slot = 1; slot <= 3; slot++) {
                if (slot == health.EquippedWeaponSlot) continue;
                uint weapon = slot == 1 ? health.PrimaryWeaponID :
                    slot == 2 ? health.SecondaryWeaponID : health.PistolWeaponID;
                int ammo = slot == 1 ? health.PrimaryAmmo : slot == 2 ? health.SecondaryAmmo : health.PistolAmmo;
                int score = ammo > 0 ? Suitability(weapon, distance) : -1;
                if (score <= best) continue;
                best = score;
                preferred = slot;
            }
            return preferred;
        }

        private static int Suitability(uint weapon, float distance) {
            return weapon switch {
                DollSingerWeapons.Shotgun or DollSingerWeapons.Thorn => distance <= 11 ? 6 : 1,
                DollSingerWeapons.Cicada => distance <= 11 ? 5 : distance >= 50 ? 1 : 2,
                DollSingerWeapons.Halo or DollSingerWeapons.Trine or DollSingerWeapons.Aegis or DollSingerWeapons.Rondo
                    => distance <= 11 ? 3 : 4,
                DollSingerWeapons.Sniper => distance <= 11 ? 1 : distance >= 50 ? 6 : 3,
                DollSingerWeapons.Vesper => distance <= 11 ? 2 : distance >= 50 ? 5 : 4,
                DollSingerWeapons.Nocturne => distance <= 11 ? 1 : distance >= 50 ? 5 : 3,
                DollSingerWeapons.Revolver => distance <= 11 ? 2 : 1,
                _ => -1
            };
        }
    }

    // 受击但不知道攻击者在哪时，按已知出口或自身朝向找安全方向，避免隔墙猜到精确射手。
    public static class PmcUnknownAttack {
        public static bool NeedsEvasion(bool clash, bool hasContact, double lastDamage, double now) {
            return !clash && !hasContact && double.IsFinite(lastDamage) && double.IsFinite(now) && now >= lastDamage &&
                   now - lastDamage < 3;
        }

        // 撤退时往已知出口走。已经到出口时则向后退。
        // 这些方向都不包含看不见的攻击者的精确位置。
        public static float3 Direction(float3 position, float3 exit, float yawDegrees) {
            if (math.all(math.isfinite(position)) && math.all(math.isfinite(exit))) {
                var towardExit = new float2(exit.x - position.x, exit.z - position.z);
                float lengthSq = math.lengthsq(towardExit);
                if (math.all(math.isfinite(towardExit)) && math.isfinite(lengthSq) && lengthSq > 1) {
                    towardExit = math.normalize(towardExit);
                    return new float3(towardExit.x, 0, towardExit.y);
                }
            }
            float yaw = math.radians(float.IsFinite(yawDegrees) ? yawDegrees : 0);
            return new float3(-math.sin(yaw), 0, -math.cos(yaw));
        }
    }

    // AI 的世界知识只来自自己的观察记录和已接收的报告。
    // 决定时读这份状态，不直接查隐藏对手现在的位置或库存。
    // 这是某个观察者对目标的记忆。这里的时间、估计位置和置信度决定 AI 下一步能依据哪些信息。
    public sealed class PmcContact {
        public Vector3 Position, Velocity;
        public Vector3 AimOffset = Vector3.up * 1.15f;
        public float Suspicion, Confidence;
        public double LastSeen = -100, LastHeard = -100;
        public bool Visible, Confirmed, PlayerControlled;
        public Vector3 LastVisualPosition, HearingBias;
        public float PositionUncertainty = 1;
        public double HearingBiasUntil = -100;
        public bool HadVisualSample;
        public double LastReported = -100;
    }

    // 服务端为每个机器人各保留一份脑状态，包含真实库存、观察缓存、掩体阶段和寻路索引。网络只同步表现和结算所需部分。
    public sealed class DollSingerEnemyBrain : IComponentData {
        public Entity Target;
        public InventoryGraph Inventory;
        public readonly Dictionary<Entity, PmcContact> Contacts = new();
        public readonly List<Entity> ExpiredContacts = new();
        public readonly HashSet<int> VisitedCaches = new();
        public readonly NavMeshPath Path = new();
        public readonly NavMeshPath ProbePath = new();
        public readonly Vector3[] Corners = new Vector3[64];
        public readonly Vector3[] ProbeCorners = new Vector3[64];
        public int CornerCount, Corner, Seed, Cache = -1, SearchStep, LootedCaches;
        public int DrawersSearched, GroundItemsLooted;
        public Vector3 CacheApproach;
        public Vector3 LootLookPoint;
        public bool CacheApproachKnown;
        public double CacheObstructedSince = -1;
        public double NextNearbyLootScan;
        public double NextTacticalSwitch;
        public double NextShieldSupplyCheck;
        public double NextOxygenSupplyCheck, NextAtmosphereSample;
        public bool TutorialActor;
        public AtmosphereSample CachedAtmosphere;
        public float CachedOxygenSupply;
        public RaidSurvivalMotion OxygenMotion;
        public PmcOxygenFailure OxygenSupplyFailure;
        public double NutritionUntil, NextNutrition;
        public string NutritionItemId;
        public int NutritionVersion;
        public Vector3 NutritionOrigin;

        public uint NutritionDamageSequence,
            NutritionShieldSequence,
            NutritionShot,
            NutritionGrenade,
            NutritionReload,
            NutritionWeapon;

        public double NextUnknownEvasion;
        public PmcGrenadeAwareness Grenade;
        public Vector3 GrenadePlanPosition;
        public double NextGrenadePath;
        public bool HazardDanger;
        public uint HazardId, HazardPlanId;
        public Vector3 HazardPosition;
        public float HazardRadius;
        public double NextHazardPath;
        public PmcMedicalFailure MedicalFailure;
        public Vector3 Goal, LastSeen, ObservedVelocity, ProgressPosition, CoverPosition, PeekPosition, TacticalGoal;
        public Vector3 RaidExit;
        public bool LocalRaidExit, LocalExitObserved;
        public double NextLocalExitCheck;
        public Vector3 AimOffset = Vector3.up * 1.15f;

        public float Yaw,
            Pitch,
            LastHealth = 100,
            Suppression,
            Confidence,
            Aggression,
            Caution,
            AimErrorX,
            AimErrorY,
            LightGlare;

        public float ExtractionProgress, ActionScore;

        // 身份由服务器分配。机器人没有模拟客户端的传输连接。
        public int ClashParticipantId, ClashTeamId = -1, ClashEpoch, ClashSlot;
        public bool IsClashBot => ClashParticipantId > 0;
        internal bool IsExpeditionRaider => !IsClashBot && !TutorialActor;
        internal int ExpeditionSquadId, SquadSlot, SquadSize;
        internal Entity SquadLeader;
        internal Entity CompanionConnection;
        internal bool IsCompanion => CompanionConnection != Entity.Null;
        internal bool LanCompanion;
        internal bool HasSquadLeader, SquadPartnerNearby;
        internal Vector3 SquadLeaderPosition;
        internal float SquadLeaderYaw;
        public Vector3 MissionGoal, MissionLookPoint, FailedGoal;
        public double FailedGoalUntil, NextMission, NextIntent, LastRadio = -100;
        public int PathFailures, RecoveryAttempts, SelectedExit = -1, RescueTargetId = -1;
        public uint IntentSequence;
        public uint LastShieldHitSequence, LastImpactTick;
        public TacticalServerWorld.Status SelfStatus;
        public TacticalServerWorld.Status ObservedSelfStatus;
        public double LastStatusHealthDamage;
        public ulong LastStatusShieldHits;
        public bool BleedOnlyDamage, HealStatus;
        public ClashBotGoal Mission;
        public bool Interacting, MissionUrgent;
        public double NextSense, NextPath, LastSeenTime = -100, VisibleSince, NextProgress, NextDecision, CommitUntil;
        public double LastConfirmedSight = -100;
        public bool ReacquiredTarget;

        public double NextCover,
            NextAimError,
            BurstUntil,
            NextBurst,
            SearchUntil,
            LootUntil,
            SpawnTime,
            LastDamage = -100;

        internal double SidestepBurst = -1;
        internal Vector3 SidestepDirection;
        public double NextNearMiss;
        public double LastHeardTime = -100;
        public string MissionAlarmId;
        public Vector3 MissionAlarmPosition;
        public double MissionAlarmHeardAt = -100, MissionAlarmUntil;
        public double SensorBatch = -1;
        public double ActionStarted;
        public double NextFlank;
        public double LastSenseTime = -100;
        public float ContactUncertainty = 1;
        public PmcCoverPhase CoverPhase;

        public double CoverPhaseUntil,
            CoverExposureUntil,
            AvoidCoverUntil,
            SearchPauseUntil,
            SearchObservationTime = -100;

        public int CoverPeeks;
        public Vector3 CoverThreatPosition, AvoidCoverPosition, SearchAnchor, SearchHeading, SearchLookPoint;
        public bool SearchFocusValid;
        public double HealUntil, NextHeal;
        public string HealItemId;
        public int HealInventoryVersion;
        public PmcReloadFailure ReloadFailure;
        public bool MovementRequested, Visible, HasCover, Leaving, Extracted, PathFailed, IsPeeking;
        public PmcAction Action;
        public string Reason = "entering expedition";
        public Mathematics.Random Random;

        public float Uncertainty(double now) {
            return Mathf.Clamp(
                ContactUncertainty + Mathf.Max(0, (float)(now - System.Math.Max(LastSeenTime, LastHeardTime))) * 1.1f,
                1, 20);
        }
    }
}