using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
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
}
