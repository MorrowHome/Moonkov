#nullable disable
using System;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    public enum ShopOperation { Buy, Sell, Emergency, Research, Fabricate }
    [Serializable] public sealed class ShopCommand
    {
        public string RequestId, Code, ItemId;
        public ShopOperation Operation;
        public int ExpectedVersion, Quantity;
    }
    [Serializable] public sealed class ShopOffer
    {
        public string Code, Name;
        // -1 means sell-only loot; zero remains a free purchase for starter supplies.
        public int BuyDust, SellDust;
        public ShopOffer() { }
        public ShopOffer(string code, int buy, int sell)
        { Code = code; Name = InventoryCatalog.Get(code).Name; BuyDust = buy; SellDust = sell; }
    }
    public static class ShopCatalog
    {
        public const int ResearchOrgans = 3, ResearchTissue = 5;
        public static readonly ShopOffer[] Offers = new[] {
            new ShopOffer("alien_tissue", -1, 8), new ShopOffer("alien_core", -1, 65),
            new ShopOffer("rifle", 0, 20), new ShopOffer("pistol", 0, 7),
            new ShopOffer("shotgun", 0, 25), new ShopOffer("sniper", 0, 45), new ShopOffer("cells", 0, 2),
            new ShopOffer("nocturne", 68, 22), new ShopOffer("tempest", 62, 20), new ShopOffer("rondo", 50, 16), new ShopOffer("cicada", 30, 10), new ShopOffer("trine", 42, 14), new ShopOffer("aegis", 64, 21), new ShopOffer("vesper", 58, 19), new ShopOffer("thorn", 50, 16), new ShopOffer("sundown", 72, 24),
            new ShopOffer("halo_capacitor", 18, 6), new ShopOffer("halo_charge_bridge", 18, 6),
            new ShopOffer("halo_mooncore_recycler", -1, 9),
            new ShopOffer("halo_focus_lens", 22, 7), new ShopOffer("halo_fan_array", 22, 7),
            new ShopOffer("halo_pulse_controller", 28, 9), new ShopOffer("halo_hit_marker", 25, 8),
            new ShopOffer("medkit", 0, 3), new ShopOffer("ration", 0, 2), new ShopOffer("water", 0, 2),
            new ShopOffer("oxygen_canister", 0, 3), new ShopOffer("energy_grenade", 12, 4),
            new ShopOffer("med_gel", 5, 1), new ShopOffer("trauma_kit", 18, 6),
            new ShopOffer("vitality_shot", 20, 6),
            new ShopOffer("stamina_shot", 18, 6),
            new ShopOffer("antidote_shot", 15, 5),
            new ShopOffer("dynamite_bundle", 24, 8),
            new ShopOffer("frag_bomb", 28, 9),
            new ShopOffer("fire_bomb", 16, 5),
            new ShopOffer("choke_bomb", 12, 4),
            new ShopOffer("alert_trip_mine", 12, 4),
            new ShopOffer("concertina_trip_mine", 20, 6),
            new ShopOffer("poison_trip_mine", 18, 6),
            new ShopOffer("ammo_box", 24, 8),
            new ShopOffer("energy_gel", 4, 1), new ShopOffer("protein_bar", 6, 2),
            new ShopOffer("electrolyte", 5, 1), new ShopOffer("oxygen_reserve", 12, 4),
            new ShopOffer("scout_rig", 18, 6), new ShopOffer("field_rig", 48, 16),
            new ShopOffer("sling_pack", 24, 8), new ShopOffer("cargo_pack", 96, 32),
            new ShopOffer("circuit_board", -1, 8), new ShopOffer("copper_coil", -1, 6),
            new ShopOffer("ceramic_plate", -1, 7), new ShopOffer("optical_sensor", -1, 16),
            new ShopOffer("data_drive", -1, 28), new ShopOffer("helium3_sample", -1, 45),
            new ShopOffer("mooncore_fragment", -1, 50), new ShopOffer("mc00_archive_drive", -1, 35),
            new ShopOffer("quantum_core", -1, 80), new ShopOffer("black_box", -1, 60)
        }.Concat(AlienBiology.Organs.Select(o=>new ShopOffer(o.Code,-1,o.SellDust))).ToArray();
        public static ShopOffer Find(string code) => Offers.FirstOrDefault(o => o.Code == code);
        public static int SaleDust(InventoryItem item, int quantity, int fullPrice) => BatteryEnergy.IsCell(item)
            ? BatteryEnergy.PortionEnergy(item, quantity) * fullPrice / BatteryEnergy.Capacity : quantity * fullPrice;
        public static bool IsWeapon(string code) => InventoryCatalog.Get(code)?.Kind == ItemKind.LongGun || InventoryCatalog.Get(code)?.Kind == ItemKind.Pistol;
        public static bool CanClaim(InventoryGraph graph) => graph != null && graph.Find("stash") != null && !graph.Items.Any(i => IsWeapon(i.Code));
    }
    // Prices and item mutations are shared for display/testing; only the backend commits trades.
    public static class ShopRules
    {
        public static string Apply(InventoryGraph graph, ShopCommand command, out InventoryGraph updated)
        {
            updated = null;
            if (graph == null || graph.Validate() != InventoryError.None || graph.Find("stash") == null ||
                command == null || !Guid.TryParse(command.RequestId, out _)) return "shop_invalid";
            if (command.ExpectedVersion != graph.Version) return "inventory_Stale";
            var copy = graph.Clone();
            if (command.Operation == ShopOperation.Research || command.Operation == ShopOperation.Fabricate)
            {
                bool fabricate=command.Operation==ShopOperation.Fabricate;
                var research=fabricate ? (command.Code==AlienBiology.MooncoreFabrication.Code ? AlienBiology.MooncoreFabrication : null) : AlienBiology.FindResearch(command.Code);
                if(research==null || !string.IsNullOrEmpty(command.ItemId) || command.Quantity!=1)return "shop_invalid";
                if(fabricate && !research.Unlocked(copy))return "fabrication_locked";
                if(!fabricate && research.Unlocked(copy))return "research_complete";
                bool Spend(string code,int quantity)
                {
                    var stock=copy.Items.Where(i=>HaloResearchDefinition.CanSpend(copy,i,code)).ToArray();
                    if(stock.Sum(i=>i.Quantity)<quantity)return false;
                    foreach(var stack in stock)
                    { int take=Math.Min(quantity,stack.Quantity);stack.Quantity-=take;quantity-=take;if(stack.Quantity==0)copy.Items.Remove(stack);if(quantity==0)break; }
                    return true;
                }
                for(int i=0;i<research.Materials.Length;i++)
                    if(!Spend(research.Materials[i],research.Quantities[i]))return fabricate ? "fabrication_materials" : "research_materials";
                if(fabricate)
                {
                    if(copy.AddSupply(research.Code,1,"stash")!=InventoryError.None)return "inventory_Full";
                }
                else research.Unlock(copy);
            }
            else if (command.Operation == ShopOperation.Emergency)
            {
                if (!string.IsNullOrEmpty(command.Code) || !string.IsNullOrEmpty(command.ItemId) || command.Quantity != 0) return "shop_invalid";
                if (!ShopCatalog.CanClaim(copy)) return "shop_has_weapon";
                var pistol = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "pistol", LoadedAmmo = 6,
                    EmergencySupply = true, Parent = "equipment", Region = "Pistol" };
                if (copy.CarriedWeight + InventoryCatalog.Get("pistol").Weight <= InventoryCatalog.CarryWeightLimit &&
                    copy.CanPlace(pistol, "equipment", "Pistol", 0, 0, false) == InventoryError.None) copy.Items.Add(pistol);
                else if (copy.AddSupply("pistol", 1, "stash", emergencySupply: true) != InventoryError.None) return "inventory_Full";
                if (copy.AddSupply("cells", 3, emergencySupply: true) != InventoryError.None &&
                    copy.AddSupply("cells", 3, "stash", emergencySupply: true) != InventoryError.None) return "inventory_Full";
            }
            else
            {
                var offer = ShopCatalog.Find(command.Code);
                var definition = InventoryCatalog.Get(command.Code);
                if (offer == null || definition == null || command.Quantity < 1 || command.Quantity > definition.MaxStack) return "shop_invalid";
                if (command.Operation == ShopOperation.Buy)
                {
                    if (!string.IsNullOrEmpty(command.ItemId) || offer.BuyDust < 0) return "shop_invalid";
                    int cost = offer.BuyDust * command.Quantity;
                    if (copy.Count("dust") < cost) return "shop_insufficient_dust";
                    foreach (var stack in copy.Items.Where(i => i.Code == "dust").ToArray())
                    { int take = Math.Min(cost, stack.Quantity); stack.Quantity -= take; cost -= take; if (stack.Quantity == 0) copy.Items.Remove(stack); if (cost == 0) break; }
                    if (definition.Container)
                    {
                        for (int i = 0; i < command.Quantity; i++)
                        {
                            var purchased = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = offer.Code };
                            if (!copy.FindSpace(purchased, "stash", out var region, out var x, out var y)) return "inventory_Full";
                            purchased.Parent = "stash"; purchased.Region = region; purchased.X = x; purchased.Y = y;
                            copy.Items.Add(purchased);
                        }
                    }
                    else if (copy.AddSupply(offer.Code, command.Quantity, "stash") != InventoryError.None) return "inventory_Full";
                }
                else if (command.Operation == ShopOperation.Sell)
                {
                    var item = copy.Find(command.ItemId);
                    if (item == null || item.Code != offer.Code || item.Quantity < command.Quantity) return "inventory_Missing";
                    if (item.EmergencySupply) return "shop_emergency_supply";
                    if (copy.Children(item.Id).Any()) return "shop_remove_modules_first";
                    if (copy.RootOf(item.Id) != "stash") return "shop_store_first";
                    int refund = ShopCatalog.SaleDust(item, command.Quantity, offer.SellDust);
                    BatteryEnergy.Take(item, command.Quantity); if (item.Quantity == 0) copy.Items.Remove(item);
                    if (copy.AddSupply("dust", refund, "stash") != InventoryError.None) return "inventory_Full";
                }
                else return "shop_invalid";
            }
            if (copy.Validate() != InventoryError.None) return "shop_invalid";
            copy.Version = graph.Version + 1; updated = copy; return null;
        }
    }
}
