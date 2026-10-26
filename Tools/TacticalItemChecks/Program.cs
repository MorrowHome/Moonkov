using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
foreach(var d in TacticalItemRules.Definitions)
{
    var graph=InventoryGraph.Create(false);
    // Some long tools require a real rig; do not manufacture illegal pocket placement.
    graph.AddLoot("rig");var rig=graph.Items.Find(i=>i.Code=="rig")!;
    rig.Parent="equipment";rig.Region="ChestRig";rig.X=rig.Y=0;
    Check(graph.AddSupply(d.Code,1,rig.Id)==InventoryError.None,"obtain "+d.Code);
    var item=graph.Items.Find(i=>i.Code==d.Code)!;
    int version=graph.Version;
    Check(TacticalItemRules.Validate(graph,item.Id,version,out var found)==InventoryError.None && found==d,"select "+d.Code);
    Check(TacticalItemRules.Take(graph,item.Id,version-1)==InventoryError.Stale,"stale "+d.Code);
    Check(TacticalItemRules.Take(graph,item.Id,version)==InventoryError.None && graph.Find(item.Id)==null,"consume exactly one "+d.Code);
    Check(TacticalItemRules.Take(graph,item.Id,version)!=InventoryError.None,"replay "+d.Code);
    Check(ShopCatalog.Find(d.Code)?.BuyDust>0,"buy "+d.Code);
}
var stash=InventoryGraph.Create(true);stash.AddSupply("antidote_shot",1,"stash");
Check(TacticalItemRules.Take(stash,stash.Items.Find(i=>i.Code=="antidote_shot")!.Id,stash.Version)==InventoryError.Inaccessible,"stash inaccessible");
var source=InventoryGraph.Create(false);source.AddSupply("cells",1,cellCharge:30);
int sourceVersion=source.Version;
Check(TacticalItemRules.PrepareResupply(source,sourceVersion,400,out var recharged,out int supplied)==InventoryError.None,"partial refill succeeds");
Check(supplied==70 && recharged.CellEnergy(true)==100 && source.CellEnergy(true)==30 && source.Version==sourceVersion,"partial energy conserved and atomic");
Check(recharged.Version==sourceVersion+1 && recharged.Validate()==InventoryError.None,"refill legal version");
Check(TacticalItemRules.PrepareResupply(recharged,recharged.Version,330,out var second,out supplied)==InventoryError.None && supplied==100 && second.CellEnergy(true)==200,"add actual cells");
Check(TacticalItemRules.PrepareResupply(source,sourceVersion-1,100,out _,out supplied)==InventoryError.Stale && supplied==0,"resupply stale untouched");
Check(TacticalItemRules.PrepareResupply(source,sourceVersion,12,out var partial,out supplied)==InventoryError.None && supplied==12 && partial.CellEnergy(true)==42,"partial final box charge conserved");
var full=InventoryGraph.Create(false,false);full.AddSupply("cells",48);
int fullEnergy=full.CellEnergy(true), fullVersion=full.Version;
Check(TacticalItemRules.PrepareResupply(full,full.Version,100,out _,out supplied)==InventoryError.Full && supplied==0 && full.CellEnergy(true)==fullEnergy && full.Version==fullVersion,"no room leaves box and inventory untouched");
Check(TacticalItemRules.BlastDamage(TacticalItemKind.Dynamite,0)==650,"dynamite inner damage");
Check(TacticalItemRules.BlastDamage(TacticalItemKind.Frag,0)==320,"frag inner damage");
Check(TacticalItemRules.BlastDamage(TacticalItemKind.Frag,8)==0 && TacticalItemRules.BlastDamage(TacticalItemKind.Frag,float.NaN)==0,"blast boundary rejects nonfinite");
var seen=new HashSet<string>();var random=new Random(7821);
for(int i=0;i<512;i++)
{
    var medical=RaidLootTable.Create(RaidLootProfile.Medical,random);
    var equipment=RaidLootTable.Create(RaidLootProfile.Equipment,random);
    Check(medical.Validate()==InventoryError.None && medical.Items.Any(x=>x.Code=="medkit" && x.Quantity>=2),"medical guaranteed supply preserved");
    Check(equipment.Validate()==InventoryError.None && equipment.Items.Any(x=>x.Code=="cells" && x.Quantity>=2),"equipment guaranteed battery preserved");
    foreach(var item in medical.Items.Concat(equipment.Items))if(TacticalItemRules.Find(item.Code)!=null)seen.Add(item.Code);
}
foreach(var def in TacticalItemRules.Definitions)Check(seen.Contains(def.Code),"natural cache obtains "+def.Code);
var stamina=new TacticalSprintState();TacticalSprintRules.Tick(ref stamina,.1f,false,false);Check(stamina.Value==100 && !stamina.Exhausted,"initialized full");
TacticalSprintRules.Tick(ref stamina,50,true,false);Check(stamina.Value==0 && stamina.Exhausted,"ordinary sprint exhausts after 50s");
TacticalSprintRules.Tick(ref stamina,1,false,false);Check(stamina.Value==0,"no early recovery");TacticalSprintRules.Tick(ref stamina,.5f,false,false);Check(stamina.Value==0,"rest delay exact boundary");
TacticalSprintRules.Tick(ref stamina,1.2f,false,false);Check(stamina.Value==24 && stamina.Exhausted,"hysteresis holds exhaustion");TacticalSprintRules.Tick(ref stamina,.05f,false,false);Check(stamina.Value>=25 && !stamina.Exhausted,"rest unlocks at 25");
TacticalSprintRules.Tick(ref stamina,100,false,false);Check(stamina.Value==100,"recovery caps");
TacticalSprintRules.Tick(ref stamina,420,true,true);Check(stamina.Value==100 && !stamina.Exhausted,"shot prevents fatigue throughout active duration");
TacticalSprintRules.Tick(ref stamina,50,true,false);Check(stamina.Value==0 && stamina.Exhausted,"expired shot permits fatigue again");
TacticalSprintRules.Tick(ref stamina,.1f,true,false,false);Check(stamina.Value==100 && !stamina.Exhausted,"downed/dead resets transient fatigue");
var coarse=new TacticalSprintState();var fine=new TacticalSprintState();TacticalSprintRules.Tick(ref coarse,50,true,false);for(int i=0;i<500;i++)TacticalSprintRules.Tick(ref fine,.1f,true,false);Check(Math.Abs(coarse.Value-fine.Value)<.001f && coarse.Exhausted==fine.Exhausted,"frame independent drain");
TacticalSprintRules.Tick(ref coarse,3,false,false);for(int i=0;i<30;i++)TacticalSprintRules.Tick(ref fine,.1f,false,false);Check(Math.Abs(coarse.Value-fine.Value)<.001f,"frame independent rest delay and recovery");
float before=fine.Value;TacticalSprintRules.Tick(ref fine,float.NaN,true,false);Check(fine.Value==before,"nonfinite delta rejected");
var light=new TacticalSprintState();var heavy=new TacticalSprintState();
TacticalSprintRules.Tick(ref light,20,true,false,true,0);TacticalSprintRules.Tick(ref heavy,20,true,false,true,InventoryCatalog.CarryWeightLimit);
Check(heavy.Value<light.Value,"heavy equipment drains sprint faster");
float lightBefore=light.Value,heavyBefore=heavy.Value;
TacticalSprintRules.Tick(ref light,2,false,false,true,0);TacticalSprintRules.Tick(ref heavy,2,false,false,true,InventoryCatalog.CarryWeightLimit);
Check(heavy.Value-heavyBefore<light.Value-lightBefore,"heavy equipment slows sprint recovery");

Console.WriteLine($"Tactical rules: {checks} checks passed.");
