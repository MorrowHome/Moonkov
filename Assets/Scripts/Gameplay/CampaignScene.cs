using Unity.Entities;
using UnityEngine;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS
{
    // Presentation reads server facts. Coordinates always come from scene authoring.
    public static class CampaignScene
    {
        public static CampaignProgress ReadProgress(World clientWorld = null)
        {
            var progress = CampaignRules.Clone(PlayerProfileClient.Campaign);
            if (!TrySnapshot(clientWorld, out var snapshot) || !snapshot.CampaignEnabled) return progress;
            progress.NodeMasks["CM01"] = snapshot.CampaignNodes01;
            progress.NodeMasks["CM02"] = snapshot.CampaignNodes02;
            progress.NodeMasks["CM03"] = snapshot.CampaignNodes03;
            if (snapshot.CampaignSampleId.Length > 0) progress.NodeItemIds["CM03/recover"] = snapshot.CampaignSampleId.ToString();
            else progress.NodeItemIds.Remove("CM03/recover");
            for (int i = 0; i < CampaignCatalog.Tasks.Length; i++)
                if ((snapshot.CampaignCompletedMask & (1 << i)) != 0) progress.CompletedTasks.Add(CampaignCatalog.Tasks[i].Id);
            if (!progress.TrackingSuppressed && progress.PrimaryTracked != null &&
                CampaignRules.Status(progress, progress.PrimaryTracked) == CampaignTaskStatus.AwaitingExtraction)
            {
                string next = CampaignRules.CurrentMain(progress);
                if (next != null) progress.PrimaryTracked = next;
            }
            return progress;
        }

        public static bool TryDestination(World clientWorld, CampaignProgress progress, string taskId, out Vector3 position, out string location) =>
            TryDestination(clientWorld, progress, taskId, null, out position, out location);

        public static bool TryDestination(World clientWorld, CampaignProgress progress, string taskId, Vector3? origin, out Vector3 position, out string location)
        {
            position = default; location = null;
            var node = CampaignRules.CurrentNode(progress, taskId);
            if (node == null) return false;
            if (node.AnchorId == "ship.return")
            {
                var map = MoonRaidMap.Active; if (!map) return false;
                position = origin.HasValue ? map.NearestExtraction(origin.Value) : map.ExtractionPosition;
                location = "返回撤离点"; return true;
            }
            var site = RaidMissionSite.Active; if (!site) return false;
            bool discovered = TrySnapshot(clientWorld, out var snapshot) && snapshot.OperationId.Length > 0;
            var anchor = site.Anchor(discovered ? node.AnchorId : "entry");
            if (anchor == null) return false;
            position = anchor.Approach;
            location = discovered ? anchor.Region + " " + anchor.Label : "研究站入口";
            return true;
        }

        public static bool TrySnapshot(World world, out RaidMissionSnapshotRpc snapshot)
        {
            snapshot = default;
            if (world == null || !world.IsCreated) return false;
            using var query = world.EntityManager.CreateEntityQuery(typeof(RaidMissionClientState), typeof(RaidClientState));
            if (query.CalculateEntityCount() != 1) return false;
            snapshot = query.GetSingleton<RaidMissionClientState>().Snapshot;
            return snapshot.RaidId == query.GetSingleton<RaidClientState>().Snapshot.RaidId;
        }
    }
}
