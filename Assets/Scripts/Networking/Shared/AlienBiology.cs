#nullable disable
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    public sealed class AlienOrganDefinition
    {
        public readonly string Species, Code, Name, Description;
        public readonly ConsumableDefinition Food;
        public readonly int SellDust;
        public AlienOrganDefinition(string species,string code,string name,string description,ConsumableDefinition food,int sellDust)
        {Species=species;Code=code;Name=name;Description=description;Food=food;SellDust=sellDust;}
    }
    public sealed class HaloResearchDefinition
    {
        public readonly string Code, Name, Description, Icon;
        public readonly int Branch;
        public readonly string[] Materials;
        public readonly int[] Quantities;
        public HaloResearchDefinition(string code,string name,string description,string icon,int branch,string[] materials,int[] quantities)
        {Code=code;Name=name;Description=description;Icon=icon;Branch=branch;Materials=materials;Quantities=quantities;}
        public bool Unlocked(InventoryGraph graph)=>Branch==0 ? graph.HaloResearchLevel>0 : (graph.HaloResearchBranches&Branch)!=0;
        public static bool CanSpend(InventoryGraph graph,InventoryItem item,string code)=>item.Code==code && graph.RootOf(item.Id)=="stash" && !item.EmergencySupply &&
            (!MissionInventory.IsArtifact(code) || (item.MissionRecovered && MissionInventory.ValidSource(item)));
        public int Stored(InventoryGraph graph,string code)=>graph.Items.Where(i=>CanSpend(graph,i,code)).Sum(i=>i.Quantity);
        public bool CanAfford(InventoryGraph graph)=>Materials.Select((code,index)=>Stored(graph,code)>=Quantities[index]).All(value=>value);
        public void Unlock(InventoryGraph graph) {if(Branch==0)graph.HaloResearchLevel=1;else graph.HaloResearchBranches|=Branch;}
    }
    // Shared source for Unity, offline saves and the online persistence service.
    public static class AlienBiology
    {
        public static readonly AlienOrganDefinition[] Organs = {
            new AlienOrganDefinition("Spider","alien_silk_gland","Spider silk gland","60s: spread -40%; energy +5, hydration -15. Replaces other species-organ activity.",new ConsumableDefinition(5,-15,10,effect:AlienFoodEffect.SilkFocus,effectSeconds:60),18),
            new AlienOrganDefinition("Snake","alien_adrenal_gland","Snake adrenal gland","45s: movement +15%, hydration drain +50%. Energy +10, hydration -10. Replaces other species-organ activity.",new ConsumableDefinition(10,-10,10,effect:AlienFoodEffect.AdrenalRush,effectSeconds:45),20),
            new AlienOrganDefinition("Caterpillar","alien_nutrient_sac","Caterpillar nutrient sac","Energy +60, healing up to 10 HP, hydration -5, fullness +45. A heavy survival meal.",new ConsumableDefinition(60,-5,45,health:10),12),
            new AlienOrganDefinition("Tentacle","alien_contractile_heart","Tentacle contractile heart","45s: halo damage +40%, movement -15%. Energy +10, hydration -15. Replaces other species-organ activity.",new ConsumableDefinition(10,-15,20,effect:AlienFoodEffect.ContractilePower,effectSeconds:45),55),
            new AlienOrganDefinition("Centipede","alien_venom_gland","Centipede venom gland","45s: halo damage +35%, poison costs 0.6 HP/s and can kill. Energy +5, hydration -10. Replaces other species-organ activity.",new ConsumableDefinition(5,-10,10,effect:AlienFoodEffect.VenomPower,effectSeconds:45),24),
            new AlienOrganDefinition("Leech","alien_regen_sac","Leech regeneration sac","60s: restore living tissue at 1 HP/s, energy drain +50%. Energy +5, hydration -10. Replaces other species-organ activity.",new ConsumableDefinition(5,-10,15,effect:AlienFoodEffect.Regeneration,effectSeconds:60),22),
            new AlienOrganDefinition("Slime","alien_gel_bladder","Slime gel bladder","45s: incoming combat damage -25%, movement -20%. Energy +5, hydration -10. Replaces other species-organ activity.",new ConsumableDefinition(5,-10,20,effect:AlienFoodEffect.GelArmor,effectSeconds:45),25),
            new AlienOrganDefinition("Roots","alien_root_nodule","Root oxygen nodule","90s: suit oxygen drain -25%, movement -10%. Energy +15, hydration -10. Replaces other species-organ activity.",new ConsumableDefinition(15,-10,15,effect:AlienFoodEffect.OxygenReserve,effectSeconds:90),50),
            new AlienOrganDefinition("FaceHugger","alien_neural_ganglion","Facehugger neural ganglion","45s: firing interval -20%, spread -30%, hydration drain +75%. Energy +5, hydration -15. Replaces other species-organ activity.",new ConsumableDefinition(5,-15,10,effect:AlienFoodEffect.NeuralOverdrive,effectSeconds:45),32)
        };
        public static readonly HaloResearchDefinition MooncoreResearch = new HaloResearchDefinition(
            "mooncore_stabilization","MOONCORE STABILIZATION","Permanently unlocks fabrication of the Mooncore recycler. Research grants knowledge, not an equipped module.",
            "halo_mooncore_recycler",8,new[]{"mooncore_fragment","circuit_board"},new[]{1,2});
        public static readonly HaloResearchDefinition MooncoreFabrication = new HaloResearchDefinition(
            "halo_mooncore_recycler","Mooncore recycler","Fabricate one physical storage-slot module in personal storage.",
            "halo_mooncore_recycler",8,new[]{"mooncore_fragment","copper_coil"},new[]{1,1});
        public static readonly HaloResearchDefinition[] Research = {
            MooncoreResearch,
            new HaloResearchDefinition("halo_biology","BIOLOGICAL FIREPOWER","Permanent: all halo damage +60%. Retained after death.","alien_neural_core",0,new[]{"alien_neural_core","alien_tissue"},new[]{3,5}),
            new HaloResearchDefinition("halo_capacity","BIOLOGICAL CAPACITY","Permanent: all halo magazine capacities +40%. Upgrades do not grant free ammunition.","alien_contractile_heart",1,new[]{"alien_nutrient_sac","alien_gel_bladder","alien_contractile_heart"},new[]{2,2,1}),
            new HaloResearchDefinition("halo_charge","BIOLOGICAL CHARGING","Permanent: all halo recharge times -25%.","alien_root_nodule",2,new[]{"alien_adrenal_gland","alien_root_nodule","alien_tissue"},new[]{2,1,5}),
            new HaloResearchDefinition("halo_stability","BIOLOGICAL STABILITY","Permanent: all halo spread -30% and handling penalties -15%.","alien_silk_gland",4,new[]{"alien_silk_gland","alien_neural_ganglion","alien_tissue"},new[]{2,2,5})
        };
        public static AlienOrganDefinition Organ(string code)=>Organs.FirstOrDefault(o=>o.Code==code);
        // Upright variants share the contractile heart item and its existing food,
        // Chinese text, icon and research integration; no new inventory code needed.
        public static AlienOrganDefinition Species(string species)=>Organs.FirstOrDefault(o=>o.Species==(species=="StiltWalker"?"Tentacle":species));
        public static HaloResearchDefinition FindResearch(string code)=>Research.FirstOrDefault(r=>r.Code==code);
        public static AlienFoodEffect Effect(ushort modules)=>(AlienFoodEffect)(modules>>12);
        public static string Activity(AlienFoodEffect effect)
        {
            switch(effect)
            {
                case AlienFoodEffect.SilkFocus:return "SILK FOCUS / SPREAD -40%";
                case AlienFoodEffect.AdrenalRush:return "ADRENAL RUSH / SPEED +15% / THIRST +50%";
                case AlienFoodEffect.ContractilePower:return "CONTRACTILE POWER / DAMAGE +40% / SPEED -15%";
                case AlienFoodEffect.VenomPower:return "VENOM POWER / DAMAGE +35% / POISON 0.6 HP/s";
                case AlienFoodEffect.Regeneration:return "REGENERATION / +1 HP/s / HUNGER +50%";
                case AlienFoodEffect.GelArmor:return "GEL ARMOR / COMBAT DAMAGE -25% / SPEED -20%";
                case AlienFoodEffect.OxygenReserve:return "ROOT RESERVE / OXYGEN DRAIN -25% / SPEED -10%";
                case AlienFoodEffect.NeuralOverdrive:return "NEURAL OVERDRIVE / FIRE INTERVAL -20% / THIRST +75%";
                default:return "";
            }
        }
    }
}
