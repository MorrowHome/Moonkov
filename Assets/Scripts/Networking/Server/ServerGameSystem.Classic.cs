using System;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private void HandleClassic(ref SystemState state, EntityCommandBuffer ecb, ArenaMatch match, double now, int expected)
        {
            var manager = state.EntityManager;
            var rules = match.Classic;
            var map = ClassicDust2Map.Active;
            if (!map || match.Players.Count == 0) return;
            if (rules.NeedsRound(now) && match.FirstRoundReady(now, expected) &&
                match.Players.Keys.All(connection => manager.HasComponent<RaidSession>(connection)) &&
                state.World.GetExistingSystemManaged<DollSingerEnemySystem>() is { } ai && WeaponManager.Instance?.WeaponRegistry != null)
                StartClassicRound(ref state, ecb, match, map, ai, now);

            foreach (var (intent, received, entity) in SystemAPI.Query<RefRO<ClassicUseRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                ecb.DestroyEntity(entity);
                if (intent.ValueRO.Round != rules.Round || !match.Players.TryGetValue(received.ValueRO.SourceConnection, out var player)) continue;
                player.UseUntil = intent.ValueRO.Held ? now + .65 : 0;
                if (!intent.ValueRO.Held) rules.CancelHold(player.Id);
            }

            int aliveA = 0, aliveB = 0;
            bool startingCombat = rules.Phase == ClassicPhase.Buy && now >= rules.Deadline;
            bool live = rules.Phase is ClassicPhase.Live or ClassicPhase.Planted;
            var tactics = match.ClassicTactics;
            if (live) tactics.BeginTick();
            foreach (var (connection, seat) in match.Players)
            {
                var actor = MatchActor(manager, connection);
                if (!TryGetLivingPosition(ref state, actor, out var position))
                {
                    if (live)
                    {
                        rules.DropBomb(seat.Id, seat.LastPosition);
                        if (manager.HasComponent<PredictedPlayerGhost>(actor))
                            SetClassicActorActive(manager, actor, active: false);
                    }
                    rules.CancelHold(seat.Id);
                    continue;
                }
                if (startingCombat) rules.EnterCombat(seat.Id);
                seat.LastPosition = position;
                if (seat.Team == 0) aliveA++; else aliveB++;
                var use = ClassicOpportunity(map, rules, seat.Id, seat.Team, position);
                if ((use is ClassicUse.CacheA or ClassicUse.CacheMid or ClassicUse.CacheB) && now < seat.UseUntil)
                {
                    int cache = use == ClassicUse.CacheA ? 0 : use == ClassicUse.CacheMid ? 1 : 2;
                    var inventory = GetRaidInventory(ref state, connection);
                    if (rules.TakeCache(seat.Id, seat.Team, cache, inventory.Graph, inventory.Graph.Version))
                    {
                        inventory.LastSentVersion = -1;
                        UpdateInventoryProtection(ref state, connection, true);
                    }
                }
                if (!live) continue;
                var health = manager.GetComponentData<PredictedPlayerGhost>(actor);
                tactics.Observe(actor, seat.Id, seat.Team, seat.Slot, false, position, health.LastShotTick, now);
                if (now >= seat.UseUntil) { rules.CancelHold(seat.Id); continue; }
                rules.Use(seat.Id, seat.Team, use, position, health.LastHitTick, now);
            }
            foreach (var bot in match.Bots.Values)
            {
                if (!TryGetLivingPosition(ref state, bot.Actor, out var position))
                {
                    if (live) rules.DropBomb(bot.Id, bot.LastPosition);
                    rules.CancelHold(bot.Id);
                    continue;
                }
                if (startingCombat) rules.EnterCombat(bot.Id);
                bot.LastPosition = position;
                if (bot.Team == 0) aliveA++; else aliveB++;
                if (live) tactics.Observe(bot.Actor, bot.Id, bot.Team, bot.Slot, true, position,
                    manager.GetComponentData<PredictedPlayerGhost>(bot.Actor).LastShotTick, now);
            }
            if (live) UpdateClassicBots(ref state, match, map, now);

            rules.Advance(now, rules.AttackTeam == 0 ? aliveA : aliveB, rules.AttackTeam == 0 ? aliveB : aliveA);
            if ((rules.Phase is ClassicPhase.RoundOver or ClassicPhase.Finished) && match.CreditedRound != rules.Round)
            {
                match.CreditedRound = rules.Round;
                foreach (var seat in match.Players.Values)
                    seat.Credits = System.Math.Min(16000, seat.Credits + (seat.Team == rules.RoundWinner ? 3250 : 1400));
            }
            bool active = rules.Phase is ClassicPhase.Live or ClassicPhase.Planted;
            foreach (var connection in match.Players.Keys) SetClassicActorActive(manager, MatchActor(manager, connection), active);
            foreach (var bot in match.Bots.Values) SetClassicActorActive(manager, bot.Actor, active);

            float remaining = (float)System.Math.Max(0, rules.Deadline - now);
            if (!UpdateMatchInventory(ref state, ecb, match, now, remaining)) return;
            Span<ClassicScoreboardEntry> roster = stackalloc ClassicScoreboardEntry[ClassicMatch.Capacity];
            roster.Clear();
            Span<byte> aliveMasks = stackalloc byte[2];
            aliveMasks.Clear();
            foreach (var (connection, player) in match.Players)
            {
                roster[player.Team * ClassicMatch.TeamSize + player.Slot] = rules.ScoreboardEntry(player.Id, player.Name);
                if (TryGetLivingPosition(ref state, MatchActor(manager, connection), out _))
                    aliveMasks[player.Team] |= (byte)(1 << player.Slot);
            }
            foreach (var bot in match.Bots.Values)
            {
                roster[bot.Team * ClassicMatch.TeamSize + bot.Slot] =
                    rules.ScoreboardEntry(bot.Id, new FixedString64Bytes($"BOT {bot.Slot + 1:00}"));
                if (TryGetLivingPosition(ref state, bot.Actor, out _)) aliveMasks[bot.Team] |= (byte)(1 << bot.Slot);
            }

            foreach (var (connection, seat) in match.Players)
            {
                if (!manager.HasComponent<RaidSession>(connection)) continue;
                var actor = MatchActor(manager, connection);
                bool alive = TryGetLivingPosition(ref state, actor, out var position);
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new ClassicSnapshotV4Rpc
                {
                    Phase = rules.Phase, Round = rules.Round, Team = seat.Team, Slot = seat.Slot, AttackTeam = rules.AttackTeam,
                    ScoreA = rules.Score[0], ScoreB = rules.Score[1],
                    AliveA = Unity.Mathematics.math.countbits((uint)aliveMasks[0]),
                    AliveB = Unity.Mathematics.math.countbits((uint)aliveMasks[1]),
                    AliveMaskA = aliveMasks[0], AliveMaskB = aliveMasks[1],
                    SeatA0 = roster[0], SeatA1 = roster[1], SeatA2 = roster[2], SeatA3 = roster[3], SeatA4 = roster[4],
                    SeatB0 = roster[5], SeatB1 = roster[6], SeatB2 = roster[7], SeatB3 = roster[8], SeatB4 = roster[9],
                    CacheMask = rules.CacheMask(seat.Id),
                    Remaining = remaining, Credits = seat.Credits, Winner = rules.Phase == ClassicPhase.Finished ? rules.Winner : rules.RoundWinner,
                    HasBomb = rules.Carrier == seat.Id, Alive = alive,
                    Use = alive ? ClassicOpportunity(map, rules, seat.Id, seat.Team, position) : ClassicUse.None,
                    UseProgress = rules.HoldProgress(seat.Id, now),
                    BombVisible = rules.Phase == ClassicPhase.Planted || rules.Phase == ClassicPhase.Live && rules.BombOnGround,
                    BombPosition = rules.BombPosition
                });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
        }

        private void UpdateClassicBots(ref SystemState state, ArenaMatch match, ClassicDust2Map map, double now)
        {
            using var profile = s_ClassicBotProfile.Auto();
            var manager = state.EntityManager;
            var rules = match.Classic;
            var tactics = match.ClassicTactics;
            var tacticalWorld = TacticalWorld(ref state);
            float dt = SystemAPI.Time.DeltaTime;
            tactics.UpdateOrders(rules, map, now);
            foreach (var bot in match.Bots.Values)
            {
                if (!tactics.Alive(bot)) continue;
                var health = manager.GetComponentData<PredictedPlayerGhost>(bot.Actor);
                var status = TacticalStatus(tacticalWorld, bot.Actor);
                PmcSprintRules.Tick(ref status.Sprint, ref status.SprintPosition, ref status.SprintPositionKnown,
                    bot.LastPosition, dt, true, health.ControllerState.Sprinting, health.ControllerState.MovementSpeed, 0, status.Stamina > now);
                health.TacticalSprintStamina = status.Sprint.Value;
                health.TacticalSprintExhausted = status.Sprint.Exhausted;
                manager.SetComponentData(bot.Actor, health);
                var use = ClassicOpportunity(map, rules, bot.Id, bot.Team, bot.LastPosition);
                if (bot.Tick(manager, tactics, map, rules, health, tacticalWorld, use, now, dt))
                    rules.Use(bot.Id, bot.Team, use, bot.LastPosition, health.LastHitTick, now);
                else rules.CancelHold(bot.Id);
            }
        }

        private static readonly Unity.Profiling.ProfilerMarker s_ClassicBotProfile = new("Moonkov.ClassicBots");

        private static Entity MatchActor(EntityManager manager, Entity connection) =>
            manager.HasComponent<JoinedClient>(connection) ? manager.GetComponentData<JoinedClient>(connection).PlayerEntity : Entity.Null;

        private static void SetClassicActorActive(EntityManager manager, Entity actor, bool active)
        {
            if (!manager.HasComponent<ClashPlayer>(actor) || !manager.HasComponent<PredictedPlayerGhost>(actor)) return;
            var id = manager.GetComponentData<ClashPlayer>(actor);
            var health = manager.GetComponentData<PredictedPlayerGhost>(actor);
            var life = health.CurrentHealth <= 0 ? ClashLife.Dead : active ? ClashLife.Alive : ClashLife.Waiting;
            bool canAct = life == ClashLife.Alive;
            if (id.CanAct == canAct && health.ClashLife == life) return;
            id.CanAct = health.ClashCanAct = canAct;
            health.ClashLife = life;
            if (!canAct) health.ControllerState.Shoot = false;
            manager.SetComponentData(actor, id);
            manager.SetComponentData(actor, health);
        }

        private void StartClassicRound(ref SystemState state, EntityCommandBuffer ecb, ArenaMatch match, ClassicDust2Map map,
            DollSingerEnemySystem ai, double now)
        {
            var manager = state.EntityManager;
            var rules = match.Classic;
            bool halftime = rules.Round == ClassicMatch.Half;
            int attackTeam = rules.Round < ClassicMatch.Half ? 0 : 1;
            int carrierSlot = (rules.Round + 1) % ClassicMatch.TeamSize;
            var assigned = match.Players.Values.FirstOrDefault(player => player.Team == attackTeam && player.Slot == carrierSlot);
            int carrier = assigned?.Id ?? BotId(attackTeam, carrierSlot);
            rules.BeginRound(now, carrier);
            var prefabs = SystemAPI.GetSingleton<PlayerEntityPrefabs>();
            int attackCharacter = prefabs.HatsuneMikuEntityPrefab != Entity.Null
                ? GameSettings.HatsuneMikuCharacterIndex : GameSettings.DollSingerCharacterIndex;
            int CharacterForTeam(int team) => team == attackTeam ? attackCharacter : GameSettings.DollSingerCharacterIndex;
            match.ClassicTactics.BeginRound(rules.Round, carrierSlot);
            foreach (var bot in match.Bots.Values)
            {
                if (manager.Exists(bot.Input)) ecb.DestroyEntity(bot.Input);
                if (manager.Exists(bot.Actor)) ecb.DestroyEntity(bot.Actor);
            }
            match.Bots.Clear();
            var occupied = new bool[ClassicMatch.Capacity];

            foreach (var (connection, seat) in match.Players)
            {
                seat.UseUntil = 0;
                if (halftime) seat.Credits = 800;
                occupied[seat.Team * ClassicMatch.TeamSize + seat.Slot] = true;
                var spawn = map.Spawn(seat.Team == attackTeam, seat.Slot);
                seat.LastPosition = spawn.Position;
                var oldActor = MatchActor(manager, connection);
                bool retain = !halftime && TryGetLivingPosition(ref state, oldActor, out _);
                int previousVersion = seat.Spawned ? GetRaidInventory(ref state, connection).Graph.Version : 0;
                if (manager.Exists(oldActor)) RemoveArenaActor(ref state, ecb, connection, oldActor);
                if (!retain)
                {
                    var kit = ClassicMatch.StartingKit();
                    kit.Version = System.Math.Max(kit.Version, previousVersion + 1);
                    SetRaidInventory(ref state, connection, kit, 1);
                }
                if (!seat.Spawned) AddPlayerToLeaderboard(manager.GetComponentData<NetworkId>(connection).Value, seat.Name);
                SpawnPlayerCharacter(ref state, ecb, connection, seat.Name, CharacterForTeam(seat.Team), seat, spawn);
                seat.Spawned = true;
            }
            for (int team = 0; team < 2; team++)
                for (int slot = 0; slot < ClassicMatch.TeamSize; slot++)
                {
                    if (occupied[team * ClassicMatch.TeamSize + slot]) continue;
                    int id = BotId(team, slot);
                    var bot = ai.SpawnClassicBot(map.Spawn(team == attackTeam, slot), team, slot, id, rules.Round, now,
                        CharacterForTeam(team));
                    if (bot != null) match.Bots.Add(id, bot);
                }
        }

        private static int BotId(int team, int slot) => -(1 + team * ClassicMatch.TeamSize + slot);

        private static ClassicUse ClassicOpportunity(ClassicDust2Map map, ClassicMatch rules, int actor, int team, Vector3 position)
        {
            if (rules.Phase == ClassicPhase.Live && team == rules.AttackTeam)
            {
                if (rules.Carrier == actor)
                {
                    if (map.InSite(0, position)) return ClassicUse.PlantA;
                    if (map.InSite(1, position)) return ClassicUse.PlantB;
                }
                if (rules.BombOnGround && CanReachBomb(position, rules.BombPosition)) return ClassicUse.PickUp;
            }
            if (rules.Phase == ClassicPhase.Planted && team != rules.AttackTeam && CanReachBomb(position, rules.BombPosition))
                return ClassicUse.Defuse;
            if (rules.Phase is ClassicPhase.Buy or ClassicPhase.Live)
            {
                var available = rules.CacheMask(actor);
                for (int cache = 0; cache < 3; cache++)
                {
                    var cachePosition = map.SupplyCachePosition(cache) - Vector3.up * .35f;
                    if ((available & (1 << cache)) != 0 && CanReachBomb(position, cachePosition))
                        return cache switch { 0 => ClassicUse.CacheA, 1 => ClassicUse.CacheMid, _ => ClassicUse.CacheB };
                }
            }
            return ClassicUse.None;
        }

        private static bool CanReachBomb(Vector3 position, Vector3 bomb) => (position - bomb).sqrMagnitude <= 2.2f * 2.2f &&
            !UnityEngine.Physics.Linecast(position + Vector3.up * 1.2f, bomb + Vector3.up * .24f, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
    }
}
