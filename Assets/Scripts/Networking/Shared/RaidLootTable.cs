// 生成箱子、抽屉和路线补给的物品组合。补货记录跟着箱子，房主传入时间和可补货条件，再由这里生成下一份内容。
using System;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public enum RaidLootProfile {
        Supplies,
        Medical,
        Industrial,
        Equipment
    }

    // 物品数量范围和相对权重在一起配置。只有战局服务器抽取。
    public static class RaidLootTable {
        private readonly struct Entry {
            public readonly string Code;
            public readonly int Weight, Minimum, Maximum;

            public Entry(string code, int weight, int minimum = 1, int maximum = 1) {
                Code = code;
                Weight = weight;
                Minimum = minimum;
                Maximum = maximum;
            }
        }

        private sealed class Table {
            public readonly Entry[] Guaranteed, Pool;
            public readonly int MinimumRolls, MaximumRolls;

            public Table(Entry[] guaranteed, Entry[] pool, int minimumRolls, int maximumRolls) {
                Guaranteed = guaranteed;
                Pool = pool;
                MinimumRolls = minimumRolls;
                MaximumRolls = maximumRolls;
            }
        }

        private static readonly Table[] Tables = {
            new(new[] { new Entry("ration", 1, 1, 2), new Entry("water", 1) }, new[] {
                new Entry("oxygen_canister", 20, 1, 2), new Entry("cells", 16, 1, 3),
                new Entry("energy_grenade", 9, 1, 2), new Entry("medkit", 10), new Entry("dust", 10, 2, 5),
                new Entry("energy_gel", 14, 1, 2), new Entry("protein_bar", 12, 1, 2),
                new Entry("hydration_gel", 12, 1, 2), new Entry("compressed_ration", 8),
                new Entry("emergency_oxygen", 10),
                new Entry("electrolyte", 12), new Entry("med_gel", 8, 1, 2),
                new Entry("oxygen_reserve", 6), new Entry("sling_pack", 5),
                new Entry("small_pack", 5), new Entry("alloy", 5, 1, 2)
            }, 2, 3),
            new(new[] { new Entry("medkit", 1, 2, 4) }, new[] {
                new Entry("med_gel", 30, 2, 3), new Entry("trauma_kit", 18),
                new Entry("hemostatic_bandage", 20, 1, 2), new Entry("rescue_kit", 10),
                new Entry("emergency_oxygen", 12),
                new Entry("vitality_shot", 5), new Entry("stamina_shot", 4), new Entry("antidote_shot", 4),
                new Entry("regeneration_shot", 4),
                new Entry("electrolyte", 16, 1, 2), new Entry("oxygen_reserve", 12),
                new Entry("water", 12, 1, 2), new Entry("oxygen_canister", 12, 1, 2),
                new Entry("ration", 8, 1, 2), new Entry("cells", 8, 1, 2),
                new Entry("small_pack", 4)
            }, 2, 3),
            new(new[] { new Entry("alloy", 1, 2, 5), new Entry("dust", 1, 3, 8) }, new[] {
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
            new(new[] { new Entry("cells", 1, 2, 4) }, new[] {
                new Entry("halo_capacitor", 12), new Entry("halo_charge_bridge", 12),
                new Entry("oxygen_recycler", 3), new Entry("glare_filter", 3), new Entry("survey_multitool", 2),
                new Entry("flash_grenade", 4), new Entry("poison_bomb", 3),
                new Entry("halo_focus_lens", 10), new Entry("halo_fan_array", 10),
                new Entry("halo_pulse_controller", 10), new Entry("halo_hit_marker", 8),
                new Entry("scout_rig", 12), new Entry("field_rig", 8),
                new Entry("sling_pack", 10), new Entry("cargo_pack", 4),
                new Entry("energy_grenade", 18, 1, 2), new Entry("dynamite_bundle", 2), new Entry("frag_bomb", 2),
                new Entry("fire_bomb", 2), new Entry("choke_bomb", 2), new Entry("smoke_grenade", 2),
                new Entry("alert_trip_mine", 2),
                new Entry("concertina_trip_mine", 2), new Entry("poison_trip_mine", 2), new Entry("ammo_box", 2),
                new Entry("helmet", 8), new Entry("rig", 6), new Entry("compact", 3),
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

        // 完整补给箱先从这个通用池抽一次，再执行各自主题的抽取。
        // 任务产物、异形解剖、个人保险箱和仅限研究取得的
        // 月核回收器仍由各自原有途径获得。
        private static readonly Entry[] SharedPool = {
            new("hemostatic_bandage", 10, 1, 2), new("rescue_kit", 5),
            new("hydration_gel", 10), new("compressed_ration", 7), new("emergency_oxygen", 10),
            new("oxygen_recycler", 2), new("glare_filter", 3), new("survey_multitool", 2),
            new("flash_grenade", 4), new("poison_bomb", 3),
            new("ration", 16, 1, 2), new("water", 16),
            new("energy_gel", 14, 1, 2), new("protein_bar", 14, 1, 2), new("electrolyte", 12),
            new("medkit", 14, 1, 2), new("med_gel", 14, 1, 2), new("trauma_kit", 6),
            new("oxygen_canister", 16, 1, 2), new("oxygen_reserve", 7), new("cells", 18, 1, 3),
            new("dust", 12, 2, 5), new("alloy", 10, 1, 2),
            new("circuit_board", 12, 1, 2), new("copper_coil", 10, 1, 2), new("ceramic_plate", 8, 1, 2),
            new("optical_sensor", 6), new("data_drive", 3), new("helium3_sample", 2),
            new("quantum_core", 1), new("black_box", 1),
            new("halo_capacitor", 3), new("halo_charge_bridge", 3),
            new("halo_focus_lens", 2), new("halo_fan_array", 2),
            new("halo_pulse_controller", 2), new("halo_hit_marker", 2),
            new("energy_grenade", 8, 1, 2), new("vitality_shot", 3),
            new("stamina_shot", 3), new("antidote_shot", 3), new("regeneration_shot", 3),
            new("dynamite_bundle", 2), new("frag_bomb", 3), new("fire_bomb", 2),
            new("smoke_grenade", 4), new("choke_bomb", 4), new("alert_trip_mine", 2),
            new("concertina_trip_mine", 2), new("poison_trip_mine", 2), new("ammo_box", 2),
            new("helmet", 4), new("energy_head_dome", 2), new("energy_shield_generator", 3),
            new("energy_sword", 2), new("dagger", 4),
            new("rifle", 3), new("compact", 3), new("pistol", 4),
            new("shotgun", 2), new("sniper", 1),
            new("cicada", 1), new("trine", 1), new("aegis", 1),
            new("vesper", 1), new("thorn", 1), new("nocturne", 1),
            new("tempest", 1), new("rondo", 1), new("sundown", 1),
            new("rig", 3), new("scout_rig", 4), new("field_rig", 2),
            new("small_pack", 4), new("sling_pack", 5), new("backpack", 2), new("cargo_pack", 1),
            new("survey_compass", 3), new("laser_rangefinder", 2), new("team_tracker", 2)
        };

        private static readonly Entry[][] DrawerPools = {
            new[] {
                new Entry("ration", 12), new Entry("water", 12), new Entry("protein_bar", 18),
                new Entry("hydration_gel", 12), new Entry("compressed_ration", 6), new Entry("emergency_oxygen", 8),
                new Entry("energy_gel", 14), new Entry("electrolyte", 5), new Entry("oxygen_canister", 6),
                new Entry("dust", 12, 1, 2), new Entry("cells", 8)
            },
            new[] {
                new Entry("med_gel", 25), new Entry("medkit", 12), new Entry("electrolyte", 18),
                new Entry("hemostatic_bandage", 18), new Entry("rescue_kit", 5), new Entry("emergency_oxygen", 8),
                new Entry("trauma_kit", 5), new Entry("vitality_shot", 3), new Entry("antidote_shot", 3),
                new Entry("regeneration_shot", 3),
                new Entry("oxygen_canister", 5), new Entry("water", 10)
            },
            new[] {
                new Entry("copper_coil", 22), new Entry("circuit_board", 18), new Entry("alloy", 18, 1, 2),
                new Entry("dust", 16, 1, 2), new Entry("ceramic_plate", 12), new Entry("optical_sensor", 8),
                new Entry("data_drive", 4), new Entry("halo_capacitor", 3), new Entry("halo_charge_bridge", 3),
                new Entry("helium3_sample", 2), new Entry("quantum_core", 1), new Entry("cells", 8)
            }
        };

        // 抽屉用比普通大箱更小的容器和抽取规则。按地图给它的补给类别找相应物品池。
        public static InventoryGraph CreateDrawer(RaidLootProfile profile, Random random) {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var graph = InventoryGraph.Create(false, false);
            graph.Items.Add(new InventoryItem { Id = LootInventoryExchange.Root, Code = LootInventoryExchange.Root });
            int index = profile == RaidLootProfile.Medical ? 1 : profile == RaidLootProfile.Supplies ? 0 : 2;
            var pool = DrawerPools[index];
            int rolls = random.Next(5) == 0 ? 0 : random.Next(1, 3);
            bool[] used = new bool[pool.Length];
            for (int roll = 0; roll < rolls; roll++) {
                int total = 0;
                for (int i = 0; i < pool.Length; i++)
                    if (!used[i] && InventoryCatalog.Get(pool[i].Code) != null)
                        total += pool[i].Weight;
                if (total == 0) break;
                int ticket = random.Next(total);
                for (int i = 0; i < pool.Length; i++) {
                    if (used[i] || InventoryCatalog.Get(pool[i].Code) == null) continue;
                    ticket -= pool[i].Weight;
                    if (ticket >= 0) continue;
                    used[i] = true;
                    if (!TryAdd(graph, pool[i], random))
                        throw new InvalidOperationException("Drawer item does not fit.");
                    break;
                }
            }
            if (graph.Validate() != InventoryError.None)
                throw new InvalidOperationException("Invalid drawer contents.");
            graph.Version = 1;
            return graph;
        }

        // 先放保底项目，再从该类箱子的加权池抽若干件。随机源从服务端地图状态传来。
        public static InventoryGraph Create(RaidLootProfile profile, Random random) {
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

        // 按物品权重选择当次抽取，成功放置受实际容器容量限制。试摆不下的物品不能堆在格子外。
        private static void AddRandom(InventoryGraph graph, Entry[] pool, int rolls, Random random) {
            // 不放回地依次抽取，防止一次稀有结果占完所有抽取机会。
            // 选填的大件放不下时换抽别的，大件货包也可以出现在普通补给池，
            // 实际上仍不能超出箱子的 6×5 格。
            bool[] used = new bool[pool.Length];
            // 跳过缺失的资源包，以及另一个池或保底项已经放入的物品码。
            for (int i = 0; i < used.Length; i++)
                used[i] = InventoryCatalog.Get(pool[i].Code) == null || graph.Count(pool[i].Code) > 0;
            int placed = 0;
            while (placed < rolls) {
                int total = 0;
                for (int i = 0; i < pool.Length; i++)
                    if (!used[i])
                        total += pool[i].Weight;
                if (total == 0) break;
                int ticket = random.Next(total);
                for (int i = 0; i < pool.Length; i++) {
                    if (used[i]) continue;
                    ticket -= pool[i].Weight;
                    if (ticket >= 0) continue;
                    used[i] = true;
                    if (TryAdd(graph, pool[i], random)) placed++;
                    break;
                }
            }
        }

        // 先决定数量和物品完整尺寸，再通过物品图的空间搜索落位。
        private static bool TryAdd(InventoryGraph graph, Entry entry, Random random) {
            var definition = InventoryCatalog.Get(entry.Code);
            if (definition == null || definition.Kind == ItemKind.Root)
                throw new InvalidOperationException("Unknown loot item: " + entry.Code);
            int quantity = random.Next(entry.Minimum, entry.Maximum + 1);
            if (!definition.Container) {
                var result = graph.AddSupply(entry.Code, quantity, LootInventoryExchange.Root, true);
                if (result == InventoryError.Full) return false;
                if (result != InventoryError.None)
                    throw new InvalidOperationException("Invalid loot item " + entry.Code + ": " + result);
                return true;
            }
            var copy = graph.Clone();
            for (int i = 0; i < quantity; i++) {
                var item = new InventoryItem
                    { Id = Guid.NewGuid().ToString("D"), Code = entry.Code, FoundInRaid = true };
                if (!copy.FindSpace(item, LootInventoryExchange.Root, out string region, out int x, out int y))
                    return false;
                item.Parent = LootInventoryExchange.Root;
                item.Region = region;
                item.X = x;
                item.Y = y;
                copy.Items.Add(item);
            }
            graph.Items = copy.Items;
            return true;
        }
    }

    // 每个补给箱的回复状态归当前服务端 World。尸体包没有这份状态。
    public sealed class RaidLootRefillState {
        public RaidLootProfile Profile { get; }
        public int RefillCount { get; private set; }
        private double m_EmptySince = -1;
        public RaidLootRefillState(RaidLootProfile profile) { Profile = profile; }

        public static bool HasContents(InventoryGraph cache) {
            return cache.Items.Exists(item => InventoryCatalog.Get(item.Code)?.Kind != ItemKind.Root);
        }

        // 只在箱已空、冷却结束、未超补货次数且没人阻挡时生成新内容，并提高补货代数，旧搜索不能继续沿用。
        public bool TryRefill(
            InventoryGraph cache,
            double now,
            double cooldown,
            int refillLimit,
            bool blocked,
            Random random) {
            // 只要箱里还有东西，连同玩家临时放进去的包与内部物品，都不会补货覆盖。
            if (HasContents(cache)) {
                m_EmptySince = -1;
                return false;
            }
            if (m_EmptySince < 0) m_EmptySince = now;
            if (blocked || refillLimit <= RefillCount || now - m_EmptySince < Math.Max(1, cooldown) ||
                cache.Version == int.MaxValue)
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