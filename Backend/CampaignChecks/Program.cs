using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Missions;

int checks = 0;
void Check(bool actual, string name) { if (!actual) throw new Exception(name); checks++; }
if (args.Length == 1 && args[0] == "--restore-only")
{
    MissionDefinition ChapterTemplate()
    {
        var d = MissionCatalog.Mooncore();
        var nodes = d.Objectives.ToList();
        nodes.Add(new MissionObjectiveDefinition { Id = "report", Anchor = "entry", Name = "登记清理队身份", Kind = MissionObjectiveKind.Interact, Seconds = 2 });
        d.Objectives = nodes.ToArray();
        return d;
    }
    MissionOperation Operation() => new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, ChapterTemplate(), 10);
    var restored = Operation();
    Check(restored.RestoreCompleted(19, 10), "saved registration and two power nodes restore");
    Check(restored.Completed(0) && restored.Completed(1) && restored.Completed(4), "only committed facility facts restored");
    Check(!restored.Completed(2) && !restored.Completed(3) && restored.Phase == MissionPhase.Active, "sample start and process not restored");
    Check(restored.Progress(3, 20) == 0, "restored power does not secretly run cutting");
    Check(!restored.RestoreCompleted(31, 20), "restore is rejected after nonempty restoration");
    Check(restored.Begin("actor", 1, 2, 1, 20) == MissionActionResult.Started, "player must start cutting normally");
    restored.Renew("actor", 1, 21); restored.Renew("actor", 1, 22);
    Check(restored.Renew("actor", 1, 23) == MissionActionResult.Completed, "real start interaction completes");
    restored.Tick(67);
    Check(restored.Phase == MissionPhase.Active && !restored.Completed(3), "restored mission still waits complete 45 seconds");
    restored.Tick(68);
    Check(restored.Phase == MissionPhase.OutputReady && restored.Completed(3), "normal process reaches real output after 45 seconds");
    var invalid = Operation();
    Check(!invalid.RestoreCompleted(4, 10) && invalid.CompletedMask == 0, "start cannot be restored without power prerequisites");
    Check(!invalid.RestoreCompleted(32, 10), "unknown restore bit rejected");
    Check(!invalid.RestoreCompleted(1u << 8, 10), "bit outside eight-node wire capacity rejected");
    Check(!invalid.RestoreCompleted(19, double.NaN) && !invalid.RestoreCompleted(19, 9), "nonfinite and backwards server time rejected");
    Check(invalid.RestoreCompleted(16, 10) && invalid.CompletedMask == 16, "registration alone can be restored");
    var live = Operation();
    Check(live.Begin("actor", 1, 4, 1, 10) == MissionActionResult.Started, "normal live registration starts");
    Check(!live.RestoreCompleted(19, 11), "restoration cannot overwrite a live interaction");
    live.Renew("actor", 1, 11);
    Check(live.Renew("actor", 1, 12) == MissionActionResult.Completed && !live.RestoreCompleted(19, 12), "completed real registration cannot be overwritten");
    var powered = Operation();
    Check(powered.Configure("actor", 1, 0, 1, 1, 10) == MissionActionResult.Completed && !powered.RestoreCompleted(16, 11), "completed real power cannot be overwritten");
    var empty = Operation();
    Check(empty.RestoreCompleted(0, 10) && !empty.RestoreCompleted(19, 10), "even an empty restore is accepted once only");
    var untouched = MissionCatalog.Mooncore();
    Check(untouched.Objectives.Length == 4 && untouched.Objectives.All(n => n.Id != "report"), "base online template remains four nodes");
    Check(untouched.Objectives.Select(n=>n.Id).SequenceEqual(new[]{"power_a","power_b","start","cutting"}), "base objective indices remain stable");
    Console.WriteLine($"Campaign facility restore: {checks} checks passed.");
    return;
}
CampaignFact Fact(string task, string node, string item = null) {
    var definition = CampaignCatalog.Find(task).Nodes.First(n => n.Id == node);
    return new CampaignFact { TaskId = task, NodeId = node, SourceId = definition.SourceId,
        Kind = definition.Kind, EventId = Guid.NewGuid().ToString(), ItemCode = item,
        ItemId = item == null ? null : Guid.NewGuid().ToString() };
}
void Record(CampaignProgress p, string task, string node, string item = null) {
    Check(CampaignRules.TryRecord(p, Fact(task, node, item), out var error), task + "/" + node + " " + error);
}
void Arrive(CampaignProgress p) { Record(p,"CM01","arrive"); Record(p,"CM01","report"); }
void Power(CampaignProgress p) { Record(p,"CM02","power_a"); Record(p,"CM02","power_b"); }
void Sample(CampaignProgress p) { Record(p,"CM03","start"); Record(p,"CM03","cutting"); Record(p,"CM03","recover","mooncore_fragment"); }
void Archive(CampaignProgress p) { Record(p,"CM04","self_check"); Record(p,"CM04","tag_a"); Record(p,"CM04","tag_b"); Record(p,"CM04","download_start"); Record(p,"CM04","download"); Record(p,"CM04","recover","mc00_archive_drive"); }

