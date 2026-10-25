#nullable disable
using System;
using System.Collections.Generic;

namespace Unity.MP_FPS.Campaign
{
    public enum CampaignObjectiveKind : byte { ReachArea, Interact, Configure, Process, RecoverItem, Extract }
    public enum CampaignSavePolicy : byte { RunOnly, OnExtraction }
    public enum CampaignTaskStatus : byte { Locked, Available, InProgress, AwaitingExtraction, Completed }

    [Serializable]
    public sealed class NodeDefinition
    {
        public string Id, AnchorId, SourceId, Action, Location, Briefing;
        public string ExistingObjectiveId, RequiredItemCode, OutputItemCode;
        public CampaignObjectiveKind Kind;
        public CampaignSavePolicy SavePolicy;
        public int Prerequisites, RequiredQuantity = 1, OutputQuantity = 1;
        public float Seconds;
    }

    [Serializable]
    public sealed class TaskDefinition
    {
        public string Id, Title, ChapterTitle, Briefing, CompletionText, PrerequisiteTask;
        public int Chapter;
        public bool Optional, RequireSavedPrerequisite;
        public NodeDefinition[] Nodes;
    }

    // Stored in the existing offline profile. A server run uses a clone until extraction.
    [Serializable]
    public sealed class CampaignProgress
    {
        public int SchemaVersion = 1, ContentVersion = 1, Revision, PreferenceRevision;
        public Dictionary<string, int> NodeMasks = new Dictionary<string, int>(StringComparer.Ordinal);
        public HashSet<string> CompletedTasks = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, string> NodeItemIds = new Dictionary<string, string>(StringComparer.Ordinal);
        public HashSet<string> ProcessedEvents = new HashSet<string>(StringComparer.Ordinal);
        public string PrimaryTracked = "CM01";
        public List<string> SecondaryTracked = new List<string>();
        public bool TrackingSuppressed;
    }

    // Only an authoritative adapter creates facts. These are not client success requests.
    [Serializable]
    public sealed class CampaignFact
    {
        public string TaskId, NodeId, SourceId, EventId, ItemId, ItemCode;
        public CampaignObjectiveKind Kind;
        public int ContentVersion = 1;
        public bool Extracted;
    }

    public static class CampaignRules
    {
        public const int MaxProcessedEvents = 128;

        public static CampaignProgress Create() => new CampaignProgress();

        public static CampaignProgress Clone(CampaignProgress p)
        {
            if (p == null) return Create();
            return new CampaignProgress
            {
                SchemaVersion = p.SchemaVersion, ContentVersion = p.ContentVersion,
                Revision = p.Revision, PreferenceRevision = p.PreferenceRevision,
                NodeMasks = p.NodeMasks == null ? null : new Dictionary<string, int>(p.NodeMasks, StringComparer.Ordinal),
                CompletedTasks = p.CompletedTasks == null ? null : new HashSet<string>(p.CompletedTasks, StringComparer.Ordinal),
                NodeItemIds = p.NodeItemIds == null ? null : new Dictionary<string, string>(p.NodeItemIds, StringComparer.Ordinal),
                ProcessedEvents = p.ProcessedEvents == null ? null : new HashSet<string>(p.ProcessedEvents, StringComparer.Ordinal),
                PrimaryTracked = p.PrimaryTracked,
                SecondaryTracked = p.SecondaryTracked == null ? null : new List<string>(p.SecondaryTracked),
                TrackingSuppressed = p.TrackingSuppressed
            };
        }

