// 把研究主线和单人战役接到终端任务页。用档案中的进度刷新已有视图，接任务、领取奖励和追踪目标等操作再交给对应的客户端。
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS.Client {
    public sealed partial class MoonkovTerminal {
        private CampaignChapterView m_CampaignTasks;
        private ResearchQuestView m_ResearchTasks;
        private bool m_FacilityRecords;

        private bool TryPresentResearchTasks() {
            // 账号拿到研究进度才使用研究主线视图。用户转去查设施资料时跳过它，走终端其余分支。
            if (PlayerProfileClient.ResearchQuests == null || m_FacilityRecords) return false;
            // 任务视图接收读取、领取、结局和章节回看这些回调。进度兑现及持久化在 PlayerProfileClient。
            m_ResearchTasks = new ResearchQuestView(m_Root, () => PlayerProfileClient.ResearchQuests,
                PlayerProfileClient.AdvanceResearchQuestAsync, Prepare, () => {
                    m_FacilityRecords = true;
                    CloseResearchTasks();
                    Render();
                }, () => Navigate(0), PlayerProfileClient.ChooseResearchEndingAsync,
                PlayerProfileClient.ReviewResearchStoryAsync);
            return true;
        }

        private void CloseResearchTasks() {
            m_ResearchTasks?.Dispose();
            m_ResearchTasks = null;
        }

        private bool TryPresentCampaignTasks() {
            var progress = PlayerProfileClient.Campaign;
            // 单人战役只对带对应进度的离线档案显示。线上账号用自己的任务和设施说明。
            if (!PlayerProfileClient.IsOffline || progress == null) return false;
            TerminalLayout.Populate(m_Content, "Tasks");
            var stylesheet = Resources.Load<StyleSheet>("Moonkov/UI/CampaignChapterOne");
            if (stylesheet != null && !m_Content.styleSheets.Contains(stylesheet))
                m_Content.styleSheets.Add(stylesheet);
            var side = m_Content.Q<VisualElement>(className: "terminal-side");
            side.AddToClassList("campaign-terminal-side");
            var headings = side.Query<Label>(className: "terminal-eyebrow").ToList();
            if (headings.Count > 0) headings[0].text = "单人战役";
            var current = CampaignCatalog.Find(CampaignRules.CurrentMain(progress));
            if (headings.Count > 1)
                headings[1].text = current == null
                    ? "调查记录"
                    : "第" + (current.Chapter == 1 ? "一" : "二") + "章 " + current.ChapterTitle;
            if (headings.Count > 2) headings[2].text = "调查记录";
            side.Q<Label>(className: "terminal-title").text = "清理任务";
            side.Q<Label>(className: "terminal-heading").text = current?.Title ?? "调查记录已保存";
            side.Q<Label>(className: "terminal-meter-name").text = "调查进度";
            side.Q<Label>(className: "terminal-copy").text = "Theophilus 研究站失联。公司派清理队调查事故，回收研究样本。沈禾留在作业船保障舱，通过通信提供支持。";
            // 以已保存任务列表计算百分比。正在调查看起来已完成的步骤也要等撤离存档确认。
            int complete = CampaignCatalog.Tasks.Count(t => progress.CompletedTasks.Contains(t.Id));
            m_Content.Q<ArcMeter>().Value = complete / (float)CampaignCatalog.PlannedMainTaskCount;
            m_Content.Q<Label>("missionCareerCount").text = "已保存 " + complete + " 项";
            m_Content.Q<Label>("mooncoreKnowledge").text =
                progress.CompletedTasks.Contains("CM03") ? "月核样本已带回。" : "月核样本等待回收。";
            m_Content.Q<Label>("archiveKnowledge").text =
                progress.CompletedTasks.Contains("CM04") ? "事故档案已带回。" : "事故档案等待回收。";
            var wide = m_Content.Q<VisualElement>(className: "terminal-wide");
            wide.Clear();
            m_CampaignTasks?.Dispose();
            // 把独立的章节列表装到右侧，并把继续按钮接回准备流程。终端仍负责整体导航。
            m_CampaignTasks = new CampaignChapterView(wide, () => CampaignScene.ReadProgress(), Prepare, "准备出战");
            if (m_FacilityRecords)
                ActionButton(side, ResearchQuestCopy.Text("返回任务管理", "Back to quests"), () => {
                    m_FacilityRecords = false;
                    Render();
                });
            var replay = new Button(async () => {
                if (m_Busy || PlayerProfileClient.IsTutorialPractice || PlayerProfileClient.RaidActive) return;
                try {
                    await PlayerProfileClient.BeginTutorialPracticeAsync();
                    Navigate(0);
                } catch (System.Exception e) { m_Content.Q<Label>("archiveKnowledge").text = e.Message; }
            }) { text = "重玩新手教学" };
            replay.SetEnabled(!PlayerProfileClient.IsTutorialPractice && !PlayerProfileClient.RaidActive);
            side.Add(replay);
            MoonkovLocalization.Bind(side);
            return true;
        }
    }
}