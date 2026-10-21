using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // Local bootstrap must opt in explicitly. Never inferred from a client RPC.
    public struct ClashSoloMode : IComponentData { }
    public struct ClashBot : IComponentData
    { public int ParticipantId, TeamId, Slot, Epoch; }

    public struct ClashBotSpawnRequest
    {
        public int ParticipantId, TeamId, Slot, Epoch, OwnerNetworkId;
        public float3 Position;
        public string Name;
    }
    public struct ClashBotIntent
    {
        public int ParticipantId, Epoch, TargetParticipantId;
        public uint Sequence;
        public ClashIntent Intent;
    }
    public struct ClashBotTeammate
    {
        public int ParticipantId;
        public ClashLife Life;
        public float3 Position;
        public float Health, ReviveProgress;
    }
    // Only self, own squad and the same public objective information humans receive.
    // Combat perception must continue to use the brain's visual/hearing observations.
    public sealed class ClashBotView
    {
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
    internal sealed class ClashBotActor
    {
        public ClashBotSpawnRequest Spawn;
        public Entity Entity, Input;
        public InventoryGraph Inventory;
        public uint LastIntent;
    }
    // One server-owned registry per World. Nav creates the requested actors; ServerGameSystem
    // alone settles them, drops a final-death bag once, and destroys actor+input on terminal life.
    public sealed class ClashBotRegistry : IComponentData
    {
        public int Epoch { get; internal set; }
        public bool NavigationReady { get; private set; }
        public string NavigationFailure { get; private set; }
        public bool NavigationFailed { get; private set; }
        public readonly List<ClashBotSpawnRequest> PendingSpawns = new List<ClashBotSpawnRequest>();
        internal readonly Dictionary<int, ClashBotActor> Actors = new Dictionary<int, ClashBotActor>();
        internal readonly Dictionary<int, ClashBotView> Views = new Dictionary<int, ClashBotView>();
        internal readonly Queue<ClashBotIntent> Intents = new Queue<ClashBotIntent>();
        public void SetNavigation(bool ready, string failure = null, bool failed = false)
        { NavigationReady = ready; NavigationFailure = ready ? null : failure; NavigationFailed = !ready && failed; }
        public bool TryGetView(int participantId, out ClashBotView view) => Views.TryGetValue(participantId, out view);
        public bool IsRegistered(int participantId) => Actors.ContainsKey(participantId);
        public bool Register(ClashBotSpawnRequest request, Entity actor, Entity input, InventoryGraph inventory)
        {
            if (request.Epoch != Epoch || actor == Entity.Null || input == Entity.Null || inventory == null ||
                Actors.ContainsKey(request.ParticipantId) || !PendingSpawns.Exists(r => r.ParticipantId == request.ParticipantId && r.Epoch == request.Epoch)) return false;
            Actors.Add(request.ParticipantId, new ClashBotActor { Spawn = request, Entity = actor, Input = input, Inventory = inventory });
            PendingSpawns.RemoveAll(r => r.ParticipantId == request.ParticipantId);
            return true;
        }
        public void Submit(ClashBotIntent intent)
        {
            if (intent.Epoch == Epoch && intent.Sequence != 0 && Actors.ContainsKey(intent.ParticipantId) && Intents.Count < 128)
                Intents.Enqueue(intent);
        }
        internal void Reset(int epoch)
        { Epoch = epoch; PendingSpawns.Clear(); Actors.Clear(); Views.Clear(); Intents.Clear(); }
    }
    public static class ClashSoloRuntime
    {
        public static bool Enabled(EntityManager manager)
        {
            using var offline = manager.CreateEntityQuery(typeof(OfflineRaidMode));
            using var solo = manager.CreateEntityQuery(typeof(ClashSoloMode));
            return offline.CalculateEntityCount() == 1 && solo.CalculateEntityCount() == 1 && BountyClashRuntime.Enabled(manager);
        }
        public static bool TryGetRegistry(EntityManager manager, out ClashBotRegistry registry)
        {
            registry = null;
            if (!Enabled(manager)) return false;
            using var query = manager.CreateEntityQuery(typeof(ClashBotRegistry));
            if (query.CalculateEntityCount() != 1) return false;
            registry = manager.GetComponentObject<ClashBotRegistry>(query.GetSingletonEntity());
            return true;
        }
    }
}
