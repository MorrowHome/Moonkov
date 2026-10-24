using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;

namespace Unity.MP_FPS
{
    public struct OnlineRaidMember : IComponentData { public int Team; }
    public sealed class OnlineRaidWorld : IComponentData
    {
        private readonly System.Collections.Generic.Dictionary<int, LocalTransform> m_TeamSpawns = new System.Collections.Generic.Dictionary<int, LocalTransform>();
        private readonly System.Collections.Generic.Dictionary<int, int> m_TeamCounts = new System.Collections.Generic.Dictionary<int, int>();
        public readonly System.Collections.Generic.HashSet<string> DeployedAccounts = new System.Collections.Generic.HashSet<string>();
        public LocalTransform Spawn(int team, LocalTransform candidate)
        {
            if (!m_TeamSpawns.TryGetValue(team, out var origin)) m_TeamSpawns.Add(team, origin = candidate);
            m_TeamCounts.TryGetValue(team, out int slot); m_TeamCounts[team] = slot + 1;
            origin.Position += new Unity.Mathematics.float3(slot % 2 * 1.8f, 0, slot / 2 * 1.8f);
            return origin;
        }
    }
    public static class OnlineInstanceServer
    {
        public static string InstanceId => Environment.GetEnvironmentVariable("MOON_INSTANCE_ID");
        public static bool Managed => !string.IsNullOrEmpty(InstanceId);
        private sealed class Config { public string BackendUrl, ServerKey; }
        public static async Task AnnounceReadyAsync()
        {
            if (!Managed) return;
            string path = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
            var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
            using var http = new HttpClient { BaseAddress = new Uri(config.BackendUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Moon-Server-Key", config.ServerKey);
            using var content = new StringContent(JsonConvert.SerializeObject(new Online.InstanceEvent { InstanceId = InstanceId, Kind = "ready", Protocol = 1 }), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("internal/instances/event", content);
            response.EnsureSuccessStatusCode();
            Debug.Log("[Online] Fresh instance ready: " + InstanceId);
        }
    }
}
