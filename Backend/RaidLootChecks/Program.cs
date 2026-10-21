using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Empty(InventoryGraph graph)
{
    graph.Items.RemoveAll(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root);
    graph.Version++;
}
static string Composition(InventoryGraph graph) => string.Join(";", graph.Items
    .Where(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root)
    .Select(item => item.Code + ":" + item.Quantity));

var ids = new HashSet<string>();
int generated = 0;
foreach (RaidLootProfile profile in Enum.GetValues<RaidLootProfile>())
{
    var compositions = new HashSet<string>();
    for (int seed = 0; seed < 32; seed++)
    {
        var graph = RaidLootTable.Create(profile, new Random(seed));
        Check(graph.Validate() == InventoryError.None, "Invalid generated cache");
        Check(graph.Version == 1 && RaidLootRefillState.HasContents(graph), "Initial cache state");
        foreach (var item in graph.Items.Where(item => InventoryCatalog.Get(item.Code).Kind != ItemKind.Root))
            Check(item.FoundInRaid && graph.RootOf(item.Id) == LootInventoryExchange.Root && ids.Add(item.Id), "Unique loot ownership");
        Check(profile switch
        {
            RaidLootProfile.Supplies => graph.Count("ration") >= 1 && graph.Count("water") >= 1,
            RaidLootProfile.Medical => graph.Count("medkit") >= 2,
            RaidLootProfile.Industrial => graph.Count("alloy") >= 2 && graph.Count("dust") >= 3,
            RaidLootProfile.Equipment => graph.Count("cells") >= 2,
            _ => false
        }, "Profile guarantee");
        compositions.Add(Composition(graph));
        Check(Composition(graph) == Composition(RaidLootTable.Create(profile, new Random(seed))), "Repeatable server roll");
        generated++;
    }
    Check(compositions.Count > 1, "Profile needs varied contents");
}

var random = new Random(42);
var cache = RaidLootTable.Create(RaidLootProfile.Supplies, random);
var refill = new RaidLootRefillState(RaidLootProfile.Supplies);
Empty(cache);
int oldVersion = cache.Version;
Check(!refill.TryRefill(cache, 0, 300, 2, false, random), "Start empty cooldown");
Check(!refill.TryRefill(cache, 299, 300, 2, false, random), "Cooldown bypassed");
Check(!refill.TryRefill(cache, 300, 300, 2, true, random), "Viewed/nearby cache refilled");
Check(refill.TryRefill(cache, 301, 300, 2, false, random), "Unoccupied cache did not refill");
Check(cache.Version == oldVersion + 1 && refill.RefillCount == 1, "Refill needs a new version");
var firstGenerationIds = cache.Items.Where(item => item.Parent == LootInventoryExchange.Root).Select(item => item.Id).ToHashSet();
var owner = InventoryGraph.Create(false);
var itemToTake = cache.Items.First(item => item.Parent == LootInventoryExchange.Root);
Check(LootInventoryExchange.TryApply(owner, cache, new InventoryCommand { ExpectedVersion = owner.Version,
    ItemId = itemToTake.Id, Parent = "pockets", Region = "1" }, oldVersion) == InventoryError.Stale, "Old client cache version accepted");

Empty(cache);
Check(!refill.TryRefill(cache, 400, 300, 2, false, random), "Second empty cooldown");
var deposit = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "small_pack", Parent = "loot", Region = "main" };
cache.Items.Add(deposit);
Check(cache.AddSupply("cells", 2, deposit.Id) == InventoryError.None, "Nested deposited contents fixture");
deposit = cache.Find(deposit.Id);
var depositedCells = cache.Items.Single(item => item.Parent == deposit.Id);
int depositedVersion = cache.Version;
Check(!refill.TryRefill(cache, 1000, 300, 2, false, random) && cache.Version == depositedVersion &&
    cache.Find(deposit.Id) == deposit && cache.Find(depositedCells.Id) == depositedCells, "Deposited contents were replaced");
Empty(cache);
Check(!refill.TryRefill(cache, 1001, 300, 2, false, random), "Depositing must reset the empty cooldown");
Check(!refill.TryRefill(cache, 1300, 300, 2, false, random), "Second cooldown bypassed");
Check(refill.TryRefill(cache, 1301, 300, 2, false, random) && refill.RefillCount == 2, "Second refill failed");
Check(!cache.Items.Any(item => firstGenerationIds.Contains(item.Id)), "Refill reused an item ID");
Empty(cache);
Check(!refill.TryRefill(cache, 1400, 300, 2, false, random) && !refill.TryRefill(cache, 2000, 300, 2, false, random), "Refill cap bypassed");
var disabled = new RaidLootRefillState(RaidLootProfile.Medical);
Check(!disabled.TryRefill(cache, 0, 1, 0, false, random) && !disabled.TryRefill(cache, 100, 1, 0, false, random), "Zero must disable refills");
Console.WriteLine($"Raid loot checks passed: {generated} valid themed caches, unique IDs, profile guarantees, varied weighted rolls, cooldown, occupancy block, deposit preservation, stale version rejection and refill cap.");


