using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using Object = UnityEngine.Object;
using BodyPart = Unity.MP_FPS.BodyPart;

public static class AlienHarvestChecks
{
    private static int s_Passed;
    private static void Check(bool value,string name)
    { if(!value)throw new InvalidOperationException("Alien harvest: "+name);s_Passed++; }

    [MenuItem("Tools/Moonkov/Aliens/Check dissection food and permanent research")]
    public static void Run()
    {
        s_Passed=0;
        var loot=new RaidLootContainers();
        var boss=AlienHarvest.Create(true,new System.Random(1));
        Check(boss.Validate()==InventoryError.None && boss.Count("alien_raw_meat")==4 && boss.Count("alien_neural_core")==1,"boss harvest");
        int id=loot.DropHarvest(boss,default);
        Check(loot.RequiresDissection(id),"sealed remains");
        var first=new RaidInventoryState();var second=new RaidInventoryState();
        first.BeginDissection(id,0,default,0);second.BeginDissection(id,0,default,0);
        first.RefreshDissection(loot,true,default,0,5.99);
        Check(first.OpenedLootId==-1 && first.DissectingLootId==id && loot.RequiresDissection(id),"no early access");
        first.RefreshDissection(loot,true,default,0,6);
        int version=loot.Containers[id].Version;
        second.RefreshDissection(loot,true,default,0,6);
        Check(first.OpenedLootId==id && second.OpenedLootId==id && version==2 && loot.Containers[id].Version==version,"one shared harvest");
        foreach(int interruption in new[]{0,1,2})
        {
            int fresh=loot.DropHarvest(AlienHarvest.Create(false,new System.Random(5)),default);
            first.BeginDissection(fresh,10,default,1);
            first.RefreshDissection(loot,interruption!=0,interruption==1 ? new Unity.Mathematics.float3(1,0,0) : default,
                interruption==2 ? 2u : 1u,16);
            Check(first.OpenedLootId==-1 && first.DissectingLootId==-1 && loot.RequiresDissection(fresh),"interrupted harvest "+interruption);
        }
        var transport=new RaidInventoryClientState();
        string json=RaidInventoryTransport.EncodeSnapshot(InventoryGraph.Create(false,false),-1,0,id,4);
        int chunks=(json.Length+399)/400;bool received=false;
        for(int i=0;i<chunks;i++)received=transport.Receive(new RaidInventoryChunkV2Rpc {RaidId=1,Sequence=1,Index=i,Count=chunks,
            Json=json.Substring(i*400,Math.Min(400,json.Length-i*400))},RaidInventoryTransport.Decode);
        Check(received && transport.DissectingLootId==id && transport.DissectionRemaining==4 && transport.Graph.Find("loot")==null,"pending snapshot hides organs");

        var food=InventoryGraph.Create(false,false);food.AddSupply("alien_raw_meat",2);
        var meat=food.Items.Single(i=>i.Code=="alien_raw_meat");
        var health=default(PredictedPlayerGhost);RaidHealth.Initialize(ref health);RaidNutrition.Initialize(ref health);
        RaidHealth.Set(ref health,BodyPart.Chest,45);
        float before=health.CurrentHealth;
        Check(RaidNutrition.Consume(ref health,food,meat.Id,food.Version)==InventoryError.None &&
            Mathf.Abs(health.CurrentHealth-before-20)<.01f && health.Hydration==80 && health.Satiety==55,"meat heals with thirst cost");
        Check(RaidNutrition.Consume(ref health,food,meat.Id,food.Version-1)==InventoryError.Stale && meat.Quantity==1,"food replay rejected");
        var organ=InventoryGraph.Create(false,false);organ.AddSupply("alien_neural_core",1);
        string organId=organ.Items.Single(i=>i.Code=="alien_neural_core").Id;
        Check(RaidNutrition.Consume(ref health,organ,organId,organ.Version)==InventoryError.None && health.AlienSurgeSeconds==60 &&
            organ.Count("alien_neural_core")==0 && (RaidNutrition.Effects(health)&SurvivalEffects.AlienSurge)!=0,"organ consumed for buff");
        RaidNutrition.Tick(ref health,61,100000,100000,100000,100000,0,0,1);
        Check(health.AlienSurgeSeconds==0,"buff expires");

        var account=InventoryGraph.Create(starter:false);
        account.AddSupply("alien_neural_core",3,"stash");account.AddSupply("alien_tissue",5,"stash");
        var command=new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=account.Version,Operation=ShopOperation.Research,Code="halo_biology",Quantity=1};
        Check(ShopRules.Apply(account,command,out var upgraded)==null && upgraded.HaloResearchLevel==1 &&
            upgraded.Count("alien_neural_core")==0 && upgraded.Count("alien_tissue")==0 && account.HaloResearchLevel==0,"atomic permanent research");
        Check(ShopRules.Apply(upgraded,command,out _)=="inventory_Stale","research replay version guard");
        command.ExpectedVersion=upgraded.Version;
        Check(ShopRules.Apply(upgraded,command,out _)=="research_complete","cannot buy same research again");
        var poor=InventoryGraph.Create(starter:false);poor.AddSupply("alien_neural_core",2,"stash");poor.AddSupply("alien_tissue",5,"stash");
        command.ExpectedVersion=poor.Version;
        Check(ShopRules.Apply(poor,command,out _)=="research_materials" && poor.Count("alien_neural_core")==2,"failed research keeps materials");
        var saved=RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(upgraded));
        var deployed=saved.ExtractLoadout();
        Check(saved.HaloResearchLevel==1 && deployed.HaloResearchLevel==1,"research survives save and deploy");
        deployed.HaloResearchLevel=0;
        Check(saved.ReturnLoadout(deployed)==InventoryError.None && saved.HaloResearchLevel==1,"settlement cannot overwrite research");

        var registry=ScriptableObject.CreateInstance<WeaponRegistry>();
        var weapon=ScriptableObject.CreateInstance<WeaponData>();weapon.Damage=10;
        registry.Weapons=new List<WeaponData> {weapon,weapon,weapon,weapon,weapon,weapon};
        try
        {
            foreach(uint weaponId in new[]{DollSingerWeapons.Halo,DollSingerWeapons.Shotgun,DollSingerWeapons.Revolver,DollSingerWeapons.Sniper})
            {
                Check(Mathf.Abs(registry.GetWeaponData(weaponId,(byte)HaloModule.BiologicalResearch).Damage-16)<.01f,"permanent halo damage "+weaponId);
                Check(Mathf.Abs(registry.GetWeaponData(weaponId,(byte)HaloModule.AlienSurge).Damage-13)<.01f,"temporary halo damage "+weaponId);
                Check(Mathf.Abs(registry.GetWeaponData(weaponId,(byte)(HaloModule.BiologicalResearch|HaloModule.AlienSurge)).Damage-20.8f)<.01f,"combined damage "+weaponId);
            }
            Check(weapon.Damage==10,"shared weapon asset unchanged");
        }
        finally {Object.DestroyImmediate(registry);Object.DestroyImmediate(weapon);}
        CheckSpeciesAndBranches();
        Directory.CreateDirectory("Library/BuildValidation/AlienHarvest");
        File.WriteAllText("Library/BuildValidation/AlienHarvest/report.json",new JObject { ["passed"]=s_Passed,["scope"]="Edit Mode rules and transport; no Play or multiplayer acceptance" }.ToString());
        Debug.Log("Alien harvest checks passed: "+s_Passed);
    }

    private static void CheckSpeciesAndBranches()
    {
        var names=Enum.GetNames(typeof(AlienSpecies));
        Check(AlienBiology.Organs.Select(o=>o.Species).Distinct().Count()==AlienBiology.Organs.Length &&
            AlienBiology.Organs.Select(o=>o.Code).Distinct().Count()==AlienBiology.Organs.Length,"unique canonical organ definitions");
        foreach(var definition in AlienBiology.Organs)
            Check(names.Contains(definition.Species) && ReferenceEquals(AlienBiology.Species(definition.Species),definition),"canonical species coverage "+definition.Species);
        foreach(string species in names)
        {
            var organ=AlienBiology.Species(species);
            Check(organ!=null && InventoryCatalog.Get(organ.Code)!=null,"registered species "+species);
            Check(organ.Species==species || species=="StiltWalker" && organ.Species=="Tentacle" &&
                ReferenceEquals(organ,AlienBiology.Species("Tentacle")),"explicit species mapping "+species);
            foreach(bool boss in new[]{false,true})
            {
                var cache=AlienHarvest.Create(boss,new System.Random(1),species);
                Check(cache.Validate()==InventoryError.None && cache.Count(organ.Code)==1 && cache.Items.Where(i=>i.Code.StartsWith("alien_")).All(i=>i.FoundInRaid),"species harvest "+species+boss);
            }
            var carried=InventoryGraph.Create(false,false);carried.AddSupply(organ.Code,1);
            var health=default(PredictedPlayerGhost);RaidHealth.Initialize(ref health);health.Energy=30;health.Hydration=100;health.Satiety=0;
            RaidHealth.Set(ref health,BodyPart.Chest,50);
            Check(RaidNutrition.Consume(ref health,carried,carried.Items.Single(i=>i.Code==organ.Code).Id,carried.Version)==InventoryError.None,"species organ use "+species);
            if(organ.Food.EffectSeconds>0)
            {
                Check(health.AlienFoodEffect==organ.Food.Effect && health.AlienFoodEffectSeconds==organ.Food.EffectSeconds,"replicated activity "+species);
                float hp=health.CurrentHealth;
                RaidNutrition.Tick(ref health,2,100,100,100,10000,0,0,1);
                if(species=="Leech")Check(Mathf.Abs(health.CurrentHealth-hp-2)<.01f,"regeneration heals living tissue");
                if(species=="Centipede")Check(Mathf.Abs(health.CurrentHealth-hp+1.2f)<.02f,"venom damage is authoritative");
                if(species=="Slime")Check(Mathf.Abs(RaidHealth.Damage(ref health,BodyPart.LeftArm,20,2)-15)<.01f,"gel combat reduction");
                RaidNutrition.Tick(ref health,100,10000,10000,10000,10000,0,0,3);
                Check(health.AlienFoodEffectSeconds==0 && health.AlienFoodEffect==AlienFoodEffect.None,"species activity expires "+species);
            }
        }
        var graph=InventoryGraph.Create(starter:false);graph.HaloResearchLevel=1;
        Check(AlienBiology.Research.Where(r=>r.Branch!=0).All(r=>r.Branch==1 || r.Branch==2 || r.Branch==4 || r.Branch==8),"explicit research branch coverage");
        foreach(var research in AlienBiology.Research.Where(r=>r.Branch==1 || r.Branch==2 || r.Branch==4))
        {
            for(int i=0;i<research.Materials.Length;i++)graph.AddSupply(research.Materials[i],research.Quantities[i],"stash");
            var command=new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=graph.Version,Operation=ShopOperation.Research,Code=research.Code,Quantity=1};
            Check(ShopRules.Apply(graph,command,out var next)==null && research.Unlocked(next),"branch unlock "+research.Code);graph=next;
        }
        var decoded=RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(graph));
        Check(decoded.HaloResearchBranches==7 && decoded.HaloResearchLevel==1 && decoded.ExtractLoadout().HaloResearchBranches==7,"old firepower and new branches survive save/deploy");
        var mooncore=AlienBiology.MooncoreResearch;
        for(int i=0;i<mooncore.Materials.Length;i++)
            if(!MissionInventory.IsArtifact(mooncore.Materials[i]))
                Check(graph.AddSupply(mooncore.Materials[i],mooncore.Quantities[i],"stash")==InventoryError.None,"mooncore material fixture "+mooncore.Materials[i]);
        var fragment=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="mooncore_fragment",Quantity=1,FoundInRaid=true,
            MissionWorldId=Guid.NewGuid().ToString("D"),MissionOperationId=Guid.NewGuid().ToString("D"),MissionContentVersion=1};
        Check(graph.FindSpace(fragment,"stash",out var region,out int x,out int y),"mooncore fixture storage space");
        fragment.Parent="stash";fragment.Region=region;fragment.X=x;fragment.Y=y;graph.Items.Add(fragment);graph.Version++;
        Check(graph.Validate()==InventoryError.None,"valid sourced mooncore fixture");
        var mooncoreCommand=new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=graph.Version,
            Operation=ShopOperation.Research,Code=mooncore.Code,Quantity=1};
        Check(ShopRules.Apply(graph,mooncoreCommand,out _)=="research_materials" && graph.HaloResearchBranches==7,"unrecovered mooncore cannot fund research");
        fragment.MissionRecovered=true;
        Check(ShopRules.Apply(graph,mooncoreCommand,out var mooncoreUnlocked)==null && mooncore.Unlocked(mooncoreUnlocked) &&
            mooncoreUnlocked.HaloResearchBranches==15 && graph.HaloResearchBranches==7,"recovered mooncore unlock preserves biological branches");
        var mooncoreSaved=RaidInventoryTransport.Decode(RaidInventoryTransport.Encode(mooncoreUnlocked));
        Check(mooncoreSaved.HaloResearchBranches==15 && mooncoreSaved.ExtractLoadout().HaloResearchBranches==15,"mooncore and biological branches survive save/deploy");
        var registry=ScriptableObject.CreateInstance<WeaponRegistry>();var weapon=ScriptableObject.CreateInstance<WeaponData>();
        weapon.MagazineSize=10;weapon.ReloadTime=4;weapon.SpreadDegrees=10;weapon.Damage=10;weapon.CooldownInMs=1;
        registry.Weapons=new List<WeaponData>{weapon,weapon,weapon,weapon,weapon,weapon};
        try
        {
            foreach(uint id in new[]{DollSingerWeapons.Halo,DollSingerWeapons.Revolver,DollSingerWeapons.Shotgun,DollSingerWeapons.Sniper})
            {
                var result=registry.GetWeaponData(id,(ushort)(HaloModule.BiologicalCapacity|HaloModule.BiologicalCharging|HaloModule.BiologicalStability));
                Check(result.MagazineSize==14 && Mathf.Abs(result.ReloadTime-3)<.01f && Mathf.Abs(result.SpreadDegrees-7)<.01f,"branch stats "+id);
                Check(Mathf.Abs(registry.GetWeaponData(id,(ushort)((int)AlienFoodEffect.ContractilePower<<12)).Damage-14)<.01f,"contractile power "+id);
                Check(Mathf.Abs(registry.GetWeaponData(id,(ushort)((int)AlienFoodEffect.NeuralOverdrive<<12)).CooldownInMs-.8f)<.01f,"neural firing interval "+id);
            }
        }
        finally {Object.DestroyImmediate(registry);Object.DestroyImmediate(weapon);}
    }

    public static void RunBatch()
    {
        try
        {
            UpdateTranslations();Run();EditorApplication.Exit(0);
        }
        catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
    }

    private static void UpdateTranslations()
    {
        var translations=JObject.Parse(File.ReadAllText("Assets/Localization/Translations.json"));
        var collection=LocalizationEditorSettings.GetStringTableCollection("MoonkovUI");
        if(collection==null)throw new InvalidOperationException("MoonkovUI table missing");
        foreach(var reference in collection.Tables)
        {
            var table=(StringTable)reference.asset;
            foreach(var pair in translations.Properties())
            {
                string value=table.LocaleIdentifier.Code=="zh-Hans" ? pair.Value.Value<string>() : pair.Name;
                if(table.GetEntry(pair.Name)?.LocalizedValue==value)continue;
                table.AddEntry(pair.Name,value);EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssetIfDirty(table);
        }
        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssetIfDirty(collection.SharedData);
    }

    public static void BuildTranslationsBatch()
    {
        try
        {
            UpdateTranslations();
            string sharedPath=AssetDatabase.GetAssetPath(LocalizationEditorSettings.GetStringTableCollection("MoonkovUI").SharedData);
            if(!File.ReadAllText(sharedPath).Contains("Rare alien neural organ"))
                throw new InvalidOperationException("Shared localization keys were not saved");
            Debug.Log("Alien harvest localization saved with shared keys.");EditorApplication.Exit(0);
        }
        catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
