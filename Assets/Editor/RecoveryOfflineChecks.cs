using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;

public static class RecoveryOfflineChecks
{
    [MenuItem("Tools/Moonkov/Check Offline Recovery Inbox")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Run in Edit Mode.");
        int checks=0;void Check(bool ok,string name) {if(!ok)throw new Exception(name);checks++;}
        string directory=Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath,"../LocalData")),"recovery-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);string path=Path.Combine(directory,"profile.json");
        try
        {
            var account=InventoryGraph.Create(true,false);account.WeaponKitVersion=1;account.StashRows=4096;
            for(int i=0;i<InventoryCatalog.MaxItems-3;i++)account.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Quantity=1,Parent="stash",Region="main",X=i%10,Y=i/10});
            string player=Guid.NewGuid().ToString("D"),deployment=Guid.NewGuid().ToString("D");
            var raid=InventoryGraph.Create(false,false);raid.WeaponKitVersion=1;
            var artifact=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="mooncore_fragment",Parent="pockets",Region="1",FoundInRaid=true,
                MissionWorldId=Guid.NewGuid().ToString("D"),MissionOperationId=Guid.NewGuid().ToString("D"),MissionContentVersion=1};raid.Items.Add(artifact);
            // Exact pre-migration Format=1 shape: there is no Recovery property.
            File.WriteAllText(path,JsonConvert.SerializeObject(new {Format=1,PlayerId=player,Inventory=account,DeploymentId=deployment,
                RaidInventoryJson=RaidInventoryTransport.Encode(InventoryGraph.Create(false,false)),CarriedCells=0,Receipts=new Dictionary<string,string>(),MissionArtifacts=new HashSet<string>()}));
            var settlement=new RaidPersistenceContext.Settlement {PlayerId=player,SettlementId=deployment,DeploymentId=deployment,Outcome="Extracted",InventoryJson=RaidInventoryTransport.Encode(raid)};
            using(var store=new OfflineProfileStore(path))
            {
                Check(store.ReadRecovery().ItemCount()==0,"Old save did not migrate to empty inbox");
                var saved=store.Settle(settlement);var inbox=store.ReadRecovery();
                Check(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path)).Value<int>("Format")==2,"New pending save must reject older readers rather than silently dropping Recovery");
                Check(!store.RaidActive && inbox.Batches.Count==2 && RaidInventoryTransport.Decode(saved.InventoryJson).MissionFirsts==3,"Full old save did not close deployment/save facts");
                Check(inbox.Batches.SelectMany(b=>b.Items).Single(i=>i.Id==artifact.Id).MissionRecovered && inbox.Batches.Sum(b=>b.Count("dust"))==40,"Pending source/reward lost");
                store.Settle(settlement);Check(store.ReadRecovery().ItemCount()==inbox.ItemCount(),"Offline settlement replay duplicated inbox");
                try {store.Deploy(new RaidPersistenceContext.Deployment {PlayerId=player,DeploymentId=Guid.NewGuid().ToString("D"),Cells=0});throw new Exception("Pending deployment accepted");}
                catch(RaidPersistenceContext.LoadoutRejectedException) {checks++;}
            }
            RecoveryClaim request;
            string claimedJson;
            using(var store=new OfflineProfileStore(path))
            {
                Check(!store.RaidActive && store.ReadRecovery().Batches.Count==2,"Restart lost pending extraction");
                for(int i=0;i<4;i++)
                {
                    var graph=RaidInventoryTransport.Decode(store.Read().InventoryJson);var dust=graph.Items.Where(x=>x.Code=="dust").Take(2).ToArray();
                    store.Move(new InventoryCommand {ExpectedVersion=graph.Version,Operation=InventoryOperation.Merge,ItemId=dust[1].Id,TargetId=dust[0].Id});
                }
                var profile=store.Read();request=new RecoveryClaim {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=RaidInventoryTransport.Decode(profile.InventoryJson).Version,ExpectedRecoveryVersion=store.ReadRecovery().Version};
                store.ClaimRecovery(request);claimedJson=store.Read().InventoryJson;
                Check(store.ReadRecovery().ItemCount()==0 && RaidInventoryTransport.Decode(claimedJson).Find(artifact.Id).MissionRecovered,"Claim after sorting lost artifact");
                store.ClaimRecovery(request);Check(store.Read().InventoryJson==claimedJson,"Offline claim replay duplicated items");
            }
            using(var store=new OfflineProfileStore(path))
            {
                store.ClaimRecovery(request);Check(store.Read().InventoryJson==claimedJson && store.ReadRecovery().ItemCount()==0,"Restart lost claim receipt");
                Check(RaidInventoryTransport.Decode(claimedJson).MissionFirsts==3,"Claim re-awarded first facts");
                var next=store.Deploy(new RaidPersistenceContext.Deployment {PlayerId=player,DeploymentId=Guid.NewGuid().ToString("D"),Cells=0});
                Check(next.DeploymentId!=null,"Empty inbox blocks next deployment");
            }
            string unsupported=Path.Combine(directory,"unsupported.json");
            var future=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));future["Format"]=999;
            string original=future.ToString();File.WriteAllText(unsupported,original);File.WriteAllText(unsupported+".bak","backup-preserved");
            try {using var rejected=new OfflineProfileStore(unsupported);throw new Exception("Unknown save version accepted");}
            catch(InvalidDataException) {checks++;}
            Check(File.ReadAllText(unsupported)==original && File.ReadAllText(unsupported+".bak")=="backup-preserved","Unsupported version overwrote the file or backup");
            Debug.Log($"Offline recovery checks passed: {checks} assertions; pre-Recovery Format1 save migration, full extraction, pending mission source/first rewards, restart, sorting, claim replay and deployment gate. Isolated save deleted.");
        }
        finally {Directory.Delete(directory,true);}
    }
}
