using System;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    // A one-time account migration, not a per-deployment supply. Callers commit
    // account/inbox/version together and defer while a raid owns the carried kit.
    public static class ProtectionKitMigration
    {
        public static bool TryApply(InventoryGraph account, RecoveryInbox inbox, out InventoryGraph stored, out RecoveryInbox pending)
        {
            stored=account;pending=inbox;
            if(account==null || inbox==null || inbox.Validate(account)!=InventoryError.None)
                throw new InvalidOperationException("Cannot migrate an invalid protection inventory.");
            if(account.ProtectionKitVersion>=1)return false;
            var next=account.Clone();var recovery=inbox.Clone();
            var helmet=next.Equipped("Helmet");
            if(helmet!=null && (helmet.Code=="helmet" || helmet.Code=="energy_head_dome"))
            {Complete(next,recovery,out stored,out pending);return true;}
            var grant=new InventoryItem {Id=Guid.NewGuid().ToString("D"),Code="energy_head_dome",Quantity=1};
            if(Place(next,grant,"equipment","Helmet",0,0,checkWeight:true) || FindAndPlace(next,grant,"stash",checkWeight:false))
            {Complete(next,recovery,out stored,out pending);return true;}
            foreach(var batch in recovery.Batches)
            {
                bool placed=Place(batch,grant,"equipment","Helmet",0,0,checkWeight:false);
                if(!placed)
                    foreach(var container in batch.CarryContainers().ToArray())
                        if(FindAndPlace(batch,grant,container,checkWeight:false)){placed=true;break;}
                if(!placed)continue;
                recovery.Version++;Complete(next,recovery,out stored,out pending);return true;
            }
            if(recovery.Batches.Count<2)
            {
                var batch=InventoryGraph.Create(false,false);
                if(Place(batch,grant,"equipment","Helmet",0,0,checkWeight:false))
                {recovery.Batches.Add(batch);recovery.Version++;Complete(next,recovery,out stored,out pending);return true;}
            }
            // Nothing was persisted: retry after space is made. No receipt is
            // consumed and no existing item or resource is replaced or refilled.
            return false;
        }
        private static bool FindAndPlace(InventoryGraph graph,InventoryItem item,string parent,bool checkWeight)
        {
            if(graph.Items.Count>=InventoryCatalog.MaxItems || graph.Find(parent)==null || !graph.FindSpace(item,parent,out var region,out var x,out var y))return false;
            return Place(graph,item,parent,region,x,y,checkWeight);
        }
        private static bool Place(InventoryGraph graph,InventoryItem item,string parent,string region,int x,int y,bool checkWeight)
        {
            if(graph.Items.Count>=InventoryCatalog.MaxItems || graph.CanPlace(item,parent,region,x,y,false)!=InventoryError.None)return false;
            var copy=item.Clone();copy.Parent=parent;copy.Region=region;copy.X=x;copy.Y=y;copy.Rotated=false;
            graph.Items.Add(copy);
            if(graph.Validate()==InventoryError.None && (!checkWeight || graph.CarriedWeight<=InventoryCatalog.CarryWeightLimit))return true;
            graph.Items.Remove(copy);return false;
        }
        private static void Complete(InventoryGraph graph,RecoveryInbox inbox,out InventoryGraph stored,out RecoveryInbox pending)
        {
            graph.ProtectionKitVersion=1;graph.Version++;
            if(inbox.Validate(graph)!=InventoryError.None)throw new InvalidOperationException("Protection migration would lose inventory ownership.");
            stored=graph;pending=inbox;
        }
    }
}
