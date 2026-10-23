#nullable disable
using System;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    // One transaction across two independent owners. Joined snapshots are presentation only.
    public static class LootInventoryExchange
    {
        public const string Root = "loot";
        public static InventoryGraph CreateCache(int index)
        {
            var graph=InventoryGraph.Create(false,false);
            graph.Items.Add(new InventoryItem {Id=Root,Code=Root});
            graph.AddSupply("dust",2+index%4,Root,true);
            graph.AddSupply("alloy",1+index%3,Root,true);
            graph.AddSupply("cells",1+index%2,Root,true);
            if(index%3==0) graph.AddSupply("medkit",1+index%2,Root,true);
            graph.AddSupply(index%2==0 ? "ration" : "water",1,Root,true);
            if(index%2==0) graph.AddSupply("oxygen_canister",1,Root,true);
            if(index%2==0) graph.AddSupply(HaloModules.Codes[(index / 2) % HaloModules.Codes.Length], 1, Root, true);
            if(index%4==0)
            {
                var bag=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="small_pack",FoundInRaid=true};
                if(graph.FindSpace(bag,Root,out var region,out var x,out var y))
                {bag.Parent=Root;bag.Region=region;bag.X=x;bag.Y=y;graph.Items.Add(bag);}
            }
            return graph;
        }
        public static InventoryGraph Snapshot(InventoryGraph carried,InventoryGraph cache)
        {
            var joined=carried.Clone();
            joined.LootRows=cache.LootRows;
            joined.Items.AddRange(cache.Items.Where(i=>i.Id==Root || cache.RootOf(i.Id)==Root).Select(i=>i.Clone()));
            return joined;
        }
        // Build and validate the new owner before clearing the dead player's graph. Only
        // direct equipment/pocket items move; descendants retain their exact placements.
        public static InventoryGraph DropOnDeath(InventoryGraph carried)
        {
            if(carried==null || carried.Validate()!=InventoryError.None || carried.Find("stash")!=null || carried.Find(Root)!=null)
                throw new InvalidOperationException("Cannot drop an invalid raid inventory.");
            var cache=InventoryGraph.Create(false,false);cache.LootRows=24;
            var retained = SecureInventory.ProtectedIds(carried);
            cache.Items.Add(new InventoryItem {Id=Root,Code=Root});
            foreach(var source in carried.Items.Where(i=>!retained.Contains(i.Id) && (i.Parent==InventoryCatalog.Equipment || i.Parent==InventoryCatalog.Pockets)))
            {
                var item=source.Clone();
                if(!cache.FindSpace(item,Root,out var region,out var x,out var y))
                    throw new InvalidOperationException("Death bag cannot fit carried equipment.");
                item.Parent=Root;item.Region=region;item.X=x;item.Y=y;cache.Items.Add(item);
            }
            cache.Items.AddRange(carried.Items.Where(i=>!retained.Contains(i.Id) && i.Parent!=null && i.Parent!=InventoryCatalog.Equipment && i.Parent!=InventoryCatalog.Pockets).Select(i=>i.Clone()));
            if(cache.Validate()!=InventoryError.None)throw new InvalidOperationException("Death bag lost an inventory hierarchy.");
            carried.Items.RemoveAll(i=>InventoryCatalog.Get(i.Code).Kind!=ItemKind.Root && !retained.Contains(i.Id));carried.Version++;
            return cache;
        }
        // Prepare both owners without mutating either. Registration must succeed before commit.
        public static InventoryError PrepareDrop(InventoryGraph carried, string itemId, int expectedVersion,
            out InventoryGraph remaining, out InventoryGraph dropped)
        {
            remaining = null; dropped = null;
            if (carried == null || carried.Validate() != InventoryError.None || carried.Find("stash") != null || carried.Find(Root) != null)
                return InventoryError.Invalid;
            if (carried.Version != expectedVersion) return InventoryError.Stale;
            var item = carried.Find(itemId);
            if (item == null) return InventoryError.Missing;
            if (!carried.Carried(item) || InventoryCatalog.Get(item.Code).Kind == ItemKind.Root) return InventoryError.Inaccessible;
            var ids = new System.Collections.Generic.HashSet<string> { itemId };
            for (int depth = 0; depth < InventoryCatalog.MaxDepth; depth++)
                foreach (var child in carried.Items) if (child.Parent != null && ids.Contains(child.Parent)) ids.Add(child.Id);
            var cache = InventoryGraph.Create(false, false); cache.LootRows = 24;
            cache.Items.Add(new InventoryItem { Id = Root, Code = Root });
            var top = item.Clone(); top.Rotated = false;
            if (!cache.FindSpace(top, Root, out var region, out var x, out var y)) return InventoryError.Full;
            top.Parent = Root; top.Region = region; top.X = x; top.Y = y; cache.Items.Add(top);
            cache.Items.AddRange(carried.Items.Where(i => i.Id != itemId && ids.Contains(i.Id)).Select(i => i.Clone()));
            var next = carried.Clone(); next.Items.RemoveAll(i => ids.Contains(i.Id)); next.Version++;
            if (cache.Validate() != InventoryError.None || next.Validate() != InventoryError.None) return InventoryError.Invalid;
            remaining = next; dropped = cache; return InventoryError.None;
        }
        public static InventoryError TryApply(InventoryGraph carried,InventoryGraph cache,InventoryCommand command,int expectedCacheVersion)
        {
            if(carried==null || cache==null || command==null || carried.Find(Root)!=null || cache.Find(Root)==null)
                return InventoryError.Invalid;
            if(command.ExpectedVersion!=carried.Version || expectedCacheVersion!=cache.Version) return InventoryError.Stale;
            var joined=Snapshot(carried,cache);
            if(joined.Validate()!=InventoryError.None) return InventoryError.Invalid;
            bool touchesCache=joined.RootOf(command.ItemId)==Root || joined.RootOf(command.Parent)==Root || joined.RootOf(command.TargetId)==Root;
            var error=joined.TryApply(command);
            if(error!=InventoryError.None) return error;
            var nextCarried=carried.Clone(); var nextCache=cache.Clone();
            nextCarried.Items=joined.Items.Where(i=>joined.RootOf(i.Id)!=Root).ToList();
            nextCache.Items.RemoveAll(i=>i.Id==Root || cache.RootOf(i.Id)==Root);
            nextCache.Items.AddRange(joined.Items.Where(i=>joined.RootOf(i.Id)==Root));
            if(nextCarried.Validate()!=InventoryError.None || nextCache.Validate()!=InventoryError.None) return InventoryError.Invalid;
            carried.Items=nextCarried.Items; carried.Version++;
            if(touchesCache) {cache.Items=nextCache.Items;cache.Version++;}
            return InventoryError.None;
        }
    }
}
