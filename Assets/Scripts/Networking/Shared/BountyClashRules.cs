// 极速赏金的比赛规则。维护队伍、报名队列、生命阶段、撤离和奖励，服务端用它推进比赛，再把判断结果同步给客户端。
using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.MP_FPS {
    public enum ClashPhase : byte {
        Lobby,
        Preparing,
        Deploying,
        Active,
        Finished
    }

    public enum ClashLife : byte {
        Waiting,
        Alive,
        Downed,
        Dead,
        Extracted,
        TimedOut
    }

    public enum ClashBountyState : byte {
        Unbanished,
        Banishing,
        Ground,
        Carried,
        Extracted
    }

    public enum ClashResult : byte {
        Ok,
        Stale,
        Invalid,
        WrongPhase,
        Full,
        Forbidden,
        Busy,
        Blocked,
        Unsupported
    }

    public enum ClashExitBlock : byte {
        None,
        Enemy,
        DownedTeammate,
        NotActive
    }

    [Serializable]
    public struct ClashPoint {
        public float X, Y, Z;

        public ClashPoint(float x, float y, float z) {
            X = x;
            Y = y;
            Z = z;
        }

        public static double DistanceSquared(ClashPoint a, ClashPoint b) {
            return (a.X - b.X) * (double)(a.X - b.X) + (a.Y - b.Y) * (double)(a.Y - b.Y) +
                   (a.Z - b.Z) * (double)(a.Z - b.Z);
        }

        public static double HorizontalSquared(ClashPoint a, ClashPoint b) {
            return (a.X - b.X) * (double)(a.X - b.X) + (a.Z - b.Z) * (double)(a.Z - b.Z);
        }

        public bool Finite =>
            !float.IsNaN(X) && !float.IsInfinity(X) && !float.IsNaN(Y) && !float.IsInfinity(Y) && !float.IsNaN(Z) &&
            !float.IsInfinity(Z);
    }

    [Serializable]
    public sealed class ClashArenaDefinition {
        public ClashPoint Center, Target;

        public float Radius,
            ExitRadius = 8,
            BlockRadius = 15,
            BanishSeconds = 60,
            ReadySeconds = 10,
            ReconnectSeconds = 90;

        public ClashPoint[] Spawns = Array.Empty<ClashPoint>(), Exits = Array.Empty<ClashPoint>();

        // 先确保比赛区域有限、两个出口以及出生点都合理。上层只有在地图配置通过后才会让人报名。
        public string Validate() {
            if (!Center.Finite || !Target.Finite || Radius < 20 || Radius > 1500 || !float.IsFinite(Radius) ||
                Spawns == null || Spawns.Length != 12 || Exits == null || Exits.Length != 2 || ExitRadius < 2 ||
                ExitRadius > 30 || BlockRadius < ExitRadius || BlockRadius > 60 || !float.IsFinite(ExitRadius) ||
                !float.IsFinite(BlockRadius) || BanishSeconds < 1 || BanishSeconds > 300 || ReadySeconds < 1 ||
                ReadySeconds > 60 || ReconnectSeconds < 1 || ReconnectSeconds > 300 || !float.IsFinite(BanishSeconds) ||
                !float.IsFinite(ReadySeconds) || !float.IsFinite(ReconnectSeconds))
                return "arena_parameters";
            foreach (var p in Spawns.Concat(Exits).Append(Target))
                if (!p.Finite || ClashPoint.HorizontalSquared(p, Center) > Radius * (double)Radius)
                    return "arena_anchor";
            return null;
        }
    }

    public static class BountyClashRules {
        public const int Version = 1, MaxPlayers = 12, MaxTeams = 4;

        public const float Duration = 900,
            BoundaryGrace = 20,
            ExtractionSeconds = 30,
            ExitTail = 90,
            ReviveSeconds = 5,
            SenseSeconds = 5,
            BoostRange = 125,
            InstinctRange = 75;

        public static bool Ended(ClashLife life) {
            return life == ClashLife.Dead || life == ClashLife.Extracted || life == ClashLife.TimedOut;
        }

        public static int Reward(int startingTeamSize) {
            return 20 + (startingTeamSize == 1 ? 60 : startingTeamSize == 2 ? 15 : 0);
        }
    }

    public sealed class ClashParticipant {
        public readonly int Id;
        public readonly string Account;
        public readonly bool IsBot;
        public string Name, DeploymentId;
        public int TeamId = -1, Slot, NetworkId, Downs, StartingTeamSize, LastAttacker;
        public bool Ready, Connected = true, Deployed;
        public ClashLife Life = ClashLife.Waiting;
        public ClashPoint Position;
        public double DisconnectDeadline, OutsideSince = -1, SenseLease, ViewLease;
        public float SenseRemaining = 5;
        public int Exit = -1;
        public float Health;
        public uint DamageRevision, LastRequest;
        public ClashResult Result;
        public float MaxFraction => Math.Max(0, 1 - Downs * .25f);
        public bool IsFinished => BountyClashRules.Ended(Life);

        internal ClashParticipant(int id, string account, string name, bool bot = false) {
            Id = id;
            Account = account;
            Name = name;
            IsBot = bot;
        }
    }

    public sealed class ClashTeam {
        public int Id, Leader, Capacity = 3;
        public string Invite;
        public readonly List<int> Members = new();
    }

    public sealed class ClashExtraction {
        public readonly string MatchId, TokenId, EventId, CarrierDeploymentId;
        public readonly string[] DeploymentIds;
        public readonly int CarrierParticipantId;

        public ClashExtraction(
            string match,
            string token,
            string carrier,
            string[] deployments,
            int carrierParticipantId = 0) {
            MatchId = match;
            TokenId = token;
            CarrierDeploymentId = carrier;
            DeploymentIds = (string[])deployments.Clone();
            EventId = Guid.NewGuid().ToString("D");
            CarrierParticipantId = carrierParticipantId;
        }
    }

    public sealed class BountyClashMatch {
        public readonly string MatchId = Guid.NewGuid().ToString("D"), TokenId = Guid.NewGuid().ToString("D");
        public readonly int Epoch;
        public readonly bool IsSolo;
        public readonly ClashArenaDefinition Arena;
        public readonly Dictionary<int, ClashParticipant> Players = new();
        public readonly Dictionary<int, ClashTeam> Teams = new();
        private readonly Dictionary<string, int> m_Accounts = new(StringComparer.Ordinal);
        private readonly Dictionary<int, Revive> m_Revives = new();
        private readonly double[,] m_ExitStarted = new double[4, 2];
        private readonly ClashExitBlock[,] m_ExitBlocked = new ClashExitBlock[4, 2];
        private int m_NextPlayer = 1;
        private double m_LastTime, m_BanishAt;
        private int m_BanishTeam = -1;
        private readonly double[,] m_ExitElapsed = new double[4, 2];
        public ClashPhase Phase { get; private set; }
        public ClashBountyState Bounty { get; private set; }
        public int CarrierId { get; private set; } = -1;
        public ClashPoint BountyPosition { get; private set; }
        public double Deadline { get; private set; }
        public double PrepareDeadline { get; private set; }
        public bool DeploymentAborted { get; private set; }
        public double DeploymentDeadline { get; private set; }
        public uint Revision { get; private set; } = 1;
        public ClashExtraction Award { get; private set; }
        public bool BanishHealed { get; private set; }
        public int BanishTeam => m_BanishTeam;

        private sealed class Revive {
            public int Actor, Target;
            public double Started, Lease;
            public uint ActorDamage, TargetDamage;
        }

        public BountyClashMatch(ClashArenaDefinition arena, int epoch, double now, bool solo = false) {
            if (arena == null || arena.Validate() != null || epoch < 1 || !TimeValid(now))
                throw new ArgumentException("Invalid clash arena.");
            Arena = arena;
            Epoch = epoch;
            IsSolo = solo;
            m_LastTime = now;
            BountyPosition = arena.Target;
            for (int t = 0; t < 4; t++)
            for (int e = 0; e < 2; e++)
                m_ExitStarted[t, e] = -1;
        }

        private static bool TimeValid(double now) { return !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0; }

        public ClashParticipant Account(string account) {
            return account != null && m_Accounts.TryGetValue(account, out int id) ? Players[id] : null;
        }

        // 同一账号只能认回自己的参赛者。是否还在准备阶段和有无空位决定能否为新玩家分席。
        public ClashParticipant Join(string account, string name, double now) {
            if (!TimeValid(now) || now < m_LastTime || string.IsNullOrWhiteSpace(account)) return null;
            var old = Account(account);
            if (old != null) {
                if (old.Connected || (!old.IsFinished && now > old.DisconnectDeadline)) return null;
                old.Connected = true;
                old.DisconnectDeadline = 0;
                Revision++;
                return old;
            }
            if (IsSolo && Players.Values.Any(participant => !participant.IsBot)) return null;
            if ((Phase != ClashPhase.Lobby && Phase != ClashPhase.Preparing) || Players.Count >= 12 ||
                (Teams.Count >= 4 && Teams.Values.All(t => t.Members.Count >= t.Capacity)))
                return null;
            var p = new ClashParticipant(m_NextPlayer++, account, name);
            Players.Add(p.Id, p);
            m_Accounts.Add(account, p.Id);
            Revision++;
            return p;
        }

        // 机器人没有账号身份，不能通过真人账号映射重连。
        public ClashParticipant JoinBot(string name) {
            if (!IsSolo || !Lobby() || Players.Count >= BountyClashRules.MaxPlayers) return null;
            var p = new ClashParticipant(m_NextPlayer++, null, name, true);
            Players.Add(p.Id, p);
            Revision++;
            return p;
        }

        // 验比赛号、轮次和递增请求号。客户端重新点击与 RPC 重发不能在另一轮修改队伍。
        public bool Request(ClashParticipant p, uint request, int epoch, string match) {
            if (p == null || !p.Connected || epoch != Epoch || match != MatchId || request == 0 ||
                request <= p.LastRequest)
                return false;
            p.LastRequest = request;
            p.Result = ClashResult.Ok;
            return true;
        }

        // 建队、加队、离队都属于准备阶段。成员变化后重置准备，必须按新队伍重新确认。
        public ClashResult CreateTeam(ClashParticipant p, int size) {
            if (!Lobby()) return ClashResult.WrongPhase;
            if (size < 1 || size > 3) return ClashResult.Invalid;
            if (p.TeamId >= 0) return ClashResult.Forbidden;
            int id = Enumerable.Range(0, 4).Where(t => !Teams.ContainsKey(t)).DefaultIfEmpty(-1).First();
            if (id < 0) return ClashResult.Full;
            var team = new ClashTeam {
                Id = id, Leader = p.Id, Capacity = size,
                Invite = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant()
            };
            team.Members.Add(p.Id);
            Teams.Add(id, team);
            p.TeamId = id;
            p.Slot = 0;
            ResetReady();
            return ClashResult.Ok;
        }

        public ClashResult JoinTeam(ClashParticipant p, string invite) {
            if (!Lobby()) return ClashResult.WrongPhase;
            if (p.TeamId >= 0) return ClashResult.Forbidden;
            var team = Teams.Values.FirstOrDefault(t => string.Equals(t.Invite, invite, StringComparison.Ordinal));
            if (team == null) return ClashResult.Forbidden;
            if (team.Members.Count >= team.Capacity) return ClashResult.Full;
            team.Members.Add(p.Id);
            p.TeamId = team.Id;
            p.Slot = team.Members.Count - 1;
            ResetReady();
            return ClashResult.Ok;
        }

        public ClashResult LeaveTeam(ClashParticipant p) {
            if (!Lobby()) return ClashResult.WrongPhase;
            if (p.TeamId >= 0 && Teams.TryGetValue(p.TeamId, out var t)) {
                t.Members.Remove(p.Id);
                if (t.Members.Count == 0) { Teams.Remove(t.Id); } else {
                    t.Leader = t.Members[0];
                    for (int i = 0; i < t.Members.Count; i++) Players[t.Members[i]].Slot = i;
                }
            }
            p.TeamId = -1;
            ResetReady();
            return ClashResult.Ok;
        }

        public ClashResult Capacity(ClashParticipant p, int size) {
            if (!Lobby() || p.TeamId < 0) return ClashResult.WrongPhase;
            var t = Teams[p.TeamId];
            if (t.Leader != p.Id || size < t.Members.Count || size < 1 || size > 3) return ClashResult.Forbidden;
            t.Capacity = size;
            ResetReady();
            return ClashResult.Ok;
        }

        // 两队且每位成员已连上并准备后启动开赛计时。有人取消或断开，要等全员再次具备条件。
        public ClashResult Ready(ClashParticipant p, bool ready, double now) {
            if (!Lobby() || p.TeamId < 0) return ClashResult.WrongPhase;
            p.Ready = ready;
            Revision++;
            if (!ready) {
                Phase = ClashPhase.Lobby;
                PrepareDeadline = 0;
            }
            if (Phase == ClashPhase.Lobby && AllReady()) {
                Phase = ClashPhase.Preparing;
                PrepareDeadline = now + Arena.ReadySeconds;
            }
            return ClashResult.Ok;
        }

        private bool Lobby() { return Phase == ClashPhase.Lobby || Phase == ClashPhase.Preparing; }

        private void ResetReady() {
            foreach (var p in Players.Values) p.Ready = false;
            Phase = ClashPhase.Lobby;
            PrepareDeadline = 0;
            Revision++;
        }

        private bool AllReady() {
            return Teams.Count >= 2 && Players.Count >= 2 &&
                   Players.Values.All(p => p.TeamId >= 0 && p.Ready && p.Connected);
        }

        // 断线先撤当前操作和准备，再按阶段保留重连窗口。活动角色不立即抹掉库存。
        public void Disconnect(ClashParticipant p, double now) {
            p.Connected = false;
            p.SenseLease = p.ViewLease = 0;
            p.DisconnectDeadline = now + Arena.ReconnectSeconds;
            if (Lobby()) {
                p.Ready = false;
                Phase = ClashPhase.Lobby;
                PrepareDeadline = 0;
            }
            Revision++;
        }

        public void MarkDeployed(int id) {
            if (Phase == ClashPhase.Deploying && Players.TryGetValue(id, out var p)) {
                p.Deployed = true;
                Revision++;
            }
        }

        public void Observe(int id, ClashPoint position, float health, uint damage) {
            if (Players.TryGetValue(id, out var p)) {
                p.Position = position;
                p.Health = health;
                p.DamageRevision = damage;
            }
        }

        // 能接近现场目标的存活角色启动驱逐计时。结束才生成可携带赏金。
        public ClashResult Banish(ClashParticipant p, bool accessible, double now) {
            if (!Active(p) || Bounty != ClashBountyState.Unbanished) return ClashResult.WrongPhase;
            if (!accessible) return ClashResult.Blocked;
            Bounty = ClashBountyState.Banishing;
            m_BanishAt = now;
            m_BanishTeam = p.TeamId;
            BanishHealed = true;
            foreach (var ally in Players.Values)
                if (ally.TeamId == p.TeamId && !ally.IsFinished)
                    ally.Downs = 0;
            Revision++;
            return ClashResult.Ok;
        }

        // 只有场上确实已掉出的赏金能被拾取。先绑定具体携带者，其他人随后不再拿到同一份。
        public ClashResult Pickup(ClashParticipant p, bool accessible) {
            if (!Active(p) || Bounty != ClashBountyState.Ground) return ClashResult.WrongPhase;
            if (!accessible) return ClashResult.Blocked;
            CarrierId = p.Id;
            Bounty = ClashBountyState.Carried;
            Revision++;
            return ClashResult.Ok;
        }

        public void Sense(ClashParticipant p, bool held, double now) { Sense(p, held ? 3 : 0, now); }

        // 查看与加强各有短时租约。携带者的加强有效才随实际经过时间消耗额度。
        public void Sense(ClashParticipant p, int flags, double now) {
            p.ViewLease = Active(p) && (flags & 1) != 0 ? now + .6 : 0;
            p.SenseLease = p.ViewLease > now && p.Id == CarrierId && (flags & 2) != 0 && p.SenseRemaining > 0
                ? now + .6
                : 0;
        }

        // 活角色才能救同队倒地者。开始时同时记两人的受伤序号，期间被打或不续租都中断。
        public ClashResult BeginRevive(ClashParticipant p, int target, bool accessible, double now) {
            if (!Active(p) || !Players.TryGetValue(target, out var victim) || victim.TeamId != p.TeamId ||
                victim.Life != ClashLife.Downed)
                return ClashResult.Forbidden;
            if (!accessible) return ClashResult.Blocked;
            if (m_Revives.Values.Any(r => r.Target == target && r.Actor != p.Id)) return ClashResult.Busy;
            m_Revives[p.Id] = new Revive {
                Actor = p.Id, Target = target, Started = now, Lease = now + .6, ActorDamage = p.DamageRevision,
                TargetDamage = victim.DamageRevision
            };
            Revision++;
            return ClashResult.Ok;
        }

        public void RenewRevive(ClashParticipant p, bool accessible, double now) {
            if (m_Revives.TryGetValue(p.Id, out var r)) {
                if (!accessible)
                    m_Revives.Remove(p.Id);
                else
                    r.Lease = now + .6;
            }
        }

        public void CancelRevive(ClashParticipant p) { m_Revives.Remove(p.Id); }

        public float ReviveProgress(int id, double now) {
            return m_Revives.Values.Where(r => r.Target == id || r.Actor == id)
                .Select(r => (float)Math.Min(1, (now - r.Started) / 5)).FirstOrDefault();
        }

        public int ReviveTarget(int actor) { return m_Revives.TryGetValue(actor, out var r) ? r.Target : -1; }

        public bool Active(ClashParticipant p) {
            return Phase == ClashPhase.Active && p != null && p.Life == ClashLife.Alive;
        }

        // 归零可能进入倒地而不是立即离场，倒地次数还影响下一次起身可有的最大生命。
        public void Down(ClashParticipant p, int attacker) {
            if (!Active(p)) return;
            p.Downs++;
            p.LastAttacker = attacker;
            p.Life = p.MaxFraction > 0 ? ClashLife.Downed : ClashLife.Dead;
            Drop(p);
            m_Revives.Remove(p.Id);
            Revision++;
        }

        public void Kill(ClashParticipant p, ClashLife outcome = ClashLife.Dead) {
            if (p.IsFinished) return;
            p.Life = outcome;
            Drop(p);
            m_Revives.Remove(p.Id);
            Revision++;
        }

        // 本机单人玩家的结算成功保存后，可以退出本人已结束的行动。
        // 剩下的机器人按超时结束，不发额外奖励或再掉一套装备。
        public bool EndSoloRound() {
            if (!IsSolo || !Players.Values.Any(p => !p.IsBot) || Players.Values.Any(p => !p.IsBot && !p.IsFinished))
                return false;
            foreach (var p in Players.Values)
                if (!p.IsFinished)
                    Kill(p, ClashLife.TimedOut);
            Phase = ClashPhase.Finished;
            Revision++;
            return true;
        }

        private void Drop(ClashParticipant p) {
            if (CarrierId != p.Id) return;
            CarrierId = -1;
            Bounty = ClashBountyState.Ground;
            BountyPosition = p.Position;
        }

        public float BanishProgress(double now) {
            return Bounty == ClashBountyState.Banishing ? (float)Math.Min(1, (now - m_BanishAt) / Arena.BanishSeconds) :
                Bounty >= ClashBountyState.Ground ? 1 : 0;
        }

        public float BoundaryRemaining(ClashParticipant p, double now) {
            return p.OutsideSince < 0 ? 20 : (float)Math.Max(0, 20 - (now - p.OutsideSince));
        }

        public ClashExitBlock ExitBlock(int team, int exit) {
            return team >= 0 && team < 4 ? m_ExitBlocked[team, exit] : ClashExitBlock.NotActive;
        }

        public float ExitRemaining(int team, int exit, double now) {
            return team >= 0 && team < 4 && m_ExitStarted[team, exit] >= 0
                ? (float)Math.Max(0, 30 - m_ExitElapsed[team, exit])
                : -1;
        }

        public void Tick(double now) {
            // 规则只接受向前走的服务端时刻。实际经过的 dt 用来推进所有计时器，而不是按客户端 FPS。
            if (!TimeValid(now) || now < m_LastTime) return;
            double dt = now - m_LastTime;
            m_LastTime = now;
            if (Lobby())
                foreach (var p in Players.Values.ToArray())
                    if (!p.Connected && now >= p.DisconnectDeadline) {
                        LeaveTeam(p);
                        Players.Remove(p.Id);
                        if (!p.IsBot) m_Accounts.Remove(p.Account);
                    }
            // 准备倒计时到点确认所有人仍在队且准备，再给真人分配出战编号。之后的扣装按这一批固定名单执行。
            if (Phase == ClashPhase.Preparing) {
                if (!AllReady()) {
                    Phase = ClashPhase.Lobby;
                    PrepareDeadline = 0;
                    return;
                }
                if (now >= PrepareDeadline) {
                    Phase = ClashPhase.Deploying;
                    DeploymentDeadline = now + 120;
                    foreach (var p in Players.Values) {
                        p.DeploymentId = p.IsBot ? null : Guid.NewGuid().ToString("D");
                        p.StartingTeamSize = Teams[p.TeamId].Members.Count;
                    }
                    Revision++;
                }
            }
            // 部署超过时间或有人掉线过期，整场取消。其他成员不应该带着新扣的装备无限等待。
            if (Phase == ClashPhase.Deploying && (now >= DeploymentDeadline ||
                                                  Players.Values.Any(p =>
                                                      !p.Connected && now >= p.DisconnectDeadline))) {
                DeploymentAborted = true;
                Phase = ClashPhase.Finished;
                foreach (var p in Players.Values) p.Life = ClashLife.TimedOut;
                Revision++;
                return;
            }
            // 所有人真正完成生成才启动全局比赛时钟。进入对局的人有同一个开始时间。
            if (Phase == ClashPhase.Deploying && Players.Values.All(p => p.Deployed)) {
                Phase = ClashPhase.Active;
                Deadline = now + 900;
                foreach (var p in Players.Values) p.Life = ClashLife.Alive;
                Revision++;
            }
            if (Phase != ClashPhase.Active) return;
            if (now >= Deadline) {
                foreach (var p in Players.Values)
                    if (!p.IsFinished)
                        Kill(p, ClashLife.TimedOut);
                Phase = ClashPhase.Finished;
                Revision++;
                return;
            }
            foreach (var p in Players.Values) {
                if (p.IsFinished) continue;
                if (!p.Connected && now >= p.DisconnectDeadline) {
                    Kill(p);
                    continue;
                }
                // 区域边界按平面范围算。短时间出界还有缓冲，回到区内就清这次出界计时。
                bool outside = ClashPoint.HorizontalSquared(p.Position, Arena.Center) >
                               Arena.Radius * (double)Arena.Radius;
                if (!outside) { p.OutsideSince = -1; } else if (p.OutsideSince < 0) { p.OutsideSince = now; } else if
                    (now - p.OutsideSince >= 20) {
                    Kill(p);
                    continue;
                }
                if (p.Id == CarrierId && p.Connected && p.Life == ClashLife.Alive) {
                    float used = (float)Math.Max(0, Math.Min(now, p.SenseLease) - (now - dt));
                    p.SenseRemaining = Math.Max(0, p.SenseRemaining - used);
                }
            }
            if (Bounty == ClashBountyState.Banishing && now - m_BanishAt >= Arena.BanishSeconds) {
                Bounty = ClashBountyState.Ground;
                BountyPosition = Arena.Target;
                Revision++;
            }
            // 逐个检查复苏期是否仍有效。到点成功才让目标重新成为活角色，再由外层同步实际血量。
            foreach (var pair in m_Revives.ToArray()) {
                var r = pair.Value;
                var a = Players[r.Actor];
                var v = Players[r.Target];
                if (!Active(a) || !a.Connected || v.Life != ClashLife.Downed || now > r.Lease ||
                    a.DamageRevision != r.ActorDamage || v.DamageRevision != r.TargetDamage) {
                    m_Revives.Remove(pair.Key);
                    continue;
                }
                if (now - r.Started >= 5) {
                    v.Life = ClashLife.Alive;
                    v.Health = 1;
                    m_Revives.Remove(pair.Key);
                    Revision++;
                }
            }
            foreach (var team in Teams.Values) {
                var members = team.Members.Select(id => Players[id]).ToArray();
                // 一队已没有能救人的成员时，倒地者也转为失败，不让整个对局永远等他们。
                if (!members.Any(p => p.Life == ClashLife.Alive))
                    foreach (var p in members)
                        if (p.Life == ClashLife.Downed)
                            Kill(p);
                for (int exit = 0; exit < 2; exit++) {
                    var point = Arena.Exits[exit];
                    bool enemy = Players.Values.Any(p =>
                        p.TeamId != team.Id && p.Life == ClashLife.Alive &&
                        ClashPoint.DistanceSquared(p.Position, point) <= Arena.BlockRadius * (double)Arena.BlockRadius);
                    bool downed = members.Any(p =>
                        p.Life == ClashLife.Downed && ClashPoint.DistanceSquared(p.Position, point) <=
                        Arena.BlockRadius * (double)Arena.BlockRadius);
                    var escaping = members.Where(p =>
                        p.Life == ClashLife.Alive && ClashPoint.DistanceSquared(p.Position, point) <=
                        Arena.ExitRadius * (double)Arena.ExitRadius).ToArray();
                    bool wasBlocked = m_ExitBlocked[team.Id, exit] != ClashExitBlock.None;
                    // 有敌人或队友倒在附近会暂停撤离，完全走离开重置计时。持续无阻才开始累计这段成功条件。
                    m_ExitBlocked[team.Id, exit] = enemy ? ClashExitBlock.Enemy :
                        downed ? ClashExitBlock.DownedTeammate : ClashExitBlock.None;
                    if (escaping.Length == 0) {
                        m_ExitStarted[team.Id, exit] = -1;
                        m_ExitElapsed[team.Id, exit] = 0;
                        continue;
                    }
                    if (m_ExitStarted[team.Id, exit] < 0)
                        m_ExitStarted[team.Id, exit] = now;
                    else if (!enemy && !downed && !wasBlocked) m_ExitElapsed[team.Id, exit] += dt;
                    if (enemy || downed || m_ExitElapsed[team.Id, exit] < 30) continue;
                    var carrier = escaping.FirstOrDefault(p => p.Id == CarrierId);
                    // 这批撤离的人包含赏金携带者时才建立唯一奖励事件。部署编号就是后续发放给哪几份档案的依据。
                    if (carrier != null) {
                        Award = new ClashExtraction(MatchId, TokenId, carrier.DeploymentId,
                            escaping.Where(p => !p.IsBot).Select(p => p.DeploymentId).ToArray(), carrier.Id);
                        Bounty = ClashBountyState.Extracted;
                        CarrierId = -1;
                        Deadline = Math.Min(Deadline, now + 90);
                    }
                    foreach (var p in escaping) {
                        p.Life = ClashLife.Extracted;
                        p.Exit = exit;
                        Revision++;
                    }
                    m_ExitStarted[team.Id, exit] = -1;
                }
            }
            if (Players.Values.All(p => p.IsFinished)) {
                Phase = ClashPhase.Finished;
                Revision++;
            }
        }
    }
}