// 局内库存快照、网络消息和每个玩家的操作状态都在这里。服务端记录搜刮、治疗和所打开的容器，客户端负责把库存分片拼回完整数据。
using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public static class RaidInventoryTransport {
        // 每段 ASCII JSON 要明显小于传输层 1378 字节的包上限。
        public const int ChunkCharacters = 400;
        public const int MaxChunks = 2048;
        public const int MaxJsonCharacters = 800000;

        // 纯库存编码给档案。如果还要表示搜刮箱、动作与结算页信息，调用 EncodeSnapshot 将它们放同一个包。
        public static string Encode(InventoryGraph graph) {
            return Newtonsoft.Json.JsonConvert.SerializeObject(graph,
                new Newtonsoft.Json.JsonSerializerSettings
                    { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii });
        }

        public static InventoryGraph Decode(string json) {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<InventoryGraph>(json);
        }

        public static string EncodeSnapshot(
            InventoryGraph graph,
            int lootId,
            int lootVersion,
            int dissectingLootId = -1,
            float dissectionRemaining = 0,
            string medicalItemId = null,
            float medicalRemaining = 0,
            string actionFeedback = null,
            int corpseCharacterIndex = -1,
            Dictionary<string, string> corpseEquipmentSlots = null,
            RaidResultManifest result = null,
            bool medicalIsConsumable = false,
            int searchTotal = -1,
            List<RaidSearchFootprint> searchFootprints = null,
            float searchItemSeconds = 0) {
            return Newtonsoft.Json.JsonConvert.SerializeObject(new RaidInventorySnapshot {
                    Graph = graph, LootId = lootId, LootVersion = lootVersion,
                    DissectingLootId = dissectingLootId, DissectionRemaining = dissectionRemaining,
                    MedicalItemId = medicalItemId, MedicalRemaining = medicalRemaining,
                    MedicalIsConsumable = medicalIsConsumable, ActionFeedback = actionFeedback,
                    CorpseCharacterIndex = corpseCharacterIndex, CorpseEquipmentSlots = corpseEquipmentSlots,
                    Result = result, SearchTotal = searchTotal, SearchFootprints = searchFootprints,
                    SearchItemSeconds = searchItemSeconds
                },
                new Newtonsoft.Json.JsonSerializerSettings
                    { StringEscapeHandling = Newtonsoft.Json.StringEscapeHandling.EscapeNonAscii });
        }
    }

    public sealed class RaidSearchFootprint {
        public string Id, Region;
        public int X, Y, Width, Height;
    }

    public sealed class RaidInventorySnapshot {
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int LootId = -1, LootVersion;
        public int CorpseCharacterIndex = -1;
        public Dictionary<string, string> CorpseEquipmentSlots;
        public int DissectingLootId = -1;
        public float DissectionRemaining;
        public int SearchTotal = -1;
        public List<RaidSearchFootprint> SearchFootprints;
        public float SearchItemSeconds;
        public string MedicalItemId;
        public float MedicalRemaining;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
    }

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

    public sealed class RaidInventoryState : IComponentData {
        public const float ItemActionMovementLimit = .75f;
        public RaidSurvivalMotion SurvivalMotion;
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int RaidId;
        public int MissionFirstsBefore, MissionFirstsSaved;
        public int LastSentVersion = -1;
        private string m_SentHeadDomeItem;
        private int m_SentOxygenPercent = -2;

        // 物品详情只显示整数百分比。实时 HUD 已读到
        // Ghost 上的精确氧气，所以不到一个百分点的变化无需再发完整库存 RPC。
        // HUD 直接读角色同步来的精确氧气。背包物品详情只显示整数百分比，所以半秒检查到显示值或装备 ID 变化才安排库存重发。
        public bool ProtectionDisplayChanged {
            get {
                var dome = Graph?.Equipped("Helmet");
                return EnergyShieldEquipment.IsHeadDome(dome?.Code) && (dome.Id != m_SentHeadDomeItem ||
                                                                        UnityEngine.Mathf.RoundToInt(dome.SuitOxygen) !=
                                                                        m_SentOxygenPercent);
            }
        }

        public void RecordProtectionSnapshot() {
            var dome = Graph?.Equipped("Helmet");
            m_SentHeadDomeItem = dome?.Id;
            m_SentOxygenPercent = dome == null ? -2 : UnityEngine.Mathf.RoundToInt(dome.SuitOxygen);
        }

        public uint Sequence, RequestId;
        public InventoryError Error;
        public int OpenedLootId = -1, LastSentLootVersion = -1;
        public int DissectingLootId = -1;
        public double NextLootNoiseAt;
        public readonly HashSet<int> SearchedLoot = new();
        private readonly Dictionary<int, int> m_SearchedGenerations = new();
        public string MedicalItemId;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
        public int MedicalVersion;
        public BodyPart MedicalPart;
        public double MedicalUntil;
        public Mathematics.float3 MedicalOrigin;

        public uint MedicalDamageSequence,
            MedicalShieldHitSequence,
            MedicalShotTick,
            MedicalGrenadeTick,
            MedicalReloadTick,
            MedicalWeapon;

        // 记过已搜不表示以后总能跳过。异形仍需解剖或同一箱经过一轮补货时，要按新内容重新读条。
        public bool NeedsSearch(RaidLootContainers caches, int id) {
            return !SearchedLoot.Contains(id) || caches.RequiresDissection(id) ||
                   caches.RefillStates.TryGetValue(id, out var refill) &&
                   (!m_SearchedGenerations.TryGetValue(id, out int generation) || generation != refill.RefillCount);
        }

        public void ClearSearchRecords() {
            SearchedLoot.Clear();
            m_SearchedGenerations.Clear();
        }

        public uint DeathBagSequence;
        public double NextDeathBagResync;
        public readonly List<int> HiddenDeathBags = new();
        public readonly List<int> RemovedSearches = new();
        public bool TacticalPending;

        // 该状态由手雷系统管理。通用取消请求不能抹掉已经开始的引信。
        public bool GrenadePending;

        public bool ActionPending =>
            GrenadePending || TacticalPending || DissectingLootId >= 0 || MedicalItemId != null;

        public bool InventoryMoveBlocked => GrenadePending || TacticalPending || MedicalItemId != null;

        // 关闭容器视图时，也收掉其搜索状态并让服务器发新结果。单纯离开 UI 不能让角色继续持有箱子。
        public void CloseLoot() {
            // 关容器窗口只停止这次搜索，不影响无关的自用动作。
            if (DissectingLootId >= 0) {
                DissectingLootId = -1;
                ActionFeedback = "已取消搜索。";
            }
            OpenedLootId = -1;
            LastSentVersion = LastSentLootVersion = -1;
        }

        // 清当前所有互斥的手部动作以及相应计时，反馈原因留在下一份快照给界面。
        public void CancelActions(string feedback = null) {
            TacticalPending = false;
            DissectingLootId = -1;
            MedicalItemId = null;
            MedicalIsConsumable = false;
            ActionFeedback = feedback;
            LastSentVersion = -1;
        }

        // 开始前尝试能否从副本使用对应医疗物品，避免对不支持的部位读完才报错。这一步先不扣原背包。
        public InventoryError BeginMedical(
            in PredictedPlayerGhost health,
            string id,
            int version,
            BodyPart part,
            Mathematics.float3 origin,
            double now,
            float seconds = 3) {
            if (RaidHealth.HandsBusy(health)) return InventoryError.Inaccessible;
            // 在副本上验证。读条结束前不扣物品，也不实际治疗。
            var probe = health;
            var error = RaidHealth.UseMedical(ref probe, Graph.Clone(), id, version, part);
            if (error != InventoryError.None) return error;
            CancelActions(ActionPending ? "原操作已取消：开始治疗；未获取物品或消耗医疗物品。" : null);
            MedicalPart = part == BodyPart.Auto ? RaidHealth.MostInjured(health) : part;
            StartItemAction(health, id, version, origin, now,
                ConsumableCatalog.MedicalSeconds(Graph.Find(id).Code, seconds), false);
            return InventoryError.None;
        }

        // 食物、饮水和便携氧气首先检查可达、资源有需求和双手空闲。之后复用治疗读条的记录机制。
        public InventoryError BeginConsumable(
            in PredictedPlayerGhost health,
            string id,
            int version,
            Mathematics.float3 origin,
            double now) {
            if (Graph == null) return InventoryError.Invalid;
            if (ActionPending || RaidHealth.HandsBusy(health)) return InventoryError.Inaccessible;
            // 先做校验，读条结束前不增加资源或消耗这一堆。
            bool oxygen = ConsumableCatalog.Get(Graph.Find(id)?.Code)?.Oxygen > 0;
            var probe = health;
            var copy = Graph.Clone();
            var error = oxygen
                ? RaidOxygenConsumables.Consume(ref probe, copy, id, version)
                : RaidNutrition.Consume(ref probe, copy, id, version);
            if (error != InventoryError.None) return error;
            CancelActions();
            StartItemAction(health, id, version, origin, now, ConsumableCatalog.UseSeconds(Graph.Find(id).Code), true);
            return InventoryError.None;
        }

        // 记录物品、背包版本与开始时的角色状态。后续尝试完成时要据此判断是不是同一件，是否被攻击或换武器打断。
        private void StartItemAction(
            in PredictedPlayerGhost health,
            string id,
            int version,
            Mathematics.float3 origin,
            double now,
            float seconds,
            bool consumable) {
            MedicalItemId = id;
            MedicalVersion = version;
            MedicalIsConsumable = consumable;
            MedicalOrigin = origin;
            MedicalUntil = now + Math.Max(.1f, seconds);
            MedicalDamageSequence = health.NonPhysiologicalDamageSequence;
            MedicalShieldHitSequence = health.ShieldHitSequence;
            MedicalShotTick = health.LastShotTick;
            MedicalGrenadeTick = health.LastGrenadeShotTick;
            MedicalReloadTick = health.LastReloadTick;
            MedicalWeapon = health.EquippedWeaponID;
            LastSentVersion = -1;
        }

        public void RefreshMedical(
            ref PredictedPlayerGhost health,
            bool accessible,
            Mathematics.float3 position,
            double now) {
            if (MedicalItemId == null) return;
            string reason = !accessible
                ? "治疗中断：玩家死亡、断线或战局已结束。"
                :
                health.NonPhysiologicalDamageSequence != MedicalDamageSequence ||
                health.ShieldHitSequence != MedicalShieldHitSequence
                    ? "治疗中断：受到攻击。"
                    :
                    health.LastShotTick != MedicalShotTick || health.LastGrenadeShotTick != MedicalGrenadeTick
                        ?
                        "治疗中断：开火或投掷。"
                        : health.LastReloadTick != MedicalReloadTick || health.EquippedWeaponID != MedicalWeapon ||
                          RaidHealth.HandsBusy(health)
                            ? "治疗中断：换弹或切换武器。"
                            : null;
            // 动作期间如果背包已被其他操作修改，就拒绝仍在等的旧消耗。没到结束时间时只更新剩余读条。
            if (Graph.Version != MedicalVersion) reason = "治疗中断：库存已变化，请重新选择物品。";
            if (reason == null && now < MedicalUntil) return;
            string id = MedicalItemId;
            bool consumable = MedicalIsConsumable;
            MedicalItemId = null;
            MedicalIsConsumable = false;
            LastSentVersion = -1;
            bool oxygen = consumable && ConsumableCatalog.Get(Graph.Find(id)?.Code)?.Oxygen > 0;
            Error = reason == null
                ?
                consumable
                    ? oxygen
                        ? RaidOxygenConsumables.Consume(ref health, Graph, id, MedicalVersion)
                        : RaidNutrition.Consume(ref health, Graph, id, MedicalVersion)
                    : RaidHealth.UseMedical(ref health, Graph, id, MedicalVersion, MedicalPart)
                : Graph.Version != MedicalVersion
                    ? InventoryError.Stale
                    : InventoryError.Inaccessible;
            // 完成后根据同一个读条种类走补氧、进食或医疗。成功才写对应人物字段，结束便清计时和动作广播。
            if (consumable)
                ActionFeedback = reason != null ? reason.Replace("治疗中断", "使用中断") + " 未消耗物品。" :
                    Error == InventoryError.None ? "补给使用完成。" : "使用失败：状态或物品已变化，未消耗物品。";
            else
                ActionFeedback = reason != null ? reason + " 未消耗医疗物品。" :
                    Error == InventoryError.None ? "治疗完成。" : "治疗失败：目标或物品状态已变化，未消耗物品。";
        }

        public double DissectionUntil, DissectionStartedAt;
        public int SearchRevealed, SearchItemCount;
        private readonly List<string> m_SearchOrder = new();
        private readonly HashSet<string> m_SearchKnown = new();
        private bool m_SearchOrderInitialized;

        // 开始搜索时记下容器中各物品的揭开顺序。有人拿走一件后不把后面的物品提前揭开，也允许已揭开的物品立即被拿。
        private void EnsureSearchOrder(InventoryGraph cache) {
            if (!m_SearchOrderInitialized) {
                m_SearchOrder.AddRange(cache.Children(LootInventoryExchange.Root).Select(i => i.Id));
                m_SearchOrderInitialized = true;
            }
            for (int n = 0; n < Math.Min(SearchRevealed, m_SearchOrder.Count); n++) m_SearchKnown.Add(m_SearchOrder[n]);
        }

        public InventoryGraph SearchPreview(InventoryGraph cache) {
            EnsureSearchOrder(cache);
            return LootInventoryExchange.SearchPreview(cache, m_SearchKnown);
        }

        // 正在搜箱时只允许操作已经揭开的物品。灰色占格只是让玩家知道还有内容，拿到那格的 ID 也不能跳过搜索。
        public InventoryError TryLootMove(
            RaidLootContainers caches,
            int id,
            InventoryCommand command,
            int expectedCacheVersion) {
            bool searching = DissectingLootId >= 0 && DissectingLootId == id;
            if (InventoryMoveBlocked || (!searching && OpenedLootId != id) ||
                (!searching && caches.RequiresDissection(id)) || !caches.Containers.TryGetValue(id, out var cache))
                return InventoryError.Inaccessible;
            HashSet<string> hidden = null;
            // 搜索阶段只能移动已知露出的物品。服务器先用可见的临时视图检查请求引用，再在完整箱子里执行真实事务。
            if (searching) {
                var visible = SearchPreview(cache);
                var joined = LootInventoryExchange.Snapshot(Graph, visible);
                // 逐一验证请求引用的身份。只知道灰色占格的 ID 还不能取这件东西。
                if (joined.Find(command.ItemId) == null ||
                    (!string.IsNullOrEmpty(command.Parent) && joined.Find(command.Parent) == null) ||
                    (!string.IsNullOrEmpty(command.TargetId) && joined.Find(command.TargetId) == null))
                    return InventoryError.Inaccessible;
                hidden = new HashSet<string>(cache.Children(LootInventoryExchange.Root)
                    .Where(i => visible.Find(i.Id) == null).Select(i => i.Id));
            }
            var error = LootInventoryExchange.TryApply(Graph, cache, command, expectedCacheVersion);
            // 事务成功后记住目前仍在箱里、又不属于隐藏项的物品。玩家把已知物先拿再放回去仍能看见。
            if (error == InventoryError.None && searching)
                foreach (var item in cache.Children(LootInventoryExchange.Root))
                    if (!hidden.Contains(item.Id))
                        m_SearchKnown.Add(item.Id);
            return error;
        }

        public float SearchSecondsPerItem;

        public float SearchItemRemaining(double now) {
            return (float)Math.Max(0,
                Math.Min(DissectionUntil, DissectionStartedAt + (SearchRevealed + 1) * SearchSecondsPerItem) - now);
        }

        public Mathematics.float3 DissectionOrigin;
        public int DissectionGeneration;

        public uint DissectionDamageSequence,
            DissectionShieldHitSequence,
            DissectionShotTick,
            DissectionGrenadeTick,
            DissectionReloadTick,
            DissectionWeapon;

        // 普通箱子搜索和异形解剖共用此计时。记录开始时的伤害、射击和换弹标记，后续能判断动作为什么中断。
        public bool BeginDissection(
            int id,
            double now,
            Mathematics.float3 origin,
            in PredictedPlayerGhost health,
            float seconds = AlienHarvest.DissectionSeconds,
            int refillGeneration = 0,
            int searchItems = 1,
            InventoryGraph searchCache = null) {
            if (RaidHealth.SearchHandsBusy(health)) return false;
            CancelActions(ActionPending ? "原操作已取消：开始搜索；未消耗物品。" : ActionFeedback);
            DissectingLootId = id;
            SearchSecondsPerItem = Math.Max(.1f, seconds);
            SearchItemCount = Math.Max(1, searchItems);
            DissectionUntil = now + SearchSecondsPerItem * SearchItemCount;
            DissectionStartedAt = now;
            SearchRevealed = 0;
            m_SearchOrder.Clear();
            m_SearchKnown.Clear();
            m_SearchOrderInitialized = false;
            if (searchCache != null) EnsureSearchOrder(searchCache);
            DissectionOrigin = origin;
            DissectionGeneration = refillGeneration;
            DissectionDamageSequence = health.NonPhysiologicalDamageSequence;
            DissectionShieldHitSequence = health.ShieldHitSequence;
            DissectionShotTick = health.LastShotTick;
            DissectionGrenadeTick = health.LastGrenadeShotTick;
            DissectionReloadTick = health.LastReloadTick;
            DissectionWeapon = health.EquippedWeaponID;
            OpenedLootId = -1;
            LastSentVersion = -1;
            return true;
        }

        // 每帧检查是否仍有权限搜这个箱、容器没变和角色没被打断。读条过程中按时间逐件揭开。
        public void RefreshDissection(
            RaidLootContainers caches,
            bool accessible,
            Mathematics.float3 position,
            in PredictedPlayerGhost health,
            double now) {
            if (DissectingLootId < 0) return;
            bool contentsChanged = caches.RefillStates.TryGetValue(DissectingLootId, out var refill) &&
                                   refill.RefillCount != DissectionGeneration;
            string reason = !accessible ? "搜索中断：离开范围、死亡或战局结束。" :
                contentsChanged ? "搜索中断：容器内容已刷新。" :
                health.NonPhysiologicalDamageSequence != DissectionDamageSequence ||
                health.ShieldHitSequence != DissectionShieldHitSequence ? "搜索中断：受到攻击。" :
                (health.LastShotTick != DissectionShotTick && health.LastShotTick != health.LastSentryShotTick) ||
                health.LastGrenadeShotTick != DissectionGrenadeTick ? "搜索中断：开火或投掷。" :
                health.LastReloadTick != DissectionReloadTick || health.EquippedWeaponID != DissectionWeapon ||
                RaidHealth.SearchHandsBusy(health) ? "搜索中断：换弹或切换武器。" : null;
            if (reason == null && now < DissectionUntil) {
                caches.Containers.TryGetValue(DissectingLootId, out var cache);
                int revealed = Math.Min(SearchItemCount,
                    (int)Math.Max(0, (now - DissectionStartedAt) / SearchSecondsPerItem));
                if (revealed != SearchRevealed) {
                    SearchRevealed = revealed;
                    LastSentVersion = -1;
                }
                if (cache != null) EnsureSearchOrder(cache);
                return;
            }
            int id = DissectingLootId;
            DissectingLootId = -1;
            LastSentVersion = -1;
            Error = reason == null ? InventoryError.None : InventoryError.Inaccessible;
            ActionFeedback = reason == null ? "搜索完成。" : reason;
            if (reason == null) {
                // 解剖成功后箱子版本不同了，下一次访问不会再把同一遗体当成首次解剖。
                if (caches.RequiresDissection(id)) caches.Containers[id].Version++;
                SearchedLoot.Add(id);
                if (caches.RefillStates.TryGetValue(id, out refill)) m_SearchedGenerations[id] = refill.RefillCount;
                OpenedLootId = id;
            }
        }

        public readonly Dictionary<int, int> SentDeathBags = new();

        public static void UpdateTotals(InventoryGraph graph, ref RaidSession session) {
            session.Dust = graph.Count("dust", true);
            session.Alloy = graph.Count("alloy", true);
            session.Cells = graph.Count("cells", true);
            session.SnapshotTimer = 0;
        }
    }

    public struct RaidInventoryMoveRpc : IRpcCommand {
        public int RaidId, ExpectedVersion, X, Y, Quantity;
        public uint RequestId;
        public InventoryOperation Operation;
        public FixedString64Bytes ItemId, Parent, Region, TargetId;
        public bool Rotated;
    }

    public struct RaidInventoryDropRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
    }

    public struct RaidActionCancelRpc : IRpcCommand {
        public int RaidId;
        public uint RequestId;
    }

    public struct RaidMedicalUseRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        public BodyPart Part;
    }

    public struct RaidConsumableUseRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
    }

    public struct RaidLootOpenRpc : IRpcCommand {
        public int RaidId, LootId;
        public uint RequestId;
    }

    public sealed class RaidCorpseRecord {
        public Mathematics.float3 Position;
        public Mathematics.quaternion Rotation;
        public int CharacterIndex;
        public Dictionary<string, string> EquipmentSlots;
        public double CreatedAt;
        public double EmptySince = -1;
    }

    // 必须与较早的遗体标记 RPC 保持不同类型。增加尸体表现数据后，
    // 生成的序列化和 Burst 代码不能继续用原来较小的布局。
    public struct RaidCorpseRpc : IRpcCommand {
        public int LootId, Version, CharacterIndex;
        public Mathematics.float3 Position;
        public Mathematics.quaternion Rotation;
        public float Age;
        public bool Empty;
        public bool RequiresDissection => CharacterIndex == -1 && Version == 1;
    }

    // 显示状态与元数据一起更新，用新的序列化类型标识。
    public struct RaidGroundItemMarkerRpc : IRpcCommand {
        public FixedString64Bytes ItemCode;
        public uint Sequence;
        public int LootId, Version, CharacterIndex;
        public Mathematics.float3 Position;
        public Mathematics.quaternion Rotation;
        public float Age;
        public bool Empty, Removed, Deleted;
    }

    public sealed class RaidDeathBagClientState : IComponentData {
        public readonly Dictionary<int, RaidCorpseRpc> Bags = new();
        public readonly Dictionary<int, string> ItemCodes = new();
        private readonly Dictionary<int, uint> m_Sequences = new();

        // 每个物品 ID 的序号只保留最近一段。早于这段窗口的更新
        // 统一拒绝，已经移走的遗体包也一样。
        private const uint SequenceWindow = 4096;
        private uint m_HighestSequence;
        private readonly List<int> m_ExpiredSequences = new();

        // 地面标记可能晚到，因此以每个 ID 上次接受的序号判断。删除与隐藏分开，缓存的旧包不能重建已移除物体。
        public void Receive(RaidGroundItemMarkerRpc update) {
            uint floor = m_HighestSequence > SequenceWindow ? m_HighestSequence - SequenceWindow : 0;
            if (update.Sequence <= floor) return;
            if (update.LootId < RaidLootContainers.FirstDeathBagId || update.Sequence == 0 ||
                (m_Sequences.TryGetValue(update.LootId, out uint sequence) && update.Sequence <= sequence))
                return;
            m_HighestSequence = Math.Max(m_HighestSequence, update.Sequence);
            m_Sequences[update.LootId] = update.Sequence;
            // 只保留有限的近期序号。更早的归入一个总下限，释放字典空间也还能拒绝过时消息。
            if (m_Sequences.Count > SequenceWindow) {
                floor = m_HighestSequence - SequenceWindow;
                m_ExpiredSequences.Clear();
                foreach (var pair in m_Sequences)
                    if (pair.Value <= floor)
                        m_ExpiredSequences.Add(pair.Key);
                foreach (int id in m_ExpiredSequences) m_Sequences.Remove(id);
            }
            if (update.Removed) {
                Bags.Remove(update.LootId);
                ItemCodes.Remove(update.LootId);
                return;
            }
            Receive(new RaidCorpseRpc {
                LootId = update.LootId, Version = update.Version, CharacterIndex = update.CharacterIndex,
                Position = update.Position, Rotation = update.Rotation, Age = update.Age, Empty = update.Empty
            });
            if (Bags.TryGetValue(update.LootId, out var accepted) && accepted.Version == update.Version)
                ItemCodes[update.LootId] = update.ItemCode.ToString();
        }

        public void Receive(RaidCorpseRpc bag) {
            if (bag.LootId < RaidLootContainers.FirstDeathBagId || bag.Version < 1 ||
                bag.CharacterIndex < RaidLootContainers.DroppedItemCharacterIndex ||
                (bag.CharacterIndex > 1 && !GameSettings.IsPlayableCharacter(bag.CharacterIndex)) ||
                !Mathematics.math.all(Mathematics.math.isfinite(bag.Position)) ||
                !Mathematics.math.all(Mathematics.math.isfinite(bag.Rotation.value)) ||
                Mathematics.math.abs(Mathematics.math.lengthsq(bag.Rotation.value) - 1) > .01f ||
                !float.IsFinite(bag.Age) || bag.Age < 0)
                return;
            if (!Bags.TryGetValue(bag.LootId, out var old) || bag.Version > old.Version) Bags[bag.LootId] = bag;
        }
    }

    public struct RaidLootMoveRpc : IRpcCommand {
        public int RaidId, LootId, ExpectedLootVersion, ExpectedVersion, X, Y, Quantity;
        public uint RequestId;
        public InventoryOperation Operation;
        public FixedString64Bytes ItemId, Parent, Region, TargetId;
        public bool Rotated;
    }

    // V2 使用 512 字节字符串，类型名称与旧 4096 字节 RPC 分开，
    // 生成的 RPC 接收器和 Burst 不能重用旧布局。
    public struct RaidInventoryChunkV2Rpc : IRpcCommand {
        public int RaidId, Index, Count;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public FixedString512Bytes Json;
    }

    // 未到齐的数据不交给 UI，更高序号一到便放弃旧分片。
    public sealed class RaidInventoryClientState : IComponentData {
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int RaidId;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public int LootId = -1, LootVersion;
        public int CorpseCharacterIndex = -1;
        public Dictionary<string, string> CorpseEquipmentSlots;
        public int DissectingLootId = -1;
        public float DissectionRemaining;
        public int SearchTotal = -1;
        public List<RaidSearchFootprint> SearchFootprints;
        public float SearchItemSeconds;
        public string MedicalItemId;
        public float MedicalRemaining;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
        private uint m_IncomingSequence;
        private int m_IncomingRaid, m_Received;
        private string[] m_Chunks;

        // 同一个 RaidId 和快照序号的分片收齐才解码，更新背包、搜刮进度和请求结果。迟到的旧分片不能盖过新背包。
        public bool Receive(RaidInventoryChunkV2Rpc chunk, Func<string, InventoryGraph> decode) {
            if (chunk.Count < 1 || chunk.Count > RaidInventoryTransport.MaxChunks || chunk.Index < 0 ||
                chunk.Index >= chunk.Count || chunk.Sequence <= Sequence)
                return false;
            // 更新一批分片开始到达时，放弃上一批未齐的内容。之后每块必须与这一批的局号、序号和总块数都匹配。
            if (m_Chunks == null || chunk.Sequence > m_IncomingSequence) {
                m_IncomingSequence = chunk.Sequence;
                m_IncomingRaid = chunk.RaidId;
                m_Chunks = new string[chunk.Count];
                m_Received = 0;
            }
            if (chunk.Sequence != m_IncomingSequence || chunk.RaidId != m_IncomingRaid ||
                chunk.Count != m_Chunks.Length)
                return false;
            // 同一个索引重复送只算一次。必须所有不同索引都齐全才拼整份 JSON。
            if (m_Chunks[chunk.Index] == null) {
                m_Chunks[chunk.Index] = chunk.Json.ToString();
                m_Received++;
            }
            if (m_Received != m_Chunks.Length) return false;
            string json = string.Concat(m_Chunks);
            m_Chunks = null;
            if (json.Length > RaidInventoryTransport.MaxJsonCharacters) return false;
            try {
                var snapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<RaidInventorySnapshot>(json);
                // 既支持当前带读条元数据的快照，也能读旧格式纯物品图。版本和箱子根关系必须正确才让 HUD 更新。
                var graph = snapshot?.Graph ?? decode(json);
                if (graph == null || graph.Find("stash") != null || graph.Validate() != InventoryError.None)
                    return false;
                if (snapshot?.Graph != null && snapshot.LootId >= 0 != (graph.Find(LootInventoryExchange.Root) != null))
                    return false;
                LootId = snapshot?.Graph != null ? snapshot.LootId : -1;
                LootVersion = snapshot?.Graph != null ? snapshot.LootVersion : 0;
                CorpseCharacterIndex = snapshot?.CorpseCharacterIndex ?? -1;
                CorpseEquipmentSlots = snapshot?.CorpseEquipmentSlots;
                DissectingLootId = snapshot?.DissectingLootId ?? -1;
                DissectionRemaining = snapshot?.DissectionRemaining ?? 0;
                SearchTotal = snapshot?.SearchTotal ?? -1;
                SearchFootprints = snapshot?.SearchFootprints;
                SearchItemSeconds = snapshot?.SearchItemSeconds ?? 0;
                MedicalItemId = snapshot?.MedicalItemId;
                MedicalRemaining = snapshot?.MedicalRemaining ?? 0;
                MedicalIsConsumable = snapshot?.MedicalIsConsumable ?? false;
                ActionFeedback = snapshot?.ActionFeedback;
                Result = snapshot?.Result != null && snapshot.Result.Valid(chunk.RaidId) ? snapshot.Result : null;
                Graph = graph;
                RaidId = chunk.RaidId;
                Sequence = chunk.Sequence;
                RequestId = chunk.RequestId;
                Error = chunk.Error;
                return true;
            } catch (Exception) { return false; }
        }
    }
}