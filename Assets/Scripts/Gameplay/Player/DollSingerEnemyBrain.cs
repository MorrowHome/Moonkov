using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum PmcAction { Scavenge, Extract, Investigate, Search, Engage, Cover, Flank, Retreat, Reload, HoldAngle, Heal, ClashObjective }
    public enum PmcCoverPhase { Approaching, Settling, Advancing, Holding, Returning }

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
