using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class MooncoreProgressionChecks
{
    public static void UpdateTranslations()
    {
        var translations=JObject.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"Localization/Translations.json")));
        var collection=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        foreach(var table in collection.StringTables)
        {
            bool mooncore=false;
            foreach(var pair in translations.Properties())
            {
                if(pair.Name=="Mooncore fragment")mooncore=true;
                if(!mooncore)continue;
                string value=table.LocaleIdentifier.Code=="zh-Hans" ? pair.Value.Value<string>() : pair.Name;
                if(table.GetEntry(pair.Name)?.Value==value)continue;
                table.AddEntry(pair.Name,value);EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }
    public static string Run()
    {
        int checks=0;
        void Check(bool ok,string name) { if(!ok)throw new InvalidOperationException("Mooncore: "+name); checks++; }
        ShopCommand Order(InventoryGraph g,ShopOperation op,string code)=>new ShopCommand { RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=g.Version,Operation=op,Code=code,Quantity=1 };
        InventoryItem Put(InventoryGraph g,string code,string parent="stash",bool recovered=true)
        {
            var item=new InventoryItem { Id=Guid.NewGuid().ToString("D"),Code=code };
            if(code=="mooncore_fragment") { item.FoundInRaid=true;item.MissionRecovered=recovered;item.MissionWorldId=Guid.NewGuid().ToString("D");item.MissionOperationId=Guid.NewGuid().ToString("D");item.MissionContentVersion=1; }
            Check(g.FindSpace(item,parent,out var region,out var x,out var y),"fixture space");
            item.Parent=parent;item.Region=region;item.X=x;item.Y=y;g.Items.Add(item);return item;
        }
        var g=InventoryGraph.Create(starter:false);
        Put(g,"mooncore_fragment",recovered:false);g.AddSupply("circuit_board",2,"stash");
        Check(ShopRules.Apply(g,Order(g,ShopOperation.Research,"mooncore_stabilization"),out _)=="research_materials" && g.Count("circuit_board")==2,"unextracted mooncore cannot spend or debit boards");
        g.Items.First(i=>i.Code=="mooncore_fragment").MissionRecovered=true;
        var research=Order(g,ShopOperation.Research,"mooncore_stabilization");
        Check(ShopRules.Apply(g,research,out var known)==null && known.Count("mooncore_fragment")==0 && known.Count("circuit_board")==0,"atomic research costs");
        Check(AlienBiology.MooncoreResearch.Unlocked(known) && known.Count("halo_mooncore_recycler")==0 && g.HaloResearchBranches==0,"knowledge only, original graph unchanged");
        Check(ShopRules.Apply(known,Order(known,ShopOperation.Research,research.Code),out _)=="research_complete","repeat research rejected");
        Check(ShopRules.Apply(g,Order(g,ShopOperation.Fabricate,"halo_mooncore_recycler"),out _)=="fabrication_locked","fabrication locked");
        Check(ShopRules.Apply(known,research,out _)=="inventory_Stale","stale version rejected");
        Check(ShopRules.Apply(known,Order(known,ShopOperation.Buy,"halo_mooncore_recycler"),out _)=="shop_invalid" && !HaloModules.Codes.Contains("halo_mooncore_recycler"),"no buy or cache bypass");
        var full=InventoryGraph.Create(starter:false);full.StashRows=3;full.HaloResearchBranches=8;
        var pack=Put(full,"small_pack");Put(full,"mooncore_fragment",pack.Id);full.AddSupply("copper_coil",1,pack.Id);
        while(full.AddSupply("dust",20,"stash")==InventoryError.None) { }
        int fullVersion=full.Version;
        Check(ShopRules.Apply(full,Order(full,ShopOperation.Fabricate,"halo_mooncore_recycler"),out _)=="inventory_Full" && full.Version==fullVersion && full.Count("mooncore_fragment")==1 && full.Count("copper_coil")==1,"full stash cannot debit nested materials");

        string directory=Path.GetFullPath("Library/BuildValidation/Mooncore");Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"offline-"+Guid.NewGuid().ToString("N")+".json");
        string moduleId=null;ShopCommand fabricate=null;
        try
        {
            using(var store=new OfflineProfileStore(file)) { }
            Put(g,"mooncore_fragment");g.AddSupply("copper_coil",2,"stash");
            research.ExpectedVersion=g.Version;
            var seed=JObject.Parse(File.ReadAllText(file));seed["Inventory"]=JObject.FromObject(g);File.WriteAllText(file,seed.ToString());
            using(var store=new OfflineProfileStore(file))
            {
                store.Trade(research);store.Trade(research);
                var account=RaidInventoryTransport.Decode(store.Read().InventoryJson);
                Check(account.HaloResearchBranches==8 && account.Count("mooncore_fragment")==1,"offline research retry charges once");
                fabricate=Order(account,ShopOperation.Fabricate,"halo_mooncore_recycler");store.Trade(fabricate);store.Trade(fabricate);
                account=RaidInventoryTransport.Decode(store.Read().InventoryJson);moduleId=account.Items.Single(i=>i.Code=="halo_mooncore_recycler").Id;
                Check(account.Count("mooncore_fragment")==0 && account.Count("copper_coil")==1 && account.Count("halo_mooncore_recycler")==1,"offline fabrication receipt charges and grants once");
                bool rejected=false;var altered=Order(account,ShopOperation.Fabricate,fabricate.Code);altered.RequestId=fabricate.RequestId;
                try {store.Trade(altered);}catch(AccountClient.InventoryRequestException e){rejected=e.ErrorCode=="shop_request_conflict";}
                Check(rejected,"request ID cannot change payload");
            }
            using(var reopened=new OfflineProfileStore(file))
            {
                reopened.Trade(fabricate);g=RaidInventoryTransport.Decode(reopened.Read().InventoryJson);
                Check(g.Find(moduleId)!=null && g.Count("halo_mooncore_recycler")==1 && AlienBiology.MooncoreResearch.Unlocked(g),"receipt and knowledge survive restart");
            }
        }
        finally { foreach(string suffix in new[]{"",".bak",".tmp",".lock"})if(File.Exists(file+suffix))File.Delete(file+suffix); }
        var rifle=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="rifle",Parent="equipment",Region="Primary",LoadedAmmo=4};g.Items.Add(rifle);
        Check(g.TryApply(new InventoryCommand {ExpectedVersion=g.Version,ItemId=moduleId,Parent=rifle.Id,Region="Storage"})==InventoryError.None,"install physical module");
        Check(HaloModules.Mask(g,rifle)==HaloModule.MooncoreRecycler && AlienBiology.Effect((ushort)HaloModules.Mask(g,rifle))==AlienFoodEffect.None,"knowledge and module do not occupy biology/activity bits");
        var registry=AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        foreach(uint weapon in new[]{DollSingerWeapons.Halo,DollSingerWeapons.Shotgun})
        {
            var before=registry.GetWeaponData(weapon);var after=registry.GetWeaponData(weapon,(ushort)HaloModule.MooncoreRecycler);
            Check(after.EnergyPerRound==Mathf.CeilToInt(before.EnergyPerRound*(2f/3f)) && Mathf.Approximately(after.ReloadTime,before.ReloadTime*1.2f) && after.Damage==before.Damage && after.MagazineSize==before.MagazineSize,"real registry energy/time tradeoff, no damage/capacity boost");
            ushort mask=(ushort)(HaloModule.MooncoreRecycler|HaloModule.HitMarker|HaloModule.BiologicalCharging|HaloModule.BiologicalResearch|HaloModule.BiologicalCapacity|HaloModule.BiologicalStability);
            mask|=(ushort)((int)AlienFoodEffect.ContractilePower<<12);
            var combined=registry.GetWeaponData(weapon,mask);
            Check(combined.EnergyPerRound==Mathf.CeilToInt(after.EnergyPerRound*1.25f) && Mathf.Approximately(combined.ReloadTime,before.ReloadTime*1.2f*.75f) && Mathf.Approximately(combined.Damage,before.Damage*1.6f*1.4f),"existing rounding order and biology/activity compose");
        }
        var state=new PredictedPlayerGhost();DollSingerWeapons.SyncEquipment(ref state,g,registry);
        Check(state.PrimaryModules==(ushort)HaloModule.MooncoreRecycler && state.PrimaryAmmo==4,"real equipment mask and paid ammo retained");
        var raid=g.ExtractLoadout();var drop=LootInventoryExchange.DropOnDeath(raid);
        Check(drop.Find(moduleId)?.Parent==rifle.Id && HaloModules.Mask(drop,drop.Find(rifle.Id))==HaloModule.MooncoreRecycler && g.Find(moduleId)==null && AlienBiology.MooncoreResearch.Unlocked(g),"deployment/death preserves module identity and permanent knowledge");
        return "PASS "+checks+" targeted Mooncore checks: recovered source, atomic costs/full storage, version/retry/restart, bit isolation, actual weapon parameters, equipment/death identity. No player-profile writes or live networking.";
    }
}
