// 接收能量手雷的服务端状态，按客户端插值 tick 估算消息已经过去多久，再让 EnergyGrenadeFeedback 从合适的时间点播放表现。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class EnergyGrenadeClientSystem : SystemBase {
        private EnergyGrenadeFeedback m_Feedback;

        protected override void OnUpdate() {
            var ecb = new EntityCommandBuffer(Collections.Allocator.Temp);
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<EnergyGrenadeStateRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                if (!m_Feedback)
                    m_Feedback = new GameObject("Network energy grenade feedback")
                        .AddComponent<EnergyGrenadeFeedback>();
                float age = 0;
                if (SystemAPI.TryGetSingleton<NetworkTime>(out var time) && time.InterpolationTick.IsValid) {
                    var rates = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var configured)
                        ? configured
                        : default;
                    rates.ResolveDefaults();
                    int ticks = unchecked((int)(time.InterpolationTick.TickIndexForValidTick - rpc.ValueRO.Tick));
                    age = Mathf.Max(0, ticks / (float)rates.SimulationTickRate);
                }
                m_Feedback.Present(rpc.ValueRO, age);
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        protected override void OnDestroy() {
            if (m_Feedback) Object.Destroy(m_Feedback.gameObject);
        }
    }
}