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
                new Entry("energy_grenade", 9, 1, 2), new Entry("medkit", 10), new Entry("dust", 10, 2, 5),
                new Entry("energy_gel", 14, 1, 2), new Entry("protein_bar", 12, 1, 2),
                new Entry("hydration_gel", 12, 1, 2), new Entry("compressed_ration", 8), new Entry("emergency_oxygen", 10),
                new Entry("electrolyte", 12), new Entry("med_gel", 8, 1, 2),
                new Entry("oxygen_reserve", 6), new Entry("sling_pack", 5),
                new Entry("small_pack", 5), new Entry("alloy", 5, 1, 2)
            }, 2, 3),
            new Table(new[] { new Entry("medkit", 1, 2, 4) }, new[]
            {
                new Entry("med_gel", 30, 2, 3), new Entry("trauma_kit", 18),
                new Entry("hemostatic_bandage", 20, 1, 2), new Entry("rescue_kit", 10), new Entry("emergency_oxygen", 12),
                new Entry("vitality_shot", 5), new Entry("stamina_shot", 4), new Entry("antidote_shot", 4), new Entry("regeneration_shot", 4),
                new Entry("electrolyte", 16, 1, 2), new Entry("oxygen_reserve", 12),
                new Entry("water", 12, 1, 2), new Entry("oxygen_canister", 12, 1, 2),
                new Entry("ration", 8, 1, 2), new Entry("cells", 8, 1, 2),
                new Entry("small_pack", 4)
            }, 2, 3),
            new Table(new[] { new Entry("alloy", 1, 2, 5), new Entry("dust", 1, 3, 8) }, new[]
            {
                new Entry("cells", 18, 2, 5), new Entry("oxygen_canister", 10),
                new Entry("oxygen_recycler", 3), new Entry("glare_filter", 3), new Entry("survey_multitool", 2),
                new Entry("halo_capacitor", 8), new Entry("halo_charge_bridge", 8),
                new Entry("halo_focus_lens", 4), new Entry("halo_hit_marker", 4),
                new Entry("circuit_board", 22, 1, 2), new Entry("copper_coil", 20, 1, 2),
                new Entry("ceramic_plate", 16, 1, 2), new Entry("optical_sensor", 10),
                new Entry("data_drive", 6), new Entry("helium3_sample", 4),
                new Entry("quantum_core", 2), new Entry("black_box", 2),
                new Entry("survey_compass", 3), new Entry("laser_rangefinder", 2), new Entry("team_tracker", 2)
            }, 2, 3),
            new Table(new[] { new Entry("cells", 1, 2, 4) }, new[]
            {
                new Entry("halo_capacitor", 12), new Entry("halo_charge_bridge", 12),
                new Entry("oxygen_recycler", 3), new Entry("glare_filter", 3), new Entry("survey_multitool", 2),
                new Entry("flash_grenade", 4), new Entry("poison_bomb", 3),
                new Entry("halo_focus_lens", 10), new Entry("halo_fan_array", 10),
                new Entry("halo_pulse_controller", 10), new Entry("halo_hit_marker", 8),
                new Entry("scout_rig", 12), new Entry("field_rig", 8),
                new Entry("sling_pack", 10), new Entry("cargo_pack", 4),
                new Entry("energy_grenade", 18, 1, 2), new Entry("dynamite_bundle", 2), new Entry("frag_bomb", 2),
                new Entry("fire_bomb", 2), new Entry("choke_bomb", 2), new Entry("smoke_grenade", 2), new Entry("alert_trip_mine", 2),
                new Entry("concertina_trip_mine", 2), new Entry("poison_trip_mine", 2), new Entry("ammo_box", 2), new Entry("helmet", 8), new Entry("rig", 6), new Entry("compact", 3),
                new Entry("pistol", 3), new Entry("shotgun", 2), new Entry("sniper", 1),
                new Entry("optical_sensor", 8), new Entry("data_drive", 6),
                new Entry("black_box", 3), new Entry("quantum_core", 1),
                new Entry("rifle", 3), new Entry("cicada", 2), new Entry("trine", 2),
                new Entry("aegis", 1), new Entry("vesper", 1), new Entry("thorn", 1),
                new Entry("nocturne", 1), new Entry("tempest", 1), new Entry("rondo", 1), new Entry("sundown", 1),
                new Entry("energy_head_dome", 4), new Entry("energy_shield_generator", 4),
                new Entry("energy_sword", 3), new Entry("dagger", 5), new Entry("backpack", 3),
                new Entry("survey_compass", 3), new Entry("laser_rangefinder", 2), new Entry("team_tracker", 2)
            }, 2, 4)
        };

        // Every full cache gets one roll from this pool before its remaining themed rolls.
        // Keep mission outputs, alien harvests, personal secure cases and the research-only
        // mooncore recycler on their existing acquisition paths.
        private static readonly Entry[] SharedPool =
        {
            new Entry("hemostatic_bandage", 10, 1, 2), new Entry("rescue_kit", 5),
            new Entry("hydration_gel", 10), new Entry("compressed_ration", 7), new Entry("emergency_oxygen", 10),
            new Entry("oxygen_recycler", 2), new Entry("glare_filter", 3), new Entry("survey_multitool", 2),
            new Entry("flash_grenade", 4), new Entry("poison_bomb", 3),
            new Entry("ration", 16, 1, 2), new Entry("water", 16),
            new Entry("energy_gel", 14, 1, 2), new Entry("protein_bar", 14, 1, 2), new Entry("electrolyte", 12),
            new Entry("medkit", 14, 1, 2), new Entry("med_gel", 14, 1, 2), new Entry("trauma_kit", 6),
            new Entry("oxygen_canister", 16, 1, 2), new Entry("oxygen_reserve", 7), new Entry("cells", 18, 1, 3),
            new Entry("dust", 12, 2, 5), new Entry("alloy", 10, 1, 2),
            new Entry("circuit_board", 12, 1, 2), new Entry("copper_coil", 10, 1, 2), new Entry("ceramic_plate", 8, 1, 2),
            new Entry("optical_sensor", 6), new Entry("data_drive", 3), new Entry("helium3_sample", 2),
            new Entry("quantum_core", 1), new Entry("black_box", 1),
            new Entry("halo_capacitor", 3), new Entry("halo_charge_bridge", 3),
            new Entry("halo_focus_lens", 2), new Entry("halo_fan_array", 2),
            new Entry("halo_pulse_controller", 2), new Entry("halo_hit_marker", 2),
            new Entry("energy_grenade", 8, 1, 2), new Entry("vitality_shot", 3),
            new Entry("stamina_shot", 3), new Entry("antidote_shot", 3), new Entry("regeneration_shot", 3),
            new Entry("dynamite_bundle", 2), new Entry("frag_bomb", 3), new Entry("fire_bomb", 2),
            new Entry("smoke_grenade", 4), new Entry("choke_bomb", 4), new Entry("alert_trip_mine", 2),
            new Entry("concertina_trip_mine", 2), new Entry("poison_trip_mine", 2), new Entry("ammo_box", 2),
            new Entry("helmet", 4), new Entry("energy_head_dome", 2), new Entry("energy_shield_generator", 3),
            new Entry("energy_sword", 2), new Entry("dagger", 4),
            new Entry("rifle", 3), new Entry("compact", 3), new Entry("pistol", 4),
            new Entry("shotgun", 2), new Entry("sniper", 1),
            new Entry("cicada", 1), new Entry("trine", 1), new Entry("aegis", 1),
            new Entry("vesper", 1), new Entry("thorn", 1), new Entry("nocturne", 1),
            new Entry("tempest", 1), new Entry("rondo", 1), new Entry("sundown", 1),
            new Entry("rig", 3), new Entry("scout_rig", 4), new Entry("field_rig", 2),
            new Entry("small_pack", 4), new Entry("sling_pack", 5), new Entry("backpack", 2), new Entry("cargo_pack", 1),
            new Entry("survey_compass", 3), new Entry("laser_rangefinder", 2), new Entry("team_tracker", 2)
        };

        private static readonly Entry[][] DrawerPools =
        {
            new[] { new Entry("ration", 12), new Entry("water", 12), new Entry("protein_bar", 18),
                new Entry("hydration_gel", 12), new Entry("compressed_ration", 6), new Entry("emergency_oxygen", 8),
                new Entry("energy_gel", 14), new Entry("electrolyte", 5), new Entry("oxygen_canister", 6),
                new Entry("dust", 12, 1, 2), new Entry("cells", 8) },
            new[] { new Entry("med_gel", 25), new Entry("medkit", 12), new Entry("electrolyte", 18),
                new Entry("hemostatic_bandage", 18), new Entry("rescue_kit", 5), new Entry("emergency_oxygen", 8),
                new Entry("trauma_kit", 5), new Entry("vitality_shot", 3), new Entry("antidote_shot", 3), new Entry("regeneration_shot", 3),
                new Entry("oxygen_canister", 5), new Entry("water", 10) },
            new[] { new Entry("copper_coil", 22), new Entry("circuit_board", 18), new Entry("alloy", 18, 1, 2),
                new Entry("dust", 16, 1, 2), new Entry("ceramic_plate", 12), new Entry("optical_sensor", 8),
                new Entry("data_drive", 4), new Entry("halo_capacitor", 3), new Entry("halo_charge_bridge", 3),
                new Entry("helium3_sample", 2), new Entry("quantum_core", 1), new Entry("cells", 8) }
        };

        public static InventoryGraph CreateDrawer(RaidLootProfile profile, Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var graph = InventoryGraph.Create(false, false);
            graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
            int index = profile == RaidLootProfile.Medical ? 1 : profile == RaidLootProfile.Supplies ? 0 : 2;
            var pool = DrawerPools[index];
            int rolls = random.Next(5) == 0 ? 0 : random.Next(1, 3);
            var used = new bool[pool.Length];
            for (int roll = 0; roll < rolls; roll++)
            {
                int total = 0;
                for (int i = 0; i < pool.Length; i++)
                    if (!used[i] && InventoryCatalog.Get(pool[i].Code) != null) total += pool[i].Weight;
                if (total == 0) break;
                int ticket = random.Next(total);
                for (int i = 0; i < pool.Length; i++)
                {
                    if (used[i] || InventoryCatalog.Get(pool[i].Code) == null) continue;
                    ticket -= pool[i].Weight;
                    if (ticket >= 0) continue;
                    used[i] = true;
                    if (!TryAdd(graph, pool[i], random)) throw new InvalidOperationException("Drawer item does not fit.");
                    break;
                }
            }
            if (graph.Validate() != InventoryError.None) throw new InvalidOperationException("Invalid drawer contents.");
            graph.Version = 1;
            return graph;
        }

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
            int rolls = random.Next(table.MinimumRolls, table.MaximumRolls + 1);
            AddRandom(graph, SharedPool, 1, random);
            AddRandom(graph, table.Pool, rolls - 1, random);
            if (graph.Validate() != InventoryError.None)
                throw new InvalidOperationException("Generated loot does not fit its cache.");
            graph.Version = 1;
            return graph;
        }

        private static void AddRandom(InventoryGraph graph, Entry[] pool, int rolls, Random random)
        {
            // Draw without replacement so one rare result cannot consume every roll.
            // Oversized optional results are skipped and another entry is drawn. A cargo
            // pack can therefore share a standard cache without overflowing its 6x5 grid.
            var used = new bool[pool.Length];
            // Skip missing content packs and codes already placed by another pool or guarantee.
            for (int i = 0; i < used.Length; i++)
                used[i] = InventoryCatalog.Get(pool[i].Code) == null || graph.Count(pool[i].Code) > 0;
            int placed = 0;
            while (placed < rolls)
            {
                int total = 0;
                for (int i = 0; i < pool.Length; i++) if (!used[i]) total += pool[i].Weight;
                if (total == 0) break;
                int ticket = random.Next(total);
                for (int i = 0; i < pool.Length; i++)
                {
                    if (used[i]) continue;
                    ticket -= pool[i].Weight;
                    if (ticket >= 0) continue;
                    used[i] = true;
                    if (TryAdd(graph, pool[i], random)) placed++;
                    break;
                }
            }
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
