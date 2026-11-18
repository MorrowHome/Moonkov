// 接收战术物品的个人状态与全场事件。每个客户端 World 都有自己的数据，屏幕上的 TacticalItemsFeedback 由实际显示 HUD 的 World 更新。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS {
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class TacticalItemsClientSystem : SystemBase {
        private TacticalItemsFeedback m_Feedback;
        private bool m_OwnsPresentation;

        protected override void OnCreate() {
            // 托管组件必须显式建立实例，否则泛型默认单例只会保存 null。
            var owner = EntityManager.CreateEntity();
            // 用托管对象保存当前客户端的战术道具状态。每个 World 自己创建一份，局内 HUD 通过查询拿到它。
            EntityManager.AddComponentObject(owner, new TacticalItemClientState());
        }

        private bool OwnsPresentation() {
            // 和 RaidHUD 一样选择第一个完整的客户端 World。同进程的其他传输对端
            // 仍使用它们各自的状态组件，不能改正在显示的 HUD。
            foreach (var world in World.All)
                if (world.IsCreated && world.IsClient() && !world.IsThinClient())
                    return world == World;
            return true;
        }

        protected override void OnUpdate() {
            m_OwnsPresentation = OwnsPresentation();
            if (m_OwnsPresentation && !m_Feedback) m_Feedback = TacticalItemsFeedback.GetOrCreate();
            using var ecb = new EntityCommandBuffer(Collections.Allocator.Temp);
            // 本人回执先更新这个 World 的进度和操作结果，只有绑定当前 HUD 的那个 World 还要更新屏幕表现。
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<TacticalItemOwnerRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                var owner = rpc.ValueRO;
                var current = SystemAPI.ManagedAPI.GetSingleton<TacticalItemClientState>();
                if (current.Apply(owner) && m_OwnsPresentation) TacticalItemsFeedback.SetOwner(owner);
                ecb.DestroyEntity(entity);
            }
            // 场景状态消息包含客户端需要绘制的物体和效果。附加的 World 即使收到同类消息也不能重复放一次特效。
            foreach (var (rpc, received, entity) in SystemAPI
                         .Query<RefRO<TacticalWorldStateRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess()) {
                if (m_OwnsPresentation) {
                    if (!m_Feedback) m_Feedback = TacticalItemsFeedback.GetOrCreate();
                    m_Feedback.Present(rpc.ValueRO);
                }
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(EntityManager);
        }

        // 随 World 清理画面对象。只有刚才负责 HUD 的那个 World 才清全局持有者，防止一并关闭其他 World 时误擦主画面。
        protected override void OnDestroy() {
            if (m_Feedback) {
                if (Application.isPlaying)
                    Object.Destroy(m_Feedback.gameObject);
                else
                    Object.DestroyImmediate(m_Feedback.gameObject);
            }
            if (m_OwnsPresentation) TacticalItemsFeedback.SetOwner(default);
        }
    }
}