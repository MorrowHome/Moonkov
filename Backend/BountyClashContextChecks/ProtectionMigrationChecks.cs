using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Context=Unity.MP_FPS.RaidPersistenceContext;

internal static class ProtectionMigrationChecks
{
    public static void Run()
    {
        int checks=0;void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        InventoryGraph Account()=>InventoryGraph.Create(true,false);
        InventoryItem Helmet(string code="helmet",float oxygen=-1)=>new(){Id=Guid.NewGuid().ToString("D"),Code=code,Parent="equipment",Region="Helmet",SuitOxygen=oxygen};
        var empty=Account();var inbox=new RecoveryInbox();
        Check(ProtectionKitMigration.TryApply(empty,inbox,out var equipped,out var noOverflow),"new profile migration did not land");
        var granted=equipped.Equipped("Helmet");
        Check(granted?.Code=="energy_head_dome" && equipped.ProtectionKitVersion==1 && empty.ProtectionKitVersion==0 && empty.Equipped("Helmet")==null,"grant changed source or lost receipt");
        Check(!ProtectionKitMigration.TryApply(equipped,noOverflow,out _,out _) && equipped.Items.Count(i=>i.Code=="energy_head_dome")==1,"repeat read granted another dome");
        var compatible=Account();var oldHelmet=Helmet(oxygen:0);compatible.Items.Add(oldHelmet);
        var shield=new InventoryItem{Id=Guid.NewGuid().ToString("D"),Code="energy_shield_generator",Parent="stash",Region="main",X=3,Y=3,ShieldCharge=0,ShieldEnergy=17};compatible.Items.Add(shield);
        Check(ProtectionKitMigration.TryApply(compatible,new RecoveryInbox(),out var old,out _),"compatible old helmet did not set receipt");
        Check(old.Equipped("Helmet").Id==oldHelmet.Id && old.Equipped("Helmet").SuitOxygen==0 && !old.Items.Any(i=>i.Code=="energy_head_dome"),"old depleted helmet replaced or refilled");
        var roundtrip=InventoryRepository.Decode(InventoryRepository.Encode(old));
        Check(roundtrip.ProtectionKitVersion==1 && roundtrip.Find(oldHelmet.Id).SuitOxygen==0 && roundtrip.Find(shield.Id).ShieldCharge==0 && roundtrip.Find(shield.Id).ShieldEnergy==17,"clone/JSON lost depleted protection resources");
        // A legal but heavy existing kit must receive its dome in storage instead
        // of exceeding the existing carried-weight limit.
        var heavy=Account();var pack=new InventoryItem{Id=Guid.NewGuid().ToString("D"),Code="backpack",Parent="equipment",Region="Backpack"};heavy.Items.Add(pack);
        Check(heavy.AddSupply("alloy",72,pack.Id)==InventoryError.None && heavy.AddSupply("dust",1,"pockets")==InventoryError.None,"heavy fixture placement failed");
        Check(heavy.CarriedWeight<=InventoryCatalog.CarryWeightLimit && heavy.CarriedWeight+InventoryCatalog.Get("energy_head_dome").Weight>InventoryCatalog.CarryWeightLimit,"heavy fixture is not the intended boundary");
        Check(ProtectionKitMigration.TryApply(heavy,new RecoveryInbox(),out var storedHeavy,out _),"heavy kit migration failed");
        Check(storedHeavy.Items.Single(i=>i.Code=="energy_head_dome").Parent=="stash" && storedHeavy.CarriedWeight==heavy.CarriedWeight,"migration equipped an overweight dome");
        var full=Account();full.WeaponKitVersion=1;full.StashRows=4096;
        for(int i=0;i<InventoryCatalog.MaxItems-3;i++)full.Items.Add(new InventoryItem{Id=Guid.NewGuid().ToString("D"),Code="dust",Quantity=1,Parent="stash",Region="main",X=i%10,Y=i/10});
        Check(ProtectionKitMigration.TryApply(full,new RecoveryInbox(),out var stillFull,out var pending),"full account migration lost grant");
        Check(stillFull.Items.Count==InventoryCatalog.MaxItems && pending.Batches.Count==1 && pending.Roots().Single().Code=="energy_head_dome" && pending.Validate(stillFull)==InventoryError.None,"full account exceeded cap or made invalid overflow");
        var blocked=new RecoveryInbox();
        for(int n=0;n<2;n++)
        {
            var batch=InventoryGraph.Create(false,false);batch.Items.Add(Helmet());
            Check(batch.AddSupply("dust",80,"pockets")==InventoryError.None,"blocked recovery fixture failed");blocked.Batches.Add(batch);
        }
        var before=InventoryRepository.Encode(full);var beforeInbox=JsonConvert.SerializeObject(blocked);
        Check(!ProtectionKitMigration.TryApply(full,blocked,out _,out _) && full.ProtectionKitVersion==0 && InventoryRepository.Encode(full)==before && JsonConvert.SerializeObject(blocked)==beforeInbox,"extreme full state consumed receipt or lost an item");
        blocked.Batches[0].Items.RemoveAll(i=>i.Parent=="equipment"&&i.Region=="Helmet");blocked.Version++;
        Check(ProtectionKitMigration.TryApply(full,blocked,out var afterRoom,out var resumed) && resumed.Batches.Count==2 && resumed.Batches[0].Equipped("Helmet").Code=="energy_head_dome" && resumed.Batches.Sum(g=>g.Count("dust"))==160 && afterRoom.ProtectionKitVersion==1,"migration did not resume into existing recovery space");
        string parent=Path.GetFullPath("Library/BuildValidation/ProtectionMigration");string directory=Path.Combine(parent,"kit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"profile.json"),player=Guid.NewGuid().ToString("D");
            var graph=Account();graph.WeaponKitVersion=1;
            File.WriteAllText(path,JsonConvert.SerializeObject(new{Format=2,PlayerId=player,Inventory=graph,Recovery=new RecoveryInbox(),Receipts=new Dictionary<string,string>(),MissionArtifacts=new HashSet<string>()}));
            string id,first;
            using(var store=new OfflineProfileStore(path))
            {
                var migrated=RaidInventoryTransport.Decode(store.Read().InventoryJson);id=migrated.Equipped("Helmet").Id;first=File.ReadAllText(path);
                Check(JObject.Parse(first)["Receipts"]!.Value<string>("migration/protection-kit-v1")=="1" && migrated.ProtectionKitVersion==1,"offline grant and migration receipt were not committed together");
            }
            using(var store=new OfflineProfileStore(path))
            {
                Check(File.ReadAllText(path)==first && RaidInventoryTransport.Decode(store.Read().InventoryJson).Equipped("Helmet").Id==id,"restart rewrote/regranted kit");
                var deployment=new Context.Deployment{PlayerId=player,DeploymentId=Guid.NewGuid().ToString("D"),Cells=0};var live=store.Deploy(deployment);
                Check(RaidInventoryTransport.Decode(store.Read().InventoryJson).Equipped("Helmet")==null,"active raid got another helmet during profile read");
                var raid=RaidInventoryTransport.Decode(live.RaidInventoryJson);
                store.Settle(new Context.Settlement{PlayerId=player,DeploymentId=deployment.DeploymentId,SettlementId=deployment.DeploymentId,Outcome="Dead",InventoryJson=live.RaidInventoryJson,
                    Dust=raid.Count("dust",true),Alloy=raid.Count("alloy",true),Cells=raid.Count("cells",true)});
                Check(!RaidInventoryTransport.Decode(store.Read().InventoryJson).Items.Any(i=>i.Code=="energy_head_dome"),"terminal death awarded another free dome");
            }
            using(var store=new OfflineProfileStore(path))Check(!RaidInventoryTransport.Decode(store.Read().InventoryJson).Items.Any(i=>i.Code=="energy_head_dome"),"reopening after death regranted kit");
            // Model fields can be absent in an older writer; the established
            // receipt dictionary still forbids a second grant after kit loss.
            var oldWriter=JObject.Parse(File.ReadAllText(path));((JObject)oldWriter["Inventory"]!).Remove("ProtectionKitVersion");File.WriteAllText(path,oldWriter.ToString());
            using(var store=new OfflineProfileStore(path))
            {
                var restored=RaidInventoryTransport.Decode(store.Read().InventoryJson);
                Check(restored.ProtectionKitVersion==1 && !restored.Items.Any(i=>i.Code=="energy_head_dome"),"missing model field defeated persistent one-time receipt");
            }
        }
        finally{Directory.Delete(directory,true);}
        Console.WriteLine($"PASS {checks} protection migration pure/offline atomic checks: once-only, compatible depleted helmet, resource clone/JSON, weight/stash, full recovery/defer/resume, restart/death and old-writer receipt. No user profile/Prefs/DB touched.");
    }
}
