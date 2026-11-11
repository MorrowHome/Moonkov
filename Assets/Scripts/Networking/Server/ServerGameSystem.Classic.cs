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
            bool live = rules.Phase is ClassicPhase.Live or ClassicPhase.Planted;
            foreach (var (connection, seat) in match.Players)
            {
                var actor = MatchActor(manager, connection);
                if (!TryGetLivingPosition(ref state, actor, out var position))
                {
                    if (live)
                    {
                        rules.DropBomb(seat.Id, seat.LastPosition);
                        if (manager.HasComponent<PredictedPlayerGhost>(actor)) RemoveArenaActor(ref state, ecb, connection, actor);
                    }
                    rules.CancelHold(seat.Id);
                    continue;
                }
                seat.LastPosition = position;
                if (seat.Team == 0) aliveA++; else aliveB++;
                if (!live || now >= seat.UseUntil) { rules.CancelHold(seat.Id); continue; }
                var use = ClassicOpportunity(map, rules, seat.Id, seat.Team, position);
                rules.Use(seat.Id, seat.Team, use, position, manager.GetComponentData<PredictedPlayerGhost>(actor).LastHitTick, now);
            }
            foreach (var bot in match.Bots.Values)
            {
                if (!TryGetLivingPosition(ref state, bot.Actor, out var position))
                {
                    if (live) rules.DropBomb(bot.Id, bot.LastPosition);
                    rules.CancelHold(bot.Id);
                    continue;
                }
                bot.LastPosition = position;
                if (bot.Team == 0) aliveA++; else aliveB++;
                if (!live) continue;
                var use = ClassicOpportunity(map, rules, bot.Id, bot.Team, position);
                if (use != ClassicUse.None)
                    rules.Use(bot.Id, bot.Team, use, position, manager.GetComponentData<PredictedPlayerGhost>(bot.Actor).LastHitTick, now);
                bot.MoveAndFight(manager, match, map, now, SystemAPI.Time.DeltaTime, rules.HoldActor == bot.Id);
            }

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
            foreach (var (connection, seat) in match.Players)
            {
                if (!manager.HasComponent<RaidSession>(connection)) continue;
                var actor = MatchActor(manager, connection);
                bool alive = TryGetLivingPosition(ref state, actor, out var position);
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new ClassicSnapshotRpc
                {
                    Phase = rules.Phase, Round = rules.Round, Team = seat.Team, AttackTeam = rules.AttackTeam,
                    ScoreA = rules.Score[0], ScoreB = rules.Score[1], AliveA = aliveA, AliveB = aliveB,
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
            var assigned = match.Players.Values.FirstOrDefault(player => player.Team == attackTeam);
            int carrier = assigned?.Id ?? BotId(attackTeam, 0);
            rules.BeginRound(now, carrier);
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
                SpawnPlayerCharacter(ref state, ecb, connection, seat.Name, seat.Character, seat, spawn);
                seat.Spawned = true;
            }
            for (int team = 0; team < 2; team++)
                for (int slot = 0; slot < ClassicMatch.TeamSize; slot++)
                {
                    if (occupied[team * ClassicMatch.TeamSize + slot]) continue;
                    int id = BotId(team, slot);
                    var bot = ai.SpawnClassicBot(map.Spawn(team == attackTeam, slot), team, slot, id, rules.Round, now);
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
            return ClassicUse.None;
        }

        private static bool CanReachBomb(Vector3 position, Vector3 bomb) => (position - bomb).sqrMagnitude <= 2.2f * 2.2f &&
            !UnityEngine.Physics.Linecast(position + Vector3.up * 1.2f, bomb + Vector3.up * .24f, LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore);
    }
}
