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
        // UI treats this cache as read-only. All writes are intents handled by
        // the selected store, and campaign facts come from the local server.
        public static CampaignProgress Campaign => IsOffline ? s_Campaign : null;

        private static void RefreshCampaign(RaidPersistenceContext.Profile profile)
        {
            s_Campaign = string.IsNullOrEmpty(profile.CampaignJson)
                ? CampaignRules.Create() : JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
            string error = CampaignRules.Validate(s_Campaign);
            if (error != null) throw new InvalidOperationException("Invalid chapter profile: " + error);
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
