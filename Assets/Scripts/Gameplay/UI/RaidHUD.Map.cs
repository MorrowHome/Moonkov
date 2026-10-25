using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private RaidMapView m_RaidMap;
        private bool m_MapOpen, m_MapContextValid, m_MapOpenedInContext;
        private int m_MapInputGuardFrame = -1, m_MapEscapeFrame = -1, m_MapToggleFrame = -1;
        private World m_MapWorld;
        private int m_MapRaid, m_MapEpoch;
        private string m_MapMatch;
        private bool MapPlayable => GameSettings.Instance.GameState == GlobalGameState.InGame && !GameSettings.Instance.IsPauseMenuOpen &&
            m_World != null && m_World.IsCreated && m_Snapshot.RaidId > 0 && m_Snapshot.Phase == RaidPhase.Active &&
            m_ConnectionQuery.HasSingleton<NetworkId>() && m_PlayerQuery.HasSingleton<PredictedPlayerGhost>() &&
            m_PlayerQuery.GetSingleton<PredictedPlayerGhost>().CurrentHealth > 0 &&
            (!m_ClashMode || m_Clash is { Approved: true, HasSnapshot: true } && m_Clash.Snapshot.Connected &&
                m_Clash.Snapshot.Phase == ClashPhase.Active && m_Clash.Snapshot.Life == ClashLife.Alive);
        private bool MapKeyPressed => Application.isFocused && Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame &&
            !Client.QuickSlotBindings.Typing(m_Root?.panel?.focusController.focusedElement as UnityEngine.UIElements.VisualElement);
        // Input delegates run before this HUD's LateUpdate. Reserve the opening M frame,
        // and the closing frame, so wheel/click/look cannot slip through before UI toggles.
        private bool MapBlocksInput => m_MapOpen || Time.frameCount <= m_MapInputGuardFrame || MapKeyPressed && GameSettings.Instance.GameState == GlobalGameState.InGame && !GameSettings.Instance.IsPauseMenuOpen && !QuickManualBusy;
        private static bool MapInputBlocked => s_Active != null && s_Active.MapBlocksInput;

        private void InitializeRaidMap()
        {
            m_MapOpen = m_MapContextValid = m_MapOpenedInContext = false; m_MapInputGuardFrame = m_MapEscapeFrame = m_MapToggleFrame = -1;
            m_RaidMap = new RaidMapView(m_Root, m_ClashView.Map, CloseRaidMapFromUI);
        }
        private void CloseRaidMapFromUI()
        { m_MapEscapeFrame = Time.frameCount; SetRaidMapOpen(false); }
        private void SetRaidMapOpen(bool open)
        {
            if (open == m_MapOpen) return;
            if (open)
            {
                if (!MapPlayable || QuickManualBusy) return;
                if (m_InventoryVisible) SetInventory(false);
                if (m_InventoryVisible) return;
                if (m_MissionDetailsVisible) SetMissionDetails(false);
                if (m_CampaignJournalVisible) SetCampaignJournal(false);
                if (!m_MapOpenedInContext)
                {
                    m_ClashView.Map.FullMap();
                    if (m_ClashView.Map.HasSurface) { m_ClashView.Map.ZoomBy(3); m_ClashView.Map.CenterOnPlayer(); }
                    m_MapOpenedInContext = true;
                }
                m_MapOpen = true; m_MapInputGuardFrame = Time.frameCount;
                m_Clash?.StopView(); CancelClashRevive(); BountyClashDarkSightRenderer.ClearImmediate();
                RaidQuickSlotInput.ReleasePrimary(); m_RaidMap.Open(); Utils.SetCursorVisible(true);
            }
            else
            {
                m_MapOpen = false; m_MapInputGuardFrame = Time.frameCount;
                m_RaidMap?.Hide(); RaidQuickSlotInput.ReleasePrimary();
                Utils.SetCursorVisible(m_InventoryVisible || m_MissionDetailsVisible || m_WasSettled || m_ClashLobbyPointer || GameSettings.Instance.IsPauseMenuOpen);
            }
        }
        private void UpdateRaidMap(bool clash)
        {
            if (m_RaidMap == null) return;
            if (!MapPlayable || !clash && !MoonRaidMap.Active)
            { ResetRaidMap(); return; }
            int epoch = clash ? m_Clash.Snapshot.Epoch : 0;
            string match = clash ? m_Clash.Snapshot.MatchId.ToString() : null;
            if (!m_MapContextValid || m_MapWorld != m_World || m_MapRaid != m_Snapshot.RaidId || m_MapEpoch != epoch || m_MapMatch != match)
            {
                SetRaidMapOpen(false); m_ClashView.Map.FullMap(); m_MapOpenedInContext = false;
                m_MapWorld = m_World; m_MapRaid = m_Snapshot.RaidId; m_MapEpoch = epoch; m_MapMatch = match; m_MapContextValid = true;
            }
            if (!clash)
            {
                var pose = m_PlayerQuery.GetSingleton<LocalTransform>();
                m_ClashView.Map.PresentExpedition(MoonRaidMap.Active, pose.Position, math.mul(pose.Rotation, math.forward()), m_Snapshot.TakenMask);
            }
            if (MapKeyPressed && m_MapToggleFrame != Time.frameCount)
            { m_MapToggleFrame = Time.frameCount; SetRaidMapOpen(!m_MapOpen); }
            if (m_MapOpen) Utils.SetCursorVisible(true);
            m_RaidMap.Present(!QuickManualBusy && !m_InventoryVisible && !m_MissionDetailsVisible || m_MapOpen, m_MapOpen, clash, clash ? m_Clash.Snapshot : default);
            if (clash) m_ClashView.SetMenuOpen(m_MapOpen || m_InventoryVisible || m_MissionDetailsVisible || GameSettings.Instance.IsPauseMenuOpen);
        }
        private void ResetRaidMap()
        {
            SetRaidMapOpen(false); m_RaidMap?.Hide();
            if (m_MapContextValid) m_ClashView?.Map.ResetData();
            m_MapContextValid = m_MapOpenedInContext = false; m_MapWorld = null; m_MapRaid = m_MapEpoch = 0; m_MapMatch = null;
        }
        private void DisposeRaidMap()
        { ResetRaidMap(); m_RaidMap?.Dispose(); m_RaidMap = null; }
    }
}
