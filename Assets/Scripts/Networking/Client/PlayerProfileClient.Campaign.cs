using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS
{
    public static partial class PlayerProfileClient
    {
        private static CampaignProgress s_Campaign;
        private static ResearchQuestProgress s_ResearchQuests;
        // UI treats this cache as read-only. All writes are intents handled by
        // the selected store, and campaign facts come from the local server.
        public static CampaignProgress Campaign => IsOffline ? s_Campaign : null;
        public static ResearchQuestProgress ResearchQuests
        {
            get
            {
                if (IsLan) return LanRoomClient.Profile.Profile.ResearchQuests;
                if (IsOffline) return IsTutorialPractice ? null : s_ResearchQuests;
                return AccountClient.IsLoggedIn ? AccountClient.ResearchQuests : null;
            }
        }

        private static void RefreshCampaign(RaidPersistenceContext.Profile profile)
        {
            s_ResearchQuests = profile.ResearchQuests;
            s_Campaign = string.IsNullOrEmpty(profile.CampaignJson)
                ? CampaignRules.Create() : JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
            string error = CampaignRules.Validate(s_Campaign);
            if (error != null) throw new InvalidOperationException("Invalid chapter profile: " + error);
        }

        public static async Task<bool> AdvanceResearchQuestAsync(ResearchQuest quest, bool claim)
        {
            if (ResearchQuests == null) return false;
            if (!IsOffline)
                return await SendResearchCommand(new ResearchQuestCommand { Quest = quest,
                    Action = claim ? ResearchQuestAction.Claim : ResearchQuestAction.Accept });
            var store = s_Offline;
            bool changed = await Task.Run(() => store.AdvanceResearchQuest(quest, claim));
            if (store != s_Offline) return false;
            if (changed) RefreshOffline();
            return changed;
        }

        public static async Task<bool> ChooseResearchEndingAsync(ResearchEnding ending)
        {
            if (ResearchQuests == null) return false;
            if (!IsOffline)
                return await SendResearchCommand(new ResearchQuestCommand { Action = ResearchQuestAction.Decide, Ending = ending });
            var store = s_Offline;
            bool changed = await Task.Run(() => store.ChooseResearchEnding(ending));
            if (store != s_Offline) return false;
            if (changed) RefreshOffline();
            return changed;
        }

        private static async Task<bool> SendResearchCommand(ResearchQuestCommand command)
        {
            if (RaidActive) return false;
            if (IsLan) await LanRoomClient.RequestAsync("research", command);
            else await AccountClient.AdvanceResearchQuestAsync(command, CancellationToken.None);
            return true;
        }

        public static async Task<bool> ReviewResearchStoryAsync(ResearchStoryChapter chapter)
        {
            if (!ResearchQuestRules.StoryAvailable(ResearchQuests, chapter)) return false;
            var command = new ResearchQuestCommand { Action = ResearchQuestAction.ReviewStory, Chapter = chapter };
            if (!IsOffline) return await SendResearchCommand(command);
            var store = s_Offline;
            bool saved = await Task.Run(() => store.ApplyResearchCommand(command));
            if (store != s_Offline) return false;
            if (saved) RefreshOffline();
            return saved;
        }

        public static async Task SetCampaignTrackingAsync(string taskId, bool secondary = false,
            bool remove = false, CancellationToken ct = default)
        {
            if (!IsOffline) throw new InvalidOperationException("章节教学仅在单人模式开放。");
            var store = s_Offline;
            await Task.Run(() => store.SetCampaignTracking(taskId, secondary, remove), ct);
            ct.ThrowIfCancellationRequested();
            if (store == s_Offline) RefreshOffline();
        }
    }
}
