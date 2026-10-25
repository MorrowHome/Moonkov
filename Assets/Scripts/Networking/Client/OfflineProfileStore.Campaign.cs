using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class OfflineProfileStore
    {
        private static void ValidateCampaignJson(string json)
        {
            var document = JObject.Parse(json);
            var tutorial = document.GetValue("Tutorial", StringComparison.OrdinalIgnoreCase);
            if (tutorial != null && tutorial.Type != JTokenType.Null)
            {
                if (!(tutorial is JObject block)) throw new InvalidDataException("Invalid tutorial block.");
                foreach (string field in new[] { "Version", "Status", "IntroPage", "CompletedSteps", "SupplyCycle" })
                    if (block.GetValue(field, StringComparison.OrdinalIgnoreCase)?.Type != JTokenType.Integer)
                        throw new InvalidDataException("Tutorial field is missing or invalid: " + field);
            }
            // Match SaveData's default when Format itself is absent. Otherwise a
            // missing version marker could bypass the format 4 shape guard.
            var formatToken = document.GetValue("Format", StringComparison.OrdinalIgnoreCase);
            int format;
            try { format = formatToken == null || formatToken.Type == JTokenType.Null ? 4 : formatToken.ToObject<int>(); }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException || ex is InvalidCastException)
            { throw new InvalidDataException("Single-player save has an invalid format marker.", ex); }
            if (format != 4) return;
            var campaign = document.GetValue("Campaign", StringComparison.OrdinalIgnoreCase) as JObject;
            if (campaign == null) throw new InvalidDataException("Format 4 chapter data is missing or invalid.");
            foreach (string field in new[] { "SchemaVersion", "ContentVersion", "Revision", "NodeMasks", "CompletedTasks", "NodeItemIds", "ProcessedEvents" })
            {
                var value = campaign.GetValue(field, StringComparison.OrdinalIgnoreCase);
                if (value == null || value.Type == JTokenType.Null)
                    throw new InvalidDataException("Format 4 chapter data is missing a required field: " + field);
            }
        }

        private static void ValidateCampaign(SaveData save)
        {
            // Missing in old formats means a fresh chapter, never historical
            // MissionFirsts or old stash fragments being interpreted as story.
            if (save.Campaign == null)
            {
                if (save.Format >= 4)
                    throw new InvalidDataException("Single-player chapter state is missing from a format 4 save.");
                save.Campaign = CampaignRules.Create();
            }
            string error = CampaignRules.Validate(save.Campaign);
            if (error != null || save.ActiveCampaignVersion < 0 || save.ActiveCampaignVersion > 1 ||
                save.DeploymentId == null && save.ActiveCampaignVersion != 0 ||
                save.Clash?.ActiveMatchId != null && save.ActiveCampaignVersion != 0)
                throw new InvalidDataException("Unsupported or damaged single-player chapter state: " + error);
        }

        public CampaignProgress ReadCampaign()
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                return CampaignRules.Clone(m_Save.Campaign);
            }
        }

        public void SetCampaignTracking(string taskId, bool secondary = false, bool remove = false)
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                var next = Copy();
                if (remove) CampaignRules.Untrack(next.Campaign, taskId);
                else if (RaidActive && CampaignCatalog.Find(taskId) != null && !next.Campaign.CompletedTasks.Contains(taskId))
                {
                    // The server-run may already have opened a later step while
                    // the durable profile still waits for extraction. Tracking
                    // is only a display preference, never an authority grant.
                    if (secondary)
                    {
                        if (next.Campaign.PrimaryTracked != taskId && !next.Campaign.SecondaryTracked.Contains(taskId))
                        {
                            if (next.Campaign.SecondaryTracked.Count >= 2)
                                throw new InvalidOperationException("campaign_tracking_full");
                            next.Campaign.SecondaryTracked.Add(taskId);
                            next.Campaign.PreferenceRevision++;
                        }
                    }
                    else
                    {
                        next.Campaign.PrimaryTracked = taskId;
                        next.Campaign.SecondaryTracked.Remove(taskId);
                        next.Campaign.TrackingSuppressed = false;
                        next.Campaign.PreferenceRevision++;
                    }
                }
                else if (!CampaignRules.TryTrack(next.Campaign, taskId, secondary, out var error))
                    throw new InvalidOperationException(error);
                // Copy() starts from the latest snapshot under the profile lock.
                // A UI read of an older revision cannot replace gameplay facts.
                Commit(next);
            }
        }

        private static void CopyCampaignTracking(CampaignProgress source, CampaignProgress target)
        {
            if (source == null || target == null) return;
            target.PrimaryTracked = source.PrimaryTracked;
            target.SecondaryTracked = new List<string>(source.SecondaryTracked);
            target.TrackingSuppressed = source.TrackingSuppressed;
            target.PreferenceRevision = Math.Max(target.PreferenceRevision, source.PreferenceRevision);
        }

        private void ApplyCampaignExtraction(SaveData next, RaidPersistenceContext.Settlement payload, InventoryGraph raid)
        {
            if (payload.CampaignProgress == null) return; // Existing outbox receipts remain valid.
            var run = CampaignRules.Clone(payload.CampaignProgress);
            if (m_LoadingCheckpoints != 0)
                throw new InvalidDataException("Chapter results cannot commit before the world has finished loading.");
            if (next.ActiveCampaignVersion != run.ContentVersion || CampaignRules.Validate(run) != null)
                throw new InvalidDataException("Chapter settlement does not match its deployment content.");

            // The server may have observed the fragment and subsequently lost it.
            // Only an actual, newly recovered source item in this frozen return
            // inventory can satisfy the final tutorial task.
            var definition = CampaignCatalog.Find("CM03");
            int recover = Array.FindIndex(definition.Nodes, node => node.Id == "recover");
            int mask = run.NodeMasks.TryGetValue("CM03", out var value) ? value : 0;
            if (!next.Campaign.CompletedTasks.Contains("CM03") && (mask & (1 << recover)) != 0)
            {
                run.NodeItemIds.TryGetValue("CM03/recover", out var itemId);
                var item = raid.Find(itemId);
                if (item == null || item.Code != "mooncore_fragment" || !raid.Carried(item) || item.MissionRecovered ||
                    !MissionInventory.ValidSource(item))
                {
                    // A successful ordinary return is still valid after dropping
                    // the sample. Save the other chapter steps, not a fake sample
                    // recovery, rather than trapping the entire stash in retry.
                    int extract = Array.FindIndex(definition.Nodes, node => node.Kind == CampaignObjectiveKind.Extract);
                    run.NodeMasks["CM03"] = mask & ~(1 << recover) & ~(1 << extract);
                    run.CompletedTasks.Remove("CM03"); run.NodeItemIds.Remove("CM03/recover");
                }
            }
            next.Campaign = CampaignRules.MergeExtracted(next.Campaign, run);
        }
    }
}
