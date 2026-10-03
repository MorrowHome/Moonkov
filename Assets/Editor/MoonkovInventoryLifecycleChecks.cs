using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;

public static class MoonkovInventoryLifecycleChecks
{
    private static void Require(bool value,string name) { if(!value) throw new Exception(name); }
    private sealed class Backend : HttpMessageHandler
    {
        public bool HoldDeploy, HoldSettlement, DeployCommitted, AbandonAcknowledged, MissingInventory;
        public readonly TaskCompletionSource<bool> DeployEntered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> DeployRelease=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> SettlementEntered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> SettlementRelease=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ConcurrentQueue<string> Paths=new ConcurrentQueue<string>();
        public string SettlementInventory;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var body=JObject.Parse(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            string path=request.RequestUri.AbsolutePath; Paths.Enqueue(path);
            if(path=="/internal/deployments")
            {DeployEntered.TrySetResult(true);if(HoldDeploy)await DeployRelease.Task.ConfigureAwait(false);DeployCommitted=true;}
            if(path=="/internal/deployments/abandon")
            {if(HoldDeploy) Require(DeployCommitted,"Shutdown abandoned an in-flight debit before resolving it.");AbandonAcknowledged=true;}
            if(path=="/internal/settlements")
            {SettlementInventory=body.Value<string>("InventoryJson");SettlementEntered.TrySetResult(true);if(HoldSettlement)await SettlementRelease.Task.ConfigureAwait(false);}
            var profile=new RaidPersistenceContext.Profile {PlayerId=body.Value<string>("PlayerId"),DeploymentId=body.Value<string>("DeploymentId"),
                CarriedCells=body.Value<int>("Cells"),RaidInventoryJson=MissingInventory ? null : RaidInventoryTransport.Encode(InventoryGraph.Create(false,false))};
            return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(JsonConvert.SerializeObject(profile),Encoding.UTF8,"application/json")};
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly RaidPersistenceContext Context;
        private readonly string m_Directory;
        public Fixture(Backend backend)
        {
            string local=Path.GetFullPath(Path.Combine(Application.dataPath,"../LocalData"));
            m_Directory=Path.GetFullPath(Path.Combine(local,"inventory-lifecycle-"+Guid.NewGuid().ToString("N")));
            Require(m_Directory.StartsWith(local+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Unsafe fixture directory.");
            Directory.CreateDirectory(m_Directory);string config=Path.Combine(m_Directory,"config.json");
            File.WriteAllText(config,JsonConvert.SerializeObject(new {BackendUrl="http://127.0.0.1",ServerKey=Guid.NewGuid().ToString("N"),OutboxDirectory="outbox"}));
            Context=new RaidPersistenceContext(config,backend);
        }
        public void Dispose()
        {Context.Dispose();Require(Context.ShutdownTask.Wait(3000),"Shutdown did not complete.");Directory.Delete(m_Directory,true);}
    }
    [MenuItem("Tools/Moonkov/Check Inventory Lifecycle")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit mode.");
        var connection=new Entity {Index=1,Version=1};
        var backend=new Backend {HoldDeploy=true};
        using(var fixture=new Fixture(backend))
        {
            fixture.Context.Profiles[connection]=new RaidPersistenceContext.Profile {PlayerId=Guid.NewGuid().ToString()};
            fixture.Context.BeginDeploy(connection,0);
            Require(backend.DeployEntered.Task.Wait(3000),"Debit request not started.");
            fixture.Context.Dispose();
            Require(!fixture.Context.ShutdownTask.IsCompleted,"World shutdown should return while a debit is pending.");
            backend.DeployRelease.SetResult(true);
            Require(fixture.Context.ShutdownTask.Wait(3000),"Pending debit cleanup failed.");
            Require(backend.AbandonAcknowledged && backend.Paths.ToArray().Length==2,"Pending debit must be followed by exactly one successful abandon.");
        }
        backend=new Backend {HoldSettlement=true};
        using(var fixture=new Fixture(backend))
        {
            string id=Guid.NewGuid().ToString();
            fixture.Context.Profiles[connection]=new RaidPersistenceContext.Profile {PlayerId=Guid.NewGuid().ToString(),DeploymentId=id};
            var graph=InventoryGraph.Create(false);graph.AddSupply("dust",2);
            fixture.Context.Inventories[connection]=new RaidInventoryState {Graph=graph};
            string original=JsonConvert.SerializeObject(graph);
            fixture.Context.BeginSave(connection,new RaidSession {SettlementId=id,Phase=RaidPhase.Extracted,PersistentDeployment=true,Dust=2});
            Require(backend.SettlementEntered.Task.Wait(3000),"Settlement not started.");
            graph.AddSupply("dust",1); fixture.Context.Dispose();backend.SettlementRelease.SetResult(true);
            Require(fixture.Context.ShutdownTask.Wait(3000),"Settlement shutdown failed.");
            Require(backend.SettlementInventory==original && backend.Paths.ToArray().Length==1,"Shutdown must preserve the exact extraction receipt instead of abandoning it.");
        }
        backend=new Backend {MissingInventory=true};
        using(var fixture=new Fixture(backend))
        {
            fixture.Context.Profiles[connection]=new RaidPersistenceContext.Profile {PlayerId=Guid.NewGuid().ToString()};
            fixture.Context.BeginDeploy(connection,0);
            try {fixture.Context.Deployments[connection].Task.GetAwaiter().GetResult();throw new Exception("Missing authoritative inventory accepted.");}
            catch(RaidPersistenceContext.LoadoutRejectedException ex) {Require(ex.Error==RaidLoadoutError.Rejected,"Invalid protocol must fail permanently instead of retrying forever.");}
        }
        // Delayed account snapshots and malformed responses must not overwrite accepted inventory.
        var account=typeof(AccountClient);var saved=AccountClient.Inventory;
        var backing=account.GetField("<Inventory>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);
        var applied=account.GetField("s_AppliedProfileRequest",BindingFlags.Static|BindingFlags.NonPublic);
        var apply=account.GetMethod("ApplyProfile",BindingFlags.Static|BindingFlags.NonPublic);
        var profileType=account.GetNestedType("Profile",BindingFlags.NonPublic);
        object request=Activator.CreateInstance(profileType,true);
        var newer=InventoryGraph.Create();newer.Version=10;backing.SetValue(null,newer);
        object savedRequest=applied.GetValue(null);
        try
        {
            profileType.GetField("InventoryJson").SetValue(request,JsonConvert.SerializeObject(InventoryGraph.Create()));
            apply.Invoke(null,new[] {request,(object)99L});Require(ReferenceEquals(AccountClient.Inventory,newer),"Older refresh rolled back inventory.");
            profileType.GetField("InventoryJson").SetValue(request,"{\"Version\":100,\"Items\":[null]}");
            try {apply.Invoke(null,new[] {request,(object)100L});throw new Exception("Invalid snapshot accepted.");}
            catch(TargetInvocationException ex) {Require(ex.InnerException is InvalidOperationException,"Malformed inventory produced an unexpected exception.");}
            Require(ReferenceEquals(AccountClient.Inventory,newer),"Invalid snapshot replaced good inventory.");
        }
        finally {backing.SetValue(null,saved);applied.SetValue(null,savedRequest);}
        Debug.Log("Inventory lifecycle checks passed: pending debit cleanup, exact settlement priority, frozen cargo, old-account snapshot rejection, malformed snapshot preservation.");
    }
}
