using System;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private void PrepareCampaign(ref SystemState state, RaidMissionWorld context, RaidMissionSite site, double now)
        {
            if (!SystemAPI.HasSingleton<OfflineRaidMode>())
            {
                if (context.CampaignChapter != 0)
                {
                    if (context.OutputLootId >= 0)
                    { LootContainers(ref state).Containers.Remove(context.OutputLootId); LootContainers(ref state).MissionCaches.Remove(context.OutputLootId); }
                    context.Operation = null; context.OutputLootId = -1; context.OutputProduced = false;
                    context.Known.Clear(); context.Actors.Clear(); context.CampaignRaidIds.Clear();
                    context.CampaignChapter = 0; context.NextSend = 0;
                    Persistence(ref state).CampaignRuns.Clear();
                }
                site.SetCampaignChapter(0); return;
            }
            var persistence = Persistence(ref state);
            bool hasActive = false, newRaid = false;
            int chapter = 0;
            CampaignProgress saved = null;
            foreach (var (session, connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess())
            {
                if (session.ValueRO.Phase != RaidPhase.Active || !persistence.Profiles.TryGetValue(connection, out var profile) || profile.Mode != null || profile.CampaignSchemaVersion != 1) continue;
                hasActive = true;
                if (!context.CampaignRaidIds.TryGetValue(connection, out int raid) || raid != session.ValueRO.RaidId)
                {
                    saved = JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
                    if (CampaignRules.Validate(saved) != null) throw new InvalidOperationException("战役档案未通过校验。");
                    persistence.CampaignRuns[connection] = CampaignRules.Clone(saved);
                    context.CampaignRaidIds[connection] = session.ValueRO.RaidId; newRaid = true;
                }
                if (!persistence.CampaignRuns.TryGetValue(connection, out var progress)) continue;
                if (!progress.CompletedTasks.Contains("CM03")) { chapter = 1; saved = progress; }
                else if (!progress.CompletedTasks.Contains("CM04")) { chapter = 2; saved = progress; }
            }
            if (!hasActive) return;
            if (newRaid || context.CampaignChapter != chapter)
            {
                if (context.OutputLootId >= 0)
                { LootContainers(ref state).Containers.Remove(context.OutputLootId); LootContainers(ref state).MissionCaches.Remove(context.OutputLootId); }
                context.Operation = null; context.OutputLootId = -1; context.OutputProduced = false;
                context.Known.Clear(); context.Actors.Clear(); context.EndedAt = 0; context.NextSend = 0; context.CampaignNextCheck = 0;
                context.CampaignChapter = chapter; site.SetCampaignChapter(chapter);
            }
            if (chapter == 0 || context.Operation != null || now < context.CampaignNextCheck) return;
            context.CampaignNextCheck = now + .5;
            string operationId = chapter == 1 ? "mc00_mooncore" : "mc00_archive";
            int index = Array.FindIndex(site.Definitions, d => d.Id == operationId);
            if (index < 0 || !MissionAnchorsReady(site, site.Definitions[index])) return;
            var definition = site.Definitions[index];
            context.Operation = new Missions.MissionOperation(context.WorldId, Guid.NewGuid().ToString("D"), ++context.Epoch, definition, now);
            context.DefinitionIndex = index;
            uint restored = 0;
            for (int i = 0; i < definition.Objectives.Length; i++)
            {
                string id = definition.Objectives[i].Id;
                if (chapter == 1 && (id == "report" && saved.CompletedTasks.Contains("CM01") ||
                    (id == "power_a" || id == "power_b") && saved.CompletedTasks.Contains("CM02"))) restored |= 1u << i;
            }
            context.Operation.RestoreCompleted(restored, now);
        }

        private void ObserveCampaign(ref SystemState state, RaidMissionWorld context)
        {
            if (context.CampaignChapter == 0 || context.Operation == null || !SystemAPI.HasSingleton<OfflineRaidMode>()) return;
            var persistence = Persistence(ref state);
            var definition = context.Site.Definitions[context.DefinitionIndex];
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                if (session.ValueRO.Phase != RaidPhase.Active || !persistence.CampaignRuns.TryGetValue(connection, out var run) ||
                    !TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var position)) continue;
                var inventory = GetRaidInventory(ref state, connection).Graph;
                var entry = context.Site.Anchor("entry");
                if (context.CampaignChapter == 1 && entry != null && math.distancesq(position, entry.Approach) < 12 * 12 && math.abs(position.y - entry.Approach.y) < 4)
                { RecordChapterFact(run, "CM01", "arrive", session.ValueRO, inventory.Version); context.Known.Add(connection); }
                // If an artifact is dropped, the live task follows real ownership again.
                string taskId = context.CampaignChapter == 1 ? "CM03" : "CM04";
                string code = context.CampaignChapter == 1 ? "mooncore_fragment" : "mc00_archive_drive";
                if (run.NodeItemIds.TryGetValue(taskId + "/recover", out string prior) &&
                    (inventory.Find(prior) == null || !inventory.Carried(inventory.Find(prior))))
                    CampaignRules.ForgetUncommittedItem(run, taskId, "recover", prior);
                CampaignMissionAdapter.RecordCompleted(run, context.CampaignChapter, definition,
                    context.Operation.CompletedMask, context.Operation.Id, inventory.Version);
                if (!CampaignRules.Available(run, taskId)) continue;
                foreach (var item in inventory.Items)
                    if (item.Code == code && !item.MissionRecovered && MissionInventory.ValidSource(item) && inventory.Carried(item) &&
                        item.MissionWorldId == context.WorldId && item.MissionOperationId == context.Operation.Id)
                    { RecordChapterFact(run, taskId, "recover", session.ValueRO, inventory.Version, item); break; }
            }
        }

        private static void RecordChapterFact(CampaignProgress run, string taskId, string nodeId, RaidSession session, int inventoryVersion, InventoryItem item = null)
        {
            var task = CampaignCatalog.Find(taskId);
            int index = Array.FindIndex(task.Nodes, n => n.Id == nodeId);
            if (index < 0 || CampaignRules.NodeCompleted(run, taskId, index)) return;
            var node = task.Nodes[index];
            CampaignRules.TryRecord(run, new CampaignFact { TaskId = taskId, NodeId = nodeId, Kind = node.Kind, SourceId = node.SourceId,
                EventId = session.SettlementId + "/" + taskId + "/" + nodeId + "/" + inventoryVersion,
                ItemId = item?.Id, ItemCode = item?.Code }, out _);
        }

        private void AddCampaignSnapshot(ref SystemState state, RaidMissionWorld context, Entity connection, RaidSession session, ref RaidMissionSnapshotRpc snapshot)
        {
            if (context.CampaignChapter == 0) return;
            var persistence = Persistence(ref state);
            if (!persistence.CampaignRuns.TryGetValue(connection, out var run)) return;
            if (session.Phase != RaidPhase.Active && session.SaveState == RaidSaveState.Saved && persistence.Profiles.TryGetValue(connection, out var profile) && !string.IsNullOrEmpty(profile.CampaignJson))
                run = JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
            snapshot.CampaignEnabled = true;
            snapshot.CampaignChapter = (byte)context.CampaignChapter;
            snapshot.CampaignNodes01 = (byte)CampaignRules.Mask(run, "CM01");
            snapshot.CampaignNodes02 = (byte)CampaignRules.Mask(run, "CM02");
            snapshot.CampaignNodes03 = (byte)CampaignRules.Mask(run, "CM03");
            snapshot.CampaignNodes04 = (byte)CampaignRules.Mask(run, "CM04");
            if (run.NodeItemIds.TryGetValue("CM03/recover", out string sampleId)) snapshot.CampaignSampleId = sampleId;
            if (run.NodeItemIds.TryGetValue("CM04/recover", out string archiveId)) snapshot.CampaignArchiveId = archiveId;
            for (int i = 0; i < CampaignCatalog.Tasks.Length; i++)
                if (run.CompletedTasks.Contains(CampaignCatalog.Tasks[i].Id)) snapshot.CampaignCompletedMask |= (byte)(1 << i);
        }
    }
}
