#nullable disable
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum TacticalItemKind : byte
    { Vitality, Stamina, Antidote, Dynamite, Frag, Fire, Choke, AlertTrap, WireTrap, PoisonTrap, AmmoBox }
    public enum TacticalItemAction : byte { Use, Cancel, Disarm, Resupply, Extinguish }
    public enum TacticalItemFeedback : byte { None, Started, Cancelled, Interrupted, Completed, NeedGround, Limit, Poisoned, NoWound, NoTarget, NoRoom, Stale, Unavailable }
    public enum TacticalWorldPhase : byte { Flying, Armed, Fire, Choke, Wire, Poison, Alarm, Supply, Explosion, Removed }
    public sealed class TacticalItemDefinition
    {
        public readonly string Code;
        public readonly TacticalItemKind Kind;
        public readonly float UseSeconds, Fuse, Radius, Lifetime;
        public bool Throwable => Kind >= TacticalItemKind.Dynamite && Kind <= TacticalItemKind.Choke;
        public bool Trap => Kind >= TacticalItemKind.AlertTrap && Kind <= TacticalItemKind.PoisonTrap;
        public bool Deployable => Trap || Kind == TacticalItemKind.AmmoBox;
        public TacticalItemDefinition(string code, TacticalItemKind kind, float use, float fuse=0, float radius=0, float lifetime=0)
        { Code=code; Kind=kind; UseSeconds=use; Fuse=fuse; Radius=radius; Lifetime=lifetime; }
    }
    // Explicit Moonkov adaptation; these are not claimed as Hunt's numerical balance.
    public static class TacticalItemRules
    {
        public const int RulesVersion=1, MaxWorldObjects=64, MaxOwnerTraps=8;
        public const float TrapVerticalTolerance=.65f, InteractionRange=2.2f;
        public const float StaminaDuration=420, AntidoteDuration=600, BleedDuration=8, BurningDuration=4;
        public const int SupplyEnergy=400, EnergyPerUse=100;
        public static readonly TacticalItemDefinition[] Definitions = {
            new("vitality_shot",TacticalItemKind.Vitality,3), new("stamina_shot",TacticalItemKind.Stamina,3),
            new("antidote_shot",TacticalItemKind.Antidote,3),
            new("dynamite_bundle",TacticalItemKind.Dynamite,1,4,8), new("frag_bomb",TacticalItemKind.Frag,1,4,8),
            new("fire_bomb",TacticalItemKind.Fire,1,6,3,30), new("choke_bomb",TacticalItemKind.Choke,1,3,4,20),
            new("alert_trip_mine",TacticalItemKind.AlertTrap,3,0,1,180),
            new("concertina_trip_mine",TacticalItemKind.WireTrap,3,0,1,180),
            new("poison_trip_mine",TacticalItemKind.PoisonTrap,3,0,1,180),
            new("ammo_box",TacticalItemKind.AmmoBox,3,0,0,120)
        };
        public static TacticalItemDefinition Find(string code)
        { foreach(var d in Definitions) if(d.Code==code)return d; return null; }
        public static bool Accessible(InventoryGraph graph, InventoryItem item) => graph!=null && item!=null &&
            item.Quantity>0 && Find(item.Code)!=null && graph.Carried(item) &&
            (item.Parent==InventoryCatalog.Pockets || item.Parent==graph.Equipped("ChestRig")?.Id);
        public static InventoryError Validate(InventoryGraph graph,string id,int version,out TacticalItemDefinition definition)
        {
            definition=null;
            if(graph==null)return InventoryError.Invalid;
            if(graph.Version!=version)return InventoryError.Stale;
            var item=graph.Find(id); if(item==null)return InventoryError.Missing;
            definition=Find(item.Code);
            return Accessible(graph,item) ? InventoryError.None : InventoryError.Inaccessible;
        }
        public static InventoryError Take(InventoryGraph graph,string id,int version)
        {
            var error=Validate(graph,id,version,out _);if(error!=InventoryError.None)return error;
            var item=graph.Find(id);if(--item.Quantity==0)graph.Items.Remove(item);graph.Version++;return InventoryError.None;
        }
        public static float BlastDamage(TacticalItemKind kind,float distance)
        {
            if(!float.IsFinite(distance)||distance<0||distance>=8)return 0;
            return (kind==TacticalItemKind.Dynamite ? 650 : kind==TacticalItemKind.Frag ? 320 : 0)*Math.Min(1,(8-distance)/6);
        }
        // Works on a copy so inventory capacity failures never spend the box or alter cells.
        // Recharges an existing legal carried cell before adding one real cell to the rig/pockets.
        public static InventoryError PrepareResupply(InventoryGraph graph,int version,int available,out InventoryGraph result,out int supplied)
        {
            result=null; supplied=0;
            if(graph==null||available<1)return InventoryError.Invalid;
            if(graph.Version!=version)return InventoryError.Stale;
            var copy=graph.Clone(); int budget=Math.Min(EnergyPerUse,available);
            foreach(var item in copy.Items)
            {
                if(!BatteryEnergy.IsCell(item)||!copy.Carried(item) ||
                    (item.Parent!=InventoryCatalog.Pockets && item.Parent!=copy.Equipped("ChestRig")?.Id))continue;
                int missing=BatteryEnergy.Capacity-BatteryEnergy.Charge(item);
                int add=Math.Min(missing,budget);if(add<=0)continue;
                item.CellCharge=BatteryEnergy.Charge(item)+add; supplied=add; break;
            }
            if(supplied==0)
            {
                // AddSupply enforces real grid capacity/weight; grant only whole lawful cells.
                if(budget<BatteryEnergy.Capacity)return InventoryError.Full;
                var error=copy.AddSupply("cells",1,InventoryCatalog.Pockets);
                if(error!=InventoryError.None && copy.Equipped("ChestRig")!=null)error=copy.AddSupply("cells",1,copy.Equipped("ChestRig").Id);
                if(error!=InventoryError.None)return error;
                supplied=BatteryEnergy.Capacity;
            }
            var validation=copy.Validate();if(validation!=InventoryError.None){supplied=0;return validation;}
            copy.Version=graph.Version+1;result=copy;return InventoryError.None;
        }
    }
}
