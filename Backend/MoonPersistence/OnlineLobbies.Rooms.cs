using Unity.MP_FPS.Online;

public sealed partial class OnlineLobbies
{
    private static readonly TimeSpan ApplicationLifetime = TimeSpan.FromMinutes(2);
    private const int MaxApplicationsPerRoom = 12;
    private const int MaxApplications = 4096;

    public sealed class JoinApplication
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string RoomId { get; set; } = "";
        public string ApplicantName { get; set; } = "";
        public string LeaderName { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public DateTime Seen { get; set; }
        public DateTime Deadline { get; set; }
    }

    private static Member Leader(Party party) => party.Members.Single(m => m.Id == party.Leader);

    private string RoomStatus(Party party, DateTime now)
    {
        if (party.Instance != null) return state.Active?.Id == party.Instance && state.Active.Phase == "Ready" ? "InRaid" : "Starting";
        if (party.Phase == "Queued") return "Queued";
        if (party.Members.Count >= PartyCapacity) return "Full";
        return now - Leader(party).Seen < HeartbeatTimeout ? "Open" : "Offline";
    }

    private LobbyRoomInfo[] BrowseRooms(Party? own, DateTime now) => state.Parties
        .Where(p => p != own && (p.Instance != null || p.Members.Any(m => now - m.Seen < HeartbeatTimeout)))
        .Select(p => new LobbyRoomInfo { RoomId = p.RoomId, LeaderName = Leader(p).Name, Mode = p.Mode,
            ConnectionPath = p.ConnectionPath, Status = RoomStatus(p, now), MemberCount = p.Members.Count, Capacity = PartyCapacity })
        .OrderByDescending(r => r.Status == "Open").ThenBy(r => r.LeaderName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RoomId).ToArray();

    private IEnumerable<JoinApplication> PendingApplications(Party party) => state.JoinApplications.Values
        .Where(r => r.RoomId == party.RoomId && r.Status == "Pending");

    private LobbyJoinRequest[] LeaderRequests(Party party) => PendingApplications(party).OrderBy(r => r.Deadline)
        .Select(r => new LobbyJoinRequest { RequestId = r.Id, ApplicantName = r.ApplicantName }).ToArray();

    private LobbyJoinRequest? ApplicantSnapshot(Guid id, DateTime now, bool raidActive, ref bool changed)
    {
        if (!state.JoinApplications.TryGetValue(id, out var request)) return null;
        if (request.Status == "Pending")
        {
            request.Seen = now;
            if (raidActive) { FinishApplication(request, "Unavailable", now); changed = true; }
        }
        var room = state.Parties.Find(p => p.RoomId == request.RoomId);
        return new LobbyJoinRequest { RequestId = request.Id, RoomId = request.RoomId, Status = request.Status,
            LeaderName = room == null ? request.LeaderName : Leader(room).Name };
    }

    private bool UpdateJoinApplication(Guid id, string name, bool raidActive, Party? own, LobbyAction action, DateTime now)
    {
        state.JoinApplications.TryGetValue(id, out var existing);
        if (action.Action == "withdraw-join")
            return existing?.Status == "Pending" && existing.Id == action.RequestId && state.JoinApplications.Remove(id);

        Require(own == null, "请先离开当前队伍。");
        Require(!raidActive, "原战局尚未结算，请先恢复或等待结算。");
        if (existing?.Status == "Pending")
        {
            Require(existing.RoomId == action.RoomId, "请先撤回当前申请。");
            return false;
        }
        var party = state.Parties.Find(p => p.RoomId == action.RoomId);
        if (party == null || RoomStatus(party, now) != "Open")
            throw new InvalidOperationException("房间当前无法加入，请刷新后重试。");
        Require(existing == null || existing.Status != "Declined" || existing.RoomId != party.RoomId ||
            now >= existing.Deadline - ApplicationLifetime + TimeSpan.FromSeconds(30), "队长刚刚拒绝了申请，请稍后再试。");
        Require(PendingApplications(party).Count() < MaxApplicationsPerRoom &&
            (existing != null || state.JoinApplications.Count < MaxApplications), "等待审批的申请较多，请稍后再试。");
        state.JoinApplications[id] = new JoinApplication { RoomId = party.RoomId, ApplicantName = name,
            LeaderName = Leader(party).Name, Seen = now, Deadline = now + ApplicationLifetime };
        return true;
    }

    private KeyValuePair<Guid, JoinApplication> FindApplication(Party? party, Guid leader, string requestId, DateTime now)
    {
        if (party == null || party.Leader != leader || RoomStatus(party, now) != "Open")
            throw new InvalidOperationException("只有可加入房间的在线队长能处理申请。");
        var entry = state.JoinApplications.FirstOrDefault(r => r.Value.RoomId == party.RoomId && r.Value.Id == requestId &&
            r.Value.Status == "Pending" && r.Value.Deadline > now && now - r.Value.Seen < HeartbeatTimeout);
        Require(entry.Value != null, "这条申请已处理或失效。");
        return entry;
    }

    public Guid ApprovalApplicant(Guid leader, string requestId, DateTime now)
    {
        lock (gate) return FindApplication(Find(leader), leader, requestId, now).Key;
    }

    private void ResolveJoinApplication(Party party, Guid leader, LobbyAction action, DateTime now, Guid? eligibleApplicant)
    {
        var (applicant, request) = FindApplication(party, leader, action.RequestId, now);
        if (action.Action == "decline-join") { FinishApplication(request, "Declined", now); return; }
        Require(eligibleApplicant == applicant && Find(applicant) == null, "申请人有未结算的战局或已加入其他小队。");
        party.Members.Add(new Member { Id = applicant, Name = request.ApplicantName, Seen = request.Seen });
        state.JoinApplications.Remove(applicant);
        Reset(party);
    }

    private static void FinishApplication(JoinApplication request, string status, DateTime now)
    { request.Status = status; request.Deadline = now + ApplicationLifetime; }

    private bool MaintainJoinApplications(DateTime now)
    {
        if (state.JoinApplications.Count == 0) return false;
        bool changed = false;
        List<Guid>? expired = null;
        var rooms = state.Parties.ToDictionary(p => p.RoomId);
        foreach (var (id, request) in state.JoinApplications)
        {
            if (request.Status != "Pending")
            {
                if (now >= request.Deadline) (expired ??= new()).Add(id);
                continue;
            }
            string? status = now >= request.Deadline || now - request.Seen >= HeartbeatTimeout ? "Expired" :
                !rooms.TryGetValue(request.RoomId, out var room) || RoomStatus(room, now) != "Open" ? "Unavailable" : null;
            if (status == null) continue;
            FinishApplication(request, status, now); changed = true;
        }
        if (expired != null) foreach (var id in expired) state.JoinApplications.Remove(id);
        return changed || expired != null;
    }
}
