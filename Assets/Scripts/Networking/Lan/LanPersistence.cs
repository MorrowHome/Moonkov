// 好友房的长期档案存在房主电脑上。它把房间成员的凭据对应到各自的 OfflineProfileStore 并维护本轮的短期入场票。
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

namespace Unity.MP_FPS {
    // Host-owned profiles. Client requests never submit an inventory snapshot or settlement.
    // All game writes use the same durable deployment/outbox pipeline as other modes.
    public sealed class LanPersistence : IDisposable {
        private readonly object m_Lock = new();
        private readonly Dictionary<string, OfflineProfileStore> m_Stores = new();
        private readonly Dictionary<string, string> m_Credentials = new();
        private readonly Dictionary<string, string> m_Tickets = new();
        private readonly Dictionary<string, string> m_Names = new();
        public string Root { get; }
        public string HostId { get; }
        public string OutboxDirectory => Path.Combine(Root, "raid-outbox");

        public LanPersistence(string root) {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Path.Combine(Root, "profiles"));
            Directory.CreateDirectory(Path.Combine(Root, "matches"));
            Directory.CreateDirectory(OutboxDirectory);
            // 房主身份单独写盘，重新开房仍可找到对应存档。好友客户端会结合这个身份派生它在该房的凭据。
            string identityFile = Path.Combine(Root, "host-id.txt");
            if (!File.Exists(identityFile)) File.WriteAllText(identityFile, Guid.NewGuid().ToString("N"));
            HostId = Guid.Parse(File.ReadAllText(identityFile)).ToString("N");
            try {
                // 开房先打开已有来宾档并建立索引。磁盘文件名是凭据哈希，档内另有真正的 PlayerId，结算走后者。
                foreach (string file in Directory.GetFiles(Path.Combine(Root, "profiles"), "*.json")) {
                    var store = new OfflineProfileStore(file);
                    string id = store.Read().PlayerId;
                    m_Stores.Add(id, store);
                    m_Credentials.Add(Path.GetFileNameWithoutExtension(file), id);
                }
            } catch {
                Dispose();
                throw;
            }
        }

        public async Task RecoverAsync() {
            var context = new RaidPersistenceContext(this);
            context.Dispose();
            await context.ShutdownTask.ConfigureAwait(false);
            // 先恢复以前没写完的出战、结算记录。若仍有待处理，不急着当作弃局，否则可能丢掉本应成功撤回的装备。
            if (Directory.GetFiles(OutboxDirectory).Any(f => !f.EndsWith(".tmp", StringComparison.Ordinal)))
                throw new IOException("局域网结算恢复尚未完成，已保留原存档，请重新开房重试。");
            lock (m_Lock) {
                foreach (var store in m_Stores.Values) store.RecoverAbandonedRaid();
            }
        }

