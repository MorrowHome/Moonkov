// 每帧最先清掉上一帧留下的 IsHit。新的伤害仍可在这一帧立刻置位，让受击动画知道发生了新事件。
using Unity.Entities;

namespace Unity.MP_FPS {
    // Its only job is to clean up the IsHit flag from the previous frame.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class ResetHitFlagSystem : SystemBase {
        protected override void OnUpdate() {
            // Reset IsHit flag for all players at the start of the frame.
            // This ensures any hit from the PREVIOUS frame is cleared before
            // any new hits from the CURRENT frame are processed.
            // 放在帧首清的是给表现系统看的瞬时标记，实际血量和上次命中的编号仍保留。
            foreach (var playerGhost in SystemAPI.Query<RefRW<PredictedPlayerGhost>>())
                if (playerGhost.ValueRO.ControllerState.IsHit)
                    playerGhost.ValueRW.ControllerState.IsHit = false;
        }
    }
}