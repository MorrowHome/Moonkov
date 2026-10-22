#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS.Inventory
{
    public enum ItemKind { Root, Material, Cell, Helmet, LongGun, Pistol, Rig, Backpack, Medical, Food, Drink, StorageModule, ShapingModule, UtilityModule, Oxygen, Throwable, Shield, Melee }
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
        private static readonly string[] LootCodes = { "dust", "alloy", "cells", "small_pack", "rig", "ration", "water", "halo_capacitor", "halo_charge_bridge", "halo_focus_lens", "halo_fan_array", "halo_pulse_controller", "halo_hit_marker", "oxygen_canister" };
        public static string LootCode(int index) => LootCodes[index % LootCodes.Length];
        public static readonly IReadOnlyDictionary<string, ItemDefinition> Definitions = Build();
        private static Dictionary<string, ItemDefinition> Build()
        {
            var defs = new[] {
                new ItemDefinition("stash", "Personal storage", ItemKind.Root, 0, 0, 1, 0, new InventoryRegion("main", 10, 80)),
                new ItemDefinition("equipment", "Equipment", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("Helmet", 1, 1, ItemKind.Helmet), new InventoryRegion("Primary", 1, 1, ItemKind.LongGun),
                    new InventoryRegion("Secondary", 1, 1, ItemKind.LongGun), new InventoryRegion("Pistol", 1, 1, ItemKind.Pistol),
                    new InventoryRegion("ChestRig", 1, 1, ItemKind.Rig), new InventoryRegion("Backpack", 1, 1, ItemKind.Backpack),
                    new InventoryRegion("Shield", 1, 1, ItemKind.Shield), new InventoryRegion("Melee", 1, 1, ItemKind.Melee)),
                new ItemDefinition("pockets", "Pockets", ItemKind.Root, 0, 0, 1, 0,
                    new InventoryRegion("1", 1, 1), new InventoryRegion("2", 1, 1), new InventoryRegion("3", 1, 1), new InventoryRegion("4", 1, 1)),
                new ItemDefinition("loot", "Supply cache", ItemKind.Root, 0, 0, 1, 0, new InventoryRegion("main", 6, 5)),
                new ItemDefinition("dust", "Moon dust", ItemKind.Material, 1, 1, 20, .15f),
                new ItemDefinition("alien_tissue", "Sealed alien tissue", ItemKind.Material, 1, 1, 10, .12f),
                new ItemDefinition("alien_core", "Alien boss nucleus", ItemKind.Material, 1, 1, 2, .4f),
                new ItemDefinition("alien_raw_meat", "Raw alien meat", ItemKind.Food, 1, 1, 4, .25f),
                new ItemDefinition("alien_neural_core", "Rare alien neural organ", ItemKind.Food, 1, 1, 1, .35f),
                new ItemDefinition("alloy", "Lunar alloy", ItemKind.Material, 2, 1, 10, .6f),
                new ItemDefinition("cells", "Energy cell", ItemKind.Cell, 1, 1, 12, .2f),
                new ItemDefinition("energy_grenade", "E-01 Energy grenade", ItemKind.Throwable, 1, 1, 3, .25f),
                new ItemDefinition("vitality_shot", "Vitality shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("stamina_shot", "Stamina shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("antidote_shot", "Antidote shot", ItemKind.Throwable, 1, 1, 3, 0.15f),
                new ItemDefinition("dynamite_bundle", "Dynamite bundle", ItemKind.Throwable, 1, 2, 2, 0.6f),
                new ItemDefinition("frag_bomb", "Fragmentation bomb", ItemKind.Throwable, 1, 1, 2, 0.4f),
                new ItemDefinition("fire_bomb", "Fire bomb", ItemKind.Throwable, 1, 2, 2, 0.5f),
                new ItemDefinition("choke_bomb", "Choke bomb", ItemKind.Throwable, 1, 1, 3, 0.3f),
                new ItemDefinition("alert_trip_mine", "Alert trip mine", ItemKind.Throwable, 1, 1, 3, 0.25f),
                new ItemDefinition("concertina_trip_mine", "Concertina trip mine", ItemKind.Throwable, 1, 2, 2, 0.5f),
                new ItemDefinition("poison_trip_mine", "Poison trip mine", ItemKind.Throwable, 1, 1, 3, 0.3f),
                new ItemDefinition("ammo_box", "Energy supply box", ItemKind.Throwable, 2, 2, 1, 1.0f),
                new ItemDefinition("medkit", "M-40 Medical injector", ItemKind.Medical, 1, 1, 4, .15f),
                new ItemDefinition("ration", "Expedition ration", ItemKind.Food, 1, 1, 3, .25f),
                new ItemDefinition("water", "Sealed drinking water", ItemKind.Drink, 1, 2, 2, .5f),
                new ItemDefinition("oxygen_canister", "O-40 Emergency oxygen canister", ItemKind.Oxygen, 1, 1, 3, .35f),
                new ItemDefinition("med_gel", "Sealed medical gel", ItemKind.Medical, 1, 1, 6, .08f),
                new ItemDefinition("trauma_kit", "Field trauma kit", ItemKind.Medical, 1, 2, 2, .45f),
                new ItemDefinition("energy_gel", "Energy gel", ItemKind.Food, 1, 1, 5, .08f),
                new ItemDefinition("protein_bar", "Protein bar", ItemKind.Food, 1, 1, 4, .15f),
                new ItemDefinition("electrolyte", "Electrolyte pouch", ItemKind.Drink, 1, 1, 3, .3f),
                new ItemDefinition("oxygen_reserve", "O-80 Reserve oxygen cylinder", ItemKind.Oxygen, 1, 2, 2, .8f),
                new ItemDefinition("circuit_board", "Control circuit board", ItemKind.Material, 1, 1, 4, .18f),
                new ItemDefinition("copper_coil", "Copper induction coil", ItemKind.Material, 2, 1, 3, .65f),
                new ItemDefinition("ceramic_plate", "Technical ceramic plate", ItemKind.Material, 2, 2, 2, .9f),
                new ItemDefinition("optical_sensor", "Optical sensor", ItemKind.Material, 1, 1, 2, .22f),
                new ItemDefinition("data_drive", "Encrypted data drive", ItemKind.Material, 1, 1, 1, .12f),
                new ItemDefinition("mooncore_fragment", "Mooncore fragment", ItemKind.Material, 1, 1, 1, .3f),
                new ItemDefinition("mc00_archive_drive", "MC00 事故档案盘", ItemKind.Material, 1, 1, 1, .12f),
                new ItemDefinition("helium3_sample", "Helium-3 sample", ItemKind.Material, 1, 2, 1, .6f),
                new ItemDefinition("quantum_core", "Quantum processor", ItemKind.Material, 2, 1, 1, .4f),
                new ItemDefinition("black_box", "Survey black box", ItemKind.Material, 2, 2, 1, 1.6f),
                new ItemDefinition("helmet", "L-01 Flight helmet", ItemKind.Helmet, 2, 2, 1, 1.2f),
                new ItemDefinition("energy_head_dome", "L-02 Energy seal dome", ItemKind.Helmet, 2, 2, 1, .45f),
                new ItemDefinition("energy_shield_generator", "S-100 Energy shield generator", ItemKind.Shield, 2, 2, 1, 1.2f),
                new ItemDefinition("energy_sword", "Energy sword", ItemKind.Melee, 1, 3, 1, 1.8f),
                new ItemDefinition("dagger", "Dagger", ItemKind.Melee, 1, 2, 1, .35f),
                new ItemDefinition("rifle", "Halo Rifle", ItemKind.LongGun, 2, 2, 1, 3.2f),
                new ItemDefinition("compact", "Halo Rifle (Compact)", ItemKind.LongGun, 2, 2, 1, 2.4f),
                new ItemDefinition("pistol", "Halo Revolver", ItemKind.Pistol, 1, 1, 1, .8f),
                new ItemDefinition("shotgun", "Halo Shotgun", ItemKind.LongGun, 2, 2, 1, 3.2f),
                new ItemDefinition("sniper", "Halo Sniper", ItemKind.LongGun, 2, 2, 1, 4.5f),
                new ItemDefinition("cicada", "Cicada / 蝉鸣", ItemKind.LongGun, 2, 2, 1, 2.2f),
                new ItemDefinition("trine", "Trine / 三律", ItemKind.LongGun, 2, 2, 1, 3.1f),
                new ItemDefinition("aegis", "Aegis / 天枢", ItemKind.LongGun, 2, 2, 1, 6.2f),
                new ItemDefinition("vesper", "Vesper / 望舒", ItemKind.LongGun, 2, 2, 1, 3.8f),
                new ItemDefinition("thorn", "Thorn / 星棘", ItemKind.LongGun, 2, 2, 1, 3.3f),
                new ItemDefinition("nocturne", "Nocturne / 夜曲", ItemKind.LongGun, 2, 2, 1, 3.7f),
                new ItemDefinition("tempest", "Tempest / 岚轮", ItemKind.LongGun, 2, 2, 1, 4.1f),
                new ItemDefinition("rondo", "Rondo / 回环", ItemKind.LongGun, 2, 2, 1, 3.0f),
                new ItemDefinition("sundown", "Sundown / 坠日", ItemKind.LongGun, 2, 2, 1, 5.2f),

                new ItemDefinition("halo_capacitor", "Expansion capacitor", ItemKind.StorageModule, 1, 1, 1, .2f),
                new ItemDefinition("halo_charge_bridge", "Rapid charge bridge", ItemKind.StorageModule, 1, 1, 1, .15f),
                new ItemDefinition("halo_mooncore_recycler", "Mooncore recycler", ItemKind.StorageModule, 1, 1, 1, .2f),
                new ItemDefinition("halo_focus_lens", "Focus lens", ItemKind.ShapingModule, 1, 1, 1, .15f),
                new ItemDefinition("halo_fan_array", "Fan array", ItemKind.ShapingModule, 1, 1, 1, .2f),
                new ItemDefinition("halo_pulse_controller", "Pulse controller", ItemKind.ShapingModule, 1, 1, 1, .15f),
                new ItemDefinition("halo_hit_marker", "Hit marker", ItemKind.UtilityModule, 1, 1, 1, .1f),
                new ItemDefinition("rig", "R-04 Chest rig", ItemKind.Rig, 2, 3, 1, .9f,
                    new InventoryRegion("left", 1, 2), new InventoryRegion("center", 2, 2), new InventoryRegion("right", 1, 2)),
                new ItemDefinition("backpack", "B-08 Expedition pack", ItemKind.Backpack, 3, 3, 1, 1.3f, new InventoryRegion("main", 5, 6)),
                new ItemDefinition("small_pack", "B-04 Scout pack", ItemKind.Backpack, 2, 3, 1, .7f, new InventoryRegion("main", 4, 4)),
                new ItemDefinition("scout_rig", "R-02 Scout rig", ItemKind.Rig, 2, 2, 1, .5f,
                    new InventoryRegion("left", 1, 2), new InventoryRegion("center", 1, 2), new InventoryRegion("right", 1, 2)),
                new ItemDefinition("field_rig", "R-06 Field rig", ItemKind.Rig, 3, 3, 1, 1.4f,
                    new InventoryRegion("left", 2, 2), new InventoryRegion("center", 2, 2), new InventoryRegion("right", 2, 2)),
                new ItemDefinition("sling_pack", "B-03 Sling pack", ItemKind.Backpack, 2, 2, 1, .45f, new InventoryRegion("main", 3, 4)),
                new ItemDefinition("cargo_pack", "B-12 Cargo pack", ItemKind.Backpack, 4, 4, 1, 2.8f, new InventoryRegion("main", 6, 7))
            };
            var result = new Dictionary<string, ItemDefinition>(); foreach (var def in defs) result.Add(def.Code, def);
            foreach(var organ in AlienBiology.Organs)result.Add(organ.Code,new ItemDefinition(organ.Code,organ.Name,ItemKind.Food,1,1,3,.2f));
            return result;
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
        // -1 denotes a new/legacy item, initialized once when first authoritatively bound.
        // Live resources follow this item through move/drop/loot/reconnect and settlement.
        public float ShieldCharge = -1, ShieldEnergy = -1, SuitOxygen = -1;
        // Assigned only by the authoritative facility output. Kept through loot/death/return.
        public string MissionWorldId, MissionOperationId;
        public int MissionContentVersion;
        public bool MissionRecovered;
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
        public int ProtectionKitVersion;
        public int HaloResearchLevel;
        // Bits 0..2: existing biology; bit 3: mooncore fabrication knowledge only.
        public int HaloResearchBranches;
        // Account facts; never merged from a client/raid graph. Bits: recovery, mooncore, archive.
        public int MissionFirsts;
        public List<InventoryItem> Items = new List<InventoryItem>();
        public InventoryItem Find(string id) => Items.Find(i => i.Id == id);
        public InventoryGraph Clone() => new InventoryGraph { Version = Version, StashRows = StashRows, LootRows = LootRows, WeaponKitVersion = WeaponKitVersion,
            HaloResearchLevel = HaloResearchLevel, HaloResearchBranches = HaloResearchBranches, MissionFirsts = MissionFirsts, ProtectionKitVersion = ProtectionKitVersion, Items = Items.ConvertAll(i => i.Clone()) };
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
        private InventoryRegion RegionFor(string parent, string region)
        {
            var code = Find(parent)?.Code;
            return InventoryCatalog.Get(code)?.Regions.FirstOrDefault(r => r.Id == region) ??
                (HaloModules.IsWeapon(code) ? HaloModules.Slots.FirstOrDefault(r => r.Id == region) : null);
        }
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
            if (HaloModules.IsWeapon(target.Code) && (!HaloModules.Compatible(target.Code, item.Code) ||
                region != HaloModules.Slot(HaloModules.FromCode(item.Code)))) return InventoryError.Incompatible;
            if (compartment.SlotKind.HasValue)
            {
                if (definition.Kind != compartment.SlotKind.Value || item.Quantity != 1) return InventoryError.Incompatible;
                x = y = 0;
            }
            else
            {
                // Pockets and rig pouches are for supplies, never bags or other wearable gear.
                if ((target.Code == "pockets" || InventoryCatalog.Get(target.Code)?.Kind == ItemKind.Rig) && definition.Container) return InventoryError.Incompatible;
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
            // Mounting, removing and swapping modules are base-only transactions. Whole
            // weapon subtrees can still move freely during a raid.
            var source = Find(request.ItemId);
            if (HaloModules.IsWeapon(Find(source?.Parent)?.Code) ||
                HaloModules.IsWeapon(Find(request.Parent)?.Code))
                if (Find(InventoryCatalog.Stash) == null || Find("loot") != null) return InventoryError.Inaccessible;
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
        private static bool ValidProtectionValue(float value) => value == -1 || float.IsFinite(value) && value >= 0 && value <= 100;
        public InventoryError Validate()
        {
            // A world-loot snapshot joins two owners; account/actor graphs keep the
            // original limit. The extra loot root must also fit a maximum-sized drop.
            int limit=Items!=null && Items.Any(i=>i?.Id=="loot") ? InventoryCatalog.MaxItems*2 : InventoryCatalog.MaxItems;
            if (Version < 1 || ProtectionKitVersion < 0 || ProtectionKitVersion > 1 || HaloResearchLevel < 0 || HaloResearchLevel > 1 || HaloResearchBranches < 0 || HaloResearchBranches > 15 || MissionFirsts < 0 || MissionFirsts > 7 || Items == null || Items.Count > limit || StashRows < 1 || StashRows > 4096 || LootRows < 1 || LootRows > 24) return InventoryError.Invalid;
            var ids = new HashSet<string>();
            foreach (var item in Items)
            {
                if (item == null) return InventoryError.Invalid;
                var def = InventoryCatalog.Get(item.Code);
                if (item.Id == null || !ids.Add(item.Id) || def == null || item.Quantity <= 0 || item.Quantity > def.MaxStack || item.LoadedAmmo < -1 || item.LoadedAmmo > 10000) return InventoryError.Invalid;
                if (item.CellCharge != -1 && (!BatteryEnergy.IsCell(item) || item.CellCharge < 1 || item.CellCharge > BatteryEnergy.Capacity)) return InventoryError.Invalid;
                if (!ValidProtectionValue(item.ShieldCharge) || !ValidProtectionValue(item.ShieldEnergy) || !ValidProtectionValue(item.SuitOxygen)) return InventoryError.Invalid;
                if ((item.ShieldCharge != -1 || item.ShieldEnergy != -1) && item.Code != "energy_shield_generator") return InventoryError.Invalid;
                if (item.SuitOxygen != -1 && item.Code != "energy_head_dome" && item.Code != "helmet") return InventoryError.Invalid;
                if (!MissionInventory.ValidSource(item)) return InventoryError.Invalid;
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
        // Shortcut transfer never searches nested bags. Manual placement still can.
        public IEnumerable<string> QuickTransferContainers()
        {
            if (Find(InventoryCatalog.Pockets) != null) yield return InventoryCatalog.Pockets;
            var rig = Equipped("ChestRig"); if (rig != null) yield return rig.Id;
            var pack = Equipped("Backpack"); if (pack != null) yield return pack.Id;
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
        public bool MedicalAccessible(InventoryItem item) => item != null && ConsumableCatalog.MedicalRestore(item.Code) > 0 &&
            (item.Parent == InventoryCatalog.Pockets || item.Parent == Equipped("ChestRig")?.Id);

        // Food/water may be used from any carried bag. Emergency suit canisters
        // must be immediately accessible in a pocket or the equipped chest rig.
        // Joined loot snapshots and the personal stash are never consumable here.
        public bool ConsumableAccessible(InventoryItem item) => item != null &&
            ConsumableCatalog.Get(item.Code) != null && Carried(item) &&
            (ConsumableCatalog.Get(item.Code).Oxygen <= 0 ||
                item.Parent == InventoryCatalog.Pockets || item.Parent == Equipped("ChestRig")?.Id);
        public InventoryError ConsumeOne(string itemId, int expectedVersion)
        {
            if (expectedVersion != Version) return InventoryError.Stale;
            var item = Find(itemId);
            if (item == null) return InventoryError.Missing;
            if (!ConsumableAccessible(item)) return InventoryError.Inaccessible;
            if (item.Quantity < 1) return InventoryError.Invalid;
            if (--item.Quantity == 0) Items.Remove(item);
            Version++; return InventoryError.None;
        }

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
            healed = Math.Min(maximum, health + ConsumableCatalog.MedicalRestore(item.Code));
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
            raid.MissionFirsts = 0;
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
            var missionError = MissionInventory.RecordRecovery(copy, raid);
            if (missionError != InventoryError.None) return missionError;
            var error = copy.Validate(); if (error != InventoryError.None) return error;
            Items = copy.Items; MissionFirsts = copy.MissionFirsts; StashRows = copy.StashRows; Version++; return InventoryError.None;
        }
    }

    // Shared by online/offline ReturnLoadout so inventory and first rewards commit together.
    // No UI discovery, accepted-task list or pinned-state input can affect this calculation.
    public static class MissionInventory
    {
        public const int FirstRecovery = 1, FirstMooncore = 2, FirstArchive = 4;
        public static bool IsArtifact(string code) => code == "mooncore_fragment" || code == "mc00_archive_drive";
        public static bool ValidSource(InventoryItem item)
        {
            if (!IsArtifact(item.Code)) return item.MissionWorldId == null && item.MissionOperationId == null && item.MissionContentVersion == 0 && !item.MissionRecovered;
            return item.Quantity == 1 && item.FoundInRaid && !item.EmergencySupply && item.MissionContentVersion == 1 &&
                Guid.TryParse(item.MissionWorldId, out var world) && world != Guid.Empty &&
                Guid.TryParse(item.MissionOperationId, out var operation) && operation != Guid.Empty;
        }
        public static InventoryError RecordRecovery(InventoryGraph account, InventoryGraph raid)
        {
            if (account == null || raid == null || account.Find("stash") == null) return InventoryError.Invalid;
            int facts = 0;
            foreach (var item in raid.Items)
            {
                if (!raid.Carried(item) || item.EmergencySupply || !item.FoundInRaid) continue;
                if (!IsArtifact(item.Code)) { facts |= FirstRecovery; continue; }
                if (!ValidSource(item)) return InventoryError.Invalid;
                if (!item.MissionRecovered)
                    facts |= FirstRecovery | (item.Code == "mooncore_fragment" ? FirstMooncore : FirstArchive);
                var returned = account.Find(item.Id);
                if (returned == null) return InventoryError.Invalid;
                returned.MissionRecovered = true;
            }
            int added = facts & ~account.MissionFirsts;
            int dust = ((added & FirstRecovery) != 0 ? 10 : 0) + ((added & FirstMooncore) != 0 ? 30 : 0) + ((added & FirstArchive) != 0 ? 20 : 0);
            if (dust > 0)
            {
                InventoryError result;
                while ((result = account.AddSupply("dust", dust, "stash")) == InventoryError.Full && account.StashRows < 4096)
                    account.StashRows = Math.Min(4096, account.StashRows * 2);
                if (result != InventoryError.None) return result;
            }
            account.MissionFirsts |= facts;
            return InventoryError.None;
        }
    }
}