Check(CampaignCatalog.Validate() == null, "campaign content is valid");
Check(CampaignCatalog.Tasks.Select(t => t.Id).SequenceEqual(new[] { "CM01", "CM02", "CM03", "CM04" }), "first chapter and the archive investigation are present");
Check(CampaignCatalog.Tasks.All(t => t.Nodes.Length <= 8 && t.Chapter <= 2), "bounded chapter nodes");
Check(CampaignCatalog.Find("CM05") == null && CampaignCatalog.Find("CS01") == null, "unimplemented follow-up content stays unavailable");
Check(CampaignCatalog.Find("CM01").Nodes[1].ExistingObjectiveId == "report", "registration reuses live mission objective");
Check(CampaignCatalog.Find("CM02").Nodes.Take(2).Select(n => n.ExistingObjectiveId).SequenceEqual(new[] { "power_a", "power_b" }), "power observes existing rules");
Check(CampaignCatalog.Find("CM03").Nodes.Take(2).Select(n => n.ExistingObjectiveId).SequenceEqual(new[] { "start", "cutting" }), "cutting observes existing rules");
Check(CampaignCatalog.Find("CM04").Nodes.Take(5).Select(n => n.ExistingObjectiveId).SequenceEqual(MissionCatalog.Archive().Objectives.Select(n => n.Id)), "investigation observes the existing archive facility");
Check(CampaignCatalog.Tasks.SelectMany(t=>t.Nodes).All(n=>n.AnchorId != null && n.SourceId.StartsWith("campaign.")), "semantic source bindings");
var firstAdapter = CampaignRules.Create(); Record(firstAdapter,"CM01","arrive");
var firstFacility = MissionCatalog.Mooncore();
firstFacility.Objectives = firstFacility.Objectives.Append(new MissionObjectiveDefinition { Id = "report", Anchor = "entry",
    Name = "登记清理队身份", Kind = MissionObjectiveKind.Interact, Seconds = 2 }).ToArray();
CampaignMissionAdapter.RecordCompleted(firstAdapter, 1, firstFacility, 19, "first-operation", 1);
Check(CampaignRules.Mask(firstAdapter,"CM01") == 3 && CampaignRules.Mask(firstAdapter,"CM02") == 3 &&
    CampaignRules.CurrentMain(firstAdapter) == "CM03", "first chapter registration and power still advance together");

var saved = CampaignRules.Create();
Check(CampaignRules.Validate(saved) == null && saved.PrimaryTracked == "CM01", "empty old profile default");
Check(CampaignRules.Available(saved,"CM01") && !CampaignRules.Available(saved,"CM02"), "chapter starts at entry");
Check(CampaignRules.CurrentMain(saved) == "CM01" && CampaignRules.CurrentNode(saved,"CM01").Id == "arrive", "entry guidance");
Check(!CampaignRules.TryRecord(saved,Fact("CM03","recover","mooncore_fragment"),out _), "future pickup cannot skip instruction");
var source = Fact("CM01","arrive"); source.SourceId = "unrelated.entry";
Check(!CampaignRules.TryRecord(saved,source,out _) && CampaignRules.Mask(saved,"CM01") == 0, "foreign source rejected");
var order = Fact("CM01","report");
Check(!CampaignRules.TryRecord(saved,order,out _), "registration must follow arrival");

