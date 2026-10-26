using System;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

public static class RaidSearchActionChecks
{
    private static int s_Checks;
    private static void Check(bool condition, string message)
    {
        s_Checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    public static string Run()
    {
        s_Checks = 0;
        var caches = new RaidLootContainers(); caches.Initialize(2);
        var origin = new float3(3, 0, 5);
        var health = new PredictedPlayerGhost { CurrentHealth = 100, LastHitTick = 1, ShieldHitSequence = 2, LastShotTick = 3,
            LastGrenadeShotTick = 4, LastReloadTick = 5, EquippedWeaponID = 6 };
        var actor = new RaidInventoryState();
        var busy = health; busy.ControllerState.IsReloadingState = true;
        actor.BeginDissection(0, 0, origin, busy, 2);
        Check(actor.DissectingLootId == -1 && actor.OpenedLootId == -1, "Already reloading cannot start search");
        busy.ControllerState.IsReloadingState = false; busy.WeaponEquipRemaining = .2f;
        actor.BeginDissection(0, 0, origin, busy, 2);
        Check(actor.DissectingLootId == -1 && actor.OpenedLootId == -1, "Weapon switch cannot start search");
        actor.BeginDissection(0, 10, origin, health, 2);
        actor.RefreshDissection(caches, true, origin, health, 11.99);
        Check(actor.DissectingLootId == 0 && actor.OpenedLootId == -1 && !actor.SearchedLoot.Contains(0), "Search cannot reveal contents early");
        actor.RefreshDissection(caches, true, origin, health, 12);
        Check(actor.OpenedLootId == 0 && actor.SearchedLoot.Contains(0) && !actor.NeedsSearch(caches, 0), "Search opens and remembers the completed container");

        var remembered = new RaidInventoryState();
        remembered.BeginDissection(1, 0, origin, health, 2);
        remembered.RefreshDissection(caches, true, origin, health, 2);
        remembered.OpenedLootId = -1;
        var another = new RaidInventoryState();
        another.BeginDissection(1, 0, origin, health, 2);
        another.RefreshDissection(caches, true, origin, health, 2);
        another.OpenedLootId = -1;
        var refilled = caches.Containers[1];
        refilled.Items.RemoveAll(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root); refilled.Version++;
        var refill = caches.RefillStates[1]; var random = new System.Random(27);
        Check(!refill.TryRefill(refilled, 10, 1, 2, false, random) && refill.TryRefill(refilled, 11, 1, 2, false, random), "Refill fixture generates a new batch");
        Check(remembered.NeedsSearch(caches, 1) && another.NeedsSearch(caches, 1), "Every observer must search a new supply batch again");
        remembered.BeginDissection(1, 20, origin, health, 2, caches.RefillGeneration(1));
        remembered.RefreshDissection(caches, true, origin, health, 21.99);
        Check(remembered.OpenedLootId == -1 && remembered.NeedsSearch(caches, 1), "Refilled contents stay hidden until the new search finishes");
        remembered.RefreshDissection(caches, true, origin, health, 22);
        Check(remembered.OpenedLootId == 1 && !remembered.NeedsSearch(caches, 1), "New batch search becomes remembered");
        refilled.Version++;
        Check(!remembered.NeedsSearch(caches, 1), "Ordinary item transfers do not restart a completed search");
        var inFlight = new RaidInventoryState();
        inFlight.BeginDissection(1, 30, origin, health, 2, caches.RefillGeneration(1));
        refilled.Items.RemoveAll(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root); refilled.Version++;
        Check(!refill.TryRefill(refilled, 31, 1, 2, false, random) && refill.TryRefill(refilled, 32, 1, 2, false, random), "Second batch fixture");
        inFlight.RefreshDissection(caches, true, origin, health, 32);
        Check(inFlight.OpenedLootId == -1 && !inFlight.SearchedLoot.Contains(1), "A batch change during search cannot reveal new contents");
        inFlight.BeginDissection(1, 40, origin, health, 2, caches.RefillGeneration(1));
        inFlight.RefreshDissection(caches, true, origin, health, 42);
        Check(inFlight.OpenedLootId == 1 && !inFlight.NeedsSearch(caches, 1), "New batch can be searched normally");
        inFlight.ClearSearchRecords();
        Check(inFlight.NeedsSearch(caches, 1), "New raid clears both search and batch memory");

        for (int interruption = 0; interruption < 10; interruption++)
        {
            actor = new RaidInventoryState();
            var changed = health;
            actor.BeginDissection(0, 20, origin, changed, 2);
            var position = origin; bool accessible = true;
            switch (interruption)
            {
                case 0: RaidHealth.Damage(ref changed,BodyPart.RightArm,1,changed.LastHitTick+1); break;
                case 1: changed.ShieldHitSequence++; break;
                case 2: changed.LastShotTick++; break;
                case 3: changed.LastGrenadeShotTick++; break;
                case 4: changed.LastReloadTick++; break;
                case 5: changed.EquippedWeaponID++; break;
                case 6: position.x += .76f; break;
                case 7: accessible = false; break;
                case 8: changed.ControllerState.IsReloadingState = true; break;
                case 9: changed.WeaponEquipRemaining = .2f; break;
            }
            actor.RefreshDissection(caches, accessible, position, changed, 22);
            Check(actor.DissectingLootId == -1 && actor.OpenedLootId == -1 && !actor.SearchedLoot.Contains(0) &&
                actor.Error == InventoryError.Inaccessible, "Interruption must not reveal or remember loot: " + interruption);
            Check(!string.IsNullOrEmpty(actor.ActionFeedback), "Interruption needs feedback: " + interruption);
            changed.ControllerState.IsReloadingState = false; changed.WeaponEquipRemaining = 0;
            actor.BeginDissection(0, 30, origin, changed, 2);
            actor.RefreshDissection(caches, true, origin, changed, 32);
            Check(actor.OpenedLootId == 0 && actor.SearchedLoot.Contains(0), "Search can be restarted after interruption: " + interruption);
        }

        var shielded = new PredictedPlayerGhost(); RaidHealth.Initialize(ref shielded);
        shielded.ShieldEquipped = true; shielded.ShieldCharge = 100; shielded.LastHitTick = 42;
        actor = new RaidInventoryState(); actor.BeginDissection(0, 0, origin, shielded, 2);
        Check(EnergyShieldRules.Absorb(ref shielded, 25, 42, RaidDamageKind.Bullet, new float3(1, 1, 1)) == 0 && shielded.LastHitTick == 42,
            "Shield absorbs a second impact in the same tick");
        actor.RefreshDissection(caches, true, origin, shielded, 2);
        Check(actor.OpenedLootId == -1 && !actor.SearchedLoot.Contains(0), "Same-tick shield impact interrupts search");

        var remains = AlienHarvest.Create(false, new System.Random(7));
        int alienId = caches.DropHarvest(remains, origin);
        actor = new RaidInventoryState();
        actor.BeginDissection(alienId, 0, origin, health);
        var hit = health; hit.ShieldHitSequence++;
        actor.RefreshDissection(caches, true, origin, hit, 6);
        Check(caches.RequiresDissection(alienId) && actor.OpenedLootId == -1, "Shield hit cannot reveal alien remains");
        actor.BeginDissection(alienId, 10, origin, hit);
        actor.RefreshDissection(caches, true, origin, hit, 16);
        Check(!caches.RequiresDissection(alienId) && actor.OpenedLootId == alienId && caches.Containers[alienId].Version == 2,
            "Restarted dissection reveals shared remains once");

        var rescueCaches=new RaidLootContainers();rescueCaches.Initialize(1);
        var rescueCache=rescueCaches.Containers[0];
        rescueCache.Items.RemoveAll(item=>InventoryCatalog.Get(item.Code).Kind!=ItemKind.Root);rescueCache.Version++;
        Check(rescueCache.AddSupply("ration",1,LootInventoryExchange.Root,true)==InventoryError.None,"rescue cache fixture");
        var starving=new PredictedPlayerGhost();RaidHealth.Initialize(ref starving);starving.Energy=0;
        actor=new RaidInventoryState {Graph=InventoryGraph.Create(false)};float hp=starving.CurrentHealth;
        actor.BeginDissection(0,0,origin,starving,2);
        for(uint tick=1;tick<=4;tick++)
        {
            RaidNutrition.Tick(ref starving,.5f,1000,1000,300,0,2,0,tick);
            actor.RefreshDissection(rescueCaches,true,origin,starving,tick*.5);
        }
        Check(actor.OpenedLootId==0 && starving.CurrentHealth<hp,"starvation damage allows a search while health continues falling");
        var food=rescueCache.Items.Find(item=>item.Code=="ration");
        string region=null;int x=0,y=0;
        Check(food!=null && actor.Graph.FindSpace(food,InventoryCatalog.Pockets,out region,out x,out y),"found food can fit");
        Check(LootInventoryExchange.TryApply(actor.Graph,rescueCache,new InventoryCommand {ExpectedVersion=actor.Graph.Version,
            Operation=InventoryOperation.Move,ItemId=food.Id,Parent=InventoryCatalog.Pockets,Region=region,X=x,Y=y},rescueCache.Version)==InventoryError.None &&
            actor.Graph.Find(food.Id)!=null && rescueCache.Find(food.Id)==null,"searched food transfers into the real inventory");
        Check(actor.BeginConsumable(starving,food.Id,actor.Graph.Version,origin,2)==InventoryError.None,"found food can be eaten");
        for(uint tick=1;tick<=6;tick++)
        {
            RaidNutrition.Tick(ref starving,.5f,1000,1000,300,0,2,0,10+tick);
            actor.RefreshMedical(ref starving,true,origin,2+tick*.5);
        }
        Check(actor.Error==InventoryError.None && starving.Energy>0 && actor.Graph.Find(food.Id)==null,
            "search, transfer and food use rescue the starving player exactly once");
        actor=new RaidInventoryState();starving.Energy=0;
        actor.BeginDissection(0,10,origin,starving,2);
        RaidHealth.CombatDamage(ref starving,BodyPart.RightArm,5,50,RaidDamageKind.Bullet);
        RaidNutrition.Tick(ref starving,.5f,1000,1000,300,0,2,0,50);
        actor.RefreshDissection(rescueCaches,true,origin,starving,10.5);
        Check(actor.OpenedLootId==-1 && actor.Error==InventoryError.Inaccessible,"same-tick starvation damage cannot conceal a real attack during search");
        return "PASS " + s_Checks + " search and refill checks";
    }
}
