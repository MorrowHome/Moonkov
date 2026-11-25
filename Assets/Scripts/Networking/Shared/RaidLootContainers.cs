using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed class RaidLootContainers : IComponentData {
        public readonly Dictionary<int, InventoryGraph> Containers = new();
        public readonly Dictionary<int, RaidCorpseRecord> DeathBags = new();
        public readonly Dictionary<int, RaidLootRefillState> RefillStates = new();
        public readonly Dictionary<int, Mathematics.float3> MissionCaches = new();
        public readonly Dictionary<int, Mathematics.float3> FurnitureCaches = new();
        public readonly Random LootRandom = new(Guid.NewGuid().GetHashCode());
        public bool Initialized { get; private set; }
        public object MapIdentity { get; private set; }
        public double NextRefillCheck;
        public const int FirstDeathBagId = 64;
        public const int DroppedItemCharacterIndex = -2;
        public const double EmptyDropGraceSeconds = 10;
        public const float DropInterestRadius = 80;
        private readonly List<int> m_ExpiredDrops = new();
        public const int FirstMissionCacheId = 1000000000;
        private int m_NextDeathBagId = FirstDeathBagId;

        public int RefillGeneration(int id) {
            return RefillStates.TryGetValue(id, out var refill) ? refill.RefillCount : 0;
        }

        // 新异形遗体包从版本 1 开始。先通过解剖增加版本，
        // 之后才允许移动物品。人形遗体沿用原来的搜刮步骤。
        // 异形遗体的初始状态要求解剖。完成后箱子版本会变化，之后按普通已打开容器访问。
        public bool RequiresDissection(int id) {
            return DeathBags.TryGetValue(id, out var corpse) && corpse.CharacterIndex == -1 &&
                   Containers.TryGetValue(id, out var graph) && graph.Version == 1;
        }

        public int Drop(
            InventoryGraph carried,
            Mathematics.float3 position,
            Mathematics.quaternion rotation = default,
            int characterIndex = 2,
            double createdAt = 0) {
            var slots = carried.Children(InventoryCatalog.Equipment).ToDictionary(i => i.Region, i => i.Id);
            var graph = LootInventoryExchange.DropOnDeath(carried);
            int id = RegisterDrop(graph, position, rotation, characterIndex, createdAt);
            DeathBags[id].EquipmentSlots =
                slots.Where(p => graph.Find(p.Value) != null).ToDictionary(p => p.Key, p => p.Value);
            return id;
        }

        public int DropHarvest(
            InventoryGraph cache,
            Mathematics.float3 position,
            Mathematics.quaternion rotation = default,
            double createdAt = 0) {
            if (cache == null || cache.Validate() != InventoryError.None ||
                cache.Find(LootInventoryExchange.Root) == null || cache.Find("stash") != null ||
                cache.Items.Exists(i => cache.Carried(i) && InventoryCatalog.Get(i.Code).Kind != ItemKind.Root))
                throw new ArgumentException("Alien harvest must be a validated loot container.");
            var graph = cache.Clone();
            graph.Version = 1;
            return RegisterDrop(graph, position, rotation, -1, createdAt);
        }

        // 正式移走玩家物品前先由掉落工具生成剩余图和地面图。地图登记成功后才换玩家原库存。
        public InventoryError DropItem(
            InventoryGraph carried,
            string itemId,
            int expectedVersion,
            Mathematics.float3 position,
            double now,
            out int id) {
            id = -1;
            if (!Mathematics.math.all(Mathematics.math.isfinite(position)) || !double.IsFinite(now) || now < 0)
                return InventoryError.Invalid;
            var error = LootInventoryExchange.PrepareDrop(carried, itemId, expectedVersion, out var remaining,
                out var dropped);
            if (error != InventoryError.None) return error;
            // 所有可能失败的校验都在提交两边结果前完成。失败不会移除任何物品。
            try {
                id = RegisterDrop(dropped, position, Mathematics.quaternion.identity, DroppedItemCharacterIndex, now);
            } catch (ArgumentException) {
                id = -1;
                return InventoryError.Invalid;
            }
            carried.Items = remaining.Items;
            carried.Version = remaining.Version;
            return InventoryError.None;
        }

        private int RegisterDrop(
            InventoryGraph graph,
            Mathematics.float3 position,
            Mathematics.quaternion rotation,
            int characterIndex,
            double createdAt) {
            // 尸体与动态丢弃物用一段专用 ID。用完就拒绝登记，不能和任务箱或抽屉重复。
            if (m_NextDeathBagId >= FirstMissionCacheId) throw new ArgumentException("Ground loot ID space exhausted.");
            int id = m_NextDeathBagId++;
            Containers.Add(id, graph);
            try {
                DeathBags.Add(id, new RaidCorpseRecord {
                    Position = position,
                    Rotation = Mathematics.math.lengthsq(rotation.value) > 0
                        ? rotation
                        : Mathematics.quaternion.identity,
                    CharacterIndex = characterIndex, CreatedAt = createdAt
                });
            } catch {
                Containers.Remove(id);
                throw;
            }
            return id;
        }

        public bool IsEmptyDrop(int id) {
            return DeathBags.TryGetValue(id, out var bag) && bag.CharacterIndex == DroppedItemCharacterIndex &&
                   Containers.TryGetValue(id, out var graph) && !RaidLootRefillState.HasContents(graph);
        }

        // 只回收已空一段时间的丢弃物。玩家仍在看的位置得到保护，拿空后也留一会，避免界面同帧突然跳掉。
        public void RecycleEmptyDrops(double now, HashSet<int> protectedIds) {
            m_ExpiredDrops.Clear();
            foreach (var pair in DeathBags) {
                if (pair.Value.CharacterIndex != DroppedItemCharacterIndex) continue;
                if (!IsEmptyDrop(pair.Key) || protectedIds.Contains(pair.Key)) {
                    pair.Value.EmptySince = -1;
                    continue;
                }
                if (pair.Value.EmptySince < 0) pair.Value.EmptySince = now;
                if (now - pair.Value.EmptySince >= EmptyDropGraceSeconds) m_ExpiredDrops.Add(pair.Key);
            }
            foreach (int id in m_ExpiredDrops) {
                Containers.Remove(id);
                DeathBags.Remove(id);
            }
        }

        public void Initialize(int count, Func<int, RaidLootProfile> profileFor = null, object mapIdentity = null) {
            // 同张地图只初始化一次。换地图才重建物资和动态容器编号，旧场景的箱子不能在新图里被意外打开。
            if (Initialized && ReferenceEquals(MapIdentity, mapIdentity)) return;
            if (count < 1 || count > FirstDeathBagId) throw new ArgumentOutOfRangeException(nameof(count));
            int version = 1;
            if (Initialized) {
                // 上一张地图排队的转移请求不能意外匹配替换进来的地图。
                foreach (var cache in Containers.Values) version = Math.Max(version, checked(cache.Version + 1));
                Containers.Clear();
                DeathBags.Clear();
                RefillStates.Clear();
                MissionCaches.Clear();
                FurnitureCaches.Clear();
            }
            for (int index = 0; index < count; index++) {
                var profile = profileFor != null ? profileFor(index) : (RaidLootProfile)(index % 4);
                if (!Containers.ContainsKey(index)) {
                    var graph = RaidLootTable.Create(profile, LootRandom);
                    graph.Version = version;
                    Containers.Add(index, graph);
                }
                RefillStates.Add(index, new RaidLootRefillState(profile));
            }
            Initialized = true;
            MapIdentity = mapIdentity;
            NextRefillCheck = 0;
        }
    }
}