var run = CampaignRules.Clone(saved);
CampaignRules.Untrack(run,"CM01");
Check(run.TrackingSuppressed && run.PrimaryTracked == null && run.Revision == 0, "untracking changes preferences only");
Arrive(run);
Check(CampaignRules.Available(run,"CM02") && !run.CompletedTasks.Contains("CM01"), "same run continuation without claiming saved");
Check(CampaignRules.Status(run,"CM01") == CampaignTaskStatus.AwaitingExtraction, "registration awaits real extraction");
Power(run);
Check(CampaignRules.Available(run,"CM03") && CampaignRules.CurrentMain(run) == "CM03", "power opens sample in same run");
Sample(run);
Check(!CampaignRules.Available(run,"CM04"), "the archive opens only after the first chapter is actually saved");
Check(run.CompletedTasks.Count == 0 && saved.NodeMasks.Count == 0, "unsaved run never mutates profile");
Check(CampaignRules.Status(run,"CM03") == CampaignTaskStatus.AwaitingExtraction, "held artifact still awaits return");
Check(run.PrimaryTracked == null, "facts do not override manual tracking cancellation");
Check(run.NodeItemIds.ContainsKey("CM03/recover"), "physical artifact is bound to node");
var illegalExtract = Fact("CM03","extract"); illegalExtract.Extracted = true;
Check(!CampaignRules.TryRecord(run,illegalExtract,out _), "cannot finalize later task before earlier extraction");
var merged = CampaignRules.MergeExtracted(saved,run);
Check(CampaignRules.Validate(merged) == null, "merged chapter profile valid");
Check(merged.CompletedTasks.SetEquals(new[] {"CM01","CM02","CM03"}), "one extraction commits complete chapter");
Check(!merged.NodeMasks.ContainsKey("CM04"), "an unstarted later investigation does not alter the saved story shape");
Check(merged.NodeItemIds["CM03/recover"] == run.NodeItemIds["CM03/recover"], "physical item identity preserved");
Check(CampaignRules.CurrentMain(merged) == "CM04" && merged.PrimaryTracked == "CM04", "saved sample opens and tracks the next investigation");
Check(saved.CompletedTasks.Count == 0 && run.CompletedTasks.Count == 0, "merge has no input mutation");
var repeated = CampaignRules.MergeExtracted(merged,run);
Check(repeated.CompletedTasks.Count == 3 && repeated.NodeItemIds.Count == merged.NodeItemIds.Count, "settlement replay cannot duplicate facts or items");

