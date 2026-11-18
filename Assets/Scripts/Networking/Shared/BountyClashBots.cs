// 极速赏金的服务端机器人接口。比赛端用登记表发出生请求，机器人读取参赛者视图并提交意图，赛局再决定能否执行。
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // 启动本机模式时要明确开启此标记，不能按客户端 RPC 推断。
    public struct ClashSoloMode : IComponentData {
    }

    public struct ClashBot : IComponentData {
        public int ParticipantId, TeamId, Slot, Epoch;
    }

    public struct ClashBotSpawnRequest {
        public int ParticipantId, TeamId, Slot, Epoch, OwnerNetworkId;
        public float3 Position;
        public string Name;
    }

    public struct ClashBotIntent {
        public int ParticipantId, Epoch, TargetParticipantId;
        public uint Sequence;
        public ClashIntent Intent;
    }

    public struct ClashBotTeammate {
        public int ParticipantId;
        public ClashLife Life;
        public float3 Position;
        public float Health, ReviveProgress;
    }

    // 只提供自身、同队和真人玩家同样能得到的公开目标信息。
    // 战斗感知仍要使用 AI 自己的视觉和听觉观察。
    public sealed class ClashBotView {
        public int ParticipantId, TeamId, Epoch;
        public ClashPhase Phase;
        public ClashLife Life;
        public ClashBountyState Bounty;
        public float3 ArenaCenter, Target, BountyPosition, ExitA, ExitB;
        public float ArenaRadius, ExitRadius, BlockRadius, Health, BanishProgress;
        public bool IsCarrier, TeamHasBounty;
        public int ReviveTarget = -1;
        public double ServerNow, Deadline;
        public ClashExitBlock ExitBlockA, ExitBlockB;
        public float ExitRemainingA, ExitRemainingB;
        public ClashBotTeammate[] Teammates = Array.Empty<ClashBotTeammate>();
    }

    internal sealed class ClashBotActor {
        public ClashBotSpawnRequest Spawn;
        public Entity Entity, Input;
        public InventoryGraph Inventory;
        public uint LastIntent;
    }

    // 每个服务器 World 保有一份注册表。导航系统生成请求的角色，ServerGameSystem
    // 才负责结算，终局死亡仅掉一次尸体包，并清理角色与输入实体。
    public sealed class ClashBotRegistry : IComponentData {
        public int Epoch { get; internal set; }
        public bool NavigationReady { get; private set; }
        public string NavigationFailure { get; private set; }
        public bool NavigationFailed { get; private set; }
        // 比赛队列记录机器人该在哪轮和哪个参赛席出生。已生成角色与机器人可见信息另按席位索引。
        public readonly List<ClashBotSpawnRequest> PendingSpawns = new();
        internal readonly Dictionary<int, ClashBotActor> Actors = new();
        internal readonly Dictionary<int, ClashBotView> Views = new();
        internal readonly Queue<ClashBotIntent> Intents = new();

        // 地图导航准备成功后才把可部署标记告诉赛局。失败原因能由准备页展示。
        public void SetNavigation(bool ready, string failure = null, bool failed = false) {
            NavigationReady = ready;
            NavigationFailure = ready ? null : failure;
            NavigationFailed = !ready && failed;
        }

        public bool TryGetView(int participantId, out ClashBotView view) {
            return Views.TryGetValue(participantId, out view);
        }

        public bool IsRegistered(int participantId) { return Actors.ContainsKey(participantId); }

        // 出生回调要符合当前轮次与当前待生成记录，重复建立同一席会拒绝入表。
        public bool Register(ClashBotSpawnRequest request, Entity actor, Entity input, InventoryGraph inventory) {
            if (request.Epoch != Epoch || actor == Entity.Null || input == Entity.Null || inventory == null ||
                Actors.ContainsKey(request.ParticipantId) ||
                !PendingSpawns.Exists(r => r.ParticipantId == request.ParticipantId && r.Epoch == request.Epoch))
                return false;
            Actors.Add(request.ParticipantId,
                new ClashBotActor { Spawn = request, Entity = actor, Input = input, Inventory = inventory });
            PendingSpawns.RemoveAll(r => r.ParticipantId == request.ParticipantId);
            return true;
        }

        // AI 只提交决策意图，服务器再从自己的角色状态判行动。队列有容量限制，不能在落后时无限积累。
        public void Submit(ClashBotIntent intent) {
            if (intent.Epoch == Epoch && intent.Sequence != 0 && Actors.ContainsKey(intent.ParticipantId) &&
                Intents.Count < 128)
                Intents.Enqueue(intent);
        }

        // 重开比赛后清空旧发出的意图、角色映射和视图，新 AI 只能读新轮定义。
        internal void Reset(int epoch) {
            Epoch = epoch;
            PendingSpawns.Clear();
            Actors.Clear();
            Views.Clear();
            Intents.Clear();
        }
    }

    public static class ClashSoloRuntime {
        public static bool Enabled(EntityManager manager) {
            using var offline = manager.CreateEntityQuery(typeof(OfflineRaidMode));
            using var solo = manager.CreateEntityQuery(typeof(ClashSoloMode));
            return offline.CalculateEntityCount() == 1 && solo.CalculateEntityCount() == 1 &&
                   BountyClashRuntime.Enabled(manager);
        }

        public static bool TryGetRegistry(EntityManager manager, out ClashBotRegistry registry) {
            registry = null;
            if (!Enabled(manager)) return false;
            using var query = manager.CreateEntityQuery(typeof(ClashBotRegistry));
            if (query.CalculateEntityCount() != 1) return false;
            registry = manager.GetComponentObject<ClashBotRegistry>(query.GetSingletonEntity());
            return true;
        }
    }
}