        public static string Validate(CampaignProgress p)
        {
            if (p == null || p.SchemaVersion != 1 || p.ContentVersion != CampaignCatalog.Version || p.Revision < 0 ||
                p.PreferenceRevision < 0 || p.NodeMasks == null || p.CompletedTasks == null || p.NodeItemIds == null ||
                p.ProcessedEvents == null || p.SecondaryTracked == null || p.ProcessedEvents.Count > MaxProcessedEvents ||
                p.NodeMasks.Count > CampaignCatalog.Tasks.Length || p.CompletedTasks.Count > CampaignCatalog.Tasks.Length ||
                p.NodeItemIds.Count > 24 || p.SecondaryTracked.Count > 2) return "campaign_profile";
            foreach (var pair in p.NodeMasks)
            {
                var task = CampaignCatalog.Find(pair.Key);
                if (task == null || pair.Value < 0 || (pair.Value & ~All(task)) != 0) return "campaign_nodes";
                for (int i = 0; i < task.Nodes.Length; i++)
                    if ((pair.Value & (1 << i)) != 0 && (pair.Value & task.Nodes[i].Prerequisites) != task.Nodes[i].Prerequisites)
                        return "campaign_node_order";
                if (pair.Value != 0 && task.PrerequisiteTask != null && !p.CompletedTasks.Contains(task.PrerequisiteTask) &&
                    (task.RequireSavedPrerequisite || !FieldComplete(p, CampaignCatalog.Find(task.PrerequisiteTask))))
                    return "campaign_task_order";
                for (int i = 0; i < task.Nodes.Length; i++)
                    if ((pair.Value & (1 << i)) != 0 && task.Nodes[i].Kind == CampaignObjectiveKind.RecoverItem &&
                        (!p.NodeItemIds.TryGetValue(task.Id + "/" + task.Nodes[i].Id, out string item) || !Guid.TryParse(item, out _)))
                        return "campaign_item_binding";
            }
            foreach (string id in p.CompletedTasks)
            {
                var task = CampaignCatalog.Find(id);
                if (task == null || Mask(p, id) != All(task) ||
                    task.PrerequisiteTask != null && !p.CompletedTasks.Contains(task.PrerequisiteTask)) return "campaign_completion";
            }
            foreach (var pair in p.NodeItemIds)
            {
                int split = pair.Key == null ? -1 : pair.Key.IndexOf('/');
                var task = split < 1 ? null : CampaignCatalog.Find(pair.Key.Substring(0, split));
                if (task == null || Array.Find(task.Nodes, n => n.Id == pair.Key.Substring(split + 1)) == null ||
                    !Guid.TryParse(pair.Value, out _)) return "campaign_item_binding";
            }
            foreach (string id in p.ProcessedEvents)
                if (string.IsNullOrWhiteSpace(id) || id.Length > 128) return "campaign_receipt";
            if (p.PrimaryTracked != null && CampaignCatalog.Find(p.PrimaryTracked) == null) return "campaign_tracking";
            var tracked = new HashSet<string>(StringComparer.Ordinal);
            if (p.PrimaryTracked != null) tracked.Add(p.PrimaryTracked);
            foreach (string id in p.SecondaryTracked)
                if (id == null || CampaignCatalog.Find(id) == null || !tracked.Add(id)) return "campaign_tracking";
            return null;
        }

        public static int Mask(CampaignProgress p, string taskId) => p?.NodeMasks != null && p.NodeMasks.TryGetValue(taskId, out int value) ? value : 0;
        public static bool NodeCompleted(CampaignProgress p, string taskId, int nodeIndex) => (Mask(p, taskId) & (1 << nodeIndex)) != 0;
        private static int All(TaskDefinition task) => (1 << task.Nodes.Length) - 1;

        public static bool Available(CampaignProgress p, string taskId)
        {
            var task = CampaignCatalog.Find(taskId);
            if (p == null || task == null || p.CompletedTasks == null) return false;
            if (p.CompletedTasks.Contains(taskId) || task.PrerequisiteTask == null) return true;
            if (p.CompletedTasks.Contains(task.PrerequisiteTask)) return true;
            return !task.RequireSavedPrerequisite && FieldComplete(p, CampaignCatalog.Find(task.PrerequisiteTask));
        }

        private static bool FieldComplete(CampaignProgress p, TaskDefinition task)
        {
            if (task == null) return false;
            int required = 0;
            for (int i = 0; i < task.Nodes.Length; i++)
                if (task.Nodes[i].Kind != CampaignObjectiveKind.Extract) required |= 1 << i;
            return (Mask(p, task.Id) & required) == required;
        }

        public static CampaignTaskStatus Status(CampaignProgress p, string taskId)
        {
            if (!Available(p, taskId)) return CampaignTaskStatus.Locked;
            if (p.CompletedTasks.Contains(taskId)) return CampaignTaskStatus.Completed;
            if (FieldComplete(p, CampaignCatalog.Find(taskId))) return CampaignTaskStatus.AwaitingExtraction;
            return Mask(p, taskId) == 0 ? CampaignTaskStatus.Available : CampaignTaskStatus.InProgress;
        }