// One item or complete nested container tree transfers to ground without changing IDs/provenance.
var dropOwner=InventoryGraph.Create(false);
var equippedPack=dropOwner.Equipped("Backpack");
var inner=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="small_pack",Parent=equippedPack.Id,Region="main"};
dropOwner.Items.Add(inner);
Check(dropOwner.AddSupply("cells",2,inner.Id,true,emergencySupply:true,cellCharge:18)==InventoryError.None,"Drop nested fixture");
Check(dropOwner.Validate()==InventoryError.None,"Drop source fixture invalid");
var original=dropOwner.Clone();int dropVersion=dropOwner.Version;
Check(LootInventoryExchange.PrepareDrop(dropOwner,equippedPack.Id,dropVersion-1,out _,out _)==InventoryError.Stale,"Stale drop accepted");
Check(LootInventoryExchange.PrepareDrop(dropOwner,"pockets",dropVersion,out _,out _)==InventoryError.Inaccessible,"Root drop accepted");
Check(LootInventoryExchange.PrepareDrop(dropOwner,"other-player-item",dropVersion,out _,out _)==InventoryError.Missing,"Foreign item drop accepted");
Check(dropOwner.Version==dropVersion && dropOwner.Items.Count==original.Items.Count,"Rejected drop changed source");
Check(LootInventoryExchange.PrepareDrop(dropOwner,equippedPack.Id,dropVersion,out var remaining,out var ground)==InventoryError.None,"Container drop rejected");
Check(dropOwner.Version==dropVersion && dropOwner.Find(inner.Id)!=null,"Preparing drop committed early");
Check(remaining.Find(equippedPack.Id)==null && remaining.Find(inner.Id)==null && remaining.Version==dropVersion+1,"Drop left duplicate descendants");
var originalCells=original.Items.Single(i=>i.Parent==inner.Id);var groundCells=ground.Find(originalCells.Id);
Check(ground.Find(equippedPack.Id)?.Parent=="loot" && ground.Find(inner.Id)?.Parent==equippedPack.Id &&
    groundCells.Quantity==originalCells.Quantity && groundCells.FoundInRaid && groundCells.EmergencySupply && groundCells.CellCharge==18,"Drop changed nested identity or charge/provenance");
var pickup=new InventoryCommand {ExpectedVersion=remaining.Version,ItemId=equippedPack.Id,Parent="equipment",Region="Backpack"};
Check(LootInventoryExchange.TryApply(remaining,ground,pickup,ground.Version)==InventoryError.None && remaining.Find(inner.Id)!=null,"Ground tree cannot be picked up again");
Check(LootInventoryExchange.TryApply(owner,ground,new InventoryCommand {ExpectedVersion=owner.Version,ItemId=equippedPack.Id,Parent="equipment",Region="Backpack"},1)==InventoryError.Stale,"Competing pickup duplicated ground item");
var direct=original.QuickTransferContainers().ToArray();
Check(direct.Length==3 && direct.Contains(equippedPack.Id) && !direct.Contains(inner.Id) && original.CarryContainers().Contains(inner.Id),"Shortcut must exclude nested bags while manual carry containers remain available");
var loose=InventoryGraph.Create(false,false);loose.AddSupply("cells",2,foundInRaid:true,emergencySupply:true,cellCharge:17);
var looseItem=loose.Items.Single(i=>i.Code=="cells");
Check(LootInventoryExchange.PrepareDrop(loose,looseItem.Id,loose.Version,out var looseRest,out var looseGround)==InventoryError.None &&
    looseRest.Count("cells")==0 && looseGround.Find(looseItem.Id).Quantity==2 && looseGround.Find(looseItem.Id).CellCharge==17,"Loose stack drop lost quantity/energy");
Console.WriteLine("Drop/shortcut checks passed: atomic preparation, stale/foreign/root rejection, nested IDs/quantity/provenance/energy, whole-tree pickup, competing claimant and direct-container-only shortcut.");

var malformedDrop=original.Clone();malformedDrop.Items.Add(null);
Check(LootInventoryExchange.PrepareDrop(malformedDrop,equippedPack.Id,malformedDrop.Version,out _,out _)==InventoryError.Invalid && malformedDrop.Items.Last()==null,"Malformed drop must fail before ownership lookup without mutating source");
