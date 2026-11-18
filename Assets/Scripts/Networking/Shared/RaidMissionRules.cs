// 地图任务的规则模型。声明前置目标、处理时限和产物，追踪参与者的操作。ServerGameSystem.Missions 负责把地图、距离和角色状态接进来。
#nullable disable
using System;
using System.Collections.Generic;

namespace Unity.MP_FPS.Missions {
    public enum MissionObjectiveKind : byte {
        Interact,
        Scan,
        Process,
        Configure
    }

    public enum MissionPhase : byte {
        Offered,
        Active,
        OutputReady,
        Resolved,
        Expired
    }

    public enum MissionActionResult : byte {
        Started,
        Running,
        Completed,
        AlreadyCompleted,
        Busy,
        Prerequisite,
        Invalid,
        Expired,
        Stale
    }

    [Serializable]
    public sealed class MissionObjectiveDefinition {
        public string Id, Anchor, Name;
        public MissionObjectiveKind Kind;
        public uint Prerequisites;
        public float Seconds;
        public bool RecordBeforePrerequisites;
        public string[] Choices;
        public int ExpectedChoice;

        public MissionObjectiveDefinition Copy() {
            var n = (MissionObjectiveDefinition)MemberwiseClone();
            n.Choices = Choices == null ? null : (string[])Choices.Clone();
            return n;
        }
    }

    [Serializable]
    public sealed class MissionDefinition {
        public string Id, Name, ArtifactCode;
        public int Version = 1, Weight = 1, OutputCount = 1;
        public float OfferSeconds = 120, ActiveSeconds = 600, MinAudienceSeconds = 420, CooldownSeconds = 180;
        public MissionObjectiveDefinition[] Objectives;

        // 任务定义由节点、前置位、时限和挂点组成。非法下标或不可能满足的组合需在启用前拦住。
        public string Validate() {
            if (string.IsNullOrWhiteSpace(Id) || Id.Length > 48 || string.IsNullOrWhiteSpace(Name) || Version < 1 ||
                Weight < 1 || Weight > 100000 || OutputCount < 1 || OutputCount > 8 ||
                string.IsNullOrWhiteSpace(ArtifactCode) || !Finite(OfferSeconds, 1, 3600) ||
                !Finite(ActiveSeconds, 1, 3600) || !Finite(MinAudienceSeconds, 1, 3600) ||
                !Finite(CooldownSeconds, 0, 3600) || Objectives == null || Objectives.Length < 1 ||
                Objectives.Length > 24)
                return "mission_definition";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            uint all = (1u << Objectives.Length) - 1;
            for (int i = 0; i < Objectives.Length; i++) {
                var n = Objectives[i];
                if (n == null || string.IsNullOrWhiteSpace(n.Id) || n.Id.Length > 48 || !ids.Add(n.Id) ||
                    string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Anchor) || n.Anchor.Length > 48 ||
                    !Enum.IsDefined(typeof(MissionObjectiveKind), n.Kind) || !Finite(n.Seconds, .1f, 600) ||
                    (n.Prerequisites & ~all) != 0 || (n.Prerequisites & (1u << i)) != 0)
                    return "mission_objective";
                if (n.Kind == MissionObjectiveKind.Configure && (n.Choices == null || n.Choices.Length < 2 ||
                                                                 n.Choices.Length > 6 || n.ExpectedChoice < 1 ||
                                                                 n.ExpectedChoice > n.Choices.Length ||
                                                                 Array.Exists(n.Choices, string.IsNullOrWhiteSpace)))
                    return "mission_configuration";
            }
            // 按依赖顺序遍历来拒绝环形引用，无需执行内容中配置的代码。
            uint visited = 0;
            for (int pass = 0; pass < Objectives.Length; pass++)
            for (int i = 0; i < Objectives.Length; i++)
                if ((Objectives[i].Prerequisites & visited) == Objectives[i].Prerequisites)
                    visited |= 1u << i;
            return visited == all ? null : "mission_cycle";
        }

        public MissionDefinition Copy() {
            var d = (MissionDefinition)MemberwiseClone();
            d.Objectives = Array.ConvertAll(Objectives, n => n.Copy());
            return d;
        }

