using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private uint m_GrenadeRequest;
        private int m_GrenadeRaid;
        private Unity.Entities.World m_GrenadeWorld;
        private float m_GrenadeRequestedAt;
        private bool m_GrenadeButtonHeld, m_GrenadeLow, m_GrenadeReleaseQueued;
        private bool m_GrenadeWasOccupied;
        private int m_GrenadeNextSlot;
        private GrenadeCommand m_GrenadeRequestedCommand;
        private PredictedPlayerGhost GrenadeState => m_World != null && m_World.IsCreated && m_PlayerQuery.HasSingleton<PredictedPlayerGhost>()
            ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>() : default;
        private bool GrenadeBusy => GrenadeHandling.Occupied(GrenadeState) || m_GrenadeRequest != 0;
        public static bool GrenadeInHands => s_Active != null && s_Active.GrenadeBusy;
        internal static bool RouteGrenadeInput(bool primaryPressed, bool primaryHeld, bool secondaryPressed, bool secondaryHeld) =>
            s_Active != null && s_Active.ReadGrenadeInput(primaryPressed, primaryHeld, secondaryPressed, secondaryHeld);

        private bool EquipGrenade(InventoryItem item)
        {
            if (!QuickAlive || QuickBusy || PointerRequested || DarkSightRequested || GameSettings.Instance.IsPauseMenuOpen ||
                !GrenadeHandling.Accessible(QuickGraph, item)) return false;
            return SendGrenade(GrenadeCommand.Equip, item.Id);
        }
        private bool SendGrenade(GrenadeCommand command, string item = null, bool low = false)
        {
            var graph = QuickGraph;
            if (graph == null || m_GrenadeRequest != 0) return false;
            uint request = NextManualActionRequest();
            if (!Send(new RaidGrenadeThrowRpc { RaidId = m_Snapshot.RaidId, ExpectedVersion = graph.Version,
                RequestId = request, ItemId = item ?? "", Command = command, Underhand = low, HoldId = GrenadeState.GrenadeHoldId })) return false;
            m_InventoryRequestId = m_GrenadeRequest = request;
            m_GrenadeRequestedCommand = command; m_GrenadeRequestedAt = m_InventoryRequestedAt = Time.unscaledTime;
            m_InventoryRequestPending = true;
            RaidQuickSlotInput.ReleasePrimary();
            return true;
        }
        private bool ReadGrenadeInput(bool primaryPressed, bool primaryHeld, bool secondaryPressed, bool secondaryHeld)
        {
            if (!GrenadeBusy) return false;
            var phase = (GrenadePhase)GrenadeState.GrenadePhase;
            if (!QuickAlive || PointerRequested || DarkSightRequested || GameSettings.Instance.IsPauseMenuOpen ||
                QuickTyping(m_Root.panel?.focusController.focusedElement as UnityEngine.UIElements.VisualElement)) return true;
            if (phase == GrenadePhase.Safe && !m_GrenadeButtonHeld && (primaryPressed || secondaryPressed))
            {
                bool low = !primaryPressed && secondaryPressed;
                if (SendGrenade(GrenadeCommand.Prime, low: low))
                { m_GrenadeButtonHeld = true; m_GrenadeLow = low; }
            }
            if (m_GrenadeButtonHeld && !(m_GrenadeLow ? secondaryHeld : primaryHeld))
            { m_GrenadeButtonHeld = false; m_GrenadeReleaseQueued = true; }
            FlushGrenadeRelease();
            return true;
        }
        private void FlushGrenadeRelease()
        {
            var phase = (GrenadePhase)GrenadeState.GrenadePhase;
            if (m_GrenadeReleaseQueued && (phase == GrenadePhase.Priming || phase == GrenadePhase.Live) &&
                SendGrenade(GrenadeCommand.Release)) m_GrenadeReleaseQueued = false;
        }
        private void UpdateGrenades(bool active)
        {
            if (m_GrenadeRaid != m_Snapshot.RaidId || m_GrenadeWorld != m_World)
            {
                m_GrenadeRaid = m_Snapshot.RaidId; m_GrenadeWorld = m_World;
                m_GrenadeRequest = 0; m_GrenadeButtonHeld = m_GrenadeReleaseQueued = false;
                m_GrenadeWasOccupied = false; m_GrenadeNextSlot = 0;
            }
            var inventory = QuickInventory;
            if (m_GrenadeRequest != 0 && inventory != null && inventory.RequestId >= m_GrenadeRequest)
            {
                if (inventory.Error != InventoryError.None)
                {
                    QuickNotice("手雷操作未完成");
                    if (m_GrenadeRequestedCommand == GrenadeCommand.Prime)
                        m_GrenadeButtonHeld = m_GrenadeReleaseQueued = false;
                }
                m_GrenadeRequest = 0;
            }
            // Do not automatically replay an uncertain network operation.
            if (m_GrenadeRequest != 0 && Time.unscaledTime - m_GrenadeRequestedAt > 8)
            { m_GrenadeRequest = 0; m_GrenadeButtonHeld = false; m_GrenadeReleaseQueued = false; QuickNotice("等待连接恢复"); }
            var state = GrenadeState; var phase = (GrenadePhase)state.GrenadePhase;
            if (phase != GrenadePhase.None) m_GrenadeWasOccupied = true;
            else if (m_GrenadeWasOccupied && m_GrenadeRequest == 0)
            {
                m_GrenadeWasOccupied = false;
                if (GrenadeHandling.Throwable(m_QuickBindings?.SelectedItem(QuickGraph)?.Code)) m_QuickBindings.Deselect();
                int next = m_GrenadeNextSlot; m_GrenadeNextSlot = 0;
                if (next != 0 && active) SelectQuickSlot(next);
            }
            bool menu = !active || PointerRequested || GameSettings.Instance.IsPauseMenuOpen || !Application.isFocused ||
                UnityEngine.Cursor.lockState != CursorLockMode.Locked;
            if (phase != GrenadePhase.None && menu)
            {
                m_GrenadeButtonHeld = false; m_GrenadeReleaseQueued = false;
                if (GrenadeHandling.Live(phase)) SendGrenade(GrenadeCommand.Drop);
                else if (phase == GrenadePhase.Safe || phase == GrenadePhase.Drawing) SendGrenade(GrenadeCommand.Stow);
                return;
            }
            FlushGrenadeRelease();
            if (phase == GrenadePhase.None && m_GrenadeRequest == 0)
                m_GrenadeButtonHeld = m_GrenadeReleaseQueued = false;
            if (menu || QuickTyping(m_Root.panel?.focusController.focusedElement as UnityEngine.UIElements.VisualElement)) return;
            bool equip = Keyboard.current?.gKey.wasPressedThisFrame == true || Gamepad.current?.leftShoulder.wasPressedThisFrame == true;
            bool cancel = Keyboard.current?.xKey.wasPressedThisFrame == true;
            if (phase != GrenadePhase.None)
            {
                if ((equip || cancel) && (phase == GrenadePhase.Safe || phase == GrenadePhase.Drawing)) SendGrenade(GrenadeCommand.Stow);
                else if (cancel && GrenadeHandling.Live(phase)) SendGrenade(GrenadeCommand.Drop);
                return;
            }
            if (!equip || QuickBusy || QuickGraph == null) return;
            // Prefer the selected throwable; otherwise take the first accessible grenade.
            var selected = m_QuickBindings?.SelectedItem(QuickGraph);
            if (!GrenadeHandling.Accessible(QuickGraph, selected)) selected = QuickGraph.Items.Find(item => GrenadeHandling.Accessible(QuickGraph, item));
            if (selected != null) EquipGrenade(selected);
        }
        private string GrenadeCaption()
        {
            var phase = (GrenadePhase)GrenadeState.GrenadePhase;
            if (phase == GrenadePhase.None) return m_GrenadeRequest != 0 ? "取出手雷" : null;
            bool pad = Gamepad.current != null && Mouse.current == null;
            return phase switch {
                GrenadePhase.Drawing => "取出手雷",
                GrenadePhase.Safe => pad ? "RT 远投 / LT 低抛 / LB 收回" : "左键远投 / 右键低抛 / G 收回",
                GrenadePhase.Priming => "启用手雷",
                GrenadePhase.Live => pad ? $"{GrenadeState.GrenadeFuseRemaining:0.0} 秒 / 松开扳机投掷" : $"{GrenadeState.GrenadeFuseRemaining:0.0} 秒 / 松开投掷 / X 丢下",
                GrenadePhase.Throwing => "投掷",
                GrenadePhase.Stowing => "收回手雷",
                _ => null
            };
        }
    }
}
