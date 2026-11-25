using Unity.Entities;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private Client.RaidHealthView m_HealthView;
        private VisualElement m_EquipmentHost, m_HealthHost;
        private Button m_EquipmentTab, m_HealthTab;
        private Label m_OxygenWarning, m_OxygenReserve, m_OxygenTime, m_OxygenAir;
        private VisualElement m_OxygenPanel, m_OxygenFill;
        private bool m_HealthSelected;
        private float m_NextOxygenAlert;
        private readonly MoonkovOxygenFeedback m_OxygenFeedback = new();

        private void EquipmentTab() { SelectHealth(false); }
        private void HealthTab() { SelectHealth(true); }

        private void SelectHealth(bool health) {
            if ((m_InventoryRequestPending && !TimedActionActive) || m_HealthSelected == health) return;
            // 背包装备页和健康页共享同一窗口。切到健康页就暂停格子操作和角色预览，切回来时再恢复。
            m_HealthSelected = health;
            m_EquipmentHost.style.display = health ? DisplayStyle.None : DisplayStyle.Flex;
            m_HealthHost.style.display = health ? DisplayStyle.Flex : DisplayStyle.None;
            m_EquipmentTab.EnableInClassList("raid-tab-active", !health);
            m_HealthTab.EnableInClassList("raid-tab-active", health);
            RefreshInventoryTitle(m_OpenedLootId);
            if (health)
                m_InventoryView.Suspend();
            else if (m_InventoryVisible) m_InventoryView.Show();
            RefreshHealth();
        }

        private void RefreshHealth() {
            if (m_World == null || !m_World.IsCreated) return;
            var player = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>()
                ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>()
                : default;
            int medicines = 0, food = 0, water = 0, oxygenCanisters = 0;
            string foodCode = null, waterCode = null, oxygenCode = null;
            float medicalRestore = 0;
            if (!m_InventoryQuery.IsEmptyIgnoreFilter) {
                var inventory = CurrentInventory;
                if (inventory.RaidId == m_Snapshot.RaidId && inventory.Graph != null)
                    foreach (var item in inventory.Graph.Items) {
                        // 只计算玩家当下够得到的补给。按钮里的数量和默认物品类型，必须对应实际能从口袋或装备中取到的物品。
                        if (inventory.Graph.MedicalAccessible(item)) {
                            medicines += item.Quantity;
                            if (medicalRestore == 0)
                                medicalRestore = Inventory.ConsumableCatalog.MedicalRestore(item.Code);
                        }
                        if (inventory.Graph.ConsumableAccessible(item)) {
                            var supply = Inventory.ConsumableCatalog.Get(item.Code);
                            if (supply.Oxygen > 0) {
                                oxygenCanisters += item.Quantity;
                                if (oxygenCode == null) oxygenCode = item.Code;
                            } else if (supply.Food) {
                                food += item.Quantity;
                                if (foodCode == null && RaidNutrition.CanConsume(player, supply)) foodCode = item.Code;
                            } else {
                                water += item.Quantity;
                                if (waterCode == null && RaidNutrition.CanConsume(player, supply))
                                    waterCode = item.Code;
                            }
                        }
                    }
            }
            if (m_InventoryVisible && m_HealthSelected)
                m_HealthView.Present(player, medicines, m_InventoryRequestPending, food, water, oxygenCanisters,
                    foodCode, waterCode, oxygenCode, medicalRestore, TimedActionBusyText());
            bool oxygenReady = player.BodyHealthInitialized && player.CurrentHealth > 0 && !m_WasSettled;
            bool needsSuitAir = RaidOxygen.NeedsSuitAir(player);
            m_OxygenPanel.style.display = DisplayStyle.None; // 实际读数由统一的战斗监测仪显示。
            if (oxygenReady) {
                MoonkovLocalization.Set(m_OxygenReserve, "O₂ / {0:0}%", player.Oxygen);
                MoonkovLocalization.Set(m_OxygenTime, "EST. {0}", Client.RaidHealthView.OxygenTimeText(player));
                MoonkovLocalization.Set(m_OxygenAir, Client.RaidHealthView.OxygenAirText(player));
                m_OxygenFill.style.width = Length.Percent(Mathf.Clamp(player.Oxygen, 0, 100));
                m_OxygenPanel.EnableInClassList("oxygen-low", player.Oxygen <= 25);
                m_OxygenPanel.EnableInClassList("oxygen-critical", needsSuitAir && player.Oxygen <= 10);
            }
            var effects = RaidNutrition.Effects(player);
            bool oxygenWarning = oxygenReady && needsSuitAir && player.Oxygen <= 25;
            var otherEffects = effects & ~(SurvivalEffects.LowOxygen | SurvivalEffects.Hypoxia);
            bool warning = (oxygenWarning || otherEffects != SurvivalEffects.None) && !m_WasSettled;
            m_OxygenWarning.style.display =
                DisplayStyle.None; // 沿用后面已有的告警节奏，画面上只保留一层提示。
            if (!oxygenReady) m_OxygenFeedback.Reset();
            bool suitAlert = oxygenReady && m_OxygenFeedback.Observe(player, Time.unscaledTime);
            if (suitAlert) MoonkovAudio.PlayWeaponLayer(MoonkovAudio.Library?.SuitWarning, Vector3.zero, 1, 0);
            if (!warning) {
                m_NextOxygenAlert = 0;
                return;
            }
            bool critical = (oxygenWarning && player.Oxygen <= 10) ||
                            (effects & (SurvivalEffects.Starving | SurvivalEffects.Dehydrated)) != 0;
            m_OxygenWarning.EnableInClassList("oxygen-critical", critical);
            string text = oxygenWarning
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
                    MoonkovLocalization.Text(player.Oxygen <= 0
                        ? "HYPOXIA / SEEK PRESSURIZED AIR"
                        : "LOW OXYGEN / {0:0}%"), player.Oxygen)
                : "";
            string nutritionText = Client.RaidHealthView.EffectText(otherEffects);
            if (!string.IsNullOrEmpty(nutritionText)) text += (text.Length > 0 ? "\n" : "") + nutritionText;
            MoonkovLocalization.Set(m_OxygenWarning, text);
            bool alert = !oxygenWarning && (effects & (SurvivalEffects.Hungry | SurvivalEffects.Starving |
                                                       SurvivalEffects.Thirsty | SurvivalEffects.Dehydrated)) != 0;
            if (alert && Time.unscaledTime >= m_NextOxygenAlert) {
                MoonkovAudio.Play(MoonkovAudio.Library?.Notification, Vector3.zero);
                m_NextOxygenAlert = Time.unscaledTime + (critical ? 10 : 20);
            }
        }

        private bool m_AudioExtracting;
        private float m_ExtractionSoundAt;
        private int m_ExtractionSoundRaid;

        private void UseMedical(Inventory.InventoryItem item) { UseMedical(item, BodyPart.Auto); }

        private void ConsumeSupply(string code) {
            if (m_World == null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = CurrentInventory;
            var item = inventory.Graph?.Items.Find(i => i.Code == code && inventory.Graph.ConsumableAccessible(i));
            if (item != null) UseConsumable(item);
        }

        private void UseConsumable(Inventory.InventoryItem item) {
            if (m_InventoryRequestPending || m_Snapshot.Phase != RaidPhase.Active ||
                m_InventoryQuery.IsEmptyIgnoreFilter)
                return;
            var inventory = CurrentInventory;
            var player = m_PlayerQuery.HasSingleton<PredictedPlayerGhost>()
                ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>()
                : default;
            var definition = Inventory.ConsumableCatalog.Get(item.Code);
            bool shieldSupply = Inventory.BatteryEnergy.IsCell(item);
            bool oxygenSupply = definition != null && definition.Oxygen > 0;
            if (!(shieldSupply ? CanRechargeShieldCell(item) :
                    oxygenSupply ? RaidOxygen.CanRefill(player) : RaidNutrition.CanConsume(player, definition))) {
                string message = shieldSupply
                    ?
                    "Cannot recharge shield: equip a generator with space for energy and use charged cells from pockets or an equipped badge."
                    : oxygenSupply
                        ? "Cannot refill oxygen: reserve is full or the player is inactive."
                        : "Cannot consume: resource is full, stomach capacity is insufficient, or the player is inactive.";
                MoonkovAudio.Error();
                m_InventoryView.Present(inventory.Graph, message);
                m_HealthView.Message(message);
                return;
            }
            m_InventoryRequestId = NextManualActionRequest();
            if (Send(new RaidConsumableUseRpc {
                    RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId,
                    ExpectedVersion = inventory.Graph.Version, ItemId = item.Id
                })) {
                TrackInventoryAudio(shieldSupply ? MoonkovAudio.Library?.BatteryInsert :
                    oxygenSupply ? MoonkovAudio.Library?.OxygenRefill : null);
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
                if (!shieldSupply) {
                    BeginTimedAction();
                    m_MedicalActive = m_ConsumableActive = true;
                    m_ConsumableFood = definition.Food;
                    m_ConsumableOxygen = oxygenSupply;
                    m_ActionDuration = Inventory.ConsumableCatalog.UseSeconds(item.Code);
                    m_MedicalEndsAt = Time.unscaledTime + m_ActionDuration;
                }
            } else {
                const string message = "Not connected. Consumable remains in its container.";
                MoonkovAudio.Error();
                m_InventoryView.Present(inventory.Graph, message);
                m_HealthView.Message(message);
            }
            RefreshHealth();
        }

        private void TreatPart(BodyPart part) {
            if (m_World == null || !m_World.IsCreated || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = CurrentInventory;
            var item = inventory.Graph?.Items.Find(inventory.Graph.MedicalAccessible);
            if (item != null) UseMedical(item, part);
        }

        private void UseMedical(Inventory.InventoryItem item, BodyPart part) {
            if (m_InventoryRequestPending || m_Snapshot.Phase != RaidPhase.Active) return;
            var inventory = CurrentInventory;
            m_InventoryRequestId = NextManualActionRequest();
            if (Send(new RaidMedicalUseRpc {
                    RaidId = m_Snapshot.RaidId, RequestId = m_InventoryRequestId,
                    ExpectedVersion = inventory.Graph.Version, ItemId = item.Id, Part = part
                })) {
                BeginTimedAction();
                TrackInventoryAudio(item.Code == "medkit" || item.Code == "med_gel"
                    ? null
                    : MoonkovAudio.Library?.Medical);
                m_InventoryRequestedAt = Time.unscaledTime;
                m_InventoryRequestPending = true;
                m_MedicalActive = true;
                m_ConsumableActive = m_ConsumableFood = m_ConsumableOxygen = false;
                m_ActionDuration =
                    Inventory.ConsumableCatalog.MedicalSeconds(item.Code, MoonRaidMap.Active.MedicalSeconds);
                m_MedicalEndsAt = Time.unscaledTime + m_ActionDuration;
            } else {
                MoonkovAudio.Error();
                m_InventoryView.Present(inventory.Graph, "Not connected. Medical item remains in its container.");
            }
            RefreshHealth();
        }
    }
}
