using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
using BodyPart=Unity.MP_FPS.BodyPart;

// Real UDP generated RPCs, real authoritative adapter and isolated loopback HTTP/PG.
// All game Worlds live in one process. This is NOT independent Player/visual acceptance.
public static class BountyClashTransportChecks
{
    private sealed class Drivers:INetworkStreamDriverConstructor
    {
        public void CreateClientDriver(World w,ref NetworkDriverStore store,NetDebug debug)
        {var d=new NetworkDriverStore.NetworkDriverInstance{driver=NetworkDriver.Create(new UDPNetworkInterface())};DefaultDriverBuilder.CreateClientPipelines(ref d);store.RegisterDriver(TransportType.Socket,d);}
        public void CreateServerDriver(World w,ref NetworkDriverStore store,NetDebug debug)
        {var d=new NetworkDriverStore.NetworkDriverInstance{driver=NetworkDriver.Create(new UDPNetworkInterface())};DefaultDriverBuilder.CreateServerPipelines(ref d);store.RegisterDriver(TransportType.Socket,d);}
    }
    // Forward to the real loopback backend, but hold one successful settlement
    // response so the disconnect/reconnect race happens while HTTP is in flight.
    private sealed class DelayedSettlementAck:DelegatingHandler
    {
        public string Settlement;
        public int Posts;
        public volatile bool Held;
        private readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public DelayedSettlementAck():base(new HttpClientHandler()){}
        public void Release()=>release.TrySetResult(true);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
        {
            bool hold=false;
            if(request.RequestUri.AbsolutePath.EndsWith("/settlements",StringComparison.Ordinal)&&Settlement!=null)
            {
                var body=JObject.Parse(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                hold=string.Equals((string)body["SettlementId"],Settlement,StringComparison.OrdinalIgnoreCase);
                if(hold)Interlocked.Increment(ref Posts);
            }
            var response=await base.SendAsync(request,cancel).ConfigureAwait(false);
            if(hold&&response.IsSuccessStatusCode)
            {
                Held=true;
                using(cancel.Register(()=>release.TrySetCanceled()))await release.Task.ConfigureAwait(false);
            }
            return response;
        }
    }
    private static double s_Time;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static Entity[] All<T>(World w)where T:unmanaged,IComponentData
    {using var q=w.EntityManager.CreateEntityQuery(typeof(T));using var array=q.ToEntityArray(Allocator.Temp);return array.ToArray();}
    private static Entity One<T>(World w)where T:unmanaged,IComponentData=>All<T>(w).Single();
    private static World Create(string name,bool server)
    {
        var w=new World(name,server?WorldFlags.GameServer:WorldFlags.GameClient);
        var types=DefaultWorldInitialization.GetAllSystems(server?WorldSystemFilterFlags.ServerSimulation:WorldSystemFilterFlags.ClientSimulation)
            .Where(t=>t.Assembly.GetName().Name.StartsWith("Unity.NetCode")||t.Assembly.GetName().Name.StartsWith("Unity.Entities")||t.Assembly.GetName().Name.StartsWith("Unity.Transforms")||t.Name.Contains("RpcCommandRequestSystem")||typeof(IGhostComponentSerializerRegistration).IsAssignableFrom(t))
            .Where(t=>!t.Assembly.GetName().Name.Contains("Editor")&&!t.Name.Contains("Physics")).ToList();
        TypeManager.SortSystemTypesInCreationOrder(types);DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(w,types);
        var clock=w.GetExistingSystemManaged<UpdateWorldTimeSystem>();if(clock!=null)clock.Enabled=false;return w;
    }
    private static void Send<T>(World client,T rpc)where T:unmanaged,IRpcCommand
    {var e=client.EntityManager.CreateEntity();client.EntityManager.AddComponentData(e,rpc);client.EntityManager.AddComponentData(e,new SendRpcCommandRequest{TargetConnection=One<NetworkId>(client)});}
    public static bool Running {get;private set;}
    public static void Start(string serverConfig)
    {
        if(Running)throw new InvalidOperationException("Clash UDP check already running.");
        Running=true;
        Directory.CreateDirectory("Library/BuildValidation/BountyClash");
        File.WriteAllText("Library/BuildValidation/BountyClash/adapter-udp-result.txt","RUNNING");
        EditorApplication.delayCall+=()=>
        {
            try{File.WriteAllText("Library/BuildValidation/BountyClash/adapter-udp-result.txt",Run(serverConfig));}
            catch(Exception error){File.WriteAllText("Library/BuildValidation/BountyClash/adapter-udp-result.txt",error.ToString());Debug.LogException(error);}
            finally{Running=false;}
        };
    }
    public static string Run(string serverConfig)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||MoonRaidMap.Active||BountyClashArenaSettings.Active)throw new InvalidOperationException("Requires idle Editor without active raid.");
        var config=JObject.Parse(File.ReadAllText(serverConfig));var address=new Uri((string)config["BackendUrl"]);
        if(!address.IsLoopback)throw new InvalidOperationException("Checks accept a dedicated temporary loopback API only.");
        var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        var holder=new GameObject("Disposable Bounty UDP fixture");holder.SetActive(false);
        var map=holder.AddComponent<MoonRaidMap>();var settings=holder.AddComponent<BountyClashArenaSettings>();
        var previousMap=MoonRaidMap.Active;var previousArena=BountyClashArenaSettings.Active;
        var oldDrivers=NetworkStreamReceiveSystem.DriverConstructor;
        var clients=new List<World>();World server=null;RaidPersistenceContext persistence=null;
        var delayedAck=new DelayedSettlementAck();
        var pendingPlayers=typeof(Gameplay.Leaderboard.LeaderboardManager).GetField("_pendingPlayers",BindingFlags.Static|BindingFlags.NonPublic);
        object previousPending=pendingPlayers.GetValue(null);pendingPlayers.SetValue(null,Activator.CreateInstance(pendingPlayers.FieldType));
        var retiredField=typeof(Gameplay.Leaderboard.LeaderboardManager).GetField("s_RetiredRaidActors",BindingFlags.Static|BindingFlags.NonPublic);
        var retired=(HashSet<int>)retiredField.GetValue(null);var previousRetired=retired.ToArray();retired.Clear();
        var report=new List<string>();s_Time=0;
        void Check(bool ok,string text){if(!ok)throw new InvalidOperationException(text);report.Add(text);}
        try
        {
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null,map);typeof(BountyClashArenaSettings).GetProperty("Active").SetValue(null,settings);
            var arena=settings.Definition;
            typeof(MoonRaidMap).GetField("m_LootPositions",Hidden).SetValue(map,new[]{BountyClashArenaSettings.Position(arena.Target)});
            NetworkStreamReceiveSystem.DriverConstructor=new Drivers();server=Create("Clash UDP authority",true);
            var em=server.EntityManager;var handle=server.GetOrCreateSystem<ServerGameSystem>();
            ref var system=ref server.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);
            Entity persistenceEntity=(Entity)typeof(ServerGameSystem).GetField("m_PersistenceEntity",Hidden).GetValue(system);
            // Create a private outbox for this run; never open the user's configured receipt directory.
            string scratch=Path.Combine(Path.GetDirectoryName(serverConfig),"udp-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
            config["OutboxDirectory"]=Path.Combine(scratch,"outbox");string privateConfig=Path.Combine(scratch,"server.json");File.WriteAllText(privateConfig,config.ToString());
            persistence=new RaidPersistenceContext(privateConfig,delayedAck);em.AddComponentObject(persistenceEntity,persistence);
            em.CreateEntity(typeof(RaidModeState));using(var q=em.CreateEntityQuery(typeof(RaidModeState)))em.SetComponentData(q.GetSingletonEntity(),new RaidModeState{Mode=RaidGameMode.BountyClash,RulesVersion=1});
            var playerPrefab=em.CreateEntity(typeof(Prefab),typeof(GhostOwner),typeof(PredictedPlayerGhost),typeof(LocalTransform),typeof(GhostGameObjectGuid),typeof(PlayerGhost.PlayerData));
            var inputPrefab=em.CreateEntity(typeof(Prefab),typeof(GhostOwner),typeof(PredictedClientInput),typeof(PlayerCommandTarget));
            var prefabsEntity=em.CreateEntity(typeof(PlayerEntityPrefabs));em.SetComponentData(prefabsEntity,new PlayerEntityPrefabs{DollSingerEntityPrefab=playerPrefab,ClientInputEntityPrefab=inputPrefab});
            var legacySpawn=em.CreateEntity(typeof(SpawnPoint),typeof(LocalToWorld));
            em.SetComponentData(legacySpawn,new LocalToWorld{Value=float4x4.Translate(new float3(20000,0,20000))});
            Loot(em).Initialize(1,null,map);
            var endpoint=NetworkEndpoint.LoopbackIpv4.WithPort(17983);
            Tick(false);using(var q=em.CreateEntityQuery(typeof(NetworkStreamDriver)))Check(q.GetSingletonRW<NetworkStreamDriver>().ValueRW.Listen(endpoint),"real UDP listener opened");
            void Core(){ref var sys=ref server.Unmanaged.GetUnsafeSystemRef<ServerGameSystem>(handle);ref var state=ref server.Unmanaged.ResolveSystemStateRef(handle);sys.OnUpdate(ref state);}
            void Tick(bool core=true)
            {
                s_Time+=1.0/60;
                foreach(var w in new[]{server}.Concat(clients).ToArray())
                {w.SetTime(new TimeData(s_Time,1f/60));w.GetExistingSystemManaged<InitializationSystemGroup>()?.Update();w.GetExistingSystemManaged<SimulationSystemGroup>()?.Update();w.EntityManager.CompleteAllTrackedJobs();}
                if(core)Core();Thread.Sleep(2);
            }
            void Pump(Func<bool> done,int frames=1800)
            {for(int i=0;i<frames;i++){Tick();if(done())return;}throw new TimeoutException("Clash UDP fixture timed out: "+report.LastOrDefault());}
            string Register(string suffix)
            {
                using var http=new HttpClient();string name="bc_"+Guid.NewGuid().ToString("N").Substring(0,16)+suffix;
                using var response=http.PostAsync(new Uri(address,"auth/register"),new StringContent(JsonConvert.SerializeObject(new{Username=name,Password=Guid.NewGuid().ToString("N")}),Encoding.UTF8,"application/json")).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();return (string)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["token"];
            }
            World Connect(string name,string token)
            {
                var w=Create(name,false);clients.Add(w);Tick();using(var q=w.EntityManager.CreateEntityQuery(typeof(NetworkStreamDriver)))q.GetSingletonRW<NetworkStreamDriver>().ValueRW.Connect(w.EntityManager,endpoint);
                Pump(()=>All<NetworkId>(w).Length==1);Send(w,new ClientJoinRequestRpc{PlayerName=name,LoginToken=token,Mode=RaidGameMode.BountyClash,RulesVersion=1});return w;
            }
            Pump(()=>persistence.ClashSupported);
            string tokenA=Register("a"),tokenC=Register("c"),tokenB=Register("b");
            var a=Connect("Clash UDP A",tokenA);var c=Connect("Clash UDP teammate",tokenC);var b=Connect("Clash UDP enemy",tokenB);
            BountyClashWorld WorldState()=>em.GetComponentObject<BountyClashWorld>(One<RaidLootWorld>(server));
            Pump(()=>WorldState().Match?.Players.Count==3);
            var world=WorldState();var match=world.Match;
            ClashParticipant Person(World w)=>match.Players.Values.Single(p=>p.NetworkId==w.EntityManager.GetComponentData<NetworkId>(One<NetworkId>(w)).Value);
            var pa=Person(a);var pc=Person(c);var pb=Person(b);
            uint sequence=0;
            void Action(World w,ClashIntent intent,int value=0,int target=0,string invite=null)
            {var p=Person(w);uint request=++sequence;Send(w,new ClashActionRpc{MatchId=match.MatchId,Epoch=match.Epoch,RequestId=request,Intent=intent,Value=value,TargetParticipantId=target,InviteCode=invite??""});Pump(()=>p.LastRequest==request);Check(p.Result==ClashResult.Ok,"UDP "+intent+" accepted");}
            Entity Player(ClashParticipant p)=>em.GetComponentData<JoinedClient>(world.Connections[p.Id]).PlayerEntity;
            void Move(ClashParticipant p,ClashPoint position)=>em.SetComponentData(Player(p),LocalTransform.FromPosition(new float3(position.X,position.Y,position.Z)));
            Action(a,ClashIntent.CreateTeam,2);Action(c,ClashIntent.JoinTeam,invite:match.Teams[pa.TeamId].Invite);Action(b,ClashIntent.CreateTeam,1);
            Check(pa.TeamId==pc.TeamId&&pa.TeamId!=pb.TeamId,"server-approved teams over UDP invite");
            Action(a,ClashIntent.SetReady,1);Action(c,ClashIntent.SetReady,1);Action(b,ClashIntent.SetReady,1);
            Pump(()=>match.Phase==ClashPhase.Active);
            Check(pa.DeploymentId!=pc.DeploymentId&&pc.DeploymentId!=pb.DeploymentId,"real backend debited three distinct deployments after roster lock");
            Check(em.GetComponentData<PredictedPlayerGhost>(Player(pa)).CurrentHealth>0,"waiting entities became alive only after all deployments");
            foreach(var p in match.Players.Values)
            {
                var expected=arena.Spawns[p.TeamId*3+p.Slot];var actual=em.GetComponentData<LocalTransform>(Player(p)).Position;
                Check(math.distance(actual,new float3(expected.X,expected.Y,expected.Z))<.01f,"team/slot spawn overrides legacy ordinary spawn");
            }
            Move(pa,arena.Target);Move(pc,new ClashPoint(arena.Target.X+1,arena.Target.Y,arena.Target.Z));Move(pb,new ClashPoint(arena.Target.X+20,arena.Target.Y,arena.Target.Z));Tick();
            BountyClashDamage.Apply(server,Player(pc),pb.NetworkId,BodyPart.Chest,1000,10,RaidDamageKind.Bullet);Tick();
            Check(pc.Life==ClashLife.Downed&&em.Exists(Player(pc))&&persistence.Saves.Count==0,"down retains original entity/equipment without settlement");
            Action(a,ClashIntent.Banish);Check(pc.Downs==0&&pc.Life==ClashLife.Downed,"banish begin restores team cap without auto-revive");
            Action(a,ClashIntent.ReviveBegin,target:pc.Id);
            for(int i=0;i<22;i++){s_Time+=.24;Action(a,ClashIntent.ReviveRenew,target:pc.Id);}
            Pump(()=>pc.Life==ClashLife.Alive);Check(em.GetComponentData<PredictedPlayerGhost>(Player(pc)).CurrentHealth>0,"same entity revived through sustained teammate RPCs");
            s_Time+=61;Tick();Action(a,ClashIntent.Pickup);Action(a,ClashIntent.SenseHeld,3);
            Pump(()=>All<ClashSenseContactRpc>(a).Length>0);
            Check(All<ClashSenseContactRpc>(b).Length==0&&All<ClashSenseContactRpc>(c).Length==0,"boost contact payload delivered only to legal holder");
            Entity body=Player(pa);int oldOwner=pa.NetworkId;
            a.EntityManager.AddComponentData(One<NetworkId>(a),new NetworkStreamRequestDisconnect());Pump(()=>!pa.Connected);
            float before=em.GetComponentData<PredictedPlayerGhost>(body).CurrentHealth;
            BountyClashDamage.Apply(server,body,pb.NetworkId,BodyPart.Chest,1,11,RaidDamageKind.Bullet);
            Check(em.GetComponentData<PredictedPlayerGhost>(body).CurrentHealth<before,"disconnected retained body remains damageable");
            clients.Remove(a);a.Dispose();a=Connect("Clash UDP A reconnect",tokenA);Pump(()=>pa.Connected&&pa.NetworkId!=oldOwner);
            Check(Player(pa)==body&&em.GetComponentData<GhostOwner>(body).NetworkId==pa.NetworkId,"reconnect rebinds original entity to new owner without redeployment");
            Check(em.GetComponentData<PlayerClientCommandInputLookup>(body).ClientCommandInputEntity!=Entity.Null,"new connection receives fresh owned command input");
            // The enemy wins the token after downing its former carrier; its own first budget is full.
            BountyClashDamage.Apply(server,body,pb.NetworkId,BodyPart.Chest,1000,12,RaidDamageKind.Bullet);Tick();
            Move(pb,pa.Position);Tick();Action(b,ClashIntent.Pickup);Check(pb.SenseRemaining==5,"new enemy holder gets personal five-second budget");
            Move(pb,arena.Exits[0]);Move(pc,new ClashPoint(arena.Exits[0].X+2,arena.Exits[0].Y,arena.Exits[0].Z));Tick();s_Time+=12;Tick();
            Check(match.ExitBlock(pb.TeamId,0)==ClashExitBlock.Enemy,"live enemy presence blocks extraction");
            Move(pc,arena.Target);Tick();s_Time+=15;Tick();Check(pb.Life==ClashLife.Alive,"paused extraction did not grant free escape");
            int dustBeforeAward=persistence.Profiles[world.Connections[pb.Id]].Dust;
            delayedAck.Settlement=pb.DeploymentId;s_Time+=16;Tick();
            Pump(()=>pb.Life==ClashLife.Extracted&&delayedAck.Held);
            var pending=persistence.Saves[pb.DeploymentId];var pendingTask=pending.Task;var pendingPayload=pending.Payload;
            Entity oldConnection=world.Connections[pb.Id];int oldEnemyOwner=pb.NetworkId;
            b.EntityManager.AddComponentData(One<NetworkId>(b),new NetworkStreamRequestDisconnect());Pump(()=>!pb.Connected);
            Check(pending.Connection==world.Connections[pb.Id]&&pending.Connection!=oldConnection,"in-flight settlement follows detached authenticated owner");
            clients.Remove(b);b.Dispose();b=Connect("Clash UDP enemy settlement reconnect",tokenB);
            Pump(()=>persistence.Joins.Count>0);for(int i=0;i<20;i++)Tick();
            Check(!pb.Connected&&pending.Connection==world.Connections[pb.Id]&&ReferenceEquals(pending.Task,pendingTask)&&ReferenceEquals(pending.Payload,pendingPayload),"authenticated reconnect waits for original receipt without replacing its Task/payload");
            delayedAck.Release();Pump(()=>persistence.Saves.Count==0&&pb.Connected&&pb.NetworkId!=oldEnemyOwner);
            var savedSession=em.GetComponentData<RaidSession>(world.Connections[pb.Id]);
            Check(savedSession.SaveState==RaidSaveState.Saved&&persistence.Profiles[world.Connections[pb.Id]].DeploymentId==null&&delayedAck.Posts==1,"delayed real HTTP ack updates retained owner, then authenticated reconnect gets Saved/fresh stash with one settlement POST");
            using(var http=new HttpClient())
            using(var request=new HttpRequestMessage(HttpMethod.Get,new Uri(address,"auth/me")))
            {
                request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",tokenB);
                using var response=http.SendAsync(request).GetAwaiter().GetResult();response.EnsureSuccessStatusCode();
                var account=JsonConvert.DeserializeObject<RaidPersistenceContext.Profile>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                var current=persistence.Profiles[world.Connections[pb.Id]];
                Check(account.Dust==dustBeforeAward+80&&current.Dust==account.Dust&&current.InventoryJson==account.InventoryJson,"reconnected profile equals real auth/me stash and solo bounty is exactly eighty once");
            }
            Check(match.Award!=null&&match.Award.DeploymentIds.SequenceEqual(new[]{pb.DeploymentId}),"immutable real-backend award uses actual same-event escape roster");
            s_Time+=91;Tick();Pump(()=>match.Phase==ClashPhase.Finished&&persistence.Saves.Count==0);
            Check(pc.Life==ClashLife.TimedOut,"remaining players obey shared ninety-second tail");
            int firstRaid=em.GetComponentData<RaidSession>(world.Connections[pb.Id]).RaidId;
            Action(b,ClashIntent.NextRound);Pump(()=>world.Match.Epoch!=match.Epoch);Check(world.Match.Phase==ClashPhase.Lobby,"server cleans and offers next round without personal redeploy bypass");
            match=world.Match;pa=Person(a);pc=Person(c);pb=Person(b);
            Action(a,ClashIntent.CreateTeam,2);Action(c,ClashIntent.JoinTeam,invite:match.Teams[pa.TeamId].Invite);Action(b,ClashIntent.CreateTeam,1);
            Action(a,ClashIntent.SetReady,1);Action(c,ClashIntent.SetReady,1);Action(b,ClashIntent.SetReady,1);Pump(()=>match.Phase==ClashPhase.Active);
            int secondRaid=em.GetComponentData<RaidSession>(world.Connections[pb.Id]).RaidId;
            Check(secondRaid==firstRaid+1,"second shared match increments personal RaidId");
            var inventory=em.GetComponentObject<RaidInventoryState>(world.Connections[pb.Id]);uint beforeRequest=inventory.RequestId;
            Send(b,new RaidActionCancelRpc{RaidId=firstRaid,RequestId=beforeRequest+1});for(int i=0;i<25;i++)Tick();
            Check(inventory.RequestId==beforeRequest,"previous-match inventory RPC rejected over UDP");
            Send(b,new RaidActionCancelRpc{RaidId=secondRaid,RequestId=beforeRequest+2});Pump(()=>inventory.RequestId==beforeRequest+2);
            Check(All<RaidSnapshotRpc>(b).Any(e=>b.EntityManager.GetComponentData<RaidSnapshotRpc>(e).RaidId==secondRaid),"second-match personal snapshot arrives through transport");
            s_Time+=901;Tick();Pump(()=>match.Phase==ClashPhase.Finished&&persistence.Saves.Count==0);
            return "PASS "+report.Count+" same-process real UDP / temporary HTTP+PG adapter checks";
        }
        finally
        {
            delayedAck.Release();pendingPlayers.SetValue(null,previousPending);retired.Clear();foreach(int id in previousRetired)retired.Add(id);
            foreach(var w in clients)if(w.IsCreated)w.Dispose();if(server!=null&&server.IsCreated)server.Dispose();NetworkStreamReceiveSystem.DriverConstructor=oldDrivers;
            typeof(MoonRaidMap).GetProperty("Active").SetValue(null,previousMap);typeof(BountyClashArenaSettings).GetProperty("Active").SetValue(null,previousArena);
            Object.DestroyImmediate(holder);UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);EditorSceneManager.CloseScene(scene,true);
            Directory.CreateDirectory("Library/BuildValidation/BountyClash");File.WriteAllLines("Library/BuildValidation/BountyClash/adapter-udp.txt",report);
        }
    }
    private static RaidLootContainers Loot(EntityManager em){using var q=em.CreateEntityQuery(typeof(RaidLootWorld));return em.GetComponentObject<RaidLootContainers>(q.GetSingletonEntity());}
}
