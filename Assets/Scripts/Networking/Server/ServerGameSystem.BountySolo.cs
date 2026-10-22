using System.Linq;
using Gameplay.Leaderboard;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private ClashBotRegistry ClashBots(ref SystemState state) => state.EntityManager.GetComponentObject<ClashBotRegistry>(SystemAPI.GetSingletonEntity<RaidLootWorld>());

        private bool CanRestartClash(ref SystemState state, BountyClashWorld world, ClashParticipant local)
        {
            var context = Persistence(ref state);
            if (context.Saves.Count > 0 || context.Deployments.Count > 0) return false;
            if (!world.Match.IsSolo) return world.Match.Phase == ClashPhase.Finished;
            if (!local.IsFinished) return false;
            if (world.Connections.TryGetValue(local.Id, out var connection) && SystemAPI.HasComponent<RaidSession>(connection))
            {
                var session = SystemAPI.GetComponent<RaidSession>(connection);
                return session.Phase != RaidPhase.Active && session.SaveState == RaidSaveState.Saved;
            }
            return world.Match.DeploymentAborted;
        }
        private void SetupSoloRoster(ref SystemState state, BountyClashWorld world, ClashParticipant human)
        {
            var match = world.Match;
            if (!match.IsSolo || human.IsBot || human.TeamId >= 0) return;
            match.CreateTeam(human, 1);
            for (int team = 0; team < 3; team++)
            {
                var leader = match.JoinBot("月影 " + (team + 1) + "-1");
                match.CreateTeam(leader, 2);
                var wing = match.JoinBot("月影 " + (team + 1) + "-2");
                match.JoinTeam(wing, match.Teams[leader.TeamId].Invite);
            }
            foreach (var bot in match.Players.Values.Where(p => p.IsBot))
            {
                // A bot owns no transport connection. Distinct nonzero owners also preserve
                // the existing weapon/VFX self filters, while damage uses participant+epoch.
                bot.NetworkId = world.NextBotOwner--;
                match.Ready(bot, true, SystemAPI.Time.ElapsedTime);
            }
        }
        private void QueueSoloSpawns(ref SystemState state, BountyClashWorld world)
        {
            if (!world.Match.IsSolo) return;
            var bots = ClashBots(ref state);
            if (!bots.NavigationReady) return;
            foreach (var p in world.Match.Players.Values)
            {
                if (!p.IsBot || p.Deployed || bots.Actors.ContainsKey(p.Id) || bots.PendingSpawns.Any(r => r.ParticipantId == p.Id)) continue;
                bots.PendingSpawns.Add(new ClashBotSpawnRequest { ParticipantId = p.Id, TeamId = p.TeamId, Slot = p.Slot,
                    Epoch = world.Epoch, OwnerNetworkId = p.NetworkId, Name = p.Name,
                    Position = ClashVector(world.Match.Arena.Spawns[p.TeamId * 3 + p.Slot]) });
            }
        }
        private bool ValidSoloActor(ref SystemState state, BountyClashWorld world, ClashParticipant p, Entity actor)
        {
            if (!SystemAPI.Exists(actor) || !SystemAPI.HasComponent<ClashBot>(actor) || !SystemAPI.HasComponent<ClashPlayer>(actor) ||
                !SystemAPI.HasComponent<GhostOwner>(actor) || !SystemAPI.HasComponent<PredictedPlayerGhost>(actor) ||
                !SystemAPI.HasComponent<LocalTransform>(actor)) return false;
            var registry = ClashBots(ref state);
            if (!registry.Actors.TryGetValue(p.Id, out var registered) || !SystemAPI.Exists(registered.Input) ||
                !SystemAPI.HasComponent<PredictedClientInput>(registered.Input) || !SystemAPI.HasComponent<PlayerClientCommandInputLookup>(actor) ||
                SystemAPI.GetComponent<PlayerClientCommandInputLookup>(actor).ClientCommandInputEntity != registered.Input) return false;
            var bot = SystemAPI.GetComponent<ClashBot>(actor);
            var tag = SystemAPI.GetComponent<ClashPlayer>(actor);
            return bot.ParticipantId == p.Id && bot.TeamId == p.TeamId && bot.Slot == p.Slot && bot.Epoch == world.Epoch &&
                tag.ParticipantId == p.Id && tag.TeamId == p.TeamId && tag.Epoch == world.Epoch &&
                SystemAPI.GetComponent<GhostOwner>(actor).NetworkId == p.NetworkId;
        }
        private void HandleSoloIntents(ref SystemState state, BountyClashWorld world, double now)
        {
            if (!world.Match.IsSolo) return;
            var bots = ClashBots(ref state);
            while (bots.Intents.Count > 0)
            {
                var request = bots.Intents.Dequeue();
                if (request.Epoch != world.Epoch || !world.Match.Players.TryGetValue(request.ParticipantId, out var p) || !p.IsBot ||
                    !bots.Actors.TryGetValue(p.Id, out var actor) || request.Sequence <= actor.LastIntent ||
                    !ValidSoloActor(ref state, world, p, actor.Entity)) continue;
                actor.LastIntent = request.Sequence;
                var match = world.Match;
                if (!match.Active(p)) continue;
                switch (request.Intent)
                {
                    case ClashIntent.Banish:
                        match.Banish(p, ClashAccessible(ref state, actor.Entity, match.Arena.Target), now); break;
                    case ClashIntent.Pickup:
                        match.Pickup(p, ClashAccessible(ref state, actor.Entity, match.BountyPosition)); break;
                    case ClashIntent.ReviveBegin:
                        if (match.ReviveTarget(p.Id) == request.TargetParticipantId)
                            match.RenewRevive(p, match.Players.TryGetValue(request.TargetParticipantId, out var renewal) && ClashAccessible(ref state, actor.Entity, renewal.Position), now);
                        else
                            match.BeginRevive(p, request.TargetParticipantId, match.Players.TryGetValue(request.TargetParticipantId, out var target) && ClashAccessible(ref state, actor.Entity, target.Position), now);
                        break;
                    case ClashIntent.ReviveRenew:
                        int id = match.ReviveTarget(p.Id);
                        match.RenewRevive(p, id >= 0 && match.Players.TryGetValue(id, out var victim) && ClashAccessible(ref state, actor.Entity, victim.Position), now); break;
                    case ClashIntent.ReviveCancel: match.CancelRevive(p); break;
                }
            }
        }
        private void UpdateSoloViews(ref SystemState state, BountyClashWorld world, double now)
        {
            if (!world.Match.IsSolo) return;
            var match = world.Match;
            var bots = ClashBots(ref state);
            foreach (var p in match.Players.Values)
            {
                if (!p.IsBot) continue;
                bool own = match.CarrierId >= 0 && match.Players[match.CarrierId].TeamId == p.TeamId;
                var bounty = match.Bounty == ClashBountyState.Carried ? match.Players[match.CarrierId].Position : match.BountyPosition;
                if (!own && match.Bounty == ClashBountyState.Carried)
                    bounty = new ClashPoint((float)System.Math.Round(bounty.X / 25) * 25, bounty.Y, (float)System.Math.Round(bounty.Z / 25) * 25);
                if (!bots.Views.TryGetValue(p.Id, out var view))
                { view = new ClashBotView(); bots.Views.Add(p.Id, view); }
                view.ParticipantId = p.Id; view.TeamId = p.TeamId; view.Epoch = match.Epoch;
                view.Phase = match.Phase; view.Life = p.Life; view.Health = p.Health; view.Bounty = match.Bounty;
                view.IsCarrier = match.CarrierId == p.Id; view.TeamHasBounty = own;
                view.ArenaCenter = ClashVector(match.Arena.Center); view.ArenaRadius = match.Arena.Radius;
                view.Target = ClashVector(match.Arena.Target); view.BountyPosition = ClashVector(bounty); view.BanishProgress = match.BanishProgress(now);
                view.ExitA = ClashVector(match.Arena.Exits[0]); view.ExitB = ClashVector(match.Arena.Exits[1]);
                view.ExitRadius = match.Arena.ExitRadius; view.BlockRadius = match.Arena.BlockRadius; view.ServerNow = now; view.Deadline = match.Deadline;
                view.ExitBlockA = match.ExitBlock(p.TeamId, 0); view.ExitBlockB = match.ExitBlock(p.TeamId, 1);
                view.ExitRemainingA = match.ExitRemaining(p.TeamId, 0, now); view.ExitRemainingB = match.ExitRemaining(p.TeamId, 1, now);
                view.ReviveTarget = match.ReviveTarget(p.Id);
                var team = match.Teams[p.TeamId];
                if (view.Teammates.Length != team.Members.Count - 1) view.Teammates = new ClashBotTeammate[team.Members.Count - 1];
                int index = 0;
                foreach (var id in team.Members)
                {
                    if (id == p.Id) continue;
                    var ally = match.Players[id];
                    view.Teammates[index++] = new ClashBotTeammate { ParticipantId = ally.Id, Life = ally.Life,
                        Position = ClashVector(ally.Position), Health = ally.Health, ReviveProgress = match.ReviveProgress(ally.Id, now) };
                }
            }
        }
        private void RetireSoloBot(ref SystemState state, EntityCommandBuffer ecb, BountyClashWorld world, ClashParticipant p, bool drop)
        {
            var bots = ClashBots(ref state);
            bots.PendingSpawns.RemoveAll(r => r.ParticipantId == p.Id);
            if (!bots.Actors.TryGetValue(p.Id, out var actor)) return;
            if (SystemAPI.Exists(actor.Entity))
            {
                if (drop && SystemAPI.HasComponent<LocalTransform>(actor.Entity))
                {
                    var pose = SystemAPI.GetComponent<LocalTransform>(actor.Entity);
                    var position = pose.Position;
                    var rotation = pose.Rotation;
                    if (UnityEngine.Physics.Raycast((Vector3)position + Vector3.up, Vector3.down, out var ground, 4,
                        LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore))
                    { position = ground.point; rotation = Quaternion.FromToRotation(Vector3.up, ground.normal) * (Quaternion)pose.Rotation; }
                    if(SystemAPI.HasComponent<PredictedPlayerGhost>(actor.Entity))
                    {
                        var health=SystemAPI.GetComponentRW<PredictedPlayerGhost>(actor.Entity);
                        EnergyShieldEquipment.Flush(ref health.ValueRW,actor.Inventory);
                    }
                    LootContainers(ref state).Drop(actor.Inventory, position, rotation, GameSettings.DollSingerCharacterIndex, SystemAPI.Time.ElapsedTime);
                }
                ecb.DestroyEntity(actor.Entity);
            }
            if (SystemAPI.Exists(actor.Input)) ecb.DestroyEntity(actor.Input);
            LeaderboardManager.RetireRaidActor(p.NetworkId);
            bots.Actors.Remove(p.Id);
        }
        private void ResetSoloBots(ref SystemState state, EntityCommandBuffer ecb, BountyClashWorld world, int epoch)
        {
            var bots = ClashBots(ref state);
            foreach (var p in world.Match.Players.Values.Where(p => p.IsBot)) RetireSoloBot(ref state, ecb, world, p, false);
            bots.Reset(epoch);
        }
    }
}
