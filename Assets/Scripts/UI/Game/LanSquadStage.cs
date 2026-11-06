using System;
using System.Linq;
using Unity.MP_FPS.Online;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed class LanSquadStage : IDisposable
    {
        public VisualElement Element { get; } = new VisualElement { name = "lanSquadStage" };
        public OnlineSquadStage Stage { get; }
        public int PageIndex { get; private set; }
        public int PageCount => Mathf.Max(1, (m_Members.Length + 3) / 4);
        private readonly VisualElement[] m_Cards = new VisualElement[4];
        private readonly Label[] m_Names = new Label[4], m_States = new Label[4];
        private readonly LobbyMember[] m_Slots = new LobbyMember[4];
        private readonly VisualElement m_Pager;
        private readonly Label m_PageLabel;
        private readonly Button m_Previous, m_Next;
        private LanMember[] m_Members = Array.Empty<LanMember>();
        private LanRoomSnapshot m_Room;
        private string m_LocalId, m_RoomId;
        private bool m_Visible;

        public LanSquadStage()
        {
            Element.AddToClassList("lan-squad-stage");
            var image = new Image { name = "lanStageImage", pickingMode = PickingMode.Ignore };
            image.AddToClassList("lan-stage-image"); Element.Add(image);
            Stage = new OnlineSquadStage(image);
            Stage.FramingChanged += AlignCards;
            var members = new VisualElement { name = "lanStageMembers" }; members.AddToClassList("lan-stage-members"); Element.Add(members);
            for (int i = 0; i < 4; i++)
            {
                var card = m_Cards[i] = new VisualElement { name = "lanStageMember" + i };
                card.AddToClassList("lan-stage-member");
                var name = m_Names[i] = new Label { enableRichText = false }; name.AddToClassList("lan-stage-name"); card.Add(name);
                var state = m_States[i] = new Label { enableRichText = false }; state.AddToClassList("lan-stage-state"); card.Add(state);
                members.Add(card);
            }
            m_Pager = new VisualElement { name = "lanStagePager" }; m_Pager.AddToClassList("lan-stage-pager"); Element.Add(m_Pager);
            m_Previous = new Button(() => SelectPage(PageIndex - 1)) { name = "lanPreviousSquad", text = "上一组" };
            m_PageLabel = new Label { name = "lanSquadPage" };
            m_Next = new Button(() => SelectPage(PageIndex + 1)) { name = "lanNextSquad", text = "下一组" };
            m_Pager.Add(m_Previous); m_Pager.Add(m_PageLabel); m_Pager.Add(m_Next);
        }

        public void Present(LanRoomSnapshot room, string localId, string localName, LobbyAppearance preview)
        {
            m_Room = room; m_LocalId = localId;
            m_Members = room?.Members?.OrderByDescending(m => m.Host).ToArray() ?? new[]
            { new LanMember { Id = "local", Name = localName, Appearance = preview ?? new LobbyAppearance() } };
            if (m_RoomId != room?.Id)
            {
                m_RoomId = room?.Id;
                int local = Array.FindIndex(m_Members, m => m.Id == localId);
                PageIndex = Mathf.Max(0, local) / 4;
            }
            SelectPage(PageIndex);
        }

        public void ShowMember(string id)
        {
            int index = Array.FindIndex(m_Members, m => m.Id == id);
            if (index >= 0) SelectPage(index / 4);
        }

        private void SelectPage(int page)
        {
            PageIndex = Mathf.Clamp(page, 0, PageCount - 1);
            Array.Clear(m_Slots, 0, 4);
            int[] order = { 1, 2, 0, 3 };
            for (int j = 0; j < 4; j++)
            {
                int index = PageIndex * 4 + j;
                if (index >= m_Members.Length) continue;
                var member = m_Members[index];
                m_Slots[order[j]] = new LobbyMember { PlayerId = member.Id, Name = member.Name,
                    Ready = member.Host || member.Ready, Appearance = member.Appearance ?? new LobbyAppearance() };
            }
            for (int i = 0; i < 4; i++)
            {
                var member = m_Slots[i];
                m_Names[i].text = member?.Name ?? "空位";
                var original = member == null ? null : m_Members.FirstOrDefault(m => m.Id == member.PlayerId);
                m_States[i].text = member == null ? "" : m_Room == null ? GameSettings.CharacterName(member.Appearance.CharacterIndex)
                    : original.InGame ? m_Room.Phase == LanRoomPhase.Preparing ? "加载中" : "战局中"
                    : (original.Host ? "房主 / " : "") + (member.Ready ? "已就绪" : "未准备");
                m_Cards[i].EnableInClassList("is-self", member != null && (m_Room == null || member.PlayerId == m_LocalId));
                m_Cards[i].EnableInClassList("is-ready", member?.Ready == true && m_Room?.Phase == LanRoomPhase.Lobby);
                m_Cards[i].EnableInClassList("is-empty", member == null);
            }
            m_Pager.style.display = PageCount > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            m_PageLabel.text = $"{PageIndex + 1} / {PageCount}";
            m_Previous.SetEnabled(PageIndex > 0); m_Next.SetEnabled(PageIndex + 1 < PageCount);
            if (m_Visible) Stage.Present(m_Slots);
            AlignCards();
        }

        private void AlignCards()
        {
            for (int i = 0; i < 4; i++)
                m_Cards[i].style.left = Length.Percent(Mathf.Clamp(Stage.SlotViewportX(i) * 100 - 11.5f, 0, 77));
        }
        public void SetVisible(bool visible)
        {
            m_Visible = visible;
            if (visible) Stage.Present(m_Slots); else Stage.Suspend();
        }
        public void Dispose() { Stage.FramingChanged -= AlignCards; Stage.Dispose(); Element.RemoveFromHierarchy(); }
    }
}
