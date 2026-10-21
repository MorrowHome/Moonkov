#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    // Separate ownership from the capacity-limited account graph. A pending batch
    // blocks the next deployment, so one raid plus its first rewards is the bound.
    [Serializable]
    public sealed class RecoveryInbox
    {
        public int Version = 1;
        public List<InventoryGraph> Batches = new List<InventoryGraph>();
        public int ItemCount() => Batches.Sum(b => b.Items.Count(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root));
        public IEnumerable<InventoryItem> Roots() => Batches.SelectMany(b => b.Items.Where(i => i.Parent == "equipment" || i.Parent == "pockets"));
        public RecoveryInbox Clone() => new RecoveryInbox {Version=Version,Batches=Batches.Select(b=>b.Clone()).ToList()};
        public InventoryError Validate(InventoryGraph account)
        {
            if(Version<1 || Batches==null || Batches.Count>2 || account==null || account.Validate()!=InventoryError.None || account.Find("stash")==null || account.Find("loot")!=null)return InventoryError.Invalid;
            var ids=new HashSet<string>(account.Items.Where(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root).Select(i=>i.Id));
            foreach(var batch in Batches)
            {
                if(batch==null || batch.Validate()!=InventoryError.None || batch.Find("stash")!=null || batch.Find("loot")!=null)return InventoryError.Invalid;
                foreach(var item in batch.Items.Where(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root))
                    if(!ids.Add(item.Id))return InventoryError.Invalid;
            }
            return InventoryError.None;
        }
    }
    public sealed class RecoveryClaim
    {
        public string RequestId, ItemId;
        public int ExpectedVersion, ExpectedRecoveryVersion;
    }
    public static class RecoveryInventory
    {
        private static HashSet<string> Tree(InventoryGraph source,string id)
        {
            var ids=new HashSet<string>{id};
            for(int depth=0;depth<InventoryCatalog.MaxDepth;depth++)
                foreach(var item in source.Items)if(item.Parent!=null && ids.Contains(item.Parent))ids.Add(item.Id);
            return ids;
        }
        private static bool Import(InventoryGraph account,InventoryGraph source,InventoryItem top,bool stashOnly=false)
        {
            var ids=Tree(source,top.Id);
            if(account.Items.Count+ids.Count>InventoryCatalog.MaxItems)return false;
            var copy=account.Clone();var item=top.Clone();
            if(stashOnly || copy.CanPlace(item,item.Parent,item.Region,item.X,item.Y,item.Rotated)!=InventoryError.None)
            {
                if(!copy.FindSpace(item,"stash",out var region,out var x,out var y))return false;
                item.Parent="stash";item.Region=region;item.X=x;item.Y=y;
            }
            copy.Items.Add(item);
            copy.Items.AddRange(source.Items.Where(i=>i.Id!=item.Id && ids.Contains(i.Id)).Select(i=>i.Clone()));
            if(copy.Validate()!=InventoryError.None)return false;
            account.Items=copy.Items;source.Items.RemoveAll(i=>ids.Contains(i.Id));return true;
        }
        // Returns fresh objects. Callers persist account/inbox/receipt/closed deployment together.
        public static InventoryError Settle(InventoryGraph account,RecoveryInbox inbox,InventoryGraph raid,
            out InventoryGraph stored,out RecoveryInbox pending)
        {
            stored=null;pending=null;
            if(inbox==null || inbox.Validate(account)!=InventoryError.None || inbox.ItemCount()!=0 || raid==null ||
                raid.Validate()!=InventoryError.None || raid.Find("stash")!=null || raid.Find("loot")!=null)return InventoryError.Invalid;
            var next=account.Clone();var cargo=raid.Clone();
            var allIds=new HashSet<string>(next.Items.Select(i=>i.Id));
            if(cargo.Items.Any(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root && !allIds.Add(i.Id)))return InventoryError.Invalid;
            int facts=0;
            foreach(var item in cargo.Items)
            {
                if(!cargo.Carried(item) || item.EmergencySupply || !item.FoundInRaid)continue;
                if(!MissionInventory.IsArtifact(item.Code))facts|=MissionInventory.FirstRecovery;
                else
                {
                    if(!MissionInventory.ValidSource(item))return InventoryError.Invalid;
                    if(!item.MissionRecovered)facts|=MissionInventory.FirstRecovery |
                        (item.Code=="mooncore_fragment" ? MissionInventory.FirstMooncore : MissionInventory.FirstArchive);
                    item.MissionRecovered=true;
                }
            }
            int added=facts & ~next.MissionFirsts;
            int dust=((added & MissionInventory.FirstRecovery)!=0 ? 10 : 0)+((added & MissionInventory.FirstMooncore)!=0 ? 30 : 0)+((added & MissionInventory.FirstArchive)!=0 ? 20 : 0);
            next.MissionFirsts|=facts;
            var result=new RecoveryInbox {Version=inbox.Version+1};
            foreach(var top in cargo.Items.Where(i=>i.Parent=="equipment" || i.Parent=="pockets").ToArray())Import(next,cargo,top);
            if(cargo.Items.Any(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root))result.Batches.Add(cargo);
            if(dust>0)
            {
                var reward=InventoryGraph.Create(false,false);
                if(reward.AddSupply("dust",dust,"pockets")!=InventoryError.None)return InventoryError.Invalid;
                foreach(var top in reward.Items.Where(i=>i.Parent=="pockets").ToArray())Import(next,reward,top,true);
                if(reward.Items.Any(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root))result.Batches.Add(reward);
            }
            next.Version++;
            if(result.Validate(next)!=InventoryError.None)return InventoryError.Invalid;
            stored=next;pending=result;return InventoryError.None;
        }
        public static InventoryError Claim(InventoryGraph account,RecoveryInbox inbox,RecoveryClaim request,
            out InventoryGraph stored,out RecoveryInbox pending)
        {
            stored=null;pending=null;
            if(request==null || inbox==null || inbox.Validate(account)!=InventoryError.None)return InventoryError.Invalid;
            if(request.ExpectedVersion!=account.Version || request.ExpectedRecoveryVersion!=inbox.Version)return InventoryError.Stale;
            var next=account.Clone();var result=inbox.Clone();bool moved=false,found=string.IsNullOrEmpty(request.ItemId);
            foreach(var batch in result.Batches)
                foreach(var top in batch.Items.Where(i=>i.Parent=="equipment" || i.Parent=="pockets").ToArray())
                {
                    if(!string.IsNullOrEmpty(request.ItemId) && top.Id!=request.ItemId)continue;
                    found=true;moved|=Import(next,batch,top,true);
                }
            if(!found)return InventoryError.Missing;
            if(!moved)return inbox.ItemCount()==0 ? InventoryError.Missing : InventoryError.Full;
            result.Batches.RemoveAll(b=>!b.Items.Any(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root));
            result.Version++;next.Version++;
            if(result.Validate(next)!=InventoryError.None)return InventoryError.Invalid;
            stored=next;pending=result;return InventoryError.None;
        }
    }
}
