using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class TacticalItemsClientSystem : SystemBase
    {
        private TacticalItemsFeedback m_Feedback;
        private bool m_OwnsPresentation;
        protected override void OnCreate() => EntityManager.CreateSingleton<TacticalItemClientState>();
        private bool OwnsPresentation()
        {
            // Match RaidHUD's first full client World. Additional in-process transport
            // peers retain their own owner component and cannot overwrite the viewed HUD.
            foreach(var world in Unity.Entities.World.All)
                if(world.IsCreated && world.IsClient() && !world.IsThinClient())return world==World;
            return true;
        }
        protected override void OnUpdate()
        {
            m_OwnsPresentation=OwnsPresentation();
            if(m_OwnsPresentation && !m_Feedback)m_Feedback=TacticalItemsFeedback.GetOrCreate();
            var ecb=new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach(var (rpc,received,entity) in SystemAPI.Query<RefRO<TacticalItemOwnerRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                var owner=rpc.ValueRO;
                var current=SystemAPI.ManagedAPI.GetSingleton<TacticalItemClientState>();
                if(current.Apply(owner) && m_OwnsPresentation)TacticalItemsFeedback.SetOwner(owner);
                ecb.DestroyEntity(entity);
            }
            foreach(var (rpc,received,entity) in SystemAPI.Query<RefRO<TacticalWorldStateRpc>,RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
            {
                if(m_OwnsPresentation)
                {if(!m_Feedback)m_Feedback=TacticalItemsFeedback.GetOrCreate();m_Feedback.Present(rpc.ValueRO);}
                ecb.DestroyEntity(entity);
            }
            if(m_Feedback)
                foreach(var (health,link,entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>,GhostGameObjectLink>().WithEntityAccess())
                    if(health.ValueRO.TacticalCoughSeconds>0 && link.LinkedInstance)
                        m_Feedback.PresentCough(entity.Index,link.LinkedInstance.transform.position+Vector3.up);
            ecb.Playback(EntityManager);ecb.Dispose();
        }
        protected override void OnDestroy()
        {if(m_Feedback){if(Application.isPlaying)Object.Destroy(m_Feedback.gameObject);else Object.DestroyImmediate(m_Feedback.gameObject);}if(m_OwnsPresentation)TacticalItemsFeedback.SetOwner(default);}
    }
}
