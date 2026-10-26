using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private RaidChatView m_ChatView;
        private RaidChatClientState m_ChatState;
        private World m_ChatWorld;
        private EntityQuery m_ChatQuery;
        private int m_ChatGuardFrame = -1;
        private ulong m_ChatSession;
        private bool ChatOpenPressed => Application.isFocused && Keyboard.current != null &&
            (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame) && ChatCanOpen;
        private bool ChatCanOpen => m_ChatState != null && m_ChatState.Session != 0 &&
            GameSettings.Instance.GameState == GlobalGameState.InGame && !GameSettings.Instance.IsPauseMenuOpen &&
            !InventoryOpen && !m_MapOpen && !(m_Cinematic?.HidesHUD ?? false) &&
            !Client.QuickSlotBindings.Typing(m_Root?.panel?.focusController.focusedElement as VisualElement);
        private bool ChatBlocksInput => m_ChatView != null && m_ChatView.IsOpen || Time.frameCount <= m_ChatGuardFrame || ChatOpenPressed;
        public static bool ChatInputBlocked => s_Active != null && s_Active.ChatBlocksInput;
        private void InitializeChat() => m_ChatView = new RaidChatView(m_Root, SendChat, CloseChat);
        private bool CloseChatForEscape()
        {
            if (m_ChatView == null || !m_ChatView.IsOpen && Time.frameCount > m_ChatGuardFrame) return false;
            // The text field handles IME cancellation first; the pause action must still be consumed.
            if (!m_ChatView.Composing) CloseChat();
            return true;
        }
        private void CloseChat()
        {
            m_ChatView?.SetOpen(false); m_ChatGuardFrame = Time.frameCount + 1;
            if (GameSettings.Instance != null) Utils.SetCursorVisible(InventoryOpen || m_MapOpen || m_WasSettled || m_ClashLobbyPointer || CinematicInputBlocked || GameSettings.Instance.IsPauseMenuOpen);
        }
        private void UpdateChat()
        {
            if (m_ChatWorld != m_World)
            {
                if (m_ChatWorld != null && m_ChatWorld.IsCreated) m_ChatQuery.Dispose();
                m_ChatWorld = m_World; m_ChatState = null; m_ChatSession = 0; CloseChat();
                if (m_ChatWorld != null && m_ChatWorld.IsCreated)
                    m_ChatQuery = m_ChatWorld.EntityManager.CreateEntityQuery(typeof(RaidChatClientState));
            }
            if (m_ChatWorld != null && m_ChatWorld.IsCreated && m_ChatQuery.HasSingleton<RaidChatClientState>())
                m_ChatState = m_ChatWorld.EntityManager.GetComponentObject<RaidChatClientState>(m_ChatQuery.GetSingletonEntity());
            bool visible = m_ChatState != null && m_ChatState.Session != 0 && m_World != null && m_World.IsCreated &&
                m_ConnectionQuery.HasSingleton<NetworkId>() && GameSettings.Instance.GameState == GlobalGameState.InGame;
            if (!visible || m_ChatSession != m_ChatState.Session || GameSettings.Instance.IsPauseMenuOpen || (m_Cinematic?.HidesHUD ?? false))
            {
                if (m_ChatView.IsOpen) CloseChat();
                m_ChatSession = m_ChatState?.Session ?? 0;
            }
            m_ChatView.Present(m_ChatState, visible && !(m_Cinematic?.HidesHUD ?? false));
            if (visible && !m_ChatView.IsOpen && ChatOpenPressed && Time.frameCount > m_ChatGuardFrame)
            {
                m_ChatView.SetOpen(true); m_ChatGuardFrame = Time.frameCount + 1;
                RaidQuickSlotInput.ReleasePrimary(); CancelMissionHold(); CancelClashRevive();
                m_Clash?.StopView(); BountyClashDarkSightRenderer.ClearImmediate();
                Utils.SetCursorVisible(true);
            }
            if (m_ChatView.IsOpen) Utils.SetCursorVisible(true);
        }
        private void SendChat()
        {
            var data = m_ChatState;
            if (data == null || data.PendingRequest != 0 || m_World == null || !m_World.IsCreated || !m_ConnectionQuery.HasSingleton<NetworkId>()) return;
            var result = RaidChatRules.Normalize(data.Draft, out string text);
            if (data.Channel == RaidChatChannel.Team && !data.TeamAvailable) result = RaidChatResult.NoTeam;
            if (result != RaidChatResult.Accepted) { data.Feedback = result; data.Revision++; return; }
            uint request = ++data.NextRequest;
            if (request == 0) request = ++data.NextRequest;
            var manager = m_World.EntityManager; var entity = manager.CreateEntity();
            manager.AddComponentData(entity, new RaidChatRequestRpc { Session = data.Session, RequestId = request, Channel = data.Channel, Text = new FixedString512Bytes(text) });
            manager.AddComponentData(entity, new SendRpcCommandRequest { TargetConnection = m_ConnectionQuery.GetSingletonEntity() });
            data.PendingRequest = request; data.PendingAt = m_World.Time.ElapsedTime; data.Feedback = RaidChatResult.Ready; data.Revision++;
            CloseChat();
        }
        private void ResetChat()
        {
            if (m_ChatView.IsOpen) CloseChat();
            m_ChatView.Present(null, false); m_ChatSession = 0;
        }
        private void DisposeChat()
        {
            m_ChatView?.Dispose(); m_ChatView = null;
            if (m_ChatWorld != null && m_ChatWorld.IsCreated) m_ChatQuery.Dispose();
            m_ChatWorld = null; m_ChatState = null; m_ChatGuardFrame = -1;
        }
    }
}
