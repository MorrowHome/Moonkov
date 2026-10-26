using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;

public static class RaidChatChecks
{
    private static int s_Count;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Chat check: " + message); s_Count++; }
    [MenuItem("Tools/Moonkov/Checks/Raid Chat")]
    public static void Run()
    {
        Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Use idle Edit Mode, without network peers.");
        s_Count = 0;
        TextAndState();
        using (var f = new Fixture())
        {
            var a = f.Add("队友甲", 1); var b = f.Add("队友乙", 2); var c = f.Add("敌方", 3);
            f.Tick(); Check(f.Session != 0, "server announces session"); f.Clear();
            f.Request(a, 1, RaidChatChannel.Team); f.Tick(); Check(f.Count == 3, "LAN/direct cooperative roster");
            Check(f.Messages().All(m => m.Name.ToString() == "队友甲"), "authoritative sender name");
            f.Clear(); f.Request(a, 1, RaidChatChannel.Team); f.Tick(); Check(f.Count == 0, "duplicate request is never broadcast twice");
            f.Clear(); f.Request(a, 2, RaidChatChannel.Global, session: f.Session + 1); f.Tick(); Check(f.Count == 0 && f.Result == RaidChatResult.Expired, "stale session");
            f.Clear(); f.Request(a, 2, RaidChatChannel.Team); f.Tick(); Check(f.Count == 3, "valid ID after stale session");
            f.Clear(); f.Request(a, 3, RaidChatChannel.Team); f.Tick();
            f.Clear(); f.Request(a, 4, RaidChatChannel.Team); f.Tick(); Check(f.Count == 0 && f.Result == RaidChatResult.RateLimited, "three-message burst");
            f.Advance(1); f.Clear(); f.Request(a, 5, RaidChatChannel.Team); f.Tick(); Check(f.Count == 3, "one token per second");
            f.Advance(1); f.Clear(); f.World.EntityManager.AddComponent<NetworkStreamRequestDisconnect>(c);
            f.Request(a, 6, RaidChatChannel.Global); f.Tick(); Check(f.Count == 2, "disconnecting receiver excluded");
            f.World.EntityManager.RemoveComponent<NetworkStreamRequestDisconnect>(c);
            var unknown = f.World.EntityManager.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame));
            f.Clear(); f.Request(unknown, 99, RaidChatChannel.Global); f.Tick(); Check(f.Count == 0, "unjoined sender rejected");
            f.World.EntityManager.AddComponentData(a, new RaidSession { Phase = RaidPhase.Dead });
            f.Advance(1); f.Clear(); f.Request(a, 7, RaidChatChannel.Global); f.Tick(); Check(f.Count == 3 && f.Messages().All(m => m.Dead), "dead sender remains connected and labelled");
            f.World.EntityManager.SetComponentData(a, new RaidSession { Phase = RaidPhase.Extracted });
            f.Advance(1); f.Clear(); f.Request(a, 8, RaidChatChannel.Team); f.Tick(); Check(f.Count == 3 && f.Messages().All(m => !m.Dead), "extracted sender can chat");
            f.World.EntityManager.CreateEntity(typeof(OfflineRaidMode));
            f.Clear(); f.Request(a, 9, RaidChatChannel.Global); f.Tick(); Check(f.Count == 0, "single player disabled");
        }
        using (var f = new Fixture())
        {
            var sender=f.Add("换行\n昵称",1);f.Tick();f.Clear();
            f.Request(sender,1,RaidChatChannel.Global," \r\n ");f.Tick();Check(f.Count==0&&f.Result==RaidChatResult.Empty,"server rejects empty body");
            f.Clear();f.Request(sender,2,RaidChatChannel.Global,new string('a',121));f.Tick();Check(f.Count==0&&f.Result==RaidChatResult.TooLong,"server revalidates text length");
            f.Clear();f.Request(sender,3,(RaidChatChannel)255);f.Tick();Check(f.Count==0&&f.Result==RaidChatResult.Unavailable,"invalid wire channel");
            f.Advance(1);f.Clear();f.Request(sender,4,RaidChatChannel.Global,"<b>plain</b>");f.Tick();
            Check(f.Count==1&&f.Messages()[0].Name.ToString()=="换行昵称"&&f.Messages()[0].Text.ToString()=="<b>plain</b>","server strips nickname controls and preserves plain-text markup");
        }
        Clash(); Online(); ClientTransport();
        string output = Path.GetFullPath("LocalData/RaidChatReview"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "checks.txt"), s_Count + " checks passed. Isolated ECS Worlds; no transport, accounts or multiplayer acceptance.\n");
        Debug.Log("Raid chat: " + s_Count + " checks passed. No network peers launched.");
    }
    private static void TextAndState()
    {
        Check(RaidChatRules.Normalize("\t你好\r\n世界\u0000", out string cleaned) == RaidChatResult.Accepted && cleaned == "你好世界", "controls removed");
        Check(RaidChatRules.Normalize(" \n ", out _) == RaidChatResult.Empty, "whitespace rejected");
        Check(RaidChatRules.Normalize(new string('月',120), out _) == RaidChatResult.Accepted, "120 Chinese characters");
        Check(RaidChatRules.Normalize(new string('月',121), out _) == RaidChatResult.TooLong, "121 characters rejected");
        string emoji = string.Concat(Enumerable.Repeat("🌙",120));
        Check(RaidChatRules.Normalize(emoji, out _) == RaidChatResult.Accepted && new FixedString512Bytes(emoji).Length == 480, "surrogate pairs count once and fit wire buffer");
        Check(RaidChatRules.Normalize("\ud800", out _) == RaidChatResult.InvalidText, "invalid UTF16 rejected");
        Check(RaidChatRules.Normalize("<b>原样</b>", out cleaned) == RaidChatResult.Accepted && cleaned == "<b>原样</b>", "markup stays plain text");
        Check(!RaidChatRules.Receives(RaidChatChannel.Team,0,"A",0,"B"), "team IDs do not bridge instance groups");
        var state = new RaidChatClientState(); state.Receive(new RaidChatResultRpc { Session = 2 });
        state.Draft="草稿"; state.PendingRequest=1;
        state.Receive(new RaidChatResultRpc { Session=2,RequestId=1,Result=RaidChatResult.RateLimited });
        Check(state.Draft=="草稿" && state.PendingRequest==0, "rejection preserves draft");
        state.PendingRequest=2; state.Receive(new RaidChatResultRpc { Session=2,RequestId=2,Result=RaidChatResult.Accepted });
        Check(state.Draft=="", "only server confirmation clears draft");
        for(uint i=1;i<=110;i++)state.Receive(new RaidChatMessageRpc{Session=2,Sequence=i});
        Check(state.Messages.Count==100 && state.Messages[0].Sequence==11, "bounded history");
        state.Receive(new RaidChatMessageRpc{Session=2,Sequence=110});Check(state.Messages.Count==100,"duplicate message");
        state.Receive(new RaidChatResultRpc{Session=3}); Check(state.Messages.Count==0 && state.Channel==RaidChatChannel.Team && state.NextRequest==0,"new match resets history and channel");
        state.Receive(new RaidChatMessageRpc{Session=2,Sequence=111}); Check(state.Messages.Count==0,"old-match message ignored");
        state.Receive(new RaidChatResultRpc{Session=4,AnnouncementSequence=10});
        state.Receive(new RaidChatResultRpc{Session=3,AnnouncementSequence=9});
        Check(state.Session==4,"out-of-order old announcement cannot restore previous match");
        var requestFields=typeof(RaidChatRequestRpc).GetFields().Select(field=>field.Name).ToArray();
        Check(requestFields.Length==4 && !requestFields.Contains("Name") && !requestFields.Contains("TeamId"),"client cannot claim identity, team or death");
    }
    private static ClashArenaDefinition Arena() => new ClashArenaDefinition { Radius=100, Spawns=new ClashPoint[12], Exits=new ClashPoint[2] };
    private static void Clash()
    {
        using var f = new Fixture();
        var a=f.Add("甲",1);var b=f.Add("乙",2);var c=f.Add("丙",3);
        var match=new BountyClashMatch(Arena(),1,0);
        var pa=match.Join("a","甲",0);pa.NetworkId=1;pa.TeamId=0;
        var pb=match.Join("b","乙",0);pb.NetworkId=2;pb.TeamId=0;
        var pc=match.Join("c","丙",0);pc.NetworkId=3;pc.TeamId=1;
        var holder=f.World.EntityManager.CreateEntity();
        var world=new BountyClashWorld{Match=match};f.World.EntityManager.AddComponentObject(holder,world);
        f.Tick(); f.Clear(); f.Request(a,1,RaidChatChannel.Team);f.Tick();Check(f.Count==2,"Clash team excludes enemy");
        Check(f.Targets().Contains(a)&&f.Targets().Contains(b)&&!f.Targets().Contains(c),"real target connections");
        f.Clear();f.Request(a,2,RaidChatChannel.Global);f.Tick();Check(f.Count==3,"global includes enemy");
        pc.TeamId=0;f.Clear();f.Request(a,3,RaidChatChannel.Team);f.Tick();Check(f.Count==3,"team changes apply immediately");
        pa.TeamId=-1;f.Advance(1);f.Clear();f.Request(a,4,RaidChatChannel.Team);f.Tick();Check(f.Count==0&&f.Result==RaidChatResult.NoTeam,"no team never falls back to global");
        f.Advance(1);f.Clear();f.Request(a,5,RaidChatChannel.Global);f.Tick();Check(f.Count==3,"unassigned global allowed");
        pa.Life=ClashLife.Dead;f.Advance(1);f.Clear();f.Request(a,6,RaidChatChannel.Global);f.Tick();Check(f.Messages().All(m=>m.Dead),"Clash death from server participant");
        ulong old=f.Session;world.Match=new BountyClashMatch(Arena(),2,0);
        var next=world.Match.Join("a","甲",0);next.NetworkId=1;
        f.Clear();f.Tick();Check(f.Session!=old,"next match creates fresh session");
        f.Clear();f.Request(a,7,RaidChatChannel.Global,session:old);f.Tick();Check(f.Count==0,"old request cannot enter next match");
        world.Match=new BountyClashMatch(Arena(),3,0,true);var solo=world.Match.Join("a","甲",0);solo.NetworkId=1;
        f.Clear();f.Request(a,8,RaidChatChannel.Global);f.Tick();Check(f.Count==0,"solo Clash disabled");
    }
    private static void Online()
    {
        using var f=new Fixture();var a=f.Add("甲",1);var b=f.Add("乙",2);var c=f.Add("丙",3);
        // Isolated local profile adapter avoids credentials, real account data and HTTP.
        var folder=Path.Combine(Application.temporaryCachePath,"RaidChatChecks",Guid.NewGuid().ToString("N"));
        using var context=new RaidPersistenceContext(new OfflineProfileStore(Path.Combine(folder,"profile.json")));
        context.Profiles[a]=new RaidPersistenceContext.Profile{OnlineInstanceId="instance",OnlineTeam=0};
        context.Profiles[b]=new RaidPersistenceContext.Profile{OnlineInstanceId="instance",OnlineTeam=0};
        context.Profiles[c]=new RaidPersistenceContext.Profile{OnlineInstanceId="instance",OnlineTeam=1};
        var holder=f.World.EntityManager.CreateEntity();f.World.EntityManager.AddComponentObject(holder,context);
        f.Tick();f.Clear();f.Request(a,1,RaidChatChannel.Team);f.Tick();Check(f.Count==2,"online authoritative profile teams");
        f.Clear();f.Request(a,2,RaidChatChannel.Global);f.Tick();Check(f.Count==3,"online Versus global");
        context.Profiles[c].OnlineTeam=0;
        f.Clear();f.Request(a,3,RaidChatChannel.Team);f.Tick();Check(f.Count==3,"online cooperative team");
    }
    private static void ClientTransport()
    {
        using var world=new World("Chat client checks",WorldFlags.GameClient);
        var system=world.GetOrCreateSystemManaged<RaidChatClientSystem>();var em=world.EntityManager;
        var connection=em.CreateEntity(typeof(NetworkId),typeof(NetworkStreamInGame));
        var impostor=em.CreateEntity();
        void Receive<T>(T value,Entity source) where T:unmanaged,IComponentData
        {var e=em.CreateEntity();em.AddComponentData(e,value);em.AddComponentData(e,new ReceiveRpcCommandRequest{SourceConnection=source});}
        Receive(new RaidChatResultRpc{Session=1},connection);system.Update();
        using var query=em.CreateEntityQuery(typeof(RaidChatClientState));var state=em.GetComponentObject<RaidChatClientState>(query.GetSingletonEntity());
        Receive(new RaidChatMessageRpc{Session=1,Sequence=2,Text="second"},connection);
        Receive(new RaidChatMessageRpc{Session=1,Sequence=1,Text="first"},connection);
        Receive(new RaidChatMessageRpc{Session=1,Sequence=3,Text="forged"},impostor);system.Update();
        Check(state.Messages.Count==2&&state.Messages[0].Text.ToString()=="first","ordered messages and source validation");
        em.AddComponent<NetworkStreamRequestDisconnect>(connection);system.Update();Check(state.Session==0&&state.Messages.Count==0,"disconnect clears client state");
    }
    private sealed class Fixture : IDisposable
    {
        public readonly World World=new World("Chat server checks",WorldFlags.GameServer);
        private readonly RaidChatServerSystem m_System;
        private readonly EntityQuery m_Messages,m_Results,m_Sends;
        public ulong Session;
        private double m_Time;
        public Fixture()
        {
            m_System=World.GetOrCreateSystemManaged<RaidChatServerSystem>();
            m_Messages=World.EntityManager.CreateEntityQuery(typeof(RaidChatMessageRpc),typeof(SendRpcCommandRequest));
            m_Results=World.EntityManager.CreateEntityQuery(typeof(RaidChatResultRpc),typeof(SendRpcCommandRequest));
            m_Sends=World.EntityManager.CreateEntityQuery(typeof(SendRpcCommandRequest));
        }
        public Entity Add(string name,int id)
        {var em=World.EntityManager;var e=em.CreateEntity();em.AddComponentData(e,new JoinedClient{PlayerName=name});em.AddComponentData(e,new NetworkId{Value=id});em.AddComponent<NetworkStreamInGame>(e);return e;}
        public void Request(Entity source,uint id,RaidChatChannel channel,string text="你好",ulong session=0)
        {var em=World.EntityManager;var e=em.CreateEntity();em.AddComponentData(e,new RaidChatRequestRpc{Session=session==0?Session:session,RequestId=id,Channel=channel,Text=text});em.AddComponentData(e,new ReceiveRpcCommandRequest{SourceConnection=source});}
        public void Tick()
        {World.SetTime(new TimeData(m_Time,.016f));m_System.Update();using var results=m_Results.ToComponentDataArray<RaidChatResultRpc>(Allocator.Temp);foreach(var r in results)if(r.RequestId==0)Session=r.Session;}
        public void Advance(double seconds)=>m_Time+=seconds;
        public int Count=>m_Messages.CalculateEntityCount();
        public RaidChatResult Result {get{using var results=m_Results.ToComponentDataArray<RaidChatResultRpc>(Allocator.Temp);return results.Length==0?RaidChatResult.Unavailable:results[results.Length-1].Result;}}
        public RaidChatMessageRpc[] Messages(){using var rows=m_Messages.ToComponentDataArray<RaidChatMessageRpc>(Allocator.Temp);return rows.ToArray();}
        public Entity[] Targets(){using var rows=m_Messages.ToComponentDataArray<SendRpcCommandRequest>(Allocator.Temp);return rows.Select(row=>row.TargetConnection).ToArray();}
        public void Clear()=>World.EntityManager.DestroyEntity(m_Sends);
        public void Dispose()=>World.Dispose();
    }
    public static void SyncTranslations()
    {
        var source=JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var keys=new[]{"CHAT ALL","Join a team to use team chat.","Enter a message.","Messages can contain up to 120 characters.","This message contains invalid text.","Sending too fast. Try again shortly.","Message not confirmed. Your draft was kept.","Chat is unavailable."};
        var collection=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach(var table in collection.StringTables)
        {
            foreach(string key in keys)table.AddEntry(key,table.LocaleIdentifier.Code=="zh-Hans"?source[key].Value<string>():key=="CHAT ALL"?"ALL":key);
            EditorUtility.SetDirty(table);AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }
}
