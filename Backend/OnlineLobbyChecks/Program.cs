using Unity.MP_FPS.Online;

var now = DateTime.UtcNow;
int passed = 0;
void Check(bool ok, string why) { if (!ok) throw new Exception(why); passed++; }
void Reject(Action act, string why) { try { act(); } catch (InvalidOperationException) { passed++; return; } throw new Exception(why); }
var service = new OnlineLobbies();
LobbySnapshot Get(Guid id) => service.Snapshot(id, now, "game.example", "game.example", 7979);
void Act(Guid id, string op, string code = "", string mode = "Coop", bool ready = false, int team = 0, bool queue = false) =>
    service.Act(id, "Operator", false, new LobbyAction { Action = op, Code = code, Mode = mode,
        ConnectionPath = OnlineNetworkProtocol.P2P, Ready = ready, Team = team, PublicQueue = queue }, now);
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
Reject(() => service.Ready(first.Id, now, OnlineNetworkProtocol.Version + 1, true), "Wrong server protocol accepted");
service.Ready(first.Id, now, OnlineNetworkProtocol.Version, true);
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
var reconnect = new OnlineLobbies();
reconnect.Act(a, "A", false, new LobbyAction { Action = "create" }, now);
string reconnectCode = reconnect.Snapshot(a, now, "host", "host", 7979).Code;
reconnect.Act(b, "B", false, new LobbyAction { Action = "join", Code = reconnectCode }, now);
reconnect.Act(a, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
reconnect.Act(b, "B", false, new LobbyAction { Action = "ready", Ready = true }, now);
var offline = reconnect.Snapshot(b, now.AddSeconds(31), "host", "host", 7979);
Check(!offline.Members.Single(m => m.PlayerId == a.ToString()).Connected && offline.Members.All(m => !m.Ready), "Offline member retained ready state");
Check(offline.Members.Single(m => m.PlayerId == a.ToString()).ReconnectSeconds == 59, "Lobby reconnect grace is not bounded");
var resumed = reconnect.Snapshot(a, now.AddSeconds(60), "host", "host", 7979);
Check(resumed.Code == reconnectCode && resumed.Members.Single(m => m.PlayerId == a.ToString()).Connected, "Reconnect within grace lost party");
Reject(() => reconnect.Act(b, "B", false, new LobbyAction { Action = "kick", TargetPlayerId = a.ToString() }, now.AddSeconds(60)), "Nonleader kicked party member");
reconnect.Snapshot(b, now.AddSeconds(120), "host", "host", 7979);
var transferred = reconnect.Snapshot(b, now.AddSeconds(151), "host", "host", 7979);
Check(transferred.LeaderId == b.ToString() && transferred.Members.Length == 1, "Expired leader stranded online member");
reconnect.Act(c, "C", false, new LobbyAction { Action = "join", Code = reconnectCode }, now.AddSeconds(151));
Reject(() => reconnect.Act(b, "B", false, new LobbyAction { Action = "kick", TargetPlayerId = b.ToString() }, now.AddSeconds(151)), "Leader kicked self");
reconnect.Act(b, "B", false, new LobbyAction { Action = "kick", TargetPlayerId = c.ToString() }, now.AddSeconds(151));
Check(reconnect.Snapshot(c, now.AddSeconds(151), "host", "host", 7979).Phase == "None", "Kicked member kept party access");
reconnect.Act(b, "B", false, new LobbyAction { Action = "ready", Ready = true }, now.AddSeconds(151));
reconnect.Act(b, "B", false, new LobbyAction { Action = "start" }, now.AddSeconds(151));
var protectedRaid = reconnect.Allocate(now.AddSeconds(151))!;
reconnect.Allocate(now.AddMinutes(10));
Check(reconnect.Current()?.Seats.Count == 1 && reconnect.Current()?.Id == protectedRaid.Id, "Lobby pruning removed active raid ownership");
Reject(() => reconnect.Act(b, "B", false, new LobbyAction { Action = "kick", TargetPlayerId = c.ToString() }, now.AddMinutes(10)), "Active raid allowed roster changes");
var entry = new OnlineLobbies();
void EntryAct(Guid id, string action, bool active = false) => entry.Act(id, "Operator", active,
    new LobbyAction { Action = action, Ready = true }, now);
LobbySnapshot EntryGet(Guid id, DateTime? at = null) => entry.Snapshot(id, at ?? now, "host", "host", 7979);
EntryAct(a, "create");
foreach (var id in new[] { b, c, d }) entry.Act(id, "Operator", false,
    new LobbyAction { Action = "join", Code = EntryGet(a).Code }, now);
foreach (var id in new[] { a, b, c, d }) EntryAct(id, "ready");
EntryAct(a, "start"); var entryRaid = entry.Allocate(now)!;
entry.Ready(entryRaid.Id, now, OnlineNetworkProtocol.Version, true);
entry.Admission(a, entryRaid.Id, now, true);
Check(EntryGet(b).CanRetry && EntryGet(b).CanLeave, "Failed first entry has no recovery actions");
Check(!EntryGet(a).CanLeave, "An unsettled participant can leave the squad");
Reject(() => EntryAct(a, "leave"), "Unsettled admitted seat left without settlement");
Reject(() => EntryAct(c, "leave", true), "Unsettled inventory escaped through the leave action");
EntryAct(b, "return");
Check(EntryGet(b).Members.Single(m => m.PlayerId == b.ToString()).Returned && EntryGet(b).CanRetry,
    "An unadmitted entry marked returned by an old client cannot recover");
EntryAct(b, "retry");
Check(!entry.Current()!.Seats.Single(s => s.Player == b).Returned && !entry.Current()!.Seats.Single(s => s.Player == b).Settled,
    "Retry did not restore the unadmitted seat");
entry.Admission(b, entryRaid.Id, now, true); entry.Settled(b);
Check(EntryGet(b).CanLeave && !EntryGet(b).CanRetry, "Completed participant cannot leave or can repeat the raid");
Reject(() => EntryAct(b, "retry"), "Retry reopened a settled deployment");
EntryAct(b, "leave"); EntryAct(c, "leave");
Check(EntryGet(b).Phase == "None" && EntryGet(c).Phase == "None", "Settled or unadmitted members stayed trapped in the squad");
Check(entry.Current()!.Id == entryRaid.Id && !entry.Current()!.Seats.Single(s => s.Player == a).Returned && !entry.ShouldStop(now),
    "Individual departure interrupted a teammate's active raid");
Reject(() => entry.Admission(c, entryRaid.Id, now), "A departed member retained admission");
Check(!EntryGet(d, now.AddMinutes(5)).CanRetry && EntryGet(d, now.AddMinutes(5)).CanLeave,
    "Expired first-entry window lost its exit or retained retry");
Reject(() => entry.Act(d, "Operator", false, new LobbyAction { Action = "retry" }, now.AddMinutes(5)), "Expired first entry was reopened");

var directory = new OnlineLobbies();
var f = Guid.NewGuid();
LobbySnapshot Browse(Guid id, DateTime? at = null) => directory.Snapshot(id, at ?? now, "game.example", "test", 7979, browseRooms: true);
void RoomAct(Guid id, string action, string? roomId = null, string? requestId = null, string? invite = null, Guid? eligible = null, bool active = false) =>
    directory.Act(id, id == a ? "A" : id == f ? "F" : "Applicant", active, new LobbyAction { Action = action,
        RoomId = roomId, RequestId = requestId, Code = invite, Ready = true }, now, eligible);
RoomAct(a, "create"); RoomAct(c, "join", invite: Browse(a).Code); RoomAct(f, "create");
var rooms = Browse(b).Rooms;
var roomA = rooms.Single(r => r.LeaderName == "A");
var roomF = rooms.Single(r => r.LeaderName == "F");
Check(Browse(b).RoomDirectoryAvailable && rooms.Length == 2 && roomA.MemberCount == 2 && roomA.Capacity == 4 &&
    roomA.Status == "Open" && roomA.Mode == "Coop" && roomA.ConnectionPath == OnlineNetworkProtocol.P2P && roomA.RoomId != Browse(a).Code,
    "Directory omitted public room details or revealed the invite code");
Check(Browse(c).Rooms.Single().RoomId == roomF.RoomId && directory.Snapshot(b, now, "host", "host", 7979).Rooms == null,
    "Directory includes the member's own room or is sent when not requested");
Reject(() => RoomAct(c, "request-join", roomF.RoomId), "Existing member applied to a second party");
Reject(() => RoomAct(b, "request-join", roomA.RoomId, active: true), "Unsettled raid applied to a party");
RoomAct(b, "request-join", roomA.RoomId);
string application = Browse(b).JoinRequest.RequestId;
RoomAct(b, "request-join", roomA.RoomId);
Check(Browse(b).Phase == "None" && Browse(b).JoinRequest.RequestId == application &&
    Browse(a).JoinRequests.Single().RequestId == application && Browse(c).JoinRequests.Length == 0,
    "Application added a member, duplicated a request, or leaked the leader's inbox");
Reject(() => RoomAct(b, "request-join", roomF.RoomId), "Applicant sent simultaneous requests");
Reject(() => directory.ApprovalApplicant(c, application, now), "Nonleader read the approval target");
Reject(() => RoomAct(f, "approve-join", requestId: application, eligible: b), "Wrong leader approved the request");
Reject(() => RoomAct(a, "approve-join", requestId: application), "Approval skipped the applicant's settlement check");
RoomAct(a, "ready"); RoomAct(c, "ready");
RoomAct(a, "approve-join", requestId: application, eligible: directory.ApprovalApplicant(a, application, now));
Check(Browse(b).Code == Browse(a).Code && Browse(b).JoinRequest == null && Browse(a).JoinRequests.Length == 0 &&
    Browse(a).Members.All(m => !m.Ready), "Approval did not join the party atomically and reset readiness");

RoomAct(d, "request-join", roomA.RoomId); RoomAct(e, "request-join", roomA.RoomId);
string requestD = Browse(d).JoinRequest.RequestId, requestE = Browse(e).JoinRequest.RequestId;
Guid eligibleD = directory.ApprovalApplicant(a, requestD, now), eligibleE = directory.ApprovalApplicant(a, requestE, now);
RoomAct(a, "approve-join", requestId: requestD, eligible: eligibleD);
Reject(() => RoomAct(a, "approve-join", requestId: requestE, eligible: eligibleE), "Concurrent approvals both consumed the last seat");
var full = Browse(e);
Check(full.JoinRequest.Status == "Unavailable" && full.Rooms.Single(r => r.RoomId == roomA.RoomId).Status == "Full",
    "Full party stayed open to applications");
foreach (var id in new[] { a, b, c, d }) RoomAct(id, "ready");
RoomAct(a, "start");
Check(Browse(e).Rooms.Single(r => r.RoomId == roomA.RoomId).Status == "Queued", "Queued party not marked unavailable");
var directoryRaid = directory.Allocate(now)!;
Check(Browse(e).Rooms.Single(r => r.RoomId == roomA.RoomId).Status == "Starting", "Starting room state missing");
directory.Ready(directoryRaid.Id, now, OnlineNetworkProtocol.Version, true);
var inRaid = Browse(e);
Check(inRaid.Rooms.Single(r => r.RoomId == roomA.RoomId).Status == "InRaid" && inRaid.Address == null && inRaid.InstanceId == null,
    "Active room is missing or its game endpoint leaked to a browser");

RoomAct(e, "request-join", roomF.RoomId);
string oldRequest = Browse(e).JoinRequest.RequestId;
RoomAct(e, "withdraw-join", requestId: oldRequest);
RoomAct(e, "request-join", roomF.RoomId);
string newRequest = Browse(e).JoinRequest.RequestId;
Reject(() => RoomAct(f, "approve-join", requestId: oldRequest, eligible: e), "Stale approval accepted a replacement request");
RoomAct(e, "withdraw-join", requestId: oldRequest);
Check(Browse(e).JoinRequest.RequestId == newRequest, "Old withdrawal canceled a different request");
RoomAct(f, "decline-join", requestId: newRequest);
Check(Browse(e).JoinRequest.Status == "Declined" && Browse(f).JoinRequests.Length == 0, "Decline did not notify the applicant");
RoomAct(Guid.NewGuid(), "request-join", roomF.RoomId);
Browse(f, now.AddSeconds(24));
Check(Browse(f, now.AddSeconds(31)).JoinRequests.Length == 0, "Offline applicants still occupy the approval inbox");
Check(!Browse(e, now.AddSeconds(62)).Rooms.Any(r => r.RoomId == roomF.RoomId), "Offline room remained advertised");

string folder = Path.Combine(Path.GetTempPath(), "moon-lobby-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var disk = new OnlineLobbies(Path.Combine(folder, "lobbies.json"));
    disk.Act(a, "A", false, new LobbyAction { Action = "create" }, now);
    var diskRoom = disk.Snapshot(e, now, "host", "host", 7979, browseRooms: true).Rooms.Single();
    disk.Act(e, "Applicant", false, new LobbyAction { Action = "request-join", RoomId = diskRoom.RoomId }, now);
    disk = new OnlineLobbies(Path.Combine(folder, "lobbies.json"));
    Check(disk.Snapshot(e, now, "host", "host", 7979, browseRooms: true).Rooms.Single().RoomId == diskRoom.RoomId &&
        disk.Snapshot(a, now, "host", "host", 7979).JoinRequests.Single().ApplicantName == "Applicant",
        "Restart lost a room's public identifier or pending application");
    disk.Act(a, "A", false, new LobbyAction { Action = "ready", Ready = true }, now);
    disk.Act(a, "A", false, new LobbyAction { Action = "start" }, now);
    var lease = disk.Allocate(now)!;
    var restored = new OnlineLobbies(Path.Combine(folder, "lobbies.json"));
    Check(restored.Current()?.Id == lease.Id && restored.Snapshot(a, now, "host", "host", 7979).Code != null, "Backend restart lost active instance or party");
    Check(restored.ShouldStop(now.AddMinutes(4)), "Unready server never timed out");
}
finally { File.Delete(Path.Combine(folder, "lobbies.json")); Directory.Delete(folder); }
Console.WriteLine($"Online lobby lifecycle: {passed} assertions passed; no Unity process or network client started.");
