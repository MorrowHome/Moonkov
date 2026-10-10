#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    public enum ItemKind { Root, Material, Cell, Helmet, LongGun, Pistol, Rig, Backpack, Medical }
    public enum InventoryError { None, Stale, Missing, Invalid, Incompatible, Bounds, Collision, Cycle, Full, Overweight, Inaccessible }
    public enum InventoryOperation { Move, Split, Merge }

    public sealed class InventoryRegion
    {
        public readonly string Id; public readonly int Width, Height; public readonly ItemKind? SlotKind;
        public InventoryRegion(string id, int width, int height, ItemKind? slot = null) { Id = id; Width = width; Height = height; SlotKind = slot; }
    }
    public sealed class ItemDefinition
    {
        public readonly string Code, Name; public readonly ItemKind Kind;
        public readonly int Width, Height, MaxStack; public readonly float Weight;
        public readonly InventoryRegion[] Regions;
        public bool Container => Regions.Length > 0;
        public ItemDefinition(string code, string name, ItemKind kind, int w, int h, int maxStack, float kg, params InventoryRegion[] regions)
        { Code = code; Name = name; Kind = kind; Width = w; Height = h; MaxStack = maxStack; Weight = kg; Regions = regions; }
    }
    // One catalog shared by Unity and the data service. Dimensions are external footprints;
    // Regions describe independent internal compartments, never a second copy of the items.
    public static class InventoryCatalog
    {
        public const string Stash = "stash", Equipment = "equipment", Pockets = "pockets";
        public const int MaxDepth = 8, MaxItems = 2048;
        public const float CarryWeightLimit = 45;
        private static readonly string[] LootCodes = { "dust", "alloy", "cells", "small_pack", "rig" };
        public static string LootCode(int index) => LootCodes[index % LootCodes.Length];
        public static readonly IReadOnlyDictionary<string, ItemDefinition> Definitions = Build();
        private static Dictionary<string, ItemDefinition> Build()
        {
            var defs = new[] {
                new ItemDefinition("stash", "Personal storage", ItemKind.Root, 0, 0, 1, 0, new InventoryRegion("main", 10, 80)),
                new ItemDefinition("equipment", "Equipment", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("Helmet", 1, 1, ItemKind.Helmet), new InventoryRegion("Primary", 1, 1, ItemKind.LongGun),
                    new InventoryRegion("Secondary", 1, 1, ItemKind.LongGun), new InventoryRegion("Pistol", 1, 1, ItemKind.Pistol),
                    new InventoryRegion("ChestRig", 1, 1, ItemKind.Rig), new InventoryRegion("Backpack", 1, 1, ItemKind.Backpack)),
                new ItemDefinition("pockets", "Pockets", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("1", 1, 1), new InventoryRegion("2", 1, 1), new InventoryRegion("3", 1, 1), new InventoryRegion("4", 1, 1)),
                new ItemDefinition("loot", "Supply cache", ItemKind.Root, 0, 0, 1, 0, new InventoryRegion("main", 6, 5)),
                new ItemDefinition("dust", "Moon dust", ItemKind.Material, 1, 1, 20, .15f),
                new ItemDefinition("alloy", "Lunar alloy", ItemKind.Material, 2, 1, 10, .6f),
                new ItemDefinition("cells", "Energy cell", ItemKind.Cell, 1, 1, 12, .2f),
                new ItemDefinition("medkit", "M-40 Medical injector", ItemKind.Medical, 1, 1, 4, .15f),
                new ItemDefinition("helmet", "L-01 Flight helmet", ItemKind.Helmet, 2, 2, 1, 1.2f),
                new ItemDefinition("rifle", "Halo Rifle", ItemKind.LongGun, 2, 2, 1, 3.2f),
                new ItemDefinition("compact", "Halo Rifle (Compact)", ItemKind.LongGun, 2, 2, 1, 2.4f),
                new ItemDefinition("pistol", "Halo Revolver", ItemKind.Pistol, 1, 1, 1, .8f),
                new ItemDefinition("shotgun", "Halo Shotgun", ItemKind.LongGun, 2, 2, 1, 3.2f),
                new ItemDefinition("sniper", "Halo Sniper", ItemKind.LongGun, 2, 2, 1, 4.5f),
                new ItemDefinition("rig", "R-04 Chest rig", ItemKind.Rig, 2, 3, 1, .9f,
                    new InventoryRegion("left", 1, 2), new InventoryRegion("center", 2, 2), new InventoryRegion("right", 1, 2)),
                new ItemDefinition("backpack", "B-08 Expedition pack", ItemKind.Backpack, 3, 3, 1, 1.3f, new InventoryRegion("main", 5, 6)),
                new ItemDefinition("small_pack", "B-04 Scout pack", ItemKind.Backpack, 2, 3, 1, .7f, new InventoryRegion("main", 4, 4))
            };
            var result = new Dictionary<string, ItemDefinition>(); foreach (var def in defs) result.Add(def.Code, def); return result;
        }
        public static ItemDefinition Get(string code) => code != null && Definitions.TryGetValue(code, out var value) ? value : null;
    }
    [Serializable] public sealed class InventoryItem
    {
        public string Id, Code, Parent, Region; public int X, Y, Quantity = 1; public bool Rotated, FoundInRaid;
        // -1 is a new weapon; live ammo follows the item through moves/deploy/settlement.
        public int LoadedAmmo = -1;
        // Missing in old saves means full. One partial cell per stack; the rest are full.
        public int CellCharge = -1;
        public bool EmergencySupply;
        public InventoryItem Clone() => (InventoryItem)MemberwiseClone();
    }
    [Serializable] public sealed class InventoryCommand
    {
        public InventoryOperation Operation; public int ExpectedVersion;
        public string ItemId, Parent, Region, TargetId; public int X, Y, Quantity; public bool Rotated;
    }
    [Serializable] public sealed class InventoryGraph
    {
        public int Version = 1, StashRows = 80, LootRows = 5;
        public int WeaponKitVersion;
        public List<InventoryItem> Items = new List<InventoryItem>();
        public InventoryItem Find(string id) => Items.Find(i => i.Id == id);
        public InventoryGraph Clone() => new InventoryGraph { Version = Version, StashRows = StashRows, LootRows = LootRows, WeaponKitVersion = WeaponKitVersion, Items = Items.ConvertAll(i => i.Clone()) };
        public int Rows(string parent, InventoryRegion region) => Find(parent)?.Code == "stash" ? StashRows : Find(parent)?.Code == "loot" ? LootRows : region.Height;
        public IEnumerable<InventoryItem> Children(string parent, string region = null) => Items.Where(i => i.Parent == parent && (region == null || i.Region == region));
        public string RootOf(string id)
        {
            var item = Find(id);
            for (int n = 0; item != null && n <= InventoryCatalog.MaxDepth; n++)
            { if (string.IsNullOrEmpty(item.Parent)) return item.Id; item = Find(item.Parent); }
            return null;
        }
        public bool Carried(InventoryItem item) { var root = RootOf(item.Id); return root == InventoryCatalog.Equipment || root == InventoryCatalog.Pockets; }
        public int Count(string code, bool carriedOnly = false) => Items.Where(i => i.Code == code && (!carriedOnly || Carried(i))).Sum(i => i.Quantity);
        public float CarriedWeight => Items.Where(Carried).Sum(i => InventoryCatalog.Get(i.Code).Weight * i.Quantity);
        public InventoryItem Equipped(string slot) => Items.Find(i => i.Parent == InventoryCatalog.Equipment && i.Region == slot);
        public static InventoryGraph Create(bool stash = true, bool starter = true)
        {
            var g = new InventoryGraph();
            foreach (string root in stash ? new[] { "stash", "equipment", "pockets" } : new[] { "equipment", "pockets" })
                g.Items.Add(new InventoryItem { Id = root, Code = root });
            if (starter)
            {
                g.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "rig", Parent = "equipment", Region = "ChestRig" });
                g.Items.Add(new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = "backpack", Parent = "equipment", Region = "Backpack" });
                g.AddStarterWeapons(!stash);
            }
            return g;
        }
        public void AddStarterWeapons(bool equipped = false)
        {
            if (WeaponKitVersion >= 1) return;
            foreach (var pair in new[] { (Code:"rifle", Slot:"Primary"), (Code:"shotgun", Slot:"Secondary"), (Code:"pistol", Slot:"Pistol") })
            {
                if (Items.Any(i => i.Code == pair.Code)) continue;
                var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = pair.Code };
                if (equipped && Equipped(pair.Slot) == null)
                { item.Parent = "equipment"; item.Region = pair.Slot; }
                else
                {
                    if (Find("stash") == null) continue;
                    while (!FindSpace(item, "stash", out _, out _, out _) && StashRows < 4096) StashRows = Math.Min(4096, StashRows * 2);
                    if (!FindSpace(item, "stash", out var region, out var x, out var y)) throw new InvalidOperationException("No space for starter halo weapon.");
                    item.Parent = "stash"; item.Region = region; item.X = x; item.Y = y;
                }
                Items.Add(item);
            }
            WeaponKitVersion = 1;
        }
        private InventoryRegion RegionFor(string parent, string region) => InventoryCatalog.Get(Find(parent)?.Code)?.Regions.FirstOrDefault(r => r.Id == region);
        public InventoryError CanPlace(InventoryItem item, string parent, string region, int x, int y, bool rotated, string ignore = null)
        {
            var definition = InventoryCatalog.Get(item.Code); var target = Find(parent); var compartment = RegionFor(parent, region);
            if (definition == null || definition.Kind == ItemKind.Root || target == null || compartment == null) return InventoryError.Missing;
            string ancestor = parent;
            for (int depth = 0; ancestor != null; depth++)
            {
                if (ancestor == item.Id) return InventoryError.Cycle;
                if (depth >= InventoryCatalog.MaxDepth) return InventoryError.Cycle;
                ancestor = Find(ancestor)?.Parent;
            }
            if (compartment.SlotKind.HasValue)
            {
                if (definition.Kind != compartment.SlotKind.Value || item.Quantity != 1) return InventoryError.Incompatible;
                x = y = 0;
            }
            else
            {
                // Pockets and rig pouches are for supplies, never bags or other wearable gear.
                if ((target.Code == "pockets" || target.Code == "rig") && definition.Container) return InventoryError.Incompatible;
                int width = rotated ? definition.Height : definition.Width, height = rotated ? definition.Width : definition.Height;
                int rows = Rows(parent, compartment);
                if (x < 0 || y < 0 || x > compartment.Width - width || y > rows - height) return InventoryError.Bounds;
            }
            foreach (var other in Children(parent, region))
            {
                if (other.Id == item.Id || other.Id == ignore) continue;
                if (compartment.SlotKind.HasValue) return InventoryError.Collision;
                var def = InventoryCatalog.Get(other.Code);
                int ow = other.Rotated ? def.Height : def.Width, oh = other.Rotated ? def.Width : def.Height;
                int iw = rotated ? definition.Height : definition.Width, ih = rotated ? definition.Width : definition.Height;
                if (x < other.X + ow && x + iw > other.X && y < other.Y + oh && y + ih > other.Y) return InventoryError.Collision;
            }
            return InventoryError.None;
        }
        public bool FindSpace(InventoryItem item, string parent, out string region, out int x, out int y)
        {
            var regions = InventoryCatalog.Get(Find(parent)?.Code)?.Regions;
            if (regions != null) foreach (var r in regions)
                for (int row = 0; row < Rows(parent, r); row++)
                    for (int col = 0; col < r.Width; col++)
                        if (CanPlace(item, parent, r.Id, col, row, item.Rotated) == InventoryError.None)
                        { region = r.Id; x = col; y = row; return true; }
            region = null; x = y = 0; return false;
        }
        private static void Position(InventoryItem item, string parent, string region, int x, int y, bool rotated)
        { item.Parent = parent; item.Region = region; item.X = x; item.Y = y; item.Rotated = rotated; }
        public InventoryError TryApply(InventoryCommand request, bool enforceWeight = true)
        {
            if (request == null || request.ExpectedVersion != Version) return InventoryError.Stale;
            var copy = Clone(); var error = copy.Apply(request);
            if (error == InventoryError.None) error = copy.Validate();
            // Rearranging an already-heavy migrated kit is allowed; adding more weight is not.
            if (error == InventoryError.None && enforceWeight && copy.CarriedWeight > InventoryCatalog.CarryWeightLimit && copy.CarriedWeight > CarriedWeight + .001f) error = InventoryError.Overweight;
            if (error != InventoryError.None) return error;
            Items = copy.Items; Version++; return InventoryError.None;
        }
        private InventoryError Apply(InventoryCommand r)
        {
            var item = Find(r.ItemId); var def = InventoryCatalog.Get(item?.Code);
            if (item == null || def == null || def.Kind == ItemKind.Root) return InventoryError.Missing;
            if (r.Operation == InventoryOperation.Merge)
            {
                var target = Find(r.TargetId); int count = r.Quantity == 0 ? item.Quantity : r.Quantity;
                if (target == null || target.Id == item.Id || target.Code != item.Code || def.Container || count <= 0 || count > item.Quantity
                    || (long)target.Quantity + count > def.MaxStack || target.FoundInRaid != item.FoundInRaid || target.EmergencySupply != item.EmergencySupply) return InventoryError.Invalid;
                if (!BatteryEnergy.CanMerge(item, target, count)) return InventoryError.Invalid;
                BatteryEnergy.Merge(item, target, count); if (item.Quantity == 0) Items.Remove(item); return InventoryError.None;
            }
            if (r.Operation == InventoryOperation.Split)
            {
                if (def.Container || r.Quantity <= 0 || r.Quantity >= item.Quantity) return InventoryError.Invalid;
                var split = item.Clone(); split.Id = Guid.NewGuid().ToString("D"); split.Quantity = r.Quantity;
                var error = CanPlace(split, r.Parent, r.Region, r.X, r.Y, r.Rotated);
                if (error != InventoryError.None) return error;
                split = BatteryEnergy.Take(item, r.Quantity); Position(split, r.Parent, r.Region, r.X, r.Y, r.Rotated); Items.Add(split); return InventoryError.None;
            }
            if (r.Operation != InventoryOperation.Move) return InventoryError.Invalid;
            var slot = RegionFor(r.Parent, r.Region);
            var displaced = slot?.SlotKind.HasValue == true ? Children(r.Parent, r.Region).FirstOrDefault(i => i.Id != item.Id) : null;
            var result = CanPlace(item, r.Parent, r.Region, r.X, r.Y, r.Rotated, displaced?.Id);
            if (result != InventoryError.None) return result;
            string oldParent = item.Parent, oldRegion = item.Region; int oldX = item.X, oldY = item.Y; bool oldRotation = item.Rotated;
            Position(item, r.Parent, r.Region, slot.SlotKind.HasValue ? 0 : r.X, slot.SlotKind.HasValue ? 0 : r.Y, r.Rotated);
            if (displaced != null)
            {
                displaced.Parent = null; // Reserve incoming's destination before searching for the replaced kit.
                if (CanPlace(displaced, oldParent, oldRegion, oldX, oldY, oldRotation) == InventoryError.None)
                    Position(displaced, oldParent, oldRegion, oldX, oldY, oldRotation);
                else if (FindSpace(displaced, oldParent, out var region, out var x, out var y)) Position(displaced, oldParent, region, x, y, displaced.Rotated);
                else return InventoryError.Full;
            }
            return InventoryError.None;
        }
        public InventoryError Validate()
        {
            // A world-loot snapshot joins two owners; account/actor graphs keep the
            // original limit. The extra loot root must also fit a maximum-sized drop.
            int limit=Items!=null && Items.Any(i=>i?.Id=="loot") ? InventoryCatalog.MaxItems*2 : InventoryCatalog.MaxItems;
            if (Version < 1 || Items == null || Items.Count > limit || StashRows < 1 || StashRows > 4096 || LootRows < 1 || LootRows > 24) return InventoryError.Invalid;
            var ids = new HashSet<string>();
            foreach (var item in Items)
            {
                if (item == null) return InventoryError.Invalid;
                var def = InventoryCatalog.Get(item.Code);
                if (item.Id == null || !ids.Add(item.Id) || def == null || item.Quantity <= 0 || item.Quantity > def.MaxStack || item.LoadedAmmo < -1 || item.LoadedAmmo > 10000) return InventoryError.Invalid;
                if (item.CellCharge != -1 && (!BatteryEnergy.IsCell(item) || item.CellCharge < 1 || item.CellCharge > BatteryEnergy.Capacity)) return InventoryError.Invalid;
                if (def.Kind == ItemKind.Root)
                { if (item.Id != item.Code || item.Parent != null || item.Quantity != 1) return InventoryError.Invalid; }
                else if (!Guid.TryParse(item.Id, out _) || item.Parent == null) return InventoryError.Invalid;
            }
            // Check every identity/type first: a malformed neighbour must not crash CanPlace.
            if (!ids.Contains("equipment") || !ids.Contains("pockets")) return InventoryError.Invalid;
            foreach (var item in Items)
            {
                if (InventoryCatalog.Get(item.Code).Kind != ItemKind.Root)
                {
                    var error = CanPlace(item, item.Parent, item.Region, item.X, item.Y, item.Rotated);
                    if (error != InventoryError.None) return error;
                    if (RootOf(item.Id) == null) return InventoryError.Cycle;
                }
            }
            return InventoryError.None;
        }
        public IEnumerable<string> CarryContainers()
        {
            // Direct pockets/rig access first; backpacks (including nested bags) last.
            if (Find("pockets") != null) yield return "pockets";
            var rig = Equipped("ChestRig"); if (rig != null) yield return rig.Id;
            foreach (var i in Items) if (InventoryCatalog.Get(i.Code).Kind == ItemKind.Backpack && Carried(i)) yield return i.Id;
        }
        public InventoryError AddSupply(string code, int quantity, string parent = null, bool foundInRaid = false, string id = null, bool emergencySupply = false, int cellCharge = -1)
        {
            if (quantity == 0) return InventoryError.None;
            var definition = InventoryCatalog.Get(code); if (definition == null || definition.Container || quantity < 0) return InventoryError.Invalid;
            if (cellCharge != -1 && (code != "cells" || cellCharge < 1 || cellCharge > BatteryEnergy.Capacity)) return InventoryError.Invalid;
            var copy = Clone();
            var supply = new InventoryItem { Id = id ?? Guid.NewGuid().ToString("D"), Code = code, Quantity = quantity,
                FoundInRaid = foundInRaid, EmergencySupply = emergencySupply, CellCharge = cellCharge };
            var containers = parent == null ? copy.CarryContainers().ToArray() : new[] { parent };
            foreach (var container in containers)
            {
                foreach (var stack in copy.Children(container).Where(i => i.Code == code && i.FoundInRaid == foundInRaid && i.EmergencySupply == emergencySupply))
                {
                    int add = Math.Min(supply.Quantity, definition.MaxStack - stack.Quantity);
                    if (add > 0 && BatteryEnergy.CanMerge(supply, stack, add)) BatteryEnergy.Merge(supply, stack, add);
                }
                while (supply.Quantity > 0)
                {
                    var item = supply.Clone(); item.Quantity = Math.Min(supply.Quantity, definition.MaxStack);
                    if (!copy.FindSpace(item, container, out var region, out var x, out var y)) break;
                    item = BatteryEnergy.Take(supply, item.Quantity);
                    Position(item, container, region, x, y, false); copy.Items.Add(item);
                }
            }
            if (supply.Quantity > 0) return InventoryError.Full;
            if (parent == null && copy.CarriedWeight > InventoryCatalog.CarryWeightLimit) return InventoryError.Overweight;
            var validation = copy.Validate(); if (validation != InventoryError.None) return validation;
            Items = copy.Items; Version++; return InventoryError.None;
        }
        public bool ConsumeAccessibleCell()
        {
            var rig = Equipped("ChestRig");
            var item = Items.Find(i => i.Code == "cells" && (i.Parent == "pockets" || i.Parent == rig?.Id));
            if (item == null) return false;
            BatteryEnergy.Take(item, 1); if (item.Quantity == 0) Items.Remove(item); Version++; return true;
        }
        public bool MedicalAccessible(InventoryItem item) => item != null && item.Code == "medkit" &&
            (item.Parent == InventoryCatalog.Pockets || item.Parent == Equipped("ChestRig")?.Id);

        // Only the match server commits this result. Full health/death/stale versions
        // leave both health and the stack untouched; a replay cannot consume it twice.
        public InventoryError UseMedical(string itemId, int expectedVersion, float health, float maximum, out float healed, bool allowZeroHealth = false)
        {
            healed = health;
            if (expectedVersion != Version) return InventoryError.Stale;
            var item = Find(itemId);
            if (item == null) return InventoryError.Missing;
            if (!MedicalAccessible(item)) return InventoryError.Inaccessible;
            if (float.IsNaN(health) || float.IsInfinity(health) || float.IsNaN(maximum) || float.IsInfinity(maximum) ||
                health < 0f || (!allowZeroHealth && health == 0f) || maximum <= health || item.Quantity < 1) return InventoryError.Invalid;
            healed = Math.Min(maximum, health + 40f);
            if (--item.Quantity == 0) Items.Remove(item);
            Version++;
            return InventoryError.None;
        }
        private bool AccessibleCell(InventoryItem item) => item.Code == "cells" && (item.Parent == "pockets" || item.Parent == Equipped("ChestRig")?.Id);
        public int CellEnergy(bool carriedOnly = false, bool accessibleOnly = false) => Items.Where(i => i.Code == "cells" &&
            (!carriedOnly || Carried(i)) && (!accessibleOnly || AccessibleCell(i))).Sum(BatteryEnergy.Stored);
        // Called once by the server when authorizing a reload; prediction uses its target ammo.
        public bool TryRecharge(int currentAmmo, int magazineSize, int energyPerRound, out int targetAmmo)
        {
            targetAmmo = currentAmmo;
            if (currentAmmo < 0 || magazineSize <= currentAmmo || energyPerRound < 1) return false;
            int rounds = Math.Min(magazineSize - currentAmmo, CellEnergy(accessibleOnly: true) / energyPerRound);
            if (rounds == 0) return false;
            int remaining = rounds * energyPerRound;
            foreach (var item in Items.Where(AccessibleCell).ToArray())
            {
                int used = Math.Min(remaining, BatteryEnergy.Stored(item)); BatteryEnergy.Spend(item, used); remaining -= used;
                if (item.Quantity == 0) Items.Remove(item);
                if (remaining == 0) break;
            }
            targetAmmo += rounds; Version++; return true;
        }
        public InventoryError AddLoot(string code)
        {
            var def = InventoryCatalog.Get(code);
            if (def == null) return InventoryError.Invalid;
            if (!def.Container) return AddSupply(code, 1, foundInRaid: true);
            var copy = Clone(); var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = code, FoundInRaid = true };
            string parent = null, region = null; int x = 0, y = 0;
            foreach (string container in new[] { "equipment" }.Concat(copy.CarryContainers()))
                if (copy.FindSpace(item, container, out region, out x, out y)) { parent = container; break; }
            if (parent == null) return InventoryError.Full;
            Position(item, parent, region, x, y, false); copy.Items.Add(item);
            if (copy.CarriedWeight > InventoryCatalog.CarryWeightLimit) return InventoryError.Overweight;
            var error = copy.Validate(); if (error != InventoryError.None) return error;
            Items = copy.Items; Version++; return InventoryError.None;
        }
        public InventoryGraph ExtractLoadout()
        {
            var raid = Clone(); raid.Items.RemoveAll(i => !Carried(i));
            var taken = new HashSet<string>(raid.Items.Where(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root).Select(i => i.Id));
            Items.RemoveAll(i => taken.Contains(i.Id));
            raid.Version = 1; Version++; return raid;
        }
        public InventoryError ReturnLoadout(InventoryGraph raid)
        {
            if (raid == null || raid.Validate() != InventoryError.None) return InventoryError.Invalid;
            var copy = Clone();
            foreach (var source in raid.Items.Where(i => InventoryCatalog.Get(i.Code).Kind != ItemKind.Root))
                if (copy.Find(source.Id) != null) return InventoryError.Invalid;
            // Restore root children into equipment/pockets where available, otherwise store whole subtrees.
            foreach (var top in raid.Items.Where(i => i.Parent == "equipment" || i.Parent == "pockets"))
            {
                var item = top.Clone();
                if (copy.CanPlace(item, item.Parent, item.Region, item.X, item.Y, item.Rotated) != InventoryError.None)
                {
                    if (!copy.FindSpace(item, "stash", out var region, out var x, out var y)) return InventoryError.Full;
                    Position(item, "stash", region, x, y, item.Rotated);
                }
                copy.Items.Add(item);
            }
            copy.Items.AddRange(raid.Items.Where(i => i.Parent != null && i.Parent != "equipment" && i.Parent != "pockets").Select(i => i.Clone()));
            var error = copy.Validate(); if (error != InventoryError.None) return error;
            Items = copy.Items; Version++; return InventoryError.None;
        }
    }
}
