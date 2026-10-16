using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum RaidLootProfile { Supplies, Medical, Industrial, Equipment }

    // Counts and relative weights are authored together; the match server alone rolls them.
    public static class RaidLootTable
    {
        private readonly struct Entry
        {
            public readonly string Code;
            public readonly int Weight, Minimum, Maximum;
            public Entry(string code, int weight, int minimum = 1, int maximum = 1)
            { Code = code; Weight = weight; Minimum = minimum; Maximum = maximum; }
        }

        private sealed class Table
        {
            public readonly Entry[] Guaranteed, Pool;
            public readonly int MinimumRolls, MaximumRolls;
            public Table(Entry[] guaranteed, Entry[] pool, int minimumRolls, int maximumRolls)
            { Guaranteed = guaranteed; Pool = pool; MinimumRolls = minimumRolls; MaximumRolls = maximumRolls; }
        }

        private static readonly Table[] Tables =
        {
            new Table(new[] { new Entry("ration", 1, 1, 2), new Entry("water", 1) }, new[]
            {
                new Entry("oxygen_canister", 20, 1, 2), new Entry("cells", 16, 1, 3),
                new Entry("medkit", 10), new Entry("dust", 10, 2, 5),
                new Entry("energy_gel", 14, 1, 2), new Entry("protein_bar", 12, 1, 2),
                new Entry("electrolyte", 12), new Entry("med_gel", 8, 1, 2),
                new Entry("oxygen_reserve", 6), new Entry("sling_pack", 5),
                new Entry("small_pack", 5), new Entry("alloy", 5, 1, 2)
            }, 2, 3),
            new Table(new[] { new Entry("medkit", 1, 2, 4) }, new[]
            {
                new Entry("med_gel", 30, 2, 3), new Entry("trauma_kit", 18),
                new Entry("electrolyte", 16, 1, 2), new Entry("oxygen_reserve", 12),
                new Entry("water", 12, 1, 2), new Entry("oxygen_canister", 12, 1, 2),
                new Entry("ration", 8, 1, 2), new Entry("cells", 8, 1, 2),
                new Entry("small_pack", 4)
            }, 2, 3),
            new Table(new[] { new Entry("alloy", 1, 2, 5), new Entry("dust", 1, 3, 8) }, new[]
            {
                new Entry("cells", 18, 2, 5), new Entry("oxygen_canister", 10),
                new Entry("halo_capacitor", 8), new Entry("halo_charge_bridge", 8),
                new Entry("halo_focus_lens", 4), new Entry("halo_hit_marker", 4),
                new Entry("circuit_board", 22, 1, 2), new Entry("copper_coil", 20, 1, 2),
                new Entry("ceramic_plate", 16, 1, 2), new Entry("optical_sensor", 10),
                new Entry("data_drive", 6), new Entry("helium3_sample", 4),
                new Entry("quantum_core", 2), new Entry("black_box", 2)
            }, 2, 3),
            new Table(new[] { new Entry("cells", 1, 2, 4) }, new[]
            {
                new Entry("halo_capacitor", 12), new Entry("halo_charge_bridge", 12),
                new Entry("halo_focus_lens", 10), new Entry("halo_fan_array", 10),
                new Entry("halo_pulse_controller", 10), new Entry("halo_hit_marker", 8),
                new Entry("scout_rig", 12), new Entry("field_rig", 8),
                new Entry("sling_pack", 10), new Entry("cargo_pack", 4),
                new Entry("helmet", 8), new Entry("rig", 6), new Entry("compact", 3),
                new Entry("pistol", 3), new Entry("shotgun", 2), new Entry("sniper", 1),
                new Entry("optical_sensor", 8), new Entry("data_drive", 6),
                new Entry("black_box", 3), new Entry("quantum_core", 1)
            }, 2, 4)
        };

        public static InventoryGraph Create(RaidLootProfile profile, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            int index = (int)profile;
            if (index < 0 || index >= Tables.Length) index = 0;
            var table = Tables[index];
            var graph = InventoryGraph.Create(false, false);
            graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
            foreach (var entry in table.Guaranteed)
                if (!TryAdd(graph, entry, random))
                    throw new InvalidOperationException("Guaranteed loot cannot fit " + entry.Code);
            // Draw without replacement so one rare result cannot consume every roll.
            // Oversized optional results are skipped and another entry is drawn. A cargo
            // pack can therefore share a standard cache without overflowing its 6x5 grid.
            var used = new bool[table.Pool.Length];
            // Optional content packs may not be registered in every build.
            for (int i = 0; i < used.Length; i++) used[i] = InventoryCatalog.Get(table.Pool[i].Code) == null;
            int rolls = random.Next(table.MinimumRolls, table.MaximumRolls + 1);
            int placed = 0;
            while (placed < rolls)
            {
                int total = 0;
                for (int i = 0; i < table.Pool.Length; i++) if (!used[i]) total += table.Pool[i].Weight;
                if (total == 0) break;
                int ticket = random.Next(total);
                for (int i = 0; i < table.Pool.Length; i++)
                {
                    if (used[i]) continue;
                    ticket -= table.Pool[i].Weight;
                    if (ticket >= 0) continue;
                    used[i] = true;
                    if (TryAdd(graph, table.Pool[i], random)) placed++;
                    break;
                }
            }
            if (graph.Validate() != InventoryError.None)
                throw new InvalidOperationException("Generated loot does not fit its cache.");
            graph.Version = 1;
            return graph;
        }

        private static bool TryAdd(InventoryGraph graph, Entry entry, Random random)
        {
            var definition = InventoryCatalog.Get(entry.Code);
            if (definition == null || definition.Kind == ItemKind.Root)
                throw new InvalidOperationException("Unknown loot item: " + entry.Code);
            int quantity = random.Next(entry.Minimum, entry.Maximum + 1);
            if (!definition.Container)
            {
                var result = graph.AddSupply(entry.Code, quantity, LootInventoryExchange.Root, true);
                if (result == InventoryError.Full) return false;
                if (result != InventoryError.None)
                    throw new InvalidOperationException("Invalid loot item " + entry.Code + ": " + result);
                return true;
            }
            var copy = graph.Clone();
            for (int i = 0; i < quantity; i++)
            {
                var item = new InventoryItem { Id = Guid.NewGuid().ToString("D"), Code = entry.Code, FoundInRaid = true };
                if (!copy.FindSpace(item, LootInventoryExchange.Root, out var region, out var x, out var y)) return false;
                item.Parent = LootInventoryExchange.Root; item.Region = region; item.X = x; item.Y = y;
                copy.Items.Add(item);
            }
            graph.Items = copy.Items;
            return true;
        }
    }

    // Per-cache state, owned by one server World. Death bags never receive this state.
    public sealed class RaidLootRefillState
    {
        public RaidLootProfile Profile { get; }
        public int RefillCount { get; private set; }
        private double m_EmptySince = -1;

        public RaidLootRefillState(RaidLootProfile profile) { Profile = profile; }

        public static bool HasContents(InventoryGraph cache) =>
            cache.Items.Exists(item => InventoryCatalog.Get(item.Code)?.Kind != ItemKind.Root);

        public bool TryRefill(InventoryGraph cache, double now, double cooldown, int refillLimit, bool blocked, Random random)
        {
            // Any remaining item, including a deposited bag and its descendants, protects the box.
            if (HasContents(cache)) { m_EmptySince = -1; return false; }
            if (m_EmptySince < 0) m_EmptySince = now;
            if (blocked || refillLimit <= RefillCount || now - m_EmptySince < Math.Max(1, cooldown) || cache.Version == int.MaxValue)
                return false;
            var replacement = RaidLootTable.Create(Profile, random);
            cache.Items = replacement.Items;
            cache.LootRows = replacement.LootRows;
            cache.Version++;
            RefillCount++;
            m_EmptySince = -1;
            return true;
        }
    }
}
