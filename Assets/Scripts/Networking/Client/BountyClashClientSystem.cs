using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Server snapshots only. No enemy positions are inferred from replicated ghosts.</summary>
    public sealed class BountyClashClientState : IComponentData
    {
        public bool ApprovalReceived, Approved, HasSnapshot;
        public string ApprovalReason;
        public ClashSnapshotRpc Snapshot;
        public ClashSenseRpc Sense;
        public double ReceivedAt, SenseReceivedAt;
        public uint NextRequest, PendingRequest;
        public double RequestedAt;
        public readonly Dictionary<int, ClashParticipantRpc> Participants = new Dictionary<int, ClashParticipantRpc>();
        private readonly Dictionary<int, ClashParticipantRpc> m_Incoming = new Dictionary<int, ClashParticipantRpc>();
        private readonly List<int> m_StaleRows = new List<int>();
        public readonly ClashSenseContactRpc[] Contacts = new ClashSenseContactRpc[BountyClashRules.MaxPlayers - 1];
        private readonly double[] m_ContactTimes = new double[BountyClashRules.MaxPlayers - 1];
        private readonly ClashSenseContactRpc[] m_IncomingContacts = new ClashSenseContactRpc[BountyClashRules.MaxPlayers - 1];
        private readonly double[] m_IncomingContactTimes = new double[BountyClashRules.MaxPlayers - 1];
        private uint m_IncomingContactSequence;
        public ClashResult LastFailure;
        public double FailureAt;

        public void Receive(ClashSnapshotRpc value, double now)
        {
            if (HasSnapshot && value.Epoch < Snapshot.Epoch) return;
            if (HasSnapshot && value.Epoch == Snapshot.Epoch && (!value.MatchId.Equals(Snapshot.MatchId) || value.Sequence <= Snapshot.Sequence)) return;
            bool changed = !HasSnapshot || value.Epoch != Snapshot.Epoch || !value.MatchId.Equals(Snapshot.MatchId);
            Snapshot = value; ReceivedAt = now; HasSnapshot = true;
            if (changed) { PendingRequest = 0; NextRequest = value.AcknowledgedRequest; Sense = default; ClearContacts(); LastFailure = ClashResult.Ok; }
            if (value.Result != ClashResult.Ok) { LastFailure = value.Result; FailureAt = now; }
            if (!value.IsCarrier || value.Life != ClashLife.Alive || value.Phase != ClashPhase.Active) ClearContacts();
            if (value.AcknowledgedRequest >= PendingRequest) PendingRequest = 0;
            Participants.Clear();
            foreach (var row in m_Incoming.Values)
                if (row.Epoch == value.Epoch && row.MatchId.Equals(value.MatchId) && row.Sequence == value.Sequence)
                    Participants[row.ParticipantId] = row;
            m_StaleRows.Clear();
            foreach (var pair in m_Incoming)
                if (pair.Value.Epoch < value.Epoch || pair.Value.Epoch == value.Epoch &&
                    (!pair.Value.MatchId.Equals(value.MatchId) || pair.Value.Sequence < value.Sequence)) m_StaleRows.Add(pair.Key);
            foreach (int id in m_StaleRows) m_Incoming.Remove(id);
            if (PendingRequest != 0 && now - RequestedAt > 5) PendingRequest = 0;
        }
        public void Receive(ClashParticipantRpc row)
        {
            if (row.ParticipantId < 1 || HasSnapshot && (row.Epoch < Snapshot.Epoch || row.Epoch == Snapshot.Epoch &&
                (!row.MatchId.Equals(Snapshot.MatchId) || row.Sequence < Snapshot.Sequence))) return;
            if (m_Incoming.Count >= BountyClashRules.MaxPlayers && !m_Incoming.ContainsKey(row.ParticipantId))
            {
                m_StaleRows.Clear();
                foreach (var pair in m_Incoming)
                    if (pair.Value.Epoch < row.Epoch || pair.Value.Epoch == row.Epoch && pair.Value.Sequence < row.Sequence) m_StaleRows.Add(pair.Key);
                foreach (int id in m_StaleRows) m_Incoming.Remove(id);
                if (m_Incoming.Count >= BountyClashRules.MaxPlayers) return;
            }
            if (m_Incoming.TryGetValue(row.ParticipantId, out var old) && (row.Epoch < old.Epoch || row.Epoch == old.Epoch && row.Sequence < old.Sequence)) return;
            m_Incoming[row.ParticipantId] = row;
            if (HasSnapshot && row.MatchId.Equals(Snapshot.MatchId) && row.Epoch == Snapshot.Epoch && row.Sequence == Snapshot.Sequence)
                Participants[row.ParticipantId] = row;
        }
        public void Receive(ClashSenseRpc value, double now)
        {
            if (!Approved || !HasSnapshot || !value.MatchId.Equals(Snapshot.MatchId) || value.Epoch != Snapshot.Epoch || value.Sequence < Sense.Sequence || value.Sequence < Snapshot.Sequence) return;
            Sense = value; SenseReceivedAt = now;
            System.Array.Clear(Contacts, 0, Contacts.Length);
            if (!value.Active || value.ContactCount <= 0 || !Snapshot.IsCarrier || Snapshot.Life != ClashLife.Alive) { ClearContacts(); return; }
            if (m_IncomingContactSequence == value.Sequence)
                for (int i = 0; i < Contacts.Length; i++)
                    if (m_IncomingContacts[i].Count == value.ContactCount) { Contacts[i] = m_IncomingContacts[i]; m_ContactTimes[i] = m_IncomingContactTimes[i]; }
        }
        public void Receive(ClashSenseContactRpc value, double now)
        {
            if (!Approved || !HasSnapshot || !Snapshot.IsCarrier || Snapshot.Life != ClashLife.Alive ||
                value.Epoch != Snapshot.Epoch || !value.MatchId.Equals(Snapshot.MatchId) || value.Sequence < Snapshot.Sequence || value.Sequence < Sense.Sequence ||
                value.Count < 1 || value.Count > Contacts.Length || value.Index < 0 || value.Index >= value.Count ||
                !Unity.Mathematics.math.all(Unity.Mathematics.math.isfinite(value.Position)) || !float.IsFinite(value.UncertaintyRadius) ||
                value.UncertaintyRadius <= 0 || !float.IsFinite(value.ValidFor) || value.ValidFor <= 0) return;
            if (value.Sequence > m_IncomingContactSequence)
            { System.Array.Clear(m_IncomingContacts, 0, m_IncomingContacts.Length); m_IncomingContactSequence = value.Sequence; }
            if (value.Sequence != m_IncomingContactSequence) return;
            m_IncomingContacts[value.Index] = value; m_IncomingContactTimes[value.Index] = now;
            if (value.Sequence == Sense.Sequence && Sense.Active && Sense.ContactCount == value.Count)
            { Contacts[value.Index] = value; m_ContactTimes[value.Index] = now; }
        }
        public void ClearContacts()
        { System.Array.Clear(Contacts, 0, Contacts.Length); System.Array.Clear(m_IncomingContacts, 0, m_IncomingContacts.Length); m_IncomingContactSequence = 0; }
        public bool ContactVisible(int index, double now) => SenseVisible(now) && Contacts[index].Sequence == Sense.Sequence &&
            Contacts[index].Count == Sense.ContactCount && Contacts[index].ValidFor > 0 && now - m_ContactTimes[index] < Mathf.Min(.35f, Contacts[index].ValidFor);
        public bool SenseVisible(double now) => Approved && Snapshot.IsCarrier && Snapshot.Life == ClashLife.Alive && Snapshot.Phase == ClashPhase.Active && Sense.Active && Sense.Remaining > 0 && now - SenseReceivedAt < .7;
        public float Remaining(double now) => Mathf.Max(0, Snapshot.Remaining - (float)(now - ReceivedAt));
        public bool ActionPending(double now) => PendingRequest != 0 && now - RequestedAt < 5;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class BountyClashClientSystem : SystemBase
    {
        private Entity m_State;
        protected override void OnCreate()
        {
            m_State = EntityManager.CreateEntity(); EntityManager.AddComponentObject(m_State, new BountyClashClientState());
        }
        protected override void OnUpdate()
        {
            var data = EntityManager.GetComponentObject<BountyClashClientState>(m_State);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<ClashJoinAcceptedRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume(); var value = rpc.ValueRO;
                data.ApprovalReceived = true;
                data.Approved = value.Accepted && value.Mode == GameSettings.Instance.RequestedMode &&
                    value.RulesVersion == GameSettings.Instance.RequestedRulesVersion;
                data.ApprovalReason = value.Reason.Length > 0 ? value.Reason.ToString() : "Server mode or rules version does not match your selection.";
                ConnectionSettings.Instance.ModeConfirmed = data.Approved;
                ConnectionSettings.Instance.ApprovedMode = value.Mode;
                ConnectionSettings.Instance.ApprovedRulesVersion = value.RulesVersion;
                if (!data.Approved) ConnectionSettings.Instance.ConnectionError = data.ApprovalReason;
                else
                {
                    using var query = EntityManager.CreateEntityQuery(typeof(RaidModeState));
                    if (!query.IsEmptyIgnoreFilter) EntityManager.SetComponentData(query.GetSingletonEntity(), new RaidModeState { Mode = value.Mode, RulesVersion = value.RulesVersion });
                }
                ecb.DestroyEntity(entity);
            }
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<ClashParticipantRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            { received.ValueRW.Consume(); data.Receive(rpc.ValueRO); ecb.DestroyEntity(entity); }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<ClashSnapshotRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            { received.ValueRW.Consume(); data.Receive(rpc.ValueRO, now); ecb.DestroyEntity(entity); }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<ClashSenseRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            { received.ValueRW.Consume(); data.Receive(rpc.ValueRO, now); ecb.DestroyEntity(entity); }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<ClashSenseContactRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            { received.ValueRW.Consume(); data.Receive(rpc.ValueRO, now); ecb.DestroyEntity(entity); }
            ecb.Playback(EntityManager);
        }
    }
}
