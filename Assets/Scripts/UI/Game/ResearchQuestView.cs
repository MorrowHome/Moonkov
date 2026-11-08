using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS.Client
{
    public sealed class ResearchQuestView : IDisposable
    {
        private static readonly RectInt[] s_WideTiles =
        {
            new RectInt(0, 0, 5, 4), new RectInt(5, 0, 3, 2), new RectInt(5, 2, 3, 2), new RectInt(8, 0, 4, 4),
            new RectInt(0, 4, 3, 4), new RectInt(3, 4, 5, 4), new RectInt(8, 4, 4, 2), new RectInt(8, 6, 4, 2)
        };
        private static readonly RectInt[] s_NarrowTiles =
        {
            new RectInt(0, 0, 5, 3), new RectInt(5, 0, 3, 3), new RectInt(0, 3, 3, 3), new RectInt(3, 3, 5, 3),
            new RectInt(0, 6, 3, 3), new RectInt(3, 6, 5, 3), new RectInt(0, 9, 4, 3), new RectInt(4, 9, 4, 3)
        };

        private readonly Func<ResearchQuestProgress> m_Read;
        private readonly Func<ResearchQuest, bool, Task<bool>> m_Advance;
        private readonly Action m_Prepare, m_Close;
        private readonly ResearchQuestRadio m_Radio = new ResearchQuestRadio();
        private readonly VisualElement m_Viewport, m_Wall, m_Detail, m_Poster;
        private readonly Label m_Heading, m_Count, m_Number, m_Title, m_Status, m_Goal, m_Brief, m_LockedBy, m_Feedback;
        private readonly Button m_Back, m_Legacy, m_Action, m_Play;
        private readonly VisualElement[] m_Cells = new VisualElement[ResearchQuestRules.Count];
        private readonly Button[] m_Cards = new Button[ResearchQuestRules.Count];
        private readonly Label[] m_CardTitles = new Label[ResearchQuestRules.Count];
        private readonly Label[] m_CardStates = new Label[ResearchQuestRules.Count];
        private readonly IVisualElementScheduledItem m_Motion, m_Refresh;
        private ResearchQuest? m_Selected;
        private Vector2 m_Offset, m_Target;
        private string m_Signature;
        private bool m_Busy, m_Error, m_Disposed, m_ReduceMotion;

        public VisualElement Element { get; }

        public ResearchQuestView(VisualElement host, Func<ResearchQuestProgress> read, Func<ResearchQuest, bool, Task<bool>> advance,
            Action prepare, Action legacy, Action close)
        {
            m_Read = read; m_Advance = advance; m_Prepare = prepare; m_Close = close;
            Element = new VisualElement { name = "researchQuestScreen" };
            Element.AddToClassList("research-quest");
            Element.AddToClassList("moonkov-style-font");
            var stylesheet = Resources.Load<StyleSheet>("Moonkov/UI/ResearchQuests");
            if (stylesheet) Element.styleSheets.Add(stylesheet);
            var header = Box(Element, "research-quest-header");
            m_Back = Button(header, "research-quest-back", Back);
            m_Back.name = "questBack";
            m_Heading = Label(header, "research-quest-heading");
            m_Count = Label(header, "research-quest-count");
            m_Legacy = Button(header, "research-quest-legacy", legacy);
            m_Viewport = Box(Element, "research-quest-viewport");
            m_Viewport.name = "questWall";
            m_Wall = Box(m_Viewport, "research-quest-wall");
            for (int i = 0; i < m_Cards.Length; i++) BuildCard(i);
            m_Detail = Box(Element, "research-quest-detail");
            m_Detail.name = "questDetail";
            m_Poster = Box(m_Detail, "research-quest-poster");
            var info = Box(m_Detail, "research-quest-info");
            var text = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            text.AddToClassList("research-quest-text"); info.Add(text);
            var line = Box(text, "research-quest-line");
            m_Number = Label(line, "research-quest-number");
            m_Status = Label(line, "research-quest-status");
            m_Title = Label(text, "research-quest-title");
            m_Goal = Label(text, "research-quest-goal");
            m_Brief = Label(text, "research-quest-copy");
            m_LockedBy = Label(text, "research-quest-copy");
            m_Feedback = Label(info, "research-quest-feedback");
            var actions = Box(info, "research-quest-actions");
            m_Play = Button(actions, "research-quest-listen", ToggleRadio);
            m_Play.name = "questListen";
            m_Action = Button(actions, "research-quest-primary", Advance);
            m_Action.name = "questAction";
            m_Motion = Element.schedule.Execute(AdvanceMotion).Every(16);
            m_Motion.Pause();
            m_Refresh = Element.schedule.Execute(Refresh).Every(200);
            Element.RegisterCallback<GeometryChangedEvent>(Layout);
            Element.RegisterCallback<NavigationCancelEvent>(evt => { Back(); evt.StopPropagation(); });
            Element.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Back(); evt.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);
            m_Viewport.RegisterCallback<PointerMoveEvent>(Move);
            m_Viewport.RegisterCallback<PointerLeaveEvent>(_ => MoveTo(Vector2.zero));
            Element.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            host.Add(Element);
            MoonkovAudio.BindUI(Element);
            SetLayout(false);
            Refresh();
            m_Cards[0].Focus();
        }

        private void BuildCard(int index)
        {
            var quest = (ResearchQuest)index;
            var entry = For(quest);
            var cell = Box(m_Wall, "research-quest-cell");
            var card = Button(cell, "research-quest-card", () => Open(quest));
            card.name = "questCard" + index;
            card.EnableInClassList("research-quest-paper", entry.Light);
            card.Add(Artwork(quest));
            var badge = Label(card, "research-quest-card-number", (index + 1).ToString("00"));
            badge.pickingMode = PickingMode.Ignore;
            var copy = Box(card, "research-quest-card-copy");
            copy.pickingMode = PickingMode.Ignore;
            m_CardTitles[index] = Label(copy, "research-quest-card-title");
            m_CardStates[index] = Label(copy, "research-quest-card-state");
            m_Cards[index] = card;
            m_Cells[index] = cell;
        }

        private static VisualElement Artwork(ResearchQuest quest)
        {
            var entry = For(quest);
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("research-quest-art");
            var image = new Image { image = Resources.Load<Texture2D>("Moonkov/UI/" + entry.Art), scaleMode = ScaleMode.ScaleAndCrop,
                pickingMode = PickingMode.Ignore };
            image.AddToClassList("research-quest-art-image");
            art.Add(image);
            if (entry.Item != null)
            {
                image.AddToClassList("research-quest-art-behind-prop");
                var prop = new Image { image = PhysicalItemVisuals.Icon(entry.Item), scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                prop.AddToClassList("research-quest-prop");
                art.Add(prop);
            }
            return art;
        }

        private void Layout(GeometryChangedEvent evt)
        {
            if (evt.newRect.width <= 0 || evt.newRect.height <= 0) return;
            SetLayout(evt.newRect.width < 850);
            Element.EnableInClassList("research-quest-short", evt.newRect.height < 600);
        }

        private void SetLayout(bool narrow)
        {
            Element.EnableInClassList("research-quest-narrow", narrow);
            var layout = narrow ? s_NarrowTiles : s_WideTiles;
            float x = 100f / (narrow ? 8 : 12), y = 100f / (narrow ? 12 : 8);
            for (int i = 0; i < layout.Length; i++)
            {
                var rect = layout[i]; var style = m_Cells[i].style;
                style.left = Length.Percent(rect.x * x); style.top = Length.Percent(rect.y * y);
                style.width = Length.Percent(rect.width * x); style.height = Length.Percent(rect.height * y);
            }
        }

        private void Move(PointerMoveEvent evt)
        {
            if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse || m_Viewport.layout.width < 1 || m_Viewport.layout.height < 1) return;
            var point = m_Viewport.WorldToLocal((Vector2)evt.position);
            var rect = m_Viewport.contentRect;
            MoveTo(new Vector2((.5f - point.x / rect.width) * 22, (.5f - point.y / rect.height) * 16));
        }

        private void MoveTo(Vector2 target)
        {
            if (m_ReduceMotion) return;
            m_Target = target;
            m_Motion.Resume();
        }

        private void AdvanceMotion()
        {
            m_Offset = Vector2.Lerp(m_Offset, m_Target, .16f);
            if ((m_Offset - m_Target).sqrMagnitude < .02f) { m_Offset = m_Target; m_Motion.Pause(); }
            m_Wall.style.translate = new Translate(m_Offset.x, m_Offset.y);
        }

        private void Open(ResearchQuest quest)
        {
            m_Selected = quest; m_Error = false;
            m_Poster.Clear(); m_Poster.Add(Artwork(quest));
            m_Viewport.style.display = DisplayStyle.None;
            m_Detail.style.display = DisplayStyle.Flex;
            MoveTo(Vector2.zero);
            Refresh();
            if (ResearchQuestRules.Status(m_Read(), quest) == ResearchQuestStatus.Available) m_Radio.Play(quest, false, Chinese);
            if (!m_ReduceMotion) TerminalMotion.Fade(m_Detail, 0, .25f);
            m_Back.Focus();
        }

        private void Back()
        {
            if (!m_Selected.HasValue) { m_Close?.Invoke(); return; }
            int card = (int)m_Selected.Value;
            m_Selected = null; m_Error = false;
            m_Radio.Stop();
            m_Viewport.style.display = DisplayStyle.Flex;
            m_Detail.style.display = DisplayStyle.None;
            Refresh();
            m_Cards[card].Focus();
        }

        private void ToggleRadio()
        {
            if (!m_Selected.HasValue) return;
            var quest = m_Selected.Value;
            if (m_Radio.Playing) m_Radio.Stop();
            else
            {
                var status = ResearchQuestRules.Status(m_Read(), quest);
                if (status != ResearchQuestStatus.Locked) m_Radio.Play(quest, status == ResearchQuestStatus.Completed, Chinese);
            }
            RefreshRadio();
        }

        public void Refresh()
        {
            if (m_Disposed) return;
            bool reduced = HalosTerminalBackdrop.PrefersReducedMotion;
            if (reduced != m_ReduceMotion)
            {
                m_ReduceMotion = reduced;
                Element.EnableInClassList("research-quest-reduced", reduced);
                if (reduced)
                {
                    m_Target = m_Offset = Vector2.zero; m_Motion.Pause();
                    m_Wall.style.translate = StyleKeyword.Null;
                }
            }
            var saved = m_Read();
            if (saved == null) return;
            RefreshRadio();
            string signature = $"{saved.Accepted}/{saved.Ready}/{saved.Completed}/{saved.HaloKills}/{saved.SentryKills}/{m_Selected}/{m_Busy}/{m_Error}/{Chinese}";
            if (signature == m_Signature) return;
            m_Signature = signature;
            Element.EnableInClassList("research-quest-en", !Chinese);
            int complete = 0;
            for (int i = 0; i < m_Cards.Length; i++)
            {
                var quest = (ResearchQuest)i;
                var state = ResearchQuestRules.Status(saved, quest);
                if (state == ResearchQuestStatus.Completed) complete++;
                Set(m_CardTitles[i], For(quest).Title);
                Set(m_CardStates[i], State(state));
                m_Cards[i].EnableInClassList("research-quest-card-live", state == ResearchQuestStatus.Active || state == ResearchQuestStatus.Ready);
                m_Cards[i].EnableInClassList("research-quest-card-locked", state == ResearchQuestStatus.Locked);
            }
            Set(m_Back, m_Selected.HasValue ? Text("← 任务墙", "← All quests") : Text("← 作业船", "← Ship"));
            Set(m_Heading, Text("任务", "Quests"));
            Set(m_Count, $"{complete:00} / {ResearchQuestRules.Count:00}");
            Set(m_Legacy, Text("设施调查", "Facility records"));
            m_Legacy.style.display = !m_Selected.HasValue && m_Legacy.clickable != null ? DisplayStyle.Flex : DisplayStyle.None;
            m_Viewport.style.display = m_Selected.HasValue ? DisplayStyle.None : DisplayStyle.Flex;
            m_Detail.style.display = m_Selected.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (!m_Selected.HasValue) return;
            var selected = m_Selected.Value;
            var entry = For(selected);
            var status = ResearchQuestRules.Status(saved, selected);
            Set(m_Number, $"{(int)selected + 1:00}");
            Set(m_Title, entry.Title);
            Set(m_Status, State(status));
            string progress = status == ResearchQuestStatus.Active ? selected switch
            {
                ResearchQuest.HaloTrial => $"   {saved.HaloKills} / {ResearchQuestRules.HaloKillGoal}",
                ResearchQuest.Sentry => $"   {saved.SentryKills} / {ResearchQuestRules.SentryKillGoal}",
                _ => ""
            } : "";
            Set(m_Goal, entry.Goal + progress);
            Set(m_Brief, entry.Brief);
            var prior = ResearchQuestRules.Prerequisite(selected);
            bool locked = status == ResearchQuestStatus.Locked;
            Set(m_LockedBy, locked && prior.HasValue ? Text("前置任务  ", "Requires  ") + For(prior.Value).Title : "");
            m_LockedBy.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
            m_Play.style.display = locked ? DisplayStyle.None : DisplayStyle.Flex;
            m_Action.style.display = locked || status == ResearchQuestStatus.Completed ? DisplayStyle.None : DisplayStyle.Flex;
            m_Action.SetEnabled(!m_Busy);
            Set(m_Action, m_Busy ? Text("正在保存", "Saving") : status switch
            {
                ResearchQuestStatus.Active => Text("准备出战", "Prepare raid"),
                ResearchQuestStatus.Ready => Text("交付任务", "Turn in"),
                _ => Text("接取任务", "Accept quest")
            });
            m_Feedback.style.display = m_Error ? DisplayStyle.Flex : DisplayStyle.None;
            Set(m_Feedback, Text("任务状态未保存，请重试。", "Could not save the quest. Try again."));
        }

        private void RefreshRadio()
        {
            if (!m_Selected.HasValue) return;
            string copy = m_Radio.Playing ? Text("停止语音", "Stop audio") :
                ResearchQuestRules.Status(m_Read(), m_Selected.Value) == ResearchQuestStatus.Completed
                    ? Text("播放结语", "Play debrief") : Text("播放简报", "Play briefing");
            if (m_Play.text != copy) Set(m_Play, copy);
        }

        private async void Advance()
        {
            if (m_Disposed || m_Busy || !m_Selected.HasValue) return;
            var quest = m_Selected.Value;
            var status = ResearchQuestRules.Status(m_Read(), quest);
            if (status == ResearchQuestStatus.Active) { m_Prepare?.Invoke(); return; }
            if (status != ResearchQuestStatus.Available && status != ResearchQuestStatus.Ready) return;
            bool claim = status == ResearchQuestStatus.Ready;
            m_Busy = true; m_Error = false; Refresh();
            try
            {
                m_Error = !await m_Advance(quest, claim);
                if (!m_Disposed && !m_Error && claim && m_Selected == quest) m_Radio.Play(quest, true, Chinese);
            }
            catch (Exception) { m_Error = true; }
            finally { m_Busy = false; Refresh(); }
        }

        private static void Set(TextElement label, string text) => MoonkovLocalization.Set(label, text);
        private static VisualElement Box(VisualElement parent, string style)
        {
            var element = new VisualElement(); element.AddToClassList(style); parent.Add(element); return element;
        }
        private static Label Label(VisualElement parent, string style, string text = "")
        {
            var element = new Label(text) { pickingMode = PickingMode.Ignore }; element.AddToClassList(style); parent.Add(element); return element;
        }
        private static Button Button(VisualElement parent, string style, Action action)
        {
            var element = new Button(action); element.AddToClassList(style); parent.Add(element); return element;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Motion.Pause(); m_Refresh.Pause();
            m_Radio.Dispose(); MoonkovAudio.UnbindUI(Element);
            Element.RemoveFromHierarchy();
        }
    }
}
