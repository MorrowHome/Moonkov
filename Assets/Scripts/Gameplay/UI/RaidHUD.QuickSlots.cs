using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    // Installed only on the owned network character; the gesture lab keeps its original keys.
    public static class RaidQuickSlotInput
    {
        private static DollSingerInput s_Input;
        public static void Bind(DollSingerInput input)
        {
            if (!input) return;
            if (s_Input && s_Input != input) Unbind(s_Input);
            s_Input = input;
            input.QuickSlotSelection = RaidHUD.RouteQuickSlotSelection;
            input.DarkSightMode = RaidHUD.UsesDarkSightViewKey;
            input.QuickSlotPrimaryUse = RaidHUD.RouteQuickSlotPrimary;
            input.MeleeAttackInput = RaidHUD.RouteMeleeInput;
        }
        public static void Unbind(DollSingerInput input)
        {
            if (!input) return;
            input.QuickSlotSelection = null; input.QuickSlotPrimaryUse = null; input.DarkSightMode = null; input.MeleeAttackInput = null;
            if (s_Input == input) s_Input = null;
        }
        internal static void ReleasePrimary() { if (s_Input) s_Input.SuppressPrimaryUntilRelease(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_Input = null;
    }

    public sealed partial class RaidHUD
    {
        private QuickSlotBindings m_QuickBindings;
        private QuickSlotView m_QuickView;
        private bool m_QuickWasTool, m_QuickActive;
        private string m_QuickStatus;
        private float m_QuickStatusUntil;
        private string m_QuickInteractionHint;
        private float m_QuickInteractionHintUntil;
        private uint m_QuickTacticalRequest, m_QuickTrackedTacticalRequest;
        private float m_QuickTacticalRequestedAt;
        private TacticalItemAction m_QuickRequestedAction;
        private uint m_QuickOwnerRequest;
        private TacticalItemFeedback m_QuickOwnerFeedback;
        private InventoryError m_QuickOwnerError;
        private int m_QuickRaid;
        private Unity.Entities.World m_QuickWorld;
        private string m_QuickProfileScope;
        private static string QuickPreferenceScope
        {
            get
            {
#if UNITY_EDITOR
                if (QuickSlotPreferenceStore.ValidationStore != null && !string.IsNullOrEmpty(QuickSlotPreferenceStore.ValidationScope))
                    return QuickSlotPreferenceStore.ValidationScope;
#endif
                return PlayerProfileClient.LocalPreferenceScope;
            }
        }
        private TacticalItemOwnerRpc QuickOwner => TacticalItemsFeedback.LocalOwner.RaidId == m_Snapshot.RaidId ? TacticalItemsFeedback.LocalOwner : default;
        private bool QuickTacticalActionPending => QuickOwner.Remaining > 0 || m_QuickTacticalRequest != 0;
        private bool QuickTacticalCancelPending => m_QuickTacticalRequest != 0 && m_QuickRequestedAction == TacticalItemAction.Cancel;
        private bool QuickManualBusy => m_InventoryRequestPending || TimedActionActive || QuickTacticalActionPending;
        private bool QuickBusy => QuickManualBusy || MeleeBusy;
        private RaidInventoryClientState QuickInventory => m_World != null && m_World.IsCreated && !m_InventoryQuery.IsEmptyIgnoreFilter
            ? m_World.EntityManager.GetComponentObject<RaidInventoryClientState>(m_InventoryQuery.GetSingletonEntity()) : null;
        private InventoryGraph QuickGraph => QuickInventory is { } inventory && inventory.RaidId == m_Snapshot.RaidId ? inventory.Graph : null;
        private bool QuickAlive => GameSettings.Instance.GameState == GlobalGameState.InGame && m_Snapshot.RaidId > 0 && m_Snapshot.Phase == RaidPhase.Active && !m_ClashActionsBlocked &&
            m_World != null && m_World.IsCreated && m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() &&
            m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().CurrentHealth > 0;
        public static bool QuickSlotToolSelected
        {
            get
            {
                if (s_Active == null || s_Active.m_QuickBindings == null || !s_Active.QuickAlive || PointerRequested || GameSettings.Instance.IsPauseMenuOpen) return false;
                var graph = s_Active.QuickGraph; var item = s_Active.m_QuickBindings.SelectedItem(graph);
                return item != null && QuickSlotBindings.WeaponSlot(graph, item) == 0;
            }
        }
        internal static bool UsesDarkSightViewKey() => s_Active != null && s_Active.m_ClashMode;
        internal static int RouteQuickSlotSelection(int number) => s_Active != null ? s_Active.SelectQuickSlot(number) : 0;
        internal static bool RouteQuickSlotPrimary(bool pressed) => s_Active != null && s_Active.UseQuickSlot(pressed);

        private void InitializeQuickSlots()
        {
            m_QuickBindings = new QuickSlotBindings(new QuickSlotPreferenceStore());
            m_QuickView = new QuickSlotView(m_Root, slot =>
            {
                if (!QuickCanBind) return;
                m_QuickBindings.Clear(slot); QuickNotice(m_QuickBindings.Reason);
            }, slot => BindQuickSlot(slot, m_InventoryView.QuickSlotCandidate));
            m_QuickView.Element.parent.Add(m_ActionPanel);
            m_InventoryView.QuickSlotDrop = DropOnQuickSlot;
            m_InventoryView.QuickSlotDragPreview = (item, point) => m_QuickView?.PreviewDrop(item == null ? -1 : m_QuickView.Hit(point), QuickSlotBindings.CanBind(QuickGraph, item));
            m_Root.RegisterCallback<KeyDownEvent>(BindQuickKey, TrickleDown.TrickleDown);
        }
        private static bool QuickTyping(VisualElement element) => QuickSlotBindings.Typing(element);
        private bool QuickCanBind => QuickAlive && m_InventoryVisible && !m_HealthSelected &&
            !GameSettings.Instance.IsPauseMenuOpen && !m_MissionDetailsVisible;
        private void BindQuickKey(KeyDownEvent evt)
        {
            if (!QuickCanBind || QuickTyping(evt.target as VisualElement) || evt.ctrlKey || evt.altKey || evt.commandKey) return;
            int slot = (int)evt.keyCode - (int)KeyCode.Alpha1;
            if (slot < 0 || slot >= QuickSlotBindings.Count) return;
            BindQuickSlot(slot, m_InventoryView.QuickSlotCandidate); evt.StopImmediatePropagation();
        }
        private void BindQuickSlot(int slot, InventoryItem item)
        {
            if (!QuickCanBind) { QuickNotice("当前无法设置快捷栏，请关闭其他菜单后重试。"); return; }
            m_QuickBindings.UseProfile(QuickPreferenceScope);
            m_QuickBindings.Bind(slot, item, QuickGraph);
            QuickNotice(m_QuickBindings.Reason);
        }
        private bool DropOnQuickSlot(InventoryItem item, Vector2 point)
        {
            int slot = m_QuickView.Hit(point);
            if (slot < 0) return false;
            BindQuickSlot(slot, item);
            return true; // A rejected hotbar drop must never become an inventory move.
        }
        private void QuickNotice(string message) { m_QuickStatus = message; m_QuickStatusUntil = Time.unscaledTime + 3; }
        private int SelectQuickSlot(int number)
        {
            if (m_QuickBindings == null) return 0;
            m_QuickBindings.UseProfile(QuickPreferenceScope);
            m_QuickBindings.Validate(QuickGraph);
            if (number == 0 || !QuickAlive || DarkSightRequested || PointerRequested || GameSettings.Instance.IsPauseMenuOpen ||
                QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement)) return 0;
            if (QuickManualBusy)
            {
                if (QuickTacticalActionPending && !QuickTacticalCancelPending) SendQuickTactical(TacticalItemAction.Cancel);
                return 0;
            }
            CancelMeleeForSelection();
            m_QuickBindings.InitializeWeapons(QuickGraph);
            var item = m_QuickBindings.Select(number - 1, QuickGraph);
            if (item == null) { QuickNotice(m_QuickBindings.Reason); return 0; }
            RaidQuickSlotInput.ReleasePrimary();
            int slot = QuickSlotBindings.WeaponSlot(QuickGraph, item);
            if (slot == MeleeInventory.EquipmentIndex)
            {
                if (!SelectMelee(item)) QuickNotice("Melee weapon unavailable. Check equipment.");
                return 0;
            }
            if (slot > 0) SendMelee(MeleeAction.Equip, item.Id, slot);
            return slot;
        }
        private bool UseQuickSlot(bool pressed)
        {
            if (m_QuickBindings == null) return false;
            var graph = QuickGraph;
            var item = m_QuickBindings.SelectedItem(graph);
            bool tool = item != null && QuickSlotBindings.WeaponSlot(graph, item) == 0;
            bool releasedTool = m_QuickWasTool && !tool;
            m_QuickWasTool = tool;
            if (releasedTool) RaidQuickSlotInput.ReleasePrimary();
            if (!tool) return releasedTool;
            if (!pressed || !QuickAlive || DarkSightRequested || QuickBusy || PointerRequested || GameSettings.Instance.IsPauseMenuOpen ||
                QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement)) return true;
            if (TacticalItemRules.Find(item.Code) != null) SendQuickTactical(TacticalItemAction.Use, item.Id);
            else if (graph.MedicalAccessible(item)) UseMedical(item);
            else if (graph.ConsumableAccessible(item)) UseConsumable(item);
            else if (EnergyGrenadeRules.Accessible(graph, item))
            {
                if (Time.unscaledTime < m_NextGrenadeThrow) { QuickNotice("Item is cooling down."); return true; }
                m_InventoryRequestId = NextManualActionRequest();
                if (Send(new RaidGrenadeThrowRpc { RaidId = m_Snapshot.RaidId, ExpectedVersion = graph.Version, ItemId = item.Id, RequestId = m_InventoryRequestId }))
                {
                    m_GrenadeRequest = m_InventoryRequestId; m_InventoryRequestedAt = Time.unscaledTime;
                    m_InventoryRequestPending = true; m_NextGrenadeThrow = Time.unscaledTime + EnergyGrenadeRules.Cooldown;
                }
                else QuickNotice("Not connected. Item remains in its container.");
            }
            return true;
        }
        private void SendQuickTactical(TacticalItemAction action, string itemId = null, uint target = 0)
        {
            var graph = QuickGraph; if (graph == null) return;
            uint request = NextManualActionRequest();
            request = System.Math.Max(request, QuickOwner.RequestId + 1);
            m_InventoryRequestId = request;
            if (!Send(new RaidTacticalItemActionRpc { RaidId = m_Snapshot.RaidId, ExpectedVersion = graph.Version,
                RequestId = request, ItemId = itemId ?? "", TargetId = target, Action = action }))
            { QuickNotice("Not connected. Item remains in its container."); return; }
            m_QuickRequestedAction = action;
            m_QuickTacticalRequest = m_QuickTrackedTacticalRequest = request; m_QuickTacticalRequestedAt = Time.unscaledTime;
            m_InventoryRequestedAt = Time.unscaledTime; m_InventoryRequestPending = true;
            QuickNotice(action == TacticalItemAction.Cancel ? "Cancellation requested." : "Use requested. Waiting for server.");
        }
        private void PresentQuickSlots(bool active)
        {
            if (m_QuickView == null) return;
            m_QuickActive = active && QuickAlive;
            string scope = QuickPreferenceScope;
            if (m_QuickRaid != m_Snapshot.RaidId || m_QuickWorld != m_World || m_QuickProfileScope != scope)
            {
                m_QuickRaid = m_Snapshot.RaidId; m_QuickWorld = m_World; m_QuickProfileScope = scope;
                m_QuickTacticalRequest = m_QuickTrackedTacticalRequest = m_QuickOwnerRequest = 0;
                m_QuickOwnerError = InventoryError.None; m_QuickInteractionHintUntil = 0;
                m_QuickOwnerFeedback = TacticalItemFeedback.None; m_QuickStatusUntil = 0;
                m_QuickBindings.Deselect(); m_QuickWasTool = false;
                RaidQuickSlotInput.ReleasePrimary();
            }
            m_QuickBindings.UseProfile(scope);
            var graph = QuickGraph;
            if (m_QuickActive) m_QuickBindings.InitializeWeapons(graph);
            if (!m_QuickActive) m_QuickBindings.Deselect();
            m_QuickBindings.Validate(graph);
            var owner = QuickOwner;
            if (m_QuickTacticalRequest != 0 && owner.RequestId >= m_QuickTacticalRequest)
            {
                m_QuickTacticalRequest = 0;
                if (owner.Remaining <= 0 && m_InventoryRequestId == owner.RequestId && QuickInventory?.RequestId >= owner.RequestId)
                    m_InventoryRequestPending = false;
            }
            if (m_QuickTrackedTacticalRequest != 0 && owner.RequestId == m_QuickTrackedTacticalRequest &&
                (owner.RequestId != m_QuickOwnerRequest || owner.Feedback != m_QuickOwnerFeedback || owner.Error != m_QuickOwnerError))
            {
                m_QuickOwnerRequest = owner.RequestId; m_QuickOwnerFeedback = owner.Feedback; m_QuickOwnerError = owner.Error;
                if (owner.Feedback != TacticalItemFeedback.None) QuickNotice(QuickFeedback(owner.Feedback));
            }
            if (m_QuickTacticalRequest != 0 && Time.unscaledTime - m_QuickTacticalRequestedAt > 8)
            { m_QuickTacticalRequest = 0; QuickNotice("Waiting for the server. Retry after the connection recovers."); }
            bool menu = PointerRequested || GameSettings.Instance.IsPauseMenuOpen;
            if (QuickTacticalActionPending && !QuickTacticalCancelPending && (menu || !m_QuickActive || Keyboard.current?.xKey.wasPressedThisFrame == true && !QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement)))
                SendQuickTactical(TacticalItemAction.Cancel);
            var selected = m_QuickBindings.SelectedItem(graph);
            TacticalItemsFeedback.SetSelectedItem(m_QuickActive ? selected?.Code : null);
            TacticalItemsFeedback.SetPresentationBlocked(!m_QuickActive || menu || DarkSightRequested || QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement));
            m_QuickView.Element.style.display = m_Snapshot.RaidId > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            string status = !m_QuickActive ? "Item use unavailable while inactive or downed." : DarkSightRequested ? "DARK SIGHT / RELEASE V TO USE ITEMS" : QuickTacticalCancelPending ? "Cancellation requested." : owner.Remaining > 0 ?
                MoonkovLocalization.Format($"USING ITEM / {owner.Remaining:0.0}s · X CANCEL") : QuickManualBusy ? "Action in progress. X cancels timed actions." :
                Time.unscaledTime < m_QuickStatusUntil ? m_QuickStatus : Time.unscaledTime < m_QuickInteractionHintUntil ? m_QuickInteractionHint : null;
            string meleeStatus = MeleeCaption();
            if (meleeStatus != null && m_QuickActive && !DarkSightRequested && !QuickManualBusy) status = meleeStatus;
            if (status == null && !m_InventoryVisible) status = QuickSprintCaption(owner);
            m_QuickView.Element.EnableInClassList("quick-slots-exhausted", m_QuickActive && owner.SprintTracked && owner.SprintExhausted && !m_InventoryVisible);
            m_QuickView.Present(m_QuickBindings, graph, !m_QuickActive || QuickBusy || DarkSightRequested, m_InventoryVisible, status);
        }
        private static string QuickSprintCaption(in TacticalItemOwnerRpc owner)
        {
            string text = !owner.SprintTracked ? "" : owner.SprintExhausted
                ? MoonkovLocalization.Format($"SPRINT {Mathf.Clamp(owner.SprintStamina, 0, 100):0}% / FATIGUED / REST TO RECOVER")
                : MoonkovLocalization.Format($"SPRINT {Mathf.Clamp(owner.SprintStamina, 0, 100):0}% / 1–9 SELECT");
            if (owner.StaminaSeconds > 0)
                text += (text.Length > 0 ? " / " : "") + MoonkovLocalization.Format($"STAMINA BOOST {owner.StaminaSeconds:0}s");
            if (owner.AntidoteSeconds > 0)
                text += (text.Length > 0 ? " / " : "") + MoonkovLocalization.Format($"ANTIDOTE {owner.AntidoteSeconds:0}s");
            return text.Length > 0 ? text : null;
        }
        private bool TryQuickTacticalInteraction(bool active)
        {
            if (!active || !QuickAlive || QuickBusy || PointerRequested || GameSettings.Instance.IsPauseMenuOpen ||
                UnityEngine.Cursor.lockState != CursorLockMode.Locked || !Application.isFocused ||
                QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement)) return false;
            if (!TryGetLocalPlayerEntity(out var entity)) return false;
            var em = m_World.EntityManager;
            if (!em.HasComponent<GhostGameObjectLink>(entity)) return false;
            var root = em.GetComponentObject<GhostGameObjectLink>(entity).LinkedInstance;
            if (!root) return false;
            // Same capsule eye and controller direction as the server. Orbit camera
            // offsets must not change reach, line of sight, or the usable floor.
            var controller = root.GetComponent<PlayerGhost>()?.Controller?.CharacterController;
            Vector3 eye = root.transform.position + Vector3.up * Mathf.Max(.3f, (controller ? controller.height : 1.8f) - .2f);
            var health = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            Vector3 direction = Quaternion.Euler(health.ControllerState.PitchDegrees, health.ControllerState.YawDegrees, 0) * Vector3.forward;
            bool found = TacticalItemsFeedback.TryFindInteraction(eye, direction,
                out uint target, out TacticalItemAction action);
            if (!found && (QuickOwner.FireSeconds > 0 || QuickOwner.BleedSeconds > 0))
            { found = true; action = TacticalItemAction.Extinguish; }
            if (!found) return false;
            m_QuickInteractionHint = action == TacticalItemAction.Resupply ? "[E] RESUPPLY FROM AMMO BOX" :
                action == TacticalItemAction.Disarm ? "[E] DISARM TRAP" : "[E] STOP BURNING / BLEEDING";
            m_QuickInteractionHintUntil = Time.unscaledTime + .2f;
            bool pressed = Keyboard.current?.eKey.wasPressedThisFrame == true || Gamepad.current?.buttonWest.wasPressedThisFrame == true;
            if (!pressed) return false;
            SendQuickTactical(action, target: target); return true;
        }
        private static string QuickFeedback(TacticalItemFeedback feedback) => feedback switch
        {
            TacticalItemFeedback.Started => "Item action started. Keep still; X cancels.",
            TacticalItemFeedback.Completed => "Item action completed.",
            TacticalItemFeedback.Cancelled => "Cancellation processed. Check inventory.",
            TacticalItemFeedback.Interrupted => "Item action interrupted. Nothing consumed.",
            TacticalItemFeedback.NeedGround => "Aim at clear ground within reach.",
            TacticalItemFeedback.Limit => "Too many active objects. Remove a trap first.",
            TacticalItemFeedback.NoRoom => "No room for supplies.",
            TacticalItemFeedback.NoWound => "No wound needs treatment.",
            TacticalItemFeedback.NoTarget => "No usable target within reach.",
            TacticalItemFeedback.Stale => "Inventory changed. Try again.",
            TacticalItemFeedback.Poisoned => "Poison exposure detected.",
            _ => "Item action rejected. Check access and placement."
        };
        private void DisposeQuickSlots()
        {
            TacticalItemsFeedback.SetSelectedItem(null);
            TacticalItemsFeedback.SetPresentationBlocked(true);
            m_Root.UnregisterCallback<KeyDownEvent>(BindQuickKey, TrickleDown.TrickleDown);
            m_InventoryView.QuickSlotDrop = null; m_InventoryView.QuickSlotDragPreview = null;
            m_QuickView?.Dispose(); m_QuickView = null; m_QuickBindings = null; m_QuickWasTool = false;
        }
    }
}
