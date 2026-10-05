#nullable disable
using System;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    public enum ShopOperation { Buy, Sell, Emergency }
    [Serializable] public sealed class ShopCommand
    {
        public string RequestId, Code, ItemId;
        public ShopOperation Operation;
        public int ExpectedVersion, Quantity;
    }
    [Serializable] public sealed class ShopOffer
    {
        public string Code, Name;
        public int BuyDust, SellDust;
        public ShopOffer() { }
        public ShopOffer(string code, int buy, int sell)
        { Code = code; Name = InventoryCatalog.Get(code).Name; BuyDust = buy; SellDust = sell; }
    }
    public static class ShopCatalog
    {
        public static readonly ShopOffer[] Offers = {
            new ShopOffer("rifle", 40, 20), new ShopOffer("pistol", 15, 7),
            new ShopOffer("shotgun", 50, 25), new ShopOffer("cells", 5, 2)
        };
        public static ShopOffer Find(string code) => Offers.FirstOrDefault(o => o.Code == code);
        public static bool IsWeapon(string code) => code == "rifle" || code == "compact" || code == "pistol" || code == "shotgun";
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
            if (command.Operation == ShopOperation.Emergency)
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
                    if (!string.IsNullOrEmpty(command.ItemId)) return "shop_invalid";
                    int cost = offer.BuyDust * command.Quantity;
                    if (copy.Count("dust") < cost) return "shop_insufficient_dust";
                    foreach (var stack in copy.Items.Where(i => i.Code == "dust").ToArray())
                    { int take = Math.Min(cost, stack.Quantity); stack.Quantity -= take; cost -= take; if (stack.Quantity == 0) copy.Items.Remove(stack); if (cost == 0) break; }
                    if (copy.AddSupply(offer.Code, command.Quantity, "stash") != InventoryError.None) return "inventory_Full";
                }
                else if (command.Operation == ShopOperation.Sell)
                {
                    var item = copy.Find(command.ItemId);
                    if (item == null || item.Code != offer.Code || item.Quantity < command.Quantity) return "inventory_Missing";
                    if (item.EmergencySupply) return "shop_emergency_supply";
                    if (copy.RootOf(item.Id) != "stash") return "shop_store_first";
                    item.Quantity -= command.Quantity; if (item.Quantity == 0) copy.Items.Remove(item);
                    if (copy.AddSupply("dust", offer.SellDust * command.Quantity, "stash") != InventoryError.None) return "inventory_Full";
                }
                else return "shop_invalid";
            }
            if (copy.Validate() != InventoryError.None) return "shop_invalid";
            copy.Version = graph.Version + 1; updated = copy; return null;
        }
    }
}
