using Unity.Entities;
using Unity.MP_FPS.DollSinger;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private Client.ContainerInventoryView m_InventoryView;

        private EntityQuery m_InventoryQuery;
        private uint m_InventorySequence, m_InventoryRequestId;
        private readonly MoonkovActionAudio m_InventoryAudio = new();
        private readonly MoonkovLootRevealAudio m_LootRevealAudio = new();

        private void TrackInventoryAudio(SoundDef success, SoundDef start = null) {
            m_InventoryAudio.Track(m_Snapshot.RaidId, m_InventoryRequestId, success, start);
        }

        private float m_InventoryRequestedAt;
        private bool m_InventoryRequestPending;
        private bool m_LootOpenPending;
        private bool m_CloseLootWhenIdle;
        private int m_DissectingLootId = -1;
        private float m_DissectionEndsAt;
        private bool m_DissectionInterrupted;
        private bool m_MedicalActive, m_ConsumableActive, m_ConsumableFood, m_ConsumableOxygen;
        private float m_MedicalEndsAt, m_ActionDuration, m_ActionFeedbackUntil;
        private string m_ActionFeedback;
        private Client.RaidTimedActionView m_ActionView;
        private Button m_SwitchDrop;
        private bool m_ActionAccepted, m_ActionCancelPending, m_ActionFailed;
        private uint m_ActionRequestId;
        private int m_ActionRaidId;
        private int m_OpenedLootId = -1;
        private Label m_InventoryTitle;

        private RaidInventoryClientState CurrentInventory =>
            m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());

        private void RefreshInventoryTitle(int lootId) {
            string title;
            if (m_HealthSelected) title = "HEALTH / SUIT STATUS";
            else if (RaidFurnitureLoot.IsFurniture(lootId)) title = "抽屉搜刮 / 随身装备";
            else if (lootId >= RaidLootContainers.FirstMissionCacheId) title = "任务输出槽 / 随身装备";
            else if (IsDroppedLoot(lootId)) title = "地面丢弃物 / 随身装备";
            else if (lootId >= RaidLootContainers.FirstDeathBagId) title = "REMAINS / CARRIED INVENTORY";
            else title = lootId >= 0 ? "SUPPLY CACHE / CARRIED INVENTORY" : "CHARACTER / CARRIED INVENTORY";
            MoonkovLocalization.Set(m_InventoryTitle, title);
        }

        private bool IsDroppedLoot(int id) {
            if (id < RaidLootContainers.FirstDeathBagId || m_World == null || !m_World.IsCreated ||
                m_StateQuery.IsEmptyIgnoreFilter)
                return false;
            var bags =
                m_World.EntityManager.GetComponentObject<RaidDeathBagClientState>(m_StateQuery.GetSingletonEntity());
            return bags.Bags.TryGetValue(id, out var bag) &&
                   bag.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex;
        }

        private uint NextManualActionRequest() {
            var inventory = CurrentInventory;
            // 搜索、整理、设备操作都可能推进请求号。拿大的一边加一，保证服务端能把这次请求和较早的操作分开。
            m_InventoryRequestId =
                System.Math.Max(System.Math.Max(m_InventoryRequestId, m_MissionRequest), inventory.RequestId) + 1;
            return m_InventoryRequestId;
        }

        private bool TimedActionActive => m_MedicalActive || m_DissectingLootId >= 0;

        private void CancelTimedAction() {
            if (!TimedActionActive || m_ActionCancelPending || m_World == null || !m_World.IsCreated ||
                m_InventoryQuery.IsEmptyIgnoreFilter)
                return;
            var inventory = CurrentInventory;
            m_InventoryRequestId = NextManualActionRequest();
            if (Send(new RaidActionCancelRpc { RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId })) {
                TrackInventoryAudio(MoonkovAudio.Library?.Equipment);
                m_LootRevealAudio.Cancel();
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
                m_ActionCancelPending = true;
            }
        }

        private void ResetTimedAction() {
            m_MedicalActive = m_ConsumableActive = m_ConsumableFood = m_ConsumableOxygen = false;
            m_DissectingLootId = -1;
            m_ActionFeedbackUntil = 0;
            m_ActionFeedback = null;
            m_ActionAccepted = m_ActionCancelPending = m_ActionFailed = false;
            m_ActionRequestId = 0;
            m_ProgressTacticalRequest = 0;
            m_ProgressTacticalDuration = 0;
            m_CloseLootWhenIdle = false;
        }

        private void BeginTimedAction() {
            // 把发起请求时的编号记到读条状态，之后才能判断是哪一次搜索或用药被服务器接受。
            m_ActionRequestId = m_InventoryRequestId;
            m_ActionAccepted = m_ActionCancelPending = false;
            m_ActionFeedback = null;
            m_ActionFeedbackUntil = 0;
            m_ActionFailed = false;
        }

        private string TimedActionBusyText() {
            return TimedActionActive
                ? m_ConsumableActive ? (m_ConsumableOxygen ? "REFILL O₂" : m_ConsumableFood ? "EAT" : "DRINK") :
                m_MedicalActive ? "TREATING" : "SEARCHING"
                : "USING ITEM";
        }

        private void PresentTimedAction() {
            if (m_ActionView == null) return;
            bool active = TimedActionActive && m_Snapshot.Phase == RaidPhase.Active;
            bool visible = m_Snapshot.Phase == RaidPhase.Active && !GameSettings.Instance.IsPauseMenuOpen &&
                           !CinematicInputBlocked && !m_MapOpen && !m_MissionDetailsVisible &&
                           !m_CampaignJournalVisible && !m_ClashActionsBlocked && !DarkSightRequested;
            // 有搜索或治疗时用这一套读条。空闲时把准星附近的空间让给机关、撤离、换弹等其他进度。
            if (!active && PresentOtherProgress(visible)) return;
            float remaining = (m_MedicalActive ? m_MedicalEndsAt : m_DissectionEndsAt) - Time.unscaledTime;
            m_ActionView.Present(active, m_ActionAccepted, m_ActionCancelPending, m_MedicalActive, remaining,
                m_ActionDuration, Time.unscaledTime < m_ActionFeedbackUntil ? m_ActionFeedback : null, m_ActionFailed,
                visible, m_InventoryVisible,
                m_ConsumableActive ? m_ConsumableOxygen ? "REFILL O₂" : m_ConsumableFood ? "EAT" : "DRINK" : null);
            if (active && visible && !ChatInputBlocked && RaidKeyBindings.Pressed(RaidKey.TiptoeOrCancel))
                CancelTimedAction();
        }

        private void SetInventory(bool visible) {
            if (visible && m_FieldRecordOpen) return;
            if (visible) SetRaidMapOpen(false);
            if (visible && m_CampaignJournalVisible) SetCampaignJournal(false);
            if (visible && m_MissionDetailsVisible) SetMissionDetails(false);
            if (m_InventoryRequestPending && m_Snapshot.Phase == RaidPhase.Active && !visible)
                if (!TimedActionActive && !QuickTacticalActionPending)
                    return;
            // 关闭背包时先对仍在进行的搜索发取消。当前还有治疗或道具动作占用请求时，等空闲再通知房主关容器。
            if (!visible && m_DissectingLootId >= 0) CancelTimedAction();
            if (visible != m_InventoryVisible && (!visible || !m_LootOpenPending))
                MoonkovAudio.Play(
                    visible ? MoonkovAudio.Library?.InventoryOpen :
                    m_OpenedLootId >= 0 ? MoonkovAudio.Library?.ContainerClose : MoonkovAudio.Library?.InventoryClose,
                    Vector3.zero);
            if (visible) { m_CloseLootWhenIdle = false; } else if (m_OpenedLootId >= 0 &&
                                                                   m_Snapshot.Phase == RaidPhase.Active) {
                if (m_MedicalActive || QuickTacticalActionPending)
                    m_CloseLootWhenIdle = true;
                else
                    RequestLoot(-1);
            }
            m_InventoryVisible = visible;
            m_Inventory.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) {
                ClearInventoryBackdrop();
                m_LootRevealAudio.Cancel();
            }
            if (visible) {
                UpdateInventoryView();
                if (!m_HealthSelected) m_InventoryView.Show();
                RefreshHealth();
            } else { m_InventoryView.Suspend(); }
            Utils.SetCursorVisible(visible || m_MapOpen || m_MissionDetailsVisible ||
                                   GameSettings.Instance.IsPauseMenuOpen || m_WasSettled);
        }

        private void ClosePack() { SetInventory(false); }

        private void CloseLootWhenIdle() {
            if (!m_CloseLootWhenIdle) return;
            if (m_InventoryVisible || m_OpenedLootId < 0 || m_Snapshot.Phase != RaidPhase.Active) {
                m_CloseLootWhenIdle = false;
                return;
            }
            // 本地窗口已经收起，但对服务端的关容器请求可以稍后发。等原请求确认才发，避免用新的请求号盖过还在执行的动作。
            if (!TimedActionActive && !QuickTacticalActionPending && !m_InventoryRequestPending && RequestLoot(-1))
                m_CloseLootWhenIdle = false;
        }

        public static int SelectNearbyDrop(RaidDeathBagClientState bags, Vector3 position, int current) {
            int next = int.MaxValue, first = int.MaxValue;
            foreach (var bag in bags.Bags.Values) {
                if (bag.LootId == current || bag.CharacterIndex != RaidLootContainers.DroppedItemCharacterIndex ||
                    bag.Empty || Vector3.Distance(position, (Vector3)bag.Position) > RaidRules.PickupRange ||
                    UnityEngine.Physics.Linecast(position + Vector3.up * 1.4f,
                        (Vector3)bag.Position + Vector3.up * .25f, LayerMask.GetMask("Default", "Ground"),
                        QueryTriggerInteraction.Ignore))
                    continue;
                first = System.Math.Min(first, bag.LootId);
                if (bag.LootId > current) next = System.Math.Min(next, bag.LootId);
            }
            return next != int.MaxValue ? next : first != int.MaxValue ? first : -1;
        }

        private void SwitchNearbyDrop() {
            if (!m_InventoryVisible || (m_InventoryRequestPending && !TimedActionActive) ||
                !m_PlayerQuery.HasSingleton<LocalTransform>())
                return;
            var bags =
                m_World.EntityManager.GetComponentObject<RaidDeathBagClientState>(m_StateQuery.GetSingletonEntity());
            int current = m_DissectingLootId >= 0 ? m_DissectingLootId : m_OpenedLootId;
            int next = SelectNearbyDrop(bags, m_PlayerQuery.GetSingleton<LocalTransform>().Position, current);
            if (next >= 0) OpenLootContainer(next);
        }

        // 先向服务端发打开请求。客户端可以立即显示自己的背包和等待进度，搜刮目标里实际能拿的物品由后续快照带来。
        private void OpenLootContainer(int id) {
            if (!RequestLoot(id)) return;
            BeginTimedAction();
            var bags =
                m_World.EntityManager.GetComponentObject<RaidDeathBagClientState>(m_StateQuery.GetSingletonEntity());
            m_HealthSelected = false;
            m_EquipmentHost.style.display = DisplayStyle.Flex;
            m_HealthHost.style.display = DisplayStyle.None;
            m_EquipmentTab.EnableInClassList("raid-tab-active", true);
            m_HealthTab.EnableInClassList("raid-tab-active", false);
            bool dissect = bags.Bags.TryGetValue(id, out var remains) && remains.RequiresDissection;
            m_MedicalActive = m_ConsumableActive = m_ConsumableFood = m_ConsumableOxygen = false;
            m_DissectingLootId = id;
            m_ActionDuration = dissect ? Inventory.AlienHarvest.DissectionSeconds : MoonRaidMap.Active.SearchSeconds;
            m_DissectionEndsAt = Time.unscaledTime + m_ActionDuration;
            string opening = dissect || m_ClassicHud ? "SEARCHING" :
                RaidFurnitureLoot.IsSupplyCache(id) ? "正在搜索补给箱…" :
                RaidFurnitureLoot.IsFurniture(id) ? "正在搜索抽屉…" :
                IsDroppedLoot(id) ? "搜索地面丢弃物…" :
                id >= RaidLootContainers.FirstDeathBagId ? "Searching fallen expedition…" : "Opening supply cache…";
            var inventory = CurrentInventory;
            // 在新容器被确认前，只展示玩家身上的库存。先复制快照并移除旧搜刮内容，避免误把上一只容器当成这只。
            var carried = inventory.Graph?.Clone();
            carried?.Items.RemoveAll(item =>
                item.Id == Inventory.LootInventoryExchange.Root ||
                inventory.Graph.RootOf(item.Id) == Inventory.LootInventoryExchange.Root);
            m_LootOpenPending = true;
            m_InventoryView.Present(carried, opening, false);
            m_InventoryView.SetReadOnly(opening);
            SetInventory(true);
        }

        private bool RequestLoot(int lootId) {
            if (m_World == null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return false;
            var inventory = CurrentInventory;
            m_InventoryRequestId = NextManualActionRequest();
            if (!Send(new RaidLootOpenRpc
                    { RaidId = m_Snapshot.RaidId, LootId = lootId, RequestId = m_InventoryRequestId })) {
                MoonkovAudio.Error();
                return false;
            }
            TrackInventoryAudio(null);
            m_LootRevealAudio.Begin(m_Snapshot.RaidId, m_InventoryRequestId, lootId, inventory.Graph);
            m_InventoryRequestedAt = Time.unscaledTime;
            m_InventoryRequestPending = true;
            return true;
        }

        // Sequence 变化表示收到了新库存消息，RequestId 用来匹配正在等的请求。收到重复的内容时也能用这两个值判断该更新什么。
        private void UpdateInventoryView() {
            if (m_World == null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = CurrentInventory;
            // 普通请求和读条动作的等待时间不同。超时后清理本地忙碌状态，仍用最后收到的库存显示，给下一次操作留下入口。
            if (m_InventoryRequestPending && Time.unscaledTime - m_InventoryRequestedAt >
                (TimedActionActive ? m_ActionDuration + 5 : 5)) {
                bool timedOut = TimedActionActive;
                m_DissectingLootId = -1;
                m_MedicalActive = m_ConsumableActive = m_ConsumableFood = m_ConsumableOxygen = false;
                m_ActionCancelPending = false;
                if (timedOut) {
                    m_ActionFeedback = "ACTION INTERRUPTED";
                    m_ActionFailed = true;
                    m_ActionFeedbackUntil = Time.unscaledTime + 3;
                }
                m_InventoryAudio.Reset();
                m_LootRevealAudio.Cancel();
                MoonkovAudio.Error();
                m_InventoryRequestPending = false;
                m_LootOpenPending = false;
                m_InventoryView.SetReadOnly(null);
                m_InventoryView.Present(inventory.Graph, "ACTION INTERRUPTED", lootId: inventory.LootId,
                    droppedItem: IsDroppedLoot(inventory.LootId), corpseCharacterIndex: inventory.CorpseCharacterIndex,
                    corpseEquipmentSlots: inventory.CorpseEquipmentSlots);
                m_HealthView.Message("ACTION INTERRUPTED");
            }
            if (!m_InventoryVisible || inventory.RaidId != m_Snapshot.RaidId || m_Snapshot.Phase != RaidPhase.Active) {
                m_LootRevealAudio.Cancel();
            } else {
                // 先处理关窗或取消，再播放排队中的物品揭示声。
                if (inventory.Sequence != m_InventorySequence) m_LootRevealAudio.Observe(inventory);
                if (m_LootRevealAudio.TryPlay(Time.unscaledTime))
                    MoonkovAudio.Play(MoonkovAudio.Library?.Container, Vector3.zero);
            }
            if (inventory.Graph == null || inventory.RaidId != m_Snapshot.RaidId) return;
            if (m_DissectingLootId >= 0) MoonkovLocalization.Set(m_InventoryTitle, "SEARCHING");
            if (inventory.Sequence != m_InventorySequence) {
                m_InventorySequence = inventory.Sequence;
                var cue = m_InventoryAudio.Observe(inventory.RaidId, inventory.RequestId,
                    inventory.MedicalItemId != null || (inventory.DissectingLootId >= 0 && m_LootOpenPending),
                    inventory.Error != Inventory.InventoryError.None, MoonkovAudio.Library?.Error);
                MoonkovAudio.PlayWeaponLayer(cue, Vector3.zero, 1, 0);
                // 旧回执可能在新动作发出去后抵达。用动作编号挡住旧的开始消息，避免读条跳回上一次搜索或治疗。
                if ((inventory.MedicalItemId != null || inventory.DissectingLootId >= 0) &&
                    inventory.RequestId < m_ActionRequestId)
                    return;
                // 服务端确认治疗后，用它给的剩余秒数重设读条。读条期间背包变只读，防止搬走正在使用的物品。
                if (inventory.MedicalItemId != null) {
                    m_ActionAccepted = true;
                    m_MedicalActive = true;
                    m_MedicalEndsAt = Time.unscaledTime + inventory.MedicalRemaining;
                    m_ConsumableActive = inventory.MedicalIsConsumable;
                    m_ConsumableOxygen = m_ConsumableActive && Inventory.ConsumableCatalog
                        .Get(inventory.Graph.Find(inventory.MedicalItemId)?.Code)?.Oxygen > 0;
                    m_ConsumableFood = m_ConsumableActive && Inventory.ConsumableCatalog
                        .Get(inventory.Graph.Find(inventory.MedicalItemId)?.Code)?.Food == true;
                    m_ActionDuration = Mathf.Max(m_ActionDuration, inventory.MedicalRemaining);
                    string action = TimedActionBusyText();
                    m_InventoryView.Present(inventory.Graph, action, false, inventory.LootId,
                        IsDroppedLoot(inventory.LootId), inventory.CorpseCharacterIndex,
                        inventory.CorpseEquipmentSlots);
                    m_InventoryView.SetReadOnly(action);
                    return;
                }
                if (inventory.DissectingLootId >= 0) {
                    m_ActionAccepted = true;
                    m_DissectingLootId = inventory.DissectingLootId;
                    m_DissectionEndsAt = Time.unscaledTime + inventory.DissectionRemaining;
                    m_ActionDuration = inventory.SearchItemSeconds > 0
                        ? inventory.SearchItemSeconds
                        : Mathf.Max(m_ActionDuration, inventory.DissectionRemaining);
                    // 搜索揭示进度和搬运物品是两件并行的事。进度快照照常刷新右栏，确认了这次搬运才解除等待。
                    bool moveAcknowledged = !m_InventoryRequestPending || inventory.RequestId >= m_InventoryRequestId;
                    bool requestFinished = m_InventoryRequestPending && moveAcknowledged;
                    if (moveAcknowledged && !m_ActionCancelPending) {
                        m_InventoryRequestPending = false;
                        m_InventoryRequestedAt = Time.unscaledTime;
                    }
                    m_LootOpenPending = false;
                    m_InventoryView.SetReadOnly(m_ActionCancelPending ? "SEARCHING" : null);
                    string moveError = requestFinished && inventory.Error != Inventory.InventoryError.None
                        ? MoonkovLocalization.Format($"Inventory: {inventory.Error}")
                        : null;
                    m_InventoryView.Present(inventory.Graph, moveError, moveAcknowledged && !m_ActionCancelPending,
                        inventory.LootId, IsDroppedLoot(inventory.LootId), searchTotal: inventory.SearchTotal,
                        searchFootprints: inventory.SearchFootprints);
                    return;
                }
                bool wasTimed = TimedActionActive;
                bool wasMedical = m_MedicalActive;
                bool wasConsumable = m_ConsumableActive;
                bool wasOpen = m_OpenedLootId >= 0;
                bool missionCache =
                    (m_OpenedLootId >= RaidLootContainers.FirstMissionCacheId &&
                     !RaidFurnitureLoot.IsFurniture(m_OpenedLootId)) ||
                    (inventory.LootId >= RaidLootContainers.FirstMissionCacheId &&
                     !RaidFurnitureLoot.IsFurniture(inventory.LootId));
                // 通常靠请求号判断操作是否完成。已经打开的容器被服务端关掉时也要处理，否则窗口会继续停在旧容器上。
                bool acknowledged = !m_InventoryRequestPending || inventory.RequestId >= m_InventoryRequestId ||
                                    (wasOpen && inventory.LootId < 0);
                if (acknowledged) m_InventoryRequestPending = false;
                m_OpenedLootId = inventory.LootId;
                if (acknowledged) {
                    m_DissectionInterrupted = m_DissectingLootId >= 0 &&
                                              inventory.Error == Inventory.InventoryError.Inaccessible;
                    m_ActionCancelPending = false;
                    m_DissectingLootId = -1;
                    m_MedicalActive = m_ConsumableActive = m_ConsumableFood = m_ConsumableOxygen = false;
                    m_LootOpenPending = false;
                    m_InventoryView.SetReadOnly(null);
                }
                RefreshInventoryTitle(inventory.LootId);
                string inventoryMessage = acknowledged && inventory.Error != Inventory.InventoryError.None
                    ? missionCache ? MissionInventoryFailure(inventory.Error) :
                    m_DissectionInterrupted || wasMedical ? "操作已中断：移动、受伤、开火、切换或库存变化；未消耗物品。" :
                    MoonkovLocalization.Format($"Inventory: {inventory.Error.ToString()}")
                    : null;
                m_InventoryView.Present(inventory.Graph, inventoryMessage, acknowledged, inventory.LootId,
                    IsDroppedLoot(inventory.LootId), inventory.CorpseCharacterIndex, inventory.CorpseEquipmentSlots);
                if (acknowledged && !string.IsNullOrEmpty(inventory.ActionFeedback)) {
                    inventoryMessage =
                        wasConsumable && inventory.Error == Inventory.InventoryError.None &&
                        inventory.ActionFeedback == "补给使用完成。" ? null :
                        wasTimed ? Client.RaidActionReadout.Receipt(inventory.ActionFeedback,
                            inventory.Error != Inventory.InventoryError.None) : inventory.ActionFeedback;
                    m_InventoryView.Present(inventory.Graph, inventoryMessage, lootId: inventory.LootId,
                        droppedItem: IsDroppedLoot(inventory.LootId),
                        corpseCharacterIndex: inventory.CorpseCharacterIndex,
                        corpseEquipmentSlots: inventory.CorpseEquipmentSlots);
                }
                if (acknowledged && wasTimed) {
                    bool consumed = wasConsumable && inventory.Error == Inventory.InventoryError.None &&
                                    inventory.ActionFeedback == "补给使用完成。";
                    m_ActionFeedback = consumed ? null :
                        !wasMedical && string.IsNullOrEmpty(inventory.ActionFeedback) &&
                        inventory.Error == Inventory.InventoryError.None && inventory.LootId >= 0 ? "CONTAINER OPENED" :
                        Client.RaidActionReadout.Receipt(inventory.ActionFeedback,
                            inventory.Error != Inventory.InventoryError.None);
                    m_ActionFailed = !consumed && m_ActionFeedback != "TREATMENT COMPLETE" &&
                                     m_ActionFeedback != "SEARCH COMPLETE" && m_ActionFeedback != "CONTAINER OPENED" &&
                                     m_ActionFeedback != "ACTION CANCELLED";
                    m_ActionFeedbackUntil = Time.unscaledTime + 3;
                }
                // 健康页接收的是这次操作的结果。任务产物过期等错误额外交给任务 HUD 提示。
                if (acknowledged) m_HealthView.Message(inventoryMessage ?? "");
                if (missionCache && acknowledged && inventory.Error == Inventory.InventoryError.Inaccessible)
                    MissionNotice(m_MissionSnapshot.Phase == Missions.MissionPhase.Expired
                        ? "回收窗口已关闭 · 本批未撤出的产物失效，普通搜刮与撤离仍可继续。"
                        : inventoryMessage);
                // 防护装备快照也可能带着同一次使用失败。按操作回执处理，无需关掉背包。
                // 只处理从打开容器变为没有容器的情况。普通随身库存快照也会用这个负编号，不能每收到一次就关闭整个背包。
                if (inventory.LootId < 0 && wasOpen) {
                    if (inventory.Error == Inventory.InventoryError.Inaccessible)
                        m_LootErrorUntil = Time.unscaledTime + 3;
                    SetInventory(false);
                } else if (m_InventoryVisible && !m_HealthSelected && inventory.LootId < 0 && !m_LootOpenPending) {
                    m_InventoryView.Show();
                }
            }
        }

        private void DropItem(Inventory.InventoryItem item) {
            if (m_InventoryRequestPending || m_Snapshot.Phase != RaidPhase.Active) return;
            var inventory = CurrentInventory;
            m_InventoryRequestId = NextManualActionRequest();
            if (Send(new RaidInventoryDropRpc {
                    RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId,
                    ExpectedVersion = inventory.Graph.Version, ItemId = item.Id
                })) {
                TrackInventoryAudio(MoonkovAudio.Library?.InventoryDrop);
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
            } else {
                MoonkovAudio.Error();
                m_InventoryView.Present(inventory.Graph, "未连接；物品仍在原容器内。", lootId: inventory.LootId,
                    droppedItem: IsDroppedLoot(inventory.LootId), corpseCharacterIndex: inventory.CorpseCharacterIndex,
                    corpseEquipmentSlots: inventory.CorpseEquipmentSlots);
            }
        }

        // 单纯整理随身物品带一份库存版本。有打开的搜刮容器时，额外带容器编号和它的版本，由房主校验两边的库存。
        private void SendInventoryMove(Inventory.InventoryCommand command) {
            var inventory = CurrentInventory;
            m_InventoryRequestId = NextManualActionRequest();
            // 同一个拖动命令可用于整理自己背包，也可用于跨容器搜刮。跨容器时服务端必须同时比对双方版本。
            bool sent = inventory.LootId >= 0
                ? Send(new RaidLootMoveRpc {
                    RaidId = m_Snapshot.RaidId, LootId = inventory.LootId, ExpectedLootVersion = inventory.LootVersion,
                    RequestId = m_InventoryRequestId, ExpectedVersion = command.ExpectedVersion,
                    Operation = command.Operation,
                    ItemId = command.ItemId ?? "", Parent = command.Parent ?? "", Region = command.Region ?? "",
                    TargetId = command.TargetId ?? "",
                    X = command.X, Y = command.Y, Quantity = command.Quantity, Rotated = command.Rotated
                })
                : Send(new RaidInventoryMoveRpc {
                    RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId,
                    ExpectedVersion = command.ExpectedVersion,
                    Operation = command.Operation, ItemId = command.ItemId ?? "", Parent = command.Parent ?? "",
                    Region = command.Region ?? "", TargetId = command.TargetId ?? "",
                    X = command.X, Y = command.Y, Quantity = command.Quantity, Rotated = command.Rotated
                });
            if (sent) {
                TrackInventoryAudio(MoonkovInventoryAudio.ForMove(inventory.Graph, command, MoonkovAudio.Library));
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
            } else {
                MoonkovAudio.Error();
                m_InventoryView.Present(inventory.Graph, "Not connected. Item remains in its container.");
            }
        }
    }
}
