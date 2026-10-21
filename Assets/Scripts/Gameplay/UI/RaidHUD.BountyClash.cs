using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private EntityQuery m_ClashQuery;
        private bool m_ClashQueryCreated, m_ClashLobbyPointer, m_ClashActionsBlocked, m_ClashMode;
        private BountyClashView m_ClashView;
        private BountyClashClientState m_Clash;
        private float m_NextSenseLease, m_NextReviveLease;
        private int m_SenseFlags;
        private int m_ReviveTarget = -1;
        private Entity m_ClashCameraOwner;
        private Camera m_ClashCamera;
        public static bool GameplayActionsBlocked => PointerRequested || s_Active != null && s_Active.m_ClashActionsBlocked;
        public static bool DarkSightRequested => s_Active != null && s_Active.CanReadClashInput && Keyboard.current != null && Keyboard.current.vKey.isPressed;
        private bool CanReadClashInput => m_ClashMode && m_Clash is { Approved: true, HasSnapshot: true } &&
            m_Clash.Snapshot.Phase == ClashPhase.Active && m_Clash.Snapshot.Life == ClashLife.Alive && m_Clash.Snapshot.Connected &&
            !PointerRequested && !m_InventoryRequestPending && !TimedActionActive && TacticalItemsFeedback.LocalOwner.Remaining <= 0 &&
            !GameSettings.Instance.IsPauseMenuOpen && UnityEngine.Cursor.lockState == CursorLockMode.Locked &&
            !Client.QuickSlotBindings.Typing(m_Root.panel?.focusController.focusedElement as VisualElement);

        private void InitializeBountyClash()
        {
            m_ClashView = new BountyClashView(m_Root, (intent, value, invite) => SendClash(intent, value, invite), ReturnToShip);
            m_ClashLobbyPointer = false; m_ClashActionsBlocked = false; m_ClashMode = false; m_Clash = null; m_SenseFlags = 0; m_ReviveTarget = -1;
        }
        private bool PresentBountyClash()
        {
            m_ClashMode = BountyClashRuntime.Enabled(m_World.EntityManager);
            if (!m_ClashMode) { m_ClashView.Show(false); m_ClashLobbyPointer = m_ClashActionsBlocked = false; return false; }
            if (!m_ClashQueryCreated)
            { m_ClashQuery = m_World.EntityManager.CreateEntityQuery(typeof(BountyClashClientState)); m_ClashQueryCreated = true; }
            if (m_ClashQuery.IsEmptyIgnoreFilter) { m_ClashActionsBlocked = m_ClashLobbyPointer = true; return true; }
            m_Clash = m_World.EntityManager.GetComponentObject<BountyClashClientState>(m_ClashQuery.GetSingletonEntity());
            var snapshot = m_Clash.Snapshot;
            bool previouslyPointer = m_ClashLobbyPointer;
            m_ClashLobbyPointer = !m_Clash.Approved || !m_Clash.HasSnapshot || BountyClashRules.Ended(snapshot.Life) || snapshot.Phase == ClashPhase.Finished || snapshot.Phase == ClashPhase.Lobby ||
                snapshot.Phase == ClashPhase.Preparing || snapshot.Phase == ClashPhase.Deploying;
            m_ClashActionsBlocked = m_ClashLobbyPointer || snapshot.Life != ClashLife.Alive;
            if (m_ClashActionsBlocked && m_InventoryVisible) SetInventory(false);
            if (m_ClashLobbyPointer) Utils.SetCursorVisible(true);
            else if (previouslyPointer && !GameSettings.Instance.IsPauseMenuOpen && !m_InventoryVisible) Utils.SetCursorVisible(false);
            bool hasLocal = m_PlayerQuery.HasSingleton<LocalTransform>();
            if (hasLocal)
            {
                var owner = m_PlayerQuery.GetSingletonEntity();
                if (owner != m_ClashCameraOwner || !m_ClashCamera)
                {
                    m_ClashCameraOwner = owner; m_ClashCamera = null;
                    if (m_World.EntityManager.HasComponent<GhostGameObjectLink>(owner))
                    {
                        var link = m_World.EntityManager.GetComponentObject<GhostGameObjectLink>(owner);
                        if (link.LinkedInstance && link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
                            m_ClashCamera = presentation.OwnedCamera;
                    }
                }
            }
            else { m_ClashCameraOwner = Entity.Null; m_ClashCamera = null; }
            Vector3 position = hasLocal ? (Vector3)m_PlayerQuery.GetSingleton<LocalTransform>().Position : default;
            bool saving = m_Snapshot.SaveState == RaidSaveState.Saving || m_Snapshot.SaveState == RaidSaveState.Retrying;
            ReadClashInput(position, hasLocal);
            m_ClashView.Present(m_Clash, position, hasLocal, m_ClashCamera, saving, GameManager.CanUseMainMenu, DarkSightRequested,
                DarkSightRequested && Mouse.current != null && Mouse.current.rightButton.isPressed);
            BountyClashArenaSettings.Active?.PresentObjective(snapshot.Bounty == ClashBountyState.Carried || snapshot.Bounty == ClashBountyState.Extracted ? 1 : snapshot.BanishProgress,
                snapshot.Bounty == ClashBountyState.Ground, snapshot.BountyPosition);
            return true;
        }
        private bool SendClash(ClashIntent intent, int value = 0, string invite = null, int target = -1, bool track = true)
        {
            if (m_Clash == null || !m_Clash.Approved || !m_Clash.HasSnapshot || !m_Clash.Snapshot.Connected) return false;
            if (track && m_Clash.ActionPending(Time.realtimeSinceStartupAsDouble)) return false;
            var rpc = new ClashActionRpc
            {
                MatchId = m_Clash.Snapshot.MatchId, Epoch = m_Clash.Snapshot.Epoch,
                Intent = intent, Value = value, TargetParticipantId = target,
                RequestId = m_Clash.NextRequest = System.Math.Max(m_Clash.NextRequest, m_Clash.Snapshot.AcknowledgedRequest) + 1
            };
            if (!string.IsNullOrEmpty(invite)) rpc.InviteCode.CopyFromTruncated(invite);
            if (!Send(rpc)) return false;
            if (track) { m_Clash.PendingRequest = rpc.RequestId; m_Clash.RequestedAt = Time.realtimeSinceStartupAsDouble; }
            return true;
        }
        private void ReadClashInput(Vector3 position, bool hasLocal)
        {
            var s = m_Clash.Snapshot; var keyboard = Keyboard.current;
            bool allowed = hasLocal && CanReadClashInput;
            bool view = allowed && keyboard != null && keyboard.vKey.isPressed;
            bool boost = view && s.IsCarrier && s.SenseRemaining > 0 && Mouse.current != null && Mouse.current.rightButton.isPressed;
            int flags = view ? boost ? 3 : 1 : 0;
            if (!boost) m_Clash.ClearContacts();
            if (flags != m_SenseFlags || flags != 0 && Time.unscaledTime >= m_NextSenseLease)
            {
                SendClash(ClashIntent.SenseHeld, flags, track: false);
                m_SenseFlags = flags; m_NextSenseLease = Time.unscaledTime + .25f;
            }
            if (!allowed || view)
            {
                CancelClashRevive(); m_ClashView.SetAction(null); return;
            }
            int candidate = -1; float nearest = 3f, reviveProgress = 0;
            foreach (var member in m_Clash.Participants.Values)
            {
                if (!member.OwnTeam || member.TeamId != s.LocalTeamId || member.IsLocal || member.Life != ClashLife.Downed) continue;
                float distance = Vector3.Distance(position, member.Position);
                if (distance < nearest) { nearest = distance; candidate = member.ParticipantId; reviveProgress = member.ReviveProgress; }
            }
            bool held = keyboard != null && keyboard.fKey.isPressed;
            if (candidate >= 0 && held)
            {
                if (m_ReviveTarget != candidate)
                { CancelClashRevive(); if (SendClash(ClashIntent.ReviveBegin, target: candidate, track: false)) m_ReviveTarget = candidate; m_NextReviveLease = Time.unscaledTime + .25f; }
                else if (Time.unscaledTime >= m_NextReviveLease)
                { SendClash(ClashIntent.ReviveRenew, target: candidate, track: false); m_NextReviveLease = Time.unscaledTime + .25f; }
                m_ClashView.SetAction("[F] HOLD TO REVIVE / {0:0}%", reviveProgress * 100); return;
            }
            CancelClashRevive();
            if (candidate >= 0) { m_ClashView.SetAction("[F] HOLD TO REVIVE TEAMMATE"); return; }
            Vector3 objective = s.Bounty == ClashBountyState.Ground ? (Vector3)s.BountyPosition : (Vector3)s.TargetPosition;
            bool near = Vector3.Distance(position, objective) <= 3;
            string hint = near && s.Bounty == ClashBountyState.Unbanished ? "[F] BANISH DEAD TARGET" :
                near && s.Bounty == ClashBountyState.Ground ? "[F] PICK UP THE UNIQUE BOUNTY" : null;
            m_ClashView.SetAction(hint);
            if (hint != null && keyboard != null && keyboard.fKey.wasPressedThisFrame)
                SendClash(s.Bounty == ClashBountyState.Unbanished ? ClashIntent.Banish : ClashIntent.Pickup);
        }
        private void CancelClashRevive()
        { if (m_ReviveTarget >= 0) SendClash(ClashIntent.ReviveCancel, target: m_ReviveTarget, track: false); m_ReviveTarget = -1; }
        private void DisposeBountyClash()
        {
            m_ClashView?.Dispose(); m_ClashView = null;
            ResetBountyConnection();
        }
        private void HideBountyClash()
        {
            m_ClashView?.Show(false); m_Clash?.ClearContacts();
            m_ClashLobbyPointer = m_ClashActionsBlocked = false; m_SenseFlags = 0; m_ReviveTarget = -1;
        }
        private void ResetBountyConnection()
        {
            if (m_ClashQueryCreated && m_World != null && m_World.IsCreated) m_ClashQuery.Dispose();
            m_ClashQueryCreated = m_ClashLobbyPointer = m_ClashActionsBlocked = m_ClashMode = false; m_Clash = null;
            m_SenseFlags = 0; m_ReviveTarget = -1;
            m_ClashCameraOwner = Entity.Null; m_ClashCamera = null;
        }
    }
}
