using System;
using Math = System.Math;
using Gameplay.Leaderboard;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed class BountyClashWorld : IComponentData
    {
        public BountyClashMatch Match;
        public int Epoch;
        public int NextBotOwner=-100000;
        public uint Sequence;
        public double NextSnapshot;
        public bool Registered, AwardQueued, Healed, Cleaned;
        public readonly Dictionary<int,Entity> Connections=new Dictionary<int,Entity>();
        public readonly Dictionary<int,RaidPersistenceContext.Join> Joins=new Dictionary<int,RaidPersistenceContext.Join>();
        internal readonly Dictionary<int,(uint Hit,uint Shield,uint Revision)> DamageEvents=new Dictionary<int,(uint,uint,uint)>();
        public readonly Dictionary<int,ClashLife> AppliedLife=new Dictionary<int,ClashLife>();
        public readonly HashSet<int> Settled=new HashSet<int>(), DeployRequested=new HashSet<int>();
    }
    public struct ClashDetached : IComponentData { public int ParticipantId; }

    public partial struct ServerGameSystem
    {
        private bool ClashEnabled(ref SystemState state)=>BountyClashRuntime.Enabled(state.EntityManager);
        private BountyClashWorld ClashWorld(ref SystemState state)
        {
            var entity=SystemAPI.GetSingletonEntity<RaidLootWorld>();
            var world=state.EntityManager.GetComponentObject<BountyClashWorld>(entity);
            if(world.Match==null && BountyClashArenaSettings.Active)
            {
                var arena=BountyClashArenaSettings.Active.Definition;
                if(arena!=null&&arena.Validate()==null)
                {
                    world.Match=new BountyClashMatch(arena,++world.Epoch,SystemAPI.Time.ElapsedTime,ClashSoloRuntime.Enabled(state.EntityManager));
                    ClashBots(ref state).Reset(world.Epoch);
                }
            }
            return world;
        }
        private static ClashPoint ClashPointOf(float3 point)=>new ClashPoint(point.x,point.y,point.z);
        private static float3 ClashVector(ClashPoint p)=>new float3(p.X,p.Y,p.Z);
        private static Vector3 ClashUnity(ClashPoint p)=>new Vector3(p.X,p.Y,p.Z);
        private void ClashAccepted(EntityCommandBuffer ecb,Entity connection,bool accepted,string reason)
        {
            var rpc=ecb.CreateEntity();ecb.AddComponent(rpc,new ClashJoinAcceptedRpc{Mode=RaidGameMode.BountyClash,RulesVersion=BountyClashRules.Version,Accepted=accepted,Reason=reason});
            ecb.AddComponent(rpc,new SendRpcCommandRequest{TargetConnection=connection});
            if(!accepted)
            {
                Debug.LogWarning("[Bounty Clash] Join rejected: "+reason);
                ecb.AddComponent(connection,new NetworkStreamRequestDisconnect{Reason=NetworkStreamDisconnectReason.ConnectionClose});
            }
        }
        // Called by the persistence adapter only after authenticated profile loading; never trusts a client team id.
        private void CompleteClashJoin(ref SystemState state,EntityCommandBuffer ecb,Entity connection,RaidPersistenceContext.Join join)
        {
            var context=Persistence(ref state);var world=ClashWorld(ref state);var match=world.Match;
            if(!context.ClashSupported || match==null){ClashAccepted(ecb,connection,false,"赏金速战后端或地图未就绪");return;}
            var profile=context.Profiles[connection];
            var previous=match.Account(profile.PlayerId);double retainedDeadline=previous?.DisconnectDeadline??0;
            var p=match.Join(profile.PlayerId,join.Name.ToString(),SystemAPI.Time.ElapsedTime);
            if(p==null){ClashAccepted(ecb,connection,false,"回合已锁定、身份在线或房间已满");return;}
            int oldOwner=p.NetworkId;int newOwner=SystemAPI.GetComponent<NetworkId>(connection).Value;
            if(world.Connections.TryGetValue(p.Id,out var old)&&state.EntityManager.Exists(old)&&old!=connection&&state.EntityManager.HasComponent<ClashDetached>(old))
            {
                var session=SystemAPI.GetComponent<RaidSession>(old);var oldJoined=SystemAPI.GetComponent<JoinedClient>(old);
                if(!context.RebindBountyOwner(old,connection,profile.PlayerId,session.SettlementId.ToString()))
                {
                    p.Connected=false;p.DisconnectDeadline=retainedDeadline;
                    ClashAccepted(ecb,connection,false,"无法恢复原结算归属，请重试");return;
                }
                p.NetworkId=newOwner;
                ecb.AddComponent(connection,session);ecb.AddComponent(connection,oldJoined);
                if(state.EntityManager.HasComponent<RaidInventoryState>(old))
                    state.EntityManager.AddComponentObject(connection,state.EntityManager.GetComponentObject<RaidInventoryState>(old));
                if(state.EntityManager.Exists(oldJoined.PlayerEntity))
                {
                    var player=oldJoined.PlayerEntity;var prefabs=SystemAPI.GetSingleton<PlayerEntityPrefabs>();
                    var input=ecb.Instantiate(prefabs.ClientInputEntityPrefab);ecb.SetComponent(input,new GhostOwner{NetworkId=p.NetworkId});
                    ecb.AddBuffer<ClientCommandInput>(input);ecb.SetComponent(input,new PlayerCommandTarget{NetworkId=p.NetworkId});
                    ecb.SetComponent(player,new GhostOwner{NetworkId=p.NetworkId});ecb.SetComponent(player,new PlayerClientCommandInputLookup{ClientCommandInputEntity=input});
                    ecb.SetComponent(connection,new CommandTarget{targetEntity=input});
                    ecb.AppendToBuffer(connection,new LinkedEntityGroup{Value=player});
                    var clients=SystemAPI.GetSingletonBuffer<ClientsMap>();clients.ElementAt(p.NetworkId).PlayerEntity=player;
                }
                ecb.DestroyEntity(old);RemovePlayerFromLeaderboard(oldOwner);AddPlayerToLeaderboard(p.NetworkId,join.Name);
            }
            else {p.NetworkId=newOwner;ecb.AddComponent(connection,new JoinedClient{PlayerName=join.Name,CharacterIndex=join.CharacterIndex,PlayerEntity=Entity.Null});}
            world.Connections[p.Id]=connection;world.Joins[p.Id]=join;
            SetupSoloRoster(ref state,world,p);
            if(!SystemAPI.HasComponent<NetworkStreamInGame>(connection))ecb.AddComponent(connection,new NetworkStreamInGame());
            ClashAccepted(ecb,connection,true,"");world.NextSnapshot=0;
        }
        // Late successful HTTP deployment after a bounded lobby abort must be refunded by the persistence adapter.
        private bool ClashDeploymentAllowed(ref SystemState state,Entity connection,string deploymentId)
        {
            if(!ClashEnabled(ref state))return false;
            var world=ClashWorld(ref state);var match=world.Match;
            return match!=null&&match.Phase==ClashPhase.Deploying&&world.Connections.Any(pair=>pair.Value==connection&&match.Players.TryGetValue(pair.Key,out var p)&&p.DeploymentId==deploymentId);
        }
        // NetCode destroys the connection's LinkedEntityGroup at the end of
        // NetworkReceiveSystemGroup. Retain the body before that ECB plays back.
        public void ClashDisconnectBeforeCleanup(ref SystemState state)
        {
            if(!ClashEnabled(ref state))return;
            var world=ClashWorld(ref state);
            using var commands=new EntityCommandBuffer(Allocator.Temp);
            // ConnectionEventsForTick is only published after NetCode cleanup.
            // The public connection state is already updated by the receive job.
            foreach(var pair in world.Connections.ToArray())
            {
                var connection=pair.Value;
                if(SystemAPI.Exists(connection)&&SystemAPI.HasComponent<NetworkStreamConnection>(connection)&&SystemAPI.HasComponent<NetworkId>(connection)&&
                    SystemAPI.GetComponent<NetworkStreamConnection>(connection).CurrentState==ConnectionState.State.Disconnected)
                    DetachClashConnection(ref state,commands,connection,SystemAPI.GetComponent<NetworkId>(connection).Value);
            }
            commands.Playback(state.EntityManager);
        }
        private bool DetachClashConnection(ref SystemState state,EntityCommandBuffer ecb,Entity connection,int networkId)
        {
            if(!ClashEnabled(ref state))return false;
            var world=ClashWorld(ref state);var match=world.Match;
            var pair=world.Connections.FirstOrDefault(x=>x.Value==connection);
            if(match==null||pair.Key==0||!match.Players.TryGetValue(pair.Key,out var p))return false;
            match.Disconnect(p,SystemAPI.Time.ElapsedTime);
            if(!SystemAPI.HasComponent<RaidSession>(connection)){world.Connections.Remove(p.Id);return true;}
            var manager=state.EntityManager;var holder=manager.CreateEntity();
            manager.AddComponentData(holder,new ClashDetached{ParticipantId=p.Id});
            manager.AddComponentData(holder,SystemAPI.GetComponent<NetworkId>(connection));
            manager.AddComponentData(holder,SystemAPI.GetComponent<RaidSession>(connection));
            manager.AddComponentData(holder,SystemAPI.GetComponent<JoinedClient>(connection));
            manager.AddComponentData(holder,new NetworkStreamRequestDisconnect());
            var context=Persistence(ref state);
            if(!context.RebindBountyOwner(connection,holder,p.Account,manager.GetComponentData<RaidSession>(holder).SettlementId.ToString()))
            {
                manager.DestroyEntity(holder);
                throw new InvalidOperationException("Bounty Clash owner transfer rejected for participant "+p.Id);
            }
            if(context.Inventories.TryGetValue(holder,out var inventory))manager.AddComponentObject(holder,inventory);
            var joined=SystemAPI.GetComponent<JoinedClient>(connection);
            if(manager.HasBuffer<LinkedEntityGroup>(connection))
            {
                var links=manager.GetBuffer<LinkedEntityGroup>(connection);
                for(int i=links.Length-1;i>=0;i--)if(links[i].Value==joined.PlayerEntity)links.RemoveAt(i);
            }
            if(SystemAPI.Exists(joined.PlayerEntity)&&SystemAPI.HasComponent<PlayerClientCommandInputLookup>(joined.PlayerEntity))
            {
                var input=SystemAPI.GetComponent<PlayerClientCommandInputLookup>(joined.PlayerEntity).ClientCommandInputEntity;
                if(SystemAPI.Exists(input))ecb.DestroyEntity(input);
                ecb.SetComponent(joined.PlayerEntity,new PlayerClientCommandInputLookup{ClientCommandInputEntity=Entity.Null});
            }
            if(SystemAPI.Exists(joined.PlayerEntity)&&SystemAPI.HasComponent<GhostOwner>(joined.PlayerEntity))
                ecb.SetComponent(joined.PlayerEntity,new GhostOwner{NetworkId=0});
            p.NetworkId=0;RemovePlayerFromLeaderboard(networkId);
            world.Connections[p.Id]=holder;SystemAPI.GetSingletonBuffer<ClientsMap>().ElementAt(networkId)=default;return true;
        }
        private void ConfigureClashSpawn(ref SystemState state,EntityCommandBuffer ecb,Entity connection,Entity player,ref PredictedPlayerGhost health)
        {
            if(!ClashEnabled(ref state))return;
            var world=ClashWorld(ref state);var pair=world.Connections.FirstOrDefault(x=>x.Value==connection);
            if(world.Match==null||pair.Key==0)return;
            var p=world.Match.Players[pair.Key];
            ecb.AddComponent(player,new ClashPlayer{ParticipantId=p.Id,TeamId=p.TeamId,Epoch=world.Match.Epoch,CanAct=false});
            health.ClashEnabled=true;health.ClashTeam=p.TeamId;health.ClashLife=ClashLife.Waiting;health.ClashMaxHealthFraction=1;health.ClashCanAct=false;health.CurrentHealth=0;
            var point=world.Match.Arena.Spawns[p.TeamId*3+p.Slot];
            ecb.SetComponent(player,LocalTransform.FromPosition(ClashVector(point)));
        }
        private bool ClashAccessible(ref SystemState state,Entity player,ClashPoint target,float radius=3)
        {
            if(!TryGetLivingPosition(ref state,player,out var position))return false;
            return math.distancesq(position,ClashVector(target))<=radius*radius && !UnityEngine.Physics.Linecast((Vector3)position+Vector3.up*1.3f,
                ClashUnity(target)+Vector3.up*.4f,LayerMask.GetMask("Default","Ground"),QueryTriggerInteraction.Ignore);
        }
        private Entity ClashPlayerEntity(ref SystemState state,BountyClashWorld world,int id)
        {
            if(world.Match!=null&&world.Match.IsSolo&&ClashBots(ref state).Actors.TryGetValue(id,out var actor))return actor.Entity;
            return world.Connections.TryGetValue(id,out var c)&&SystemAPI.Exists(c)&&SystemAPI.HasComponent<JoinedClient>(c)?SystemAPI.GetComponent<JoinedClient>(c).PlayerEntity:Entity.Null;
        }
        private static uint ClashDamageRevision(BountyClashWorld world,int participant,PredictedPlayerGhost health)
        {
            if(!world.DamageEvents.TryGetValue(participant,out var observed))observed=(health.LastHitTick,health.ShieldHitSequence,0);
            else if(observed.Hit!=health.LastHitTick||observed.Shield!=health.ShieldHitSequence)
                observed=(health.LastHitTick,health.ShieldHitSequence,observed.Revision+1);
            world.DamageEvents[participant]=observed;return observed.Revision;
        }
        private void HandleBountyClash(ref SystemState state,EntityCommandBuffer ecb)
        {
            if(!ClashEnabled(ref state))return;
            var world=ClashWorld(ref state);var match=world.Match;if(match==null)return;
            double now=SystemAPI.Time.ElapsedTime;
            var context=Persistence(ref state);
            foreach(var p in match.Players.Values)
            {
                Entity player=ClashPlayerEntity(ref state,world,p.Id);
                if(!SystemAPI.Exists(player)||!SystemAPI.HasComponent<PredictedPlayerGhost>(player)||!SystemAPI.HasComponent<LocalTransform>(player))continue;
                var h=SystemAPI.GetComponent<PredictedPlayerGhost>(player);
                if(match.Phase==ClashPhase.Deploying&&(!p.IsBot||ClashBots(ref state).NavigationReady&&ValidSoloActor(ref state,world,p,player)))match.MarkDeployed(p.Id);
                match.Observe(p.Id,ClashPointOf(SystemAPI.GetComponent<LocalTransform>(player).Position),h.CurrentHealth,ClashDamageRevision(world,p.Id,h));
                if(match.Active(p)&&h.CurrentHealth<=0)
                {int killer=SystemAPI.HasComponent<ClashPlayer>(player)?SystemAPI.GetComponent<ClashPlayer>(player).LastAttacker:0;match.Down(p,killer);}
                int revive=match.ReviveTarget(p.Id);
                if(revive>=0&&match.Players.TryGetValue(revive,out var victim)&&!ClashAccessible(ref state,player,victim.Position))match.CancelRevive(p);
            }
            foreach(var (request,received,entity) in SystemAPI.Query<RefRO<ClashActionRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var r=request.ValueRO;Entity connection=received.ValueRO.SourceConnection;
                var pair=world.Connections.FirstOrDefault(x=>x.Value==connection);
                if(pair.Key!=0&&match.Players.TryGetValue(pair.Key,out var p)&&match.Request(p,r.RequestId,r.Epoch,r.MatchId.ToString()))
                {
                    var player=ClashPlayerEntity(ref state,world,p.Id);var result=ClashResult.Ok;
                    bool forbiddenSolo=match.IsSolo&&(r.Intent==ClashIntent.CreateTeam||r.Intent==ClashIntent.JoinTeam||r.Intent==ClashIntent.LeaveTeam||r.Intent==ClashIntent.SetCapacity);
                    if(forbiddenSolo)result=ClashResult.Forbidden;
                    else switch(r.Intent)
                    {
                        case ClashIntent.CreateTeam:result=match.CreateTeam(p,r.Value);break;
                        case ClashIntent.JoinTeam:result=match.JoinTeam(p,r.InviteCode.ToString());break;
                        case ClashIntent.LeaveTeam:result=match.LeaveTeam(p);break;
                        case ClashIntent.SetCapacity:result=match.Capacity(p,r.Value);break;
                        case ClashIntent.SetReady:result=match.IsSolo&&r.Value!=0&&!ClashBots(ref state).NavigationReady?ClashResult.Busy:match.Ready(p,r.Value!=0,now);break;
                        case ClashIntent.Banish:result=match.Banish(p,ClashAccessible(ref state,player,match.Arena.Target),now);break;
                        case ClashIntent.Pickup:result=match.Pickup(p,ClashAccessible(ref state,player,match.BountyPosition));break;
                        case ClashIntent.ReviveBegin:
                            result=match.BeginRevive(p,r.TargetParticipantId,match.Players.TryGetValue(r.TargetParticipantId,out var v)&&ClashAccessible(ref state,player,v.Position),now);break;
                        case ClashIntent.ReviveRenew:
                            int target=match.ReviveTarget(p.Id);match.RenewRevive(p,target>=0&&match.Players.TryGetValue(target,out var victim)&&ClashAccessible(ref state,player,victim.Position),now);break;
                        case ClashIntent.ReviveCancel:match.CancelRevive(p);break;
                        case ClashIntent.SenseHeld:match.Sense(p,r.Value,now);break;
                        case ClashIntent.NextRound:
                            if(!CanRestartClash(ref state,world,p))result=ClashResult.Busy;
                            else {if(match.IsSolo)match.EndSoloRound();ResetBountyClash(ref state,ecb,world);return;}break;
                        default:result=ClashResult.Invalid;break;
                    }
                    p.Result=result;world.NextSnapshot=0;
                }
                ecb.DestroyEntity(entity);
            }
            HandleSoloIntents(ref state,world,now);
            match.Tick(now);
            foreach(var stale in world.Connections.Keys.Where(id=>!match.Players.ContainsKey(id)).ToArray())
            {
                var connection=world.Connections[stale];
                if(SystemAPI.Exists(connection)&&SystemAPI.HasComponent<ClashDetached>(connection))
                {context.Profiles.Remove(connection);context.Inventories.Remove(connection);ecb.DestroyEntity(connection);}
                world.Connections.Remove(stale);world.Joins.Remove(stale);
            }
            if(match.Phase==ClashPhase.Deploying&&!world.Registered)
            {
                world.Registered=true;
                context.RegisterBountyMatchAsync(Guid.Parse(match.MatchId),BountyClashRules.Version,Guid.Parse(match.TokenId),match.Players.Values.Where(p=>!p.IsBot).Select(p=>new RaidPersistenceContext.BountyParticipant{PlayerId=p.Account,DeploymentId=p.DeploymentId,TeamId=p.TeamId}).ToArray());
            }
            if(match.Phase==ClashPhase.Deploying)QueueSoloSpawns(ref state,world);
            if(match.Phase==ClashPhase.Deploying)
                foreach(var p in match.Players.Values)
                    if(world.Connections.TryGetValue(p.Id,out var c)&&p.Connected&&!world.DeployRequested.Contains(p.Id))
                    {
                        world.DeployRequested.Add(p.Id);var join=world.Joins[p.Id];
                        context.BeginBountyDeploy(c,Guid.Parse(match.MatchId),BountyClashRules.Version,p.TeamId,Guid.Parse(p.DeploymentId),join.CarryCells,join);
                    }
            // Persist the whole award event before any participant is destroyed or its inventory is transferred.
            if(match.Award!=null&&!world.AwardQueued)
            {
                var award=match.Award;
                foreach(var awarded in match.Players.Values)
                    if(!awarded.IsBot&&Array.IndexOf(award.DeploymentIds,awarded.DeploymentId)>=0&&world.Connections.TryGetValue(awarded.Id,out var awardConnection))
                        UpdateInventoryProtection(ref state,awardConnection);
                if(!match.Players.TryGetValue(award.CarrierParticipantId,out var awardCarrier)||!awardCarrier.IsBot)context.BeginBountyRedemption(Guid.Parse(award.MatchId),Guid.Parse(award.TokenId),Guid.Parse(award.EventId),Guid.Parse(award.CarrierDeploymentId),award.DeploymentIds.Select(Guid.Parse).ToArray());world.AwardQueued=true;
            }
            foreach(var p in match.Players.Values)
            {
                Entity player=ClashPlayerEntity(ref state,world,p.Id);
                if(SystemAPI.Exists(player)&&SystemAPI.HasComponent<PredictedPlayerGhost>(player))
                {
                    var h=SystemAPI.GetComponent<PredictedPlayerGhost>(player);
                    bool changed=!world.AppliedLife.TryGetValue(p.Id,out var previous)||previous!=p.Life;
                    bool healed=match.BanishHealed&&!world.Healed&&p.TeamId==match.BanishTeam&&!p.IsFinished;
                    h.ClashEnabled=true;h.ClashTeam=p.TeamId;h.ClashDarkSight=match.Active(p)&&p.Connected&&p.ViewLease>=now;h.ClashLife=p.Life;h.ClashCanAct=match.Active(p)&&p.Connected;h.ClashMaxHealthFraction=p.MaxFraction;
                    if((changed&&p.Life==ClashLife.Alive)||healed)RaidHealth.Initialize(ref h);
                    if(p.Life!=ClashLife.Alive){h.CurrentHealth=0;h.ControllerState.Shoot=false;}
                    if(p.Life==ClashLife.Downed)
                    {
                        h.ControllerState.Stance=Unity.MP_FPS.DollSinger.DollSingerStance.Prone;h.AccumulatedMovement=float3.zero;
                        if(state.EntityManager.HasComponent<PlayerControllerLink>(player))
                            state.EntityManager.GetComponentObject<PlayerControllerLink>(player).Controller?.ApplyStanceCapsule(Unity.MP_FPS.DollSinger.DollSingerStance.Prone);
                    }
                    SystemAPI.SetComponent(player,h);
                    if(SystemAPI.HasComponent<ClashPlayer>(player)){var tag=SystemAPI.GetComponent<ClashPlayer>(player);tag.CanAct=match.Active(p)&&p.Connected;SystemAPI.SetComponent(player,tag);}
                    world.AppliedLife[p.Id]=p.Life;
                }
                if(p.IsBot&&p.IsFinished&&world.Settled.Add(p.Id))
                {
                    if(p.Life==ClashLife.Dead&&p.LastAttacker!=0&&p.LastAttacker!=p.Id&&match.Players.TryGetValue(p.LastAttacker,out var botKiller)&&botKiller.TeamId!=p.TeamId)
                        LeaderboardManager.Instance?.AddKill(botKiller.NetworkId,p.NetworkId);
                    RetireSoloBot(ref state,ecb,world,p,p.Life==ClashLife.Dead);
                }
                if(p.IsFinished&&!world.Settled.Contains(p.Id)&&world.Connections.TryGetValue(p.Id,out var connection)&&SystemAPI.Exists(connection)&&SystemAPI.HasComponent<RaidSession>(connection))
                {
                    world.Settled.Add(p.Id);
                    if(p.Life==ClashLife.Dead&&p.LastAttacker!=0&&p.LastAttacker!=p.Id&&match.Players.TryGetValue(p.LastAttacker,out var killer)&&killer.TeamId!=p.TeamId&&killer.Connected&&p.Connected)
                        LeaderboardManager.Instance?.AddKill(killer.NetworkId,p.NetworkId);
                    FinishRaid(ref state,ecb,connection,match.DeploymentAborted||p.Life==ClashLife.Extracted?RaidPhase.Extracted:p.Life==ClashLife.TimedOut?RaidPhase.TimedOut:RaidPhase.Dead);
                }
            }
            UpdateSoloViews(ref state,world,now);
            if(match.BanishHealed)world.Healed=true;
            if(match.Phase==ClashPhase.Finished&&!world.Cleaned){world.Cleaned=true;ClearTacticalItems(ref state,ecb);}
            if(now>=world.NextSnapshot){world.NextSnapshot=now+.2;SendClashSnapshots(ref state,ecb,world,now);}
        }
        private void ResetBountyClash(ref SystemState state,EntityCommandBuffer ecb,BountyClashWorld world)
        {
            ClearTacticalItems(ref state,ecb);
            var old=world.Match;
            if(old.IsSolo)ResetSoloBots(ref state,ecb,world,world.Epoch+1);
            var next=new BountyClashMatch(old.Arena,++world.Epoch,SystemAPI.Time.ElapsedTime,old.IsSolo);
            world.Match=next;world.Registered=world.AwardQueued=world.Healed=world.Cleaned=false;
            world.Settled.Clear();world.DeployRequested.Clear();world.AppliedLife.Clear();world.DamageEvents.Clear();
            var context=Persistence(ref state);var previousConnections=world.Connections.ToArray();world.Connections.Clear();world.Joins.Clear();
            foreach(var pair in previousConnections)
            {
                if(!old.Players.TryGetValue(pair.Key,out var p)||!SystemAPI.Exists(pair.Value))continue;
                if(!p.Connected)
                {
                    context.Profiles.Remove(pair.Value);context.Inventories.Remove(pair.Value);
                    if(SystemAPI.HasComponent<ClashDetached>(pair.Value))ecb.DestroyEntity(pair.Value);
                    continue;
                }
                var joined=SystemAPI.GetComponent<JoinedClient>(pair.Value);
                var participant=next.Join(p.Account,p.Name,SystemAPI.Time.ElapsedTime);participant.NetworkId=p.NetworkId;
                world.Connections[participant.Id]=pair.Value;
                world.Joins[participant.Id]=new RaidPersistenceContext.Join{Name=joined.PlayerName,CharacterIndex=joined.CharacterIndex,CarryCells=0};
                SetupSoloRoster(ref state,world,participant);

            }
            var loot=LootContainers(ref state);int version=loot.Containers.Count>0?loot.Containers.Values.Max(g=>g.Version)+1:1;
            loot.Containers.Clear();loot.DeathBags.Clear();loot.MissionCaches.Clear();loot.RefillStates.Clear();
            var map=MoonRaidMap.Active;
            for(int i=0;map!=null&&i<map.LootPositions.Length;i++)
            {var graph=RaidLootTable.Create(map.GetLootProfile(i),loot.LootRandom);graph.Version=version;loot.Containers[i]=graph;loot.RefillStates[i]=new RaidLootRefillState(map.GetLootProfile(i));}
            foreach(var inventory in context.Inventories.Values)
            {inventory.CancelActions();inventory.SearchedLoot.Clear();inventory.OpenedLootId=-1;inventory.LastSentVersion=inventory.LastSentLootVersion=-1;inventory.NextDeathBagResync=0;}
            loot.NextRefillCheck=0;world.NextSnapshot=0;
        }
        private void SendClashSnapshots(ref SystemState state,EntityCommandBuffer ecb,BountyClashWorld world,double now)
        {
            var match=world.Match;uint sequence=++world.Sequence;
            foreach(var local in match.Players.Values)
            {
                if(!local.Connected||!world.Connections.TryGetValue(local.Id,out var connection)||!SystemAPI.Exists(connection))continue;
                match.Teams.TryGetValue(local.TeamId,out var team);
                var coarse=match.Bounty==ClashBountyState.Carried?match.Players[match.CarrierId].Position:match.BountyPosition;
                bool own=match.CarrierId>=0&&match.Players[match.CarrierId].TeamId==local.TeamId;
                if(!own&&match.Bounty==ClashBountyState.Carried)coarse=new ClashPoint((float)Math.Round(coarse.X/25)*25,coarse.Y,(float)Math.Round(coarse.Z/25)*25);
                var navigationStatus=new FixedString128Bytes();
                if(match.IsSolo&&!ClashBots(ref state).NavigationReady)navigationStatus.CopyFromTruncated(ClashBots(ref state).NavigationFailed ? "AI opponents could not be prepared. Return to the stash and try again." : "Preparing AI opponents...");
                var snapshot=new ClashSnapshotRpc{IsSolo=match.IsSolo,SoloNavigationReady=!match.IsSolo||ClashBots(ref state).NavigationReady,SoloNavigationStatus=navigationStatus,MatchId=match.MatchId,RulesVersion=BountyClashRules.Version,Epoch=match.Epoch,LocalParticipantId=local.Id,LocalTeamId=local.TeamId,
                    TeamCapacity=team?.Capacity??0,InviteCode=team?.Invite??"",PlayerCount=match.Players.Count,TeamCount=match.Teams.Count,CarrierId=match.CarrierId,Sequence=sequence,
                    AcknowledgedRequest=local.LastRequest,Result=local.Result,Phase=match.Phase,Bounty=match.Bounty,Life=local.Life,ServerNow=now,
                    ArenaCenter=ClashVector(match.Arena.Center),ArenaRadius=match.Arena.Radius,Remaining=(float)Math.Max(0,match.Deadline-now),PrepareRemaining=(float)Math.Max(0,match.PrepareDeadline-now),BanishProgress=match.BanishProgress(now),
                    // The HUD uses a negative value to hide the boundary countdown.
                    BoundaryRemaining=match.Active(local)&&local.OutsideSince>=0?match.BoundaryRemaining(local,now):-1,SenseRemaining=local.SenseRemaining,ReviveProgress=match.ReviveProgress(local.Id,now),
                    ExitRemainingA=match.ExitRemaining(local.TeamId,0,now),ExitRemainingB=match.ExitRemaining(local.TeamId,1,now),ExitBlockA=match.ExitBlock(local.TeamId,0),ExitBlockB=match.ExitBlock(local.TeamId,1),
                    TargetPosition=ClashVector(match.Arena.Target),ExitPositionA=ClashVector(match.Arena.Exits[0]),ExitPositionB=ClashVector(match.Arena.Exits[1]),BountyPosition=ClashVector(coarse),IsCarrier=match.CarrierId==local.Id,TeamHasBounty=own,Connected=local.Connected,DeploymentAborted=match.DeploymentAborted,DeploymentRemaining=(float)Math.Max(0,match.DeploymentDeadline-now),CanStartNext=CanRestartClash(ref state,world,local),IsLeader=team!=null&&team.Leader==local.Id,Closing=match.Bounty==ClashBountyState.Extracted,ReconnectRemaining=local.Connected?0:(float)Math.Max(0,local.DisconnectDeadline-now)};
                SendClashRpc(ecb,connection,snapshot);
                foreach(var p in match.Players.Values)
                {
                    bool teammate=p.TeamId>=0&&p.TeamId==local.TeamId;
                    bool authorized=teammate||p.Id==local.Id;
                    SendClashRpc(ecb,connection,new ClashParticipantRpc{MatchId=match.MatchId,Epoch=match.Epoch,Sequence=sequence,ParticipantId=p.Id,IsBot=p.IsBot,TeamId=authorized?p.TeamId:-1,NetworkId=authorized?p.NetworkId:0,Name=authorized?p.Name:"",
                        Life=authorized?p.Life:ClashLife.Waiting,Ready=authorized&&p.Ready,Connected=authorized&&p.Connected,OwnTeam=teammate,IsLocal=p.Id==local.Id,Position=teammate?ClashVector(p.Position):float3.zero,
                        Health=teammate?p.Health:0,ReconnectRemaining=!authorized||p.Connected?0:(float)Math.Max(0,p.DisconnectDeadline-now),MaxHealthFraction=authorized?p.MaxFraction:0,ReviveProgress=teammate?match.ReviveProgress(p.Id,now):0});
                }
                bool viewing=local.ViewLease>=now&&match.Active(local);
                bool sense=viewing&&local.Id==match.CarrierId&&local.SenseLease>=now&&local.SenseRemaining>0;
                uint sectors=0;bool presence=false;
                var contacts=new List<float3>(11);
                if(viewing&&local.Id==match.CarrierId)
                    foreach(var enemy in match.Players.Values)
                    {
                        if(enemy.TeamId==local.TeamId||enemy.Life!=ClashLife.Alive)continue;
                        float3 delta=ClashVector(enemy.Position)-ClashVector(local.Position);float distance=math.length(delta);
                        presence|=distance<=BountyClashRules.InstinctRange;
                        if(!sense||distance>BountyClashRules.BoostRange)continue;
                        float angle=math.atan2(delta.x,delta.z);int sector=((int)math.floor((angle+math.PI/8)/(math.PI/4))+8)%8;
                        int band=distance<50?0:distance<100?1:2;sectors|=1u<<(sector+band*8);
                        float phase=enemy.Id*37+match.Epoch*13+(float)Math.Floor(now*2);
                        float3 fuzzy=math.round((ClashVector(enemy.Position)+new float3(0,1,0))/2)*2+
                            new float3(math.sin(phase),math.sin(phase*1.7f),math.cos(phase))*.5f;
                        if(contacts.Count<11)contacts.Add(fuzzy);
                    }
                SendClashRpc(ecb,connection,new ClashSenseRpc{MatchId=match.MatchId,Epoch=match.Epoch,Sequence=sequence,Remaining=local.SenseRemaining,Active=sense&&local.SenseRemaining>0,Presence=presence,SectorMask=sectors,ContactCount=contacts.Count});
                for(int i=0;i<contacts.Count;i++)SendClashRpc(ecb,connection,new ClashSenseContactRpc{MatchId=match.MatchId,Epoch=match.Epoch,Sequence=sequence,Index=i,Count=contacts.Count,
                    Position=contacts[i],UncertaintyRadius=3,ValidFor=.35f});
            }
        }
        private static void SendClashRpc<T>(EntityCommandBuffer ecb,Entity connection,T data)where T:unmanaged,IRpcCommand
        {var rpc=ecb.CreateEntity();ecb.AddComponent(rpc,data);ecb.AddComponent(rpc,new SendRpcCommandRequest{TargetConnection=connection});}
    }
}
