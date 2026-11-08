using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private static readonly ResearchQuest[] s_ResearchDevices =
            { ResearchQuest.Tablet, ResearchQuest.FlashDrive, ResearchQuest.HardDrive };
        private VisualElement m_ResearchList, m_ResearchPrompt;
        private Label m_ResearchPromptText;
        private readonly Label[] m_ResearchLines = new Label[ResearchQuestRules.Count];
        private ResearchQuest? m_ResearchHold;
        private float m_ResearchNextRefresh;
        private bool m_ResearchInputPriority;

        private void InitializeResearchQuests()
        {
            var style = Resources.Load<StyleSheet>("Moonkov/UI/ResearchQuests");
            if (style && !m_Root.styleSheets.Contains(style)) m_Root.styleSheets.Add(style);
            m_ResearchList = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ResearchList.AddToClassList("research-quest-raid-list");
            m_ResearchList.AddToClassList("moonkov-style-font");
            m_ResearchList.style.display = DisplayStyle.None;
            m_Root.Q<ScrollView>("raidRightRail")?.Add(m_ResearchList);
            for (int i = 0; i < m_ResearchLines.Length; i++)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("research-quest-raid-line");
                m_ResearchLines[i] = label;
                m_ResearchList.Add(label);
            }
            m_ResearchPrompt = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ResearchPrompt.AddToClassList("research-quest-interaction");
            m_ResearchPrompt.AddToClassList("moonkov-style-font");
            m_ResearchPrompt.style.display = DisplayStyle.None;
            m_ResearchPromptText = new Label { pickingMode = PickingMode.Ignore };
            m_ResearchPromptText.AddToClassList("research-quest-raid-line");
            m_ResearchPrompt.Add(m_ResearchPromptText);
            m_Root.Add(m_ResearchPrompt);
            m_ResearchNextRefresh = 0;
            m_ResearchHold = null;
        }

        private bool UpdateResearchQuests(bool live, bool canInteract, Vector3 position)
        {
            m_ResearchInputPriority = false;
            if (m_ResearchList == null) return false;
            int raid = m_Snapshot.RaidId;
            if (!live || !PlayerProfileClient.IsOffline || !ResearchQuestRuntime.TryRead(raid, out var quest))
            {
                m_ResearchList.style.display = DisplayStyle.None;
                m_ResearchPrompt.style.display = DisplayStyle.None;
                ReleaseResearchHold();
                return false;
            }
            m_ResearchList.style.display = DisplayStyle.Flex;
            if (Time.unscaledTime >= m_ResearchNextRefresh)
            {
                m_ResearchNextRefresh = Time.unscaledTime + .16f;
                for (int i = 0; i < m_ResearchLines.Length; i++)
                {
                    var id = (ResearchQuest)i;
                    var label = m_ResearchLines[i];
                    bool active = ResearchQuestRules.Has(quest.Active, id);
                    label.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                    if (active) MoonkovLocalization.Set(label, For(id).Title + "   " + FieldProgress(id, quest));
                }
            }

            bool held = canInteract && m_InteractionBindings.Operate.IsPressed();
            if (!held) ReleaseResearchHold();
            ResearchQuest? target = null;
            float best = m_WorldFocusScore;
            if (canInteract)
                foreach (var id in s_ResearchDevices)
                {
                    if (!ResearchQuestRuntime.NeedsDevice(raid, id)) continue;
                    var anchor = ResearchQuestRuntime.Anchor(id);
                    if (anchor == null) continue;
                    float score = RaidInteractionAim.Score(Camera.main, position, ResearchQuestRuntime.InteractionPoint(id, anchor), 3,
                        RaidInteractionAim.MissionSurface(anchor.Id));
                    if (score >= 0 && score >= best) { best = score; target = id; }
                }
            if (!m_ResearchHold.HasValue && target.HasValue && held && m_InteractionBindings.Operate.WasPressedThisFrame())
                m_ResearchHold = target;
            if (m_ResearchHold.HasValue)
            {
                if (target == m_ResearchHold) ResearchQuestRuntime.Hold(raid, target.Value, true);
                else ReleaseResearchHold();
            }
            string notice = quest.Notice switch
            {
                "interrupted" => Text("操作中断", "Transfer interrupted"),
                "space" => Text("背包空间不足", "Not enough inventory space"),
                "extract" => Text("带着道具撤离", "Extract with the item"),
                _ => null
            };
            string prompt = target.HasValue ? m_InteractionBindings.Label(m_InteractionBindings.Operate) + "   " + DeviceAction(target.Value)
                + (quest.CanOperate && quest.Target == target.Value ? $"   {Mathf.FloorToInt(quest.InteractionProgress * 100)}%" : "")
                : notice;
            m_ResearchPrompt.style.display = canInteract && prompt != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (prompt != null && m_ResearchPromptText.text != prompt) MoonkovLocalization.Set(m_ResearchPromptText, prompt);
            return m_ResearchInputPriority = target.HasValue || m_ResearchHold.HasValue;
        }

        private void ReleaseResearchHold()
        {
            if (!m_ResearchHold.HasValue) return;
            ResearchQuestRuntime.Hold(m_Snapshot.RaidId, m_ResearchHold.Value, false);
            m_ResearchHold = null;
        }

        private static string DeviceAction(ResearchQuest quest) => quest switch
        {
            ResearchQuest.Tablet => Text("拿取研究员平板", "Take personnel tablet"),
            ResearchQuest.FlashDrive => Text("读取管理员日志", "Copy administrator logs"),
            _ => Text("拷贝研究数据库", "Copy research database")
        };

        private static string FieldProgress(ResearchQuest id, ResearchQuestRuntime.Presentation view)
        {
            if (ResearchQuestRules.Has(view.Recovered, id)) return Text("带回后交付", "Extract to turn in");
            return id switch
            {
                ResearchQuest.Exposure => $"{view.Seconds / 60:00}:{view.Seconds % 60:00} / 05:00",
                ResearchQuest.HaloTrial => $"{view.HaloKills} / {ResearchQuestRules.HaloKillGoal}",
                ResearchQuest.Biology => $"{Mathf.Min(view.Samples, ResearchQuestRules.SampleGoal)} / {ResearchQuestRules.SampleGoal}  " +
                    (view.Samples >= ResearchQuestRules.SampleGoal ? Text("带回后交付", "Extract to turn in") : Text("封存样本", "samples")),
                ResearchQuest.Tablet => view.TabletAvailable ? Text("MC00 L22 隔离柜", "MC00 L22 isolation cabinet") : Text("取回平板", "Recover the tablet"),
                ResearchQuest.FlashDrive => ResearchQuestRules.Has(view.Issued, id) ? Text("MC00 L22 管理工作站", "MC00 L22 administrator workstation") :
                    Text("取回配发的 U 盘", "Recover the issued flash drive"),
                ResearchQuest.HardDrive => ResearchQuestRules.Has(view.Issued, id) ? Text("MC00 L22 研究服务器", "MC00 L22 research server") :
                    Text("取回配发的硬盘", "Recover the issued hard drive"),
                ResearchQuest.HaloStorage => ResearchQuestRules.Has(view.Issued, id) ? Text("把封存包放入光环收纳格", "Place package in the Halo cargo slot") :
                    Text("取回封存包", "Recover the research package"),
                _ => $"{view.SentryKills} / {ResearchQuestRules.SentryKillGoal}"
            };
        }
    }
}
