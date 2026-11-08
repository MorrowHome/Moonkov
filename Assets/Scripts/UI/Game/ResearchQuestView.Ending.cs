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

        private void OpenEnding()
        {
            if (m_ChooseEnding == null || m_StoryEvent != null || !ResearchQuestRules.DecisionAvailable(m_Read())) return;
            m_EndingPreview = ResearchEnding.None;
            m_StoryError = false;
            m_StoryEvent = Box(Element, "research-ending");
            m_StoryEvent.name = "researchEndingScreen";
            m_QuestHeader.style.visibility = Visibility.Hidden;
            m_QuestScroll.style.visibility = Visibility.Hidden;
            RenderEnding();
            if (m_Read().Ending == ResearchEnding.None) m_Radio.Play(ResearchQuest.EndOfTransmission, true, Chinese);
            else m_Radio.Stop();
            RefreshEndingAudio();
        }

        private void CloseEnding()
        {
            if (m_StorySaving) return;
            m_Radio.Stop();
            m_StoryEvent?.RemoveFromHierarchy();
            m_StoryEvent = null;
            m_QuestHeader.style.visibility = Visibility.Visible;
            m_QuestScroll.style.visibility = Visibility.Visible;
            m_StoryAudio = null;
            m_StoryConfirm = null;
            Array.Clear(m_EndingChoices, 0, m_EndingChoices.Length);
            m_EndingNav.Focus();
        }

        private void RenderEnding()
        {
            var selected = m_Read().Ending;
            m_StoryEvent.Clear();
            var header = Box(m_StoryEvent, "research-quest-header");
            Label(header, "research-quest-heading", selected == ResearchEnding.None ? Text("最终抉择", "Final decision") : Text("结局", "Epilogue"));
            m_StoryAudio = Button(header, "research-quest-nav", ToggleEndingAudio);
            var back = Button(header, "research-quest-nav", CloseEnding);
            back.name = "questEndingBack";
            Set(back, Text("返回任务", "Back to quests"));
            back.SetEnabled(!m_StorySaving);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            m_StoryEvent.Add(scroll);
            var main = Box(scroll, "research-ending-content");
            if (selected != ResearchEnding.None)
            {
                BuildEpilogue(main, selected);
                m_StoryAudio.Focus();
                RefreshEndingAudio();
                return;
            }

            var choices = Box(main, "research-ending-choices");
            for (int i = 0; i < m_EndingChoices.Length; i++)
            {
                var ending = (ResearchEnding)(i + 1);
                var copy = Ending(ending);
                var option = Button(choices, "research-ending-option", () => SelectEnding(ending));
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

        private static void BuildEpilogue(VisualElement host, ResearchEnding ending)
        {
            var copy = Ending(ending);
            var card = Box(host, "research-ending-result");
            AddStoryImage(card, ending);
            var report = Box(card, "research-ending-report");
            Label(report, "research-ending-title", copy.title);
            Label(report, "research-ending-copy", copy.epilogue);
            if (ending == ResearchEnding.Corporate)
                Label(report, "research-ending-reward", Text(
                    $"已入库：{ResearchQuestRules.CorporateDust} 月尘、搭载月核回收模块的战斗光环",
                    $"Deposited: {ResearchQuestRules.CorporateDust} dust and a Battle Halo with a mooncore recycler"));
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
                    if (!m_StoryError && m_Read().Ending == ending) m_Radio.PlayEnding(ending, Chinese);
                    RefreshEndingAudio();
                }
            }
        }

        private void ToggleEndingAudio()
        {
            if (m_Radio.Playing) m_Radio.Stop();
            else if (m_Read().Ending == ResearchEnding.None) m_Radio.Play(ResearchQuest.EndOfTransmission, true, Chinese);
            else m_Radio.PlayEnding(m_Read().Ending, Chinese);
            RefreshEndingAudio();
        }

        private void RefreshEndingAudio()
        {
            if (m_StoryAudio == null || m_Read() == null) return;
            string text = m_Radio.Playing ? Text("停止语音", "Stop audio") :
                m_Read().Ending == ResearchEnding.None ? Text("播放通讯", "Play transmission") : Text("播放结局", "Play epilogue");
            if (m_StoryAudio.text != text) Set(m_StoryAudio, text);
        }
    }
}
