using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Context=Unity.MP_FPS.RaidPersistenceContext;

int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
string root=Path.Combine(Path.GetTempPath(),"moon-clash-context-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"Assets"));
UnityEngine.Application.dataPath=Path.Combine(root,"Assets");
try
{
    var payload=new Context.Settlement{PlayerId=Guid.NewGuid().ToString("D"),SettlementId=Guid.NewGuid().ToString("D"),Outcome="Dead",DeploymentId=null,InventoryJson=null};
    var old=new{payload.PlayerId,payload.SettlementId,payload.Outcome,payload.DeploymentId,payload.Dust,payload.Alloy,payload.Cells,payload.InventoryJson};
    Check(JsonConvert.SerializeObject(payload)==JsonConvert.SerializeObject(old),"legacy settlement hash changed due to optional metadata");
    var deploy=new Context.Deployment{PlayerId=payload.PlayerId,DeploymentId=payload.SettlementId,Cells=0};
    Check(JsonConvert.SerializeObject(deploy)==JsonConvert.SerializeObject(new{deploy.PlayerId,deploy.DeploymentId,deploy.Cells}),"legacy deployment changed serialization");
    RecoveryOfflineChecks.Run();checks+=14;
    using(var offline=new OfflineProfileStore(Path.Combine(root,"isolated-profile.json")))
    {
        using(var context=new Context(offline))Check(!context.ClashSupported,"offline offers PvP");
        try{offline.Deploy(new Context.Deployment{PlayerId=offline.Read().PlayerId,DeploymentId=Guid.NewGuid().ToString("D"),Mode="BountyClash"});throw new Exception("offline accepted Clash");}
        catch(Context.LoadoutRejectedException){checks++;}
    }
    string config=Path.Combine(root,"server.json"),outbox=Path.Combine(root,"outbox");
    File.WriteAllText(config,JsonConvert.SerializeObject(new{BackendUrl="http://127.0.0.1:1/",ServerKey=new string('k',32),OutboxDirectory=outbox}));
    var handler=new Handler{FailWrites=true};var first=new Context(config,handler);
    var match=Guid.NewGuid();var token=Guid.NewGuid();var eventId=Guid.NewGuid();
    var ids=new[]{Guid.NewGuid(),Guid.NewGuid()};var participants=new Context.BountyParticipant[2];
    for(int i=0;i<2;i++)
    {
        var entity=new Entity{Index=i+1};var p=new Context.Profile{PlayerId=Guid.NewGuid().ToString("D"),DeploymentId=ids[i].ToString("D"),Mode="BountyClash",MatchId=match.ToString("D"),RulesVersion=1,TeamId=0};
        first.Profiles.Add(entity,p);var graph=InventoryGraph.Create(false,false);graph.AddSupply("dust",i+1,foundInRaid:true);
        first.Inventories.Add(entity,new Unity.MP_FPS.RaidInventoryState{Graph=graph,RaidId=4});
        participants[i]=new Context.BountyParticipant{PlayerId=p.PlayerId,DeploymentId=p.DeploymentId,TeamId=0};
        File.WriteAllText(Path.Combine(outbox,ids[i].ToString("N")+".deploy"),JsonConvert.SerializeObject(new Context.Deployment{PlayerId=p.PlayerId,DeploymentId=p.DeploymentId,Mode=p.Mode,MatchId=p.MatchId,RulesVersion=1}));
    }
    first.BeginBountyRedemption(match,token,eventId,ids[0],ids);
    for(int i=0;i<2;i++)first.BeginSave(new Entity{Index=i+1},new Unity.MP_FPS.RaidSession{RaidId=4,SettlementId=ids[i].ToString("D"),PersistentDeployment=true,Phase=Unity.MP_FPS.RaidPhase.Extracted});
    try{await Task.WhenAll(first.Saves.Values.Select(s=>s.Task));}catch(HttpRequestException){}
    Check(Directory.GetFiles(outbox,"*.bounty").Length==1 && Directory.GetFiles(outbox,"*.json").Length==2,"failed delivery did not retain entire extraction");
    Check(first.Saves.Count==2,"BeginSave duplicated group saves");
    first.Dispose();await first.ShutdownTask;
    // Restore just the atomic group file: this is the crash between group creation
    // and writing any individual receipt. Every member must still be recovered.
    foreach(string file in Directory.GetFiles(outbox,"*.json"))File.Delete(file);
    var good=new Handler();var resumed=new Context(config,good);
    await Task.WhenAll(resumed.Saves.Values.Select(s=>s.Task));
    Check(good.Settled.Count==2 && good.Redemptions.Count==1,"group restart lost a teammate or duplicated redemption");
    Check(good.Settled.Values.All(p=>p.Value<string>("BountyEventId")==eventId.ToString("D") && p.Value<string>("Outcome")=="Extracted"),"replay lost event metadata");
    resumed.CleanBountyJournals();
    Check(Directory.GetFiles(outbox).Length==0,"acknowledged group left debit or reward journals");
    resumed.Dispose();await resumed.ShutdownTask;
    var oldBackend=new Context(config,new Handler{Capabilities=false});
    var gate=oldBackend.RegisterBountyMatchAsync(match,1,token,participants);
    try{await gate;throw new Exception("old backend accepted new mode");}catch(Context.LoadoutRejectedException){checks++;}
    oldBackend.Dispose();await oldBackend.ShutdownTask;
    // Exercise the real account facade; all PlayerPrefs calls resolve to the
    // in-memory harness stub, never the user's Unity preferences.
    var accountType=typeof(Unity.MP_FPS.AccountClient);const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
    void Field(string name,object value)=>accountType.GetField(name,flags)!.SetValue(null,value);
    void Apply(Guid id,long request)
    {
        var type=accountType.GetNestedType("Profile",System.Reflection.BindingFlags.NonPublic)!;
        var profile=JsonConvert.DeserializeObject(JsonConvert.SerializeObject(new{PlayerId=id.ToString("D"),DisplayName="same-name",InventoryJson=JsonConvert.SerializeObject(InventoryGraph.Create()),Dust=0,Alloy=0,Cells=0}),type);
        accountType.GetMethod("ApplyProfile",flags)!.Invoke(null,new[]{profile,(object)request});
    }
    Field("s_PreferenceKey","memory-only");Field("s_Token",new string('t',64));Field("s_Expires",DateTime.UtcNow.AddHours(1));Field("s_BaseUrl","https://one.example/");
    Check(Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope=="","unverified account exposes scope");
    var accountId=Guid.NewGuid();Apply(accountId,1);string scopeA=Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope;
    Check(scopeA.StartsWith("online/")&&!scopeA.Contains(new string('t',64)),"scope absent or exposes token");
    Apply(Guid.NewGuid(),2);Check(Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope!=scopeA,"same display name leaks quickslots between accounts");
    Field("s_BaseUrl","https://two.example/");Check(Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope=="","unverified service reused prior identity");
    Apply(accountId,3);Check(Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope!=scopeA,"scope shared across services");
    accountType.GetMethod("Clear",flags)!.Invoke(null,null);Check(Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope=="","logout retained scope");
    await Unity.MP_FPS.PlayerProfileClient.EnterOfflineAsync(default);string offlineScope=Unity.MP_FPS.PlayerProfileClient.LocalPreferenceScope;
    Check(offlineScope.StartsWith("offline/")&&offlineScope!=scopeA,"offline shares account quickslots");Unity.MP_FPS.PlayerProfileClient.LeaveOffline();
    // Shutdown while the HTTP debit is still in flight must wait for its result,
    // then refund the original kit instead of posting a death abandonment.
    var lateHandler=new DelayedHandler();var late=new Context(config,lateHandler);
    var lateConnection=new Entity{Index=99};var latePlayer=Guid.NewGuid();var lateDeployment=Guid.NewGuid();var lateMatch=Guid.NewGuid();
    late.Profiles.Add(lateConnection,new Context.Profile{PlayerId=latePlayer.ToString("D")});
    await late.RegisterBountyMatchAsync(lateMatch,1,Guid.NewGuid(),new[]{new Context.BountyParticipant{PlayerId=latePlayer.ToString("D"),DeploymentId=lateDeployment.ToString("D"),TeamId=0}});
    late.BeginBountyDeploy(lateConnection,lateMatch,1,0,lateDeployment,0);
    await lateHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));late.Dispose();lateHandler.Release.TrySetResult();await late.ShutdownTask;
    Check(lateHandler.Refunds==1 && lateHandler.Abandons==0,"shutdown discarded late debit or posted death instead of refund");
    Check(Directory.GetFiles(outbox).Length==0,"late-debit refund left an unacknowledged journal");
    Console.WriteLine($"PASS {checks} context/offline checks: legacy receipt bytes, old Format1/2 recovery, no offline PvP, failed delivery, complete group restart, one redemption and fail-closed old backend capability.");
}
finally{Directory.Delete(root,true);}

