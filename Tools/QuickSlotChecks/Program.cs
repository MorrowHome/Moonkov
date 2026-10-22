using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.Inventory;
int count=0;
void Check(bool ok,string title){if(!ok)throw new Exception(title);count++;}
var store=new MemoryStore();var bindings=new QuickSlotBindings(store);
var graph=InventoryGraph.Create(false);var rig=graph.Equipped("ChestRig")!;
var primary=graph.Equipped("Primary")!;var secondary=graph.Equipped("Secondary")!;var pistol=graph.Equipped("Pistol")!;
bindings.UseProfile("account-a");
foreach(var pair in new[]{(primary,1),(secondary,2),(pistol,3)})
{
    Check(bindings.Bind(pair.Item2-1,pair.Item1,graph),"actual weapon binds");
    int version=graph.Version;
    Check(QuickSlotBindings.WeaponSlot(graph,bindings.Select(pair.Item2-1,graph))==pair.Item2,"weapon maps to its equipped slot");
    Check(graph.Version==version,"select weapon never mutates inventory");
}
int slot=3;
foreach(var code in new[]{"medkit","med_gel","trauma_kit","ration","water","oxygen_canister","energy_grenade"}.Concat(TacticalItemRules.Definitions.Select(d=>d.Code)))
{
    var g=InventoryGraph.Create(false);var r=g.Equipped("ChestRig")!;
    Check(g.AddSupply(code,1,r.Id)==InventoryError.None,"lawful obtain "+code);
    var item=g.Items.Find(i=>i.Code==code)!;int version=g.Version;
    Check(bindings.Bind(slot,item,g),"usable binds "+code);
    Check(bindings.Select(slot,g)==item && item.Quantity==1 && g.Version==version,"selection does not consume "+code);
    item.Parent=g.Equipped("Backpack")!.Id;bindings.Validate(g);
    bool bagAllowed=ConsumableCatalog.Get(code) is {Oxygen:<=0};
    Check(bindings.Available(slot,g)==bagAllowed,"bag follows existing access rule "+code);
    item.Parent="unreachable-container";bindings.Validate(g);
    Check(bindings.Selected==-1 && !bindings.Available(slot,g),"lost access disables "+code);
    item.Parent=r.Id;Check(bindings.Select(slot,g)==item,"same identity becomes accessible "+code);
    g.Items.Remove(item);g.AddSupply(code,1,r.Id);
    Check(bindings.Select(slot,g)==null && bindings.Selected==-1,"same code new identity never substitutes "+code);
    Check(bindings.Code(slot)==code,"unavailable silhouette retained "+code);
    slot=slot==8?3:slot+1;
}
Check(!bindings.Bind(0,new InventoryItem{Id=primary.Id,Code=primary.Code,Parent=primary.Parent,Region=primary.Region},graph),"forged candidate object rejected");
Check(bindings.Bind(8,primary,graph) && bindings[0]==null && bindings.Selected==-1,"duplicate binding moved and selection reset");
bindings.Select(8,graph);bindings.Clear(8);Check(bindings.Selected==-1 && bindings[8]==null,"clear selected");
bindings.Bind(8,primary,graph);bindings.UseProfile("account-b");Check(bindings[8]==null && bindings.Selected==-1,"account isolation");
bindings.UseProfile("account-a");Check(bindings[8]==primary.Id && bindings.Selected==-1,"account reload exact identity");
bindings.UseProfile("");Check(!bindings.Bind(0,primary,graph) && bindings[8]==null,"unauthenticated scope never saves");
bindings.UseProfile("account-a");bindings.Select(8,graph);bindings.Validate(null);Check(bindings.Selected==-1,"lost snapshot clears active item");
primary.Region="Secondary";secondary.Region="Primary";
Check(QuickSlotBindings.WeaponSlot(graph,bindings.Select(8,graph))==2,"same item moved to new equipment slot resolves real slot");
primary.Quantity=0;Check(bindings.Select(8,graph)==null,"exhausted weapon unavailable");
store.Values["bad"]=new QuickSlotSave{Items=new string[1]};bindings.UseProfile("bad");Check(bindings[0]==null,"malformed saved array resets");
Check(bindings.Select(-1,graph)==null && bindings.Select(9,graph)==null,"invalid index rejected");
var defaults=new QuickSlotBindings(store);defaults.UseProfile("defaults");
var start=InventoryGraph.Create(false);defaults.InitializeWeapons(start);
Check(defaults[0]==start.Equipped("Primary")!.Id && defaults[1]==start.Equipped("Secondary")!.Id && defaults[2]==start.Equipped("Pistol")!.Id,"first profile seeds only actual equipped weapons");
defaults.Clear(0);defaults.InitializeWeapons(start);Check(defaults[0]==null,"intentional clear stays cleared");
string previous=defaults[1];defaults.InitializeWeapons(InventoryGraph.Create(false));Check(defaults[1]==previous,"new raid never reseeds same-code identities");
// Real shared shop/placement/drop rules, never debug-only creation as the acquisition path.
foreach (string code in new[] { MeleeInventory.DaggerCode, MeleeInventory.SwordCode })
{
    var profile = InventoryGraph.Create(true, false);
    Check(profile.AddSupply("dust", 80, "stash") == InventoryError.None, "earned dust fits stash");
    var offer = ShopCatalog.Find(code)!;
    var buy = new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), Operation = ShopOperation.Buy,
        Code = code, Quantity = 1, ExpectedVersion = profile.Version };
    Check(ShopRules.Apply(profile, buy, out var purchased) == null, "actual shop buys " + code);
    profile = purchased; var meleeItem = profile.Items.Single(i => i.Code == code);
    Check(profile.Count("dust") == 80 - offer.BuyDust && meleeItem.Quantity == 1, "purchase pays exact price once");
    var hotbar = new QuickSlotBindings(store); hotbar.UseProfile("melee-" + code);
    Check(!hotbar.Bind(8, meleeItem, profile), "stashed melee cannot bind");
    Check(profile.TryApply(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = profile.Version,
        ItemId = meleeItem.Id, Parent = "equipment", Region = MeleeInventory.Slot }) == InventoryError.None, "real melee equipment slot accepts " + code);
    meleeItem = profile.Equipped(MeleeInventory.Slot)!;
    Check(MeleeInventory.CanEquip(profile, meleeItem) && QuickSlotBindings.WeaponSlot(profile, meleeItem) == 4, "melee resolves independent equipment index");
    Check(hotbar.Bind(8, meleeItem, profile), "equipped melee binds arbitrary number nine");
    int selectedVersion = profile.Version;
    Check(hotbar.Select(8, profile) == meleeItem && profile.Version == selectedVersion && meleeItem.Quantity == 1,
        "selecting melee never consumes or mutates inventory");
    Check(ShopCatalog.CanClaim(profile), "owning melee does not block emergency firearm access");
    var seeded = new QuickSlotBindings(store); seeded.UseProfile("melee-default-" + code); seeded.InitializeWeapons(profile);
    Check(seeded[3] == meleeItem.Id && seeded[0] == null, "new profile seeds only real equipped melee without phantom fists");
    var jsonOptions = new System.Text.Json.JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true };
    string json = System.Text.Json.JsonSerializer.Serialize(profile, jsonOptions);
    var reloaded = System.Text.Json.JsonSerializer.Deserialize<InventoryGraph>(json, jsonOptions)!;
    Check(reloaded.Validate() == InventoryError.None && reloaded.Equipped(MeleeInventory.Slot)!.Id == meleeItem.Id,
        "JSON inventory reload preserves exact melee identity and slot");
    var loadedBindings = new QuickSlotBindings(store); loadedBindings.UseProfile("melee-" + code);
    Check(loadedBindings.Select(8, reloaded)?.Id == meleeItem.Id, "saved real binding resolves after graph reload");
    var raid = reloaded.Clone(); var stashIds = reloaded.Items.Where(i => i.Id == "stash" || reloaded.RootOf(i.Id) == "stash").Select(i => i.Id).ToHashSet();
    raid.Items.RemoveAll(i => stashIds.Contains(i.Id));
    Check(raid.Validate() == InventoryError.None, "carried melee remains valid after stash is excluded");
    Check(LootInventoryExchange.PrepareDrop(raid, meleeItem.Id, raid.Version, out var remaining, out var dropped) == InventoryError.None,
        "actual drop transaction accepts equipped melee");
    Check(remaining.Find(meleeItem.Id) == null && dropped.Find(meleeItem.Id)?.Code == code, "drop transfers exactly the original item");
    loadedBindings.Validate(remaining);
    Check(loadedBindings.Selected == -1 && !loadedBindings.Available(8, remaining) && loadedBindings.Code(8) == code,
        "dropping melee clears selection and keeps unavailable silhouette");
    Check(LootInventoryExchange.TryApply(remaining, dropped, new InventoryCommand { Operation = InventoryOperation.Move,
        ExpectedVersion = remaining.Version, ItemId = meleeItem.Id, Parent = "equipment", Region = MeleeInventory.Slot }, dropped.Version) == InventoryError.None,
        "actual loot transfer can retrieve the same melee item");
    Check(loadedBindings.Select(8, remaining)?.Id == meleeItem.Id, "same item becomes available again after retrieval");
    var corpse = LootInventoryExchange.DropOnDeath(remaining);
    Check(corpse.Validate() == InventoryError.None && corpse.Find(meleeItem.Id)?.Code == code, "death loot preserves real melee identity");
    Check(profile.FindSpace(profile.Find(meleeItem.Id), "stash", out var region, out var x, out var y), "melee has real stash placement");
    Check(profile.TryApply(new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = profile.Version,
        ItemId = meleeItem.Id, Parent = "stash", Region = region, X = x, Y = y }) == InventoryError.None, "melee can unequip before sale");
    Check(ShopRules.Apply(profile, new ShopCommand { RequestId = Guid.NewGuid().ToString("D"), Operation = ShopOperation.Sell,
        Code = code, ItemId = meleeItem.Id, Quantity = 1, ExpectedVersion = profile.Version }, out var sold) == null,
        "real shop can sell melee");
    Check(sold.Find(meleeItem.Id) == null && sold.Count("dust") == 80 - offer.BuyDust + offer.SellDust, "sale removes exact item and returns listed value");
}
var emptyHands = InventoryGraph.Create(false, false); int emptyCount = emptyHands.Items.Count;
Check(MeleeInventory.ActiveKind(emptyHands, 0, false) == MeleeWeaponKind.Fists, "no active weapon falls back to fists");
Check(MeleeInventory.ActiveKind(emptyHands, 1, true) == MeleeWeaponKind.None, "firearm remains outside melee mode");
Check(emptyHands.Items.Count == emptyCount && InventoryCatalog.Get("fists") == null, "fists never create a catalog or inventory item");
Console.WriteLine($"Quick slots: {count} checks passed; in-memory store only, no real preferences touched.");
sealed class MemoryStore : IQuickSlotStore
{
    public readonly Dictionary<string,QuickSlotSave> Values=new();
    public QuickSlotSave Load(string scope)=>Values.TryGetValue(scope,out var v)?new QuickSlotSave{Initialized=v.Initialized,Items=(string[])v.Items.Clone(),Codes=(string[])v.Codes.Clone()}:null!;
    public void Save(string scope,QuickSlotSave v)=>Values[scope]=new QuickSlotSave{Initialized=v.Initialized,Items=(string[])v.Items.Clone(),Codes=(string[])v.Codes.Clone()};
}
