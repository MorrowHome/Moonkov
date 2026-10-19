using Unity.MP_FPS.Missions;
using Unity.MP_FPS.Inventory;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
MissionOperation Create(MissionDefinition d) => new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, d, 0);
double Finish(MissionOperation op, string actor, int node, uint request, double now)
{
    Check(op.Begin(actor, 1, node, request, now) == MissionActionResult.Started, "begin " + node);
    double end = now + op.Objective(node).Seconds;
    for (double t = now + .5; t < end; t += .5)
        Check(op.Renew(actor, request, t) == MissionActionResult.Running, "hold " + node);
    Check(op.Renew(actor, request, end) == MissionActionResult.Completed, "finish " + node);
    return end + .1;
}
var moon = MissionCatalog.Mooncore();
var archive = MissionCatalog.Archive();
Check(moon.Validate() == null && archive.Validate() == null, "valid content");
var cycle = moon.Copy(); cycle.Objectives[0].Prerequisites = 4;
Check(cycle.Validate() == "mission_cycle", "cycle rejected");
var nan = moon.Copy(); nan.Objectives[0].Seconds = float.NaN;
Check(nan.Validate() != null, "nonfinite content rejected");
var op = Create(moon);
moon.Objectives[0].Seconds = 300;
Check(op.Objective(0).Seconds == 4, "freeze definitions");
Check(op.Begin("actor", 1, 2, 1, 0) == MissionActionResult.Prerequisite, "cannot skip power");
Check(op.Begin("actor", 9, 0, 2, 0) == MissionActionResult.Invalid, "old epoch rejected");
Check(op.Begin("actor", 1, 0, 2, double.NaN) == MissionActionResult.Invalid, "nonfinite server time");
double time = Finish(op, "actor", 0, 2, 1);
Check(op.Completed(0) && !op.Completed(1), "individual switches");
Check(op.Begin("actor", 1, 0, 3, time) == MissionActionResult.AlreadyCompleted, "no duplicate power");
time = Finish(op, "late-player", 1, 1, time + .1);
time = Finish(op, "third-player", 2, 1, time);
op.Tick(time + 44);
Check(op.Phase == MissionPhase.Active, "cutting not early");
op.Tick(time + 45);
Check(op.Phase == MissionPhase.OutputReady && op.Completed(3), "complete without acceptance or tracking");
Check(op.Resolve() && !op.Resolve(), "resolve once");
var conflict = Create(MissionCatalog.Mooncore());
Check(conflict.Begin("A", 1, 0, 1, 0) == MissionActionResult.Started, "lock actor");
Check(conflict.Begin("B", 1, 0, 1, .1) == MissionActionResult.Busy, "lock contention");
conflict.Cancel("A");
Check(conflict.Begin("B", 1, 0, 2, .2) == MissionActionResult.Started, "release after cancel");
Check(conflict.Begin("B", 1, 0, 2, .3) == MissionActionResult.Running, "retry does not restart");
Check(conflict.Begin("B", 1, 0, 1, .4) == MissionActionResult.Stale, "old request rejected");
conflict.Tick(3);
Check(conflict.Renew("B", 2, 3) == MissionActionResult.Stale, "lease expires on disconnect");
Check(conflict.Begin("A", 1, 0, 2, 3) == MissionActionResult.Started, "unlock after expiry");
Check(conflict.Renew("A", 2, 2) == MissionActionResult.Invalid, "time cannot go backwards");
var late = Create(MissionCatalog.Mooncore()); late.Tick(120);
Check(late.Phase == MissionPhase.Expired, "unvisited offer expires");
Check(late.Begin("A", 1, 0, 1, 121) == MissionActionResult.Expired, "expired cannot activate");
var active = Create(MissionCatalog.Mooncore()); active.Begin("A", 1, 0, 1, 1); active.Tick(601);
Check(active.Phase == MissionPhase.Expired, "active deadline");
var arc = Create(archive);
time = Finish(arc, "explorer", 1, 1, 1);
Check(arc.Completed(1) && !arc.Completed(0), "early scan retained");
time = Finish(arc, "explorer", 2, 2, time);
Check(arc.Begin("explorer", 1, 3, 3, time) == MissionActionResult.Prerequisite, "real selfcheck required");
time = Finish(arc, "explorer", 0, 4, time + .1);
time = Finish(arc, "explorer", 3, 5, time);
arc.Tick(time + 20);
Check(arc.Phase == MissionPhase.OutputReady, "archive complete after return download");
var templates = new[] { MissionCatalog.Mooncore(), MissionCatalog.Archive() };
var found = new HashSet<string>();
for (uint seed = 1; seed <= 128; seed++)
{
    var one = new MissionDirector(seed); var two = new MissionDirector(seed);
    double now = one.NextEvaluation;
    var first = one.TryOffer(templates, now, 600, false, _ => true);
    var repeat = two.TryOffer(templates, now, 600, false, _ => true);
    Check(first != null && repeat != null && first.Id == repeat.Id, "reproducible random"); found.Add(first.Id);
}
Check(found.Count == 2, "both complete templates appear");
var director = new MissionDirector(42);
Check(director.TryOffer(templates, 0, 600, false, _ => true) == null, "initial grace");
Check(director.TryOffer(templates, 200, 600, true, _ => true) == null, "region exclusion");
Check(director.TryOffer(templates, 240, 600, false, _ => false) == null, "unsafe anchors excluded");
Check(director.TryOffer(templates, 280, 299, false, _ => true) == null, "do not issue impossible late task");
var last = director.TryOffer(templates, 320, 350, false, _ => true);
Check(last?.Id == "mc00_archive", "shorter eligible task");
Check(director.TryOffer(templates, 321, 600, false, _ => true) == null, "no deployment reroll");
InventoryItem Artifact(string code, string region = "1") => new InventoryItem {
    Id = Guid.NewGuid().ToString(), Code = code, Parent = "pockets", Region = region, FoundInRaid = true,
    MissionWorldId = Guid.NewGuid().ToString(), MissionOperationId = Guid.NewGuid().ToString(), MissionContentVersion = 1
};
var carried = InventoryGraph.Create(false, false);
var artifact = Artifact("mooncore_fragment"); carried.Items.Add(artifact);
Check(carried.Validate() == InventoryError.None, "valid sourced artifact");
var forged = carried.Clone(); forged.Items[2].MissionOperationId = null;
Check(forged.Validate() == InventoryError.Invalid, "artifact requires server source");
var account = InventoryGraph.Create(true, false);
Check(account.ReturnLoadout(carried) == InventoryError.None, "atomic return/reward");
Check(account.MissionFirsts == 3 && account.Count("dust") == 40, "first extraction plus first mooncore");
Check(account.Find(artifact.Id)?.MissionRecovered == true, "source marked recovered");
int version = account.Version;
Check(account.ReturnLoadout(carried) == InventoryError.Invalid && account.Version == version && account.Count("dust") == 40, "duplicate return leaves account intact");
var nextRaid = account.ExtractLoadout();
Check(nextRaid.MissionFirsts == 0 && account.MissionFirsts == 3, "raid cannot own account facts");
Check(account.ReturnLoadout(nextRaid) == InventoryError.None && account.Count("dust") == 40, "redeploy source does not repeat first reward");
var nextSource = InventoryGraph.Create(false, false); nextSource.Items.Add(Artifact("mooncore_fragment", "2"));
Check(account.ReturnLoadout(nextSource) == InventoryError.None && account.Count("dust") == 40, "second source not second first reward");
var archiveRaid = InventoryGraph.Create(false, false); archiveRaid.Items.Add(Artifact("mc00_archive_drive", "3"));
Check(account.ReturnLoadout(archiveRaid) == InventoryError.None && account.MissionFirsts == 7 && account.Count("dust") == 60, "independent archive first reward");
var dead = InventoryGraph.Create(false, false); var lost = Artifact("mooncore_fragment"); dead.Items.Add(lost);
var deathBag = LootInventoryExchange.DropOnDeath(dead);
Check(dead.Find(lost.Id) == null && deathBag.Find(lost.Id) != null, "death transfers unique artifact");
var looter = InventoryGraph.Create(false, false);
Check(LootInventoryExchange.TryApply(looter, deathBag, new InventoryCommand { Operation = InventoryOperation.Move, ExpectedVersion = looter.Version,
    ItemId = lost.Id, Parent = "pockets", Region = "1" }, deathBag.Version) == InventoryError.None, "late looter obtains actual item");
Check(deathBag.Find(lost.Id) == null && looter.Find(lost.Id).MissionOperationId == lost.MissionOperationId, "source preserved through death loot");
var looterAccount = InventoryGraph.Create(true, false);
Check(looterAccount.ReturnLoadout(looter) == InventoryError.None && looterAccount.MissionFirsts == 3, "no contribution/acceptance gate for recovered loot");
var badFacts = account.Clone(); badFacts.MissionFirsts = 8;
Check(badFacts.Validate() == InventoryError.Invalid, "unknown fact bits rejected");
Console.WriteLine($"PASS {checks} mission/inventory checks; two complete rule flows; automatic recovery rewards and death transfer.");
