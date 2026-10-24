using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Owned by the server World. Worker tasks contain data only; ECS access stays on the main thread.
    public sealed partial class RaidPersistenceContext : IComponentData, IDisposable
    {
        [Serializable] private sealed class Config
        {
            public string BackendUrl;
            public string ServerKey;
            public string OutboxDirectory = "LocalData/raid-outbox";
        }

        public sealed class Profile
        {
            public string PlayerId;
            public string DisplayName;
            public int Dust, Alloy, Cells;
            public string DeploymentId, CellStackId;
            public int CarriedCells;
            public string InventoryJson, RaidInventoryJson;
            public int MissionSchemaVersion;
            public string Mode, MatchId; public int RulesVersion, TeamId;
        }

        public sealed class Settlement
        {
            public string PlayerId, SettlementId, Outcome, DeploymentId;
            public int Dust, Alloy, Cells;
            public string InventoryJson;
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string Mode;
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string MatchId;
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string BountyEventId;
            [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public int RulesVersion;
            [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public int TeamId;
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public BountyRedemption Redemption;
        }

        public sealed class Join
        {
            public FixedString64Bytes Name;
            public int CharacterIndex;
            public Task<Profile> Task;
            public string Token;
            public bool NeedsRefresh;
            public int CarryCells;
        }

        public sealed class Deployment
        {
            public string PlayerId, DeploymentId;
            public int Cells;
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string Mode;
            [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string MatchId;
            [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public int RulesVersion;
            [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public int TeamId;
            [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public bool Refund;
        }

        public sealed class Deploy
        {
            public Entity Connection;
            public Join Join;
            public Deployment Payload;
            public Task<Profile> Task;
            public double RetryAt;
        }

        public sealed class LoadoutRejectedException : Exception
        {
            public readonly RaidLoadoutError Error;
            public LoadoutRejectedException(RaidLoadoutError error, string message = null) : base(message) { Error = error; }
        }

        public sealed class Save
        {
            public Entity Connection;
            public int RaidId;
            public Settlement Payload;
            public Task<Profile> Task;
            public double RetryAt;
            public bool Warned;
            public Deployment AbandonedDeployment;
        }

        public readonly Dictionary<Entity, Join> Joins = new Dictionary<Entity, Join>();
        public readonly Dictionary<string, Save> Saves = new Dictionary<string, Save>();
        public readonly Dictionary<Entity, Profile> Profiles = new Dictionary<Entity, Profile>();
        public readonly Dictionary<Entity, Deploy> Deployments = new Dictionary<Entity, Deploy>();
        public readonly Dictionary<Entity, RaidSession> ReadyRaids = new Dictionary<Entity, RaidSession>();
        public readonly Dictionary<Entity, RaidInventoryState> Inventories = new Dictionary<Entity, RaidInventoryState>();
        public bool Enabled => m_Http != null || m_Offline != null || m_Lan != null;
        public bool MissionsSupported
        {
            get
            {
                if (m_Offline != null || m_Lan != null || !Enabled) return true;
                if (Profiles.Count == 0) return false;
                foreach (var profile in Profiles.Values) if (profile.MissionSchemaVersion < 1) return false;
                return true;
            }
        }
        private readonly OfflineProfileStore m_Offline;
        private readonly LanPersistence m_Lan;
        private readonly HttpClient m_Http;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly string m_Outbox;
        private bool m_Disposed;
        public Task ShutdownTask { get; private set; } = Task.CompletedTask;

        public RaidPersistenceContext() : this((string)null) { }

        public RaidPersistenceContext(OfflineProfileStore store)
        {
            m_Offline = store ?? throw new ArgumentNullException(nameof(store));
            m_Outbox = store.OutboxDirectory;
            Directory.CreateDirectory(m_Outbox);
            ReplayOutbox();
        }

        public RaidPersistenceContext(LanPersistence lan)
        {
            m_Lan = lan ?? throw new ArgumentNullException(nameof(lan));
            m_Outbox = lan.OutboxDirectory;
            m_ClashCapability = Task.FromResult(true);
            ReplayOutbox();
        }

        public RaidPersistenceContext(string configPath, HttpMessageHandler handler = null)
        {
            // Secrets are outside Assets and are never read by client Worlds.
            string root = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(configPath)) configPath = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
            if (string.IsNullOrEmpty(configPath)) configPath = Path.Combine(root, "moon-server.local.json");
            if (!File.Exists(configPath))
            {
                throw new InvalidOperationException("Online server requires moon-server.local.json (or MOON_SERVER_CONFIG). Use the explicit single-player or LAN room entry for local play.");
            }
            var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
            if (config == null || string.IsNullOrEmpty(config.ServerKey) || config.ServerKey.Length < 32 ||
                !Uri.TryCreate(config.BackendUrl, UriKind.Absolute, out var url) ||
                (url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback)))
                throw new InvalidOperationException("Invalid moon server config: use a server key and HTTPS (HTTP is allowed on loopback only).");
            m_Outbox = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)), config.OutboxDirectory));
            Directory.CreateDirectory(m_Outbox);
            m_Http = handler == null ? new HttpClient() : new HttpClient(handler);
            m_Http.BaseAddress = url; m_Http.Timeout = TimeSpan.FromSeconds(10);
            m_Http.DefaultRequestHeaders.Add("X-Moon-Server-Key", config.ServerKey);
            m_ClashCapability=CheckClashCapabilityAsync();
            ReplayOutbox();
        }

        private void ReplayOutbox()
        {
            // Replay receipts retained across a normal restart or a lost HTTP acknowledgement.
            ReplayBountyFinalizations();
            foreach (string file in Directory.GetFiles(m_Outbox, "*.json"))
            {
                if(Guid.TryParse(Path.GetFileNameWithoutExtension(file),out var receiptId) && Saves.ContainsKey(receiptId.ToString("D"))) continue;
                var payload = JsonConvert.DeserializeObject<Settlement>(File.ReadAllText(file));
                if (payload == null || !Guid.TryParse(payload.SettlementId, out _) || !Guid.TryParse(payload.PlayerId, out _))
                    throw new InvalidDataException("Invalid raid outbox file: " + Path.GetFileName(file));
                if (Saves.ContainsKey(payload.SettlementId)) continue;
                var save = new Save { Payload = payload };
                save.Task = SaveAsync(payload);
                Saves.Add(payload.SettlementId, save);
            }
            foreach (string file in Directory.GetFiles(m_Outbox,"*.refund"))
            {
                var payload=JsonConvert.DeserializeObject<Deployment>(File.ReadAllText(file));
                if(payload==null || payload.Mode!="BountyClash" || !Guid.TryParse(payload.DeploymentId,out _)) throw new InvalidDataException("Invalid deployment refund journal.");
                payload.Refund=true;Abandon(payload);
            }
            foreach (string file in Directory.GetFiles(m_Outbox, "*.deploy"))
            {
                var payload = JsonConvert.DeserializeObject<Deployment>(File.ReadAllText(file));
                if (payload == null || !Guid.TryParse(payload.DeploymentId, out _) || !Guid.TryParse(payload.PlayerId, out _) || !RaidRules.ValidLoadout(payload.Cells))
                    throw new InvalidDataException("Invalid deployment journal: " + Path.GetFileName(file));
                // A completed raid's exact settlement takes precedence over crash recovery.
                if (!Saves.ContainsKey(payload.DeploymentId)) Abandon(payload);
            }
            Debug.Log("[Raid] Persistent stash enabled. Pending receipts: " + Saves.Count);
        }

        public void BeginJoin(Entity connection, FixedString64Bytes name, int characterIndex, string token, int carryCells = 0)
        {
            if (Joins.ContainsKey(connection) || Profiles.ContainsKey(connection)) return;
            Joins.Add(connection, new Join
            {
                Name = name, CharacterIndex = characterIndex, Token = token, CarryCells = carryCells,
                Task = ResolveJoinAsync(token)
            });
        }

        public void RefreshJoin(Join join)
        {
            join.NeedsRefresh = false;
            join.Task = m_Http == null ? ResolveJoinAsync(join.Token) : RefreshAuthenticatedJoinAsync(join.Task.Result.PlayerId);
        }

        private async Task<Profile> RefreshAuthenticatedJoinAsync(string playerId)
        {
            using (var response = await m_Http.GetAsync("internal/accounts/" + Guid.Parse(playerId).ToString("D"), m_Stop.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        public void BeginSave(Entity connection, RaidSession session)
        {
            if (Saves.TryGetValue(session.SettlementId.ToString(),out var pending))
            { pending.Connection=connection;pending.RaidId=session.RaidId;return; }
            var profile = Profiles[connection];
            var payload = new Settlement
            {
                PlayerId = profile.PlayerId, SettlementId = session.SettlementId.ToString(),
                Mode=profile.Mode,MatchId=profile.MatchId,RulesVersion=profile.RulesVersion,TeamId=profile.TeamId,
                Outcome = session.Phase.ToString(), Dust = session.Dust, Alloy = session.Alloy, Cells = session.Cells,
                DeploymentId = session.PersistentDeployment ? session.SettlementId.ToString() : null,
                InventoryJson = Inventories.TryGetValue(connection, out var inventory) ? JsonConvert.SerializeObject(inventory.Graph) : null
            };
            Saves.Add(payload.SettlementId, new Save
            {
                Connection = connection, RaidId = session.RaidId, Payload = payload, Task = SaveAsync(payload)
            });
        }

        public void BeginDeploy(Entity connection, int cells, Join join = null)
        {
            var payload = new Deployment { PlayerId = Profiles[connection].PlayerId, DeploymentId = Guid.NewGuid().ToString("D"), Cells = cells };
            Deployments.Add(connection, new Deploy { Connection=connection, Join=join, Payload=payload, Task=DeployAsync(payload) });
        }

        private string DeploymentFile(Deployment payload) => Path.Combine(m_Outbox, Guid.Parse(payload.DeploymentId).ToString("N") + ".deploy");

        private static void WriteJournal(string file, object payload)
        {
            if (File.Exists(file))
            {
                if(!Newtonsoft.Json.Linq.JToken.DeepEquals(Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(file)),Newtonsoft.Json.Linq.JToken.FromObject(payload)))
                    throw new InvalidDataException("Outbox receipt conflicts with its immutable payload.");
                return;
            }
            string temporary = file + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temporary, file);
        }

        public Task<Profile> DeployAsync(Deployment payload) => Task.Run(async () =>
        {
            await AwaitBountyRegistrationAsync(payload).ConfigureAwait(false);
            WriteJournal(DeploymentFile(payload), payload);
            var profile = await PostAsync(ProfilePath("deployments",payload.Mode), payload).ConfigureAwait(false);
            if (profile.Mode!=payload.Mode || profile.MatchId!=payload.MatchId || profile.RulesVersion!=payload.RulesVersion || profile.TeamId!=payload.TeamId ||
                profile.PlayerId != payload.PlayerId || profile.DeploymentId != payload.DeploymentId || profile.CarriedCells != payload.Cells ||
                (payload.Cells > 0 && !Guid.TryParse(profile.CellStackId, out _)))
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected, "Deployment response mismatch.");
            Inventory.InventoryGraph graph;
            try { graph = string.IsNullOrEmpty(profile.RaidInventoryJson) ? null : RaidInventoryTransport.Decode(profile.RaidInventoryJson); }
            catch (JsonException) { throw new LoadoutRejectedException(RaidLoadoutError.Rejected, "Malformed deployment inventory."); }
            if (graph == null || graph.Find("stash") != null || graph.Find("loot")!=null || graph.Validate() != Inventory.InventoryError.None || graph.Count("cells",true) != payload.Cells)
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected, "Deployment inventory missing or invalid; update the persistence service.");
            return profile;
        });

        public void Abandon(Deployment payload, bool startImmediately = true)
        {
            if (Saves.ContainsKey(payload.DeploymentId)) return;
            var save = new Save { Payload = new Settlement { PlayerId=payload.PlayerId, SettlementId=payload.DeploymentId }, AbandonedDeployment=payload };
            if (startImmediately) save.Task = RetrySave(save);
            Saves.Add(payload.DeploymentId, save);
        }

        public Task<Profile> RetrySave(Save save) => save.AbandonedDeployment == null ? SaveAsync(save.Payload) : Task.Run(async () =>
        {
            var profile = await PostAsync(ProfilePath(save.AbandonedDeployment.Refund ? "deployments/refund" : "deployments/abandon",save.AbandonedDeployment.Mode), save.AbandonedDeployment).ConfigureAwait(false);
            if (profile.PlayerId != save.Payload.PlayerId) throw new InvalidDataException("Recovery profile mismatch.");
            File.Delete(DeploymentFile(save.AbandonedDeployment));
            File.Delete(Path.ChangeExtension(DeploymentFile(save.AbandonedDeployment),".refund"));
            return profile;
        });

        public Task<Profile> SaveAsync(Settlement payload)
        {
            // One tiny local receipt per completed raid; the frame never waits for disk or HTTP.
            return Task.Run(async () =>
            {
                string file = Path.Combine(m_Outbox, Guid.Parse(payload.SettlementId).ToString("N") + ".json");
                WriteJournal(file, payload);
                await RedeemBeforeSaveAsync(payload).ConfigureAwait(false);
                var profile = await PostAsync(ProfilePath("settlements",payload.Mode), payload).ConfigureAwait(false);
                if (profile.PlayerId != payload.PlayerId) throw new InvalidDataException("Settlement profile mismatch.");
                // Delete the open-raid journal first. A crash between deletes then replays the exact receipt.
                File.Delete(Path.ChangeExtension(file, ".deploy"));
                File.Delete(file);
                return profile;
            });
        }

        private async Task<Profile> PostAsync(string path, object payload)
        {
            if (m_Offline != null) return m_Offline.Request(path, payload);
            if (m_Lan != null) return m_Lan.Request(path, payload);
            using (var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"))
            using (var response = await m_Http.PostAsync(path, content, m_Stop.Token).ConfigureAwait(false))
            {
                // Do not log request bodies: guest tokens and server keys are credentials.
                if (path.EndsWith("/deployments",StringComparison.Ordinal) && (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new LoadoutRejectedException(body.Contains("insufficient_cells") ? RaidLoadoutError.InsufficientCells : RaidLoadoutError.Rejected);
                }
                response.EnsureSuccessStatusCode();
                var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (profile == null || !Guid.TryParse(profile.PlayerId, out _) || profile.Dust < 0 || profile.Alloy < 0 || profile.Cells < 0)
                    throw new InvalidDataException("Invalid persistence response.");
                return profile;
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (!Enabled) { m_Stop.Dispose(); return; }
            // The World disappears before disconnect processing necessarily gets another tick.
            // Capture data now; cleanup must not query ECS or await HTTP on the main thread.
            foreach (var profile in Profiles.Values)
                if (!string.IsNullOrEmpty(profile.DeploymentId))
                    Abandon(DeploymentOf(profile), startImmediately:false);
            var deploying = new List<Task<Profile>>();
            foreach (var deploy in Deployments.Values)
            {
                if (deploy.Task != null) deploying.Add(deploy.Task);
                if(deploy.Payload.Mode=="BountyClash") RefundBountyDeployment(deploy.Payload,deploy.Task);
                else Abandon(deploy.Payload, startImmediately:false);
            }
            var saves = new List<Save>(Saves.Values);
            m_Stop.CancelAfter(TimeSpan.FromSeconds(20));
            ShutdownTask = FlushShutdownAsync(deploying, saves);
        }

        private async Task FlushShutdownAsync(List<Task<Profile>> deploying, List<Save> saves)
        {
            try
            {
                // A pending debit must resolve before its cancellation is acknowledged locally.
                foreach (var task in deploying) { try { await task.ConfigureAwait(false); } catch (Exception) { /* journal retained */ } }
                foreach (var save in saves)
                {
                    try
                    {
                        if (save.Task != null) { try { await save.Task.ConfigureAwait(false); } catch (Exception) { /* retry exact payload */ } }
                        if (save.Task == null || !save.Task.IsCompletedSuccessfully)
                            await RetrySave(save).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Raid] Shutdown receipt remains in outbox for recovery: " + ex.GetType().Name);
                    }
                }
                CleanBountyJournals();
            }
            finally { m_Http?.Dispose(); m_Stop.Dispose(); }
        }
    }
}
