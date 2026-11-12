using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private void HandleArena(ref SystemState state, EntityCommandBuffer ecb)
        {
            var match = SystemAPI.ManagedAPI.GetSingleton<ArenaMatch>();
            var manager = state.EntityManager;
            var room = LanRoomClient.Server;
            double now = SystemAPI.Time.ElapsedTime;
            foreach (var connection in match.Players.Keys.ToArray())
            {
                if (manager.HasComponent<NetworkStreamConnection>(connection) &&
                    manager.GetComponentData<NetworkStreamConnection>(connection).CurrentState != ConnectionState.State.Disconnected) continue;
                var departed = match.Players[connection];
                match.Classic?.DropBomb(departed.Id, departed.LastPosition);
                match.Players.Remove(connection);
                Persistence(ref state).Inventories.Remove(connection);
            }

            bool local = match.Classic != null && SystemAPI.HasSingleton<OfflineRaidMode>();
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<ClientJoinRequestV2Rpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var connection = received.ValueRO.SourceConnection;
                ecb.DestroyEntity(entity);
                if (!manager.HasComponent<NetworkId>(connection) || manager.HasComponent<NetworkStreamInGame>(connection) ||
                    match.Players.ContainsKey(connection)) continue;
                var intent = request.ValueRO;
                ArenaMatch.Player joined = null;
                if (intent.Mode == match.Mode && intent.RulesVersion == match.RulesVersion &&
                    !manager.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    if (local && match.Players.Count == 0 && intent.LoginToken.ToString() == "single-player")
                        joined = match.Join(connection, "offline", intent.PlayerName.ToString(), intent.CharacterIndex, now);
                    else if (room != null && room.TryAuthorizeMatch(match.Mode, intent.LoginToken.ToString(), out var participant))
                        joined = match.Join(connection, participant.Id, participant.Name, participant.Appearance.CharacterIndex, now);
                }
                if (joined != null && match.Classic != null)
                {
                    ecb.AddComponent(connection, new NetworkStreamInGame());
                    ecb.AddComponent(connection, new RaidSession { RaidId = 1, Phase = RaidPhase.Active, ExtractionIndex = -1 });
                }
                var reply = ecb.CreateEntity();
                ecb.AddComponent(reply, new ClashJoinAcceptedRpc
                {
                    Accepted = joined != null, Mode = match.Mode, RulesVersion = match.RulesVersion,
                    Reason = joined != null ? default : new FixedString64Bytes("Match ticket or version invalid.")
                });
                ecb.AddComponent(reply, new SendRpcCommandRequest { TargetConnection = connection });
            }

            if (match.Classic != null)
            {
                HandleClassic(ref state, ecb, match, now, local ? 1 : room?.MatchMembers ?? ClassicMatch.Capacity);
                return;
            }
            match.Tick(now, room?.MatchMembers ?? ArenaMatch.Capacity);
            match.ReservedSpawns.Clear();
            foreach (var (connection, seat) in match.Players)
            {
                if (seat.Spawned && manager.HasComponent<JoinedClient>(connection))
                {
                    var actor = manager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                    if (manager.HasComponent<ClashPlayer>(actor) && manager.HasComponent<PredictedPlayerGhost>(actor))
                    {
                        var identity = manager.GetComponentData<ClashPlayer>(actor);
                        var ghost = manager.GetComponentData<PredictedPlayerGhost>(actor);
                        if (ghost.CurrentHealth <= 0 && match.Phase == ArenaPhase.Fighting)
                        {
                            match.RecordKill(identity.LastAttacker, seat);
                            AddDeathToLeaderboard(manager.GetComponentData<NetworkId>(connection).Value);
                            RemoveArenaActor(ref state, ecb, connection, actor);
                            seat.RespawnAt = now + ArenaMatch.RespawnSeconds;
                            continue;
                        }
                        bool canAct = match.Phase == ArenaPhase.Fighting;
                        if (identity.CanAct != canAct)
                        {
                            identity.CanAct = ghost.ClashCanAct = canAct;
                            ghost.ClashLife = canAct ? ClashLife.Alive : ClashLife.Waiting;
                            manager.SetComponentData(actor, identity);
                            manager.SetComponentData(actor, ghost);
                        }
                        continue;
                    }
                }
                if ((!seat.Spawned || match.Phase == ArenaPhase.Fighting) && now >= seat.RespawnAt &&
                    FindSpawnPoint(ref state, out var spawnPoint, match.ReservedSpawns))
                {
                    match.ReservedSpawns.Add(spawnPoint.Position);
                    if (!seat.Spawned)
                    {
                        SetRaidInventory(ref state, connection, ArenaArmory.NewKit(), 1);
                        AddPlayerToLeaderboard(manager.GetComponentData<NetworkId>(connection).Value, seat.Name);
                    }
                    else GetRaidInventory(ref state, connection).CancelActions();
                    SpawnPlayerCharacter(ref state, ecb, connection, seat.Name, seat.Character, seat,
                        LocalTransform.FromPositionRotation(spawnPoint.Position, spawnPoint.Rotation));
                    seat.Spawned = true;
                }
            }

            float remaining = (float)System.Math.Max(0, match.Deadline - now);
            if (!UpdateMatchInventory(ref state, ecb, match, now, remaining)) return;
            foreach (var (connection, seat) in match.Players)
            {
                if (!manager.HasComponent<RaidSession>(connection)) continue;
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new ArenaSnapshotRpc { Phase = match.Phase, Players = match.Players.Count, Kills = seat.Score,
                    BestScore = match.BestScore, Remaining = remaining, Respawn = (float)System.Math.Max(0, seat.RespawnAt - now),
                    Won = match.Winner == seat.Id, Draw = match.Winner == 0 });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private bool UpdateMatchInventory(ref SystemState state, EntityCommandBuffer ecb, ArenaMatch match, double now, float remaining)
        {
            UpdateSurvival(ref state);
            UpdateTacticalItems(ref state, ecb);
            HandleArenaSupplies(ref state, ecb, match);
            var classic = match.Classic;
            if (classic != null) HandleLootRequests(ref state, ecb, classic);
            HandleInventoryRequests(ref state, ecb, discard: classic == null);
            if (classic != null) RefreshLootContainers(ref state);
            bool send = now >= match.NextSnapshot;
            if (send) match.NextSnapshot = now + .25;
            foreach (var connection in match.Players.Keys)
            {
                if (!state.EntityManager.HasComponent<RaidSession>(connection)) continue;
                if (state.EntityManager.HasComponent<RaidInventoryState>(connection))
                {
                    if (classic != null && state.EntityManager.HasComponent<JoinedClient>(connection))
                    {
                        RefreshLootAccess(ref state, connection, SystemAPI.GetComponent<RaidSession>(connection), classic);
                        if (send) SendDeathBags(ref state, ecb, connection);
                    }
                    SendInventory(ref state, ecb, connection, 1);
                }
                if (!send) continue;
                var raid = ecb.CreateEntity();
                ecb.AddComponent(raid, new RaidSnapshotV2Rpc { RaidId = 1, Sequence = (uint)(now * 1000 + 1),
                    Phase = RaidPhase.Active, TimeLeft = remaining, ExtractionRemaining = -1 });
                ecb.AddComponent(raid, new SendRpcCommandRequest { TargetConnection = connection });
            }
            return send;
        }

        private void RemoveArenaActor(ref SystemState state, EntityCommandBuffer ecb, Entity connection, Entity actor)
        {
            var manager = state.EntityManager;
            var joined = manager.GetComponentData<JoinedClient>(connection);
            joined.PlayerEntity = Entity.Null;
            manager.SetComponentData(connection, joined);
            var links = manager.GetBuffer<LinkedEntityGroup>(connection);
            for (int index = links.Length - 1; index >= 0; index--)
                if (links[index].Value == actor) links.RemoveAt(index);
            if (manager.HasComponent<PlayerClientCommandInputLookup>(actor))
            {
                var input = manager.GetComponentData<PlayerClientCommandInputLookup>(actor).ClientCommandInputEntity;
                if (manager.Exists(input)) ecb.DestroyEntity(input);
            }
            GetRaidInventory(ref state, connection).CancelActions();
            ecb.DestroyEntity(actor);
        }

        private void HandleArenaSupplies(ref SystemState state, EntityCommandBuffer ecb, ArenaMatch match)
        {
            var manager = state.EntityManager;
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<ArenaSupplyRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var connection = received.ValueRO.SourceConnection;
                ecb.DestroyEntity(entity);
                if (!match.Players.TryGetValue(connection, out var seat) || !manager.HasComponent<JoinedClient>(connection) ||
                    !manager.HasComponent<RaidInventoryState>(connection)) continue;
                var inventory = GetRaidInventory(ref state, connection);
                var intent = request.ValueRO;
                if (intent.RequestId <= inventory.RequestId) continue;
                inventory.RequestId = intent.RequestId;
                inventory.LastSentVersion = -1;
                inventory.ActionFeedback = null;
                var actor = manager.GetComponentData<JoinedClient>(connection).PlayerEntity;
                string code = intent.Code.ToString();
                var definition = match.Classic == null ? null : Inventory.InventoryCatalog.Get(code);
                int price = match.Classic == null ? 0 : ClassicMatch.Price(definition);
                int quantity = match.Classic == null ? 1 : intent.Quantity;
                bool open = match.Classic == null ? match.Phase != ArenaPhase.Finished :
                    match.Classic.Phase == ClassicPhase.Buy && quantity >= 1 &&
                    quantity <= ClassicMatch.PurchaseLimit(definition, seat.Credits);
                inventory.Error = !open || !TryGetLivingPosition(ref state, actor, out _) ||
                    inventory.InventoryMoveBlocked || manager.GetComponentData<PredictedPlayerGhost>(actor).ControllerState.IsReloadingState
                    ? Inventory.InventoryError.Inaccessible : match.Classic == null
                        ? ArenaArmory.Issue(inventory.Graph, code, intent.ExpectedVersion)
                        : ClassicMatch.Purchase(inventory.Graph, code, intent.ExpectedVersion, quantity);
                if (inventory.Error == Inventory.InventoryError.None)
                {
                    seat.Credits -= price * quantity;
                    UpdateInventoryProtection(ref state, connection, true);
                }
            }
        }
    }
}
