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
Console.WriteLine($"Quick slots: {count} checks passed; in-memory store only, no real preferences touched.");
sealed class MemoryStore : IQuickSlotStore
{
    public readonly Dictionary<string,QuickSlotSave> Values=new();
    public QuickSlotSave Load(string scope)=>Values.TryGetValue(scope,out var v)?new QuickSlotSave{Initialized=v.Initialized,Items=(string[])v.Items.Clone(),Codes=(string[])v.Codes.Clone()}:null!;
    public void Save(string scope,QuickSlotSave v)=>Values[scope]=new QuickSlotSave{Initialized=v.Initialized,Items=(string[])v.Items.Clone(),Codes=(string[])v.Codes.Clone()};
}
