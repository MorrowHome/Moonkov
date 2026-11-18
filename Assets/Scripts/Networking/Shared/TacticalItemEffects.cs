// 把战术状态写进这一帧的移动参数。冲刺疲劳降低速度，触发绊线则进一步降低移动和跳跃，让预测与服务端能用同样的控制参数。
namespace Unity.MP_FPS {
    public static class TacticalItemEffects {
        public static FirstPersonController.ControllerConsts Movement(
            in PredictedPlayerGhost state,
            FirstPersonController.ControllerConsts constants) {
            // 疲劳只改变这一帧控制器收到的速度参数。仍保留比走路略快的冲刺，恢复之后自然重新读基础速度。
            if (state.TacticalSprintExhausted)
                constants.Sprint.Speed =
                    UnityEngine.Mathf.Max(constants.Walk.Speed * 1.05f, constants.Sprint.Speed * .8f);
            // 绊线是进一步限制，降低走路与跳跃，冲刺上限压到走路。持续时长由服务器的战术状态自行推进。
            if (state.TacticalWireSeconds > 0) {
                constants.Walk.Speed *= .35f;
                constants.Sprint.Speed = constants.Walk.Speed;
                constants.JumpHeight *= .35f;
            }
            return constants;
        }
    }
}