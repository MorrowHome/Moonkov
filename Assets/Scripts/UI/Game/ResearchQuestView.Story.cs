// 任务领取后接入研究故事。按档案检查还有哪一章没看，从故事库找对应片段，播放到末尾后保存读完标记。
using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS.Client {
    public sealed partial class ResearchQuestView {
        private readonly Func<ResearchStoryChapter, Task<bool>> m_ReviewStory;
        private readonly Button m_ChapterNav;
        private VisualElement m_ChapterOverlay, m_ChapterDeck;
        private Label m_ChapterHeading;
        private readonly Button[] m_Chapters = new Button[ResearchQuestRules.StoryCount];
        private ResearchStoryChapter? m_Chapter;
        private ResearchStoryPlayer m_ChapterPlayer;

        private ResearchStoryChapter? PendingStory(ResearchQuestProgress saved) {
            if (m_ReviewStory == null || saved == null || saved.Ending != ResearchEnding.None) return null;

            // 章节要先由任务规则解锁，并且存档没标记已读，才显示新内容徽标。
            bool Unread(ResearchStoryChapter chapter) {
                return ResearchQuestRules.StoryAvailable(saved, chapter) &&
                       !ResearchQuestRules.StoryReviewed(saved, chapter);
            }

            for (int i = ResearchQuestRules.StoryCount - 1; i >= 0; i--) {
                var chapter = (ResearchStoryChapter)i;
                if (Unread(chapter))
                    return chapter == ResearchStoryChapter.Recall && Unread(ResearchStoryChapter.Lucent)
                        ? ResearchStoryChapter.Lucent
                        : chapter;
            }
            return null;
        }

        private bool TryOpenStoryAfterClaim(ResearchQuest quest) {
            ResearchStoryChapter? chapter = quest switch {
                ResearchQuest.Biology => ResearchStoryChapter.Spectrum,
                ResearchQuest.Tablet => ResearchStoryChapter.Nadir,
                ResearchQuest.FlashDrive => ResearchStoryChapter.Lockdown,
                ResearchQuest.Sentry => ResearchStoryChapter.Qualification,
                ResearchQuest.HardDrive => ResearchStoryChapter.Lucent,
                ResearchQuest.NoWayBack => ResearchStoryChapter.Ultimatum,
                _ => null
            };
            // 交付任务不一定对应一个新章节。没有该章节或已经读完时继续普通的任务反馈。
            if (!chapter.HasValue || m_ReviewStory == null ||
                !ResearchQuestRules.StoryAvailable(m_Read(), chapter.Value) ||
                ResearchQuestRules.StoryReviewed(m_Read(), chapter.Value))
                return false;
            OpenStory(chapter);
            return true;
        }

        private void OpenStory(ResearchStoryChapter? initial) {
            if (m_ReviewStory == null || m_ChapterOverlay != null || m_StoryEvent != null) return;
            var saved = m_Read();
            m_Radio.Stop();
            m_ChapterOverlay = Box(Element, "research-ending");
            m_ChapterOverlay.name = "researchStoryScreen";
            m_QuestHeader.style.visibility = Visibility.Hidden;
            m_QuestScroll.style.visibility = Visibility.Hidden;
            var header = Box(m_ChapterOverlay, "research-quest-header");
            m_ChapterHeading = Label(header, "research-quest-heading");
            var back = Button(header, "research-quest-nav", CloseStory);
            back.name = "questStoryBack";
            Set(back, Text("返回任务", "Back to quests"));
            var scroll = new ScrollView(ScrollViewMode.Vertical)
                { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("research-story-scroll");
            m_ChapterOverlay.Add(scroll);
            scroll.RegisterCallback<GeometryChangedEvent>(evt => {
                if (m_ChapterOverlay != null)
                    m_ChapterOverlay.EnableInClassList("research-story-compact", evt.newRect.width < 660);
            });
            var row = Box(scroll, "research-story-chapters");
            var defaultChapter = ResearchStoryChapter.Recruitment;
            for (int i = 0; i < m_Chapters.Length; i++) {
                var chapter = (ResearchStoryChapter)i;
                bool open = ResearchQuestRules.StoryAvailable(saved, chapter);
                // 自己主动打开回看页时默认选最近能读的章节。来自任务交付的指定章节可以覆盖这个默认值。
                if (open) defaultChapter = chapter;
                var button = m_Chapters[i] = Button(row, "research-story-chapter", () => SelectStory(chapter));
                button.name = "questStoryChapter" + i;
                button.SetEnabled(open);
                string title = open ? ResearchSceneLibrary.Chapter(chapter).Title : Text("未解锁", "Locked");
                button.tooltip = title;
                Set(button, open ? $"{i + 1:00}  {title}" : $"{i + 1:00}");
                button.EnableInClassList("research-story-unread",
                    open && !ResearchQuestRules.StoryReviewed(saved, chapter));
            }
            m_ChapterDeck = Box(scroll, "research-story-deck");
            SelectStory(initial ?? defaultChapter);
        }

        private void SelectStory(ResearchStoryChapter chapter) {
            // 章节的保存必须完成才能换到另一章。重复点当前章保留播放位置。
            if (m_ChapterPlayer?.Saving == true || m_Chapter == chapter ||
                !ResearchQuestRules.StoryAvailable(m_Read(), chapter))
                return;
            m_Chapter = chapter;
            var scene = ResearchSceneLibrary.Chapter(chapter);
            Set(m_ChapterHeading, scene.Title);
            for (int i = 0; i < m_Chapters.Length; i++)
                m_Chapters[i].EnableInClassList("research-story-current", i == (int)chapter);
            m_ChapterDeck.Clear();
            m_ChapterPlayer = new ResearchStoryPlayer(m_ChapterDeck, scene, m_Radio, () => FinishStory(chapter));
        }

        // 故事播完先提交完成信息。任务页等到保存结果才能按新的进度继续。
        private async Task<bool> FinishStory(ResearchStoryChapter chapter) {
            if (!ResearchQuestRules.StoryReviewed(m_Read(), chapter) && !await m_ReviewStory(chapter)) return false;
            if (m_Disposed) return true;
            m_Chapters[(int)chapter].EnableInClassList("research-story-unread", false);
            // 有些任务一次开放两个连续章节。保存第一章后可以直接播放还没读的下一章。
            if (chapter == ResearchStoryChapter.Lucent &&
                !ResearchQuestRules.StoryReviewed(m_Read(), ResearchStoryChapter.Recall)) {
                m_ChapterPlayer = null;
                SelectStory(ResearchStoryChapter.Recall);
                return true;
            }
            CloseStory(true);
            // 读完结局前的最后一章且还没选结局时，转到独立的选择窗口。已经选过则保留回看状态。
            if (chapter == ResearchStoryChapter.Originals && m_Read().Ending == ResearchEnding.None)
                OpenEnding();
            else if (chapter == ResearchStoryChapter.Recruitment &&
                     ResearchQuestRules.Status(m_Read(), ResearchQuest.Exposure) == ResearchQuestStatus.Available)
                Select(ResearchQuest.Exposure, true);
            else if (chapter == ResearchStoryChapter.Qualification &&
                     PendingStory(m_Read()) == ResearchStoryChapter.Lucent)
                OpenStory(ResearchStoryChapter.Lucent);
            else if (chapter == ResearchStoryChapter.Ultimatum &&
                     ResearchQuestRules.Status(m_Read(), ResearchQuest.EndOfTransmission) ==
                     ResearchQuestStatus.Available)
                Select(ResearchQuest.EndOfTransmission, true);
            return true;
        }

        private void CloseStory() { CloseStory(false); }

        private void CloseStory(bool completed) {
            if (!completed && m_ChapterPlayer?.Saving == true) return;
            m_Radio.Stop();
            m_ChapterOverlay?.RemoveFromHierarchy();
            m_ChapterOverlay = null;
            m_ChapterPlayer = null;
            m_Chapter = null;
            m_ChapterDeck = null;
            m_ChapterHeading = null;
            Array.Clear(m_Chapters, 0, m_Chapters.Length);
            m_QuestHeader.style.visibility = Visibility.Visible;
            m_QuestScroll.style.visibility = Visibility.Visible;
            Refresh();
            m_ChapterNav.Focus();
        }
    }
}