var archiveRun = CampaignRules.Clone(merged);
Check(CampaignRules.Available(archiveRun,"CM04") && CampaignRules.CurrentNode(archiveRun,"CM04").Id == "self_check", "saved chapter opens the investigation");
Check(!CampaignRules.TryRecord(archiveRun,Fact("CM04","recover","mc00_archive_drive"),out _), "a drive cannot skip the facility process");
var adapted = CampaignRules.Clone(merged); var archiveDefinition = MissionCatalog.Archive();
CampaignMissionAdapter.RecordCompleted(adapted, 2, MissionCatalog.Mooncore(), 31, "test-deployment", 1);
Check(CampaignRules.Mask(adapted,"CM04") == 0, "a different facility cannot advance the archive");
CampaignMissionAdapter.RecordCompleted(adapted, 2, archiveDefinition, 4, "test-deployment", 1);
Check(CampaignRules.Mask(adapted,"CM04") == 0, "an early B scan waits for the story prerequisites");
CampaignMissionAdapter.RecordCompleted(adapted, 2, archiveDefinition, 5, "test-deployment", 1);
Check(CampaignRules.Mask(adapted,"CM04") == 1, "self-check records even while an early B scan waits");
CampaignMissionAdapter.RecordCompleted(adapted, 2, archiveDefinition, 7, "test-deployment", 1);
Check(CampaignRules.Mask(adapted,"CM04") == 7, "both scans are retained once A is recorded");
CampaignMissionAdapter.RecordCompleted(adapted, 2, archiveDefinition, 31, "test-deployment", 1);
Check(CampaignRules.Mask(adapted,"CM04") == 31 && CampaignRules.CurrentNode(adapted,"CM04").Id == "recover", "completed download still requires taking the actual drive");
var liveStory = CampaignRules.Clone(merged);
var liveArchive = new MissionOperation(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, archiveDefinition, 10);
double time = 10; uint request = 0;
void ArchiveInteraction(int node, int seconds)
{
    request++;
    Check(liveArchive.Begin("player", 1, node, request, time) == MissionActionResult.Started, "archive interaction starts");
    for (int second = 1; second <= seconds; second++)
    {
        var result = liveArchive.Renew("player", request, time + second);
        Check(result == (second == seconds ? MissionActionResult.Completed : MissionActionResult.Running), "archive interaction keeps its real duration");
    }
    time += seconds + .1;
    CampaignMissionAdapter.RecordCompleted(liveStory, 2, archiveDefinition, liveArchive.CompletedMask, "live-deployment", 1);
}
ArchiveInteraction(2, 4);
Check(CampaignRules.Mask(liveStory,"CM04") == 0, "a real early B scan is held for later");
ArchiveInteraction(0, 3);
ArchiveInteraction(1, 4);
Check(CampaignRules.Mask(liveStory,"CM04") == 7, "real early scanning is carried into the story");
ArchiveInteraction(3, 1);
liveArchive.Tick(time + 18);
CampaignMissionAdapter.RecordCompleted(liveStory, 2, archiveDefinition, liveArchive.CompletedMask, "live-deployment", 1);
Check(CampaignRules.CurrentNode(liveStory,"CM04").Id == "download", "download does not complete early");
liveArchive.Tick(time + 20);
CampaignMissionAdapter.RecordCompleted(liveStory, 2, archiveDefinition, liveArchive.CompletedMask, "live-deployment", 1);
Check(liveArchive.Phase == MissionPhase.OutputReady && CampaignRules.CurrentNode(liveStory,"CM04").Id == "recover", "completed download exposes the physical pickup step");
Archive(archiveRun);
Check(CampaignRules.Status(archiveRun,"CM04") == CampaignTaskStatus.AwaitingExtraction && !merged.CompletedTasks.Contains("CM04"), "a held drive remains unsaved");
string archiveId = archiveRun.NodeItemIds["CM04/recover"];
var lostArchive = CampaignRules.Clone(archiveRun);
Check(CampaignRules.ForgetUncommittedItem(lostArchive,"CM04","recover",archiveId), "dropping the drive revokes possession");
var earlyReturn = CampaignRules.MergeExtracted(merged,lostArchive);
Check(!earlyReturn.CompletedTasks.Contains("CM04") && CampaignRules.CurrentNode(earlyReturn,"CM04").Id == "self_check", "returning without the drive permits a fresh investigation");
Record(lostArchive,"CM04","recover","mc00_archive_drive");
var archiveSaved = CampaignRules.MergeExtracted(merged,lostArchive);
Check(archiveSaved.CompletedTasks.Contains("CM04") && archiveSaved.NodeItemIds["CM04/recover"] == lostArchive.NodeItemIds["CM04/recover"], "extraction commits the physical archive identity");
Check(CampaignRules.Validate(archiveSaved) == null && CampaignRules.CurrentMain(archiveSaved) == null, "the next story task is not fabricated");

var cancelled = CampaignRules.Clone(saved);
CampaignRules.Untrack(cancelled,"CM01");
var cancellation = CampaignRules.MergeExtracted(cancelled,run);
Check(cancellation.TrackingSuppressed && cancellation.PrimaryTracked == null && cancellation.PreferenceRevision == cancelled.PreferenceRevision, "latest saved preference wins stale run");

var partial = CampaignRules.Create(); Arrive(partial); Record(partial,"CM02","power_a");
var returnedEarly = CampaignRules.MergeExtracted(saved,partial);
Check(returnedEarly.CompletedTasks.SetEquals(new[]{"CM01"}), "early retreat saves only actual completed registration");
Check(CampaignRules.Mask(returnedEarly,"CM02") == 0 && CampaignRules.CurrentMain(returnedEarly) == "CM02", "incomplete live circuit state does not become persistent repair");
Check(returnedEarly.PrimaryTracked == "CM02", "saved completed primary continues to next real task");

var incompleteSample = CampaignRules.Create(); Arrive(incompleteSample); Power(incompleteSample);
Record(incompleteSample,"CM03","start"); Record(incompleteSample,"CM03","cutting");
var noSample = CampaignRules.MergeExtracted(saved,incompleteSample);
Check(noSample.CompletedTasks.SetEquals(new[]{"CM01","CM02"}) && CampaignRules.Mask(noSample,"CM03") == 0, "cutting alone is not recovered sample");
Check(CampaignRules.Status(noSample,"CM03") == CampaignTaskStatus.Available, "lost attempt can retry sample");

