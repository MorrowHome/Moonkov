using Unity.MP_FPS.Inventory;
using System.Text.Json;

int checks=0;
void Check(bool ok,string name) {if(!ok)throw new Exception(name);checks++;}
InventoryGraph Full(int free=0)
{
    var graph=InventoryGraph.Create(true,false);graph.WeaponKitVersion=1;graph.StashRows=4096;
    for(int i=0;i<InventoryCatalog.MaxItems-3-free;i++)graph.Items.Add(new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="dust",Quantity=1,Parent="stash",Region="main",X=i%10,Y=i/10});
    Check(graph.Validate()==InventoryError.None,"full fixture");return graph;
}
var source=InventoryGraph.Create(false);source.WeaponKitVersion=1;
var pack=source.Equipped("Backpack");source.AddSupply("cells",2,pack.Id,true,emergencySupply:true,cellCharge:13);
var artifact=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="mooncore_fragment",Parent="pockets",Region="1",FoundInRaid=true,
    MissionWorldId=Guid.NewGuid().ToString("D"),MissionOperationId=Guid.NewGuid().ToString("D"),MissionContentVersion=1};source.Items.Add(artifact);
Check(source.Validate()==InventoryError.None,"source fixture");
var account=Full();var inbox=new RecoveryInbox();string before=InventoryRepository.Encode(account),cargo=InventoryRepository.Encode(source);
Check(RecoveryInventory.Settle(account,inbox,source,out var stored,out var pending)==InventoryError.None,"full settlement rejected");
Check(stored.Items.Count==InventoryCatalog.MaxItems && pending.Batches.Count==2,"cargo/reward must both overflow");
Check(stored.MissionFirsts==3 && pending.Batches.SelectMany(b=>b.Items).Single(i=>i.Id==artifact.Id).MissionRecovered,"pending mission facts not committed");
Check(pending.Batches.Sum(b=>b.Count("dust"))==40,"first reward not preserved in second batch");
Check(InventoryRepository.Encode(account)==before && InventoryRepository.Encode(source)==cargo,"settlement mutated inputs");
var cells=source.Items.Single(i=>i.Code=="cells");var held=pending.Batches.SelectMany(b=>b.Items).Single(i=>i.Id==cells.Id);
Check(held.Parent==pack.Id && held.CellCharge==13 && held.EmergencySupply && held.Quantity==2,"pending changed subtree/provenance/energy");
Check(pending.ItemCount()<=InventoryCatalog.MaxItems*2 && pending.Validate(stored)==InventoryError.None,"bounded valid pending");
var request=new RecoveryClaim {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=stored.Version,ExpectedRecoveryVersion=pending.Version};
request.ExpectedRecoveryVersion--;
Check(RecoveryInventory.Claim(stored,pending,request,out _,out _)==InventoryError.Stale,"old inbox version accepted");request.ExpectedRecoveryVersion++;
request.ExpectedVersion--;Check(RecoveryInventory.Claim(stored,pending,request,out _,out _)==InventoryError.Stale,"old account version accepted");request.ExpectedVersion++;
Check(RecoveryInventory.Claim(stored,pending,request,out _,out _)==InventoryError.Full,"full claim should preserve inbox");
var roots=pending.Roots().ToArray();var toFree=roots.Sum(i=>InventoryCatalog.Get(i.Code).Container ? pending.Batches.Single(b=>b.Find(i.Id)!=null).Items.Count(x=>x.Parent==i.Id)+1 : 1);
stored.Items.RemoveRange(3,60);stored.Version++;
request.ExpectedVersion=stored.Version;
Check(RecoveryInventory.Claim(stored,pending,request,out var claimed,out var remaining)==InventoryError.None && remaining.ItemCount()==0,"claim after sorting failed");
Check(claimed.Find(artifact.Id)?.MissionRecovered==true && claimed.MissionFirsts==3 && claimed.Count("dust")==stored.Count("dust")+40,"claim repeated reward or lost facts");
Check(claimed.Find(pack.Id)?.Parent=="stash" && claimed.Find(cells.Id)?.Parent==pack.Id,"claim detached tree");
Check(RecoveryInventory.Claim(claimed,remaining,request,out _,out _)==InventoryError.Stale,"old claim versions accepted");
request.ExpectedVersion=claimed.Version;request.ExpectedRecoveryVersion=remaining.Version;
Check(RecoveryInventory.Claim(claimed,remaining,request,out _,out _)==InventoryError.Missing,"empty replay granted items");
var researchFixture=claimed.Clone();researchFixture.AddSupply("circuit_board",2,"stash");
Check(ShopRules.Apply(researchFixture,new ShopCommand {RequestId=Guid.NewGuid().ToString("D"),ExpectedVersion=researchFixture.Version,
    Operation=ShopOperation.Research,Code="mooncore_stabilization",Quantity=1},out var researched)==null && (researched.HaloResearchBranches & 8)!=0 && researched.Find(artifact.Id)==null,"claimed artifact cannot be spent for permanent research");
var collision=Full(1);var collisionCargo=source.Clone();collision.Items.Add(collisionCargo.Find(artifact.Id).Clone());
Check(RecoveryInventory.Settle(collision,new RecoveryInbox(),source,out _,out _)==InventoryError.Invalid,"duplicate source ID accepted");
var serialized=JsonSerializer.Serialize(pending,new JsonSerializerOptions {IncludeFields=true});
var reloaded=JsonSerializer.Deserialize<RecoveryInbox>(serialized,new JsonSerializerOptions {IncludeFields=true});
Check(reloaded.Validate(stored)==InventoryError.None && reloaded.ItemCount()==pending.ItemCount(),"pending restart lost data");
var partial=Full(2);
Check(RecoveryInventory.Settle(partial,new RecoveryInbox(),source,out var partialStored,out var partialPending)==InventoryError.None && partialStored.Items.Count>partial.Items.Count && partialPending.ItemCount()>0,"normal fit portion not stored");
Console.WriteLine($"Recovery pure checks passed: {checks} assertions; full/partial account, two bounded batches, first facts/rewards while pending, claims, ID/tree/energy preservation and stale versions.");
if(args.Length>0)await DatabaseChecks.Run(Path.GetFullPath(args.Single()));