        private static string CredentialKey(string credential) {
            if (credential == null || credential.Length != 64 || credential.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("无效的局域网玩家身份。");
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(credential))).Replace("-", "")
                .ToLowerInvariant();
        }

        public string OpenPlayer(string credential, string name) {
            lock (m_Lock) {
                // 来宾的昵称可变。房主按凭据的哈希找原档，第一次见才分配一个新的文件和玩家 ID。
                string key = CredentialKey(credential);
                if (!m_Credentials.TryGetValue(key, out string id)) {
                    var store = new OfflineProfileStore(Path.Combine(Root, "profiles", key + ".json"));
                    id = store.Read().PlayerId;
                    m_Stores.Add(id, store);
                    m_Credentials.Add(key, id);
                }
                m_Names[id] = name;
                return id;
            }
        }

        public void SetTicket(string ticket, string id) {
            lock (m_Lock) { m_Tickets.Add(ticket, id); }
        }

        public void RevokeTickets() {
            lock (m_Lock) { m_Tickets.Clear(); }
        }

        // 成员离开或返回时移除他的所有短期入场票。长期成员档仍保留，下次加入可以读回。
        public void RevokePlayer(string id) {
            lock (m_Lock) {
                foreach (string key in m_Tickets.Where(p => p.Value == id).Select(p => p.Key).ToArray())
                    m_Tickets.Remove(key);
            }
        }

        public LanProfileSnapshot Read(string id) {
            lock (m_Lock) {
                return new LanProfileSnapshot {
                    Profile = Named(m_Stores[id].Read()), Recovery = m_Stores[id].ReadRecovery(),
                    RaidActive = m_Stores[id].RaidActive
                };
            }
        }

        private RaidPersistenceContext.Profile Named(RaidPersistenceContext.Profile profile) {
            if (m_Names.TryGetValue(profile.PlayerId, out string name)) profile.DisplayName = name;
            return profile;
        }

        public LanProfileSnapshot Mutate(string id, string action, JObject payload) {
            lock (m_Lock) {
                var store = m_Stores[id];
                // 档案在活动战局中不能同时拖格或买卖。仓库提交经过房主自己的锁和版本规则，执行后返回新的完整档案。
                if (store.RaidActive) throw new InvalidOperationException("对局结算完成前不能更改装备。");
                switch (action) {
                    case "move": store.Move(payload.ToObject<InventoryCommand>()); break;
                    case "trade": store.Trade(payload.ToObject<ShopCommand>()); break;
                    case "recovery": store.ClaimRecovery(payload.ToObject<RecoveryClaim>()); break;
                    case "research":
                        if (!store.ApplyResearchCommand(payload.ToObject<ResearchQuestCommand>()))
                            throw new InvalidOperationException("当前任务状态无法执行此操作。");
                        break;
                    default: throw new InvalidDataException("未知仓库操作。");
                }
                return Read(id);
            }
        }

        // 战局服务器用同一套持久化请求进来。入场先把本轮票据还原成房间成员，之后出战和结算改的是这个成员的档案。
        public RaidPersistenceContext.Profile Request(string path, object payload) {
            lock (m_Lock) {
                var data = JObject.FromObject(payload);
                if (path == "internal/sessions/resolve") {
                    // 战局请求带同一轮房间签发的票。经房主索引查到的玩家 ID 才是实际入场身份。
                    if (!m_Tickets.TryGetValue((string)data["Token"] ?? "", out string player))
                        throw new InvalidOperationException("请在好友房间准备并进入对局。");
                    return Named(m_Stores[player].Read());
                }
                string id = (string)data["PlayerId"];
                if (id == null || !m_Stores.TryGetValue(id, out var store)) throw new InvalidDataException("未知局域网存档。");
                return Named(store.Request(path, payload));
            }
        }

        private sealed class Registration {
            public string MatchId, TokenId;
            public int RulesVersion;
            public RaidPersistenceContext.BountyParticipant[] Participants;
        }

        private static void WriteOnce(string file, object value) {
            // 比赛登记和奖励记录一旦写入，原编号只接受同样内容。临时文件刷完再改名，半途退出也不会留下半份正式记录。
            string json = JsonConvert.SerializeObject(value);
            if (File.Exists(file)) {
                if (!JToken.DeepEquals(JToken.Parse(File.ReadAllText(file)), JToken.Parse(json)))
                    throw new InvalidDataException("局域网赏金记录冲突。");
                return;
            }
            using (var stream = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(file + ".tmp", file);
        }

        public void RegisterBounty(
            string match,
            int version,
            string token,
            RaidPersistenceContext.BountyParticipant[] participants) {
            lock (m_Lock) {
                if (!Guid.TryParse(match, out var matchId) || !Guid.TryParse(token, out _) ||
                    version != BountyClashRules.Version || participants == null || participants.Length < 1 ||
                    participants.Length > BountyClashRules.MaxPlayers ||
                    participants.Any(p =>
                        p == null || !m_Stores.ContainsKey(p.PlayerId) || !Guid.TryParse(p.DeploymentId, out _) ||
                        p.TeamId < 0 || p.TeamId >= 4) ||
                    participants.Select(p => p.PlayerId).Distinct().Count() != participants.Length ||
                    participants.Select(p => p.DeploymentId).Distinct().Count() != participants.Length ||
                    participants.GroupBy(p => p.TeamId).Any(g => g.Count() > 3))
                    throw new InvalidDataException("无效的局域网赏金参赛名单。");
                var registration = new Registration
                    { MatchId = match, TokenId = token, RulesVersion = version, Participants = participants };
                string file = Path.Combine(Root, "matches", matchId.ToString("N") + ".json");
                // 赏金 token 只允许分配给一场。先落整场名单，之后才逐个给房里每名参加者登记。
                foreach (string other in Directory.GetFiles(Path.Combine(Root, "matches"), "*.json"))
                    if (other != file && JsonConvert.DeserializeObject<Registration>(File.ReadAllText(other)).TokenId ==
                        token)
                        throw new InvalidDataException("赏金凭证已用于其他对局。");
                WriteOnce(file, registration);
                foreach (var player in participants)
                    m_Stores[player.PlayerId].RegisterBountyMatch(match, version, token, participants, true);
            }
        }

        public void RedeemBounty(RaidPersistenceContext.BountyRedemption award) {
            lock (m_Lock) {
                string file = Path.Combine(Root, "matches", Guid.Parse(award.MatchId).ToString("N"));
                var registration = JsonConvert.DeserializeObject<Registration>(File.ReadAllText(file + ".json"));
                // Replay a partially written registration before distributing an extraction.
                // 上次写完整场登记却没来得及登记到每位玩家时，先补个人档，再检查携带者和撤离队友都属于原名单。
                foreach (var player in registration.Participants)
                    m_Stores[player.PlayerId].RegisterBountyMatch(registration.MatchId, registration.RulesVersion,
                        registration.TokenId, registration.Participants, true);
                var carrier = registration.Participants.Single(p => p.DeploymentId == award.CarrierDeploymentId);
                if (award.TokenId != registration.TokenId || !Guid.TryParse(award.ExtractionEventId, out _) ||
                    award.ExtractedDeploymentIds == null || award.ExtractedDeploymentIds.Length < 1 ||
                    award.ExtractedDeploymentIds.Length > 3 ||
                    award.ExtractedDeploymentIds.Distinct().Count() != award.ExtractedDeploymentIds.Length ||
                    !award.ExtractedDeploymentIds.Contains(carrier.DeploymentId) ||
                    award.ExtractedDeploymentIds.Any(id =>
                        !registration.Participants.Any(p => p.DeploymentId == id && p.TeamId == carrier.TeamId)))
                    throw new InvalidDataException("无效的局域网赏金撤离记录。");
                // 先落下整场统一的奖励事件，再给成功撤出的成员写个人奖励。重试能从同一个事件继续发到还没处理的档。
                WriteOnce(file + ".award", award);
                foreach (var player in registration.Participants.Where(p =>
                             award.ExtractedDeploymentIds.Contains(p.DeploymentId)))
                    m_Stores[player.PlayerId].RedeemBounty(award);
            }
        }

        public void Dispose() {
            lock (m_Lock) {
                foreach (var store in m_Stores.Values) store.Dispose();
                m_Stores.Clear();
                m_Tickets.Clear();
            }
        }
    }
}