sealed class Handler:HttpMessageHandler
{
    public bool FailWrites,Capabilities=true;
    public readonly Dictionary<string,JObject> Settled=new();public readonly HashSet<string> Redemptions=new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        if(request.Method==HttpMethod.Get)return Reply(new{features=Capabilities?new[]{"bounty-clash-v1"}:Array.Empty<string>()});
        if(FailWrites)return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var body=JObject.Parse(await request.Content!.ReadAsStringAsync(ct));var path=request.RequestUri!.AbsolutePath;
        lock(Settled)
        {
            if(path.EndsWith("/redemptions")){Redemptions.Add(body.Value<string>("MatchId")!);return Reply(new{status="redeemed"});}
            if(path.EndsWith("/settlements"))
            {
                string id=body.Value<string>("SettlementId")!;
                if(Settled.TryGetValue(id,out var old)&&old.ToString()!=body.ToString())throw new Exception("receipt drift");
                Settled[id]=body;return Reply(new{PlayerId=body.Value<string>("PlayerId"),Dust=0,Alloy=0,Cells=0});
            }
            return Reply(new{status="registered"});
        }
    }
    static HttpResponseMessage Reply(object body)=>new(HttpStatusCode.OK){Content=new StringContent(JsonConvert.SerializeObject(body),Encoding.UTF8,"application/json")};
}

sealed class DelayedHandler:HttpMessageHandler
{
    public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Refunds,Abandons;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        object reply;
        if(request.Method==HttpMethod.Get)reply=new{features=new[]{"bounty-clash-v1"}};
        else
        {
            var body=JObject.Parse(await request.Content!.ReadAsStringAsync(ct));var path=request.RequestUri!.AbsolutePath;
            if(path.EndsWith("/deployments"))
            {
                Started.TrySetResult();await Release.Task;
                reply=new Context.Profile{PlayerId=body.Value<string>("PlayerId"),DeploymentId=body.Value<string>("DeploymentId"),
                    Mode=body.Value<string>("Mode"),MatchId=body.Value<string>("MatchId"),RulesVersion=1,
                    RaidInventoryJson=JsonConvert.SerializeObject(InventoryGraph.Create(false,false))};
            }
            else if(path.EndsWith("/refund")){Refunds++;reply=new Context.Profile{PlayerId=body.Value<string>("PlayerId")};}
            else if(path.EndsWith("/abandon")){Abandons++;reply=new Context.Profile{PlayerId=body.Value<string>("PlayerId")};}
            else reply=new{status="registered"};
        }
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonConvert.SerializeObject(reply),Encoding.UTF8,"application/json")};
    }
}
