using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS
{
    public static partial class PlayerProfileClient
    {
        private static OfflineProfileStore s_TutorialReturnStore;
        private static TutorialProgress s_Tutorial;
        public static TutorialProgress Tutorial => IsOffline ? s_Tutorial : null;
        public static bool IsTutorialPractice => s_TutorialReturnStore != null;

        public static async Task UpdateTutorialAsync(bool skip = false, bool nextIntro = false, int steps = 0)
        {
            var store = s_Offline;
            if (store == null) return;
            await Task.Run(() => store.UpdateTutorial(skip, nextIntro, steps));
            if (store == s_Offline) s_Tutorial = store.ReadTutorial();
        }
        public static async Task<string> ClaimTutorialSuppliesAsync()
        {
            var store = OfflineStore;
            var result = await Task.Run(store.ClaimTutorialSupplies);
            if (store == s_Offline) RefreshOffline();
            return result;
        }
        public static async Task BeginTutorialPracticeAsync()
        {
            if (!IsOffline || IsTutorialPractice || RaidActive) throw new InvalidOperationException("请先结束当前行动。 ");
            string path = Path.Combine(Path.GetDirectoryName(s_Offline.FilePath), "TutorialPractice", Guid.NewGuid().ToString("N"), "profile.json");
            var previous = s_Offline;
            var practice = await Task.Run(() => new OfflineProfileStore(path));
            if (s_Offline != previous || previous.RaidActive) { practice.Dispose(); return; }
            s_TutorialReturnStore = previous; s_Offline = practice; RefreshOffline();
        }
        public static void EndTutorialPractice()
        {
            if (!IsTutorialPractice || RaidActive) throw new InvalidOperationException("请先结束练习行动。 ");
            var practice = s_Offline;
            s_Offline = s_TutorialReturnStore; s_TutorialReturnStore = null;
            practice.Dispose(); RefreshOffline();
        }
    }
}
