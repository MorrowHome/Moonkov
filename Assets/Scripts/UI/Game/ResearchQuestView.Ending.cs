using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS.Client
{
    public sealed partial class ResearchQuestView
    {
        private readonly Func<ResearchEnding, Task<bool>> m_ChooseEnding;
        private VisualElement m_StoryEvent;
        private Button m_StoryAudio, m_StoryConfirm;
        private readonly Button[] m_EndingChoices = new Button[3];
        private ResearchEnding m_EndingPreview;
        private bool m_StorySaving, m_StoryError;
        private ResearchStoryPlayer m_EndingPlayer;

        private void OpenEnding()
        {
            var saved = m_Read();
            if (m_ChooseEnding == null || m_StoryEvent != null || m_ChapterOverlay != null || !ResearchQuestRules.DecisionAvailable(saved)) return;
            if (saved.Ending == ResearchEnding.None && m_ReviewStory != null &&
                !ResearchQuestRules.StoryReviewed(saved, ResearchStoryChapter.Originals))
            {
                OpenStory(ResearchStoryChapter.Originals);
                return;
            }
            m_Radio.Stop();
            m_EndingPreview = ResearchEnding.None;
            m_StoryError = false;
            m_StoryEvent = Box(Element, "research-ending");
            m_StoryEvent.name = "researchEndingScreen";
            m_QuestHeader.style.visibility = Visibility.Hidden;
            m_QuestScroll.style.visibility = Visibility.Hidden;
            RenderEnding();
            if (saved.Ending == ResearchEnding.None) m_Radio.Play(ResearchQuest.EndOfTransmission, true, Chinese);
            RefreshEndingAudio();
        }

        private void CloseEnding() => CloseEnding(false);

        private void CloseEnding(bool completed)
        {
            if (m_StorySaving || !completed && m_EndingPlayer?.Saving == true) return;
            m_Radio.Stop();
            m_StoryEvent?.RemoveFromHierarchy();
            m_StoryEvent = null;
            m_QuestHeader.style.visibility = Visibility.Visible;
            m_QuestScroll.style.visibility = Visibility.Visible;
            m_StoryAudio = null;
            m_StoryConfirm = null;
            m_EndingPlayer = null;
            Array.Clear(m_EndingChoices, 0, m_EndingChoices.Length);
            m_EndingNav.Focus();
        }

        private void RenderEnding()
        {
            var selected = m_Read().Ending;
            m_StoryEvent.Clear();
            var header = Box(m_StoryEvent, "research-quest-header");
            Label(header, "research-quest-heading", selected == ResearchEnding.None ? Text("最终抉择", "Final decision") : Ending(selected).title);
            m_StoryAudio = selected == ResearchEnding.None ? Button(header, "research-quest-nav", ToggleEndingAudio) : null;
            var back = Button(header, "research-quest-nav", CloseEnding);
            back.name = "questEndingBack";
            Set(back, Text("返回任务", "Back to quests"));
            back.SetEnabled(!m_StorySaving);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("research-story-scroll");
            m_StoryEvent.Add(scroll);
            var main = Box(scroll, "research-ending-content");
            if (selected != ResearchEnding.None)
            {
                m_EndingPlayer = new ResearchStoryPlayer(main, ResearchSceneLibrary.Epilogue(selected), m_Radio, () =>
                {
                    CloseEnding(true);
                    return Task.FromResult(true);
                });
                return;
            }

            var choices = Box(main, "research-ending-choices");
            for (int i = 0; i < m_EndingChoices.Length; i++)
            {
                var ending = (ResearchEnding)(i + 1);
                var copy = Ending(ending);
                var option = Button(choices, "research-ending-option", () => SelectEnding(ending));
                if (i == m_EndingChoices.Length - 1) option.AddToClassList("research-ending-option-last");
                option.name = $"questEnding{(int)ending}";
                option.SetEnabled(!m_StorySaving);
                m_EndingChoices[i] = option;
                AddStoryImage(option, ending);
                Label(option, "research-ending-title", copy.title);
                Label(option, "research-ending-copy", copy.description);
            }
            if (m_StoryError) Label(main, "research-quest-feedback", Text("未保存抉择，请重试。", "Could not save the decision. Try again."));
            m_StoryConfirm = Button(main, "research-quest-primary", ConfirmEnding);
            m_StoryConfirm.AddToClassList("research-ending-confirm");
            m_StoryConfirm.name = "questEndingConfirm";
            UpdateEndingSelection();
            RefreshEndingAudio();
            if (!m_StorySaving) m_EndingChoices[Mathf.Max(0, (int)m_EndingPreview - 1)].Focus();
        }

        private static void AddStoryImage(VisualElement host, ResearchEnding ending)
        {
            var art = ending switch
            {
                ResearchEnding.Expose => "PageArt/CommsPrint",
                ResearchEnding.Corporate => "PageArt/Technical",
                _ => "PageArt/ArchiveDark"
            };
            var image = new Image { image = Resources.Load<Texture2D>("Moonkov/UI/" + art),
                scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            image.AddToClassList("research-ending-art");
            host.Add(image);
        }

        private void SelectEnding(ResearchEnding ending)
        {
            if (m_StorySaving) return;
            m_EndingPreview = ending;
            UpdateEndingSelection();
        }

        private void UpdateEndingSelection()
        {
            for (int i = 0; i < m_EndingChoices.Length; i++)
                m_EndingChoices[i].EnableInClassList("research-ending-selected", i + 1 == (int)m_EndingPreview);
            m_StoryConfirm.SetEnabled(!m_StorySaving && m_EndingPreview != ResearchEnding.None);
            Set(m_StoryConfirm, m_StorySaving ? Text("正在保存", "Saving") : m_EndingPreview == ResearchEnding.None
                ? Text("选择结局", "Choose an outcome") : Text("确认：", "Confirm: ") + Ending(m_EndingPreview).title);
        }

        private async void ConfirmEnding()
        {
            if (m_StorySaving || m_EndingPreview == ResearchEnding.None) return;
            var ending = m_EndingPreview;
            m_StorySaving = true; m_StoryError = false; RenderEnding();
            try { m_StoryError = !await m_ChooseEnding(ending); }
            catch (Exception) { m_StoryError = true; }
            finally
            {
                m_StorySaving = false;
                if (!m_Disposed && m_StoryEvent != null)
                {
                    RenderEnding();
                    RefreshEndingAudio();
                }
            }
        }

        private void ToggleEndingAudio()
        {
            if (m_Radio.Playing) m_Radio.Stop();
            else m_Radio.Play(ResearchQuest.EndOfTransmission, true, Chinese);
            RefreshEndingAudio();
        }

        private void RefreshEndingAudio()
        {
            m_EndingPlayer?.RefreshAudio();
            if (m_StoryAudio == null || m_Read() == null) return;
            string text = m_Radio.Playing ? Text("停止语音", "Stop audio") : Text("播放通讯", "Play transmission");
            if (m_StoryAudio.text != text) Set(m_StoryAudio, text);
        }
    }
}
