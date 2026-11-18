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
                // 首次收到有效类型的网络消息时才创建表现对象，没用到手雷的客户端不需要额外挂一个常驻对象。
                if (!m_Feedback)
                    m_Feedback = new GameObject("Network energy grenade feedback")
                        .AddComponent<EnergyGrenadeFeedback>();
                float age = 0;
                if (SystemAPI.TryGetSingleton<NetworkTime>(out var time) && time.InterpolationTick.IsValid) {
                    var rates = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var configured)
                        ? configured
                        : default;
                    rates.ResolveDefaults();
                    // 用插值显示走到的 tick 减去消息的事件 tick。除以模拟频率就能估算开播偏移，插值仍在事件之前时以零为下限。
                    int ticks = unchecked((int)(time.InterpolationTick.TickIndexForValidTick - rpc.ValueRO.Tick));
                    age = Mathf.Max(0, ticks / (float)rates.SimulationTickRate);
                }
                // 服务端状态已决定手雷在哪和何时变化。这里只把相应表现补到当前显示时刻，然后删除临时 RPC 实体。
                m_Feedback.Present(rpc.ValueRO, age);
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        // World 销毁时表现对象也一起清掉，回到主菜单或再开一局时不会继续播放上一局的手雷效果。
        protected override void OnDestroy() {
            if (m_Feedback) Object.Destroy(m_Feedback.gameObject);
        }
    }
}