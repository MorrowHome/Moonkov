// 每帧最先清掉上一帧留下的 IsHit。新的伤害仍可在这一帧立刻置位，让受击动画知道发生了新事件。
using Unity.Entities;

namespace Unity.MP_FPS {
    // 负责清除上一帧留下的 IsHit。
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class ResetHitFlagSystem : SystemBase {
        protected override void OnUpdate() {
            // 帧首把所有角色的 IsHit 清掉。
            // 让本帧新计算的命中能再次置位，
            // 从而不会把上一帧的受击误认成本帧事件。
            // 放在帧首清的是给表现系统看的瞬时标记，实际血量和上次命中的编号仍保留。
            foreach (var playerGhost in SystemAPI.Query<RefRW<PredictedPlayerGhost>>())
                if (playerGhost.ValueRO.ControllerState.IsHit)
                    playerGhost.ValueRW.ControllerState.IsHit = false;
        }
    }
}