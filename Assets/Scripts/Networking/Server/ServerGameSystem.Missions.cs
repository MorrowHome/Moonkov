using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Missions;

namespace Unity.MP_FPS
{
    public sealed class RaidMissionWorld : IComponentData
    {
        public sealed class Actor
        {
            public int RaidId, Node = -1;
            public string Key;
            public uint Request;
            public uint AcknowledgedRequest;
            public uint LastHitTick;
            public float Health;
            public MissionActionResult Result;
        }
        public readonly Dictionary<Entity, Actor> Actors = new Dictionary<Entity, Actor>();
        public readonly HashSet<Entity> Known = new HashSet<Entity>();
        public RaidMissionSite Site;
        public string WorldId;
        public MissionDirector Director;
        public MissionOperation Operation;
        public uint Sequence;
        public uint Seed;
        public int Epoch, DefinitionIndex, OutputLootId = -1;
        public double NextSend, EndedAt;
        public bool OutputProduced;
        public bool BackendCompatible = true;
    }

    public partial struct ServerGameSystem
    {
        private void HandleMissions(ref SystemState state, EntityCommandBuffer ecb)
        {
            var site = RaidMissionSite.Active;
            if (!site) return;
            var context = state.EntityManager.GetComponentObject<RaidMissionWorld>(SystemAPI.GetSingletonEntity<RaidLootWorld>());
            double now = SystemAPI.Time.ElapsedTime;
            if (context.Site != site)
            {
                context.Site = site; context.WorldId = Guid.NewGuid().ToString("D");
                context.Seed = site.Seed == 0 ? unchecked((uint)Guid.NewGuid().GetHashCode()) : site.Seed;
                context.Director = new MissionDirector(context.Seed, now); context.Operation = null;
                Debug.Log("[Missions] Director seed " + context.Seed + ". Fixed author seed is available for reproduction.");
                context.Known.Clear(); context.Actors.Clear(); context.Epoch = 0; context.Sequence = 0;
            }
            float audience = 0;
            foreach (var (session, joined) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>())
                if (session.ValueRO.Phase == RaidPhase.Active && TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var p) &&
                    math.distancesq(p, site.Center) <= 300 * 300) audience = math.max(audience, session.ValueRO.TimeLeft);
            if (context.Operation == null && context.BackendCompatible)
            {
                var chosen = context.Director.TryOffer(site.Definitions, now, audience, false, d => MissionAnchorsReady(site, d));
                if (chosen != null)
                {
                    context.Operation = new MissionOperation(context.WorldId, Guid.NewGuid().ToString("D"), ++context.Epoch, chosen, now);
                    context.DefinitionIndex = Array.FindIndex(site.Definitions, d => d.Id == chosen.Id);
                    context.OutputLootId = -1; context.OutputProduced = false; context.EndedAt = 0;
                    context.Known.Clear(); context.Actors.Clear();
                }
            }
            var operation = context.Operation;
            if (operation != null) operation.Tick(now);
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidMissionActionRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var connection = received.ValueRO.SourceConnection;
                var r = request.ValueRO;
                if (operation != null && r.OperationId.ToString() == operation.Id && r.Epoch == operation.Epoch &&
                    SystemAPI.HasComponent<RaidSession>(connection) && SystemAPI.HasComponent<JoinedClient>(connection) &&
                    !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    var session = SystemAPI.GetComponent<RaidSession>(connection);
                    if (session.RaidId == r.RaidId && session.Phase == RaidPhase.Active)
                    {
                        if (!context.Actors.TryGetValue(connection, out var actor) || actor.RaidId != session.RaidId)
                        {
                            if (actor != null) operation.Cancel(actor.Key);
                            actor = new RaidMissionWorld.Actor { RaidId = session.RaidId, Key = session.SettlementId.ToString() };
                            context.Actors[connection] = actor;
                        }
                        var player = SystemAPI.GetComponent<JoinedClient>(connection).PlayerEntity;
                        bool accessible = MissionAccessible(ref state, player, site, operation, r.Node, out float health);
                        uint hitTick = accessible ? SystemAPI.GetComponent<PredictedPlayerGhost>(player).LastHitTick : 0;
                        if (r.Intent == MissionIntent.Begin || r.Intent == MissionIntent.Configure) actor.AcknowledgedRequest = r.RequestId;
                        if (r.Intent == MissionIntent.Cancel)
                        {
                            if (actor.Request == r.RequestId && actor.Node == r.Node) { operation.Cancel(actor.Key); actor.Node = -1; }
                        }
                        else if (!accessible || (actor.Node >= 0 && (health < actor.Health || hitTick != actor.LastHitTick)))
                        { operation.Cancel(actor.Key); actor.Node = -1; actor.Result = MissionActionResult.Invalid; }
                        else if (r.Intent == MissionIntent.Begin)
                        {
                            actor.AcknowledgedRequest = r.RequestId;
                            actor.Result = operation.Begin(actor.Key, r.Epoch, r.Node, r.RequestId, now);
                            if (actor.Result == MissionActionResult.Started || actor.Result == MissionActionResult.Running)
                            { actor.Node = r.Node; actor.Request = r.RequestId; actor.Health = health; actor.LastHitTick = hitTick; context.Known.Add(connection); }
                        }
                        else if (r.Intent == MissionIntent.Configure)
                        {
                            actor.Result = operation.Configure(actor.Key, r.Epoch, r.Node, r.RequestId, r.Choice, now);
                            if (actor.Result == MissionActionResult.Completed) context.Known.Add(connection);
                        }
                        else if (r.Intent == MissionIntent.Renew && actor.Node == r.Node && actor.Request == r.RequestId)
                        {
                            actor.Result = operation.Renew(actor.Key, r.RequestId, now);
                            if (actor.Result != MissionActionResult.Running) actor.Node = -1;
                        }
                    }
                }
                ecb.DestroyEntity(entity);
            }
            if (operation != null)
            {
                foreach (var pair in context.Actors)
                {
                    if (pair.Value.Node < 0) continue;
                    bool valid = SystemAPI.Exists(pair.Key) && SystemAPI.HasComponent<JoinedClient>(pair.Key) &&
                        SystemAPI.HasComponent<RaidSession>(pair.Key) && !SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(pair.Key) &&
                        SystemAPI.GetComponent<RaidSession>(pair.Key).Phase == RaidPhase.Active;
                    float health = 0;
                    if (valid) valid = MissionAccessible(ref state, SystemAPI.GetComponent<JoinedClient>(pair.Key).PlayerEntity,
                        site, operation, pair.Value.Node, out health) && health >= pair.Value.Health &&
                        SystemAPI.GetComponent<PredictedPlayerGhost>(SystemAPI.GetComponent<JoinedClient>(pair.Key).PlayerEntity).LastHitTick == pair.Value.LastHitTick;
                    if (!valid) { operation.Cancel(pair.Value.Key); pair.Value.Node = -1; pair.Value.Result = MissionActionResult.Invalid; }
                }
                if (operation.Phase == MissionPhase.OutputReady && !context.OutputProduced) CreateMissionOutput(ref state, context);
                if (operation.Phase == MissionPhase.OutputReady && context.OutputProduced && !MissionArtifactsRemain(ref state, context, false)) operation.Resolve();
                if (operation.Phase == MissionPhase.Expired) MissionArtifactsRemain(ref state, context, true);
                bool ended = operation.Phase == MissionPhase.Expired || operation.Phase == MissionPhase.Resolved;
                if (ended && context.EndedAt == 0) context.EndedAt = now;
                if (ended && now - context.EndedAt >= 120 && !MissionPlayersNear(ref state, site.Center, 40))
                {
                    if (context.OutputLootId >= 0)
                    { LootContainers(ref state).Containers.Remove(context.OutputLootId); LootContainers(ref state).MissionCaches.Remove(context.OutputLootId); }
                    context.Operation = null; context.OutputLootId = -1; context.Known.Clear(); context.Actors.Clear();
                }
            }
            if (now < context.NextSend) return;
            context.NextSend = now + .25; context.Sequence++;
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection)) continue;
                if (context.Operation != null && TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var p) &&
                    math.distancesq(p, site.Center) <= site.DiscoveryRadius * site.DiscoveryRadius) context.Known.Add(connection);
                var snapshot = new RaidMissionSnapshotRpc { RaidId = session.ValueRO.RaidId, Sequence = context.Sequence,
                    WorldId = context.WorldId, OutputLootId = -1, DefinitionIndex = -1, SaveState = session.ValueRO.SaveState, BackendSupported = context.BackendCompatible };
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    var inventory = GetRaidInventory(ref state, connection);
                    snapshot.MissionFirstsBefore = inventory.MissionFirstsBefore; snapshot.MissionFirstsSaved = inventory.MissionFirstsSaved;
                    if (session.ValueRO.Phase == RaidPhase.Extracted)
                        foreach (var item in inventory.Graph.Items) if (MissionInventory.IsArtifact(item.Code) && !item.MissionRecovered && inventory.Graph.Carried(item)) snapshot.RecoveredArtifacts++;
                }
                if (context.Operation != null && context.Known.Contains(connection))
                {
                    var op = context.Operation;
                    snapshot.OperationId = op.Id; snapshot.Epoch = op.Epoch; snapshot.Phase = op.Phase;
                    snapshot.CompletedMask = op.CompletedMask; snapshot.DefinitionIndex = context.DefinitionIndex;
                    snapshot.Remaining = (float)System.Math.Max(0, op.Deadline - now); snapshot.OutputLootId = context.OutputLootId;
                    if (context.OutputLootId >= 0 && LootContainers(ref state).Containers.TryGetValue(context.OutputLootId, out var output))
                        foreach (var item in output.Items) if (MissionInventory.IsArtifact(item.Code)) snapshot.OutputRemaining++;
                    for (int i = 0; i < op.ObjectiveCount; i++)
                        if (i < 4) snapshot.ProgressA[i] = op.Progress(i, now); else snapshot.ProgressB[i - 4] = op.Progress(i, now);
                    if (context.Actors.TryGetValue(connection, out var actor)) { snapshot.RequestId = actor.AcknowledgedRequest; snapshot.Result = actor.Result; }
                }
                var entity = ecb.CreateEntity(); ecb.AddComponent(entity, snapshot);
                ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private static bool MissionAnchorsReady(RaidMissionSite site, MissionDefinition definition)
        {
            var entrance = site.Anchor("entry");
            if (entrance == null || !NavMesh.SamplePosition(entrance.Approach, out var start, 3, NavMesh.AllAreas)) return false;
            var path = new NavMeshPath();
            foreach (var n in definition.Objectives)
            {
                var a = site.Anchor(n.Anchor); if (a == null) return false;
                if (!UnityEngine.Physics.Raycast(a.Approach + Vector3.up, Vector3.down, out _, 2.5f,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)) return false;
                if (!NavMesh.SamplePosition(a.Approach, out var end, 3, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            }
            return true;
        }
        private bool MissionPlayersNear(ref SystemState state, Vector3 center, float radius)
        {
            foreach (var (session, joined) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>())
                if (session.ValueRO.Phase == RaidPhase.Active && TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var p) &&
                    math.distancesq(p, center) < radius * radius) return true;
            return false;
        }
        private bool MissionAccessible(ref SystemState state, Entity player, RaidMissionSite site, MissionOperation operation, int node, out float health)
        {
            health = 0;
            var definition = operation.Objective(node); if (definition == null || definition.Kind == MissionObjectiveKind.Process) return false;
            var anchor = site.Anchor(definition.Anchor); if (anchor == null || !TryGetLivingPosition(ref state, player, out var position)) return false;
            var ghost = SystemAPI.GetComponent<PredictedPlayerGhost>(player);
            health = ghost.CurrentHealth;
            var eye = (Vector3)position + Vector3.up * 1.4f;
            var facing = Quaternion.Euler(ghost.ControllerState.PitchDegrees, ghost.ControllerState.YawDegrees, 0) * Vector3.forward;
            return math.distancesq(position, anchor.Position) <= 3 * 3 &&
                Vector3.Dot(facing, (anchor.Position - eye).normalized) >= .65f &&
                !UnityEngine.Physics.Linecast(eye, anchor.Position,
                    LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
        }
        private void CreateMissionOutput(ref SystemState state, RaidMissionWorld context)
        {
            var definition = context.Operation.Definition;
            var cache = InventoryGraph.Create(false, false);
            cache.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
            for (int i = 0; i < definition.OutputCount; i++)
                cache.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = definition.ArtifactCode,
                    Parent = LootInventoryExchange.Root, Region = "main", X = i % 6, Y = i / 6, FoundInRaid = true,
                    MissionWorldId = context.WorldId, MissionOperationId = context.Operation.Id, MissionContentVersion = definition.Version });
            if (cache.Validate() != InventoryError.None) throw new InvalidOperationException("Mission output failed inventory validation.");
            context.OutputLootId = RaidLootContainers.FirstMissionCacheId + context.Epoch;
            var anchor = context.Site.Anchor(definition.ArtifactCode == "mooncore_fragment" ? "sampler" : "archive");
            var loot = LootContainers(ref state); loot.Containers.Add(context.OutputLootId, cache);
            loot.MissionCaches.Add(context.OutputLootId, anchor.Position); context.OutputProduced = true;
        }
        private bool MissionArtifactsRemain(ref SystemState state, RaidMissionWorld context, bool destroy)
        {
            bool remains = false;
            bool Match(InventoryItem item) => MissionInventory.IsArtifact(item.Code) && item.MissionOperationId == context.Operation.Id && !item.MissionRecovered;
            foreach (var graph in LootContainers(ref state).Containers.Values)
            {
                bool found = graph.Items.Exists(Match); remains |= found;
                if (destroy && found) { graph.Items.RemoveAll(item => Match(item)); graph.Version++; }
            }
            foreach (var (session, connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess())
            {
                if (session.ValueRO.Phase != RaidPhase.Active || !state.EntityManager.HasComponent<RaidInventoryState>(connection)) continue;
                var inventory = GetRaidInventory(ref state, connection); bool found = inventory.Graph.Items.Exists(Match); remains |= found;
                if (destroy && found) { inventory.Graph.Items.RemoveAll(item => Match(item)); inventory.Graph.Version++; inventory.LastSentVersion = -1; }
            }
            return remains;
        }
    }
}
