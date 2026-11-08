using System.Security.Cryptography;
using Unity.MP_FPS.Online;

// One server slot is intentional on the host PC. Lobby/account traffic stays light;
// each allocation gets a new process and a clean map, never a reused raid World.
public sealed partial class OnlineLobbies
{
    private const int PartyCapacity = 4;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(90);
    private static readonly System.Text.Json.JsonSerializerOptions StateJson = new() { IncludeFields = true };
    public sealed class Member
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool Ready { get; set; }
        public int Team { get; set; }
        public DateTime Seen { get; set; }
        public LobbyAppearance Appearance { get; set; } = new();
    }
    public sealed class Party
    {
        public string Code { get; set; } = "";
        public string RoomId { get; set; } = Guid.NewGuid().ToString("N");
        public Guid Leader { get; set; }
        public string Mode { get; set; } = "Coop";
        public string ConnectionPath { get; set; } = OnlineNetworkProtocol.P2P;
        public string Phase { get; set; } = "Lobby";
        public string Message { get; set; } = "";
        public bool PublicQueue { get; set; }
        public DateTime QueuedAt { get; set; }
        public List<Member> Members { get; set; } = new();
        public string? Instance { get; set; }
    }
    public sealed class Seat
    {
        public Guid Player { get; set; }
        public int Team { get; set; }
        public bool Admitted { get; set; }
        public bool Settled { get; set; }
        public bool Returned { get; set; }
    }
    public sealed class Allocation
    {
        public string Id { get; set; } = "";
        public string Mode { get; set; } = "Coop";
        // Persisted allocations from protocol 1 were VPS sessions.
        public string ConnectionPath { get; set; } = OnlineNetworkProtocol.Vps;
        public bool IceAvailable { get; set; }
        public string? IceHostId { get; set; }
        public string? IceHostAddress { get; set; }
        public string Phase { get; set; } = "Starting";
        public DateTime Created { get; set; }
        public DateTime ReadyAt { get; set; }
        public List<Seat> Seats { get; set; } = new();
    }
    public sealed class State
    {
        public List<Party> Parties { get; set; } = new();
        public Dictionary<Guid, JoinApplication> JoinApplications { get; set; } = new();
        public Allocation? Active { get; set; }
    }
    private readonly object gate = new();
    private State state = new();
    private readonly string? stateFile;
    private DateTime nextHeartbeatSave;
    public OnlineLobbies(string? path = null)
    {
        stateFile = path;
        if (path != null && File.Exists(path))
            state = System.Text.Json.JsonSerializer.Deserialize<State>(File.ReadAllText(path), StateJson) ?? new();
    }
    private Party? Find(Guid id) => state.Parties.FirstOrDefault(p => p.Members.Any(m => m.Id == id));
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private bool Maintain(DateTime now)
    {
        bool changed = false;
        // A raid owns its roster until all settlement receipts have been handled.
        foreach (var party in state.Parties.Where(p => p.Instance == null).ToArray())
        {
            var offline = party.Members.Where(m => now - m.Seen >= HeartbeatTimeout).ToArray();
            if (offline.Length == 0) continue;
            if (party.Phase == "Queued" || offline.Any(m => m.Ready))
            {
                Reset(party);
                party.Message = "队员连接中断，已取消准备。";
                changed = true;
            }
            if (party.Members.RemoveAll(m => now - m.Seen >= ReconnectGrace) > 0)
            {
                Reset(party);
                party.Message = "离线队员已离队。";
                changed = true;
            }
            if (party.Members.Count == 0) { state.Parties.Remove(party); continue; }
            if (party.Members.All(m => m.Id != party.Leader))
                party.Leader = party.Members.OrderByDescending(m => now - m.Seen < HeartbeatTimeout).First().Id;
        }
        return MaintainJoinApplications(now) || changed;
    }
    private void Save()
    {
        if (stateFile == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        var temp = stateFile + ".tmp";
        File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(state, StateJson));
        File.Move(temp, stateFile, true);
    }
    public LobbySnapshot Snapshot(Guid id, DateTime now, string address, string serverName, int port,
        bool browseRooms = false, bool raidActive = false)
    {
        lock (gate)
        {
            bool changed = Maintain(now);
            var p = Find(id);
            if (p == null)
            {
                var request = ApplicantSnapshot(id, now, raidActive, ref changed);
                if (changed) Save();
                return new LobbySnapshot { SelfId = id.ToString(), Phase = "None", RoomDirectoryAvailable = true,
                    CanRequestJoin = !raidActive, JoinRequest = request, Rooms = browseRooms ? BrowseRooms(null, now) : null };
            }
            p.Members.Single(m => m.Id == id).Seen = now;
            // Keep restart recovery bounded without writing a file for every poll.
            if (changed || now >= nextHeartbeatSave) { Save(); nextHeartbeatSave = now.AddSeconds(15); }
            var a = p.Instance == state.Active?.Id ? state.Active : null;
            var seat = a?.Seats.FirstOrDefault(s => s.Player == id);
            return new LobbySnapshot
            {
                SelfId = id.ToString(), Code = p.Code, LeaderId = p.Leader.ToString(), Mode = p.Mode,
                Phase = a == null ? p.Phase : a.Phase, InstanceId = a?.Id, PublicQueue = p.PublicQueue,
                Address = a?.Phase == "Ready" ? address : null, ServerName = serverName, Port = port, Message = p.Message,
                NetworkProtocol = OnlineNetworkProtocol.Version, ConnectionPath = a?.ConnectionPath ?? p.ConnectionPath,
                IceAvailable = a?.IceAvailable ?? false, IceHostId = a?.IceHostId, IceHostAddress = a?.IceHostAddress,
                CanRetry = CanRetry(a, seat, now), CanLeave = a == null || seat != null && (!seat.Admitted || seat.Settled),
                RoomDirectoryAvailable = true, Rooms = browseRooms ? BrowseRooms(p, now) : null,
                JoinRequests = p.Leader == id && p.Phase == "Lobby" && p.Instance == null ? LeaderRequests(p) : Array.Empty<LobbyJoinRequest>(),
                Members = p.Members.Select(m => new LobbyMember { PlayerId = m.Id.ToString(), Name = m.Name,
                    Ready = m.Ready, Team = m.Team, Appearance = m.Appearance.Copy(), Connected = now - m.Seen < HeartbeatTimeout,
                    ReconnectSeconds = Math.Max(0, (int)Math.Ceiling((ReconnectGrace - (now - m.Seen)).TotalSeconds)),
                    Returned = a?.Seats.FirstOrDefault(s => s.Player == m.Id)?.Returned ?? false }).ToArray()
            };
        }
    }
    public void UpdateAppearance(Guid id, LobbyAppearance appearance)
    {
        lock (gate)
        {
            var party = Find(id);
            if (party == null || party.Instance != null) return;
            var member = party.Members.Single(m => m.Id == id);
            if (member.Appearance.SameAs(appearance)) return;
            if (party.Phase == "Queued") { Reset(party); party.Message = "出战配置已更改，请重新准备。"; }
            member.Ready = false;
            member.Appearance = appearance.Copy();
            Save();
        }
    }
    public void Act(Guid id, string name, bool raidActive, LobbyAction request, DateTime now, Guid? eligibleApplicant = null)
    {
        lock (gate)
        {
            if (Maintain(now)) Save();
            var p = Find(id);
            if (request.Action is "request-join" or "withdraw-join")
            {
                if (UpdateJoinApplication(id, name, raidActive, p, request, now)) Save();
                return;
            }
            if (request.Action == "create" || request.Action == "join")
            {
                Require(p == null, "请先离开当前队伍。");
                Require(!raidActive, "原战局尚未结算，请先恢复或等待结算。");
                if (request.Action == "create")
                {
                    Require(state.Parties.Count < 256, "大厅已满，请稍后重试。");
                    string code;
                    do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); } while (state.Parties.Any(x => x.Code == code));
                    p = new Party { Code = code, Leader = id };
                    state.Parties.Add(p);
                }
                else
                {
                    p = state.Parties.FirstOrDefault(x => x.Code == request.Code?.Trim().ToUpperInvariant());
                    Require(p != null && p.Phase == "Lobby" && p.Instance == null, "队伍不存在或已经出发。");
                    Require(p!.Members.Count < PartyCapacity, "队伍已满（最多 4 人）。");
                }
                p!.Members.Add(new Member { Id = id, Name = name, Seen = now });
                state.JoinApplications.Remove(id);
                Reset(p);
            }
            else
            {
                Require(p != null, "请先创建或加入队伍。");
                var me = p!.Members.Single(m => m.Id == id); me.Seen = now;
                if (request.Action == "return")
                {
                    var seat = state.Active?.Seats.FirstOrDefault(s => s.Player == id);
                    if (seat != null)
                    {
                        // Merely clicking Return cannot release a deployment that still owns gear.
                        Require(!raidActive, "结算尚未写入仓库，请等待后重试。");
                        seat.Returned = true;
                        if (!seat.Admitted) seat.Settled = true;
                    }
                }
                else if (request.Action == "retry")
                {
                    var active = p.Instance == state.Active?.Id ? state.Active : null;
                    var seat = active?.Seats.FirstOrDefault(s => s.Player == id);
                    Require(CanRetry(active, seat, now), "本局已结束或已完成结算，不能重新加入。");
                    if (!seat!.Admitted)
                    {
                        Require(!raidActive, "仍有未结算的装备。");
                        // Recover an entry that an older client incorrectly marked as returned.
                        seat.Returned = seat.Settled = false;
                    }
                }
                else if (request.Action == "leave")
                {
                    if (p.Instance != null)
                    {
                        var seat = state.Active?.Id == p.Instance ? state.Active.Seats.FirstOrDefault(s => s.Player == id) : null;
                        Require(!raidActive && seat != null && (!seat.Admitted || seat.Settled), "请等待本人的战局结算后再离队。");
                        seat!.Returned = true;
                        if (!seat.Admitted) seat.Settled = true;
                    }
                    p.Members.Remove(me);
                    if (p.Members.Count == 0) state.Parties.Remove(p);
                    else { if (p.Leader == id) p.Leader = p.Members[0].Id; if (p.Instance == null) Reset(p); }
                }
                else if (request.Action == "cancel")
                {
                    Require(p.Leader == id && p.Phase == "Queued" && p.Instance == null, "只有队长可取消尚未分配的对局。"); Reset(p);
                }
                else
                {
                    Require(p.Phase == "Lobby" && p.Instance == null, "出发后不能更改队伍，请先取消等待。");
                    Require(!raidActive, "仍有未结算的装备。");
                    switch (request.Action)
                    {
                        case "settings":
                            Require(p.Leader == id && request.Mode is "Coop" or "Versus", "只有队长可选择合作或对抗。");
                            Require(OnlineNetworkProtocol.ValidPath(request.ConnectionPath), "请选择 P2P 直连或 VPS 中转。");
                            Reset(p); p.Mode = request.Mode; p.PublicQueue = request.PublicQueue; p.ConnectionPath = request.ConnectionPath; break;
                        case "team":
                            Require(request.Team is 0 or 1, "请选择 A 队或 B 队。"); Reset(p); me.Team = request.Team; break;
                        case "ready": me.Ready = request.Ready; break;
                        case "approve-join":
                        case "decline-join":
                            ResolveJoinApplication(p, id, request, now, eligibleApplicant); break;
                        case "kick":
                            Require(p.Leader == id, "只有队长可移除队员。");
                            Require(Guid.TryParse(request.TargetPlayerId, out var target) && target != id && p.Members.Any(m => m.Id == target),
                                "请选择其他队员。");
                            p.Members.RemoveAll(m => m.Id == target); Reset(p); break;
                        case "start":
                            Require(p.Leader == id, "只有队长可出发。");
                            Require(p.Members.All(m => m.Ready && now - m.Seen < HeartbeatTimeout), "所有在线队员都需准备。");
                            if (p.Mode == "Versus" && !p.PublicQueue)
                                Require(p.Members.Select(m => m.Team).Distinct().Count() >= 2, "好友对抗需要至少两人，分别加入 A、B 队。");
                            p.Phase = "Queued"; p.QueuedAt = now; p.Message = p.PublicQueue ? "正在寻找队伍；队长可取消。" : "等待本机对局槽位。"; break;
                        default: throw new InvalidOperationException("未知大厅操作。");
                    }
                }
            }
            Save();
        }
    }
    private static void Reset(Party p)
    { p.Phase = "Lobby"; p.Message = ""; foreach (var m in p.Members) m.Ready = false; }
    private static bool CanRetry(Allocation? allocation, Seat? seat, DateTime now) =>
        allocation?.Phase == "Ready" && seat != null && (seat.Admitted
            ? !seat.Returned && !seat.Settled : now - allocation.ReadyAt < TimeSpan.FromMinutes(5));
    public Allocation? Current()
    { lock (gate) return state.Active == null ? null : System.Text.Json.JsonSerializer.Deserialize<Allocation>(System.Text.Json.JsonSerializer.Serialize(state.Active)); }
    public Allocation? Allocate(DateTime now)
    {
        lock (gate)
        {
            bool changed = Maintain(now);
            if (state.Active != null) { if (changed) Save(); return null; }
            var queued = state.Parties.Where(p => p.Phase == "Queued").OrderBy(p => p.QueuedAt).ToList();
            List<Party>? chosen = null;
            foreach (var first in queued)
            {
                chosen = new List<Party> { first };
                if (first.PublicQueue)
                {
                    foreach (var other in queued.Where(p => p != first && p.PublicQueue && p.Mode == first.Mode && p.ConnectionPath == first.ConnectionPath))
                        if (chosen.Sum(p => p.Members.Count) + other.Members.Count <= PartyCapacity) chosen.Add(other);
                    if (first.Mode == "Versus" && chosen.Count < 2) { chosen = null; continue; }
                    // Coop never waits for strangers; public queue just allows another ready party to join this allocation.
                }
                break;
            }
            if (chosen == null || chosen.Count == 0) { if (changed) Save(); return null; }
            var a = new Allocation { Id = Guid.NewGuid().ToString("N"), Mode = chosen[0].Mode,
                ConnectionPath = chosen[0].ConnectionPath, Created = now };
            for (int i = 0; i < chosen.Count; i++)
            {
                var p = chosen[i]; p.Instance = a.Id; p.Phase = "Starting"; p.Message = "正在为本局加载全新地图。";
                a.Seats.AddRange(p.Members.Select(m => new Seat { Player = m.Id, Team = a.Mode == "Coop" ? 0 : p.PublicQueue ? i : m.Team }));
            }
            state.Active = a; Save(); return Current();
        }
    }
    public void Ready(string instance, DateTime now, int protocol, bool iceAvailable = false, string? iceHostId = null, string? iceHostAddress = null)
    {
        lock (gate)
        {
            Require(state.Active?.Id == instance && protocol == OnlineNetworkProtocol.Version, "对局实例或协议不匹配，请同步更新独立服务器。");
            Require(state.Active!.ConnectionPath != OnlineNetworkProtocol.P2P || iceAvailable, "本局选择了 P2P，但独立服务器未启用内置连接模块。");
            if (state.Active.Phase == "Starting")
            {
                state.Active.IceAvailable = iceAvailable;
                state.Active.IceHostId = iceHostId;
                state.Active.IceHostAddress = iceHostAddress;
                state.Active.Phase = "Ready"; state.Active.ReadyAt = now; Save();
            }
        }
    }
    public (string Instance, int Team) Admission(Guid id, string instance, DateTime now, bool consume = false)
    {
        lock (gate)
        {
            var a = state.Active;
            var seat = a?.Seats.FirstOrDefault(s => s.Player == id);
            Require(a?.Id == instance && a.Phase == "Ready" && seat != null && !seat.Returned && !seat.Settled,
                "这张地图未就绪、已结束，或你不在本局名单中。");
            if (consume) { seat!.Admitted = true; Save(); }
            return (a!.Id, seat!.Team);
        }
    }
    public void Settled(Guid player)
    {
        lock (gate)
        {
            var seat = state.Active?.Seats.FirstOrDefault(s => s.Player == player);
            if (seat == null || !seat.Admitted) return;
            seat.Settled = true; Save();
        }
    }
    public bool ShouldStop(DateTime now)
    {
        lock (gate)
        {
            var a = state.Active;
            if (a == null) return false;
            if (a.Phase == "Starting") return now - a.Created > TimeSpan.FromMinutes(3);
            // Give each client time to load/connect, and keep result screens alive until Return.
            if (now - a.ReadyAt > TimeSpan.FromMinutes(5))
                foreach (var s in a.Seats.Where(s => !s.Admitted)) { s.Settled = true; s.Returned = true; }
            return a.Seats.All(s => s.Settled && s.Returned) ||
                (a.Seats.All(s => s.Returned) && now - a.ReadyAt > TimeSpan.FromMinutes(2)) ||
                now - a.Created > TimeSpan.FromMinutes(50);
        }
    }
    public void Finish(string instance, string? failure = null)
    {
        lock (gate)
        {
            if (state.Active?.Id != instance) return;
            foreach (var p in state.Parties.Where(p => p.Instance == instance))
            { p.Instance = null; Reset(p); p.Message = failure ?? "本局已结束。队伍已保留，可准备下一局。"; }
            state.Active = null; Save();
        }
    }
}