        public static NodeDefinition CurrentNode(CampaignProgress p, string taskId)
        {
            var task = CampaignCatalog.Find(taskId);
            if (task == null || !Available(p, taskId) || p.CompletedTasks.Contains(taskId)) return null;
            int mask = Mask(p, taskId);
            foreach (var node in task.Nodes)
            {
                int index = Array.IndexOf(task.Nodes, node);
                if ((mask & (1 << index)) == 0 && (mask & node.Prerequisites) == node.Prerequisites) return node;
            }
            return null;
        }

        public static string CurrentMain(CampaignProgress p)
        {
            string pendingExtraction = null;
            foreach (var task in CampaignCatalog.Tasks)
            {
                if (task.Optional || !Available(p, task.Id) || p.CompletedTasks.Contains(task.Id)) continue;
                if (!FieldComplete(p, task)) return task.Id;
                pendingExtraction = task.Id;
            }
            return pendingExtraction;
        }

        public static bool TryRecord(CampaignProgress p, CampaignFact fact, out string error)
        {
            error = Validate(p);
            if (error != null) return false;
            var task = fact == null ? null : CampaignCatalog.Find(fact.TaskId);
            int index = task == null ? -1 : Array.FindIndex(task.Nodes, n => n.Id == fact.NodeId);
            if (index < 0 || fact.ContentVersion != CampaignCatalog.Version || string.IsNullOrWhiteSpace(fact.EventId) || fact.EventId.Length > 128)
                return Fail("campaign_fact", out error);
            if (!string.IsNullOrEmpty(fact.ItemId) && !Guid.TryParse(fact.ItemId, out _)) return Fail("campaign_item_binding", out error);
            var node = task.Nodes[index];
            if (fact.SourceId != node.SourceId || fact.Kind != node.Kind) return Fail("campaign_source", out error);
            if (!Available(p, task.Id)) return Fail("campaign_locked", out error);
            int mask = Mask(p, task.Id);
            if ((mask & (1 << index)) != 0) return true;
            if (p.ProcessedEvents.Contains(fact.EventId)) return Fail("campaign_event_conflict", out error);
            if ((mask & node.Prerequisites) != node.Prerequisites) return Fail("campaign_prerequisite", out error);
            if (node.Kind == CampaignObjectiveKind.Extract && !fact.Extracted) return Fail("campaign_extraction", out error);
            if (node.Kind == CampaignObjectiveKind.Extract && task.PrerequisiteTask != null && !p.CompletedTasks.Contains(task.PrerequisiteTask))
                return Fail("campaign_extraction_order", out error);
            if (node.Kind == CampaignObjectiveKind.RecoverItem &&
                (fact.ItemCode != node.RequiredItemCode || !Guid.TryParse(fact.ItemId, out _))) return Fail("campaign_item_binding", out error);
            p.NodeMasks[task.Id] = mask | (1 << index);
            if (!string.IsNullOrEmpty(fact.ItemId)) p.NodeItemIds[task.Id + "/" + node.Id] = fact.ItemId;
            // Node masks are the durable idempotency key. The bounded event set detects reuse among new nodes.
            if (p.ProcessedEvents.Count < MaxProcessedEvents) p.ProcessedEvents.Add(fact.EventId);
            if (node.Kind == CampaignObjectiveKind.Extract && Mask(p, task.Id) == All(task)) p.CompletedTasks.Add(task.Id);
            p.Revision++;
            FollowTracking(p, task.Id);
            return true;
        }

        private static bool Fail(string value, out string error) { error = value; return false; }

        // Dropping a required artifact revokes only this run's possession fact, never a saved chapter.
        // Reacquiring it must carry a fresh authoritative pickup event ID; prior receipts remain valid.
        public static bool ForgetUncommittedItem(CampaignProgress p, string taskId, string nodeId, string itemId)
        {
            if (Validate(p) != null || p.CompletedTasks.Contains(taskId)) return false;
            var task = CampaignCatalog.Find(taskId);
            int index = task == null ? -1 : Array.FindIndex(task.Nodes, n => n.Id == nodeId);
            string key = taskId + "/" + nodeId;
            if (index < 0 || task.Nodes[index].Kind != CampaignObjectiveKind.RecoverItem ||
                !p.NodeItemIds.TryGetValue(key, out string bound) || bound != itemId || !NodeCompleted(p, taskId, index)) return false;
            int mask = Mask(p, taskId) & ~(1 << index);
            for (int pass = 0; pass < task.Nodes.Length; pass++)
                for (int i = 0; i < task.Nodes.Length; i++)
                    if ((mask & (1 << i)) != 0 && (mask & task.Nodes[i].Prerequisites) != task.Nodes[i].Prerequisites)
                        mask &= ~(1 << i);
            p.NodeMasks[taskId] = mask;
            for (int i = 0; i < task.Nodes.Length; i++)
                if ((mask & (1 << i)) == 0) p.NodeItemIds.Remove(taskId + "/" + task.Nodes[i].Id);
            p.Revision++;
            return true;
        }

