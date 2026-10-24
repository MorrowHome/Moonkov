using Unity.MP_FPS.Online;

var now = DateTime.UtcNow;
int passed = 0;
void Check(bool ok, string why) { if (!ok) throw new Exception(why); passed++; }
void Reject(Action act, string why) { try { act(); } catch (InvalidOperationException) { passed++; return; } throw new Exception(why); }
var service = new OnlineLobbies();
LobbySnapshot Get(Guid id) => service.Snapshot(id, now, "game.example", "game.example", 7979);
void Act(Guid id, string op, string code = "", string mode = "Coop", bool ready = false, int team = 0, bool queue = false) =>
    service.Act(id, "Operator", false, new LobbyAction { Action = op, Code = code, Mode = mode, Ready = ready, Team = team, PublicQueue = queue }, now);
Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid(), e = Guid.NewGuid();
Act(a, "create"); string code = Get(a).Code;
Act(b, "join", code); Act(c, "join", code); Act(d, "join", code);
Check(Get(a).Members.Length == 4 && Get(b).LeaderId == a.ToString(), "Four-person party did not share authoritative roster");
Reject(() => Act(e, "join", code), "Fifth player entered party");
Reject(() => Act(b, "settings", mode: "Versus"), "Nonleader changed mode");
Act(a, "ready", ready: true);
Reject(() => Act(a, "start"), "Started without all ready");
foreach (var id in new[] { b, c, d }) Act(id, "ready", ready: true);
Act(a, "settings", mode: "Coop");
Check(Get(a).Members.All(m => !m.Ready), "Mode change did not invalidate readiness");
foreach (var id in new[] { a, b, c, d }) Act(id, "ready", ready: true);
Act(a, "start"); var first = service.Allocate(now)!;
Check(first.Seats.Count == 4 && first.Seats.All(s => s.Team == 0), "Coop seats were not allied");
Check(service.Allocate(now) == null && Get(a).Address == null, "Second map allocated or endpoint advertised before ready");
Reject(() => service.Admission(a, first.Id, now), "Admission before scene ready");
Reject(() => service.Ready(first.Id, now, 2), "Wrong server protocol accepted");
service.Ready(first.Id, now, 1);
Check(Get(a).Address == "game.example", "Ready server not advertised");
Reject(() => service.Admission(e, first.Id, now), "Uninvited player admitted");
Reject(() => service.Admission(a, "another-raid", now), "Cross-instance admission accepted");
var tickets = new GameJoinTickets(); var ticket = tickets.Issue(a, now, first.Id);
Check(tickets.Consume(ticket, now) == null && tickets.Consume(ticket, now, "wrong") == null, "Bound ticket accepted by unrelated server");
Check(tickets.Consume(ticket, now, first.Id) == a && tickets.Consume(ticket, now, first.Id) == null, "Bound ticket single-use failed");
foreach (var id in new[] { a, b, c, d }) service.Admission(id, first.Id, now, true);
Reject(() => service.Act(a, "Operator", true, new LobbyAction { Action = "return" }, now), "Open equipment deployment released by return click");
Check(!service.ShouldStop(now.AddMinutes(3)), "Live raid stopped merely because deadline passed");
foreach (var id in new[] { a, b, c, d }) { service.Settled(id); Act(id, "return"); }
Check(service.ShouldStop(now), "Complete settled party failed to release map");
service.Finish(first.Id);
Check(Get(a).Phase == "Lobby" && Get(a).Members.Length == 4 && Get(a).Members.All(m => !m.Ready), "Party was lost or remained ready after return");
foreach (var id in new[] { a, b, c, d }) Act(id, "ready", ready: true);
Act(a, "start"); var second = service.Allocate(now)!;
Check(second.Id != first.Id && second.Seats.All(s => !s.Admitted && !s.Settled), "Old instance state leaked into next raid");
service.Finish(second.Id, "simulated launch failure");
Act(b, "leave"); Act(c, "leave"); Act(d, "leave");
Act(a, "ready", ready: true); Act(a, "start"); var solo = service.Allocate(now)!;
Check(solo.Seats.Count == 1 && solo.Mode == "Coop", "Solo cooperation required strangers"); service.Finish(solo.Id);
Act(a, "settings", mode: "Versus"); Act(a, "ready", ready: true);
Reject(() => Act(a, "start"), "Solo private versus accepted");
Act(b, "join", Get(a).Code); Act(b, "team", team: 1);
Act(a, "ready", ready: true); Act(b, "ready", ready: true); Act(a, "start");
var versus = service.Allocate(now)!;
Check(versus.Seats.Select(s => s.Team).Distinct().Count() == 2, "Friend versus did not separate teams"); service.Finish(versus.Id);
Act(a, "settings", mode: "Versus", queue: true); Act(a, "ready", ready: true); Act(b, "ready", ready: true); Act(a, "start");
Check(service.Allocate(now) == null && Get(a).Phase == "Queued", "Public versus started without opposing party");
Act(c, "create"); Act(c, "settings", mode: "Versus", queue: true); Act(c, "ready", ready: true); Act(c, "start");
var matched = service.Allocate(now)!;
Check(matched.Seats.Count == 3 && matched.Seats.Single(s => s.Player == a).Team == matched.Seats.Single(s => s.Player == b).Team &&
    matched.Seats.Single(s => s.Player == c).Team != matched.Seats.Single(s => s.Player == a).Team, "Matchmaker split a premade party");
service.Finish(matched.Id);
Reject(() => Act(a, "start"), "Old ready state survived completed match");
Act(a, "leave"); Check(Get(b).LeaderId == b.ToString(), "Leader departure stranded party");
Act(b, "settings", mode: "Coop"); Act(b, "ready", ready: true); Act(b, "start"); Act(b, "cancel");
Check(Get(b).Phase == "Lobby" && service.Allocate(now) == null, "Cancelled queue still allocated");
Act(b, "ready", ready: true); Act(b, "start");
Check(service.Allocate(now.AddSeconds(31)) == null && Get(b).Phase == "Lobby", "Offline party was launched");
string folder = Path.Combine(Path.GetTempPath(), "moon-lobby-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var disk = new OnlineLobbies(Path.Combine(folder, "lobbies.json"));
    disk.Act(a, "A", false, new LobbyAction { Action = "create" }, now);
    disk.Act(a, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
    disk.Act(a, "A", false, new LobbyAction { Action = "start" }, now);
    var lease = disk.Allocate(now)!;
    var restored = new OnlineLobbies(Path.Combine(folder, "lobbies.json"));
    Check(restored.Current()?.Id == lease.Id && restored.Snapshot(a, now, "host", "host", 7979).Code != null, "Backend restart lost active instance or party");
    Check(restored.ShouldStop(now.AddMinutes(4)), "Unready server never timed out");
}
finally { File.Delete(Path.Combine(folder, "lobbies.json")); Directory.Delete(folder); }
Console.WriteLine($"Online lobby lifecycle: {passed} assertions passed; no Unity process or network client started.");
