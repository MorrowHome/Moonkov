// 把换弹输入和本地角色状态接到光环蓄能提示。HaloChargeFeedback 给出等确认、充能、已满等状态，这里把它显示成准星旁的读数。
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public partial class InGameHUD {
        private readonly HaloChargeFeedback m_ChargeFeedback = new();
        private EntityQuery m_ChargeInventoryQuery;
        private bool m_ChargeQueryCreated;
        private VisualElement m_ChargeWeaponPanel;
        private HaloChargeNotice m_LastChargeNotice;
        private float m_NextChargeText;

        public static void ObserveHaloChargeInput(in PlayerInput input, in PredictedPlayerGhost player) {
            if (s_Active) s_Active.m_ChargeFeedback.ObserveInput(input, player, Time.unscaledTime);
        }

        private void ResetChargeFeedback() {
            m_ChargeFeedback.Reset();
            m_LastChargeNotice = HaloChargeNotice.None;
            m_NextChargeText = 0;
            if (m_ChargeQueryCreated && m_ClientWorld != null && m_ClientWorld.IsCreated)
                m_ChargeInventoryQuery.Dispose();
            m_ChargeQueryCreated = false;
        }

        private void PresentChargeFeedback(in PredictedPlayerGhost player) {
            if (m_ReloadingLabel == null || !DollSingerWeapons.IsHalo(player.EquippedWeaponID) ||
                !player.InventoryWeapons) {
                m_ChargeFeedback.Reset();
                m_LastChargeNotice = HaloChargeNotice.None;
                m_NextChargeText = 0;
                return;
            }
            InventoryGraph graph = null;
            // 只有装备有光环的角色才需要库存查询，用当前客户端 World 建一次，换局后要释放并重建。
            if (!m_ChargeQueryCreated) {
                m_ChargeInventoryQuery =
                    m_EntityManager.CreateEntityQuery(typeof(RaidClientState), typeof(RaidInventoryClientState));
                m_ChargeQueryCreated = true;
            }
            if (!m_ChargeInventoryQuery.IsEmptyIgnoreFilter) {
                var snapshot = m_ChargeInventoryQuery.GetSingleton<RaidClientState>().Snapshot;
                var inventory =
                    m_EntityManager.GetComponentObject<RaidInventoryClientState>(m_ChargeInventoryQuery
                        .GetSingletonEntity());
                // 只有本局有效战斗阶段的库存可用来算电池剩余。旧图与新局快照错位时先记未知。
                if (snapshot.Phase == RaidPhase.Active && inventory.RaidId == snapshot.RaidId) graph = inventory.Graph;
            }
            var weapon =
                WeaponManager.Instance.WeaponRegistry.GetWeaponData(player.EquippedWeaponID, player.EquippedModules);
            var status = m_ChargeFeedback.Update(player, weapon, graph,
                !GameSettings.Instance.IsPauseMenuOpen && !RaidHUD.GameplayActionsBlocked, Time.unscaledTime);
            // 共享能量模式让生命监视区处理提示。旧的装弹文案在兼容模式仍可继续用。
            if (player.HaloEnergyInitialized) {
                // 共用的光环能量由已有的医疗监测 HUD 显示。
                m_LastChargeNotice = status.Notice;
                m_ReloadingLabel.style.display = DisplayStyle.None;
                m_ChargeWeaponPanel.style.display = DisplayStyle.None;
                return;
            }
            // 状态改变立即更新，连续变化的倒计时限频刷新。画面之间可保留已有提示。
            if (status.Notice != m_LastChargeNotice || Time.unscaledTime >= m_NextChargeText) {
                PresentChargeStatus(m_RootElement, status);
                if (status.Notice == HaloChargeNotice.Charging)
                    MoonkovLocalization.Set(m_ReloadingLabel, "TARGET {0}/{1}", status.Ammo, status.Capacity);
                m_LastChargeNotice = status.Notice;
                m_NextChargeText = Time.unscaledTime + .1f;
            }
            // 弹药栏可能自动隐藏。两次文字刷新之间仍应保留短暂的补能提示。
            if (status.Notice != HaloChargeNotice.None) m_ChargeWeaponPanel.style.display = DisplayStyle.Flex;
            if (m_AmmoBar != null) {
                bool charging = status.Notice == HaloChargeNotice.Charging;
                m_AmmoBar.style.display = DisplayStyle.None; // 动作进度由共用的光环读条显示。
                if (charging) {
                    m_AmmoBar.highValue = 1;
                    m_AmmoBar.value = status.Progress;
                }
            }
        }

        // 原生界面检查也复用此处。这里展示已得到的状态，不解析战斗输入。
        public static void PresentChargeStatus(VisualElement root, HaloChargeStatus status) {
            var label = root.Q<Label>("reloading-label");
            var bar = root.Q<ProgressBar>("player-ammo-bar");
            bool charging = status.Notice == HaloChargeNotice.Charging;
            bool visible = status.Notice != HaloChargeNotice.None;
            label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            label.EnableInClassList("halo-charge-warning",
                status.Notice == HaloChargeNotice.MoveCells || status.Notice == HaloChargeNotice.LowEnergy);
            if (bar != null) {
                bar.style.display = charging ? DisplayStyle.Flex : DisplayStyle.None;
                if (charging) {
                    bar.highValue = 1;
                    bar.value = status.Progress;
                }
            }
            if (!visible) return;
            root.Q("weapon-info-container").style.display = DisplayStyle.Flex;
            switch (status.Notice) {
                case HaloChargeNotice.Charging:
                    MoonkovLocalization.Set(label, "CHARGING / {0:0.0}s\nTARGET {1}/{2}", status.Remaining, status.Ammo,
                        status.Capacity);
                    break;
                case HaloChargeNotice.Ready:
                    MoonkovLocalization.Set(label, "CHARGED / {0}/{1}", status.Ammo, status.Capacity);
                    break;
                case HaloChargeNotice.Full: MoonkovLocalization.Set(label, "HALO FULL"); break;
                case HaloChargeNotice.MoveCells:
                    MoonkovLocalization.Set(label, "Move cells to pockets or your chest badge.");
                    break;
                case HaloChargeNotice.LowEnergy:
                    MoonkovLocalization.Set(label, "CELL ENERGY TOO LOW\nREADY {0} / NEED {1}", status.Energy,
                        status.Cost);
                    break;
                case HaloChargeNotice.Syncing: MoonkovLocalization.Set(label, "Syncing battery inventory..."); break;
                default: MoonkovLocalization.Set(label, "Awaiting charge confirmation..."); break;
            }
        }
    }
}