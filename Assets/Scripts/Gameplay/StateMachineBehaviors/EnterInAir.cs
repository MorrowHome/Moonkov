// Animator 进入腾空状态时打开 IsInAir，供后续动画过渡使用。落地动画退出时由 ExitLanding 清掉。
using UnityEngine;

namespace Unity.MP_FPS {
    public class EnterInAir : StateMachineBehaviour {
        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) {
            // 动画进入腾空段时打开标记，让动画过渡继续按空中姿势处理。
            animator.SetBool("IsInAir", true);
        }
    }
}