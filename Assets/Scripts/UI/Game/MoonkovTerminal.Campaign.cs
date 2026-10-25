using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS.Client
{
    public sealed partial class MoonkovTerminal
    {
        private CampaignChapterView m_CampaignTasks;

        private bool TryPresentCampaignTasks()
        {
            var progress = PlayerProfileClient.Campaign;
            if (!PlayerProfileClient.IsOffline || progress == null) return false;
            TerminalLayout.Populate(m_Content, "Tasks");
            var stylesheet = Resources.Load<StyleSheet>("Moonkov/UI/CampaignChapterOne");
            if (stylesheet != null && !m_Content.styleSheets.Contains(stylesheet)) m_Content.styleSheets.Add(stylesheet);
            var side = m_Content.Q<VisualElement>(className: "terminal-side");
            side.AddToClassList("campaign-terminal-side");
            var headings = side.Query<Label>(className: "terminal-eyebrow").ToList();
            if (headings.Count > 0) headings[0].text = "单人战役";
            if (headings.Count > 1) headings[1].text = "第一章 静默着陆";
            if (headings.Count > 2) headings[2].text = "现场通讯";
            side.Q<Label>(className: "terminal-title").text = "清理任务";
            side.Q<Label>(className: "terminal-heading").text = "调查事故并带回样本";
            side.Q<Label>(className: "terminal-meter-name").text = "调查进度";
            side.Q<Label>(className: "terminal-copy").text = "Theophilus 研究站失联。公司派清理队调查事故，回收研究样本。沈禾留在作业船保障舱，通过通信提供支持。";
            int complete = CampaignCatalog.Tasks.Count(t => progress.CompletedTasks.Contains(t.Id));
            m_Content.Q<ArcMeter>().Value = complete / 3f;
            m_Content.Q<Label>("missionCareerCount").text = complete + "/3";
            m_Content.Q<Label>("mooncoreKnowledge").text = "顾衡：先恢复本地终端，核实设施和人员情况。样本回收后保持封存。";
            m_Content.Q<Label>("archiveKnowledge").text = complete == 3 ? CampaignCatalog.Find("CM03").CompletionText :
                "沈禾：船这边正常。我守着回程通道，你先确认里面能不能走。";
            var wide = m_Content.Q<VisualElement>(className: "terminal-wide");
            wide.Clear();
            m_CampaignTasks?.Dispose();
            m_CampaignTasks = new CampaignChapterView(wide, () => CampaignScene.ReadProgress(), Prepare, "准备出战");
            MoonkovLocalization.Bind(side);
            return true;
        }
    }
}
