// 把服务端发来的极速赏金状态、参赛者、感知脉冲和目标整理成客户端状态，给计时、排行榜和感知表现读取。镜头和感知按键的本地状态也在这里收口。
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>Server snapshots only. No enemy positions are inferred from replicated ghosts.</summary>
    public sealed class BountyClashClientState : IComponentData {
        public bool ApprovalReceived, Approved, HasSnapshot;
        public string ApprovalReason;
        public ClashSnapshotRpc Snapshot;
        public ClashSenseRpc Sense;
        public double ReceivedAt, SenseReceivedAt;
        public uint NextRequest, PendingRequest;
        public double RequestedAt;
        public readonly Dictionary<int, ClashParticipantRpc> Participants = new();
        private readonly Dictionary<int, ClashParticipantRpc> m_Incoming = new();
        private readonly List<int> m_StaleRows = new();
        public readonly ClashSenseContactRpc[] Contacts = new ClashSenseContactRpc[BountyClashRules.MaxPlayers - 1];
        private readonly double[] m_ContactTimes = new double[BountyClashRules.MaxPlayers - 1];

        private readonly ClashSenseContactRpc[] m_IncomingContacts =
            new ClashSenseContactRpc[BountyClashRules.MaxPlayers - 1];

        private readonly double[] m_IncomingContactTimes = new double[BountyClashRules.MaxPlayers - 1];
        private uint m_IncomingContactSequence, m_BoostStoppedThrough, m_ViewStoppedThrough;
        private bool m_ViewRequested, m_BoostRequested;

        private uint ObservedSenseSequence =>
            System.Math.Max(Snapshot.Sequence, System.Math.Max(Sense.Sequence, m_IncomingContactSequence));

        private bool CarrierAlive =>
            Approved && HasSnapshot && Snapshot.Connected && Snapshot.IsCarrier && Snapshot.Life == ClashLife.Alive &&
            Snapshot.Phase == ClashPhase.Active;

        public ClashResult LastFailure;
        public double FailureAt;

        // 比赛重开会换轮次。排行榜行和状态快照分别到达，所以只合并属于同一轮和同一序号的数据。
        public void Receive(ClashSnapshotRpc value, double now) {
            // 先确认比赛轮次，再看比赛 ID 和快照序号。旧轮次的计时、角色状态或准备结果不能覆盖新局。
            if (HasSnapshot && value.Epoch < Snapshot.Epoch) return;
            if (HasSnapshot && value.Epoch == Snapshot.Epoch &&
                (!value.MatchId.Equals(Snapshot.MatchId) || value.Sequence <= Snapshot.Sequence))
                return;
            bool changed = !HasSnapshot || value.Epoch != Snapshot.Epoch || !value.MatchId.Equals(Snapshot.MatchId);
            Snapshot = value;
            ReceivedAt = now;
            HasSnapshot = true;
            // 切局时重置还没回执的点按、感知状态和追踪目标。那些编号都是旧比赛的，带过来会让新局看起来已经操作过。
            if (changed) {
                PendingRequest = 0;
                NextRequest = value.AcknowledgedRequest;
                Sense = default;
                m_IncomingContactSequence = m_BoostStoppedThrough = m_ViewStoppedThrough = 0;
                m_ViewRequested = m_BoostRequested = false;
                ClearContacts();
                LastFailure = ClashResult.Ok;
            }
            if (value.Result != ClashResult.Ok) {
                LastFailure = value.Result;
                FailureAt = now;
            }
            if (!value.Connected || value.Life != ClashLife.Alive || value.Phase != ClashPhase.Active) {
                StopView();
            } else if (!value.IsCarrier) {
                StopBoost();
                m_ViewStoppedThrough = System.Math.Max(m_ViewStoppedThrough, ObservedSenseSequence);
                Sense.Presence = false;
            } else if (value.SenseRemaining <= 0) { StopBoost(); }
            // 服务端快照带已处理的请求号。它追上正在等待的操作时才解锁按钮；长时间无回执也会超时释放。
            if (value.AcknowledgedRequest >= PendingRequest) PendingRequest = 0;
            Participants.Clear();
            // 排行榜的行先进入暂存字典。直到状态头收到同一个比赛、轮次和序号，才把它们放进要显示的名单。
            foreach (var row in m_Incoming.Values)
                if (row.Epoch == value.Epoch && row.MatchId.Equals(value.MatchId) && row.Sequence == value.Sequence)
                    Participants[row.ParticipantId] = row;
            m_StaleRows.Clear();
            foreach (var pair in m_Incoming)
                if (pair.Value.Epoch < value.Epoch || (pair.Value.Epoch == value.Epoch &&
                                                       (!pair.Value.MatchId.Equals(value.MatchId) ||
                                                        pair.Value.Sequence < value.Sequence)))
                    m_StaleRows.Add(pair.Key);
            foreach (int id in m_StaleRows) m_Incoming.Remove(id);
            if (PendingRequest != 0 && now - RequestedAt > 5) PendingRequest = 0;
        }

        public void Receive(ClashParticipantRpc row) {
            if (row.ParticipantId < 1 || (HasSnapshot && (row.Epoch < Snapshot.Epoch || (row.Epoch == Snapshot.Epoch &&
                    (!row.MatchId.Equals(Snapshot.MatchId) || row.Sequence < Snapshot.Sequence)))))
                return;
            // 暂存区也有玩家数量上限。先淘汰较早的行，再决定是否能加入这张新榜，防止内存积累。
            if (m_Incoming.Count >= BountyClashRules.MaxPlayers && !m_Incoming.ContainsKey(row.ParticipantId)) {
                m_StaleRows.Clear();
                foreach (var pair in m_Incoming)
                    if (pair.Value.Epoch < row.Epoch ||
                        (pair.Value.Epoch == row.Epoch && pair.Value.Sequence < row.Sequence))
                        m_StaleRows.Add(pair.Key);
                foreach (int id in m_StaleRows) m_Incoming.Remove(id);
                if (m_Incoming.Count >= BountyClashRules.MaxPlayers) return;
            }
            if (m_Incoming.TryGetValue(row.ParticipantId, out var old) &&
                (row.Epoch < old.Epoch || (row.Epoch == old.Epoch && row.Sequence < old.Sequence)))
                return;
            m_Incoming[row.ParticipantId] = row;
            if (HasSnapshot && row.MatchId.Equals(Snapshot.MatchId) && row.Epoch == Snapshot.Epoch &&
                row.Sequence == Snapshot.Sequence)
                Participants[row.ParticipantId] = row;
        }

        // 松开感知或失去资格后，先清本地显示，再用序号挡住迟到的旧感知消息，避免松手后闪回上一批目标。
        public void Receive(ClashSenseRpc value, double now) {
            if (!Approved || !HasSnapshot || !value.MatchId.Equals(Snapshot.MatchId) || value.Epoch != Snapshot.Epoch ||
                value.Sequence <= Sense.Sequence || value.Sequence < Snapshot.Sequence)
                return;
            Sense = value;
            SenseReceivedAt = now;
            ClearVisibleContacts();
            // 仅开启查看时可有基础感知提示，但显示加强目标还要求玩家活着、携带赏金且本地仍按住加强。
            bool presenceAllowed = m_ViewRequested && CarrierAlive && value.Sequence > m_ViewStoppedThrough;
            if (!presenceAllowed) {
                Sense.Presence = false;
                m_ViewStoppedThrough = System.Math.Max(m_ViewStoppedThrough, value.Sequence);
            }
            bool boostAllowed = presenceAllowed && m_BoostRequested && Snapshot.SenseRemaining > 0 && value.Active &&
                                value.Remaining > 0 && value.Sequence > m_BoostStoppedThrough &&
                                value.ContactCount >= 0 && value.ContactCount <= Contacts.Length;
            if (!boostAllowed) {
                m_BoostStoppedThrough = System.Math.Max(m_BoostStoppedThrough, value.Sequence);
                Sense.Active = false;
                Sense.SectorMask = 0;
                Sense.ContactCount = 0;
                if (m_IncomingContactSequence <= value.Sequence) ClearIncomingContacts();
                return;
            }
            // 目标消息先到的话存起来，等匹配的感知头数据确认目标数再显示。
            if (m_IncomingContactSequence == value.Sequence)
                for (int i = 0; i < Contacts.Length; i++)
                    if (m_IncomingContacts[i].Count == value.ContactCount) {
                        Contacts[i] = m_IncomingContacts[i];
                        m_ContactTimes[i] = m_IncomingContactTimes[i];
                    }
        }

        public void Receive(ClashSenseContactRpc value, double now) {
            if (!Approved || !HasSnapshot || value.Epoch != Snapshot.Epoch || !value.MatchId.Equals(Snapshot.MatchId) ||
                value.Sequence < Snapshot.Sequence || value.Sequence < Sense.Sequence || value.Count < 1 ||
                value.Count > Contacts.Length || value.Index < 0 || value.Index >= value.Count ||
                !Mathematics.math.all(Mathematics.math.isfinite(value.Position)) ||
                !float.IsFinite(value.UncertaintyRadius) || value.UncertaintyRadius <= 0 ||
                !float.IsFinite(value.ValidFor) || value.ValidFor <= 0)
                return;
            if (!m_ViewRequested || !m_BoostRequested || !CarrierAlive || Snapshot.SenseRemaining <= 0) {
                m_BoostStoppedThrough = System.Math.Max(m_BoostStoppedThrough, value.Sequence);
                return;
            }
            if (value.Sequence <= m_BoostStoppedThrough) return;
            // 碰到更新一批目标时先清旧批次。之后填入它自己的槽位，只有目标序号、感知头和声明数一致才可见。
            if (value.Sequence > m_IncomingContactSequence) {
                ClearIncomingContacts();
                m_IncomingContactSequence = value.Sequence;
            }
            if (value.Sequence != m_IncomingContactSequence) return;
            m_IncomingContacts[value.Index] = value;
            m_IncomingContactTimes[value.Index] = now;
            if (value.Sequence == Sense.Sequence && Sense.Active && Sense.ContactCount == value.Count) {
                Contacts[value.Index] = value;
                m_ContactTimes[value.Index] = now;
            }
        }

        // Repeated held-input frames do not move stop barriers. Only a stop transition
        // invalidates output; headers arriving while stopped advance their own barrier.
        public void SetSenseInput(bool view, bool boost) {
            // 加强感知必须依附在感知视图里。松开按键要清本轮结果，持续按住则不反复改允许通过的消息序号。
            boost &= view;
            if (!view) {
                if (m_ViewRequested || m_BoostRequested) StopView();
                return;
            }
            m_ViewRequested = true;
            if (!boost && m_BoostRequested) StopBoost();
            m_BoostRequested = boost;
        }

        public void StopBoost() {
            m_BoostRequested = false;
            m_BoostStoppedThrough = System.Math.Max(m_BoostStoppedThrough, ObservedSenseSequence);
            Sense.Active = false;
            Sense.SectorMask = 0;
            Sense.ContactCount = 0;
            ClearContacts();
            // Base-view Presence remains valid until its own header expires or view stops.
        }

        // 退出整个视图会连加强状态一起清掉。松开加强只清目标时，基础感知仍可继续到自己的有效期。
        public void StopView() {
            StopBoost();
            m_ViewRequested = false;
            m_ViewStoppedThrough = System.Math.Max(m_ViewStoppedThrough, ObservedSenseSequence);
            Sense.Presence = false;
        }

        private void ClearVisibleContacts() {
            System.Array.Clear(Contacts, 0, Contacts.Length);
            System.Array.Clear(m_ContactTimes, 0, m_ContactTimes.Length);
        }

        private void ClearIncomingContacts() {
            System.Array.Clear(m_IncomingContacts, 0, m_IncomingContacts.Length);
            System.Array.Clear(m_IncomingContactTimes, 0, m_IncomingContactTimes.Length);
        }

        // Packet-batch cleanup only. Never reset the sequence high-water mark here.
        public void ClearContacts() {
            ClearVisibleContacts();
            ClearIncomingContacts();
        }

        // 目标是短暂线索，超时就不再显示。读取时还核对它确实属于当前正在展示的感知批次。
        public bool ContactVisible(int index, double now) {
            return SenseVisible(now) && Contacts[index].Sequence == Sense.Sequence &&
                   Contacts[index].Count == Sense.ContactCount && Contacts[index].ValidFor > 0 &&
                   now - m_ContactTimes[index] < Mathf.Min(.35f, Contacts[index].ValidFor);
        }

        public bool SenseVisible(double now) {
            return m_ViewRequested && m_BoostRequested && CarrierAlive && Snapshot.SenseRemaining > 0 &&
                   Sense.Sequence > m_BoostStoppedThrough && Sense.Active && Sense.Remaining > 0 &&
                   now - SenseReceivedAt < .7;
        }

        public float Remaining(double now) { return Mathf.Max(0, Snapshot.Remaining - (float)(now - ReceivedAt)); }
        public bool ActionPending(double now) { return PendingRequest != 0 && now - RequestedAt < 5; }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class BountyClashClientSystem : SystemBase {
        private Entity m_State;

        protected override void OnCreate() {
            m_State = EntityManager.CreateEntity();
            EntityManager.AddComponentObject(m_State, new BountyClashClientState());
        }

        protected override void OnUpdate() {
            var data = EntityManager.GetComponentObject<BountyClashClientState>(m_State);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<ClashJoinAcceptedRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                var value = rpc.ValueRO;
                data.ApprovalReceived = true;
                // 服务端说接受还不够，回来的玩法和规则版本必须也是这次想进的模式，加载才能往下继续。
                data.Approved = value.Accepted && value.Mode == GameSettings.Instance.RequestedMode &&
                                value.RulesVersion == GameSettings.Instance.RequestedRulesVersion;
                data.ApprovalReason = value.Reason.Length > 0
                    ? value.Reason.ToString()
                    : "Server mode or rules version does not match your selection.";
                ConnectionSettings.Instance.ModeConfirmed = data.Approved;
                ConnectionSettings.Instance.ApprovedMode = value.Mode;
                ConnectionSettings.Instance.ApprovedRulesVersion = value.RulesVersion;
                if (!data.Approved) { ConnectionSettings.Instance.ConnectionError = data.ApprovalReason; } else {
                    using var query = EntityManager.CreateEntityQuery(typeof(RaidModeState));
                    if (!query.IsEmptyIgnoreFilter)
                        EntityManager.SetComponentData(query.GetSingletonEntity(),
                            new RaidModeState { Mode = value.Mode, RulesVersion = value.RulesVersion });
                }
                ecb.DestroyEntity(entity);
            }
            // 所有网络状态用同一帧现实时间记录到达时刻。HUD 可以据此平滑倒计时和控制线索过期。
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<ClashParticipantRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                data.Receive(rpc.ValueRO);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<ClashSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                data.Receive(rpc.ValueRO, now);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<ClashSenseRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                data.Receive(rpc.ValueRO, now);
                ecb.DestroyEntity(entity);
            }
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<ClashSenseContactRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                received.ValueRW.Consume();
                data.Receive(rpc.ValueRO, now);
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
    }
}