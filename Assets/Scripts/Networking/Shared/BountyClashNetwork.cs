// 极速赏金客户端和服务端共用的模式状态、操作请求、比赛快照与个人感知消息。这里也给其他系统提供角色能否按本玩法行动的判断入口。
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace Unity.MP_FPS {
    // 这四个值是客户端申请与服务端实际玩法确认的共同编号。收到同玩法、同规则版本才能结束入场等待。
    public enum RaidGameMode : byte {
        Expedition,
        BountyClash,
        Arena,
        Classic
    }

    public struct RaidModeState : IComponentData {
        public RaidGameMode Mode;
        public int RulesVersion;
    }

    public struct ClashJoinAcceptedRpc : IRpcCommand {
        public RaidGameMode Mode;
        public int RulesVersion;
        public bool Accepted;
        public FixedString64Bytes Reason;
    }

    public enum ClashIntent : byte {
        CreateTeam,
        JoinTeam,
        LeaveTeam,
        SetReady,
        SetCapacity,
        Banish,
        Pickup,
        ReviveBegin,
        ReviveRenew,
        ReviveCancel,
        SenseHeld,
        NextRound
    }

    // 玩家请求只带比赛、轮次、请求号和意图参数。服务端根据真正连接找到角色再决定能否操作。
    public struct ClashActionRpc : IRpcCommand {
        public FixedString64Bytes MatchId;
        public int Epoch, TargetParticipantId, Value;
        public uint RequestId;
        public ClashIntent Intent;
        public FixedString32Bytes InviteCode;
    }

    // 公共对局读条与接收者自己的可行动、队伍、感知额度一起形成快照。请求回执号用于解锁相应按钮。
    public struct ClashSnapshotRpc : IRpcCommand {
        public FixedString64Bytes MatchId;

        public int RulesVersion,
            Epoch,
            LocalParticipantId,
            LocalTeamId,
            TeamCapacity,
            PlayerCount,
            TeamCount,
            CarrierId;

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
        public bool IsSolo, SoloNavigationReady;
        public FixedString128Bytes SoloNavigationStatus;
        public float DeploymentRemaining;
        public float ReconnectRemaining, ArenaRadius;
        public float3 ArenaCenter;
    }

    // 排行榜行独立发送，用 MatchId、Epoch 和 Sequence 同主快照对应。只有同队允许得到精确位置。
    public struct ClashParticipantRpc : IRpcCommand {
        public FixedString64Bytes MatchId, Name;
        public int Epoch, ParticipantId, TeamId, NetworkId;
        public uint Sequence;
        public ClashLife Life;
        public bool Ready, Connected, OwnTeam, IsLocal, IsBot;
        public float3 Position; // 只有同队的成员行传位置，其他行的位置归零。
        public float Health, MaxHealthFraction, ReviveProgress, ReconnectRemaining;
    }

    // 基础感知仅指示扇区和距离带，大致位置与增强目标分开。增强消息也带本轮编号和短有效期。
    public struct ClashSenseRpc : IRpcCommand {
        public FixedString64Bytes MatchId;
        public int Epoch;
        public uint Sequence;
        public float Remaining;
        public bool Active, Presence;
        public int ContactCount;
        public uint SectorMask; // 八个方向扇区和三个距离范围，不含敌人精确坐标。
    }

    public struct ClashSenseContactRpc : IRpcCommand {
        public FixedString64Bytes MatchId;
        public int Epoch, Index, Count;
        public uint Sequence;
        public float3 Position;
        public float UncertaintyRadius, ValidFor;
    }

    // 服务端角色实体记录它属于比赛哪个席位与队伍。开火、移动及伤害系统就能快速查它能否行动。
    public struct ClashPlayer : IComponentData {
        public int ParticipantId, TeamId, Epoch;
        public bool CanAct;
        public int LastAttacker;
    }

    public static class BountyClashRuntime {
        public static bool Enabled(EntityManager manager) {
            using var query = manager.CreateEntityQuery(typeof(RaidModeState));
            return query.CalculateEntityCount() == 1 &&
                   query.GetSingleton<RaidModeState>().Mode == RaidGameMode.BountyClash;
        }

        // 其他模式不受赏金范围影响。启用后从本 World 的比赛配置按水平坐标检查边界。
        public static bool InArena(EntityManager manager, float3 position) {
            if (!Enabled(manager)) return true;
            using var query = manager.CreateEntityQuery(typeof(BountyClashWorld));
            if (query.CalculateEntityCount() != 1) return false;
            var match = manager.GetComponentObject<BountyClashWorld>(query.GetSingletonEntity()).Match;
            return match != null &&
                   ClashPoint.HorizontalSquared(new ClashPoint(position.x, position.y, position.z),
                       match.Arena.Center) <= match.Arena.Radius * (double)match.Arena.Radius;
        }

        public static bool CanAct(EntityManager manager, Entity player) {
            return !manager.HasComponent<ClashPlayer>(player) || manager.GetComponentData<ClashPlayer>(player).CanAct;
        }
    }
}