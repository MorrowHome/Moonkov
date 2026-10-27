using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum PmcAction { Scavenge, Extract, Investigate, Search, Engage, Cover, Flank, Retreat, Reload, HoldAngle, Heal, ClashObjective, EvadeGrenade, EvadeHazard }
    public enum PmcCoverPhase { Approaching, Settling, Advancing, Holding, Returning }

    // A sound identifies a place, not an opponent or a firing solution.
    public static class PmcSoundClue
    {
        public static bool Hear(DollSingerEnemyBrain brain, string id, Vector3 position, double now)
        {
            if (brain == null || string.IsNullOrEmpty(id) || brain.IsClashBot || brain.Leaving ||
                brain.Target != Entity.Null || brain.MissionAlarmId == id) return false;
            brain.MissionAlarmId = id;
            brain.MissionAlarmPosition = position;
            brain.MissionAlarmHeardAt = now;
            brain.MissionAlarmUntil = now + 30;
            brain.NextDecision = 0;
            if (brain.Action == PmcAction.Scavenge) brain.CommitUntil = 0;
            return true;
        }
    }

    // The observer only remembers positions it could actually see. Turning away
    // does not erase the clue before detonation or track it through walls.
    public struct PmcGrenadeAwareness
    {
        private ulong m_ObservedId, m_DangerId;
        private double m_ObservedSince, m_LastObservation, m_RecognizedAt;
        public Vector3 Position;
        public float Radius;
        public double Until;
        public bool Active(double now) => double.IsFinite(now) && now >= m_RecognizedAt && now < Until;
        public void Miss() { m_ObservedId = 0; }
        public bool Observe(ulong id, Vector3 position, float radius, double fuseUntil, double now)
        {
            if (id == 0 || !PmcGrenadeRules.Finite(position) || !float.IsFinite(radius) || radius <= 0 ||
                !double.IsFinite(fuseUntil) || !double.IsFinite(now) || fuseUntil <= now)
            { Miss(); return false; }
            if (m_ObservedId != id || now < m_LastObservation || now - m_LastObservation > .25)
            { m_ObservedId = id; m_ObservedSince = m_LastObservation = now; return false; }
            m_LastObservation = now;
            if (now - m_ObservedSince < .25) return false;
            bool changed = !Active(now) || m_DangerId != id || (position - Position).sqrMagnitude > 1;
            m_DangerId = id; m_RecognizedAt = now; Position = position; Radius = radius;
            Until = System.Math.Min(fuseUntil, now + 4);
            return changed;
        }
    }
    public static class PmcGrenadeRules
    {
        public static bool Finite(Vector3 point) => float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        public static ulong EnergyId(uint id) => id == 0 ? 0 : (1UL << 32) | id;
        public static bool Candidate(bool clash, TacticalItemKind kind, TacticalWorldPhase phase,
            float distance, float radius, float angle, float cone) => phase == TacticalWorldPhase.Flying &&
            (kind == TacticalItemKind.Frag || kind == TacticalItemKind.Dynamite) &&
            VisibleDanger(clash, distance, radius, angle, cone);
        public static bool VisibleDanger(bool clash, float distance, float radius, float angle, float cone) => !clash &&
            float.IsFinite(distance) && distance >= 0 && float.IsFinite(radius) && radius > 0 &&
            distance <= radius + 2 && float.IsFinite(angle) && float.IsFinite(cone) && angle >= 0 && angle <= cone;

        // Reject a route that first approaches the last observed grenade. This
        // also prevents a failed escape from reversing into the blast.
        public static bool SegmentSafe(Vector3 from, Vector3 to, Vector3 grenade, float minimum)
        {
            if (!Finite(from) || !Finite(to) || !Finite(grenade) || !float.IsFinite(minimum) || minimum < 0) return false;
            double ax = from.x, az = from.z, dx = (double)to.x - ax, dz = (double)to.z - az;
            double length = dx * dx + dz * dz;
            double t = length > 0 ? System.Math.Max(0, System.Math.Min(1, (((double)grenade.x - ax) * dx + ((double)grenade.z - az) * dz) / length)) : 0;
            double x = ax + t * dx - grenade.x, z = az + t * dz - grenade.z;
            return x * x + z * z >= (double)minimum * minimum;
        }
    }

    public static class PmcSprintRules
    {
        // An exhausted raider walks to recover, but may still attempt to flee an explosive.
        public static bool Allows(bool clash, PmcAction action, bool exhausted) =>
            clash || !exhausted || action == PmcAction.EvadeGrenade || action == PmcAction.EvadeHazard;

        public static void Tick(ref TacticalSprintState stamina, ref float3 previous, ref bool known,
            float3 position, float dt, bool alive, bool sprinting, float speed, float weight, bool stimulated)
        {
            bool valid = math.all(math.isfinite(position));
            float3 travel = valid && known ? position - previous : float3.zero;
            float threshold = math.max(.005f, float.IsFinite(dt) ? dt * .15f : 0);
            float maximum = math.max(2, float.IsFinite(dt) ? dt * 12 : 0);
            float distance = travel.x * travel.x + travel.z * travel.z;
            bool moved = valid && known && distance > threshold * threshold && distance <= maximum * maximum;
            if (valid) previous = position;
            known = alive && valid && float.IsFinite(dt) && dt > 0;
            TacticalSprintRules.Tick(ref stamina, dt, alive && sprinting && speed > .1f && moved, stimulated, alive, weight);
        }
    }

    public static class PmcHazardRules
    {
        public static bool Active(bool clash, TacticalServerWorld.Status status, TacticalWorldPhase phase, double areaUntil,
            double now, bool inside)
        {
            if (clash || status == null || !inside || !double.IsFinite(now) || !double.IsFinite(areaUntil) || areaUntil <= now)
                return false;
            double until = phase == TacticalWorldPhase.Fire ? status.Fire : phase == TacticalWorldPhase.Wire ? status.Wire :
                phase == TacticalWorldPhase.Poison && status.Antidote <= now ? status.Poison : 0;
            return double.IsFinite(until) && until > now;
        }
    }

    // Returns true when the shared status action consumed this update. The
    // caller supplies its real safety/position check and otherwise handles medicine.
    public static class PmcSelfTreatment
    {
        public static bool Advance(DollSingerEnemyBrain brain, bool eligible, bool bleeding, bool burning, double now)
        {
            if (!eligible)
            {
                if (brain.HealUntil > 0) brain.NextHeal = now + .75;
                brain.HealUntil = 0; brain.HealItemId = null; brain.HealStatus = false;
                return true;
            }
            if (brain.HealUntil > 0 && !brain.HealStatus && (bleeding || burning))
            {
                brain.HealUntil = 0; brain.HealItemId = null;
                brain.NextHeal = now + .75; brain.CommitUntil = brain.NextDecision = 0;
                return true;
            }
            if (brain.HealUntil <= 0 && (bleeding || burning))
            {
                if (now < brain.NextHeal) return true;
                brain.HealStatus = true; brain.HealItemId = null;
                brain.HealUntil = now + TacticalItemRules.StopBleedSeconds;
                brain.CommitUntil = brain.HealUntil;
                return true;
            }
            if (!brain.HealStatus) return false;
            if ((bleeding || burning) && now < brain.HealUntil) return true;
            if (bleeding) brain.SelfStatus.Bleed = 0;
            if (burning) brain.SelfStatus.Fire = 0;
            brain.HealUntil = 0; brain.HealStatus = false;
            brain.NextHeal = now + .75; brain.CommitUntil = brain.NextDecision = 0;
            return true;
        }
    }

    // A minority of ordinary raid actors carry the same physical shield item as
    // players. Tutorial and bounty encounters retain their existing loadouts.
    public static class PmcProtectionLoadout
    {
        public const float ShieldChance = 1f / 3f;
        public static bool ShouldEquip(bool clash, bool tutorial, float roll) =>
            !clash && !tutorial && float.IsFinite(roll) && roll >= 0 && roll < ShieldChance;

        public static bool TryEquip(InventoryGraph inventory, bool clash, bool tutorial, float roll)
        {
            if (!ShouldEquip(clash, tutorial, roll) || inventory == null) return false;
            var shield = new InventoryItem { Id = System.Guid.NewGuid().ToString("D"),
                Code = EnergyShieldEquipment.ShieldCode, Parent = InventoryCatalog.Equipment,
                Region = "Shield", FoundInRaid = true };
            if (!float.IsFinite(inventory.CarriedWeight) ||
                inventory.CarriedWeight + InventoryCatalog.Get(shield.Code).Weight > InventoryCatalog.CarryWeightLimit) return false;
            if (inventory.CanPlace(shield, InventoryCatalog.Equipment, "Shield", 0, 0, false) != InventoryError.None)
                return false;
            inventory.Items.Add(shield);
            inventory.Version++;
            return true;
        }

        public static void Initialize(ref PredictedPlayerGhost health, InventoryGraph inventory)
        {
            // Ordinary PMCs do not simulate oxygen. Preserve their existing reserve
            // when the shared equipment binding sees no life-support collar.
            float oxygen = health.Oxygen;
            EnergyShieldEquipment.Synchronize(ref health, inventory);
            if (!health.HeadDomeEquipped) health.Oxygen = oxygen;
        }

        public static bool CanUseLootedShield(InventoryItem item) => item?.Code == EnergyShieldEquipment.ShieldCode &&
            (item.ShieldCharge == -1 || item.ShieldCharge > 0 || item.ShieldEnergy == -1 || item.ShieldEnergy > 0);

        // Binding a found item preserves its stored resources; it does not create
        // a new generator or refill it. Called immediately after the loot transfer.
        public static bool BindLootedShield(ref PredictedPlayerGhost health, InventoryGraph inventory)
        {
            if (health.CurrentHealth <= 0 || health.ShieldEquipped ||
                !CanUseLootedShield(inventory?.Equipped("Shield"))) return false;
            Initialize(ref health, inventory);
            return health.ShieldEquipped;
        }
    }

    // An inaccessible backpack cell cannot be re-attempted every frame while
    // the physical inventory and weapon's energy cost remain unchanged.
    public struct PmcReloadFailure
    {
        private bool m_Failed;
        private int m_Version,m_Cost;
        private uint m_Weapon;
        public bool Matches(int version,uint weapon,int cost) => m_Failed && m_Version==version && m_Weapon==weapon && m_Cost==cost;
        public void Record(int version,uint weapon,int cost) {m_Failed=true;m_Version=version;m_Weapon=weapon;m_Cost=cost;}
        public void Clear() {m_Failed=false;}
    }

    public static class PmcReloadInventory
    {
        public static bool Prepare(InventoryGraph inventory,int energyPerRound)
        {
            string rig=inventory.Equipped("ChestRig")?.Id;
            if(inventory.CellEnergy(accessibleOnly:true)>=energyPerRound)return true;
            // Stored cells must be moved to pockets or the equipped rig first.
            foreach(var source in inventory.Items.Where(i=>i.Code=="cells" && inventory.Carried(i) &&
                i.Parent!=InventoryCatalog.Pockets && i.Parent!=rig).ToArray())
            {
                var item=inventory.Find(source.Id);
                foreach(string parent in new[] {InventoryCatalog.Pockets,rig})
                    if(parent!=null && inventory.FindSpace(item,parent,out var region,out int x,out int y) &&
                        inventory.TryApply(new InventoryCommand {Operation=InventoryOperation.Move,ExpectedVersion=inventory.Version,
                            ItemId=item.Id,Parent=parent,Region=region,X=x,Y=y})==InventoryError.None)
                    {if(inventory.CellEnergy(accessibleOnly:true)>=energyPerRound)return true;break;}
            }
            return false;
        }
    }

    public struct PmcMedicalFailure
    {
        private bool m_Failed;
        private int m_Version;
        public bool Matches(int version) => m_Failed && m_Version == version;
        public void Record(int version) { m_Failed = true; m_Version = version; }
        public void Clear() { m_Failed = false; }
    }

    public static class PmcMedicalInventory
    {
        public static InventoryItem FindAccessible(InventoryGraph inventory) => inventory?.Items
            .FirstOrDefault(item => item.Quantity > 0 && inventory.MedicalAccessible(item));
        public static bool HasCarried(InventoryGraph inventory) => inventory != null && inventory.Items.Any(item =>
            item.Quantity > 0 && ConsumableCatalog.MedicalRestore(item.Code) > 0 && inventory.Carried(item));

        // NPCs use the same carried-storage and versioned movement rules as players.
        public static InventoryItem Prepare(InventoryGraph inventory)
        {
            var available = FindAccessible(inventory);
            if (available != null || inventory == null) return available;
            string rig = inventory.Equipped("ChestRig")?.Id;
            var sources = inventory.Items.Where(item => item.Quantity > 0 && ConsumableCatalog.MedicalRestore(item.Code) > 0 &&
                inventory.Carried(item)).Select(item => item.Id).ToArray();
            foreach (string id in sources)
            {
                var item = inventory.Find(id);
                foreach (string parent in new[] { InventoryCatalog.Pockets, rig })
                {
                    if (parent == null) continue;
                    var def = InventoryCatalog.Get(item.Code);
                    for (int orientation = 0; orientation < (def.Width == def.Height ? 1 : 2); orientation++)
                    {
                        var probe = item.Clone(); if (orientation == 1) probe.Rotated = !probe.Rotated;
                        if (!inventory.FindSpace(probe, parent, out string region, out int x, out int y)) continue;
                        var command = new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = inventory.Version,
                            ItemId = id, Parent = parent, Region = region, X = x, Y = y, Rotated = probe.Rotated };
                        if (inventory.TryApply(command) == InventoryError.None) return inventory.Find(id);
                    }
                }
            }
            return null;
        }
    }

    public static class PmcWeaponTactics
    {
        // Separate thresholds keep a target near the boundary from causing a weapon swap loop.
        public static int PreferredSlot(in PredictedPlayerGhost health, float distance)
        {
            if (!health.InventoryWeapons || health.CurrentHealth <= 0 || RaidHealth.HandsBusy(health) ||
                !float.IsFinite(distance) || distance < 0) return 0;
            uint desired = distance <= 11 ? DollSingerWeapons.Shotgun : distance >= 18 ? DollSingerWeapons.Halo : DollSingerWeapons.None;
            if (desired == DollSingerWeapons.None || health.EquippedWeaponID == desired) return 0;
            if (health.PrimaryWeaponID == desired && health.PrimaryAmmo > 0 && health.EquippedWeaponSlot != 1) return 1;
            if (health.SecondaryWeaponID == desired && health.SecondaryAmmo > 0 && health.EquippedWeaponSlot != 2) return 2;
            if (health.PistolWeaponID == desired && health.PistolAmmo > 0 && health.EquippedWeaponSlot != 3) return 3;
            return 0;
        }
    }

    public static class PmcUnknownAttack
    {
        public static bool NeedsEvasion(bool clash, bool hasContact, double lastDamage, double now) =>
            !clash && !hasContact && double.IsFinite(lastDamage) && double.IsFinite(now) &&
            now >= lastDamage && now - lastDamage < 3;

        // Retreat toward a known exit, or backward when already there. Neither
        // direction encodes an unseen attacker's actual position.
        public static float3 Direction(float3 position, float3 exit, float yawDegrees)
        {
            if (math.all(math.isfinite(position)) && math.all(math.isfinite(exit)))
            {
                float2 towardExit = new float2(exit.x - position.x, exit.z - position.z);
                float lengthSq = math.lengthsq(towardExit);
                if (math.all(math.isfinite(towardExit)) && math.isfinite(lengthSq) && lengthSq > 1)
                { towardExit = math.normalize(towardExit); return new float3(towardExit.x, 0, towardExit.y); }
            }
            float yaw = math.radians(float.IsFinite(yawDegrees) ? yawDegrees : 0);
            return new float3(-math.sin(yaw), 0, -math.cos(yaw));
        }
    }

    // Observations are the only way world knowledge enters a brain. Decision code uses
    // this snapshot, never a hidden opponent's current transform or inventory.
    public sealed class PmcContact
    {
        public Vector3 Position, Velocity;
        public Vector3 AimOffset = Vector3.up * 1.15f;
        public float Suspicion, Confidence;
        public double LastSeen = -100, LastHeard = -100;
        public bool Visible, Confirmed;
        public Vector3 LastVisualPosition, HearingBias;
        public float PositionUncertainty = 1;
        public double HearingBiasUntil = -100;
        public bool HadVisualSample;
        public double LastReported = -100;
    }

    public sealed class DollSingerEnemyBrain : IComponentData
    {
        public Entity Target;
        public InventoryGraph Inventory;
        public readonly Dictionary<Entity, PmcContact> Contacts = new Dictionary<Entity, PmcContact>();
        public readonly List<Entity> ExpiredContacts = new List<Entity>();
        public readonly HashSet<int> VisitedCaches = new HashSet<int>();
        public readonly NavMeshPath Path = new NavMeshPath();
        public readonly NavMeshPath ProbePath = new NavMeshPath();
        public readonly Vector3[] Corners = new Vector3[64];
        public readonly Vector3[] ProbeCorners = new Vector3[64];
        public int CornerCount, Corner, Seed, Cache = -1, SearchStep, LootedCaches;
        public int DrawersSearched;
        public Vector3 CacheApproach;
        public Vector3 LootLookPoint;
        public bool CacheApproachKnown;
        public double CacheObstructedSince = -1;
        public double NextCorpseScan;
        public double NextTacticalSwitch;
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
        public Vector3 AimOffset = Vector3.up * 1.15f;
        public float Yaw, Pitch, LastHealth = 100, Suppression, Confidence, Aggression, Caution, AimErrorX, AimErrorY;
        public float ExtractionProgress, ActionScore;
        // A server-assigned identity, never a fabricated transport connection.
        public int ClashParticipantId, ClashTeamId = -1, ClashEpoch, ClashSlot;
        public bool IsClashBot => ClashParticipantId > 0;
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
        public double NextCover, NextAimError, BurstUntil, NextBurst, SearchUntil, LootUntil, SpawnTime, LastDamage = -100;
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
        public double CoverPhaseUntil, CoverExposureUntil, AvoidCoverUntil, SearchPauseUntil, SearchObservationTime = -100;
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
        public Unity.Mathematics.Random Random;
        public float Uncertainty(double now) => Mathf.Clamp(ContactUncertainty +
            Mathf.Max(0, (float)(now - System.Math.Max(LastSeenTime, LastHeardTime))) * 1.1f, 1, 20);
    }
}
