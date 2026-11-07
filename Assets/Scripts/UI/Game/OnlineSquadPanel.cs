using System;
using System.Linq;
using Unity.MP_FPS.Online;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // The account service owns the party. This view only presents snapshots and
    // emits player intent; it can also be reviewed with isolated sample data.
    public sealed class OnlineSquadPanel : IDisposable
    {
        public VisualElement Element { get; }
        public OnlineSquadStage Stage { get; }
        private readonly Action<LobbyAction> m_Action;
        private readonly VisualElement[] m_Cards = new VisualElement[4];
        private readonly Label[] m_Names = new Label[4], m_Statuses = new Label[4];
        private readonly Button[] m_MemberActions = new Button[4];
        private readonly LobbyMember[] m_Slots = new LobbyMember[4];
        private LobbySnapshot m_Snapshot;
        private bool m_Disposed;

        public OnlineSquadPanel(Action<LobbyAction> action, Action back, Action retry, Action<int, int> appearanceChanged)
        {
            m_Action = action;
            Element = TerminalLayout.Clone("OnlineLobby", "onlineLobby");
            // Clone detaches this root from its UXML TemplateContainer.
            // Keep the screen's stylesheet on the root that actually gets mounted.
            var styles = Resources.Load<StyleSheet>("Moonkov/UI/OnlineSquadStyles");
            if (styles != null && !Element.styleSheets.Contains(styles)) Element.styleSheets.Add(styles);
            Stage = new OnlineSquadStage(Element.Q<Image>("onlineStage"));
            Stage.FramingChanged += AlignMembers;
            Element.RegisterCallback<GeometryChangedEvent>(e =>
                Element.EnableInClassList("online-compact", e.newRect.width < 1250 || e.newRect.height < 760));
            var members = Element.Q("onlineMembers");
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                var card = m_Cards[i] = new VisualElement { name = "onlineMember" + i };
                card.AddToClassList("online-member");
                var name = m_Names[i] = new Label(); name.AddToClassList("online-member-name"); card.Add(name);
                var status = m_Statuses[i] = new Label(); status.AddToClassList("online-member-status"); card.Add(status);
                var button = m_MemberActions[i] = new Button(() =>
                {
                    var member = m_Slots[slot];
                    if (member == null) CopyCode();
                    else m_Action?.Invoke(new LobbyAction { Action = "kick", TargetPlayerId = member.PlayerId });
                });
                button.AddToClassList("online-member-action"); card.Add(button); members.Add(card);
            }
            Element.Q<Button>("onlineBack").clicked += back;
            Element.RegisterCallback<KeyDownEvent>(e =>
            { if (e.keyCode == KeyCode.Escape) { back?.Invoke(); e.StopPropagation(); } });
            Element.Q<Button>("onlineCreate").clicked += () => Emit("create");
            Element.Q<Button>("onlineJoin").clicked += () => m_Action?.Invoke(new LobbyAction { Action = "join", Code = Element.Q<TextField>("onlineInvite").value });
            Element.Q<Button>("onlineCopy").clicked += CopyCode;
            Element.Q<Button>("onlineLeave").clicked += () => Emit("leave");
            Element.Q<Button>("onlineCancel").clicked += () => Emit("cancel");
            Element.Q<Button>("onlineStart").clicked += () => Emit("start");
            Element.Q<Button>("onlineReady").clicked += () => m_Action?.Invoke(new LobbyAction
            { Action = "ready", Ready = !(m_Snapshot?.Members.FirstOrDefault(m => m.PlayerId == m_Snapshot.SelfId)?.Ready ?? false) });
            Element.Q<Button>("onlineRetry").clicked += retry;
            Element.Q<Button>("onlineReturn").clicked += () => Emit("return");
            void Settings() => m_Action?.Invoke(new LobbyAction { Action = "settings",
                Mode = Element.Q<RadioButtonGroup>("onlineMode").value == 0 ? "Coop" : "Versus",
                ConnectionPath = Element.Q<RadioButtonGroup>("onlineConnection").value == 0 ? OnlineNetworkProtocol.P2P : OnlineNetworkProtocol.Vps,
                PublicQueue = Element.Q<Toggle>("onlinePublic").value });
            Element.Q<RadioButtonGroup>("onlineMode").RegisterValueChangedCallback(_ => Settings());
            Element.Q<RadioButtonGroup>("onlineConnection").RegisterValueChangedCallback(_ => Settings());
            Element.Q<Toggle>("onlinePublic").RegisterValueChangedCallback(_ => Settings());
            Element.Q<RadioButtonGroup>("onlineTeam").RegisterValueChangedCallback(e => m_Action?.Invoke(new LobbyAction { Action = "team", Team = e.newValue }));
            void Appearance() => appearanceChanged?.Invoke(Element.Q<DropdownField>("onlineCharacter").index + 2,
                Element.Q<DropdownField>("onlinePose").index - 1);
            Element.Q<DropdownField>("onlineCharacter").RegisterValueChangedCallback(_ => Appearance());
            Element.Q<DropdownField>("onlinePose").RegisterValueChangedCallback(_ => Appearance());
            var motion = Element.Q<Toggle>("onlineReduceMotion");
            motion.SetValueWithoutNotify(PlayerPrefs.GetInt("Moonkov.SquadReducedMotion", OnlineSquadStage.SystemReducedMotion ? 1 : 0) != 0);
            SetReducedMotion(motion.value);
            motion.RegisterValueChangedCallback(e => { SetReducedMotion(e.newValue); PlayerPrefs.SetInt("Moonkov.SquadReducedMotion", e.newValue ? 1 : 0); });
            // Clone already bound the static UXML. Member names are player data;
            // leave those newly created labels out of the translation bindings.
        }

        private void Emit(string action) => m_Action?.Invoke(new LobbyAction { Action = action });
        private void CopyCode() { if (!string.IsNullOrEmpty(m_Snapshot?.Code)) GUIUtility.systemCopyBuffer = m_Snapshot.Code; }
        public void SetVisible(bool visible)
        { Element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; if (!visible) Stage.Suspend(); }
        public void SetReducedMotion(bool reduced)
        { Stage.ReducedMotion = reduced; Element.EnableInClassList("online-reduced-motion", reduced); }
        public void FocusPrimary()
        {
            string name = string.IsNullOrEmpty(m_Snapshot?.Code) ? "onlineCreate" : m_Snapshot.Phase == "Lobby" ? "onlineReady" : "onlineBack";
            var button = Element.Q<Button>(name);
            if (button.enabledInHierarchy) button.Focus();
        }

        public void Present(LobbySnapshot snapshot, LobbyAppearance self, string displayName, string error, bool busy, bool reconnect)
        {
            m_Snapshot = snapshot;
            bool party = snapshot != null && snapshot.Phase != "None" && !string.IsNullOrEmpty(snapshot.Code);
            bool leader = party && snapshot.SelfId == snapshot.LeaderId;
            bool lobby = party && snapshot.Phase == "Lobby";
            var me = party ? snapshot.Members.FirstOrDefault(m => m.PlayerId == snapshot.SelfId) : null;
            bool pending = lobby && me != null && !self.SameAs(me.Appearance);
            var character = Element.Q<DropdownField>("onlineCharacter");
            character.SetValueWithoutNotify(character.choices[Mathf.Clamp(self.CharacterIndex - 2, 0, 2)]);
            var pose = Element.Q<DropdownField>("onlinePose");
            pose.SetValueWithoutNotify(pose.choices[Mathf.Clamp(self.PoseIndex + 1, 0, 4)]);
            Element.Q(className: "online-personal").SetEnabled((!party || lobby) && !busy);
            Element.Q("onlineEntry").style.display = party ? DisplayStyle.None : DisplayStyle.Flex;
            Element.Q("onlineParty").style.display = party ? DisplayStyle.Flex : DisplayStyle.None;
            Element.Q("onlineEntry").SetEnabled(!busy);
            var copy = Element.Q<Button>("onlineCopy"); copy.style.display = party ? DisplayStyle.Flex : DisplayStyle.None;
            MoonkovLocalization.Set(copy, party ? "队伍码 {0}" : "", snapshot?.Code ?? ""); copy.tooltip = "复制队伍码";
            MoonkovLocalization.Set(Element.Q<Label>("onlineCount"), "{0} / 4", party ? snapshot.Members.Length : 1);
            Array.Clear(m_Slots, 0, m_Slots.Length);
            if (party)
            {
                var captain = snapshot.Members.FirstOrDefault(m => m.PlayerId == snapshot.LeaderId) ?? snapshot.Members.FirstOrDefault();
                m_Slots[1] = captain;
                int next = 0; int[] order = { 2, 0, 3 };
                foreach (var member in snapshot.Members)
                    if (member != captain && next < order.Length) m_Slots[order[next++]] = member;
            }
            else m_Slots[1] = new LobbyMember { PlayerId = "local", Name = displayName, Appearance = self };
            for (int i = 0; i < 4; i++)
            {
                var member = m_Slots[i];
                bool own = member != null && (!party || member.PlayerId == snapshot.SelfId);
                m_Cards[i].EnableInClassList("is-self", own);
                m_Cards[i].EnableInClassList("is-ready", member?.Ready == true && member.Connected);
                m_Cards[i].EnableInClassList("is-offline", member != null && !member.Connected);
                m_Names[i].text = member == null ? "空位" : member.Name;
                m_Statuses[i].text = member == null ? "" : !party ? GameSettings.CharacterName(self.CharacterIndex)
                    : member.Returned ? "已返回" : snapshot.Phase == "Starting" ? "加载中" : snapshot.Phase == "Ready" ? "已出发"
                    : !member.Connected ? "连接中断 / " + member.ReconnectSeconds + " 秒"
                    : (member.PlayerId == snapshot.LeaderId ? "队长 / " : "") + (member.Ready ? "已准备" : "未准备") +
                        (snapshot.Mode == "Versus" && !snapshot.PublicQueue ? member.Team == 0 ? " / A 队" : " / B 队" : "");
                var button = m_MemberActions[i];
                button.text = member == null ? "邀请" : "移除";
                button.style.display = lobby && (member == null || leader && !own) ? DisplayStyle.Flex : DisplayStyle.None;
                button.SetEnabled(!busy);
            }
            Stage.Present(m_Slots);
            AlignMembers();
            string message = error ?? "";
            if (party)
            {
                Element.Q("onlineSettings").SetEnabled(lobby && leader && !busy);
                Element.Q<RadioButtonGroup>("onlineMode").SetValueWithoutNotify(snapshot.Mode == "Coop" ? 0 : 1);
                Element.Q<RadioButtonGroup>("onlineConnection").SetValueWithoutNotify(snapshot.ConnectionPath == OnlineNetworkProtocol.Vps ? 1 : 0);
                Element.Q<Toggle>("onlinePublic").SetValueWithoutNotify(snapshot.PublicQueue);
                var team = Element.Q<RadioButtonGroup>("onlineTeam");
                team.style.display = snapshot.Mode == "Versus" && !snapshot.PublicQueue ? DisplayStyle.Flex : DisplayStyle.None;
                team.SetEnabled(lobby && !busy); team.SetValueWithoutNotify(me?.Team ?? 0);
                var ready = Element.Q<Button>("onlineReady"); MoonkovLocalization.Set(ready, me?.Ready == true ? "取消准备" : "准备");
                ready.SetEnabled(lobby && !busy && !pending);
                bool validTeams = snapshot.Mode != "Versus" || snapshot.PublicQueue || snapshot.Members.Select(m => m.Team).Distinct().Count() >= 2;
                var start = Element.Q<Button>("onlineStart"); start.style.display = leader ? DisplayStyle.Flex : DisplayStyle.None;
                MoonkovLocalization.Set(start, snapshot.PublicQueue && snapshot.Mode == "Versus" ? "寻找对手" : "出发");
                start.SetEnabled(lobby && !busy && !pending && validTeams && snapshot.Members.All(m => m.Ready && m.Connected));
                Element.Q<Button>("onlineLeave").SetEnabled(lobby && !busy);
                Element.Q<Button>("onlineCancel").style.display = leader && snapshot.Phase == "Queued" ? DisplayStyle.Flex : DisplayStyle.None;
                var retry = Element.Q<Button>("onlineRetry");
                retry.style.display = snapshot.Phase == "Ready" && me?.Returned != true && !reconnect ? DisplayStyle.Flex : DisplayStyle.None;
                retry.SetEnabled(!busy);
                var returned = Element.Q<Button>("onlineReturn");
                returned.style.display = snapshot.Phase == "Ready" && me?.Returned != true ? DisplayStyle.Flex : DisplayStyle.None;
                returned.SetEnabled(!busy);
                Element.Q<Button>("onlineCancel").SetEnabled(!busy);
                if (string.IsNullOrEmpty(message)) message = pending ? "正在同步出战配置。" : !validTeams ? "请分别加入 A、B 两队。"
                    : snapshot.Phase == "Queued" ? snapshot.PublicQueue && snapshot.Mode == "Versus" ? "正在寻找其他队伍。" : "等待空闲服务器。"
                    : snapshot.Phase == "Starting" ? "正在加载本局地图。"
                    : snapshot.Phase == "Ready" ? me?.Returned == true ? "等待队友结算。" : "战局已就绪。"
                    : snapshot.Message ?? "";
            }
            var statusLabel = Element.Q<Label>("onlineStatus"); MoonkovLocalization.Set(statusLabel, message);
            statusLabel.EnableInClassList("is-error", !string.IsNullOrEmpty(error));
        }

        private void AlignMembers()
        {
            for (int i = 0; i < 4; i++)
                m_Cards[i].style.left = new Length(Mathf.Clamp(Stage.SlotViewportX(i) * 100 - 10.5f, 0, 79), LengthUnit.Percent);
        }
        public void Dispose()
        { if (m_Disposed) return; m_Disposed = true; Stage.FramingChanged -= AlignMembers; Stage.Dispose(); Element.RemoveFromHierarchy(); }
    }
}
