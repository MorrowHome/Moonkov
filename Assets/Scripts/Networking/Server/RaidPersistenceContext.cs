using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
        }

        public sealed class Settlement
        {
            public string PlayerId, SettlementId, Outcome;
            public int Dust, Alloy, Cells;
        }

        public sealed class Join
        {
            public FixedString64Bytes Name;
            public int CharacterIndex;
            public Task<Profile> Task;
            public string Token;
            public bool NeedsRefresh;
        }

        public sealed class Save
        {
            public Entity Connection;
            public int RaidId;
            public Settlement Payload;
            public Task<Profile> Task;
            public double RetryAt;
            public bool Warned;
        }

        public readonly Dictionary<Entity, Join> Joins = new Dictionary<Entity, Join>();
        public readonly Dictionary<string, Save> Saves = new Dictionary<string, Save>();
        public readonly Dictionary<Entity, Profile> Profiles = new Dictionary<Entity, Profile>();
        public bool Enabled => m_Http != null;
        private readonly HttpClient m_Http;
        private readonly CancellationTokenSource m_Stop = new CancellationTokenSource();
        private readonly string m_Outbox;

        public RaidPersistenceContext()
        {
            // Secrets are outside Assets and are never read by client Worlds.
            string root = Path.GetDirectoryName(Application.dataPath);
            string configPath = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
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
            m_Http = new HttpClient { BaseAddress = url, Timeout = TimeSpan.FromSeconds(10) };
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
            Debug.Log("[Raid] Persistent stash enabled. Pending receipts: " + Saves.Count);
        }

        public void BeginJoin(Entity connection, FixedString64Bytes name, int characterIndex, string token)
        {
            if (Joins.ContainsKey(connection) || Profiles.ContainsKey(connection)) return;
            Joins.Add(connection, new Join
            {
                Name = name, CharacterIndex = characterIndex, Token = token,
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
                Outcome = session.Phase.ToString(), Dust = session.Dust, Alloy = session.Alloy, Cells = session.Cells
            };
            Saves.Add(payload.SettlementId, new Save
            {
                Connection = connection, RaidId = session.RaidId, Payload = payload, Task = SaveAsync(payload)
            });
        }

        public Task<Profile> SaveAsync(Settlement payload)
        {
            // One tiny local receipt per completed raid; the frame never waits for disk or HTTP.
            return Task.Run(async () =>
            {
                string file = Path.Combine(m_Outbox, Guid.Parse(payload.SettlementId).ToString("N") + ".json");
                if (!File.Exists(file))
                {
                    string temporary = file + ".tmp";
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    File.Move(temporary, file);
                }
                var profile = await PostAsync("internal/settlements", payload).ConfigureAwait(false);
                if (profile.PlayerId != payload.PlayerId) throw new InvalidDataException("Settlement profile mismatch.");
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
                response.EnsureSuccessStatusCode();
                var profile = JsonConvert.DeserializeObject<Profile>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (profile == null || !Guid.TryParse(profile.PlayerId, out _) || profile.Dust < 0 || profile.Alloy < 0 || profile.Cells < 0)
                    throw new InvalidDataException("Invalid persistence response.");
                return profile;
            }
        }

        public void Dispose()
        {
            m_Stop.Cancel();
            m_Http?.Dispose();
            // Workers may still read the token while unwinding; keep its source alive until they finish.
        }
    }
}
