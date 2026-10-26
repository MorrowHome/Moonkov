using Unity.Entities;
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public partial class DollSingerEnemySystem
    {
        private void EvaluateDecision(DollSingerEnemyBrain brain, Vector3 position, PredictedPlayerGhost health,
            MoonRaidMap map, double now, out PmcAction best, out float score, out string reason, out bool emergency)
        {
            bool contact = brain.Target != Entity.Null && brain.Confidence > .2f;
            bool confirmed = contact && brain.Contacts.TryGetValue(brain.Target, out var memory) && memory.Confirmed;
            bool lowHealth = RaidHealth.DangerFraction(health) < m_Tuning.RetreatHealthFraction;
            int spareAmmo = (health.EquippedWeaponSlot != 1 ? health.PrimaryAmmo : 0) +
                (health.EquippedWeaponSlot != 2 ? health.SecondaryAmmo : 0) + (health.EquippedWeaponSlot != 3 ? health.PistolAmmo : 0);
            var weapon = WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID,health.EquippedModules);
            int cost=weapon?.EnergyPerRound ?? 1;
            bool energy = weapon != null && !brain.ReloadFailure.Matches(brain.Inventory.Version,health.EquippedWeaponID,cost) &&
                brain.Inventory.CellEnergy(carriedOnly: true) >= cost;
            bool exhausted = health.CurrentAmmo == 0 && spareAmmo == 0 && !energy;
            bool underThreat = brain.Visible || now - brain.LastDamage < 3 || brain.Suppression > .45f;
            double seenAge = now - brain.LastSeenTime;
            // A treatable injury requests temporary Retreat below, not permanent raid departure.
            // Keep deliberate departure (spent supplies, exhausted caches, timeout) latched.
            bool bleeding = brain.SelfStatus != null && brain.SelfStatus.Bleed > now;
            if (!brain.IsClashBot && (exhausted || lowHealth && !bleeding && (!NeedsHealing(health) || FindMedical(brain.Inventory) == null) ||
                brain.LootedCaches >= m_Tuning.DesiredCaches || now - brain.SpawnTime >
                map.RaidDuration * (m_Tuning.DepartureFraction + (brain.Seed % 3 - 1) * .08f)))
                brain.Leaving = true;

            PmcAction choice = brain.IsClashBot ? PmcAction.ClashObjective : brain.Leaving ? PmcAction.Extract : PmcAction.Scavenge;
            float utility = brain.IsClashBot && brain.Mission == ClashBotGoal.Rescue ? 99 : brain.Leaving ? 45 : 12;
            string explanation = brain.IsClashBot ? "squad mission: " + brain.Mission : brain.Leaving ? "bank loot / leave before raid timeout" : "search supplies";
            void Consider(PmcAction action, float value, string why)
            {
                if (brain.Action == action) value += 8;
                if (value <= utility) return;
                choice = action; utility = value; explanation = why;
            }

            if (contact)
            {
                if (brain.Visible)
                {
                    Consider(PmcAction.Engage, 50 + brain.Aggression * 12, "confirmed opponent; deliberate bursts");
                    if (brain.IsClashBot && !brain.Leaving && brain.Suppression < .3f && HasClashCoveringAlly(brain, now))
                        Consider(PmcAction.Flank, 78, "partner has a firing window; take the other approach lane");
                    // Finish the chosen angle before reconsidering it; failed paths may abort it.
                    bool flanking = brain.Action == PmcAction.Flank && now - brain.ActionStarted < 6 &&
                        HorizontalDistance(position, brain.TacticalGoal) > 2 && !brain.PathFailed;
                    if (!brain.Leaving && brain.Suppression < .4f && (!brain.IsClashBot || flanking || HasClashCoveringAlly(brain, now)) && (flanking ||
                        brain.Action == PmcAction.Engage && now >= brain.NextFlank && now - brain.ActionStarted > 5))
                        Consider(PmcAction.Flank, 64 + brain.Aggression * 8, "change the firing angle after an inconclusive duel");
                }
                else if (!brain.Leaving && confirmed && seenAge < m_Tuning.MemorySeconds)
                {
                    if (seenAge < m_Tuning.HoldAngleSeconds && brain.Suppression < .3f)
                        Consider(PmcAction.HoldAngle, 57, "watch the last exit briefly before committing to a search");
                    else Consider(PmcAction.Search, 38 + brain.Confidence * 10, "search remembered motion and plausible exits");
                }
                else if (!brain.Leaving)
                    Consider(PmcAction.Investigate, 26 + brain.Confidence * 10, "investigate an uncertain sound, without a firing solution");

                bool coverCycle = brain.Action == PmcAction.Cover && brain.HasCover &&
                    brain.CoverPhase != PmcCoverPhase.Settling && !brain.PathFailed;
                if (brain.Suppression > .3f || now - brain.LastDamage < 3 || brain.HasCover && seenAge < 4 || coverCycle)
                    Consider(PmcAction.Cover, coverCycle ? 90 : 55 + brain.Suppression * 30 + brain.Caution * 8,
                        "finish reaching cover or returning from a short peek");
            }
            else if (!brain.IsClashBot && !brain.Leaving && now < brain.MissionAlarmUntil)
                Consider(PmcAction.Investigate, 35, "investigate the operating sampler");

            // Withdrawal stops pursuing stale contacts. A wounded raider can actually leave
            // once sight is broken, instead of being stuck in Retreat until all memory expires.
            if ((lowHealth || exhausted || brain.Leaving && (!brain.IsClashBot || brain.Mission != ClashBotGoal.Escort)) && underThreat)
                Consider(PmcAction.Retreat, 110, "break contact to survive and extract");
            if (health.ControllerState.IsReloadingState || energy && ShouldReload(brain, health, position, now))
                Consider(PmcAction.Reload, health.CurrentAmmo == 0 ? 98 : 76, "replenish the magazine during a safe opening");
            if (NeedsHealing(health) && FindMedical(brain.Inventory) != null && now >= brain.NextHeal &&
                !health.ControllerState.IsReloadingState && SafeToTreat(brain, position, now))
                Consider(PmcAction.Heal, lowHealth ? 108 : 84, "treat an injured body part with carried medicine");
            if (bleeding && now >= brain.NextHeal && !health.ControllerState.IsReloadingState && SafeToTreat(brain, position, now))
                Consider(PmcAction.Heal, 110, "stop bleeding during a safe opening");

            // New confirmed danger must interrupt looting/search, regardless of their commitment.
            bool passive = brain.Action == PmcAction.Scavenge || brain.Action == PmcAction.Extract ||
                brain.Action == PmcAction.Search || brain.Action == PmcAction.Investigate || brain.Action == PmcAction.Heal || brain.Action == PmcAction.ClashObjective;
            emergency = health.CurrentAmmo == 0 || brain.Suppression > .7f || now - brain.LastDamage < 1 ||
                brain.Visible && passive || brain.Action == PmcAction.Heal && !SafeToTreat(brain, position, now) ||
                brain.Action == PmcAction.HoldAngle && seenAge >= m_Tuning.HoldAngleSeconds;
            best = choice; score = utility; reason = explanation;
        }

        private bool ShouldReload(DollSingerEnemyBrain brain, PredictedPlayerGhost health, Vector3 position, double now)
        {
            if (brain.Action == PmcAction.Heal || health.ControllerState.IsReloadingState) return false;
            if (health.CurrentAmmo <= 0) return true;
            var weapon = WeaponManager.Instance?.WeaponRegistry?.GetWeaponData(health.EquippedWeaponID,health.EquippedModules);
            return weapon != null && health.CurrentAmmo < weapon.MagazineSize &&
                health.CurrentAmmo <= Mathf.Max(1, Mathf.FloorToInt(weapon.MagazineSize * m_Tuning.TacticalReloadFraction)) &&
                SafeToTreat(brain, position, now);
        }

        private static bool NeedsHealing(PredictedPlayerGhost health) => health.CurrentHealth > 0 &&
            health.BodyHealthInitialized && health.TacticalPoisonSeconds <= 0 && SelectTreatment(health) != BodyPart.Auto &&
            (RaidHealth.DangerFraction(health) < .8f ||
            health.CurrentHealth < health.MaxHealth - 25 || health.LeftLegHealth <= 0 || health.RightLegHealth <= 0 ||
            health.LeftArmHealth <= 0 || health.RightArmHealth <= 0);

        private static BodyPart SelectTreatment(PredictedPlayerGhost health)
        {
            if (RaidHealth.CanHeal(health, BodyPart.Head) && health.HeadHealth < RaidHealth.Maximum(health, BodyPart.Head) * .65f) return BodyPart.Head;
            if (RaidHealth.CanHeal(health, BodyPart.Chest) && health.ChestHealth < RaidHealth.Maximum(health, BodyPart.Chest) * .65f) return BodyPart.Chest;
            var selected = BodyPart.Auto;float fraction = float.MaxValue;
            for (int i = 0; i < RaidHealth.PartCount; i++)
            {
                var part = (BodyPart)i;
                if (!RaidHealth.CanHeal(health, part)) continue;
                float value = RaidHealth.Get(health, part) / RaidHealth.Maximum(health, part);
                if (value >= fraction) continue;
                fraction = value;selected = part;
            }
            return selected;
        }

        private static InventoryItem FindMedical(InventoryGraph inventory)
        {
            if (inventory == null) return null;
            foreach (var item in inventory.Items)
                if (item.Quantity > 0 && inventory.MedicalAccessible(item)) return item;
            return null;
        }

        private bool SafeToTreat(DollSingerEnemyBrain brain, Vector3 position, double now)
        {
            if (brain.Visible || brain.Suppression >= .3f || now - brain.LastDamage < m_Tuning.HealSafetySeconds) return false;
            if (brain.Target == Entity.Null || brain.Confidence <= .2f) return true;
            double observationAge = now - System.Math.Max(brain.LastSeenTime, brain.LastHeardTime);
            if (observationAge < 2 || HorizontalDistance(position, brain.LastSeen) < 8 && observationAge < 6) return false;
            return observationAge > 6 || WorldBlocked(position + Vector3.up * 1.15f, brain.LastSeen + brain.AimOffset);
        }

        private void AdvanceHealing(DollSingerEnemyBrain brain, ref PredictedPlayerGhost health, Vector3 position, double now)
        {
            bool bleeding = brain.SelfStatus != null && brain.SelfStatus.Bleed > now;
            if (brain.Action != PmcAction.Heal || !SafeToTreat(brain, position, now) ||
                health.ControllerState.IsReloadingState || HorizontalDistance(position, brain.TacticalGoal) > TacticalArrivalDistance ||
                !bleeding && !NeedsHealing(health))
            {
                if (brain.HealUntil > 0) brain.NextHeal = now + .75;
                brain.HealUntil = 0;
                brain.HealItemId = null;
                brain.HealBleeding = false;
                return;
            }
            if (brain.HealUntil <= 0)
            {
                if (now < brain.NextHeal) return;
                if (bleeding)
                {
                    brain.HealBleeding = true; brain.HealItemId = null;
                    brain.HealUntil = now + TacticalItemRules.StopBleedSeconds;
                    brain.CommitUntil = brain.HealUntil;
                    return;
                }
                var medical = FindMedical(brain.Inventory);
                if (medical == null) { brain.NextDecision = 0; return; }
                brain.HealBleeding = false;
                brain.HealItemId = medical.Id;
                brain.HealInventoryVersion = brain.Inventory.Version;
                brain.HealUntil = now + m_Tuning.HealSeconds;
                brain.CommitUntil = brain.HealUntil;
                return;
            }
            if (brain.HealBleeding)
            {
                if (bleeding && now < brain.HealUntil) return;
                if (bleeding) brain.SelfStatus.Bleed = 0;
                brain.HealUntil = 0; brain.HealBleeding = false;
                brain.NextHeal = now + .75; brain.CommitUntil = brain.NextDecision = 0;
                return;
            }
            if (brain.Inventory.Version != brain.HealInventoryVersion)
            {
                brain.HealUntil = 0;
                brain.HealItemId = null;
                brain.NextHeal = now + 1;
                brain.NextDecision = 0;
                return;
            }
            if (now < brain.HealUntil) return;
            // The shared server transaction checks body health, accessibility and inventory
            // version. Nothing is consumed before the treatment timer finishes.
            BodyPart treatment = SelectTreatment(health);
            RaidHealth.UseMedical(ref health, brain.Inventory, brain.HealItemId, brain.HealInventoryVersion, treatment);
            brain.LastHealth = health.CurrentHealth;
            brain.HealUntil = 0;
            brain.HealItemId = null;
            brain.NextHeal = now + 1;
            brain.CommitUntil = brain.NextDecision = 0;
        }
    }
}
