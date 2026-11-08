using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

internal static class FieldExpansionChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
        string[] codes={"oxygen_recycler","glare_filter","survey_multitool","hemostatic_bandage","rescue_kit",
            "hydration_gel","compressed_ration","emergency_oxygen","flash_grenade","poison_bomb","regeneration_shot"};
        foreach(string code in codes)
        {
            var graph=InventoryGraph.Create(true,false);
            graph.AddSupply("dust",200,InventoryCatalog.Stash);
            int original=graph.Count("dust");var offer=ShopCatalog.Find(code);
            Check(offer!=null && offer.BuyDust>offer.SellDust && offer.SellDust>0,"positive spread "+code);
            int quantity=Math.Min(2,InventoryCatalog.Get(code).MaxStack);
            var command=new ShopCommand{Operation=ShopOperation.Buy,Code=code,Quantity=quantity,ExpectedVersion=graph.Version,RequestId=Guid.NewGuid().ToString()};
            Check(ShopRules.Apply(graph,command,out var bought)==null,"purchase "+code);
            Check(bought.Count(code)==quantity && bought.Count("dust")==original-quantity*offer!.BuyDust && graph.Count(code)==0,"atomic purchase "+code);
            Check(bought.Validate()==InventoryError.None,"legal purchased stacks "+code);
            var product=bought.Items.First(i=>i.Code==code);
            Check(!bought.MedicalAccessible(product) && !bought.ConsumableAccessible(product) && !TacticalItemRules.Accessible(bought,product),"stash grants no use "+code);
            var stale=new ShopCommand{Operation=ShopOperation.Buy,Code=code,Quantity=1,ExpectedVersion=graph.Version-1,RequestId=Guid.NewGuid().ToString()};
            Check(ShopRules.Apply(graph,stale,out var rejected)!=null && rejected==null && graph.Count("dust")==original,"stale purchase is atomic "+code);
        }
        var equipment=InventoryGraph.Create(true,false);
        var injection=TacticalItemRules.Find("regeneration_shot");
        Check(injection.Injectable && !injection.Throwable && !injection.Deployable && !injection.Trap,"regeneration follows injection flow");
        Check(!TacticalItemRules.IsInjection(TacticalItemKind.Flash) && !TacticalItemRules.IsInjection(TacticalItemKind.PoisonBomb),"grenades remain distinct from injections");
        equipment.AddSupply("oxygen_recycler",1,InventoryCatalog.Stash);equipment.AddSupply("glare_filter",1,InventoryCatalog.Stash);
        Check(SpecialEquipmentRules.OxygenDrainMultiplier(equipment)==1 && SpecialEquipmentRules.GlareMultiplier(equipment)==1,"stored passives inactive");
        var recycler=equipment.Items.First(i=>i.Code=="oxygen_recycler");recycler.Parent=InventoryCatalog.Equipment;recycler.Region="Special1";recycler.X=recycler.Y=0;
        var filter=equipment.Items.First(i=>i.Code=="glare_filter");filter.Parent=InventoryCatalog.Equipment;filter.Region="Special2";filter.X=filter.Y=0;
        Check(equipment.Validate()==InventoryError.None && SpecialEquipmentRules.OxygenDrainMultiplier(equipment)==.75f && SpecialEquipmentRules.GlareMultiplier(equipment)==.4f,"equipped passives");
        equipment.Items.Add(new InventoryItem{Id=Guid.NewGuid().ToString(),Code="oxygen_recycler",Parent=InventoryCatalog.Equipment,Region="Special3"});
        Check(equipment.Validate()==InventoryError.None && SpecialEquipmentRules.OxygenDrainMultiplier(equipment)==.75f,"duplicate passives do not stack");
        Check(SpecialEquipmentRules.Compass("survey_multitool") && SpecialEquipmentRules.Rangefinder("survey_multitool") &&
            !SpecialEquipmentRules.Compass("laser_rangefinder") && !SpecialEquipmentRules.Rangefinder("survey_compass"),"survey functions remain distinct");
        Check(TacticalItemRules.FlashStrength(2,1)==1 && TacticalItemRules.FlashStrength(8,1)==.5f,"flash distance falloff");
        Check(TacticalItemRules.FlashStrength(2,-1)==0 && TacticalItemRules.FlashStrength(12,1)==0 &&
            TacticalItemRules.FlashStrength(float.NaN,1)==0,"flash gaze and invalid boundaries");
        foreach(var code in new[]{"flash_grenade","poison_bomb"})
        {
            var tactical=TacticalItemRules.Find(code);
            Check(tactical.Throwable && !tactical.Deployable && !tactical.Trap,"handheld flow "+code);
            Check(TacticalItemRules.BlastDamage(tactical.Kind,0)==0,"utility grenade has no blast damage "+code);
        }
        var found=new HashSet<string>();var random=new Random(88120);
        for(int n=0;n<800;n++)
            foreach(RaidLootProfile profile in Enum.GetValues<RaidLootProfile>())
            {
                var cache=RaidLootTable.Create(profile,random);
                if(cache.Validate()!=InventoryError.None)throw new Exception("invalid themed cache");
                foreach(var item in cache.Items)found.Add(item.Code);
            }
        foreach(string code in codes)Check(found.Contains(code),"obtainable through seeded loot "+code);
        return checks;
    }
}
