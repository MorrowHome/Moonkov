using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private Label m_GrenadeLabel;
        private float m_NextGrenadeThrow, m_NextGrenadeText, m_GrenadeErrorUntil;
        private uint m_GrenadeRequest;

        private void UpdateGrenades(bool active)
        {
            m_GrenadeLabel = m_Root.Q<Label>("raidGrenade");
            if (m_GrenadeLabel == null) return;
            m_GrenadeLabel.style.display = active && !m_InventoryVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active || m_InventoryQuery.IsEmptyIgnoreFilter) return;
            var inventory = m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity());
            var graph = inventory.Graph;
            if (graph == null) return;
            Inventory.InventoryItem selected = null;
            int available = 0;
            foreach (var item in graph.Items)
                if (EnergyGrenadeRules.Accessible(graph, item))
                { available += item.Quantity; selected ??= item; }
            if (m_GrenadeRequest != 0 && inventory.RequestId >= m_GrenadeRequest)
            {
                if (inventory.Error != Inventory.InventoryError.None) m_GrenadeErrorUntil = Time.unscaledTime + 2;
                m_GrenadeRequest = 0;
            }
            if (Time.unscaledTime >= m_NextGrenadeText)
            {
                MoonkovLocalization.Set(m_GrenadeLabel, m_GrenadeErrorUntil > Time.unscaledTime ? "THROW REJECTED / CHECK RIG AND CLEARANCE" :
                    m_InventoryRequestPending && m_GrenadeRequest != 0 ? "THROW REQUESTED..." : "[G] ENERGY GRENADE / {0}", available);
                m_NextGrenadeText = Time.unscaledTime + .1f;
            }
            if (ChatInputBlocked || selected == null || m_InventoryVisible || QuickBusy || DarkSightRequested || GameSettings.Instance.IsPauseMenuOpen ||
                UnityEngine.Cursor.lockState != CursorLockMode.Locked || Time.unscaledTime < m_NextGrenadeThrow ||
                Keyboard.current == null || !Keyboard.current.gKey.wasPressedThisFrame) return;
            m_InventoryRequestId = System.Math.Max(m_InventoryRequestId, inventory.RequestId) + 1;
            if (!Send(new RaidGrenadeThrowRpc { RaidId = m_Snapshot.RaidId, ExpectedVersion = graph.Version,
                ItemId = selected.Id, RequestId = m_InventoryRequestId })) return;
            m_GrenadeRequest = m_InventoryRequestId;
            m_InventoryRequestedAt = Time.unscaledTime; m_InventoryRequestPending = true;
            m_NextGrenadeThrow = Time.unscaledTime + EnergyGrenadeRules.Cooldown;
            m_NextGrenadeText = 0;
        }
    }
}
