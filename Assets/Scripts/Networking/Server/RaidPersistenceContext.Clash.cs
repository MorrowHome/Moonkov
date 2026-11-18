// 极速赏金在服务端持久化上下文里以一场比赛为单位登记名单和奖励，绑定每个成员的扣装结果，最后将撤离兑换与各自结算写入档案。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Entities;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class RaidPersistenceContext {
        public sealed class BountyParticipant {
            public string PlayerId, DeploymentId;
            public int TeamId;
        }

        private sealed class BountyRegistration {
            public string MatchId, TokenId;
            public int RulesVersion;
            public BountyParticipant[] Participants;
        }

        public sealed class BountyRedemption {
            public string MatchId, TokenId, ExtractionEventId, CarrierDeploymentId;
            public string[] ExtractedDeploymentIds;
        }

        private sealed class BountyFinalization {
            public BountyRedemption Redemption;
            public Settlement[] Settlements;
        }

        private sealed class Capability {
            public string[] Features { get; set; }
        }

        private readonly Task<bool> m_ClashCapability = Task.FromResult(false);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task> m_ClashRegistrations = new();
        private bool m_ClashSoloEnabled;

        public bool ClashSupported =>
            m_Offline != null
                ? m_ClashSoloEnabled
                : m_ClashCapability.Status == TaskStatus.RanToCompletion && m_ClashCapability.Result;

        // 由服务器 World 明确提供 OfflineRaidMode、ClashSoloMode
        // 和 BountyClash 开启条件。这些标记不能从客户端请求读取。
        public void SetClashSoloEnabled(bool enabled) {
            if (enabled && m_Offline == null)
                throw new InvalidOperationException("Solo Clash requires an isolated offline profile.");
            m_ClashSoloEnabled = enabled;
        }

        private async Task<bool> CheckClashCapabilityAsync() {
            try {
                using (var response = await m_Http.GetAsync("internal/health", m_Stop.Token).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode) return false;
                    var health =
                        JsonConvert.DeserializeObject<Capability>(await response.Content.ReadAsStringAsync()
                            .ConfigureAwait(false));
                    return health?.Features?.Contains("bounty-clash-v1") == true;
                }
            } catch (Exception) { return false; }
        }

        // 在线会消耗后端签发的游戏票并带实例号校验。LAN 票和单人占位值走本地档案认领，不通过网络。
        private async Task<Profile> ResolveJoinAsync(string token) {
            await m_ClashCapability.ConfigureAwait(false);
            return await PostAsync(m_Http == null ? "internal/sessions/resolve" : "internal/game-tickets/consume",
                new { Token = token, InstanceId = OnlineInstanceServer.InstanceId }).ConfigureAwait(false);
        }

        private async Task PostClashAsync(string path, object payload) {
            // 房主为所有成员统一登记或派发奖励。单人只能在服务器显式启用极速赏金后登记；重开恢复已记录结果仍可走旧事件。
            if (m_Lan != null) {
                if (path == "matches") {
                    var registration = (BountyRegistration)payload;
                    await Task.Run(() => m_Lan.RegisterBounty(registration.MatchId, registration.RulesVersion,
                        registration.TokenId, registration.Participants)).ConfigureAwait(false);
                } else if (path == "redemptions") {
                    await Task.Run(() => m_Lan.RedeemBounty((BountyRedemption)payload)).ConfigureAwait(false);
                } else { throw new InvalidOperationException("Unsupported LAN Clash operation."); }
                return;
            }
            if (m_Offline != null) {
                if (path == "matches") {
                    if (!m_ClashSoloEnabled)
                        throw new LoadoutRejectedException(RaidLoadoutError.Rejected,
                            "Offline Clash was not explicitly enabled by this server World.");
                    var registration = (BountyRegistration)payload;
                    await Task.Run(() => m_Offline.RegisterBountyMatch(registration.MatchId, registration.RulesVersion,
                        registration.TokenId, registration.Participants)).ConfigureAwait(false);
                } else if (path == "redemptions") {
                    // 恢复可能发生在选择新玩法之前。此时档案
                    // 只接受已经登记且确属本人的出战或事件。
                    await Task.Run(() => m_Offline.RedeemBounty((BountyRedemption)payload)).ConfigureAwait(false);
                } else { throw new InvalidOperationException("Unsupported local Clash operation."); }
                return;
            }
            if (m_Http == null || !await m_ClashCapability.ConfigureAwait(false))
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,
                    "Bounty Clash persistence is unavailable; update the backend.");
            using (var content = new System.Net.Http.StringContent(JsonConvert.SerializeObject(payload),
                       System.Text.Encoding.UTF8, "application/json"))
            using (var response = await m_Http.PostAsync("internal/bounty-clash/" + path, content, m_Stop.Token)
                       .ConfigureAwait(false)) { response.EnsureSuccessStatusCode(); }
        }

        public Task RegisterBountyMatchAsync(
            Guid matchId,
            int rulesVersion,
            Guid tokenId,
            BountyParticipant[] participants) {
            var registration = new BountyRegistration {
                MatchId = matchId.ToString("D"), RulesVersion = rulesVersion, TokenId = tokenId.ToString("D"),
                Participants = participants
            };
            var task = PostClashAsync("matches", registration);
            m_ClashRegistrations[registration.MatchId] = task;
            return task;
        }

        public void BeginBountyDeploy(
            Entity connection,
            Guid matchId,
            int rulesVersion,
            int teamId,
            Guid deploymentId,
            int cells,
            Join join = null) {
            if (m_Offline != null && !m_ClashSoloEnabled)
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,
                    "Offline Clash requires its explicit local marker.");
            if (Deployments.ContainsKey(connection)) return;
            var payload = new Deployment {
                PlayerId = Profiles[connection].PlayerId, DeploymentId = deploymentId.ToString("D"), Cells = cells,
                Mode = "BountyClash", MatchId = matchId.ToString("D"), RulesVersion = rulesVersion, TeamId = teamId
            };
            Deployments.Add(connection,
                new Deploy { Connection = connection, Join = join, Payload = payload, Task = DeployAsync(payload) });
        }

        // 给这场扣装备前必须等参赛凭据写好。否则日后账本无法判断这批物品属于哪名参赛者。
        private async Task AwaitBountyRegistrationAsync(Deployment payload) {
            if (payload.Mode == null) return;
            if (m_Offline != null && !m_ClashSoloEnabled)
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,
                    "Offline Clash deployment is not enabled.");
            if (payload.Mode != "BountyClash" ||
                !m_ClashRegistrations.TryGetValue(payload.MatchId, out var registration))
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,
                    "Bounty Clash roster has not been registered.");
            try { await registration.ConfigureAwait(false); } catch (Exception) {
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected,
                    "Bounty Clash roster registration failed before debit.");
            }
        }

        public void RefundBountyDeployment(Deployment payload, Task<Profile> debitTask = null) {
            if (Saves.ContainsKey(payload.DeploymentId)) return;
            // 没提交过扣装请求就没有需要退的装。若已提交而结果未知，等待原任务并核对待发文件，不能假定没扣。
            if (debitTask == null && !File.Exists(DeploymentFile(payload))) return;
            payload.Refund = true;
            var save = new Save {
                Payload = new Settlement { PlayerId = payload.PlayerId, SettlementId = payload.DeploymentId },
                AbandonedDeployment = payload
            };
            Saves.Add(payload.DeploymentId, save);
            save.Task = Task.Run(async () => {
                if (debitTask != null)
                    try { await debitTask.ConfigureAwait(false); } catch (Exception) {
                        /* 待发日志可区分可能已扣装和登记失败。 */
                    }
                if (!File.Exists(DeploymentFile(payload)))
                    return new Profile { PlayerId = payload.PlayerId }; // 未发出扣装请求。
                // 把已决定的退款记录单独留在本地，重启后也按原出战 ID 继续，可区分一般弃局。
                WriteJournal(Path.ChangeExtension(DeploymentFile(payload), ".refund"), payload);
                return await RetrySave(save).ConfigureAwait(false);
            });
        }

        // 通过服务端认证以后才调用，或在分离
        // 已认证的拥有者时调用。既有回执和任务保持原样。
        public bool RebindBountyOwner(
            Entity oldOwner,
            Entity newOwner,
            string authenticatedPlayerId,
            string settlementId) {
            if (oldOwner.Equals(newOwner) || !Guid.TryParse(authenticatedPlayerId, out var playerId) ||
                playerId == Guid.Empty || !Guid.TryParse(settlementId, out var receiptId) || receiptId == Guid.Empty ||
                !Profiles.TryGetValue(oldOwner, out var source) || !Guid.TryParse(source.PlayerId, out var sourceId) ||
                sourceId != playerId)
                return false;
            // 移动连接归属前，同时核对原玩家、当前出战、旧结算和目标连接。即使实际账户一致也不能把另一场的回执带过去。
            if (source.DeploymentId != null &&
                (!Guid.TryParse(source.DeploymentId, out var deploymentId) || deploymentId != receiptId))
                return false;
            Profiles.TryGetValue(newOwner, out var destination);
            if (destination != null &&
                (!Guid.TryParse(destination.PlayerId, out var destinationId) || destinationId != playerId))
                return false;
            Saves.TryGetValue(receiptId.ToString("D"), out var save);
            if (save != null && (!save.Connection.Equals(oldOwner) ||
                                 !Guid.TryParse(save.Payload.PlayerId, out var savePlayer) || savePlayer != playerId ||
                                 !Guid.TryParse(save.Payload.SettlementId, out var saveReceipt) ||
                                 saveReceipt != receiptId))
                return false;
            if (Saves.Values.Any(pending => pending.Connection.Equals(oldOwner) && !ReferenceEquals(pending, save)))
                return false;
            Inventories.TryGetValue(oldOwner, out var inventory);
            if (inventory != null && Inventories.TryGetValue(newOwner, out var existing) &&
                !ReferenceEquals(inventory, existing))
                return false;
            // 正在进行的出战继续使用原战局绑定。已结束时
            // 可以采用认证后新读到的账号库存，避免使用旧缓存。
            // 活动出战要保留原随身库存和绑定。只把连接上的引用转过来，既有请求体、编号和任务保持原样。
            Profiles[newOwner] = source.DeploymentId == null && destination != null ? destination : source;
            Profiles.Remove(oldOwner);
            if (inventory != null) {
                Inventories[newOwner] = inventory;
                Inventories.Remove(oldOwner);
            }
            if (save != null) save.Connection = newOwner;
            return true;
        }

        internal static Deployment DeploymentOf(Profile profile) {
            return new Deployment {
                PlayerId = profile.PlayerId, DeploymentId = profile.DeploymentId,
                Cells = profile.CarriedCells, Mode = profile.Mode, MatchId = profile.MatchId,
                RulesVersion = profile.RulesVersion, TeamId = profile.TeamId
            };
        }

        private static string ProfilePath(string path, string mode) {
            return mode == "BountyClash" ? "internal/bounty-clash/" + path : "internal/" + path;
        }

        private string BountyFile(BountyRedemption redemption) {
            return Path.Combine(m_Outbox, Guid.Parse(redemption.MatchId).ToString("N") + ".bounty");
        }

        // 兑换前一次性保留整组撤离数据。即使之后崩溃，
        // 也能恢复每位成员此前冻结的库存回执。
        // 赏金兑换是整场共用的事件。先把兑换和需要落盘的结算留下记录，重启后可以接着送同一份结果。
        public void BeginBountyRedemption(
            Guid matchId,
            Guid tokenId,
            Guid extractionEventId,
            Guid carrierDeploymentId,
            Guid[] extractedDeploymentIds) {
            if (m_Offline != null && !m_ClashSoloEnabled)
                throw new InvalidOperationException("New local Clash extraction requires its explicit server marker.");
            var redemption = new BountyRedemption {
                MatchId = matchId.ToString("D"), TokenId = tokenId.ToString("D"),
                ExtractionEventId = extractionEventId.ToString("D"),
                CarrierDeploymentId = carrierDeploymentId.ToString("D"),
                ExtractedDeploymentIds = extractedDeploymentIds.Select(id => id.ToString("D")).ToArray()
            };
            var snapshots = new List<Settlement>();
            // 发赏金时从服务器此刻认定的撤离名单逐人复制库存，保存为不可变的个人结算内容。断线也不影响这些结果。
            foreach (string id in redemption.ExtractedDeploymentIds) {
                var pair = Profiles.Single(p =>
                    p.Value.DeploymentId == id && p.Value.MatchId == redemption.MatchId &&
                    p.Value.Mode == "BountyClash");
                var graph = Inventories[pair.Key].Graph;
                snapshots.Add(new Settlement {
                    PlayerId = pair.Value.PlayerId, DeploymentId = id, SettlementId = id, Outcome = "Extracted",
                    Dust = graph.Count("dust", true), Alloy = graph.Count("alloy", true),
                    Cells = graph.Count("cells", true), InventoryJson = JsonConvert.SerializeObject(graph),
                    Mode = pair.Value.Mode, MatchId = pair.Value.MatchId, RulesVersion = pair.Value.RulesVersion,
                    TeamId = pair.Value.TeamId,
                    BountyEventId = redemption.ExtractionEventId, Redemption = redemption
                });
            }
            // 整组记录要先确定，才能向后端逐个保存。关闭时这些成员都有结算中的标记，不会被当成普通弃局。
            var finalization = new BountyFinalization { Redemption = redemption, Settlements = snapshots.ToArray() };
            // 发异步请求前将所有结算登记到队列。关停时不能将其中一人当作弃局。
            var journal = Task.Run(() => WriteJournal(BountyFile(redemption), finalization));
            foreach (var payload in snapshots) {
                if (Saves.ContainsKey(payload.SettlementId))
                    throw new InvalidOperationException("Bounty recipient is already settling.");
                var connection = Profiles.Single(p => p.Value.DeploymentId == payload.DeploymentId).Key;
                var save = new Save
                    { Connection = connection, RaidId = Inventories[connection].RaidId, Payload = payload };
                Saves.Add(payload.SettlementId, save);
                save.Task = SaveBountyAsync(payload, journal);
            }
        }

        private void ReplayBountyFinalizations() {
            foreach (string file in Directory.GetFiles(m_Outbox, "*.bounty")) {
                var group = JsonConvert.DeserializeObject<BountyFinalization>(File.ReadAllText(file));
                // 重启核对整组撤离成员和已冻结的个人结果。两份名单一致才能重发奖励，防止把不该领奖的人补到旧回执。
                if (group?.Redemption == null || group.Settlements == null || group.Settlements.Length is < 1 or > 3 ||
                    Path.GetFullPath(file) != BountyFile(group.Redemption) ||
                    !group.Redemption.ExtractedDeploymentIds.OrderBy(id => id)
                        .SequenceEqual(group.Settlements.Select(p => p.DeploymentId).OrderBy(id => id)))
                    throw new InvalidDataException("Invalid Bounty Clash finalization journal.");
                foreach (var payload in group.Settlements) {
                    if (payload.Mode != "BountyClash" || payload.MatchId != group.Redemption.MatchId ||
                        payload.BountyEventId != group.Redemption.ExtractionEventId ||
                        !Guid.TryParse(payload.PlayerId, out _) || payload.SettlementId != payload.DeploymentId ||
                        payload.InventoryJson == null)
                        throw new InvalidDataException("Invalid Bounty Clash settlement journal.");
                    payload.Redemption = group.Redemption;
                    if (Saves.ContainsKey(payload.SettlementId))
                        throw new InvalidDataException("Duplicate Bounty Clash finalization.");
                    var save = new Save { Payload = payload };
                    Saves.Add(payload.SettlementId, save);
                    save.Task = SaveBountyAsync(payload, Task.CompletedTask);
                }
            }
        }

        private async Task<Profile> SaveBountyAsync(Settlement payload, Task journal) {
            // 先等整组兑换日志写完，再做个人结算日志和请求。部分网络失败时仍留下完整可重放结果。
            await journal.ConfigureAwait(false);
            // SaveAsync 同样在任何网络操作前写好个人回执。
            return await SaveAsync(payload).ConfigureAwait(false);
        }

        private async Task RedeemBeforeSaveAsync(Settlement payload) {
            if (payload.Redemption != null)
                await PostClashAsync("redemptions", payload.Redemption).ConfigureAwait(false);
        }

        public void CleanBountyJournals() {
            foreach (string file in Directory.GetFiles(m_Outbox, "*.bounty")) {
                var group = JsonConvert.DeserializeObject<BountyFinalization>(File.ReadAllText(file));
                if (group == null) continue;
                bool pending = false;
                foreach (var payload in group.Settlements)
                    if (File.Exists(Path.Combine(m_Outbox, Guid.Parse(payload.SettlementId).ToString("N") + ".json")) ||
                        (Saves.TryGetValue(payload.SettlementId, out var save) &&
                         (save.Task == null || !save.Task.IsCompletedSuccessfully)))
                        pending = true;
                // 所有个人结果都已确认才删整组文件。只要有一个还在重试，就要能复盘该场共同奖励。
                if (!pending) File.Delete(file);
            }
        }
    }
}