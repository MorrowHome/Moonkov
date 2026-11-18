// 主菜单通过这里更新单人教学进度和领取补给。重复练习时会切进一个单独的临时档案，结束练习再切回原来的档案。
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS {
    public static partial class PlayerProfileClient {
        private static OfflineProfileStore s_TutorialReturnStore;
        private static TutorialProgress s_Tutorial;
        public static TutorialProgress Tutorial => IsOffline ? s_Tutorial : null;
        // 保存了返回档案的引用，说明当前正在隔离练习。此时练习进度仍写到它自己的临时档。
        public static bool IsTutorialPractice => s_TutorialReturnStore != null;

        public static async Task UpdateTutorialAsync(bool skip = false, bool nextIntro = false, int steps = 0) {
            var store = s_Offline;
            if (store == null) return;
            // 引导更新后台写盘，回到主线程只刷新当时仍在使用的档案数据。
            await Task.Run(() => store.UpdateTutorial(skip, nextIntro, steps));
            if (store == s_Offline) s_Tutorial = store.ReadTutorial();
        }

        public static async Task<string> ClaimTutorialSuppliesAsync() {
            var store = OfflineStore;
            string result = await Task.Run(store.ClaimTutorialSupplies);
            if (store == s_Offline) RefreshOffline();
            return result;
        }

        public static async Task BeginTutorialPracticeAsync() {
            // 切练习前等正式仓库操作都收尾。此时已有活动出战则拒绝切换，否则可能把正在结算的档换走。
            await FlushMenuInventoryAsync(CancellationToken.None);
            if (!IsOffline || IsTutorialPractice || RaidActive) throw new InvalidOperationException("请先结束当前行动。 ");
            string path = Path.Combine(Path.GetDirectoryName(s_Offline.FilePath), "TutorialPractice",
                Guid.NewGuid().ToString("N"), "profile.json");
            var previous = s_Offline;
            var practice = await Task.Run(() => new OfflineProfileStore(path));
            // 新练习档在后台建立时原档可能已经变了。确认归属没变才切换，否则释放刚开的档。
            if (s_Offline != previous || previous.RaidActive) {
                practice.Dispose();
                return;
            }
            s_TutorialReturnStore = previous;
            s_Offline = practice;
            RefreshOffline();
        }

        public static void EndTutorialPractice() {
            if (!IsTutorialPractice || RaidActive) throw new InvalidOperationException("请先结束练习行动。 ");
            var practice = s_Offline;
            // 练习结束确认没有活动出战，恢复原仓库引用后再关闭临时档案。练习局带出的东西只会留在练习档。
            s_Offline = s_TutorialReturnStore;
            s_TutorialReturnStore = null;
            practice.Dispose();
            RefreshOffline();
        }
    }
}