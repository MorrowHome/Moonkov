using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using Unity.MP_FPS.EmbeddedNetworking;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Isolated EditMode transport check. No ECS worlds, game clients or backend requests.</summary>
[InitializeOnLoad]
public static class EmbeddedIceTransportChecks
{
    static Run s_Run;
    static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../LocalData/EmbeddedIce"));
    static EmbeddedIceTransportChecks()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        EditorApplication.quitting += Cancel;
    }

    [MenuItem("Tools/Moonkov/Check Embedded ICE Transport (Isolated)")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run only in idle EditMode.");
        if (s_Run != null) throw new InvalidOperationException("ICE check is already running.");
        var run = new Run();
        try { run.Begin(); s_Run = run; EditorApplication.update += Update; }
        catch { run.Dispose(); throw; }
    }
    static void Update()
    {
        try { if (s_Run.Tick()) Finish(null); }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static void Cancel() { if (s_Run != null) Finish("Cancelled before completion."); }
    static void Finish(string error)
    {
        EditorApplication.update -= Update;
        var run = s_Run;
        s_Run = null;
        try { run.Dispose(); }
        catch (Exception disposeError) { error = (error ?? "") + "\nDispose: " + disposeError; }
        var report = run.Report;
        report.success = error == null;
        report.error = error;
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "unity-transport-check.json"), JsonUtility.ToJson(report, true));
        if (report.success) Debug.Log($"[EmbeddedIceCheck] PASS: {report.checks.Count} assertions. Isolated ICE + DTLS only; no public P2P claim.");
        else Debug.LogError("[EmbeddedIceCheck] FAIL: " + error);
    }

    [Serializable]
    public class Result
    {
        public bool success;
        public string scope = "EditMode native ICE + UTP/NetCode pipelines + DTLS. No scene, backend, Player build or public network test.";
        public string error;
        public List<string> checks = new List<string>();
        public EmbeddedIceStatistics client, server;
        public EmbeddedIceStatistics rejectionClient, rejectionServer;
        public double maximumDriverUpdateMilliseconds;
        public int authenticationFailureReason;
        public int udpBaselineRejectionReason;
    }

    sealed class Run : IDisposable
    {
        public readonly Result Report = new Result();
        EmbeddedIceTransport m_ClientIce, m_ServerIce;
        NetworkDriver m_Client, m_Server;
        NetworkConnection m_Connection, m_Accepted;
        int m_ClientPeer, m_ServerPeer, m_Stage, m_ReceivedMask;
        bool m_RejectName, m_Sent, m_UdpBaseline;
        double m_Deadline;
        string m_Ca, m_Certificate, m_Key;

        public void Begin()
        {
            var folder = Path.Combine(Root, "check-certificate");
            m_Ca = File.ReadAllText(Path.Combine(folder, "ca.pem"));
            m_Certificate = File.ReadAllText(Path.Combine(folder, "server.pem"));
            m_Key = File.ReadAllText(Path.Combine(folder, "server-key.pem"));
            BeginPair();
        }
        void BeginPair()
        {
            if (!m_UdpBaseline)
            {
                m_ClientIce = new EmbeddedIceTransport();
                m_ServerIce = new EmbeddedIceTransport();
                m_ClientPeer = m_ClientIce.AddPeer("127.0.0.1");
                m_ServerPeer = m_ServerIce.AddPeer("127.0.0.1");
                m_ClientIce.Gather(m_ClientPeer);
                m_ServerIce.Gather(m_ServerPeer);
            }
            m_Stage = m_UdpBaseline ? 1 : 0;
            m_Deadline = EditorApplication.timeSinceStartup + 20;
            m_Connection = m_Accepted = default;
        }
        public bool Tick()
        {
            if (EditorApplication.timeSinceStartup > m_Deadline)
                throw new TimeoutException($"ICE check stage {m_Stage}, reject-name={m_RejectName}.");
            if (m_Stage == 0)
            {
                if (!m_ClientIce.IsGatheringComplete(m_ClientPeer) || !m_ServerIce.IsGatheringComplete(m_ServerPeer)) return false;
                m_ClientIce.SetRemoteDescription(m_ClientPeer, m_ServerIce.GetLocalDescription(m_ServerPeer));
                m_ServerIce.SetRemoteDescription(m_ServerPeer, m_ClientIce.GetLocalDescription(m_ClientPeer));
                m_Stage = 1;
            }
            if (m_Stage == 1)
            {
                if (!m_UdpBaseline && (!Connected(m_ClientIce.GetState(m_ClientPeer)) || !Connected(m_ServerIce.GetState(m_ServerPeer)))) return false;
                Require(true, m_UdpBaseline ? "ordinary UDP certificate baseline" : m_RejectName ? "ICE ready for negative certificate check" : "ICE connectivity established");
                CreateDrivers();
                m_Stage = 2;
            }
            long start = Stopwatch.GetTimestamp();
            m_Server.ScheduleUpdate().Complete();
            m_Client.ScheduleUpdate().Complete();
            Report.maximumDriverUpdateMilliseconds = Math.Max(Report.maximumDriverUpdateMilliseconds,
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            if (!m_Accepted.IsCreated) m_Accepted = m_Server.Accept();
            if (m_Accepted.IsCreated) PumpServer();
            NetworkEvent.Type type;
            while ((type = m_Client.PopEventForConnection(m_Connection, out var reader)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Connect)
                {
                    Require(!m_RejectName, "DTLS certificate name accepted only for correct server");
                    if (!m_Sent) { Send(1, 64); Send(2, 1200); m_Sent = true; }
                }
                else if (type == NetworkEvent.Type.Data)
                {
                    int length = reader.Length;
                    int id = reader.ReadByte();
                    Require(id == 1 && length == 64 || id == 2 && length == 1200, "echo datagram length");
                    for (int i = 1; i < length; i++)
                        if (reader.ReadByte() != Pattern(id, i)) throw new InvalidOperationException("Echo payload differs.");
                    Require(true, $"DTLS roundtrip payload {length} bytes");
                    m_ReceivedMask |= 1 << id;
                }
                else if (type == NetworkEvent.Type.Disconnect)
                {
                    int reason = reader.Length > 0 ? reader.ReadByte() : -1;
                    if (m_UdpBaseline)
                    {
                        Require(reason == (int)Unity.Networking.Transport.Error.DisconnectReason.AuthenticationFailure ||
                            reason == (int)Unity.Networking.Transport.Error.DisconnectReason.MaxConnectionAttempts,
                            $"ordinary UDP rejects wrong name (reason={reason})");
                        Require(!m_Accepted.IsCreated, "ordinary UDP never accepts wrong-name session");
                        Report.udpBaselineRejectionReason = reason;
                        DisposePair();
                        m_UdpBaseline = false;
                        BeginPair();
                        return false;
                    }
                    Report.authenticationFailureReason = reason;
                    Report.rejectionClient = m_ClientIce.GetStatistics();
                    Report.rejectionServer = m_ServerIce.GetStatistics();
                    Require(m_RejectName && reason == Report.udpBaselineRejectionReason,
                        $"wrong-name rejection matches ordinary UTP (reason={reason})");
                    Require(!m_Accepted.IsCreated, "unauthenticated connection never accepted by server");
                    Require(Connected(m_ClientIce.GetState(m_ClientPeer)) && Report.rejectionClient.ReceivedBytes > 0 &&
                        Report.rejectionServer.ReceivedBytes > 0 && Report.rejectionClient.SendErrors == 0 &&
                        Report.rejectionServer.SendErrors == 0, "rejected DTLS has a live bidirectional ICE path");
                    return true;
                }
            }
            if (!m_RejectName && m_ReceivedMask == 6)
            {
                Report.client = m_ClientIce.GetStatistics();
                Report.server = m_ServerIce.GetStatistics();
                Require(Report.client.DroppedPackets == 0 && Report.server.DroppedPackets == 0 &&
                    m_ClientIce.AdapterDrops == 0 && m_ServerIce.AdapterDrops == 0, "no drops in isolated positive path");
                var oldOwner = m_ClientIce;
                DisposePair();
                bool rejectedDisposed = false;
                try { oldOwner.GetStatistics(); } catch (ObjectDisposedException) { rejectedDisposed = true; }
                Require(rejectedDisposed, "driver disposal releases native owner");
                oldOwner.Dispose(); // Idempotent caller cleanup is safe after driver disposal.
                m_RejectName = true;
                m_UdpBaseline = true;
                BeginPair();
            }
            return false;
        }
        void CreateDrivers()
        {
            var clientSettings = Settings();
            var serverSettings = Settings();
            try
            {
                clientSettings.WithSecureClientParameters(caCertificate: m_Ca,
                    serverName: m_RejectName ? "wrong-name.invalid" : "moon-ice-check.invalid");
                serverSettings.WithSecureServerParameters(certificate: m_Certificate, privateKey: m_Key);
                // Same NetCode builder extension as EntityDriverConstructor, without ECS worlds.
                m_Client = m_UdpBaseline
                    ? DefaultDriverBuilder.CreateClientNetworkDriver(new UDPNetworkInterface(), clientSettings).driver
                    : DefaultDriverBuilder.CreateClientNetworkDriver(m_ClientIce.CreateNetworkInterface(), clientSettings).driver;
                m_Server = m_UdpBaseline
                    ? DefaultDriverBuilder.CreateServerNetworkDriver(new UDPNetworkInterface(), serverSettings).driver
                    : DefaultDriverBuilder.CreateServerNetworkDriver(m_ServerIce.CreateNetworkInterface(), serverSettings).driver;
            }
            finally { clientSettings.Dispose(); serverSettings.Dispose(); }
            var clientEndpoint = m_UdpBaseline ? NetworkEndpoint.LoopbackIpv4 : m_ClientIce.LocalEndpoint;
            var serverEndpoint = m_UdpBaseline ? NetworkEndpoint.LoopbackIpv4 : m_ServerIce.LocalEndpoint;
            Require(m_Client.Bind(clientEndpoint) == 0 && m_Server.Bind(serverEndpoint) == 0, "isolated endpoint bind");
            Require(m_Server.Listen() == 0, "UTP listen");
            if (!m_RejectName)
            {
                bool duplicateRejected = false;
                try { m_ClientIce.CreateNetworkInterface(); } catch (InvalidOperationException) { duplicateRejected = true; }
                Require(duplicateRejected, "duplicate driver ownership rejected");
            }
            m_Connection = m_Client.Connect(m_UdpBaseline ? m_Server.GetLocalEndpoint() : m_ClientIce.GetEndpoint(m_ClientPeer));
        }
        static NetworkSettings Settings()
        {
            var settings = new NetworkSettings(Allocator.Temp);
            settings.WithNetworkConfigParameters(connectTimeoutMS: 300, maxConnectAttempts: 20,
                disconnectTimeoutMS: 8000, sendQueueCapacity: 64, receiveQueueCapacity: 64);
            return settings;
        }
        void PumpServer()
        {
            NetworkEvent.Type type;
            while ((type = m_Server.PopEventForConnection(m_Accepted, out var reader)) != NetworkEvent.Type.Empty)
            {
                if (type != NetworkEvent.Type.Data) continue;
                Require(!m_RejectName, "server data requires authenticated DTLS");
                Require(m_Server.BeginSend(NetworkPipeline.Null, m_Accepted, out var writer) == 0, "echo BeginSend");
                for (int i = 0; i < reader.Length; i++) writer.WriteByte(reader.ReadByte());
                Require(m_Server.EndSend(writer) > 0, "echo EndSend");
            }
        }
        void Send(int id, int length)
        {
            Require(m_Client.BeginSend(NetworkPipeline.Null, m_Connection, out var writer) == 0, "client BeginSend");
            writer.WriteByte((byte)id);
            for (int i = 1; i < length; i++) writer.WriteByte(Pattern(id, i));
            Require(m_Client.EndSend(writer) > 0, "client EndSend");
        }
        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Report.checks.Add(name);
        }
        static bool Connected(EmbeddedIceState state) => state == EmbeddedIceState.Connected || state == EmbeddedIceState.Completed;
        static byte Pattern(int id, int index) => (byte)(id * 13 + index * 31);
        void DisposePair()
        {
            try { if (m_Client.IsCreated) m_Client.Dispose(); }
            finally
            {
                try { if (m_Server.IsCreated) m_Server.Dispose(); }
                finally
                {
                    m_Client = m_Server = default;
                    m_ClientIce?.Dispose(); m_ClientIce = null;
                    m_ServerIce?.Dispose(); m_ServerIce = null;
                }
            }
        }
        public void Dispose() { DisposePair(); m_Key = null; }
    }
}
