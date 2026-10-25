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
        private void PrepareFirstChapter(ref SystemState state, RaidMissionWorld context, RaidMissionSite site, double now)
        {
            if (!SystemAPI.HasSingleton<OfflineRaidMode>())
            {
                if (context.FirstChapter)
                {
                    if (context.OutputLootId >= 0)
                    { LootContainers(ref state).Containers.Remove(context.OutputLootId); LootContainers(ref state).MissionCaches.Remove(context.OutputLootId); }
                    context.Operation = null; context.OutputLootId = -1; context.OutputProduced = false;
                    context.Known.Clear(); context.Actors.Clear(); context.CampaignRaidIds.Clear();
                    context.FirstChapter = false; context.NextSend = 0;
                    Persistence(ref state).CampaignRuns.Clear();
                }
                site.SetFirstChapter(false); return;
            }
            var persistence = Persistence(ref state);
            bool hasActive = false, enabled = false, newRaid = false;
            CampaignProgress saved = null;
            foreach (var (session, connection) in SystemAPI.Query<RefRO<RaidSession>>().WithEntityAccess())
            {
                if (session.ValueRO.Phase != RaidPhase.Active || !persistence.Profiles.TryGetValue(connection, out var profile) || profile.Mode != null || profile.CampaignSchemaVersion != 1) continue;
                hasActive = true;
                if (!context.CampaignRaidIds.TryGetValue(connection, out int raid) || raid != session.ValueRO.RaidId)
                {
                    saved = JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
                    if (CampaignRules.Validate(saved) != null) throw new InvalidOperationException("第一章档案未通过校验。");
                    persistence.CampaignRuns[connection] = CampaignRules.Clone(saved);
                    context.CampaignRaidIds[connection] = session.ValueRO.RaidId; newRaid = true;
                }
                if (persistence.CampaignRuns.TryGetValue(connection, out var progress) && !progress.CompletedTasks.Contains("CM03"))
                { enabled = true; saved = progress; }
            }
            if (!hasActive) return;
            if (newRaid || context.FirstChapter != enabled)
            {
                if (context.OutputLootId >= 0)
                { LootContainers(ref state).Containers.Remove(context.OutputLootId); LootContainers(ref state).MissionCaches.Remove(context.OutputLootId); }
                context.Operation = null; context.OutputLootId = -1; context.OutputProduced = false;
                context.Known.Clear(); context.Actors.Clear(); context.EndedAt = 0; context.NextSend = 0; context.CampaignNextCheck = 0;
                context.FirstChapter = enabled; site.SetFirstChapter(enabled);
            }
            if (!enabled || context.Operation != null || now < context.CampaignNextCheck) return;
            context.CampaignNextCheck = now + .5;
            int index = Array.FindIndex(site.Definitions, d => d.Id == "mc00_mooncore");
            if (index < 0 || !MissionAnchorsReady(site, site.Definitions[index])) return;
            var definition = site.Definitions[index];
            context.Operation = new Missions.MissionOperation(context.WorldId, Guid.NewGuid().ToString("D"), ++context.Epoch, definition, now);
            context.DefinitionIndex = index;
            uint restored = 0;
            for (int i = 0; i < definition.Objectives.Length; i++)
            {
                string id = definition.Objectives[i].Id;
                if (id == "report" && saved.CompletedTasks.Contains("CM01") ||
                    (id == "power_a" || id == "power_b") && saved.CompletedTasks.Contains("CM02")) restored |= 1u << i;
            }
            context.Operation.RestoreCompleted(restored, now);
        }

        private void ObserveFirstChapter(ref SystemState state, RaidMissionWorld context)
        {
            if (!context.FirstChapter || context.Operation == null || !SystemAPI.HasSingleton<OfflineRaidMode>()) return;
            var persistence = Persistence(ref state);
            var definition = context.Site.Definitions[context.DefinitionIndex];
            foreach (var (session, joined, connection) in SystemAPI.Query<RefRO<RaidSession>, RefRO<JoinedClient>>().WithEntityAccess())
            {
                if (session.ValueRO.Phase != RaidPhase.Active || !persistence.CampaignRuns.TryGetValue(connection, out var run) ||
                    !TryGetLivingPosition(ref state, joined.ValueRO.PlayerEntity, out var position)) continue;
                var inventory = GetRaidInventory(ref state, connection).Graph;
                var entry = context.Site.Anchor("entry");
                if (entry != null && math.distancesq(position, entry.Approach) < 12 * 12 && math.abs(position.y - entry.Approach.y) < 4)
                { RecordChapterFact(run, "CM01", "arrive", session.ValueRO, inventory.Version); context.Known.Add(connection); }
                // If a sample is dropped, the live task follows real ownership again.
                if (run.NodeItemIds.TryGetValue("CM03/recover", out string prior) &&
                    (inventory.Find(prior) == null || !inventory.Carried(inventory.Find(prior))))
                    CampaignRules.ForgetUncommittedItem(run, "CM03", "recover", prior);
                foreach (var task in CampaignCatalog.Tasks)
                {
                    if (!CampaignRules.Available(run, task.Id) || run.CompletedTasks.Contains(task.Id)) continue;
                    foreach (var node in task.Nodes)
                    {
                        if (node.ExistingObjectiveId == null) continue;
                        int index = Array.FindIndex(definition.Objectives, n => n.Id == node.ExistingObjectiveId);
                        if (index >= 0 && context.Operation.Completed(index)) RecordChapterFact(run, task.Id, node.Id, session.ValueRO, inventory.Version);
                    }
                }
                if (!CampaignRules.Available(run, "CM03")) continue;
                foreach (var item in inventory.Items)
                    if (item.Code == "mooncore_fragment" && !item.MissionRecovered && MissionInventory.ValidSource(item) && inventory.Carried(item) &&
                        item.MissionWorldId == context.WorldId && item.MissionOperationId == context.Operation.Id)
                    { RecordChapterFact(run, "CM03", "recover", session.ValueRO, inventory.Version, item); break; }
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

        private void AddFirstChapterSnapshot(ref SystemState state, RaidMissionWorld context, Entity connection, RaidSession session, ref RaidMissionSnapshotRpc snapshot)
        {
            if (!context.FirstChapter) return;
            var persistence = Persistence(ref state);
            if (!persistence.CampaignRuns.TryGetValue(connection, out var run)) return;
            if (session.Phase != RaidPhase.Active && session.SaveState == RaidSaveState.Saved && persistence.Profiles.TryGetValue(connection, out var profile) && !string.IsNullOrEmpty(profile.CampaignJson))
                run = JsonConvert.DeserializeObject<CampaignProgress>(profile.CampaignJson);
            snapshot.CampaignEnabled = true;
            snapshot.CampaignNodes01 = (byte)CampaignRules.Mask(run, "CM01");
            snapshot.CampaignNodes02 = (byte)CampaignRules.Mask(run, "CM02");
            snapshot.CampaignNodes03 = (byte)CampaignRules.Mask(run, "CM03");
            if (run.NodeItemIds.TryGetValue("CM03/recover", out string sampleId)) snapshot.CampaignSampleId = sampleId;
            for (int i = 0; i < CampaignCatalog.Tasks.Length; i++)
                if (run.CompletedTasks.Contains(CampaignCatalog.Tasks[i].Id)) snapshot.CampaignCompletedMask |= (byte)(1 << i);
        }
    }
}