var dropped = CampaignRules.Create(); Arrive(dropped); Power(dropped); Sample(dropped);
string droppedId = dropped.NodeItemIds["CM03/recover"];
int oldReceipts = dropped.ProcessedEvents.Count;
Check(!CampaignRules.ForgetUncommittedItem(dropped,"CM03","recover",Guid.NewGuid().ToString()), "another item cannot revoke bound pickup");
Check(CampaignRules.ForgetUncommittedItem(dropped,"CM03","recover",droppedId), "dropping item revokes unsaved possession");
Check(CampaignRules.Validate(dropped) == null && CampaignRules.CurrentNode(dropped,"CM03").Id == "recover", "guidance returns to physical output");
Check(dropped.ProcessedEvents.Count == oldReceipts, "dropping keeps old authority receipts");
var droppedReturn = CampaignRules.MergeExtracted(saved,dropped);
Check(!droppedReturn.CompletedTasks.Contains("CM03"), "dropped sample cannot finish on extraction");
Record(dropped,"CM03","recover","mooncore_fragment");
var reacquired = CampaignRules.MergeExtracted(saved,dropped);
Check(reacquired.CompletedTasks.Contains("CM03"), "fresh verified pickup restores completion opportunity");
Check(!CampaignRules.ForgetUncommittedItem(reacquired,"CM03","recover",reacquired.NodeItemIds["CM03/recover"]), "saved chapter never rolls back after later item movement");

var idempotent = CampaignRules.Create(); var first = Fact("CM01","arrive");
Check(CampaignRules.TryRecord(idempotent,first,out _), "first verified entry");
int revision = idempotent.Revision;
Check(CampaignRules.TryRecord(idempotent,first,out _) && idempotent.Revision == revision, "replayed node does not mutate revision");
var conflict = Fact("CM01","report"); conflict.EventId = first.EventId;
Check(!CampaignRules.TryRecord(idempotent,conflict,out _), "event reused for another node rejected");
var foreignKind = Fact("CM01","report"); foreignKind.Kind = CampaignObjectiveKind.Process;
Check(!CampaignRules.TryRecord(idempotent,foreignKind,out _), "source does not excuse wrong objective kind");
var invalidItem = Fact("CM01","report"); invalidItem.ItemId = "not-an-item";
Check(!CampaignRules.TryRecord(idempotent,invalidItem,out _), "non-item fact cannot poison saved item bindings");

var tracked = CampaignRules.Create();
Check(!CampaignRules.TryTrack(tracked,"CM02",false,out _), "unknown future task cannot be tracked");
int factsBefore = tracked.Revision;
CampaignRules.Untrack(tracked,"CM01");
Check(CampaignRules.TryTrack(tracked,"CM01",false,out _) && !tracked.TrackingSuppressed && tracked.Revision == factsBefore, "retracking is still display only");
Arrive(tracked);
Check(tracked.PrimaryTracked == "CM02", "tracked main follows newly available step");
Check(CampaignRules.TryTrack(tracked,"CM01",true,out _), "available prior objective can be compact tracked");
Check(!tracked.SecondaryTracked.Contains(tracked.PrimaryTracked), "primary is not duplicated in compact list");

var forged = CampaignRules.Create(); forged.NodeMasks["CM03"] = 7;
Check(CampaignRules.Validate(forged) == "campaign_task_order", "forged later task mask rejected");
var malformed = CampaignRules.Create(); malformed.NodeMasks["CM01"] = 2;
Check(CampaignRules.Validate(malformed) == "campaign_node_order", "out of order saved node rejected");
malformed = CampaignRules.Create(); malformed.CompletedTasks.Add("CM01");
Check(CampaignRules.Validate(malformed) == "campaign_completion", "completion without verified nodes rejected");
malformed = CampaignRules.Clone(run); malformed.NodeItemIds.Clear();
Check(CampaignRules.Validate(malformed) == "campaign_item_binding", "recovery flag requires physical source item binding");
malformed = CampaignRules.Create(); malformed.ContentVersion = 2;
Check(CampaignRules.Validate(malformed) != null, "unknown content version retained as load error");
var bounded = CampaignRules.Create();
for(int i=0;i<CampaignRules.MaxProcessedEvents;i++) bounded.ProcessedEvents.Add("historical-"+i);
Check(CampaignRules.TryRecord(bounded,Fact("CM01","arrive"),out _) && bounded.ProcessedEvents.Count == CampaignRules.MaxProcessedEvents, "receipt memory stays bounded");
Check(CampaignRules.Validate(bounded) == null, "bounded receipt profile remains valid");

Console.WriteLine($"Campaign story: {checks} checks passed.");
