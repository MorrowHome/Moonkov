using Unity.Entities;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private World m_MeleeWorld;
        private int m_MeleeRaid, m_MeleeEpoch;
        private uint m_MeleeNextRequest, m_MeleePendingRequest, m_MeleeTrackedRequest, m_MeleeObservedRequest;
        private byte m_MeleeObservedFeedback;
        private MeleeAction m_MeleePendingAction;
        private float m_MeleeRequestedAt, m_MeleeNoticeUntil;
        private string m_MeleeNotice;
        private bool m_MeleeInitialized;
        private bool ReadMelee(out PredictedPlayerGhost ghost)
        {
            ghost = default;
            if (m_World == null || !m_World.IsCreated || !m_PlayerQuery.HasSingleton<PredictedPlayerGhost>()) return false;
            ghost = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            return !m_ClashMode || m_Clash != null && m_Clash.HasSnapshot && ghost.MeleeEpoch == m_Clash.Snapshot.Epoch;
        }
        private bool MeleeTimedActionActive => m_MeleePendingRequest != 0 && m_MeleePendingAction != MeleeAction.Equip ||
            ReadMelee(out var ghost) && ghost.MeleePhase != (byte)MeleePhase.Idle;
        private bool MeleeBusy => m_MeleePendingRequest != 0 || ReadMelee(out var ghost) &&
            (ghost.MeleePhase != (byte)MeleePhase.Idle || ghost.MeleeRecoveryRemaining > .0001f);
        private bool MeleeNeedsCancellation => m_MeleePendingRequest != 0 &&
            (m_MeleePendingAction == MeleeAction.Light || m_MeleePendingAction == MeleeAction.Heavy) ||
            ReadMelee(out var ghost) && ghost.MeleePhase != (byte)MeleePhase.Idle;
        private bool MeleeCancelPending => m_MeleePendingRequest != 0 && m_MeleePendingAction == MeleeAction.Cancel;
        internal static bool RouteMeleeInput(bool light, bool heavy) => s_Active != null && s_Active.ReadMeleeInput(light, heavy);
        private void InitializeMelee() { m_MeleeInitialized = true; ResetMeleeClient(); }
        private void ResetMeleeClient()
        {
            m_MeleeNextRequest = m_MeleePendingRequest = m_MeleeTrackedRequest = m_MeleeObservedRequest = 0;
            m_MeleeObservedFeedback = 0; m_MeleeNotice = null; m_MeleeNoticeUntil = 0;
            m_MeleeRequestedAt = 0;
        }
        private void MeleeNotice(string text) { m_MeleeNotice = text; m_MeleeNoticeUntil = Time.unscaledTime + 2.5f; }
        private bool EnsureMeleeContext(in PredictedPlayerGhost ghost)
        {
            if (m_MeleeWorld == m_World && m_MeleeRaid == m_Snapshot.RaidId && m_MeleeEpoch == ghost.MeleeEpoch) return false;
            m_MeleeWorld = m_World; m_MeleeRaid = m_Snapshot.RaidId; m_MeleeEpoch = ghost.MeleeEpoch;
            ResetMeleeClient(); return true;
        }
        private bool SendMelee(MeleeAction action, string itemId = null, int equipmentSlot = 0)
        {
            if (!m_MeleeInitialized || !ReadMelee(out var ghost)) return false;
            EnsureMeleeContext(ghost);
            if (unchecked((int)(ghost.MeleeRequestId - m_MeleeNextRequest)) > 0) m_MeleeNextRequest = ghost.MeleeRequestId;
            unchecked { m_MeleeNextRequest++; if (m_MeleeNextRequest == 0) m_MeleeNextRequest++; }
            uint request = m_MeleeNextRequest;
            if (!Send(new RaidMeleeActionRpc { RaidId = m_Snapshot.RaidId, Epoch = ghost.MeleeEpoch,
                RequestId = request, ItemId = itemId ?? "", Action = action, EquipmentSlot = (byte)equipmentSlot }))
            { MeleeNotice("Melee unavailable. Waiting for connection."); return false; }
            m_MeleePendingAction = action; m_MeleePendingRequest = m_MeleeTrackedRequest = request;
            m_MeleeRequestedAt = Time.unscaledTime; return true;
        }
        private bool SelectMelee(InventoryItem item)
        {
            if (!QuickAlive || QuickManualBusy || !MeleeInventory.CanEquip(QuickGraph, item)) return false;
            return SendMelee(MeleeAction.Equip, item.Id, MeleeInventory.EquipmentIndex);
        }
        private void CancelMeleeForSelection()
        {
            if (MeleeNeedsCancellation && !MeleeCancelPending) SendMelee(MeleeAction.Cancel);
        }
        private bool ReadMeleeInput(bool light, bool heavy)
        {
            if (!m_MeleeInitialized) return false;
            var graph = QuickGraph; var selected = m_QuickBindings?.SelectedItem(graph);
            bool selectedMelee = MeleeInventory.CanEquip(graph, selected);
            if (!ReadMelee(out var ghost)) return selectedMelee;
            if (selected != null && !selectedMelee && QuickSlotBindings.WeaponSlot(graph, selected) == 0) return false;
            bool melee = ghost.InventoryWeapons && graph != null &&
                ((MeleeWeaponKind)ghost.MeleeWeapon != MeleeWeaponKind.None || ghost.EquippedWeaponID == DollSingerWeapons.None);
            if (!selectedMelee && !melee) return false;
            // Both buttons are consumed for the entire melee mode, including blocked frames.
            if ((!light && !heavy) || !QuickAlive || QuickManualBusy || DarkSightRequested || PointerRequested ||
                GameSettings.Instance.IsPauseMenuOpen || QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement)) return true;
            if (MeleeBusy) { MeleeNotice("Melee recovering. Wait before attacking."); return true; }
            if (selectedMelee && (ghost.EquippedWeaponSlot != MeleeInventory.EquipmentIndex || ghost.EquippedWeaponItem.ToString() != selected.Id))
            { MeleeNotice("Select the melee weapon again to equip it."); return true; }
            if (!melee) return true;
            SendMelee(heavy ? MeleeAction.Heavy : MeleeAction.Light, ghost.EquippedWeaponItem.ToString());
            return true;
        }
        private void PresentMelee(bool active)
        {
            if (!m_MeleeInitialized) return;
            if (!ReadMelee(out var ghost))
            {
                if (m_MeleeWorld != m_World || !active) ResetMeleeClient();
                m_MeleeWorld = m_World; return;
            }
            if (EnsureMeleeContext(ghost)) RaidQuickSlotInput.ReleasePrimary();
            if (m_MeleePendingRequest != 0 && (ghost.MeleeRequestId == m_MeleePendingRequest ||
                unchecked((int)(ghost.MeleeRequestId - m_MeleePendingRequest)) > 0)) m_MeleePendingRequest = 0;
            if (m_MeleeTrackedRequest != 0 && ghost.MeleeRequestId == m_MeleeTrackedRequest &&
                (ghost.MeleeRequestId != m_MeleeObservedRequest || ghost.MeleeFeedback != m_MeleeObservedFeedback))
            {
                m_MeleeObservedRequest = ghost.MeleeRequestId; m_MeleeObservedFeedback = ghost.MeleeFeedback;
                var feedback = (MeleeFeedback)ghost.MeleeFeedback;
                if (feedback != MeleeFeedback.None && feedback != MeleeFeedback.Accepted && feedback != MeleeFeedback.Equipped)
                    MeleeNotice(MeleeFeedbackText(feedback));
            }
            if (m_MeleePendingRequest != 0 && Time.unscaledTime - m_MeleeRequestedAt > 8)
            { m_MeleePendingRequest = 0; MeleeNotice("Melee unavailable. Waiting for connection."); }
            bool typing = QuickTyping(m_Root.panel?.focusController.focusedElement as VisualElement);
            if (MeleeNeedsCancellation && !MeleeCancelPending && (!active || !QuickAlive || PointerRequested ||
                GameSettings.Instance.IsPauseMenuOpen || DarkSightRequested || !typing && Keyboard.current?.xKey.wasPressedThisFrame == true))
                SendMelee(MeleeAction.Cancel);
        }
        private string MeleeCaption()
        {
            if (!m_MeleeInitialized || !ReadMelee(out var ghost) || !m_QuickActive || m_InventoryVisible) return null;
            if (m_MeleePendingRequest != 0) return m_MeleePendingAction == MeleeAction.Cancel ? "Melee cancellation requested." :
                m_MeleePendingAction == MeleeAction.Equip ? "Equipping melee weapon." : "Melee attack requested.";
            if (Time.unscaledTime < m_MeleeNoticeUntil) return m_MeleeNotice;
            var kind = (MeleeWeaponKind)ghost.MeleeWeapon;
            if (kind == MeleeWeaponKind.None || m_QuickBindings?.SelectedItem(QuickGraph) is { } selected &&
                QuickSlotBindings.WeaponSlot(QuickGraph, selected) == 0) return null;
            string name = kind == MeleeWeaponKind.Fists ? "FISTS" : InventoryCatalog.Get(kind == MeleeWeaponKind.Dagger
                ? MeleeInventory.DaggerCode : MeleeInventory.SwordCode)?.Name ?? "FISTS";
            string detail;
            if (ghost.MeleePhase != (byte)MeleePhase.Idle)
                detail = (MeleeAttackKind)ghost.MeleeAttack == MeleeAttackKind.Heavy
                    ? MoonkovLocalization.Format($"HEAVY ATTACK / {ghost.MeleeRecoveryRemaining:0.0}s / X CANCEL")
                    : MoonkovLocalization.Format($"LIGHT ATTACK / {ghost.MeleeRecoveryRemaining:0.0}s / X CANCEL");
            else if (ghost.MeleeRecoveryRemaining > .0001f)
                detail = MoonkovLocalization.Format($"MELEE RECOVERY / {ghost.MeleeRecoveryRemaining:0.0}s");
            else detail = MoonkovLocalization.Format($"MELEE {Mathf.Clamp(ghost.MeleeStamina, 0, 100):0}% / LMB LIGHT / RMB HEAVY");
            return MoonkovLocalization.Text(name) + " / " + detail;
        }
        private static string MeleeFeedbackText(MeleeFeedback feedback) => feedback switch
        {
            MeleeFeedback.Cancelled => "Melee interrupted. Recovery continues.",
            MeleeFeedback.Exhausted => "Not enough combat stamina. Rest before attacking.",
            MeleeFeedback.Busy => "Melee recovering. Wait before attacking.",
            MeleeFeedback.Inactive => "Melee unavailable while inactive or downed.",
            MeleeFeedback.Stale => "Melee state changed. Select the weapon again.",
            _ => "Melee weapon unavailable. Check equipment."
        };
        private void DisposeMelee()
        {
            if (MeleeNeedsCancellation && !MeleeCancelPending) SendMelee(MeleeAction.Cancel);
            ResetMeleeClient(); m_MeleeInitialized = false; m_MeleeWorld = null;
        }
    }
}
