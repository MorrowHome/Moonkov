using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // Host-owned profiles. Client requests never submit an inventory snapshot or settlement.
    // All game writes use the same durable deployment/outbox pipeline as other modes.
    public sealed class LanPersistence : IDisposable
    {
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, OfflineProfileStore> m_Stores = new Dictionary<string, OfflineProfileStore>();
        private readonly Dictionary<string, string> m_Credentials = new Dictionary<string, string>();
        private readonly Dictionary<string, string> m_Tickets = new Dictionary<string, string>();
        private readonly Dictionary<string, string> m_Names = new Dictionary<string, string>();
        public string Root { get; }
        public string HostId { get; }
        public string OutboxDirectory => Path.Combine(Root, "raid-outbox");
        public LanPersistence(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Path.Combine(Root, "profiles"));
            Directory.CreateDirectory(Path.Combine(Root, "matches"));
            Directory.CreateDirectory(OutboxDirectory);
            string identityFile=Path.Combine(Root,"host-id.txt");
            if(!File.Exists(identityFile))File.WriteAllText(identityFile,Guid.NewGuid().ToString("N"));
            HostId=Guid.Parse(File.ReadAllText(identityFile)).ToString("N");
            try
            {
                foreach (var file in Directory.GetFiles(Path.Combine(Root, "profiles"), "*.json"))
                {
                    var store = new OfflineProfileStore(file); var id = store.Read().PlayerId;
                    m_Stores.Add(id, store); m_Credentials.Add(Path.GetFileNameWithoutExtension(file), id);
                }
            }
            catch { Dispose(); throw; }
        }
        public async Task RecoverAsync()
        {
            var context = new RaidPersistenceContext(this); context.Dispose(); await context.ShutdownTask.ConfigureAwait(false);
            if (Directory.GetFiles(OutboxDirectory).Any(f => !f.EndsWith(".tmp", StringComparison.Ordinal)))
                throw new IOException("局域网结算恢复尚未完成，已保留原存档，请重新开房重试。");
            lock (m_Lock) foreach (var store in m_Stores.Values) store.RecoverAbandonedRaid();
        }
        private static string CredentialKey(string credential)
        {
            if (credential == null || credential.Length != 64 || credential.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("无效的局域网玩家身份。");
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(credential))).Replace("-", "").ToLowerInvariant();
        }
        public string OpenPlayer(string credential, string name)
        {
            lock (m_Lock)
            {
                string key = CredentialKey(credential);
                if (!m_Credentials.TryGetValue(key, out var id))
                {
                    var store = new OfflineProfileStore(Path.Combine(Root, "profiles", key + ".json"));
                    id = store.Read().PlayerId; m_Stores.Add(id, store); m_Credentials.Add(key, id);
                }
                m_Names[id] = name; return id;
            }
        }
        public void SetTicket(string ticket, string id) { lock (m_Lock) m_Tickets.Add(ticket, id); }
        public void RevokeTickets() { lock (m_Lock) m_Tickets.Clear(); }
        public void RevokePlayer(string id)
        { lock (m_Lock) foreach (var key in m_Tickets.Where(p => p.Value == id).Select(p => p.Key).ToArray()) m_Tickets.Remove(key); }
        public LanProfileSnapshot Read(string id)
        {
            lock (m_Lock) return new LanProfileSnapshot { Profile = Named(m_Stores[id].Read()), Recovery = m_Stores[id].ReadRecovery(), RaidActive = m_Stores[id].RaidActive };
        }
        private RaidPersistenceContext.Profile Named(RaidPersistenceContext.Profile profile)
        { if (m_Names.TryGetValue(profile.PlayerId, out var name)) profile.DisplayName = name; return profile; }
        public LanProfileSnapshot Mutate(string id, string action, JObject payload)
        {
            lock (m_Lock)
            {
                var store = m_Stores[id];
                if (store.RaidActive) throw new InvalidOperationException("对局结算完成前不能更改装备。");
                switch (action)
                {
                    case "move": store.Move(payload.ToObject<InventoryCommand>()); break;
                    case "trade": store.Trade(payload.ToObject<ShopCommand>()); break;
                    case "recovery": store.ClaimRecovery(payload.ToObject<RecoveryClaim>()); break;
                    default: throw new InvalidDataException("未知仓库操作。");
                }
                return Read(id);
            }
        }
        public RaidPersistenceContext.Profile Request(string path, object payload)
        {
            lock (m_Lock)
            {
                var data = JObject.FromObject(payload);
                if (path == "internal/sessions/resolve")
                {
                    if (!m_Tickets.TryGetValue((string)data["Token"] ?? "", out var player))
                        throw new InvalidOperationException("请从局域网房间准备并进入对局。");
                    return Named(m_Stores[player].Read());
                }
                string id = (string)data["PlayerId"];
                if (id == null || !m_Stores.TryGetValue(id, out var store)) throw new InvalidDataException("未知局域网存档。");
                return Named(store.Request(path, payload));
            }
        }
        private sealed class Registration
        {
            public string MatchId, TokenId; public int RulesVersion;
            public RaidPersistenceContext.BountyParticipant[] Participants;
        }
        private static void WriteOnce(string file, object value)
        {
            string json = JsonConvert.SerializeObject(value);
            if (File.Exists(file))
            { if (!JToken.DeepEquals(JToken.Parse(File.ReadAllText(file)), JToken.Parse(json))) throw new InvalidDataException("局域网赏金记录冲突。"); return; }
            using (var stream = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            { byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(file + ".tmp", file);
        }
        public void RegisterBounty(string match, int version, string token, RaidPersistenceContext.BountyParticipant[] participants)
        {
            lock (m_Lock)
            {
                if (!Guid.TryParse(match, out var matchId) || !Guid.TryParse(token, out _) || version != BountyClashRules.Version ||
                    participants == null || participants.Length < 1 || participants.Length > BountyClashRules.MaxPlayers ||
                    participants.Any(p => p == null || !m_Stores.ContainsKey(p.PlayerId) || !Guid.TryParse(p.DeploymentId, out _) || p.TeamId < 0 || p.TeamId >= 4) ||
                    participants.Select(p => p.PlayerId).Distinct().Count() != participants.Length ||
                    participants.Select(p => p.DeploymentId).Distinct().Count() != participants.Length || participants.GroupBy(p => p.TeamId).Any(g => g.Count() > 3))
                    throw new InvalidDataException("无效的局域网赏金参赛名单。");
                var registration = new Registration { MatchId = match, TokenId = token, RulesVersion = version, Participants = participants };
                string file = Path.Combine(Root, "matches", matchId.ToString("N") + ".json");
                foreach (var other in Directory.GetFiles(Path.Combine(Root, "matches"), "*.json"))
                    if (other != file && JsonConvert.DeserializeObject<Registration>(File.ReadAllText(other)).TokenId == token)
                        throw new InvalidDataException("赏金凭证已用于其他对局。");
                WriteOnce(file, registration);
                foreach (var player in participants) m_Stores[player.PlayerId].RegisterBountyMatch(match, version, token, participants, true);
            }
        }
        public void RedeemBounty(RaidPersistenceContext.BountyRedemption award)
        {
            lock (m_Lock)
            {
                string file = Path.Combine(Root, "matches", Guid.Parse(award.MatchId).ToString("N"));
                var registration = JsonConvert.DeserializeObject<Registration>(File.ReadAllText(file + ".json"));
                // Replay a partially written registration before distributing an extraction.
                foreach (var player in registration.Participants)
                    m_Stores[player.PlayerId].RegisterBountyMatch(registration.MatchId, registration.RulesVersion, registration.TokenId, registration.Participants, true);
                var carrier = registration.Participants.Single(p => p.DeploymentId == award.CarrierDeploymentId);
                if (award.TokenId != registration.TokenId || !Guid.TryParse(award.ExtractionEventId, out _) || award.ExtractedDeploymentIds == null ||
                    award.ExtractedDeploymentIds.Length < 1 || award.ExtractedDeploymentIds.Length > 3 ||
                    award.ExtractedDeploymentIds.Distinct().Count() != award.ExtractedDeploymentIds.Length || !award.ExtractedDeploymentIds.Contains(carrier.DeploymentId) ||
                    award.ExtractedDeploymentIds.Any(id => !registration.Participants.Any(p => p.DeploymentId == id && p.TeamId == carrier.TeamId)))
                    throw new InvalidDataException("无效的局域网赏金撤离记录。");
                WriteOnce(file + ".award", award);
                foreach (var player in registration.Participants.Where(p => award.ExtractedDeploymentIds.Contains(p.DeploymentId))) m_Stores[player.PlayerId].RedeemBounty(award);
            }
        }
        public void Dispose()
        { lock (m_Lock) { foreach (var store in m_Stores.Values) store.Dispose(); m_Stores.Clear(); m_Tickets.Clear(); } }
    }
}
