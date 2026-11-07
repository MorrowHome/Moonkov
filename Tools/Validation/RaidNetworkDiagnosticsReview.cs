using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.MP_FPS.EmbeddedNetworking;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

public static class RaidNetworkDiagnosticsReview
{
    const BindingFlags Hidden=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    public static object Run()
    {
        if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
        var oldGame=GameManager.GameConnection;
        using var world=new World("P2P diagnostic sampling, no sockets",WorldFlags.GameClient);
        // Allocates an empty native queue only. No peers, sockets, STUN or driver.
        using var transport=new EmbeddedIceTransport();
        var session=(EmbeddedIceClientSession)Activator.CreateInstance(typeof(EmbeddedIceClientSession),Hidden,null,new object[]{"diagnostic-fixture"},null);
        var game=new GameConnection();
        var system=world.CreateSystemManaged<RaidNetworkDiagnosticsSystem>();var em=world.EntityManager;
        var connection=em.CreateEntity(typeof(NetworkStreamConnection),typeof(NetworkSnapshotAck),typeof(NetworkId));
        em.SetComponentData(connection,new NetworkId{Value=7});
        em.SetComponentData(connection,new NetworkStreamConnection{CurrentState=ConnectionState.State.Connected});
        em.SetComponentData(connection,new NetworkSnapshotAck{EstimatedRTT=1800,DeviationRTT=350,ServerCommandAge=896});
        em.AddBuffer<OutgoingRpcDataStreamBuffer>(connection).ResizeUninitialized(32768);
        var messages=new List<string>();int checks=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;}
        void Set(string name,object value)=>typeof(RaidNetworkDiagnosticsSystem).GetField(name,Hidden).SetValue(system,value);
        void Step(bool due=false){Set("m_LastFrame",-1);if(due)Set("m_NextSample",0d);system.Update();}
        void Log(string message,string stack,LogType type){if(message.StartsWith("[NetDiag]",StringComparison.Ordinal))messages.Add(message);}
        Application.logMessageReceived+=Log;
        try
        {
            typeof(EmbeddedIceClientSession).GetProperty("Transport",Hidden).SetValue(session,transport);
            typeof(GameConnection).GetProperty("IceSession",Hidden).SetValue(game,session);
            typeof(GameManager).GetProperty("GameConnection",Hidden).SetValue(null,game);
            Step();
            Check(messages.Count==1 && messages[0].Contains("baseline=True"),"First connection reports a baseline");
            Check(messages[0].Contains("rtt_ms=1800") && messages[0].Contains("jitter_ms=350"),"Measured RTT and deviation are recorded");
            Check(messages[0].Contains("rpc_queue_bytes=32768") && messages[0].Contains("command_age_ticks=3.50"),"Real ECS queue and command age are recorded");
            for(int i=0;i<20;i++)Step();
            Check(messages.Count==1,"Rapid updates do not spam logs");
            double now=Time.realtimeSinceStartupAsDouble;
            Set("m_Start",now-10);Set("m_LastUpdate",now-.75);Set("m_MaxGap",.75);Set("m_Frames",299);
            Step(true);
            Check(messages.Count==2 && messages[1].Contains("baseline=False") && messages[1].Contains("loop_fps=30.0"),"Window counts actual process updates separately from RTT");
            Check(messages[1].Contains("max_loop_gap_ms=750"),"An injected process stall remains visible in the sample");
            em.SetComponentData(connection,new NetworkSnapshotAck{EstimatedRTT=45});em.GetBuffer<OutgoingRpcDataStreamBuffer>(connection).Clear();
            Step(true);
            Check(messages.Count==3 && messages[2].Contains("rtt_ms=45") && messages[2].Contains("rpc_queue_bytes=0"),"Recovery records fresh values");
            Check(messages.TrueForAll(x=>x.Length<800 && !x.Contains("diagnostic-fixture")),"Logs remain bounded and omit session identifiers");
            em.SetComponentData(connection,new NetworkStreamConnection{CurrentState=ConnectionState.State.Disconnected});Step(true);
            Check(messages.Count==3,"Disconnected peers do not produce connected samples");
            em.RemoveComponent<NetworkStreamConnection>(connection);em.DestroyEntity(connection);Step(true);
            Check(typeof(RaidNetworkDiagnosticsSystem).GetField("m_Transport",Hidden).GetValue(system)==null,"Stopping clears the old transport baseline");
            Check((ulong)typeof(RaidNetworkDiagnosticsSystem).GetMethod("Delta",Hidden).Invoke(null,new object[]{5UL,ulong.MaxValue})==5UL,"Reset counters cannot underflow into huge rates");
        }
        finally
        {
            Application.logMessageReceived-=Log;
            typeof(GameManager).GetProperty("GameConnection",Hidden).SetValue(null,oldGame);
            session.Dispose();
        }
        return new {checks,records=messages.Count,scope="Actual ECS logger and empty native queue; injected metrics, no peers, sockets, Play or multiplayer."};
    }
}
