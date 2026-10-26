using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ServerGameSystem))]
    public partial class RaidChatServerSystem : SystemBase
    {
        private struct Member
        {
            public Entity Connection;
            public int Id, Team;
            public string Group;
            public FixedString64Bytes Name;
            public bool Dead;
        }
        private readonly Dictionary<Entity, Member> m_Members = new Dictionary<Entity, Member>();
        private readonly Dictionary<Entity, RaidChatBudget> m_Budgets = new Dictionary<Entity, RaidChatBudget>();
        private readonly Dictionary<Entity, bool> m_Announced = new Dictionary<Entity, bool>();
        private readonly List<Entity> m_Stale = new List<Entity>();
        private EntityQuery m_Offline, m_Clash, m_Persistence;
        private ulong m_Session;
        private string m_Match;
        private int m_Epoch;
        private uint m_Sequence, m_AnnouncementSequence;
        protected override void OnCreate()
        {
            m_Offline = GetEntityQuery(typeof(OfflineRaidMode));
            m_Clash = GetEntityQuery(typeof(BountyClashWorld));
            m_Persistence = GetEntityQuery(typeof(RaidPersistenceContext));
            m_Session = NewSession();
        }
        private static ulong NewSession() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) | 1UL;
        protected override void OnUpdate()
        {
            var clash = m_Clash.IsEmptyIgnoreFilter ? null : EntityManager.GetComponentObject<BountyClashWorld>(m_Clash.GetSingletonEntity()).Match;
            bool enabled = m_Offline.IsEmptyIgnoreFilter && (clash == null || !clash.IsSolo);
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            if (!enabled && m_Announced.Count > 0)
            {
                foreach (var connection in m_Announced.Keys)
                    if (EntityManager.Exists(connection) && EntityManager.HasComponent<NetworkStreamInGame>(connection) && !EntityManager.HasComponent<NetworkStreamRequestDisconnect>(connection))
                        Send(ecb, connection, new RaidChatResultRpc { Session = 0, AnnouncementSequence = ++m_AnnouncementSequence, Result = RaidChatResult.Unavailable });
                m_Announced.Clear(); m_Budgets.Clear();
            }
            string match = clash?.MatchId;
            int epoch = clash?.Epoch ?? 0;
            if (match != m_Match || epoch != m_Epoch)
            {
                m_Match = match; m_Epoch = epoch; m_Session = NewSession(); m_Sequence = 0;
                m_Announced.Clear(); m_Budgets.Clear();
            }
            var persistence = m_Persistence.IsEmptyIgnoreFilter ? null : EntityManager.GetComponentObject<RaidPersistenceContext>(m_Persistence.GetSingletonEntity());
            m_Members.Clear();
            if (enabled)
            foreach (var (joined, id, connection) in SystemAPI.Query<RefRO<JoinedClient>, RefRO<NetworkId>>()
                .WithAll<NetworkStreamInGame>().WithNone<NetworkStreamRequestDisconnect>().WithEntityAccess())
            {
                if (EntityManager.HasComponent<NetworkStreamConnection>(connection) &&
                    EntityManager.GetComponentData<NetworkStreamConnection>(connection).CurrentState != ConnectionState.State.Connected) continue;
                var value = joined.ValueRO;
                var member = new Member { Connection = connection, Id = id.ValueRO.Value, Team = 0, Group = "cooperative", Name = value.PlayerName };
                if (clash != null)
                {
                    ClashParticipant participant = null;
                    foreach (var p in clash.Players.Values) if (p.NetworkId == member.Id && p.Connected && !p.IsBot) { participant = p; break; }
                    if (participant == null) continue;
                    member.Team = participant.TeamId; member.Group = clash.MatchId;
                    member.Name.CopyFromTruncated(participant.Name);
                    member.Dead = participant.Life == ClashLife.Dead;
                }
                else
                {
                    if (persistence != null && persistence.Profiles.TryGetValue(connection, out var profile) && !string.IsNullOrEmpty(profile.OnlineInstanceId))
                    { member.Team = profile.OnlineTeam; member.Group = profile.OnlineInstanceId; }
                    if (EntityManager.HasComponent<RaidSession>(connection)) member.Dead = EntityManager.GetComponentData<RaidSession>(connection).Phase == RaidPhase.Dead;
                    if (EntityManager.HasComponent<PredictedPlayerGhost>(value.PlayerEntity))
                        member.Dead |= EntityManager.GetComponentData<PredictedPlayerGhost>(value.PlayerEntity).CurrentHealth <= 0;
                }
                m_Members.Add(connection, member);
            }
            foreach (var member in m_Members.Values)
            {
                bool team = member.Team >= 0;
                if (m_Announced.TryGetValue(member.Connection, out bool prior) && prior == team) continue;
                Result(ecb, member.Connection, 0, RaidChatResult.Ready, team);
                m_Announced[member.Connection] = team;
            }
            m_Stale.Clear();
            foreach (var connection in m_Announced.Keys) if (!m_Members.ContainsKey(connection)) m_Stale.Add(connection);
            foreach (var connection in m_Stale) { m_Announced.Remove(connection); m_Budgets.Remove(connection); }
            foreach (var (request, received, entity) in SystemAPI.Query<RefRO<RaidChatRequestRpc>, RefRW<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                received.ValueRW.Consume();
                var r = request.ValueRO;
                var source = received.ValueRO.SourceConnection;
                if (m_Members.TryGetValue(source, out var sender))
                {
                    if (!m_Budgets.TryGetValue(source, out var budget)) m_Budgets.Add(source, budget = new RaidChatBudget());
                    // A duplicate has already had its outcome sent on the reliable RPC stream.
                    if (r.Session == m_Session && budget.Begin(r.RequestId))
                    {
                        var result = budget.Spend(SystemAPI.Time.ElapsedTime) ? RaidChatRules.Normalize(r.Text.ToString(), out _) : RaidChatResult.RateLimited;
                        if (r.Channel != RaidChatChannel.Team && r.Channel != RaidChatChannel.Global) result = RaidChatResult.Unavailable;
                        if (r.Channel == RaidChatChannel.Team && sender.Team < 0) result = RaidChatResult.NoTeam;
                        if (result == RaidChatResult.Accepted)
                        {
                            RaidChatRules.Normalize(r.Text.ToString(), out string text);
                            var message = new RaidChatMessageRpc { Session = m_Session, Sequence = ++m_Sequence, RequestId = r.RequestId,
                                SenderId = sender.Id, Name = CleanName(sender.Name), Dead = sender.Dead, Channel = r.Channel, Text = text };
                            foreach (var recipient in m_Members.Values)
                                if (RaidChatRules.Receives(r.Channel, sender.Team, sender.Group, recipient.Team, recipient.Group)) Send(ecb, recipient.Connection, message);
                        }
                        Result(ecb, source, r.RequestId, result, sender.Team >= 0);
                    }
                    else if (r.Session != m_Session) Result(ecb, source, r.RequestId, RaidChatResult.Expired, sender.Team >= 0);
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }
        private static FixedString64Bytes CleanName(FixedString64Bytes name)
        {
            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(RaidChatRules.Normalize(name.ToString(), out string text) == RaidChatResult.Accepted ? text : "Player");
            return result;
        }
        private void Result(EntityCommandBuffer ecb, Entity connection, uint request, RaidChatResult result, bool team)
            => Send(ecb, connection, new RaidChatResultRpc { Session = m_Session, AnnouncementSequence = request == 0 ? ++m_AnnouncementSequence : 0, RequestId = request, Result = result, TeamAvailable = team });
        private static void Send<T>(EntityCommandBuffer ecb, Entity connection, T value) where T : unmanaged, IComponentData
        {
            var entity = ecb.CreateEntity(); ecb.AddComponent(entity, value);
            ecb.AddComponent(entity, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }
}
