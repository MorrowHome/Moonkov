using System;
using System.Threading.Tasks;
using Unity.MP_FPS.Inventory;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS.Client
{
    public sealed class ResearchQuestView : IDisposable
    {
        private readonly Func<ResearchQuestProgress> m_Read;
        private readonly Func<ResearchQuest, bool, Task<bool>> m_Advance;
        private readonly Action m_Prepare, m_Close;
        private readonly ResearchQuestRadio m_Radio = new ResearchQuestRadio();
        private readonly VisualElement m_Deck;
        private readonly Image m_Photo;
        private readonly Label m_Heading, m_Count, m_Title, m_Status, m_Brief, m_Goal, m_Feedback, m_Unlocks;
        private readonly Label m_VoiceHeading, m_GoalHeading, m_RewardHeading, m_ExhibitId, m_ExhibitMass, m_ExhibitIndex;
        private readonly Button m_Back, m_Legacy, m_Action, m_Play;
        private readonly Button[] m_Choices = new Button[ResearchQuestRules.Count];
        private readonly Label[] m_ChoiceTitles = new Label[ResearchQuestRules.Count];
        private readonly IVisualElementScheduledItem m_Refresh;
        private ResearchQuest? m_Selected;
        private string m_Signature;
        private bool m_BriefPlayed, m_Busy, m_Error, m_Disposed;

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
            var emblem = new Image
            {
                image = Resources.Load<Texture2D>("Moonkov/UI/Halos/Emblem"),
                scaleMode = ScaleMode.ScaleToFit,
                tintColor = new Color32(161, 166, 175, 255),
                pickingMode = PickingMode.Ignore
            };
            emblem.AddToClassList("research-quest-emblem");
            header.Add(emblem);
            m_Heading = Label(header, "research-quest-heading");
            m_Count = Label(header, "research-quest-count");
            m_Legacy = Button(header, "research-quest-nav", legacy);
            m_Back = Button(header, "research-quest-nav", Close);
            m_Back.name = "questBack";

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("research-quest-scroll"); Element.Add(scroll);
            scroll.RegisterCallback<GeometryChangedEvent>(Layout);
            m_Deck = Box(scroll, "research-quest-deck");
            var left = Box(m_Deck, "research-quest-left");
            var right = Box(m_Deck, "research-quest-right");

            var exhibit = Box(left, "research-quest-panel");
            exhibit.AddToClassList("research-quest-exhibit");
            exhibit.name = "questExhibit";
            m_Photo = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore, name = "questModelPhoto" };
            m_Photo.AddToClassList("research-quest-photograph");
            exhibit.Add(m_Photo);
            var identifiers = Box(exhibit, "research-quest-identifiers");
            m_ExhibitId = Label(identifiers, "research-quest-data");
            m_ExhibitMass = Label(identifiers, "research-quest-data");
            var scale = Box(exhibit, "research-quest-scale");
            scale.pickingMode = PickingMode.Ignore;
            scale.generateVisualContent += DrawScale;
            m_ExhibitIndex = Label(exhibit, "research-quest-exhibit-index");

            var footer = Box(left, "research-quest-footer");
            var objectives = Box(footer, "research-quest-objectives");
            var voice = Panel(objectives, "research-quest-voice", out m_VoiceHeading);
            m_Play = Button(voice, "research-quest-listen", ToggleRadio);
            m_Play.name = "questListen";
            var goal = Panel(objectives, "research-quest-goal-panel", out m_GoalHeading);
            m_Goal = Label(goal, "research-quest-goal");
            var rewards = Panel(footer, "research-quest-rewards", out m_RewardHeading);
            m_Unlocks = Label(rewards, "research-quest-unlocks");

            var overview = Box(right, "research-quest-panel");
            overview.AddToClassList("research-quest-overview");
            var overviewHeader = Box(overview, "research-quest-overview-head");
            m_Title = Label(overviewHeader, "research-quest-title");
            m_Status = Label(overviewHeader, "research-quest-status");
            var briefScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            briefScroll.AddToClassList("research-quest-brief-scroll");
            overview.Add(briefScroll);
            m_Brief = Label(briefScroll, "research-quest-copy");
            m_Feedback = Label(overview, "research-quest-feedback");
            var action = Box(right, "research-quest-panel");
            action.AddToClassList("research-quest-action-frame");
            m_Action = Button(action, "research-quest-primary", Advance);
            m_Action.name = "questAction";
            var grid = Box(right, "research-quest-panel");
            grid.AddToClassList("research-quest-matrix");
            grid.name = "questMatrix";
            BuildMatrix(grid);

            m_Refresh = Element.schedule.Execute(Refresh).Every(200);
            Element.RegisterCallback<NavigationCancelEvent>(evt => { Close(); evt.StopPropagation(); });
            Element.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopImmediatePropagation(); }
            }, TrickleDown.TrickleDown);
            Element.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            host.Add(Element);
            MoonkovAudio.BindUI(Element);
            Select(InitialQuest(m_Read()), false);
            m_Choices[(int)m_Selected.Value].Focus();
        }

        private static ResearchQuest InitialQuest(ResearchQuestProgress progress)
        {
            foreach (var preferred in new[] { ResearchQuestStatus.Ready, ResearchQuestStatus.Active, ResearchQuestStatus.Available })
                for (int i = 0; i < ResearchQuestRules.Count; i++)
                    if (ResearchQuestRules.Status(progress, (ResearchQuest)i) == preferred) return (ResearchQuest)i;
            return progress == null || progress.Completed == 0 ? ResearchQuest.Exposure : ResearchQuest.Sentry;
        }

        private void BuildMatrix(VisualElement matrix)
        {
            for (int row = 0; row < 2; row++)
            {
                var line = Box(matrix, "research-quest-matrix-row");
                for (int column = 0; column < 4; column++)
                {
                    int index = row * 4 + column;
                    var id = (ResearchQuest)index;
                    var button = Button(line, "research-quest-node", () => Select(id, true));
                    button.name = "questCard" + index;
                    Label(button, "research-quest-node-number", (index + 1).ToString("00"));
                    m_ChoiceTitles[index] = Label(button, "research-quest-node-title");
                    m_Choices[index] = button;
                }
            }
        }

        private void Layout(GeometryChangedEvent evt)
        {
            if (evt.newRect.width < 1 || evt.newRect.height < 1) return;
            bool narrow = evt.newRect.width < 623;
            Element.EnableInClassList("research-quest-narrow", narrow);
            Element.EnableInClassList("research-quest-compact", evt.newRect.width < 898 || evt.newRect.height < 541);
            m_Deck.style.height = Mathf.Max(narrow ? 1000 : 465, evt.newRect.height);
        }

        private void Select(ResearchQuest quest, bool readBriefing)
        {
            if (m_Selected != quest)
            {
                m_Radio.Stop();
                m_Selected = quest;
                m_Error = m_BriefPlayed = false;
                var entry = For(quest);
                m_Photo.image = Resources.Load<Texture2D>(entry.Item == null
                    ? "Moonkov/UI/" + entry.Art : "Moonkov/UI/QuestExhibits/" + entry.Item);
                m_Photo.scaleMode = entry.Item == null ? ScaleMode.ScaleAndCrop : ScaleMode.ScaleToFit;
                m_Photo.EnableInClassList("research-quest-scene-image", entry.Item == null);
            }
            if (readBriefing && ResearchQuestRules.Status(m_Read(), quest) == ResearchQuestStatus.Available) PlayBriefing();
            Refresh();
        }

        private void PlayBriefing()
        {
            if (m_BriefPlayed) return;
            m_BriefPlayed = true;
            m_Radio.Play(m_Selected.Value, false, Chinese);
        }

        private void Close() => m_Close?.Invoke();

        private void ToggleRadio()
        {
            if (!m_Selected.HasValue) return;
            var quest = m_Selected.Value;
            if (m_Radio.Playing) m_Radio.Stop();
            else
            {
                var status = ResearchQuestRules.Status(m_Read(), quest);
                if (status == ResearchQuestStatus.Locked) return;
                m_BriefPlayed = true;
                m_Radio.Play(quest, status == ResearchQuestStatus.Completed, Chinese);
            }
            RefreshRadio();
        }

        public void Refresh()
        {
            var saved = m_Read();
            if (m_Disposed || saved == null || !m_Selected.HasValue) return;
            RefreshRadio();
            string signature = $"{saved.Accepted}/{saved.Ready}/{saved.Completed}/{saved.HaloKills}/{saved.SentryKills}/{m_Selected}/{m_Busy}/{m_Error}/{Chinese}";
            if (signature == m_Signature) return;
            m_Signature = signature;
            Element.EnableInClassList("research-quest-en", !Chinese);
            var selected = m_Selected.Value;
            var entry = For(selected);
            int complete = 0;
            for (int i = 0; i < m_Choices.Length; i++)
            {
                var quest = (ResearchQuest)i;
                var state = ResearchQuestRules.Status(saved, quest);
                if (state == ResearchQuestStatus.Completed) complete++;
                Set(m_ChoiceTitles[i], For(quest).NodeTitle);
                m_Choices[i].tooltip = For(quest).Title + " / " + State(state);
                m_Choices[i].EnableInClassList("research-quest-node-selected", quest == selected);
                m_Choices[i].EnableInClassList("research-quest-node-locked", state == ResearchQuestStatus.Locked);
                m_Choices[i].EnableInClassList("research-quest-node-ready", state == ResearchQuestStatus.Ready);
            }

            var status = ResearchQuestRules.Status(saved, selected);
            Set(m_Back, Text("返回作业船", "Back to ship"));
            Set(m_Legacy, Text("设施调查", "Facility records"));
            Set(m_Heading, Text("任务管理", "Quest Management"));
            Set(m_Count, $"{complete:00} / {ResearchQuestRules.Count:00}");
            Set(m_Title, entry.Title);
            Set(m_Status, State(status));
            var prior = ResearchQuestRules.Prerequisite(selected);
            Set(m_Brief, status == ResearchQuestStatus.Locked && prior.HasValue
                ? entry.Brief + "\n\n" + Text("完成前置任务  ", "Requires  ") + For(prior.Value).Title : entry.Brief);
            Set(m_VoiceHeading, Text("任务通讯", "Quest Comms"));
            Set(m_GoalHeading, Text("任务目标", "Quest Requirements"));
            Set(m_RewardHeading, Text("交付收益", "Rewards"));
            string progress = status == ResearchQuestStatus.Active ? selected switch
            {
                ResearchQuest.HaloTrial => $"  {saved.HaloKills} / {ResearchQuestRules.HaloKillGoal}",
                ResearchQuest.Sentry => $"  {saved.SentryKills} / {ResearchQuestRules.SentryKillGoal}",
                _ => ""
            } : "";
            Set(m_Goal, entry.Goal + progress);
            Set(m_Unlocks, Rewards(selected));
            Set(m_ExhibitId, ExhibitId(selected));
            Set(m_ExhibitIndex, $"RQ-{(int)selected + 1:00}");
            var item = InventoryCatalog.Get(entry.Item);
            m_ExhibitMass.style.display = item == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (item != null) Set(m_ExhibitMass, Text("重量  ", "MASS  ") + $"{item.Weight:0.00} kg");
            m_Play.SetEnabled(status != ResearchQuestStatus.Locked);
            m_Action.SetEnabled(!m_Busy && status != ResearchQuestStatus.Locked && status != ResearchQuestStatus.Completed);
            Set(m_Action, m_Busy ? Text("正在保存", "Saving") : status switch
            {
                ResearchQuestStatus.Active => Text("准备出战", "Prepare raid"),
                ResearchQuestStatus.Ready => Text("交付任务", "Turn in quest"),
                ResearchQuestStatus.Completed or ResearchQuestStatus.Locked => State(status),
                _ => Text("接取任务", "Accept Quest")
            });
            m_Feedback.style.display = m_Error ? DisplayStyle.Flex : DisplayStyle.None;
            Set(m_Feedback, Text("任务状态未保存，请重试。", "Could not save the quest. Try again."));
        }

        private static string Rewards(ResearchQuest quest)
        {
            string unlocked = null;
            for (int i = 0; i < ResearchQuestRules.Count; i++)
                if (ResearchQuestRules.Prerequisite((ResearchQuest)i) == quest)
                    unlocked = unlocked == null ? For((ResearchQuest)i).Title : unlocked + "\n" + For((ResearchQuest)i).Title;
            return unlocked == null ? Text("保存任务记录", "Archive mission record") : Text("解锁\n", "Unlocks\n") + unlocked;
        }

        private static string ExhibitId(ResearchQuest quest) => quest switch
        {
            ResearchQuest.Tablet => Text("证物  IT-07", "EXHIBIT  IT-07"),
            ResearchQuest.FlashDrive => Text("证物  FK-12", "EXHIBIT  FK-12"),
            ResearchQuest.HardDrive => Text("证物  RD-04", "EXHIBIT  RD-04"),
            _ => "THEOPHILUS"
        };

        private void RefreshRadio()
        {
            var state = ResearchQuestRules.Status(m_Read(), m_Selected.Value);
            string copy = m_Radio.Playing ? Text("停止语音", "Stop audio") : state == ResearchQuestStatus.Completed
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
                bool changed = await m_Advance(quest, claim);
                if (!m_Disposed && m_Selected == quest)
                {
                    m_Error = !changed;
                    if (changed && claim) m_Radio.Play(quest, true, Chinese);
                    else if (changed) PlayBriefing();
                }
            }
            catch (Exception) { if (m_Selected == quest) m_Error = true; }
            finally { m_Busy = false; Refresh(); }
        }

        private static void DrawScale(MeshGenerationContext context)
        {
            float width = context.visualElement.contentRect.width;
            if (width < 8) return;
            var painter = context.painter2D;
            painter.strokeColor = new Color32(90, 97, 105, 230);
            painter.lineWidth = 1;
            painter.BeginPath();
            for (int i = 0; i <= 20; i++)
            {
                float x = 1 + (width - 2) * i / 20;
                painter.MoveTo(new Vector2(x, i % 5 == 0 ? 0 : 6));
                painter.LineTo(new Vector2(x, 12));
            }
            painter.Stroke();
        }

        private static void Set(TextElement label, string text) => MoonkovLocalization.Set(label, text);

        private static VisualElement Panel(VisualElement parent, string style, out Label title)
        {
            var element = Box(parent, "research-quest-panel");
            element.AddToClassList(style);
            var header = Box(element, "research-quest-panel-head");
            title = Label(header, "research-quest-panel-title");
            return element;
        }

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
            m_Disposed = true; m_Refresh.Pause();
            m_Radio.Dispose(); MoonkovAudio.UnbindUI(Element);
            Element.RemoveFromHierarchy();
        }
    }
}
