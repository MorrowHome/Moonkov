using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Unity.MP_FPS.Inventory;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS
{
    // One isolated single-player profile. Every mutation commits a complete snapshot
    // before publishing it, and deployment/settlement receipts survive a restart.
    public sealed partial class OfflineProfileStore : IDisposable
    {
        private sealed class SaveData
        {
            public int Format = 4;
            public string PlayerId = Guid.NewGuid().ToString("D");
            public InventoryGraph Inventory;
            public RecoveryInbox Recovery = new RecoveryInbox();
            public string DeploymentId, RaidInventoryJson;
            public int CarriedCells;
            public Dictionary<string, string> Receipts = new Dictionary<string, string>();
            public HashSet<string> MissionArtifacts = new HashSet<string>();
            public OfflineClashState Clash;
            public CampaignProgress Campaign;
            public int ActiveCampaignVersion;
        }

        private readonly object m_Lock = new object();
        private SaveData m_Save;
        private readonly FileStream m_Lease;
        private bool m_Disposed;
        private int m_LoadingCheckpoints;
        public string FilePath { get; }
        public string OutboxDirectory => Path.Combine(Path.GetDirectoryName(FilePath), "raid-outbox");
        public bool RaidActive { get { lock (m_Lock) return m_Save.DeploymentId != null; } }

        // A single-player loading attempt owns this checkpoint until the camera
        // and replicated world are ready. Refund only after server shutdown has
        // drained its settlement tasks, so a late abandon cannot erase the kit.
        public sealed class LoadingCheckpoint
        {
            private readonly OfflineProfileStore m_Owner;
            private string m_Snapshot;
            internal LoadingCheckpoint(OfflineProfileStore owner, string snapshot)
            { m_Owner = owner; m_Snapshot = snapshot; }

            public void Complete()
            {
                lock (m_Owner.m_Lock)
                {
                    if (m_Snapshot == null) return;
                    m_Snapshot = null; m_Owner.m_LoadingCheckpoints--;
                }
            }

            public void Refund()
            {
                lock (m_Owner.m_Lock)
                {
                    if (m_Snapshot == null) return;
                    if (m_Owner.m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                    if (m_Owner.RaidActive) throw new InvalidOperationException("Wait for raid shutdown before refunding a failed load.");
                    var next = JsonConvert.DeserializeObject<SaveData>(m_Snapshot);
                    if (JsonConvert.SerializeObject(m_Owner.m_Save) != m_Snapshot)
                    {
                        // Keep terminal receipts so replaying an old journal cannot
                        // debit or settle this refunded attempt a second time.
                        foreach (var receipt in m_Owner.m_Save.Receipts)
                            if (!next.Receipts.ContainsKey(receipt.Key)) next.Receipts.Add(receipt.Key, "loading_refunded");
                        // Tracking may change while the loading screen is open.
                        // Campaign facts cannot commit before loading completes.
                        CopyCampaignTracking(m_Owner.m_Save.Campaign, next.Campaign);
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

        public LoadingCheckpoint BeginLoadingCheckpoint()
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive) throw new InvalidOperationException("The previous deployment must settle before loading another raid.");
                m_LoadingCheckpoints++;
                return new LoadingCheckpoint(this, JsonConvert.SerializeObject(m_Save));
            }
        }

        public OfflineProfileStore(string path)
        {
            FilePath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            try { m_Lease = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new IOException("Single-player save is in use or unavailable. Close other instances and retry.", ex); }
            try
            {
                if (File.Exists(FilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(FilePath);
                        ValidateCampaignJson(json);
                        m_Save = JsonConvert.DeserializeObject<SaveData>(json);
                        Validate(m_Save); TryMigrateProtection();
                        if (m_Save.Format < 4) Commit(Copy());
                    }
                    catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
                    { throw new InvalidDataException("Single-player save is invalid. The original file was preserved.", ex); }
                }
                else
                {
                    m_Save = new SaveData { Inventory = InventoryGraph.Create(), Campaign = CampaignRules.Create() };
                    m_Save.Inventory.AddSupply("dust", 100, "stash");
                    m_Save.Inventory.AddSupply("cells", 12, "stash");
                    m_Save.Inventory.AddSupply("medkit", 4, "stash");
                    Commit(m_Save);
                }
            }
            catch { m_Lease.Dispose(); throw; }
        }

        private static void Validate(SaveData save)
        {
            if(save!=null && save.Recovery==null)save.Recovery=new RecoveryInbox();
            if (save == null || (save.Format != 1 && save.Format != 2 && save.Format != 3 && save.Format != 4) || !Guid.TryParse(save.PlayerId, out _) ||
                save.Inventory?.Items == null || save.Inventory.Validate() != InventoryError.None || save.Inventory.Find("stash") == null ||
                save.Inventory.Find("loot") != null || save.Recovery.Validate(save.Inventory)!=InventoryError.None || save.Receipts == null || save.MissionArtifacts == null ||
                (save.DeploymentId != null && (!Guid.TryParse(save.DeploymentId, out _) ||
                    string.IsNullOrEmpty(save.RaidInventoryJson) || !RaidRules.ValidLoadout(save.CarriedCells))))
                throw new InvalidDataException("Unsupported or damaged single-player save.");
            ValidateClash(save);
            ValidateCampaign(save);
        }

        private SaveData Copy() => JsonConvert.DeserializeObject<SaveData>(JsonConvert.SerializeObject(m_Save));

        private void Commit(SaveData save)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
            ApplyProtectionMigration(save);
            save.Format=4;
            Validate(save);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(save, Formatting.Indented));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(data, 0, data.Length); stream.Flush(true); }
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
            else File.Move(temporary, FilePath);
            m_Save = save;
        }

        public RaidPersistenceContext.Profile Read()
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                TryMigrateProtection();
                return new RaidPersistenceContext.Profile
                {
                    PlayerId = m_Save.PlayerId, DisplayName = "Single-player Operator",
                    Dust = m_Save.Inventory.Count("dust"), Alloy = m_Save.Inventory.Count("alloy"),
                    Cells = m_Save.Inventory.Count("cells"), InventoryJson = RaidInventoryTransport.Encode(m_Save.Inventory), MissionSchemaVersion = 1,
                    CampaignSchemaVersion = 1, CampaignJson = JsonConvert.SerializeObject(m_Save.Campaign)
                };
            }
        }

        public RecoveryInbox ReadRecovery()
        {lock(m_Lock) {if(m_Disposed)throw new ObjectDisposedException(nameof(OfflineProfileStore));return m_Save.Recovery.Clone();}}
        public void ClaimRecovery(RecoveryClaim request)
        {
            lock(m_Lock)
            {
                if(request==null || !Guid.TryParse(request.RequestId,out _))throw new AccountClient.InventoryRequestException("recovery_invalid");
                string key="claim/"+request.RequestId,receipt=Receipt(request);
                if(m_Save.Receipts.TryGetValue(key,out var previous))
                {if(previous!=receipt)throw new AccountClient.InventoryRequestException("recovery_request_conflict");return;}
                if(RaidActive)throw new AccountClient.InventoryRequestException("raid_active");
                var next=Copy();var error=RecoveryInventory.Claim(next.Inventory,next.Recovery,request,out var stored,out var pending);
                if(error!=InventoryError.None)throw new AccountClient.InventoryRequestException("recovery_"+error);
                next.Inventory=stored;next.Recovery=pending;next.Receipts.Add(key,receipt);Commit(next);
            }
        }
        public void Move(InventoryCommand command)
        {
            lock (m_Lock)
            {
                if (RaidActive) throw new AccountClient.InventoryRequestException("raid_active");
                var next = Copy(); var error = next.Inventory.TryApply(command);
                if (error != InventoryError.None) throw new AccountClient.InventoryRequestException("inventory_" + error);
                Commit(next);
            }
        }

        private static string Receipt(object payload)
        {
            using var hash = SHA256.Create();
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload))));
        }

        public void Trade(ShopCommand command)
        {
            lock (m_Lock)
            {
                if (command == null || !Guid.TryParse(command.RequestId, out _)) throw new AccountClient.InventoryRequestException("shop_invalid");
                string key = "shop/" + command.RequestId, receipt = Receipt(command);
                if (m_Save.Receipts.TryGetValue(key, out var previous))
                {
                    if (previous != receipt) throw new AccountClient.InventoryRequestException("shop_request_conflict");
                    return;
                }
                if (RaidActive) throw new AccountClient.InventoryRequestException("raid_active");
                string error = ShopRules.Apply(m_Save.Inventory, command, out var graph);
                if (error != null) throw new AccountClient.InventoryRequestException(error);
                var next = Copy(); next.Inventory = graph; next.Receipts.Add(key, receipt); Commit(next);
            }
        }

        private void CheckPlayer(string playerId)
        { if (playerId != m_Save.PlayerId) throw new InvalidDataException("Single-player profile mismatch."); }

        public RaidPersistenceContext.Profile Deploy(RaidPersistenceContext.Deployment payload)
        {
            lock (m_Lock)
            {
                CheckPlayer(payload.PlayerId);
                if(payload.Mode!=null || IsClashDeployment(m_Save,payload.DeploymentId))return DeployClash(payload);
                if(payload.MatchId!=null || payload.RulesVersion!=0 || payload.TeamId!=0 || m_Save.Clash?.ActiveMatchId!=null)
                    throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,"Invalid single-player deployment binding.");
                if (!Guid.TryParse(payload.DeploymentId, out _) || !RaidRules.ValidLoadout(payload.Cells) ||
                    m_Save.Receipts.ContainsKey(payload.DeploymentId) ||
                    (RaidActive && (payload.DeploymentId != m_Save.DeploymentId || payload.Cells != m_Save.CarriedCells)))
                    throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                if (!RaidActive)
                {
                    if(m_Save.Recovery.ItemCount()>0)throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected,"recovery_pending");
                    var next = Copy(); SupersedeClashRegistration(next); PrepareCells(next.Inventory, payload.Cells);
                    var raid = next.Inventory.ExtractLoadout();
                    if (raid.Validate() != InventoryError.None || raid.CarriedWeight > InventoryCatalog.CarryWeightLimit)
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                    next.DeploymentId = payload.DeploymentId; next.CarriedCells = payload.Cells;
                    next.ActiveCampaignVersion = next.Campaign.ContentVersion;
                    next.RaidInventoryJson = RaidInventoryTransport.Encode(raid); Commit(next);
                }
                var profile = Read(); profile.DeploymentId = m_Save.DeploymentId;
                profile.CarriedCells = m_Save.CarriedCells; profile.RaidInventoryJson = m_Save.RaidInventoryJson;
                profile.CellStackId = RaidInventoryTransport.Decode(m_Save.RaidInventoryJson).Items.FirstOrDefault(i => i.Code == "cells")?.Id;
                return profile;
            }
        }

        private static void PrepareCells(InventoryGraph graph, int desired)
        {
            int current = graph.Count("cells", true);
            if (current > desired)
            {
                int remaining = current - desired;
                foreach (var source in graph.Items.Where(i => i.Code == "cells" && graph.Carried(i)).ToArray())
                {
                    if (remaining == 0) break;
                    int amount = Math.Min(source.Quantity, remaining); var item = BatteryEnergy.Take(source, amount);
                    if (source.Quantity == 0) graph.Items.Remove(source);
                    if (!graph.FindSpace(item, "stash", out var region, out var x, out var y))
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                    item.Parent = "stash"; item.Region = region; item.X = x; item.Y = y;
                    graph.Items.Add(item); remaining -= amount;
                }
            }
            else if (current < desired)
            {
                int remaining = desired - current;
                var stock = graph.Items.Where(i => i.Code == "cells" && !graph.Carried(i)).ToArray();
                if (stock.Sum(i => i.Quantity) < remaining)
                    throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.InsufficientCells);
                foreach (var source in stock)
                {
                    int amount = Math.Min(source.Quantity, remaining); var item = BatteryEnergy.Take(source, amount);
                    if (source.Quantity == 0) graph.Items.Remove(source);
                    if (graph.AddSupply("cells", amount, foundInRaid: item.FoundInRaid, id: item.Id,
                        emergencySupply: item.EmergencySupply, cellCharge: item.CellCharge) != InventoryError.None)
                        throw new RaidPersistenceContext.LoadoutRejectedException(RaidLoadoutError.Rejected);
                    remaining -= amount; if (remaining == 0) break;
                }
            }
        }

        public RaidPersistenceContext.Profile Settle(RaidPersistenceContext.Settlement payload)
        {
            lock (m_Lock)
            {
                CheckPlayer(payload.PlayerId);
                if(payload.Mode!=null || IsClashDeployment(m_Save,payload.SettlementId))return SettleClash(payload);
                if(payload.MatchId!=null || payload.RulesVersion!=0 || payload.TeamId!=0 || payload.BountyEventId!=null || payload.Redemption!=null || m_Save.Clash?.ActiveMatchId!=null)
                    throw new InvalidDataException("Invalid single-player settlement binding.");
                string receipt = Receipt(payload);
                if (m_Save.Receipts.TryGetValue(payload.SettlementId, out var previous))
                {
                    if (receipt != previous) throw new InvalidDataException("Single-player settlement receipt conflict.");
                    return Read();
                }
                if (payload.SettlementId != m_Save.DeploymentId || payload.DeploymentId != m_Save.DeploymentId ||
                    !(payload.Outcome == "Extracted" || payload.Outcome == "Dead" || payload.Outcome == "TimedOut"))
                    throw new InvalidDataException("Single-player settlement does not own the deployment.");
                var next = Copy();
                if (payload.Outcome == "Extracted")
                {
                    var raid = RaidInventoryTransport.Decode(payload.InventoryJson);
                    if (raid == null || raid.Validate() != InventoryError.None || raid.Find("stash") != null || raid.Find("loot") != null ||
                        raid.Count("dust", true) != payload.Dust || raid.Count("alloy", true) != payload.Alloy || raid.Count("cells", true) != payload.Cells)
                        throw new InvalidDataException("Invalid single-player extraction inventory.");
                    foreach (var artifact in raid.Items.Where(i => MissionInventory.IsArtifact(i.Code) && !i.MissionRecovered))
                        if (!next.MissionArtifacts.Add(artifact.Id)) throw new InvalidDataException("Mission artifact was already extracted.");
                    var error=RecoveryInventory.Settle(next.Inventory,next.Recovery,raid,out var stored,out var pending);
                    if(error!=InventoryError.None)throw new InvalidDataException("Unable to return single-player equipment: "+error);
                    next.Inventory=stored;next.Recovery=pending;
                    ApplyCampaignExtraction(next, payload, raid);
                }
                else ReturnProtected(next, RaidInventoryTransport.Decode(payload.InventoryJson ?? next.RaidInventoryJson));
                next.DeploymentId = next.RaidInventoryJson = null; next.CarriedCells = 0; next.ActiveCampaignVersion = 0;
                next.Receipts.Add(payload.SettlementId, receipt); Commit(next); return Read();
            }
        }

        public RaidPersistenceContext.Profile Abandon(RaidPersistenceContext.Deployment payload)
        {
            lock (m_Lock)
            {
                CheckPlayer(payload.PlayerId);
                if(payload.Mode!=null || IsClashDeployment(m_Save,payload.DeploymentId))return AbandonClash(payload);
                if(payload.MatchId!=null || payload.RulesVersion!=0 || payload.TeamId!=0 || m_Save.Clash?.ActiveMatchId!=null)
                    throw new InvalidDataException("Invalid single-player abandonment binding.");
                if (m_Save.Receipts.ContainsKey(payload.DeploymentId)) return Read();
                if (RaidActive && m_Save.DeploymentId != payload.DeploymentId) throw new InvalidDataException("Single-player deployment mismatch.");
                var next = Copy();
                if (next.RaidInventoryJson != null) ReturnProtected(next, RaidInventoryTransport.Decode(next.RaidInventoryJson));
                next.DeploymentId = next.RaidInventoryJson = null; next.CarriedCells = 0; next.ActiveCampaignVersion = 0;
                next.Receipts.Add(payload.DeploymentId, "abandoned"); Commit(next); return Read();
            }
        }

        public void RecoverAbandonedRaid()
        {
            lock (m_Lock)
                if (RaidActive) Abandon(CurrentDeployment());
        }

        private static void ReturnProtected(SaveData save, InventoryGraph raid)
        {
            var retained = SecureInventory.FailureReturn(raid);
            if (retained.Items.Count == 2) return;
            var error = RecoveryInventory.Settle(save.Inventory, save.Recovery, retained, out var stored, out var pending, missionRewards: false);
            if (error != InventoryError.None) throw new InvalidDataException("Unable to return protected equipment: " + error);
            save.Inventory = stored; save.Recovery = pending;
        }

        public RaidPersistenceContext.Profile Request(string path, object payload)
        {
            switch (path)
            {
                case "internal/sessions/resolve": return Read();
                case "internal/deployments": return Deploy((RaidPersistenceContext.Deployment)payload);
                case "internal/deployments/abandon": return Abandon((RaidPersistenceContext.Deployment)payload);
                case "internal/settlements": return Settle((RaidPersistenceContext.Settlement)payload);
                case "internal/bounty-clash/deployments": return Deploy((RaidPersistenceContext.Deployment)payload);
                case "internal/bounty-clash/deployments/abandon": return Abandon((RaidPersistenceContext.Deployment)payload);
                case "internal/bounty-clash/deployments/refund": return RefundClash((RaidPersistenceContext.Deployment)payload);
                case "internal/bounty-clash/settlements": return Settle((RaidPersistenceContext.Settlement)payload);
                default: throw new InvalidOperationException("Unsupported single-player persistence operation.");
            }
        }

        public void Dispose()
        {
            lock (m_Lock) { if (m_Disposed) return; m_Disposed = true; m_Lease.Dispose(); }
        }
    }
}
