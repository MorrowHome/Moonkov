using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public enum RaidGameMode : byte { Expedition, BountyClash }
    public struct RaidModeState : IComponentData { public RaidGameMode Mode; public int RulesVersion; }
    public struct ClashJoinAcceptedRpc : IRpcCommand
    { public RaidGameMode Mode; public int RulesVersion; public bool Accepted; public FixedString64Bytes Reason; }
    public enum ClashIntent : byte { CreateTeam, JoinTeam, LeaveTeam, SetReady, SetCapacity, Banish, Pickup, ReviveBegin, ReviveRenew, ReviveCancel, SenseHeld, NextRound }
    public struct ClashActionRpc : IRpcCommand
    {
        public FixedString64Bytes MatchId;
        public int Epoch, TargetParticipantId, Value;
        public uint RequestId;
        public ClashIntent Intent;
        public FixedString32Bytes InviteCode;
    }
    public struct ClashSnapshotRpc : IRpcCommand
    {
        public FixedString64Bytes MatchId;
        public int RulesVersion, Epoch, LocalParticipantId, LocalTeamId, TeamCapacity, PlayerCount, TeamCount, CarrierId;
        public FixedString32Bytes InviteCode;
        public uint Sequence, AcknowledgedRequest;
        public ClashResult Result;
        public ClashPhase Phase;
        public ClashBountyState Bounty;
        public ClashLife Life;
        public double ServerNow;
        public float Remaining, PrepareRemaining, BanishProgress, BoundaryRemaining, SenseRemaining, ReviveProgress;
        public float ExitRemainingA, ExitRemainingB;
        public ClashExitBlock ExitBlockA, ExitBlockB;
        public float3 TargetPosition, ExitPositionA, ExitPositionB, BountyPosition;
        public bool IsCarrier, TeamHasBounty, Connected, IsLeader, Closing, DeploymentAborted, CanStartNext;
        public float DeploymentRemaining;
        public float ReconnectRemaining, ArenaRadius;
        public float3 ArenaCenter;
    }
    public struct ClashParticipantRpc : IRpcCommand
    {
        public FixedString64Bytes MatchId, Name;
        public int Epoch, ParticipantId, TeamId, NetworkId;
        public uint Sequence;
        public ClashLife Life;
        public bool Ready, Connected, OwnTeam, IsLocal;
        public float3 Position; // Only own-team rows carry a position. Other rows are zeroed.
        public float Health, MaxHealthFraction, ReviveProgress, ReconnectRemaining;
    }
    public struct ClashSenseRpc : IRpcCommand
    {
        public FixedString64Bytes MatchId;
        public int Epoch;
        public uint Sequence;
        public float Remaining;
        public bool Active, Presence;
        public int ContactCount;
        public uint SectorMask; // Eight direction sectors, three distance bands; never exact enemy coordinates.
    }
    public struct ClashSenseContactRpc : IRpcCommand
    {
        public FixedString64Bytes MatchId;
        public int Epoch, Index, Count;
        public uint Sequence;
        public float3 Position;
        public float UncertaintyRadius, ValidFor;
    }
    public struct ClashPlayer : IComponentData
    {
        public int ParticipantId, TeamId, Epoch;
        public bool CanAct;
        public int LastAttacker;
    }
    public static class BountyClashRuntime
    {
        public static bool Enabled(EntityManager manager)
        {
            using var query = manager.CreateEntityQuery(typeof(RaidModeState));
            return query.CalculateEntityCount() == 1 && query.GetSingleton<RaidModeState>().Mode == RaidGameMode.BountyClash;
        }
        public static bool InArena(EntityManager manager,float3 position)
        {
            if(!Enabled(manager))return true;
            using var query=manager.CreateEntityQuery(typeof(BountyClashWorld));
            if(query.CalculateEntityCount()!=1)return false;
            var match=manager.GetComponentObject<BountyClashWorld>(query.GetSingletonEntity()).Match;
            return match!=null && ClashPoint.HorizontalSquared(new ClashPoint(position.x,position.y,position.z),match.Arena.Center)<=match.Arena.Radius*(double)match.Arena.Radius;
        }
        public static bool CanAct(EntityManager manager, Entity player) => !manager.HasComponent<ClashPlayer>(player) || manager.GetComponentData<ClashPlayer>(player).CanAct;
    }
}
