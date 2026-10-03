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
    public sealed class RaidPersistenceContext : IComponentData, IDisposable
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
        }

        public sealed class Settlement
        {
            public string PlayerId, SettlementId, Outcome, DeploymentId;
            public int Dust, Alloy, Cells;
            public string InventoryJson;
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
        public bool Enabled => m_Http != null;
        private readonly HttpClient m_Http;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly string m_Outbox;
        private bool m_Disposed;
        public Task ShutdownTask { get; private set; } = Task.CompletedTask;

        public RaidPersistenceContext() : this(null) { }

        public RaidPersistenceContext(string configPath, HttpMessageHandler handler = null)
        {
            // Secrets are outside Assets and are never read by client Worlds.
            string root = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(configPath)) configPath = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
            if (string.IsNullOrEmpty(configPath)) configPath = Path.Combine(root, "moon-server.local.json");
            if (!File.Exists(configPath))
            {
                Debug.Log("[Raid] No server persistence configuration: stash is session-only.");
                return;
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
            // Replay receipts retained across a normal restart or a lost HTTP acknowledgement.
            foreach (string file in Directory.GetFiles(m_Outbox, "*.json"))
            {
                var payload = JsonConvert.DeserializeObject<Settlement>(File.ReadAllText(file));
                if (payload == null || !Guid.TryParse(payload.SettlementId, out _) || !Guid.TryParse(payload.PlayerId, out _))
                    throw new InvalidDataException("Invalid raid outbox file: " + Path.GetFileName(file));
                var save = new Save { Payload = payload };
                save.Task = SaveAsync(payload);
                Saves.Add(payload.SettlementId, save);
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
                Task = PostAsync("internal/sessions/resolve", new { Token = token })
            });
        }

        public void RefreshJoin(Join join)
        {
            join.NeedsRefresh = false;
            join.Task = PostAsync("internal/sessions/resolve", new { Token = join.Token });
        }

        public void BeginSave(Entity connection, RaidSession session)
        {
            var profile = Profiles[connection];
            var payload = new Settlement
            {
                PlayerId = profile.PlayerId, SettlementId = session.SettlementId.ToString(),
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
            if (File.Exists(file)) return;
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
            WriteJournal(DeploymentFile(payload), payload);
            var profile = await PostAsync("internal/deployments", payload).ConfigureAwait(false);
            if (profile.PlayerId != payload.PlayerId || profile.DeploymentId != payload.DeploymentId || profile.CarriedCells != payload.Cells ||
                (payload.Cells > 0 && !Guid.TryParse(profile.CellStackId, out _)))
                throw new LoadoutRejectedException(RaidLoadoutError.Rejected, "Deployment response mismatch.");
            Inventory.InventoryGraph graph;
            try { graph = string.IsNullOrEmpty(profile.RaidInventoryJson) ? null : RaidInventoryTransport.Decode(profile.RaidInventoryJson); }
            catch (JsonException) { throw new LoadoutRejectedException(RaidLoadoutError.Rejected, "Malformed deployment inventory."); }
            if (graph == null || graph.Find("stash") != null || graph.Validate() != Inventory.InventoryError.None || graph.Count("cells",true) != payload.Cells)
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
            var profile = await PostAsync("internal/deployments/abandon", save.AbandonedDeployment).ConfigureAwait(false);
            if (profile.PlayerId != save.Payload.PlayerId) throw new InvalidDataException("Recovery profile mismatch.");
            File.Delete(DeploymentFile(save.AbandonedDeployment));
            return profile;
        });

        public Task<Profile> SaveAsync(Settlement payload)
        {
            // One tiny local receipt per completed raid; the frame never waits for disk or HTTP.
            return Task.Run(async () =>
            {
                string file = Path.Combine(m_Outbox, Guid.Parse(payload.SettlementId).ToString("N") + ".json");
                WriteJournal(file, payload);
                var profile = await PostAsync("internal/settlements", payload).ConfigureAwait(false);
                if (profile.PlayerId != payload.PlayerId) throw new InvalidDataException("Settlement profile mismatch.");
                // Delete the open-raid journal first. A crash between deletes then replays the exact receipt.
                File.Delete(Path.ChangeExtension(file, ".deploy"));
                File.Delete(file);
                return profile;
            });
        }

        private async Task<Profile> PostAsync(string path, object payload)
        {
            using (var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"))
            using (var response = await m_Http.PostAsync(path, content, m_Stop.Token).ConfigureAwait(false))
            {
                // Do not log request bodies: guest tokens and server keys are credentials.
                if (path == "internal/deployments" && (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound))
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
                    Abandon(new Deployment { PlayerId=profile.PlayerId, DeploymentId=profile.DeploymentId, Cells=profile.CarriedCells }, startImmediately:false);
            var deploying = new List<Task<Profile>>();
            foreach (var deploy in Deployments.Values)
            {
                if (deploy.Task != null) deploying.Add(deploy.Task);
                Abandon(deploy.Payload, startImmediately:false);
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
            }
            finally { m_Http.Dispose(); m_Stop.Dispose(); }
        }
    }
}