        internal static bool Finite(float value, float min, float max) {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }
    }

    public static class MissionCatalog {
        private static MissionObjectiveDefinition Node(
            string id,
            string anchor,
            string name,
            float seconds,
            uint prerequisites = 0,
            MissionObjectiveKind kind = MissionObjectiveKind.Interact,
            bool early = false) {
            return new MissionObjectiveDefinition {
                Id = id, Anchor = anchor, Name = name, Seconds = seconds,
                Prerequisites = prerequisites, Kind = kind, RecordBeforePrerequisites = early
            };
        }

        // 场景配置可替换模板，开始运行时冻结一份副本。
        public static MissionDefinition Mooncore() {
            return new MissionDefinition {
                Id = "mc00_mooncore", Name = "封存样本", ArtifactCode = "mooncore_fragment", Weight = 45, OutputCount = 2,
                Objectives = new[] {
                    new MissionObjectiveDefinition {
                        Id = "power_a", Anchor = "power_a", Name = "隔离烧损的供电 A 线路",
                        Kind = MissionObjectiveKind.Configure,
                        Seconds = 1, ExpectedChoice = 1, Choices = new[] { "断开烧损支路，保留备用母线", "重新合上烧损支路", "切断整座设施电源" }
                    },
                    Node("power_b", "power_b", "恢复维修配电 B", 4),
                    Node("start", "sampler", "启动月核切割", 3, 3),
                    Node("cutting", "sampler", "等待切割完成", 45, 4, MissionObjectiveKind.Process)
                }
            };
        }

        public static MissionDefinition Archive() {
            return new MissionDefinition {
                Id = "mc00_archive", Name = "事故之前", ArtifactCode = "mc00_archive_drive", Weight = 35,
                MinAudienceSeconds = 300, ActiveSeconds = 420,
                Objectives = new[] {
                    Node("self_check", "archive", "运行档案设备自检", 3),
                    Node("tag_a", "power_a", "读取隔离柜的 18:42 校验标签", 4, 1, MissionObjectiveKind.Scan, true),
                    Node("tag_b", "power_b", "读取母线柜的 18:47 校验标签", 4, 1, MissionObjectiveKind.Scan, true),
                    Node("download_start", "archive", "确认档案下载", 1, 7),
                    Node("download", "archive", "等待档案下载", 20, 8, MissionObjectiveKind.Process)
                }
            };
        }
    }

    // 机器运行声只提供位置线索，不泄露操作者身份。
    public static class MissionMachineSounds {
        public const float CutterRange = 40, ArchiveRange = 25;

        public static bool WithinRange(float squaredDistance, float range, bool obstructed) {
            if (!float.IsFinite(squaredDistance) || squaredDistance < 0 || !float.IsFinite(range) || range <= 0)
                return false;
            float limit = obstructed ? range * .55f : range;
            return squaredDistance <= limit * limit;
        }

        public static bool TryActiveSource(MissionOperation operation, out string anchor, out float range) {
            anchor = null;
            range = 0;
            if (operation == null) return false;
            if (operation.DefinitionId == "mc00_mooncore" && operation.IsProcessing("cutting")) {
                anchor = "sampler";
                range = CutterRange;
                return true;
            }
            if (operation.DefinitionId == "mc00_archive" && operation.IsProcessing("download")) {
                anchor = "archive";
                range = ArchiveRange;
                return true;
            }
            return false;
        }
    }

    // 由一个服务端 World 掌管。接取、发现与界面追踪状态由各自系统维护。
    public sealed class MissionOperation {
        private sealed class Interaction {
            public string Actor;
            public uint Request;
            public int Node;
            public double Started, LeaseUntil;
        }

        private readonly Dictionary<string, Interaction> m_Interactions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, uint> m_Requests = new(StringComparer.Ordinal);
        private readonly Dictionary<int, double> m_Processes = new();
        private readonly List<string> m_ExpiredActors = new();
        private readonly List<int> m_CompletedProcesses = new();
        private readonly MissionDefinition m_Definition;
        private readonly uint m_All;
        private double m_LastTime;
        private bool m_Restored;
        public readonly string WorldSessionId, Id;
        public readonly int Epoch;
        public MissionPhase Phase { get; private set; } = MissionPhase.Offered;
        public uint CompletedMask { get; private set; }
        public uint Revision { get; private set; } = 1;
        public double OfferedAt { get; }
        public double Deadline { get; private set; }
        public string DefinitionId => m_Definition.Id;
        public MissionDefinition Definition => m_Definition.Copy();
        public int ObjectiveCount => m_Definition.Objectives.Length;

        public MissionObjectiveDefinition Objective(int index) {
            return index >= 0 && index < ObjectiveCount ? m_Definition.Objectives[index].Copy() : null;
        }

        public bool IsProcessing(string objectiveId) {
            if (Phase != MissionPhase.Active || string.IsNullOrEmpty(objectiveId)) return false;
            foreach (var process in m_Processes)
                if (m_Definition.Objectives[process.Key].Id == objectiveId)
                    return true;
            return false;
        }

        // 一场地图实例有自己的任务操作号和轮次，便于旧 UI 请求只影响原实例。开始时复制定义避免外部改时间或节点。
        public MissionOperation(string worldSessionId, string id, int epoch, MissionDefinition definition, double now) {
            if (!Guid.TryParse(worldSessionId, out _) || !Guid.TryParse(id, out _) || epoch < 1 || definition == null ||
                definition.Validate() != null || !ValidTime(now))
                throw new ArgumentException("Invalid mission instance.");
            WorldSessionId = worldSessionId;
            Id = id;
            Epoch = epoch;
            m_Definition = definition.Copy();
            m_All = (1u << ObjectiveCount) - 1;
            OfferedAt = m_LastTime = now;
            Deadline = now + m_Definition.OfferSeconds;
        }

        // 这个任务模型只认服务器传来的角色、当前轮次和时间。距离、视线及人物能否操作，由 ServerGameSystem.Missions 在调用前确认。
        // 调用者提供服务器已验证的玩家和出战身份及服务端时间。
        // 距离、可见、生命和所有权仍由现有服务端适配层核对。
        public MissionActionResult Begin(string actor, int epoch, int node, uint request, double now) {
            if (!ValidTime(now) || now < m_LastTime || string.IsNullOrWhiteSpace(actor) || actor.Length > 128 ||
                epoch != Epoch || node < 0 || node >= ObjectiveCount || request == 0)
                return MissionActionResult.Invalid;
            Tick(now);
            if (Phase == MissionPhase.Expired || Phase == MissionPhase.Resolved) return MissionActionResult.Expired;
            // 同一玩家更早的开始请求无效。已在相同节点跑那一个请求时可返回继续，避免重发让读条从头开始。
            if (m_Requests.TryGetValue(actor, out uint last) && request <= last)
                return m_Interactions.TryGetValue(actor, out var old) && old.Request == request && old.Node == node
                    ? MissionActionResult.Running
                    : MissionActionResult.Stale;
            if (!m_Requests.ContainsKey(actor) && m_Requests.Count >= 256) return MissionActionResult.Busy;
            m_Requests[actor] = request;
            var objective = m_Definition.Objectives[node];
            if (objective.Kind == MissionObjectiveKind.Process || objective.Kind == MissionObjectiveKind.Configure)
                return MissionActionResult.Invalid;
            if (Completed(node)) return MissionActionResult.AlreadyCompleted;
            if (!objective.RecordBeforePrerequisites && !Prerequisites(objective))
                return MissionActionResult.Prerequisite;
            foreach (var interaction in m_Interactions.Values)
                if (interaction.Actor != actor && interaction.Node == node)
                    return MissionActionResult.Busy;
            // 一项手动交互给玩家短期租约。同一节点当前被另一人操作时不可抢占，房主接受请求才启用整个任务。
            m_Interactions[actor] = new Interaction
                { Actor = actor, Request = request, Node = node, Started = now, LeaseUntil = now + 2 };
            if (Phase == MissionPhase.Offered) {
                Phase = MissionPhase.Active;
                Deadline = now + m_Definition.ActiveSeconds;
                Revision++;
            }
            return MissionActionResult.Started;
        }

        // 客户端持续按住时会不断续租当前交互。服务端若收不到后续请求，Tick 会让旧操作到期，避免玩家离开后读条自己跑完。
        public MissionActionResult Renew(string actor, uint request, double now) {
            if (!ValidTime(now) || now < m_LastTime) return MissionActionResult.Invalid;
            Tick(now);
            if (Phase == MissionPhase.Expired || Phase == MissionPhase.Resolved) return MissionActionResult.Expired;
            if (actor == null || !m_Interactions.TryGetValue(actor, out var action) || action.Request != request)
                return MissionActionResult.Stale;
            action.LeaseUntil = now + 2;
            if (now - action.Started < m_Definition.Objectives[action.Node].Seconds) return MissionActionResult.Running;
            m_Interactions.Remove(actor);
            Complete(action.Node);
            StartProcesses(now);
            return MissionActionResult.Completed;
        }

        // 有选择值的节点由单独配置请求完成。需要校验值可选、阶段和前置节点，再让后续机器流程启动。
        public MissionActionResult Configure(string actor, int epoch, int node, uint request, int choice, double now) {
            if (!ValidTime(now) || now < m_LastTime || string.IsNullOrWhiteSpace(actor) || actor.Length > 128 ||
                epoch != Epoch || node < 0 || node >= ObjectiveCount || request == 0)
                return MissionActionResult.Invalid;
            Tick(now);
            if (Phase == MissionPhase.Expired || Phase == MissionPhase.Resolved) return MissionActionResult.Expired;
            if (m_Requests.TryGetValue(actor, out uint old) && request <= old) return MissionActionResult.Stale;
            if (!m_Requests.ContainsKey(actor) && m_Requests.Count >= 256) return MissionActionResult.Busy;
            m_Requests[actor] = request;
            var n = m_Definition.Objectives[node];
            if (n.Kind != MissionObjectiveKind.Configure) return MissionActionResult.Invalid;
            if (Completed(node)) return MissionActionResult.AlreadyCompleted;
            if (!Prerequisites(n)) return MissionActionResult.Prerequisite;
            if (choice != n.ExpectedChoice) return MissionActionResult.Invalid;
            if (Phase == MissionPhase.Offered) {
                Phase = MissionPhase.Active;
                Deadline = now + m_Definition.ActiveSeconds;
                Revision++;
            }
            Complete(node);
            StartProcesses(now);
            return MissionActionResult.Completed;
        }

        public void Cancel(string actor) {
            if (actor != null) m_Interactions.Remove(actor);
        }

        public bool Completed(int node) {
            return node >= 0 && node < ObjectiveCount && (CompletedMask & (1u << node)) != 0;
        }

        // 从任务已完成位、手动交互的开始时间或机器截止时间推算进度，界面随动不负责完成判定。
        public float Progress(int node, double now) {
            if (node < 0 || node >= ObjectiveCount || !ValidTime(now)) return 0;
            if (Completed(node)) return 1;
            if (m_Processes.TryGetValue(node, out double start))
                return Fraction(now - start, m_Definition.Objectives[node].Seconds);
            foreach (var action in m_Interactions.Values)
                if (action.Node == node)
                    return Fraction(now - action.Started, m_Definition.Objectives[node].Seconds);
            return 0;
        }

        // 统一清掉过期手动操作并提交已到时间的机器步骤。节点改变可再开启满足前置条件的流程。
        public void Tick(double now) {
            if (!ValidTime(now) || now < m_LastTime) return;
            m_LastTime = now;
            if (Phase == MissionPhase.Expired || Phase == MissionPhase.Resolved) return;
            if (now >= Deadline) {
                Phase = MissionPhase.Expired;
                m_Interactions.Clear();
                m_Processes.Clear();
                Revision++;
                return;
            }
            m_ExpiredActors.Clear();
            foreach (var pair in m_Interactions)
                if (now >= pair.Value.LeaseUntil)
                    m_ExpiredActors.Add(pair.Key);
            foreach (string actor in m_ExpiredActors) m_Interactions.Remove(actor);
            m_CompletedProcesses.Clear();
            foreach (var pair in m_Processes)
                if (now - pair.Value >= m_Definition.Objectives[pair.Key].Seconds)
                    m_CompletedProcesses.Add(pair.Key);
            foreach (int node in m_CompletedProcesses) {
                m_Processes.Remove(node);
                Complete(node);
            }
            StartProcesses(now);
        }

        public bool Resolve() {
            if (Phase != MissionPhase.OutputReady) return false;
            Phase = MissionPhase.Resolved;
            Revision++;
            return true;
        }

        // 已经落盘的设施进度只由本机章节适配层恢复。
        // 只有在实际交互前允许恢复一次，而且不能跳过前置节点。
        // 保留地图内已经有可靠完成记录的步骤，同时检查依赖掩码，恢复并不会凭空产生新产物。
        public bool RestoreCompleted(uint mask, double now) {
            if (m_Restored || Phase != MissionPhase.Offered || CompletedMask != 0 || !ValidTime(now) ||
                now < m_LastTime || (mask & ~m_All) != 0)
                return false;
            for (int i = 0; i < ObjectiveCount; i++)
                if ((mask & (1u << i)) != 0 && (mask & m_Definition.Objectives[i].Prerequisites) !=
                    m_Definition.Objectives[i].Prerequisites)
                    return false;
            m_Restored = true;
            CompletedMask = mask;
            m_LastTime = now;
            Revision++;
            if (mask != 0) {
                Phase = mask == m_All ? MissionPhase.OutputReady : MissionPhase.Active;
                Deadline = now + m_Definition.ActiveSeconds;
            }
            StartProcesses(now);
            return true;
        }

        private void Complete(int node) {
            if (Completed(node)) return;
            CompletedMask |= 1u << node;
            Revision++;
            if (CompletedMask == m_All) {
                Phase = MissionPhase.OutputReady;
                Revision++;
            }
        }

        private bool Prerequisites(MissionObjectiveDefinition n) {
            return (CompletedMask & n.Prerequisites) == n.Prerequisites;
        }

        // 机器节点只有前置全部满足才开始倒计时。人为节点等玩家操作，共用同一个完成掩码。
        private void StartProcesses(double now) {
            if (Phase != MissionPhase.Active) return;
            for (int i = 0; i < ObjectiveCount; i++)
                if (!Completed(i) && m_Definition.Objectives[i].Kind == MissionObjectiveKind.Process &&
                    Prerequisites(m_Definition.Objectives[i]) && !m_Processes.ContainsKey(i))
                    m_Processes.Add(i, now);
        }

        private static bool ValidTime(double now) { return !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0; }

        private static float Fraction(double elapsed, float seconds) {
            return (float)Math.Max(0, Math.Min(1, elapsed / seconds));
        }
    }

    // Unity 和 .NET 运行时都能稳定得到同样抽取结果。选中任务仍须通过适配层的地点和安全判断。
    public sealed class MissionDirector {
        private uint m_Random;
        private readonly Dictionary<string, double> m_LastOffered = new(StringComparer.Ordinal);
        public double NextEvaluation { get; private set; }

        public MissionDirector(uint seed, double now = 0) {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            m_Random = seed == 0 ? 0x9e3779b9u : seed;
            NextEvaluation = now + 50 + Unit() * 60;
        }

        // 挑任务前看受众剩余时间、任务冷却和地图可用定义。满足条件才按随机值选一次，避免刚进图刷很多任务。
        public MissionDefinition TryOffer(
            IReadOnlyList<MissionDefinition> definitions,
            double now,
            float audienceTimeLeft,
            bool regionOccupied,
            Func<MissionDefinition, bool> validCandidate) {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < NextEvaluation || definitions == null ||
                validCandidate == null)
                return null;
            NextEvaluation = now + 30;
            if (regionOccupied || !MissionDefinition.Finite(audienceTimeLeft, 0, 3600)) return null;
            var candidates = new List<MissionDefinition>();
            int weight = 0;
            foreach (var d in definitions)
                if (d != null && d.Validate() == null && audienceTimeLeft >= d.MinAudienceSeconds &&
                    (!m_LastOffered.TryGetValue(d.Id, out double previous) || now - previous >= d.CooldownSeconds) &&
                    validCandidate(d)) {
                    candidates.Add(d);
                    weight = checked(weight + d.Weight);
                }
            if (weight == 0) return null;
            int roll = (int)(Next() % (uint)weight);
            foreach (var d in candidates) {
                if (roll < d.Weight) {
                    m_LastOffered[d.Id] = now;
                    NextEvaluation = now + 90 + Unit() * 60;
                    return d.Copy();
                }
                roll -= d.Weight;
            }
            return null;
        }

        private uint Next() {
            m_Random ^= m_Random << 13;
            m_Random ^= m_Random >> 17;
            m_Random ^= m_Random << 5;
            return m_Random;
        }

        private double Unit() { return Next() / 4294967296.0; }
    }
}