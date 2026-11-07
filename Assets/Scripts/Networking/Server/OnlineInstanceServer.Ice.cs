using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.MP_FPS.EmbeddedNetworking;
using Unity.MP_FPS.Online;
using UnityEngine;

namespace Unity.MP_FPS
{
    // The account backend only exchanges SDP. All raid datagrams use the native
    // ICE agents owned by this process's NetCode driver.
    internal static class OnlineIceServer
    {
        sealed class Config { public string BackendUrl, ServerKey; }
        sealed class Peer
        {
            public string Player, Attempt;
            public int Id;
            public bool PublicOnly, Answered, Connected;
            public double Deadline;
        }
        static EmbeddedIceTransport s_Transport;
        static CancellationTokenSource s_Stop;
        static Task s_Loop;
        public static string BindAddress { get; private set; }
        public static string HostId { get; private set; }
        public static bool Selected => OnlineInstanceServer.Managed &&
            Environment.GetEnvironmentVariable("MOON_INSTANCE_CONNECTION") == OnlineNetworkProtocol.P2P;
        public static bool Available => Selected && s_Transport != null && !s_Transport.IsDisposed;
        internal static EmbeddedIceTransport DiagnosticTransport => Available ? s_Transport : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            s_Stop?.Cancel();
            s_Transport?.Dispose();
            s_Transport = null; BindAddress = HostId = null;
        }
        public static EmbeddedIceTransport CreateTransport()
        {
            if (!Selected) throw new InvalidOperationException("This instance did not select P2P.");
            if (Available) throw new InvalidOperationException("P2P server transport is already owned by a driver.");
            BindAddress = EmbeddedIceNetwork.PhysicalAddress(Environment.GetEnvironmentVariable("MOON_ICE_BIND_ADDRESS"));
            HostId = EmbeddedIceNetwork.HostId(OnlineInstanceServer.InstanceId);
            s_Transport = new EmbeddedIceTransport(1024);
            return s_Transport;
        }
        public static void Start()
        {
            if (!Available || s_Loop != null && !s_Loop.IsCompleted) return;
            s_Stop?.Dispose();
            s_Stop = new CancellationTokenSource();
            Application.quitting -= Stop;
            Application.quitting += Stop;
            s_Loop = RunAsync(s_Stop.Token);
        }
        static void Stop() => s_Stop?.Cancel();
        static async Task RunAsync(CancellationToken ct)
        {
            var transport = s_Transport;
            var peers = new Dictionary<string, Peer>();
            try
            {
                string configPath = Environment.GetEnvironmentVariable("MOON_SERVER_CONFIG");
                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                using var http = new HttpClient { BaseAddress = new Uri(config.BackendUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.Add("X-Moon-Server-Key", config.ServerKey);
                string stunHost = Environment.GetEnvironmentVariable("MOON_ICE_STUN_HOST") ?? "stun.cloudflare.com";
                ushort stunPort = ushort.TryParse(Environment.GetEnvironmentVariable("MOON_ICE_STUN_PORT"), out var value) ? value : (ushort)3478;
                string stun = null;
                double nextWarning = 0;
                while (!ct.IsCancellationRequested && !transport.IsDisposed)
                {
                    try
                    {
                        if (stun == null) stun = await EmbeddedIceNetwork.ResolveStunAsync(stunHost, ct);
                        var answers = new List<IceAnswer>();
                        foreach (var peer in peers.Values.ToArray())
                        {
                            var state = transport.GetState(peer.Id);
                            if (EmbeddedIceNetwork.Connected(state)) peer.Connected = true;
                            if (!peer.Answered)
                            {
                                if (transport.IsGatheringComplete(peer.Id))
                                {
                                    try { answers.Add(new IceAnswer { PlayerId = peer.Player, AttemptId = peer.Attempt,
                                        Description = EmbeddedIceNetwork.Description(transport.GetLocalDescription(peer.Id), peer.PublicOnly) }); }
                                    catch (InvalidOperationException) { answers.Add(new IceAnswer { PlayerId = peer.Player, AttemptId = peer.Attempt, Error = "no-public-candidate" }); }
                                }
                                else if (Time.realtimeSinceStartupAsDouble > peer.Deadline)
                                    answers.Add(new IceAnswer { PlayerId = peer.Player, AttemptId = peer.Attempt, Error = "gather-timeout" });
                            }
                            if (state == EmbeddedIceState.Failed || !peer.Connected && peer.Answered && Time.realtimeSinceStartupAsDouble > peer.Deadline)
                            { transport.RemovePeer(peer.Id); peers.Remove(peer.Player); }
                        }
                        using var content = new StringContent(JsonConvert.SerializeObject(new IceServerPoll
                        { InstanceId = OnlineInstanceServer.InstanceId, Answers = answers.ToArray() }), Encoding.UTF8, "application/json");
                        using var response = await http.PostAsync("internal/instances/ice", content, ct);
                        response.EnsureSuccessStatusCode();
                        var inbox = JsonConvert.DeserializeObject<IceServerInbox>(await response.Content.ReadAsStringAsync());
                        ct.ThrowIfCancellationRequested();
                        foreach (var answer in answers)
                            if (peers.TryGetValue(answer.PlayerId, out var answered) && answered.Attempt == answer.AttemptId) answered.Answered = true;
                        foreach (var offer in inbox?.Offers ?? Array.Empty<IceOffer>())
                        {
                            peers.TryGetValue(offer.PlayerId, out var current);
                            if (current != null && (offer.Cancelled || current.Attempt != offer.AttemptId))
                            { transport.RemovePeer(current.Id); peers.Remove(current.Player); current = null; }
                            if (offer.Cancelled || current != null) continue;
                            var peer = new Peer { Player = offer.PlayerId, Attempt = offer.AttemptId, PublicOnly = offer.PublicOnly,
                                Deadline = Time.realtimeSinceStartupAsDouble + 30 };
                            peer.Id = transport.AddPeer(BindAddress, stun, stunPort);
                            peers.Add(peer.Player, peer);
                            // Both sides suppress private/local candidates for the host-PC client.
                            transport.SetRemoteDescription(peer.Id, EmbeddedIceNetwork.Description(offer.Description, offer.PublicOnly));
                            transport.Gather(peer.Id);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        if (transport.IsDisposed) break;
                        if (Time.realtimeSinceStartupAsDouble >= nextWarning)
                        {
                            Debug.LogWarning("[P2P] Signaling temporarily unavailable: " + ex.GetType().Name);
                            nextWarning = Time.realtimeSinceStartupAsDouble + 15;
                        }
                    }
                    await Task.Delay(500, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { Debug.LogError("[P2P] Server signaling stopped: " + ex.GetType().Name); }
            finally
            {
                if (!transport.IsDisposed)
                    foreach (var peer in peers.Values) transport.RemovePeer(peer.Id);
            }
        }
    }
}
