// 把战役任务的步骤翻译成当前地图上的目的地。CampaignRules 给任务节点，RaidMissionSite 和 MoonRaidMap 给它实际坐标。
// HUD 和地图共用这套目标。探索阶段先指向研究站入口，返航阶段改指撤离点。
using Unity.Entities;
using UnityEngine;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS {
    // 显示层读取服务端确认的数据，坐标取自场景配置。
    public static class CampaignScene {
        public static CampaignProgress ReadProgress(World clientWorld = null) {
            // 先复制档案中的持久进度。局内当前任务只能补到这个显示副本里。
            var progress = CampaignRules.Clone(PlayerProfileClient.Campaign);
            if (!TrySnapshot(clientWorld, out var snapshot) || !snapshot.CampaignEnabled) return progress;
            // 局内快照包含各任务做过的节点和捡到的两类任务道具编号。用它覆盖这局临时进度，地图和 HUD 便能马上更新。
            progress.NodeMasks["CM01"] = snapshot.CampaignNodes01;
            progress.NodeMasks["CM02"] = snapshot.CampaignNodes02;
            progress.NodeMasks["CM03"] = snapshot.CampaignNodes03;
            progress.NodeMasks["CM04"] = snapshot.CampaignNodes04;
            if (snapshot.CampaignSampleId.Length > 0)
                progress.NodeItemIds["CM03/recover"] = snapshot.CampaignSampleId.ToString();
            else
                progress.NodeItemIds.Remove("CM03/recover");
            if (snapshot.CampaignArchiveId.Length > 0)
                progress.NodeItemIds["CM04/recover"] = snapshot.CampaignArchiveId.ToString();
            else
                progress.NodeItemIds.Remove("CM04/recover");
            for (int i = 0; i < CampaignCatalog.Tasks.Length; i++)
                if ((snapshot.CampaignCompletedMask & (1 << i)) != 0)
                    progress.CompletedTasks.Add(CampaignCatalog.Tasks[i].Id);
            // 正追踪的任务已做完局内部分、只差撤离时，如果跟踪未关闭，尝试把标记让给下一项主线。
            if (!progress.TrackingSuppressed && progress.PrimaryTracked != null &&
                CampaignRules.Status(progress, progress.PrimaryTracked) == CampaignTaskStatus.AwaitingExtraction) {
                string next = CampaignRules.CurrentMain(progress);
                if (next != null) progress.PrimaryTracked = next;
            }
            return progress;
        }

        public static bool TryDestination(
            World clientWorld,
            CampaignProgress progress,
            string taskId,
            out Vector3 position,
            out string location) {
            return TryDestination(clientWorld, progress, taskId, null, out position, out location);
        }

        public static bool TryDestination(
            World clientWorld,
            CampaignProgress progress,
            string taskId,
            Vector3? origin,
            out Vector3 position,
            out string location) {
            position = default;
            location = null;
            var node = CampaignRules.CurrentNode(progress, taskId);
            if (node == null) return false;
            // 返航节点指向地图撤离区。调用方给了角色位置时取离它最近的出口。
            if (node.AnchorId == "ship.return") {
                var map = MoonRaidMap.Active;
                if (!map) return false;
                position = origin.HasValue ? map.NearestExtraction(origin.Value) : map.ExtractionPosition;
                location = "返回撤离点";
                return true;
            }
            var site = RaidMissionSite.Active;
            if (!site) return false;
            // 服务器尚未告知发现研究站前，只标出入口。发现后才给出站内的实际交互点。
            bool discovered = TrySnapshot(clientWorld, out var snapshot) && snapshot.OperationId.Length > 0;
            var anchor = site.Anchor(discovered ? node.AnchorId : "entry");
            if (anchor == null) return false;
            position = anchor.Approach;
            location = discovered ? anchor.Region + " " + anchor.Label : "研究站入口";
            return true;
        }

        public static bool TrySnapshot(World world, out RaidMissionSnapshotRpc snapshot) {
            snapshot = default;
            if (world == null || !world.IsCreated) return false;
            using var query =
                world.EntityManager.CreateEntityQuery(typeof(RaidMissionClientState), typeof(RaidClientState));
            if (query.CalculateEntityCount() != 1) return false;
            snapshot = query.GetSingleton<RaidMissionClientState>().Snapshot;
            return snapshot.RaidId == query.GetSingleton<RaidClientState>().Snapshot.RaidId;
        }
    }
}