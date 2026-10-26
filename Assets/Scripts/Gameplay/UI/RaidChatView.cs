using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    // Owns only presentation and editing. The client World owns messages and pending sends.
    public sealed class RaidChatView : IDisposable
    {
        public readonly VisualElement Element;
        public readonly TextField Input;
        private readonly ScrollView m_History;
        private readonly VisualElement m_Composer;
        private readonly Button m_Channel;
        private readonly Label m_Feedback;
        private readonly Action m_Send, m_Cancel;
        private RaidChatClientState m_State;
        private bool m_Open, m_Composing, m_PriorIme, m_ScrollToEnd;
        private int m_Revision = -1, m_ImeFrame = -1;
        private uint m_LastSequence;
        private float m_VisibleUntil, m_ErrorUntil;
        private ulong m_Session;
        private Keyboard m_Keyboard;
        private string m_Locale;
        public bool IsOpen => m_Open;
        public bool Composing => m_Composing || m_ImeFrame == Time.frameCount;
        public RaidChatView(VisualElement root, Action send, Action cancel)
        {
            Element = Resources.Load<VisualTreeAsset>("Moonkov/UI/RaidChat").CloneTree().Q("raidChat");
            Element.styleSheets.Add(Resources.Load<StyleSheet>("Moonkov/UI/RaidChat"));
            root.Add(Element);
            Input = Element.Q<TextField>("chatInput");
            m_History = Element.Q<ScrollView>("chatHistory");
            m_Composer = Element.Q("chatComposer");
            m_Channel = Element.Q<Button>("chatChannel"); m_Feedback = Element.Q<Label>("chatFeedback");
            Input.RegisterValueChangedCallback(Edited);
            Element.RegisterCallback<KeyDownEvent>(KeyDown, TrickleDown.TrickleDown);
            m_Channel.clicked += SwitchChannel;
            m_Send = send; m_Cancel = cancel;
            MoonkovLocalization.Bind(Element);
            SetOpen(false);
            Element.style.display = DisplayStyle.None;
        }
        public void Present(RaidChatClientState state, bool visible)
        {
            m_State = state;
            Element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible || state == null) return;
            if (m_Session != state.Session)
            {
                m_Session = state.Session; m_LastSequence = 0; m_VisibleUntil = m_ErrorUntil = 0;
                SetOpen(false); Input.SetValueWithoutNotify(state.Draft); m_Revision = -1;
            }
            string locale = MoonkovLocalization.Text("TEAM");
            if (state.LastSequence != m_LastSequence) { m_LastSequence = state.LastSequence; m_VisibleUntil = Time.unscaledTime + 10; }
            if (m_Revision != state.Revision || m_Locale != locale)
            {
                m_Revision = state.Revision; m_Locale = locale;
                RebuildHistory();
                MoonkovLocalization.Set(m_Channel, state.Channel == RaidChatChannel.Team ? "TEAM" : "CHAT ALL");
                m_Channel.EnableInClassList("chat-global", state.Channel == RaidChatChannel.Global);
                m_Channel.SetEnabled(state.PendingRequest == 0 && (state.TeamAvailable || state.Channel == RaidChatChannel.Team));
                bool inputWasEnabled = Input.enabledSelf;
                Input.SetEnabled(state.PendingRequest == 0 && (state.TeamAvailable || state.Channel == RaidChatChannel.Global));
                if (m_Open && Input.enabledSelf && !inputWasEnabled) Input.schedule.Execute(() => { if (m_Open) Input.Focus(); });
                if (state.PendingRequest == 0) Input.SetValueWithoutNotify(state.Draft);
                string error = FeedbackText(!state.TeamAvailable && state.Channel == RaidChatChannel.Team ? RaidChatResult.NoTeam : state.Feedback);
                if (!string.IsNullOrEmpty(error)) { MoonkovLocalization.Set(m_Feedback, error); m_ErrorUntil = Time.unscaledTime + 5; }
                else m_ErrorUntil = 0;
            }
            m_Feedback.style.display = m_Open && !state.TeamAvailable && state.Channel == RaidChatChannel.Team || Time.unscaledTime < m_ErrorUntil ? DisplayStyle.Flex : DisplayStyle.None;
            Element.EnableInClassList("chat-hidden", !m_Open && Time.unscaledTime >= Math.Max(m_VisibleUntil, m_ErrorUntil));
        }
        public void SetOpen(bool open)
        {
            bool changed = m_Open != open;
            m_Open = open;
            m_ScrollToEnd |= changed && open;
            Element.EnableInClassList("chat-open", open);
            m_Composer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            m_History.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
            m_History.Query<VisualElement>().ForEach(element => element.pickingMode = open ? PickingMode.Position : PickingMode.Ignore);
            m_History.contentContainer.pickingMode = PickingMode.Ignore;
            Element.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
            m_History.verticalScrollerVisibility = open ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            if (open && changed)
            {
                m_Keyboard = Keyboard.current;
                if (m_Keyboard != null)
                {
                    m_PriorIme = m_Keyboard.imeSelected.isPressed;
                    m_Keyboard.onIMECompositionChange += Composition;
                    m_Keyboard.SetIMEEnabled(true);
                }
                Input.SetValueWithoutNotify(m_State?.Draft ?? "");
                Input.Focus();
                Input.schedule.Execute(() => { if (m_Open) Input.Focus(); });
            }
            else if (!open)
            {
                if (m_Keyboard != null)
                { m_Keyboard.onIMECompositionChange -= Composition; m_Keyboard.SetIMEEnabled(m_PriorIme); m_Keyboard = null; }
                m_Composing = false; Input.Blur(); m_Channel.Blur();
            }
            m_Revision = -1;
            if (m_State != null) RebuildHistory();
        }
        private void Composition(IMECompositionString composition) { m_Composing = composition.Count > 0; m_ImeFrame = Time.frameCount; }
        private void Edited(ChangeEvent<string> evt) { if (m_State != null) m_State.Draft = evt.newValue; }
        private void KeyDown(KeyDownEvent evt)
        {
            if (!m_Open) return;
            bool ime = m_Composing || m_ImeFrame == Time.frameCount;
            if (evt.keyCode == KeyCode.Escape && !ime) { m_Cancel(); evt.StopImmediatePropagation(); }
            else if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !ime)
            { m_Send(); evt.PreventDefault(); evt.StopImmediatePropagation(); }
            // Tab keeps editing focus. It must never open the inventory.
            else if (evt.keyCode == KeyCode.Tab) { Input.Focus(); evt.PreventDefault(); evt.StopImmediatePropagation(); }
        }
        private void SwitchChannel()
        {
            if (m_State == null || m_State.PendingRequest != 0) return;
            m_State.Channel = !m_State.TeamAvailable || m_State.Channel == RaidChatChannel.Team ? RaidChatChannel.Global : RaidChatChannel.Team;
            m_State.Revision++; Input.Focus();
        }
        private void RebuildHistory()
        {
            if (m_State == null) return;
            bool atBottom = m_ScrollToEnd || m_History.verticalScroller.highValue - m_History.verticalScroller.value < 4;
            m_ScrollToEnd = false;
            float scroll = m_History.scrollOffset.y;
            m_History.Clear();
            int first = m_Open ? 0 : Math.Max(0, m_State.Messages.Count - 5);
            for (int i = first; i < m_State.Messages.Count; i++)
            {
                var message = m_State.Messages[i];
                string channel = MoonkovLocalization.Text(message.Channel == RaidChatChannel.Team ? "TEAM" : "CHAT ALL");
                string dead = message.Dead ? " [" + MoonkovLocalization.Text("DEAD") + "]" : "";
                var row = new Label("[" + channel + "] " + message.Name + dead + ": " + message.Text) { enableRichText = false, pickingMode = PickingMode.Ignore };
                row.AddToClassList("chat-row"); row.AddToClassList(message.Channel == RaidChatChannel.Team ? "chat-team" : "chat-global");
                m_History.Add(row);
            }
            m_History.schedule.Execute(() => m_History.scrollOffset = new Vector2(0, !m_Open || atBottom ? m_History.verticalScroller.highValue : scroll)).StartingIn(32);
        }
        public static string FeedbackText(RaidChatResult result) => result switch
        {
            RaidChatResult.NoTeam => "Join a team to use team chat.",
            RaidChatResult.Empty => "Enter a message.",
            RaidChatResult.TooLong => "Messages can contain up to 120 characters.",
            RaidChatResult.InvalidText => "This message contains invalid text.",
            RaidChatResult.RateLimited => "Sending too fast. Try again shortly.",
            RaidChatResult.Expired => "Message not confirmed. Your draft was kept.",
            RaidChatResult.Unavailable => "Chat is unavailable.",
            _ => ""
        };
        public void Dispose()
        {
            SetOpen(false); m_Channel.clicked -= SwitchChannel;
            Input.UnregisterValueChangedCallback(Edited);
            Element.UnregisterCallback<KeyDownEvent>(KeyDown, TrickleDown.TrickleDown);
            Element.RemoveFromHierarchy();
        }
    }
}
