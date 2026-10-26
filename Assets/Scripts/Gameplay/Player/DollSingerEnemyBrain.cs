using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum PmcAction { Scavenge, Extract, Investigate, Search, Engage, Cover, Flank, Retreat, Reload, HoldAngle, Heal, ClashObjective, EvadeGrenade }
    public enum PmcCoverPhase { Approaching, Settling, Advancing, Holding, Returning }

    // The observer only remembers positions it could actually see. Turning away
    // does not erase the clue before detonation or track it through walls.
    public struct PmcGrenadeAwareness
    {
        private uint m_ObservedId, m_DangerId;
        private double m_ObservedSince, m_LastObservation, m_RecognizedAt;
        public Vector3 Position;
        public float Radius;
        public double Until;
        public bool Active(double now) => double.IsFinite(now) && now >= m_RecognizedAt && now < Until;
        public void Miss() { m_ObservedId = 0; }
        public bool Observe(uint id, Vector3 position, float radius, double fuseUntil, double now)
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
        public static bool Candidate(bool clash, TacticalItemKind kind, TacticalWorldPhase phase,
            float distance, float radius, float angle, float cone) => !clash && phase == TacticalWorldPhase.Flying &&
            (kind == TacticalItemKind.Frag || kind == TacticalItemKind.Dynamite) &&
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
        public bool BleedOnlyDamage, HealBleeding;
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
