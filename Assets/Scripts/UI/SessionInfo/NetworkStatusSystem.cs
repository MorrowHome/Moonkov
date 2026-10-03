using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct NetworkStatusSystem : ISystem
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        StatusEntity = Entity.Null;
    }
    
    public static Entity StatusEntity { get; private set; }
    
    private const string k_NotConnected = "<color=#ff5555>Not connected!</color>";
    private const string k_RedColor = "#ff5555";
    private const string k_OrangeColor = "#ffb86c";
    private const string k_GreenColor = "#50fa7b";
    private float m_FrameMs;

    public void OnCreate(ref SystemState state)
    {
        var entityManager = state.EntityManager;

        if (!SystemAPI.TryGetSingletonEntity<NetworkStatusSingleton>(out var entity))
        {
            entity = entityManager.CreateEntity(typeof(NetworkStatusSingleton));
            entityManager.SetComponentData(entity, new NetworkStatusSingleton { Status =  k_NotConnected });
        }

        StatusEntity = entity;
    }

    public void OnUpdate(ref SystemState state)
    {
        state.CompleteDependency();
        
        ref var statusSingleton = ref SystemAPI.GetComponentRW<NetworkStatusSingleton>(StatusEntity).ValueRW;
        statusSingleton.Status.Clear();

        if (!SystemAPI.TryGetSingleton<NetworkStreamConnection>(out var connection) ||
            !SystemAPI.TryGetSingleton<NetworkStreamDriver>(out var driver))
        {
            statusSingleton.Status = k_NotConnected;
            return;
        }

        var sb = new FixedString512Bytes();
        var pingColor = new FixedString32Bytes(k_OrangeColor);

        if (SystemAPI.TryGetSingleton<NetworkSnapshotAck>(out var ack) && connection.CurrentState == ConnectionState.State.Connected)
        {
            var pingEstimate = (int)ack.EstimatedRTT;
            var deviationRTT = (int)ack.DeviationRTT;

            if (ack.EstimatedRTT > 200)
                pingColor.CopyFrom(k_RedColor);
            else if (ack.EstimatedRTT <= 100)
                pingColor.CopyFrom(k_GreenColor);
            
            sb.Append("<color=");
            sb.Append(pingColor);
            sb.Append(">");
            
            sb.Append("Ping:");
            sb.Append(pingEstimate);
            sb.Append('±');
            sb.Append(deviationRTT);
            sb.Append("ms, ");
            // Presentation runs once per rendered frame. Do not use a prediction step's dt as FPS.
            m_FrameMs = math.lerp(m_FrameMs > 0f ? m_FrameMs : Time.unscaledDeltaTime * 1000f,
                Time.unscaledDeltaTime * 1000f, .1f);
            sb.Append("Frame:");
            sb.Append((int)m_FrameMs);
            sb.Append("ms, ");
            if (SystemAPI.TryGetSingleton<NetworkTime>(out var time) && time.ServerTick.IsValid && ack.LastReceivedSnapshotByLocal.IsValid)
            {
                sb.Append("Snapshot gap:");
                sb.Append(time.ServerTick.TicksSince(ack.LastReceivedSnapshotByLocal));
                sb.Append(" ticks, ");
            }
        }
        else
        {
            sb.Append("<color=");
            sb.Append(pingColor);
            sb.Append(">");
        }

        sb.Append(connection.CurrentState.ToFixedString());
        sb.Append(" @ ");
        sb.Append(driver.GetRemoteEndPoint(connection).ToFixedString());
        sb.Append("</color>");

        // Write the final string to our singleton component.
        statusSingleton.Status = sb;
    }
}
