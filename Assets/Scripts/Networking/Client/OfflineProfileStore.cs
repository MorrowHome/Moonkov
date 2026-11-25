// 单人档案的实际读写者。PlayerProfileClient 读仓库，RaidPersistenceContext 通过它锁定出战装备、提交结算和恢复未完成的保存。
// 对同一份档案的修改放在锁里，先在副本上算完整结果，再按 Commit 的流程写盘。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS {
    // 独立的单人档案。每次修改先提交完整快照，
    // 随后才对外展示。出战和结算回执重启后仍可恢复。
    public sealed partial class OfflineProfileStore : IDisposable {
        public const int CurrentFormat = 4;

        private sealed class SaveData {
            public int Format = CurrentFormat;
            public string PlayerId = Guid.NewGuid().ToString("D");
            public InventoryGraph Inventory;
            public RecoveryInbox Recovery = new();
            public string DeploymentId, RaidInventoryJson;
            public int CarriedCells;
            public Dictionary<string, string> Receipts = new();
            public HashSet<string> MissionArtifacts = new();
            public OfflineClashState Clash;
            public CampaignProgress Campaign;
            public TutorialProgress Tutorial;
            public ResearchQuestProgress ResearchQuests = new();
            public int ActiveCampaignVersion;
        }

        private readonly object m_Lock = new();
        private SaveData m_Save;
        private readonly FileStream m_Lease;
        private bool m_Disposed;
        private int m_LoadingCheckpoints;
        public string FilePath { get; }
        public string OutboxDirectory => Path.Combine(Path.GetDirectoryName(FilePath), "raid-outbox");

        public bool RaidActive {
            get {
                lock (m_Lock) { return m_Save.DeploymentId != null; }
            }
        }

        // 一次单人加载在相机
        // 和同步后的地图就绪前一直持有这个检查点。退款要等服务器关停
        // 并且待处理结算全都收尾，防止迟来的放弃请求删掉原有装备。
        // 入场加载出错时需要有地方退回。这个检查点保存加载前的档案内容，加载走完就确认，没走完可以恢复。
        public sealed class LoadingCheckpoint {
            private readonly OfflineProfileStore m_Owner;
            private string m_Snapshot;

            internal LoadingCheckpoint(OfflineProfileStore owner, string snapshot) {
                m_Owner = owner;
                m_Snapshot = snapshot;
            }

            // 加载成功只要丢掉起点快照和检查点计数。出战和库存这时已经由本机档案正常持有。
            public void Complete() {
                lock (m_Owner.m_Lock) {
                    if (m_Snapshot == null) return;
                    m_Snapshot = null;
                    m_Owner.m_LoadingCheckpoints--;
                }
            }

            public void Refund() {
                lock (m_Owner.m_Lock) {
                    if (m_Snapshot == null) return;
                    if (m_Owner.m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                    // 加载失败后也要先让服务端 World 停止并处理放弃请求。还有活动出战就不能提前把出装还回去。
                    if (m_Owner.RaidActive)
                        throw new InvalidOperationException("Wait for raid shutdown before refunding a failed load.");
                    var next = JsonConvert.DeserializeObject<SaveData>(m_Snapshot);
                    if (JsonConvert.SerializeObject(m_Owner.m_Save) != m_Snapshot) {
                        // 保留已经结束的操作回执。这样重放旧日志时，
                        // 这次已退款的尝试不会再次扣装或结算。
                        // 把失败期间已经写下的回执一并带回去。这样恢复旧写入记录时知道这趟加载已经退款，不能再次扣装。
                        foreach (var receipt in m_Owner.m_Save.Receipts)
                            if (!next.Receipts.ContainsKey(receipt.Key))
                                next.Receipts.Add(receipt.Key, "loading_refunded");
                        // 加载界面仍可更改任务追踪。
                        // 游戏场景准备好之前，章节事实不能落盘。
                        // 加载页可以改变追踪偏好和引导状态。退款恢复的是原出装，把这些新偏好复制回来可避免界面白操作。
                        CopyCampaignTracking(m_Owner.m_Save.Campaign, next.Campaign);
                        next.Tutorial.Status = m_Owner.m_Save.Tutorial.Status;
                        next.Tutorial.IntroPage = m_Owner.m_Save.Tutorial.IntroPage;
                        // 恢复后用比前后档案都新的版本。拿着失败加载期间旧库存的菜单动作会被拒绝，而不会意外命中新档。
                        next.Inventory.Version = Math.Max(next.Inventory.Version, m_Owner.m_Save.Inventory.Version) + 1;
                        next.Recovery.Version = Math.Max(next.Recovery.Version, m_Owner.m_Save.Recovery.Version) + 1;
                        Validate(next);
                        m_Owner.Commit(next);
                    }
                    m_Snapshot = null;
                    m_Owner.m_LoadingCheckpoints--;
                }
            }
        }

        public LoadingCheckpoint BeginLoadingCheckpoint() {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive)
                    throw new InvalidOperationException(
                        "The previous deployment must settle before loading another raid.");
                // 检查点记录在正式出战前打开的档案快照。检查点仍在时，战役和部分仓库写入会等加载完成。
                m_LoadingCheckpoints++;
                return new LoadingCheckpoint(this, JsonConvert.SerializeObject(m_Save));
            }
        }

        public OfflineProfileStore(string path) {
            FilePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            // 给存档文件加一个独占租约。第二个档案对象不能同时打开并用自己的读改写覆盖这份档。
            try {
                m_Lease = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None);
            } catch (IOException ex) {
                throw new IOException("Single-player save is in use or unavailable. Close other instances and retry.",
                    ex);
            }
            try {
                if (File.Exists(FilePath)) {
                    try {
                        string json = File.ReadAllText(FilePath);
                        // 先检查原始 JSON 里的关键字段，再反序列化。当新版本字段被删掉时，C# 的默认值不能把它假装成有效记录。
                        ValidateCampaignJson(json);
                        m_Save = JsonConvert.DeserializeObject<SaveData>(json);
                        Validate(m_Save);
                        TryMigrateProtection();
                        // 旧版档案先通过兼容校验和迁移才能转成当前格式。读到损坏记录就抛错，原文件仍可恢复。
                        if (m_Save.Format < CurrentFormat) Commit(Copy());
                    } catch (Exception ex) when (ex is JsonException || ex is InvalidDataException) {
                        throw new InvalidDataException(
                            "Single-player save is invalid. The original file was preserved.", ex);
                    }
                } else {
                    // 确认磁盘还没有档案才发起始资源并激活教学。有损坏的旧档时不会用新档覆盖。
                    m_Save = new SaveData {
                        Inventory = InventoryGraph.Create(), Campaign = CampaignRules.Create(),
                        Tutorial = new TutorialProgress { Status = TutorialStatus.Active }
                    };
                    m_Save.Receipts.Add("tutorial/schema-v1", "1");
                    m_Save.Inventory.AddSupply("dust", InventoryCatalog.StartingDust, "stash");
                    m_Save.Inventory.AddSupply("cells", 12, "stash");
                    m_Save.Inventory.AddSupply("medkit", 4, "stash");
                    Commit(m_Save);
                }
            } catch {
                m_Lease.Dispose();
                throw;
            }
        }

        private static void Validate(SaveData save) {
            if (save != null && save.Recovery == null) save.Recovery = new RecoveryInbox();
            if (save == null || (save.Format < 1 || save.Format > CurrentFormat) ||
                !Guid.TryParse(save.PlayerId, out _) || save.Inventory?.Items == null ||
                save.Inventory.Validate() != InventoryError.None || save.Inventory.Find("stash") == null ||
                save.Inventory.Find("loot") != null || save.Recovery.Validate(save.Inventory) != InventoryError.None ||
                save.Receipts == null || save.MissionArtifacts == null ||
                (save.DeploymentId != null && (!Guid.TryParse(save.DeploymentId, out _) ||
                                               string.IsNullOrEmpty(save.RaidInventoryJson) ||
                                               !RaidRules.ValidLoadout(save.CarriedCells))))
                throw new InvalidDataException("Unsupported or damaged single-player save.");
            // 通用库存通过检查后，还分别核对极速赏金账本、单人章节、教学和研究数据。几块记录必须对得上彼此的出战阶段。
            ValidateClash(save);
            ValidateCampaign(save);
            ValidateTutorial(save);
            ResearchQuestRules.Migrate(save.ResearchQuests);
            if (!ResearchQuestRules.Valid(save.ResearchQuests))
                throw new InvalidDataException("Invalid research quest progress.");
        }

        // 先整份复制档案，再尝试业务操作。有任何校验或写盘失败，内存中的 m_Save 继续指向原档。
        private SaveData Copy() { return JsonConvert.DeserializeObject<SaveData>(JsonConvert.SerializeObject(m_Save)); }

        private void Commit(SaveData save) {
            if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
            ApplyProtectionMigration(save);
            save.Format = CurrentFormat;
            Validate(save);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            // 把已经校验过的整份新档写进临时文件并刷盘。全部写好后才换正式文件，同时留下上一版备份。
            string temporary = FilePath + ".tmp";
            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(save, Formatting.Indented));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
            if (File.Exists(FilePath))
                File.Replace(temporary, FilePath, FilePath + ".bak");
            else
                File.Move(temporary, FilePath);
            // 磁盘替换成功才让调用者读到新档。不能先更新内存，否则断电后地图和仓库会看到不同结果。
            m_Save = save;
        }

        public RaidPersistenceContext.Profile Read() {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                TryMigrateProtection();
                // 用和在线、好友房一致的数据结构回快照。复杂的背包转换成 JSON，读取方拿到的是副本。
                return new RaidPersistenceContext.Profile {
                    PlayerId = m_Save.PlayerId, DisplayName = "Single-player Operator",
                    Dust = m_Save.Inventory.Count("dust"), Alloy = m_Save.Inventory.Count("alloy"),
                    Cells = m_Save.Inventory.Count("cells"),
                    InventoryJson = RaidInventoryTransport.Encode(m_Save.Inventory), MissionSchemaVersion = 1,
                    CampaignSchemaVersion = 1, CampaignJson = JsonConvert.SerializeObject(m_Save.Campaign),
                    ResearchQuests = m_Save.ResearchQuests.Copy()
                };
            }
        }

        public RecoveryInbox ReadRecovery() {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                return m_Save.Recovery.Clone();
            }
        }

        public void ClaimRecovery(RecoveryClaim request) {
            lock (m_Lock) {
                if (request == null || !Guid.TryParse(request.RequestId, out _))
                    throw new AccountClient.InventoryRequestException("recovery_invalid");
                // 把领取的请求号和请求内容一起记下来。相同请求重试只返回成功结果，重复编号却换了领取对象就报冲突。
                string key = "claim/" + request.RequestId, receipt = Receipt(request);
                if (m_Save.Receipts.TryGetValue(key, out string previous)) {
                    if (previous != receipt)
                        throw new AccountClient.InventoryRequestException("recovery_request_conflict");
                    return;
                }
                if (RaidActive) throw new AccountClient.InventoryRequestException("raid_active");
                var next = Copy();
                var error = RecoveryInventory.Claim(next.Inventory, next.Recovery, request, out var stored,
                    out var pending);
                if (error != InventoryError.None)
                    throw new AccountClient.InventoryRequestException("recovery_" + error);
                next.Inventory = stored;
                next.Recovery = pending;
                next.Receipts.Add(key, receipt);
                Commit(next);
            }
        }

        public void Move(InventoryCommand command) {
            lock (m_Lock) {
                if (RaidActive) throw new AccountClient.InventoryRequestException("raid_active");
                // 局内持有出装时拒绝仓库移动。仓库移动只改副本并用库存版本校验，通过才写回档案。
                var next = Copy();
                var error = next.Inventory.TryApply(command);
                if (error != InventoryError.None)
                    throw new AccountClient.InventoryRequestException("inventory_" + error);
                Commit(next);
            }
        }

        private static string Receipt(object payload) {
            using var hash = SHA256.Create();
            return Convert.ToBase64String(
                hash.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload))));
        }

        public void Trade(ShopCommand command) {
            lock (m_Lock) {
                if (command == null || !Guid.TryParse(command.RequestId, out _))
                    throw new AccountClient.InventoryRequestException("shop_invalid");
                // 交易回执与扣款、新库存同次保存。网络重复提交同一订单时不会再卖一次或给第二份物品。
                string key = "shop/" + command.RequestId, receipt = Receipt(command);
                if (m_Save.Receipts.TryGetValue(key, out string previous)) {
                    if (previous != receipt) throw new AccountClient.InventoryRequestException("shop_request_conflict");
                    return;
                }
                if (RaidActive) throw new AccountClient.InventoryRequestException("raid_active");
                string error = ShopRules.Apply(m_Save.Inventory, command, out var graph);
                if (error != null) throw new AccountClient.InventoryRequestException(error);
                var next = Copy();
                next.Inventory = graph;
                next.Receipts.Add(key, receipt);
                Commit(next);
            }
        }

        private void CheckPlayer(string playerId) {
            if (playerId != m_Save.PlayerId) throw new InvalidDataException("Single-player profile mismatch.");
        }

        // 出战会把带进本局的库存单独保存，并在档案里留下出战编号。接下来结束或恢复行动都凭这条记录认领同一批物品。
        public RaidPersistenceContext.Profile Deploy(RaidPersistenceContext.Deployment payload) {
            lock (m_Lock) {
                CheckPlayer(payload.PlayerId);
                if (payload.Mode != null || IsClashDeployment(m_Save, payload.DeploymentId))
                    return DeployClash(payload);
                if (payload.MatchId != null || payload.RulesVersion != 0 || payload.TeamId != 0 ||
                    m_Save.Clash?.ActiveMatchId != null)
                    throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,
                        "Invalid single-player deployment binding.");
                // 先挡掉无效编号、已结算编号与另一个出战。相同 ID、相同携带电池数的重试会读回已经锁下的出装。
                if (!Guid.TryParse(payload.DeploymentId, out _) || !RaidRules.ValidLoadout(payload.Cells) ||
                    m_Save.Receipts.ContainsKey(payload.DeploymentId) ||
                    (RaidActive && (payload.DeploymentId != m_Save.DeploymentId ||
                                    payload.Cells != m_Save.CarriedCells)))
                    throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                if (!RaidActive) {
                    if (m_Save.Recovery.ItemCount() > 0)
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,
                            "recovery_pending");
                    // 没有活动出战时才重新从仓库划装备。尚未领取的回收物要先处理，任务配发物品也要确认放得下。
                    var next = Copy();
                    SupersedeClashRegistration(next);
                    PrepareCells(next.Inventory, payload.Cells);
                    var raid = next.Inventory.ExtractLoadout();
                    if (ResearchQuestRules.Issue(raid, next.ResearchQuests) != InventoryError.None)
                        throw new RaidPersistenceContext.LoadoutRejectedException(
                            RaidLoadoutError.ResearchSuppliesNoSpace, "No space for mission-issued equipment.");
                    if (raid.Validate() != InventoryError.None ||
                        raid.CarriedWeight > InventoryCatalog.CarryWeightLimit)
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                    next.DeploymentId = payload.DeploymentId;
                    next.CarriedCells = payload.Cells;
                    // 本局连同章节内容版本一起绑定。以后回局外结算要核对这个版本，再把地图中记录的任务推进写进去。
                    next.ActiveCampaignVersion = next.Campaign.ContentVersion;
                    next.RaidInventoryJson = RaidInventoryTransport.Encode(raid);
                    Commit(next);
                }
                var profile = Read();
                profile.DeploymentId = m_Save.DeploymentId;
                profile.CarriedCells = m_Save.CarriedCells;
                profile.RaidInventoryJson = m_Save.RaidInventoryJson;
                profile.CellStackId = RaidInventoryTransport.Decode(m_Save.RaidInventoryJson).Items
                    .FirstOrDefault(i => i.Code == "cells")?.Id;
                return profile;
            }
        }

        private static void PrepareCells(InventoryGraph graph, int desired) {
            int current = graph.Count("cells", true);
            // 带得比选择的多就把多余电池拆回仓库。拆堆本身还保留每节电池的电量和来源。
            if (current > desired) {
                int remaining = current - desired;
                foreach (var source in graph.Items.Where(i => i.Code == "cells" && graph.Carried(i)).ToArray()) {
                    if (remaining == 0) break;
                    int amount = Math.Min(source.Quantity, remaining);
                    var item = BatteryEnergy.Take(source, amount);
                    if (source.Quantity == 0) graph.Items.Remove(source);
                    if (!graph.FindSpace(item, "stash", out string region, out int x, out int y))
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                    item.Parent = "stash";
                    item.Region = region;
                    item.X = x;
                    item.Y = y;
                    graph.Items.Add(item);
                    remaining -= amount;
                }
            // 带得不够就从非随身库存取差额。仓库数量够却塞不进随身容器时，也要拒绝整次出战。
            } else if (current < desired) {
                int remaining = desired - current;
                var stock = graph.Items.Where(i => i.Code == "cells" && !graph.Carried(i)).ToArray();
                if (stock.Sum(i => i.Quantity) < remaining)
                    throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.InsufficientCells);
                foreach (var source in stock) {
                    int amount = Math.Min(source.Quantity, remaining);
                    var item = BatteryEnergy.Take(source, amount);
                    if (source.Quantity == 0) graph.Items.Remove(source);
                    if (graph.AddSupply("cells", amount, foundInRaid: item.FoundInRaid, id: item.Id,
                            emergencySupply: item.EmergencySupply, cellCharge: item.CellCharge) != InventoryError.None)
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                    remaining -= amount;
                    if (remaining == 0) break;
                }
            }
        }

        // 先按结算编号核对回执，再根据撤离、死亡或超时决定物品如何处理。重复收到相同结算请求时，不能把战利品再存一遍。
        public RaidPersistenceContext.Profile Settle(RaidPersistenceContext.Settlement payload) {
            lock (m_Lock) {
                CheckPlayer(payload.PlayerId);
                if (payload.Mode != null || IsClashDeployment(m_Save, payload.SettlementId))
                    return SettleClash(payload);
                if (payload.MatchId != null || payload.RulesVersion != 0 || payload.TeamId != 0 ||
                    payload.BountyEventId != null || payload.Redemption != null || m_Save.Clash?.ActiveMatchId != null)
                    throw new InvalidDataException("Invalid single-player settlement binding.");
                string receipt = Receipt(payload);
                // 同一结算号只承认相同的最终结果。先查回执让上次已保存但没收到回应的请求安全重发。
                if (m_Save.Receipts.TryGetValue(payload.SettlementId, out string previous)) {
                    if (receipt != previous)
                        throw new InvalidDataException("Single-player settlement receipt conflict.");
                    return Read();
                }
                // 要关闭的必须是当前档案正在进行的那次出战。只支持撤出、死亡和超时三种落盘结果。
                if (payload.SettlementId != m_Save.DeploymentId || payload.DeploymentId != m_Save.DeploymentId ||
                    !(payload.Outcome == "Extracted" || payload.Outcome == "Dead" || payload.Outcome == "TimedOut"))
                    throw new InvalidDataException("Single-player settlement does not own the deployment.");
                var next = Copy();
                if (payload.Outcome == "Extracted") {
                    var raid = RaidInventoryTransport.Decode(payload.InventoryJson);
                    if (raid == null || raid.Validate() != InventoryError.None || raid.Find("stash") != null ||
                        raid.Find("loot") != null || raid.Count("dust", true) != payload.Dust ||
                        raid.Count("alloy", true) != payload.Alloy || raid.Count("cells", true) != payload.Cells)
                        throw new InvalidDataException("Invalid single-player extraction inventory.");
                    // 撤离前先核对物品来源和数量，任务样本还按物品 ID 登记。一样的样本不能借另一个请求再领取任务收益。
                    foreach (var artifact in raid.Items.Where(i =>
                                 MissionInventory.IsArtifact(i.Code) && !i.MissionRecovered))
                        if (!next.MissionArtifacts.Add(artifact.Id))
                            throw new InvalidDataException("Mission artifact was already extracted.");
                    // 先用完整携带清单合并本局研究结果，再移除本局专用配发物品。正式能带回的才交给仓库及回收箱。
                    ApplyResearchSettlement(next, payload, raid, true);
                    ResearchQuestRules.RemoveSupplies(raid);
                    var error = RecoveryInventory.Settle(next.Inventory, next.Recovery, raid, out var stored,
                        out var pending);
                    if (error != InventoryError.None)
                        throw new InvalidDataException("Unable to return single-player equipment: " + error);
                    next.Inventory = stored;
                    next.Recovery = pending;
                    ApplyCampaignExtraction(next, payload, raid);
                } else {
                    ApplyResearchSettlement(next, payload, null, false);
                    // 失败时从能拿到的最新局内库存计算保护物品。丢失部分不给仓库，研究进度按失败规则记录。
                    ReturnProtected(next,
                        RaidInventoryTransport.Decode(payload.InventoryJson ?? next.RaidInventoryJson));
                }
                next.DeploymentId = next.RaidInventoryJson = null;
                next.CarriedCells = 0;
                next.ActiveCampaignVersion = 0;
                // 关出战、更新教学、登记回执和存仓库在同次写盘中完成。重启后要么还在旧出战，要么看到完整的新结算。
                FinishTutorialAttempt(next, payload.Outcome == "Extracted");
                next.Receipts.Add(payload.SettlementId, receipt);
                Commit(next);
                return Read();
            }
        }

        public RaidPersistenceContext.Profile Abandon(RaidPersistenceContext.Deployment payload) {
            lock (m_Lock) {
                CheckPlayer(payload.PlayerId);
                if (payload.Mode != null || IsClashDeployment(m_Save, payload.DeploymentId))
                    return AbandonClash(payload);
                if (payload.MatchId != null || payload.RulesVersion != 0 || payload.TeamId != 0 ||
                    m_Save.Clash?.ActiveMatchId != null)
                    throw new InvalidDataException("Invalid single-player abandonment binding.");
                // 崩溃恢复按原出战编号放弃。已有终结回执则只读当前档案，保留之前已恢复或已结算的结果。
                if (m_Save.Receipts.ContainsKey(payload.DeploymentId)) return Read();
                if (RaidActive && m_Save.DeploymentId != payload.DeploymentId)
                    throw new InvalidDataException("Single-player deployment mismatch.");
                var next = Copy();
                if (next.RaidInventoryJson != null)
                    ReturnProtected(next, RaidInventoryTransport.Decode(next.RaidInventoryJson));
                next.DeploymentId = next.RaidInventoryJson = null;
                next.CarriedCells = 0;
                next.ActiveCampaignVersion = 0;
                FinishTutorialAttempt(next, false);
                next.Receipts.Add(payload.DeploymentId, "abandoned");
                Commit(next);
                return Read();
            }
        }

        public void RecoverAbandonedRaid() {
            lock (m_Lock) {
                if (RaidActive) Abandon(CurrentDeployment());
            }
        }

        private static void ReturnProtected(SaveData save, InventoryGraph raid) {
            // 死亡、超时或主动放弃只保留保险区和允许退还的特殊装备。任务配发物仍要清掉，留下的东西按无奖励回收流程入仓。
            var retained = SecureInventory.FailureReturn(raid);
            ResearchQuestRules.RemoveSupplies(retained);
            if (retained.Items.Count == 2) return;
            var error = RecoveryInventory.Settle(save.Inventory, save.Recovery, retained, out var stored,
                out var pending, false);
            if (error != InventoryError.None)
                throw new InvalidDataException("Unable to return protected equipment: " + error);
            save.Inventory = stored;
            save.Recovery = pending;
        }

        public RaidPersistenceContext.Profile Request(string path, object payload) {
            // 这里模拟账号后端的各条写入路径。战局调用方不必为了单人另写一套出战和结算协议。
            switch (path) {
                case "internal/sessions/resolve": return Read();
                case "internal/deployments": return Deploy((RaidPersistenceContext.Deployment)payload);
                case "internal/deployments/abandon": return Abandon((RaidPersistenceContext.Deployment)payload);
                case "internal/settlements": return Settle((RaidPersistenceContext.Settlement)payload);
                case "internal/bounty-clash/deployments": return Deploy((RaidPersistenceContext.Deployment)payload);
                case "internal/bounty-clash/deployments/abandon":
                    return Abandon((RaidPersistenceContext.Deployment)payload);
                case "internal/bounty-clash/deployments/refund":
                    return RefundClash((RaidPersistenceContext.Deployment)payload);
                case "internal/bounty-clash/settlements": return Settle((RaidPersistenceContext.Settlement)payload);
                default: throw new InvalidOperationException("Unsupported single-player persistence operation.");
            }
        }

        public void Dispose() {
            lock (m_Lock) {
                if (m_Disposed) return;
                m_Disposed = true;
                m_Lease.Dispose();
            }
        }
    }
}