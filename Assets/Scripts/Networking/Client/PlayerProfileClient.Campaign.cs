// 给战役和研究页面提供档案入口。研究命令按当前模式发到房主、账号后端或本机档案，单人战役的追踪选项由离线档案保存。
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS {
    public static partial class PlayerProfileClient {
        private static CampaignProgress s_Campaign;
        private static ResearchQuestProgress s_ResearchQuests;

        // UI 只读这份缓存。需要修改时发出意图，由
        // 当前选定的档案处理。章节事实来自本机服务端。
        public static CampaignProgress Campaign => IsOffline ? s_Campaign : null;

        public static ResearchQuestProgress ResearchQuests {
            get {
                if (IsLan) return LanRoomClient.Profile.Profile.ResearchQuests;
                if (IsOffline) return IsTutorialPractice ? null : s_ResearchQuests;
                return AccountClient.IsLoggedIn ? AccountClient.ResearchQuests : null;
            }
        }

        private static void RefreshCampaign(RaidPersistenceContext.Profile profile) {
            s_ResearchQuests = profile.ResearchQuests;
            s_Campaign = string.IsNullOrEmpty(profile.CampaignJson)
                ? CampaignRules.Create()
                : JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
            string error = CampaignRules.Validate(s_Campaign);
            if (error != null) throw new InvalidOperationException("Invalid chapter profile: " + error);
        }

        // 接任务和领取是不同动作。单人改本地档，好友与在线包成研究命令让它们的权威档案处理。
        public static async Task<bool> AdvanceResearchQuestAsync(ResearchQuest quest, bool claim) {
            if (ResearchQuests == null) return false;
            if (!IsOffline)
                return await SendResearchCommand(new ResearchQuestCommand {
                    Quest = quest,
                    Action = claim ? ResearchQuestAction.Claim : ResearchQuestAction.Accept
                });
            var store = s_Offline;
            bool changed = await Task.Run(() => store.AdvanceResearchQuest(quest, claim));
            if (store != s_Offline) return false;
            if (changed) RefreshOffline();
            return changed;
        }

        // 结局选择也属于档案操作。单人提交结束后先确认还在原档案，再刷新菜单里能看到的剧情和奖励。
        public static async Task<bool> ChooseResearchEndingAsync(ResearchEnding ending) {
            if (ResearchQuests == null) return false;
            if (!IsOffline)
                return await SendResearchCommand(new ResearchQuestCommand
                    { Action = ResearchQuestAction.Decide, Ending = ending });
            var store = s_Offline;
            bool changed = await Task.Run(() => store.ChooseResearchEnding(ending));
            if (store != s_Offline) return false;
            if (changed) RefreshOffline();
            return changed;
        }

        private static async Task<bool> SendResearchCommand(ResearchQuestCommand command) {
            // 出战已经接管档案时，仓库界面不能同时改研究状态。这时等待该局结算。
            if (RaidActive) return false;
            if (IsLan)
                await LanRoomClient.RequestAsync("research", command);
            else
                await AccountClient.AdvanceResearchQuestAsync(command, CancellationToken.None);
            return true;
        }

        // 先看章节是否已解锁，再发一次读过的命令给当前档案。服务端也按自己的进度校验。
        public static async Task<bool> ReviewResearchStoryAsync(ResearchStoryChapter chapter) {
            if (!ResearchQuestRules.StoryAvailable(ResearchQuests, chapter)) return false;
            var command = new ResearchQuestCommand { Action = ResearchQuestAction.ReviewStory, Chapter = chapter };
            if (!IsOffline) return await SendResearchCommand(command);
            var store = s_Offline;
            bool saved = await Task.Run(() => store.ApplyResearchCommand(command));
            if (store != s_Offline) return false;
            if (saved) RefreshOffline();
            return saved;
        }

        // 章节追踪只在单人使用。磁盘写完后若玩家已换档，别拿旧档的数据更新新页面。
        public static async Task SetCampaignTrackingAsync(
            string taskId,
            bool secondary = false,
            bool remove = false,
            CancellationToken ct = default) {
            if (!IsOffline) throw new InvalidOperationException("章节教学仅在单人模式开放。");
            var store = s_Offline;
            await Task.Run(() => store.SetCampaignTracking(taskId, secondary, remove), ct);
            ct.ThrowIfCancellationRequested();
            if (store == s_Offline) RefreshOffline();
        }
    }
}