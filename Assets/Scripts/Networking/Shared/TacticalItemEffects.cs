// 把战术状态写进这一帧的移动参数。冲刺疲劳降低速度，触发绊线则进一步降低移动和跳跃，让预测与服务端能用同样的控制参数。
namespace Unity.MP_FPS {
    public static class TacticalItemEffects {
        public static FirstPersonController.ControllerConsts Movement(
            in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants) {
            if (state.TacticalSprintExhausted)
                constants.Sprint.Speed =
                    UnityEngine.Mathf.Max(constants.Walk.Speed * 1.05f, constants.Sprint.Speed * .8f);
            if (state.TacticalWireSeconds > 0) {
                constants.Walk.Speed *= .35f;
                constants.Sprint.Speed = constants.Walk.Speed;
                constants.JumpHeight *= .35f;
            }
            return constants;
        }
    }
}