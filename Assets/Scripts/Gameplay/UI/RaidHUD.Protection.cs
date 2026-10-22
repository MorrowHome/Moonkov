using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private VisualElement m_ProtectionRoot, m_ShieldTrack, m_ShieldFill;
        private Label m_ShieldValue, m_ShieldPower, m_ShieldStatus, m_HeadDomeStatus;
        private int m_ProtectionRaid;
        private uint m_ProtectionBreak;
        private float m_ProtectionBrokenUntil;

        private void InitializeProtection()
        {
            m_ProtectionRoot = Resources.Load<VisualTreeAsset>("Moonkov/UI/RaidProtection").CloneTree();
            m_ProtectionRoot.name = "raidProtectionScope";
            m_ProtectionRoot.pickingMode = PickingMode.Ignore;
            m_QuickView.Element.parent.Add(m_ProtectionRoot);
            m_ShieldValue = m_ProtectionRoot.Q<Label>("raidShieldValue");
            m_ShieldPower = m_ProtectionRoot.Q<Label>("raidShieldPower");
            m_ShieldStatus = m_ProtectionRoot.Q<Label>("raidShieldStatus");
            m_HeadDomeStatus = m_ProtectionRoot.Q<Label>("raidHeadDomeStatus");
            m_ShieldTrack = m_ProtectionRoot.Q("raidShieldTrack");
            m_ShieldFill = m_ProtectionRoot.Q("raidShieldFill");
            m_ProtectionRaid = 0; m_ProtectionBreak = 0; m_ProtectionBrokenUntil = 0;
            MoonkovLocalization.Bind(m_ProtectionRoot);
            m_ProtectionRoot.style.display = DisplayStyle.None;
        }

        private bool CanRechargeShieldCell(InventoryItem item)
        {
            if (item == null || !QuickAlive || QuickBusy || TimedActionInputActive || QuickGraph == null) return false;
            var current = QuickGraph.Find(item.Id);
            return current != null && EnergyShieldInventory.CanRecharge(m_PlayerQuery.GetSingleton<PredictedPlayerGhost>(), QuickGraph, current);
        }

        private void PresentProtection(bool active)
        {
            if (m_ProtectionRoot == null) return;
            bool visible = active && m_World != null && m_World.IsCreated && m_PlayerQuery.HasSingleton<PredictedPlayerGhost>();
            m_ProtectionRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) return;
            var parent = m_ClashMode ? m_ClashView.EquipmentStatusHost : m_QuickView.Element.parent;
            if (m_ProtectionRoot.parent != parent) parent.Add(m_ProtectionRoot);
            m_ProtectionRoot.style.position = m_ClashMode ? Position.Relative : Position.Absolute;
            m_ProtectionRoot.style.left = m_ClashMode ? 0 : 12;
            m_ProtectionRoot.style.top = m_ClashMode ? 0 : 155;
            m_ProtectionRoot.style.width = m_ClashMode ? Length.Percent(100) : new Length(200);
            m_ProtectionRoot.EnableInClassList("raid-protection-clash", m_ClashMode);

            var player = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            if (m_ProtectionRaid != m_Snapshot.RaidId)
            {
                m_ProtectionRaid = m_Snapshot.RaidId; m_ProtectionBreak = player.ShieldBreakSequence;
                m_ProtectionBrokenUntil = 0;
            }
            if (player.ShieldBreakSequence != m_ProtectionBreak)
            { m_ProtectionBreak = player.ShieldBreakSequence; m_ProtectionBrokenUntil = Time.unscaledTime + 1.8f; }
            MoonkovLocalization.Set(m_ShieldValue, player.ShieldEquipped ? "SHIELD / {0:0}" : "SHIELD / NOT EQUIPPED", player.ShieldCharge);
            m_ShieldTrack.style.display = player.ShieldEquipped ? DisplayStyle.Flex : DisplayStyle.None;
            m_ShieldFill.style.width = Length.Percent(Mathf.Clamp(player.ShieldCharge / EnergyShieldRules.Capacity * 100, 0, 100));
            m_ShieldPower.style.display = player.ShieldEquipped ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_ShieldPower, "SHIELD ENERGY / {0:0}%", player.ShieldEnergy / EnergyShieldRules.EnergyCapacity * 100);
            m_ShieldStatus.style.display = player.ShieldEquipped ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(m_ShieldStatus, Time.unscaledTime < m_ProtectionBrokenUntil ? "SHIELD BROKEN" :
                player.ShieldEnergy <= 0 ? "ENERGY EMPTY / RECHARGE WITH A CELL" :
                player.ShieldCharge >= EnergyShieldRules.Capacity ? "SHIELD READY" :
                player.ShieldRecoveryDelay > 0 ? "RECOVERY IN {0:0.0}s" :
                player.ShieldRegenerating ? "RECHARGING SHIELD" : "SHIELD RECOVERY PAUSED", player.ShieldRecoveryDelay);
            m_ProtectionRoot.EnableInClassList("raid-protection-low", player.ShieldEquipped && player.ShieldCharge <= 0);
            bool unsealed = !player.HeadDomeEquipped && !player.BreathableAir;
            m_HeadDomeStatus.EnableInClassList("raid-protection-head-danger", unsealed || !player.BreathableAir && player.Oxygen <= RaidOxygen.CriticalThreshold);
            MoonkovLocalization.Set(m_HeadDomeStatus, unsealed ? "NO HEAD SHROUD / SEEK PRESSURIZED AIR" :
                player.HeadDomeEquipped ? "HEAD SHROUD / O₂ {0:0}%" : "HEAD SHROUD / NOT EQUIPPED", player.Oxygen);
        }

        private void DisposeProtection()
        {
            m_ProtectionRoot?.RemoveFromHierarchy(); m_ProtectionRoot = null;
        }
    }
}
