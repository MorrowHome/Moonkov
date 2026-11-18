// 给人形 AI 的候选行动打分。当前线索、弹药、身体状态和补给决定是交火、换位、装填、治疗、搜刮还是撤离。
// 排好优先级之后由 Tactics 计算具体走法，危险逼近时可以打断手上的事。
using Unity.Entities;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public partial class DollSingerEnemySystem {
        private void EvaluateDecision(
            DollSingerEnemyBrain brain,
            Vector3 position,
            PredictedPlayerGhost health,
            MoonRaidMap map,
            double now,
            out PmcAction best,
            out float score,
            out string reason,
            out bool emergency) {
            bool contact = brain.Target != Entity.Null && brain.Confidence > .2f;
            // 低置信度的声响能触发调查。曾确认过的目标才进入守角或搜索，真正可见才准备交火。
            bool confirmed = contact && brain.Contacts.TryGetValue(brain.Target, out var memory) && memory.Confirmed;
            bool lowHealth = RaidHealth.DangerFraction(health) < m_Tuning.RetreatHealthFraction;
            int spareAmmo = (health.EquippedWeaponSlot != 1 ? health.PrimaryAmmo : 0) +
                            (health.EquippedWeaponSlot != 2 ? health.SecondaryAmmo : 0) +
                            (health.EquippedWeaponSlot != 3 ? health.PistolAmmo : 0);
            var weapon =
                WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID, health.EquippedModules);
            int cost = weapon?.EnergyPerRound ?? 1;
            // 决定装填前确认有能访问的电量，并且没有在同一库存版本下失败过。
            bool energy = weapon != null &&
                          !brain.ReloadFailure.Matches(brain.Inventory.Version, health.EquippedWeaponID, cost) &&
                          brain.Inventory.CellEnergy(true) >= (health.HaloEnergyInitialized ? 1 : cost);
            bool exhausted = !health.HaloEnergyInitialized && health.CurrentAmmo == 0 && spareAmmo == 0 && !energy;
            bool energyLow = health.HaloEnergyInitialized && health.HaloEnergy < cost;
            bool underThreat = brain.Visible || now - brain.LastDamage < 3 || brain.Suppression > .45f;
            double seenAge = now - brain.LastSeenTime;
            float holdAngleSeconds = m_Tuning.HoldAngleSeconds;
            if (brain.IsExpeditionRaider && confirmed)
                holdAngleSeconds = Mathf.Min(holdAngleSeconds,
                    (position - brain.LastSeen).sqrMagnitude <= 24 * 24 ? .75f : 1.5f);
            bool needsHealing = NeedsHealing(health);
            var medical = needsHealing ? FindMedical(brain.Inventory) : null;
            bool grenadeDanger = !brain.IsClashBot && brain.Grenade.Active(now);
            bool hazardDanger = !brain.IsClashBot && brain.HazardDanger;
            bool handsBusy = brain.IsClashBot ? health.ControllerState.IsReloadingState : RaidHealth.HandsBusy(health);
            bool canPrepareMedical = !grenadeDanger && !hazardDanger && needsHealing && now >= brain.NextHeal &&
                                     !handsBusy && SafeToTreat(brain, position, now);
            // 附近安全且手空时，可先把带来的药移动到能使用的位置。一次没成功便记录当前版本等库存变化。
            if (!brain.IsClashBot && canPrepareMedical && medical == null &&
                !brain.MedicalFailure.Matches(brain.Inventory.Version)) {
                medical = PmcMedicalInventory.Prepare(brain.Inventory);
                if (medical == null)
                    brain.MedicalFailure.Record(brain.Inventory.Version);
                else
                    brain.MedicalFailure.Clear();
            }
            bool potentialMedical =
                medical != null || (!canPrepareMedical && PmcMedicalInventory.HasCarried(brain.Inventory));
            // 还能治疗的伤势只触发下面的临时 Retreat，不马上结束远征。
            // 若因为耗尽补给、箱子或时间主动离场，就保留这个决定。
            bool bleeding = brain.SelfStatus != null && brain.SelfStatus.Bleed > now;
            bool burning = !brain.IsClashBot && brain.SelfStatus != null && brain.SelfStatus.Fire > now;
            // 资源少不一定马上撤。先判断是否带了真能用的补给，可治疗的受伤也可临时后退而非结束一局。
            bool lowOxygen = !brain.IsClashBot && PmcLifeSupport.Low(health, PmcLifeSupport.DepartureThreshold);
            bool canRefillOxygen = lowOxygen && health.HeadDomeEquipped &&
                                   !brain.OxygenSupplyFailure.Matches(brain.Inventory.Version) &&
                                   PmcLifeSupport.HasCarried(brain.Inventory);
            bool lowNutrition = !brain.IsClashBot && PmcNutrition.Low(health, PmcNutrition.DangerThreshold);
            bool nutritionDeparture = lowNutrition && PmcNutrition.NeedsDeparture(health, brain.Inventory);
            if (!brain.IsClashBot && !brain.IsCompanion && (exhausted || (lowOxygen && !canRefillOxygen) ||
                                                            nutritionDeparture ||
                                                            (lowHealth && !bleeding && !burning &&
                                                             (!needsHealing || !potentialMedical)) ||
                                                            brain.LootedCaches >= m_Tuning.DesiredCaches ||
                                                            now - brain.SpawnTime > map.RaidDuration *
                                                            (m_Tuning.DepartureFraction + (brain.Seed % 3 - 1) * .08f)))
                brain.Leaving = true;
            // 没有更急的事时，先分配该做的小队或个人任务。队友离队太远会给归队更高分。
            bool follow = (brain.HasSquadLeader || brain.IsCompanion) && !brain.Leaving;
            var choice = brain.IsClashBot ? PmcAction.ClashObjective :
                brain.Leaving ? PmcAction.Extract :
                follow ? PmcAction.FollowSquad : PmcAction.Scavenge;
            float utility = brain.IsClashBot && brain.Mission == ClashBotGoal.Rescue ? 99 : brain.Leaving ? 45 : 12;
            string explanation = brain.IsClashBot ? "squad mission: " + brain.Mission :
                brain.Leaving ? "bank loot / leave before raid timeout" :
                follow ? "advance with squad and watch the other approach" : "search supplies";
            if (brain.HasSquadLeader && follow && !brain.Visible &&
                (position - brain.SquadLeaderPosition).sqrMagnitude > 22 * 22)
                utility = 62;

            // 当前行动小幅加分，防止两个分数接近时来回切。分数高出的紧急行为仍能胜出。
            void Consider(PmcAction action, float value, string why) {
                if (brain.Action == action) value += 8;
                if (value <= utility) return;
                choice = action;
                utility = value;
                explanation = why;
            }

            if (contact) {
                if (brain.Visible) {
                    // 有机会射击时先交火。换角度利用射击间歇或此前已有的合法路线，不同时放掉火力。
                    bool betweenBursts = now >= brain.BurstUntil && now < brain.NextBurst;
                    bool flanking = brain.Action == PmcAction.Flank && now - brain.ActionStarted < 6 &&
                                    HorizontalDistance(position, brain.TacticalGoal) > 2 && !brain.PathFailed;
                    bool coveringAlly = brain.IsClashBot
                        ? HasClashCoveringAlly(brain, now)
                        : now >= brain.NextFlank && TryGetExpeditionCoveringAlly(brain, position, now, out _);
                    bool calm = !brain.Leaving && brain.Suppression < .3f && now - brain.LastDamage >= 3;
                    bool readyToFight = health.CurrentAmmo > 0 && !health.ControllerState.IsReloadingState &&
                                        !energyLow && brain.Suppression <= .7f;
                    // 先争取打出一轮，再利用后续连发间歇换位，或走完已经开始的路线。
                    // 刚被击中时重新提高反击优先级。
                    float flankDelay = brain.IsExpeditionRaider ? Mathf.Lerp(2.5f, 1.65f, brain.Aggression) : 8;
                    bool enoughFireTime = !brain.IsExpeditionRaider || (readyToFight && now - brain.VisibleSince >= 1);
                    bool squadFlank = calm && coveringAlly && betweenBursts && enoughFireTime;
                    bool flankOpening = calm &&
                                        ((!brain.IsClashBot && !brain.SquadPartnerNearby) || flanking ||
                                         coveringAlly) && (flanking || (enoughFireTime && betweenBursts &&
                                                                        brain.Action == PmcAction.Engage &&
                                                                        now >= brain.NextFlank &&
                                                                        now - brain.ActionStarted > flankDelay));
                    // 还持枪且看见敌人时，优先与其交火。轻微压制或例行绕侧不必抢在前面。
                    // 严重压制仍应优先找掩体。
                    Consider(PmcAction.Engage,
                        (readyToFight && !squadFlank && !flankOpening ? 82 : 50) + brain.Aggression * 12,
                        "confirmed opponent; prioritize deliberate return fire");
                    if (squadFlank)
                        Consider(PmcAction.Flank, 78, "partner has a firing window; take the other approach lane");
                    // 先把选定的新角度走完，再想需不需要换。路径失败可以提前中止。
                    if (flankOpening)
                        Consider(PmcAction.Flank, 64 + brain.Aggression * 8,
                            "change the firing angle after an inconclusive duel");
                // 丢失对方几秒时可先看住最后一个出口，再去搜索。线索不可靠时只调查声音周围。
                } else if (!brain.Leaving && confirmed && seenAge < ContactMemorySeconds(brain)) {
                    if (seenAge < holdAngleSeconds && brain.Suppression < .3f)
                        Consider(PmcAction.HoldAngle, 57, "watch the last exit briefly before committing to a search");
                    else
                        Consider(PmcAction.Search, 38 + brain.Confidence * 10,
                            "search remembered motion and plausible exits");
                } else if (!brain.Leaving) {
                    Consider(PmcAction.Investigate, 26 + brain.Confidence * 10,
                        "investigate an uncertain sound, without a firing solution");
                }
                bool coverCycle = brain.Action == PmcAction.Cover && brain.HasCover &&
                                  brain.CoverPhase != PmcCoverPhase.Settling && !brain.PathFailed;
                if (brain.Suppression > .3f || now - brain.LastDamage < 3 || (brain.HasCover && seenAge < 4) ||
                    coverCycle)
                    Consider(PmcAction.Cover, coverCycle ? 90 : 55 + brain.Suppression * 30 + brain.Caution * 8,
                        "finish reaching cover or returning from a short peek");
            } else if (!brain.IsClashBot && !brain.Leaving && now < brain.MissionAlarmUntil) {
                Consider(PmcAction.Investigate, 35, "investigate the sound source");
            }

            // 决定后退时不继续追旧线索。受伤者脱离视线后能实际离场，
            // 无需维持 Retreat 直到对该敌人的全部记忆都过期。
            if (PmcUnknownAttack.NeedsEvasion(brain.IsClashBot, contact, brain.LastDamage, now))
                Consider(PmcAction.Retreat, 105, "move to safety after an unseen attack");
            if ((lowHealth || exhausted || energyLow || lowOxygen || lowNutrition ||
                 (brain.Leaving && (!brain.IsClashBot || brain.Mission != ClashBotGoal.Escort))) && underThreat)
                Consider(PmcAction.Retreat, 110,
                    (lowOxygen || lowNutrition) && !brain.Leaving
                        ? "break contact to replenish survival supplies"
                        : "break contact to survive and extract");
            if (health.ControllerState.IsReloadingState || (energy && ShouldReload(brain, health, position, now)))
                Consider(PmcAction.Reload, health.CurrentAmmo == 0 ? 98 : 76,
                    "replenish the magazine during a safe opening");
            if (canPrepareMedical && medical != null)
                Consider(PmcAction.Heal, lowHealth ? 108 : 84, "treat an injured body part with carried medicine");
            if ((bleeding || burning) && !hazardDanger && now >= brain.NextHeal && !handsBusy &&
                SafeToTreat(brain, position, now))
                Consider(PmcAction.Heal, 110, "stop burning or bleeding during a safe opening");
            // 手雷和持续危险区令其它行动让位。更换方案的判断还会把这些当成可以抢占的紧急条件。
            if (hazardDanger) Consider(PmcAction.EvadeHazard, 190, "leave the harmful area");
            if (grenadeDanger) Consider(PmcAction.EvadeGrenade, 200, "move away from a visible explosive");

            // 刚确认的危险可以立即打断搜刮或搜索，即使还有没走完的承诺时间。
            bool passive = brain.Action == PmcAction.Scavenge || brain.Action == PmcAction.Extract ||
                           brain.Action == PmcAction.Search || brain.Action == PmcAction.Investigate ||
                           brain.Action == PmcAction.Heal || brain.Action == PmcAction.ClashObjective ||
                           brain.Action == PmcAction.FollowSquad;
            emergency = grenadeDanger || hazardDanger || (brain.Action == PmcAction.EvadeGrenade && !grenadeDanger) ||
                        (brain.Action == PmcAction.EvadeHazard && !hazardDanger) || health.CurrentAmmo == 0 ||
                        brain.Suppression > .7f || now - brain.LastDamage < 1 || (brain.Visible && passive) ||
                        (brain.Action == PmcAction.Heal && !SafeToTreat(brain, position, now)) ||
                        (brain.Action == PmcAction.HoldAngle && seenAge >= holdAngleSeconds);
            best = choice;
            score = utility;
            reason = explanation;
        }

        private bool ShouldReload(
            DollSingerEnemyBrain brain,
            PredictedPlayerGhost health,
            Vector3 position,
            double now) {
            if (brain.Action == PmcAction.Heal || brain.Action == PmcAction.EvadeGrenade ||
                brain.Action == PmcAction.EvadeHazard || health.ControllerState.IsReloadingState)
                return false;
            if (health.HaloEnergyInitialized)
                return health.HaloEnergy <= PmcShieldSupply.RechargeThreshold && SafeToTreat(brain, position, now);
            if (health.CurrentAmmo <= 0) return true;
            var weapon =
                WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID, health.EquippedModules);
            return weapon != null && health.CurrentAmmo < weapon.MagazineSize &&
                   health.CurrentAmmo <= Mathf.Max(1,
                       Mathf.FloorToInt(weapon.MagazineSize * m_Tuning.TacticalReloadFraction)) &&
                   SafeToTreat(brain, position, now);
        }

        private static bool NeedsHealing(PredictedPlayerGhost health) {
            return health.CurrentHealth > 0 && health.BodyHealthInitialized && health.TacticalPoisonSeconds <= 0 &&
                   SelectTreatment(health) != BodyPart.Auto && (RaidHealth.DangerFraction(health) < .8f ||
                                                                health.CurrentHealth < health.MaxHealth - 25 ||
                                                                health.LeftLegHealth <= 0 ||
                                                                health.RightLegHealth <= 0 ||
                                                                health.LeftArmHealth <= 0 ||
                                                                health.RightArmHealth <= 0);
        }

        private static BodyPart SelectTreatment(PredictedPlayerGhost health) {
            if (RaidHealth.CanHeal(health, BodyPart.Head) &&
                health.HeadHealth < RaidHealth.Maximum(health, BodyPart.Head) * .65f)
                return BodyPart.Head;
            if (RaidHealth.CanHeal(health, BodyPart.Chest) &&
                health.ChestHealth < RaidHealth.Maximum(health, BodyPart.Chest) * .65f)
                return BodyPart.Chest;
            var selected = BodyPart.Auto;
            float fraction = float.MaxValue;
            for (int i = 0; i < RaidHealth.PartCount; i++) {
                var part = (BodyPart)i;
                if (!RaidHealth.CanHeal(health, part)) continue;
                float value = RaidHealth.Get(health, part) / RaidHealth.Maximum(health, part);
                if (value >= fraction) continue;
                fraction = value;
                selected = part;
            }
            return selected;
        }

        private static InventoryItem FindMedical(InventoryGraph inventory) {
            return PmcMedicalInventory.FindAccessible(inventory);
        }

        // 目标可见、刚受到伤害或压制太强都不能原地用药。仍有接触记忆时还要查对方射线是否可能打过来。
        private bool SafeToTreat(DollSingerEnemyBrain brain, Vector3 position, double now) {
            if (brain.Visible || brain.Suppression >= .3f || now - brain.LastDamage < m_Tuning.HealSafetySeconds)
                return false;
            if (brain.Target == Entity.Null || brain.Confidence <= .2f) return true;
            double observationAge = now - System.Math.Max(brain.LastSeenTime, brain.LastHeardTime);
            if (observationAge < 2 || (HorizontalDistance(position, brain.LastSeen) < 8 && observationAge < 6))
                return false;
            return observationAge > 6 || WorldBlocked(position + Vector3.up * 1.15f, brain.LastSeen + brain.AimOffset);
        }

        private void AdvanceHealing(
            DollSingerEnemyBrain brain,
            ref PredictedPlayerGhost health,
            Vector3 position,
            double now) {
            bool bleeding = brain.SelfStatus != null && brain.SelfStatus.Bleed > now;
            bool burning = !brain.IsClashBot && brain.SelfStatus != null && brain.SelfStatus.Fire > now;
            bool eligible = brain.Action == PmcAction.Heal && (!brain.HazardDanger || brain.IsClashBot) &&
                            SafeToTreat(brain, position, now) &&
                            !(brain.IsClashBot
                                ? health.ControllerState.IsReloadingState
                                : RaidHealth.HandsBusy(health)) &&
                            HorizontalDistance(position, brain.TacticalGoal) <= TacticalArrivalDistance &&
                            (bleeding || burning || NeedsHealing(health));
            double previous = brain.HealUntil;
            if (PmcSelfTreatment.Advance(brain, eligible, bleeding, burning, now)) {
                if (previous <= 0 && brain.HealUntil > 0) RaidConsumableAcoustics.Mark(ref health);
                return;
            }
            // 启动治疗时选部位、记录药品和库存版本，并设置完成时间。按一次动作持续走完。
            if (brain.HealUntil <= 0) {
                if (now < brain.NextHeal) return;
                var medical = FindMedical(brain.Inventory);
                if (medical == null) {
                    brain.NextDecision = 0;
                    return;
                }
                brain.HealStatus = false;
                brain.HealItemId = medical.Id;
                brain.HealInventoryVersion = brain.Inventory.Version;
                brain.HealUntil = now + ConsumableCatalog.MedicalSeconds(medical.Code, m_Tuning.HealSeconds);
                brain.CommitUntil = brain.HealUntil;
                RaidConsumableAcoustics.Mark(ref health);
                return;
            }
            // 等待中物资换过就重新确认，避免用已经被挪走或消耗的药结算。
            if (brain.Inventory.Version != brain.HealInventoryVersion) {
                brain.HealUntil = 0;
                brain.HealItemId = null;
                brain.NextHeal = now + 1;
                brain.NextDecision = 0;
                return;
            }
            if (now < brain.HealUntil) return;
            // 共用的服务端事务检查身体状态、物品能否访问和当前库存版本。
            // 治疗计时结束才消费物品。
            var treatment = SelectTreatment(health);
            RaidHealth.UseMedical(ref health, brain.Inventory, brain.HealItemId, brain.HealInventoryVersion, treatment);
            brain.LastHealth = health.CurrentHealth;
            brain.HealUntil = 0;
            brain.HealItemId = null;
            brain.NextHeal = now + 1;
            brain.CommitUntil = brain.NextDecision = 0;
        }
    }
}