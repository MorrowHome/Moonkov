using System.Security.Cryptography;
using Unity.MP_FPS.Online;

// One server slot is intentional on the host PC. Lobby/account traffic stays light;
// each allocation gets a new process and a clean map, never a reused raid World.
public sealed class OnlineLobbies
{
    public sealed class Member
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public bool Ready { get; set; }
        public int Team { get; set; }
        public DateTime Seen { get; set; }
    }
    public sealed class Party
    {
        public string Code { get; set; } = "";
        public Guid Leader { get; set; }
        public string Mode { get; set; } = "Coop";
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
        public string Phase { get; set; } = "Starting";
        public DateTime Created { get; set; }
        public DateTime ReadyAt { get; set; }
        public List<Seat> Seats { get; set; } = new();
    }
    public sealed class State
    {
        public List<Party> Parties { get; set; } = new();
        public Allocation? Active { get; set; }
    }
    private readonly object gate = new();
    private State state = new();
    private readonly string? stateFile;
    public OnlineLobbies(string? path = null)
    {
        stateFile = path;
        if (path != null && File.Exists(path))
            state = System.Text.Json.JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new();
    }
    private Party? Find(Guid id) => state.Parties.FirstOrDefault(p => p.Members.Any(m => m.Id == id));
    private static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private void Save()
    {
        if (stateFile == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        var temp = stateFile + ".tmp";
        File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(state));
        File.Move(temp, stateFile, true);
    }
    public LobbySnapshot Snapshot(Guid id, DateTime now, string address, string serverName, int port)
    {
        lock (gate)
        {
            var p = Find(id);
            if (p == null) return new LobbySnapshot { SelfId = id.ToString(), Phase = "None" };
            p.Members.Single(m => m.Id == id).Seen = now;
            var a = p.Instance == state.Active?.Id ? state.Active : null;
            return new LobbySnapshot
            {
                SelfId = id.ToString(), Code = p.Code, LeaderId = p.Leader.ToString(), Mode = p.Mode,
                Phase = a == null ? p.Phase : a.Phase, InstanceId = a?.Id, PublicQueue = p.PublicQueue,
                Address = a?.Phase == "Ready" ? address : null, ServerName = serverName, Port = port, Message = p.Message,
                Members = p.Members.Select(m => new LobbyMember { PlayerId = m.Id.ToString(), Name = m.Name,
                    Ready = m.Ready, Team = m.Team, Returned = a?.Seats.FirstOrDefault(s => s.Player == m.Id)?.Returned ?? false }).ToArray()
            };
        }
    }
    public void Act(Guid id, string name, bool raidActive, LobbyAction request, DateTime now)
    {
        lock (gate)
        {
            var p = Find(id);
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
                    Require(p!.Members.Count < 4, "队伍已满（最多 4 人）。");
                }
                p!.Members.Add(new Member { Id = id, Name = name, Seen = now });
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
                else if (request.Action == "leave")
                {
                    Require(p.Instance == null, "请先返回队伍，等待本局所有成员结算。");
                    p.Members.Remove(me);
                    if (p.Members.Count == 0) state.Parties.Remove(p);
                    else { if (p.Leader == id) p.Leader = p.Members[0].Id; Reset(p); }
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
                            Reset(p); p.Mode = request.Mode; p.PublicQueue = request.PublicQueue; break;
                        case "team":
                            Require(request.Team is 0 or 1, "请选择 A 队或 B 队。"); Reset(p); me.Team = request.Team; break;
                        case "ready": me.Ready = request.Ready; break;
                        case "start":
                            Require(p.Leader == id, "只有队长可出发。");
                            Require(p.Members.All(m => m.Ready && now - m.Seen < TimeSpan.FromSeconds(30)), "所有在线队员都需准备。");
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
    public Allocation? Current()
    { lock (gate) return state.Active == null ? null : System.Text.Json.JsonSerializer.Deserialize<Allocation>(System.Text.Json.JsonSerializer.Serialize(state.Active)); }
    public Allocation? Allocate(DateTime now)
    {
        lock (gate)
        {
            if (state.Active != null) return null;
            bool changed = false;
            foreach (var p in state.Parties.Where(p => p.Phase == "Queued").ToArray())
                if (p.Members.Any(m => now - m.Seen > TimeSpan.FromSeconds(30))) { Reset(p); p.Message = "队员离线，已取消出发。"; changed = true; }
            foreach (var stale in state.Parties.Where(p => p.Phase == "Lobby" && p.Instance == null && p.Members.All(m => now - m.Seen > TimeSpan.FromDays(1))).ToArray())
            { state.Parties.Remove(stale); changed = true; }
            var queued = state.Parties.Where(p => p.Phase == "Queued").OrderBy(p => p.QueuedAt).ToList();
            List<Party>? chosen = null;
            foreach (var first in queued)
            {
                chosen = new List<Party> { first };
                if (first.PublicQueue)
                {
                    foreach (var other in queued.Where(p => p != first && p.PublicQueue && p.Mode == first.Mode))
                        if (chosen.Sum(p => p.Members.Count) + other.Members.Count <= 4) chosen.Add(other);
                    if (first.Mode == "Versus" && chosen.Count < 2) { chosen = null; continue; }
                    // Coop never waits for strangers; public queue just allows another ready party to join this allocation.
                }
                break;
            }
            if (chosen == null || chosen.Count == 0) { if (changed) Save(); return null; }
            var a = new Allocation { Id = Guid.NewGuid().ToString("N"), Mode = chosen[0].Mode, Created = now };
            for (int i = 0; i < chosen.Count; i++)
            {
                var p = chosen[i]; p.Instance = a.Id; p.Phase = "Starting"; p.Message = "正在为本局加载全新地图。";
                a.Seats.AddRange(p.Members.Select(m => new Seat { Player = m.Id, Team = a.Mode == "Coop" ? 0 : p.PublicQueue ? i : m.Team }));
            }
            state.Active = a; Save(); return Current();
        }
    }
    public void Ready(string instance, DateTime now, int protocol)
    {
        lock (gate)
        {
            Require(state.Active?.Id == instance && protocol == 1, "对局实例或协议不匹配。");
            if (state.Active!.Phase == "Starting") { state.Active.Phase = "Ready"; state.Active.ReadyAt = now; Save(); }
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
