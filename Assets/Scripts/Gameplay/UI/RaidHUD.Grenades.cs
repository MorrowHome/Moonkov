// 局内手雷按键和 HUD 反馈。客户端从角色状态区分取出、准备、松手和收回等阶段，通过请求让服务器执行。
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private uint m_GrenadeRequest;
        private int m_GrenadeRaid;
        private Entities.World m_GrenadeWorld;
        private float m_GrenadeRequestedAt;
        private bool m_GrenadeButtonHeld, m_GrenadeLow, m_GrenadeReleaseQueued;
        private bool m_GrenadeWasOccupied;
        private int m_GrenadeNextSlot;
        private GrenadeCommand m_GrenadeRequestedCommand;

        private PredictedPlayerGhost GrenadeState =>
            m_World != null && m_World.IsCreated && m_PlayerQuery.HasSingleton<PredictedPlayerGhost>()
                ? m_PlayerQuery.GetSingleton<PredictedPlayerGhost>()
                : default;

        private bool GrenadeBusy => GrenadeHandling.Occupied(GrenadeState) || m_GrenadeRequest != 0;
        public static bool GrenadeInHands => s_Active != null && s_Active.GrenadeBusy;

        internal static bool RouteGrenadeInput(
            bool primaryPressed,
            bool primaryHeld,
            bool secondaryPressed,
            bool secondaryHeld) {
            return s_Active != null &&
                   s_Active.ReadGrenadeInput(primaryPressed, primaryHeld, secondaryPressed, secondaryHeld);
        }

        private bool EquipGrenade(InventoryItem item) {
            if (!QuickAlive || QuickBusy || PointerRequested || DarkSightRequested ||
                GameSettings.Instance.IsPauseMenuOpen || !GrenadeHandling.Accessible(QuickGraph, item))
                return false;
            return SendGrenade(GrenadeCommand.Equip, item.Id);
        }

        private bool SendGrenade(GrenadeCommand command, string item = null, bool low = false) {
            var graph = QuickGraph;
            // 手雷动作顺序依赖前一次确认。还在等待动作时不再并发发请求，每次都带当前库存版本和手持编号。
            if (graph == null || m_GrenadeRequest != 0) return false;
            uint request = NextManualActionRequest();
            if (!Send(new RaidGrenadeThrowRpc {
                    RaidId = m_Snapshot.RaidId, ExpectedVersion = graph.Version,
                    RequestId = request, ItemId = item ?? "", Command = command, Underhand = low,
                    HoldId = GrenadeState.GrenadeHoldId
                }))
                return false;
            m_InventoryRequestId = m_GrenadeRequest = request;
            m_GrenadeRequestedCommand = command;
            m_GrenadeRequestedAt = m_InventoryRequestedAt = Time.unscaledTime;
            m_InventoryRequestPending = true;
            RaidQuickSlotInput.ReleasePrimary();
            return true;
        }

        private bool ReadGrenadeInput(
            bool primaryPressed,
            bool primaryHeld,
            bool secondaryPressed,
            bool secondaryHeld) {
            if (!GrenadeBusy) return false;
            var phase = (GrenadePhase)GrenadeState.GrenadePhase;
            if (!QuickAlive || PointerRequested || DarkSightRequested || GameSettings.Instance.IsPauseMenuOpen ||
                QuickTyping(m_Root.panel?.focusController.focusedElement as UnityEngine.UIElements.VisualElement))
                return true;
            if (phase == GrenadePhase.Safe && !m_GrenadeButtonHeld && (primaryPressed || secondaryPressed)) {
                // 根据按下的一侧选择游戏里的投掷动作。记住哪枚键启动，只等那枚键松开才请求完成。
                bool low = !primaryPressed && secondaryPressed;
                if (SendGrenade(GrenadeCommand.Prime, low: low)) {
                    m_GrenadeButtonHeld = true;
                    m_GrenadeLow = low;
                }
            }
            if (m_GrenadeButtonHeld && !(m_GrenadeLow ? secondaryHeld : primaryHeld)) {
                m_GrenadeButtonHeld = false;
                m_GrenadeReleaseQueued = true;
            }
            FlushGrenadeRelease();
            return true;
        }

        // 玩家可能在准备手雷的回执到达前就松手。这里记住这次松开，等状态允许发送时再补发。
        private void FlushGrenadeRelease() {
            var phase = (GrenadePhase)GrenadeState.GrenadePhase;
            if (m_GrenadeReleaseQueued && (phase == GrenadePhase.Priming || phase == GrenadePhase.Live) &&
                SendGrenade(GrenadeCommand.Release))
                m_GrenadeReleaseQueued = false;
        }

        private void UpdateGrenades(bool active) {
            // 换局清空手雷状态、上次按键和排队等待的武器切换。旧局操作不能在新局得到补发。
            if (m_GrenadeRaid != m_Snapshot.RaidId || m_GrenadeWorld != m_World) {
                m_GrenadeRaid = m_Snapshot.RaidId;
                m_GrenadeWorld = m_World;
                m_GrenadeRequest = 0;
                m_GrenadeButtonHeld = m_GrenadeReleaseQueued = false;
                m_GrenadeWasOccupied = false;
                m_GrenadeNextSlot = 0;
            }
            var inventory = QuickInventory;
            // 从最新库存回执收尾请求。准备失败时清空松手队列，否则会存下一次无效的动作。
            if (m_GrenadeRequest != 0 && inventory != null && inventory.RequestId >= m_GrenadeRequest) {
                if (inventory.Error != InventoryError.None) {
                    QuickNotice("手雷操作未完成");
                    if (m_GrenadeRequestedCommand == GrenadeCommand.Prime)
                        m_GrenadeButtonHeld = m_GrenadeReleaseQueued = false;
                }
                m_GrenadeRequest = 0;
            }
            // Do not automatically replay an uncertain network operation.
            if (m_GrenadeRequest != 0 && Time.unscaledTime - m_GrenadeRequestedAt > 8) {
                m_GrenadeRequest = 0;
                m_GrenadeButtonHeld = false;
                m_GrenadeReleaseQueued = false;
                QuickNotice("等待连接恢复");
            }
            var state = GrenadeState;
            var phase = (GrenadePhase)state.GrenadePhase;
            if (phase != GrenadePhase.None) { m_GrenadeWasOccupied = true; } else if (m_GrenadeWasOccupied &&
             m_GrenadeRequest == 0) {
                m_GrenadeWasOccupied = false;
                if (GrenadeHandling.Throwable(m_QuickBindings?.SelectedItem(QuickGraph)?.Code))
                    m_QuickBindings.Deselect();
                int next = m_GrenadeNextSlot;
                m_GrenadeNextSlot = 0;
                if (next != 0 && active) SelectQuickSlot(next);
            }
            bool menu = !active || PointerRequested || GameSettings.Instance.IsPauseMenuOpen ||
                        !Application.isFocused || Cursor.lockState != CursorLockMode.Locked;
            // 菜单打开时停止读取玩法按键。手上已有道具就按当前游戏状态做收回或丢弃处理。
            if (phase != GrenadePhase.None && menu) {
                m_GrenadeButtonHeld = false;
                m_GrenadeReleaseQueued = false;
                if (GrenadeHandling.Live(phase))
                    SendGrenade(GrenadeCommand.Drop);
                else if (phase == GrenadePhase.Safe || phase == GrenadePhase.Drawing) SendGrenade(GrenadeCommand.Stow);
                return;
            }
            FlushGrenadeRelease();
            if (phase == GrenadePhase.None && m_GrenadeRequest == 0)
                m_GrenadeButtonHeld = m_GrenadeReleaseQueued = false;
            if (menu || QuickTyping(
                    m_Root.panel?.focusController.focusedElement as UnityEngine.UIElements.VisualElement))
                return;
            bool equip = RaidKeyBindings.Pressed(RaidKey.Grenade) ||
                         Gamepad.current?.leftShoulder.wasPressedThisFrame == true;
            bool cancel = RaidKeyBindings.Pressed(RaidKey.TiptoeOrCancel);
            if (phase != GrenadePhase.None) {
                if ((equip || cancel) && (phase == GrenadePhase.Safe || phase == GrenadePhase.Drawing))
                    SendGrenade(GrenadeCommand.Stow);
                else if (cancel && GrenadeHandling.Live(phase)) SendGrenade(GrenadeCommand.Drop);
                return;
            }
            if (!equip || QuickBusy || QuickGraph == null) return;
            // Prefer the selected throwable; otherwise take the first accessible grenade.
            var selected = m_QuickBindings?.SelectedItem(QuickGraph);
            // 先尝试快捷栏目前选中的可用手雷。没有时，再找随身库存第一件符合规则的手雷。
            if (!GrenadeHandling.Accessible(QuickGraph, selected))
                selected = QuickGraph.Items.Find(item => GrenadeHandling.Accessible(QuickGraph, item));
            if (selected != null) EquipGrenade(selected);
        }

        private string GrenadeCaption() {
            var phase = (GrenadePhase)GrenadeState.GrenadePhase;
            if (phase == GrenadePhase.None) return m_GrenadeRequest != 0 ? "取出手雷" : null;
            bool pad = Gamepad.current != null && Mouse.current == null;
            return phase switch {
                GrenadePhase.Drawing => "取出手雷",
                GrenadePhase.Safe => pad
                    ? "RT 远投 / LT 低抛 / LB 收回"
                    : MoonkovLocalization.Format($"左键远投 / 右键低抛 / {RaidKeyBindings.DisplayName(RaidKey.Grenade)} 收回"),
                GrenadePhase.Priming => "启用手雷",
                GrenadePhase.Live => pad
                    ? $"{GrenadeState.GrenadeFuseRemaining:0.0} 秒 / 松开扳机投掷"
                    : MoonkovLocalization.Format(
                        $"{GrenadeState.GrenadeFuseRemaining:0.0} 秒 / 松开投掷 / {RaidKeyBindings.DisplayName(RaidKey.TiptoeOrCancel)} 丢下"),
                GrenadePhase.Throwing => "投掷",
                GrenadePhase.Stowing => "收回手雷",
                _ => null
            };
        }
    }
}