        public static bool TryTrack(CampaignProgress p, string taskId, bool secondary, out string error)
        {
            error = Validate(p);
            if (error != null) return false;
            if (!Available(p, taskId) || p.CompletedTasks.Contains(taskId)) return Fail("campaign_tracking_locked", out error);
            if (secondary)
            {
                if (p.PrimaryTracked == taskId || p.SecondaryTracked.Contains(taskId)) return true;
                if (p.SecondaryTracked.Count == 2) return Fail("campaign_tracking_full", out error);
                p.SecondaryTracked.Add(taskId);
            }
            else
            {
                p.PrimaryTracked = taskId; p.SecondaryTracked.Remove(taskId); p.TrackingSuppressed = false;
            }
            p.PreferenceRevision++;
            return true;
        }

        public static void Untrack(CampaignProgress p, string taskId)
        {
            if (p == null || p.SecondaryTracked == null) return;
            bool changed = p.SecondaryTracked.Remove(taskId);
            if (p.PrimaryTracked == taskId) { p.PrimaryTracked = null; p.TrackingSuppressed = true; changed = true; }
            if (changed) p.PreferenceRevision++;
        }

        private static void FollowTracking(CampaignProgress p, string finishedTask)
        {
            if (p.TrackingSuppressed || p.PrimaryTracked != finishedTask) return;
            string next = CurrentMain(p);
            if (next != null && next != finishedTask) p.PrimaryTracked = next;
            else if (p.CompletedTasks.Contains(finishedTask)) p.PrimaryTracked = next;
            p.SecondaryTracked.RemoveAll(id => p.CompletedTasks.Contains(id));
        }

        // The caller first validates item bindings against the actual frozen extracted inventory.
        // UI preferences always come from the latest saved profile, never an older server-run clone.
        public static CampaignProgress MergeExtracted(CampaignProgress saved, CampaignProgress run)
        {
            string error = Validate(saved) ?? Validate(run);
            if (error != null) throw new ArgumentException(error);
            var next = Clone(saved);
            foreach (var task in CampaignCatalog.Tasks)
            {
                int runMask = Mask(run, task.Id), merged = Mask(next, task.Id);
                bool complete = FieldComplete(run, task) && Available(run, task.Id);
                for (int i = 0; i < task.Nodes.Length; i++)
                {
                    var node = task.Nodes[i];
                    if ((runMask & (1 << i)) == 0 || node.Kind == CampaignObjectiveKind.Extract) continue;
                    if (!complete && node.SavePolicy == CampaignSavePolicy.RunOnly) continue;
                    merged |= 1 << i;
                    string key = task.Id + "/" + node.Id;
                    if (run.NodeItemIds.TryGetValue(key, out string item)) next.NodeItemIds[key] = item;
                }
                next.NodeMasks[task.Id] = merged;
                if (FieldComplete(next, task) && Available(next, task.Id))
                {
                    int index = Array.FindIndex(task.Nodes, n => n.Kind == CampaignObjectiveKind.Extract);
                    if (index >= 0) next.NodeMasks[task.Id] |= 1 << index;
                    if (Mask(next, task.Id) == All(task)) next.CompletedTasks.Add(task.Id);
                }
            }
            foreach (string id in run.ProcessedEvents)
            {
                if (next.ProcessedEvents.Count == MaxProcessedEvents) break;
                next.ProcessedEvents.Add(id);
            }
            next.Revision = Math.Max(saved.Revision, run.Revision) + 1;
            next.SecondaryTracked.RemoveAll(id => next.CompletedTasks.Contains(id));
            if (next.PrimaryTracked != null && next.CompletedTasks.Contains(next.PrimaryTracked))
                next.PrimaryTracked = next.TrackingSuppressed ? null : CurrentMain(next);
            return next;
        }
    }
}
